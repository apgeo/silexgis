// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Matching a read sheet against the trip it is being imported into: who each written name is,
/// which station each place is, and which rows the log already holds.
/// </summary>
public class TrackingCsvPlannerTests
{
    private static readonly Guid Ion = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Maria = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Mihai = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid TeamOne = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    /// <summary>
    /// A model with an entrance and three stations below it, 50 m apart. Invented, and simple
    /// enough that which station a depth is nearest to is obvious by reading.
    /// </summary>
    private static IReadOnlyCollection<TrackingDepthResolver.Station> Stations { get; } =
    [
        new("0", "0", "entrance", 1000, true),
        new("1", "1", "main", 950, false),
        new("2", "2", "main", 900, false),
        new("3", "3", "main", 850, false),
    ];

    private static TrackingCsvSubject Subject(
        IEnumerable<DeclaredDepthPlaces.Declared>? declarations = null,
        IEnumerable<(Guid, DateTimeOffset)>? existing = null) => new()
        {
            Roster = [(Ion, "Ion Popescu"), (Maria, "Maria Pop"), (Mihai, "Mihai Ionescu")],
            Teams = [(TeamOne, "Echipa 1")],
            Stations = Stations,
            HasModel = true,
            Format = SurveyModelFormat.Survex3d,
            Declarations = [.. declarations ?? []],
            Existing = new HashSet<(Guid, DateTimeOffset)>(existing ?? []),
        };

    private static TrackingCsvPlan PlanOf(string rows, TrackingCsvSubject? subject = null)
    {
        var parsed = TrackingCsvParser.Parse(
            "Data si ora,Adancime,Statie,Loc,Speologi,Echipa,Nota,Stare\r\n" + rows + "\r\n");
        parsed.Readable.ShouldBeTrue();
        return TrackingCsvPlanner.Plan(parsed.Rows, subject ?? Subject());
    }

    [Fact]
    public void One_row_about_a_party_becomes_one_report_for_each_person_on_it()
    {
        var plan = PlanOf("12.09.2026 09:00,100,,,\"Ion Popescu; Maria Pop\",Echipa 1,,");

        plan.Reports.Count.ShouldBe(2);
        plan.Reports.Select(r => r.CaverId).ShouldBe([Ion, Maria]);
        plan.Reports.ShouldAllBe(r => r.At == new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));
        plan.Reports.ShouldAllBe(r => r.TeamId == TeamOne);
        plan.Refused.ShouldBeEmpty();
    }

    [Fact]
    public void A_name_is_matched_by_the_most_of_it_that_the_roster_answers_to()
    {
        var plan = PlanOf(
            "12.09.2026 09:00,100,,,Ion Popescu,,,\r\n"
            + "12.09.2026 10:00,100,,,Ion P.,,,\r\n"
            + "12.09.2026 11:00,100,,,Ion,,,");

        plan.Reports.Select(r => r.CaverId).ShouldAllBe(id => id == Ion);
        plan.Reports[0].MatchedBy.ShouldBe(CaverNameLadder.Rung.FullName);
        plan.Reports[1].MatchedBy.ShouldBe(CaverNameLadder.Rung.GivenNameAndInitial);
        plan.Reports[2].MatchedBy.ShouldBe(CaverNameLadder.Rung.GivenName);
    }

    [Fact]
    public void A_name_nobody_answers_to_costs_that_persons_report_and_not_the_row()
    {
        // A party of two with one unknown name is one report and a question, not nothing.
        var plan = PlanOf("12.09.2026 09:00,100,,,\"Ion Popescu; Gheorghe\",,,");

        plan.Reports.Count.ShouldBe(1);
        plan.Reports[0].CaverId.ShouldBe(Ion);
        plan.Refused.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.CaverNotOnRoster && d.Detail == "Gheorghe");
    }

    [Fact]
    public void An_unmatched_name_is_listed_once_however_many_rows_wrote_it()
    {
        // The list a reviewer acts on: a missing participant is one thing to fix, and a refusal
        // per row buries it under its own repetitions.
        var plan = PlanOf(
            "12.09.2026 09:00,100,,,Gheorghe,,,\r\n"
            + "12.09.2026 10:00,100,,,Gheorghe,,,\r\n"
            + "12.09.2026 11:00,100,,,Gheorghe,,,");

        plan.UnmatchedCavers.ShouldBe(["Gheorghe"]);
        plan.Refused.Count(d => d.Problem == TrackingCsvProblem.CaverNotOnRoster).ShouldBe(3);
    }

    [Fact]
    public void A_name_two_people_answer_to_is_refused_rather_than_settled_by_taking_the_first()
    {
        var subject = Subject() with
        {
            Roster = [(Ion, "Ion Popescu"), (Mihai, "Ion Popa")],
        };

        var plan = PlanOf("12.09.2026 09:00,100,,,Ion,,,", subject);

        plan.Reports.ShouldBeEmpty();
        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.CaverAmbiguous);
    }

    [Fact]
    public void A_team_nobody_recognises_is_dropped_and_the_report_still_stands()
    {
        // A team is indicative: losing it costs a label, and refusing the row costs the position.
        var plan = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,Echipa 9,,");

        plan.Reports.Count.ShouldBe(1);
        plan.Reports[0].TeamId.ShouldBeNull();
        plan.Reports[0].Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.TeamNotOnTrip
            && d.Severity == TrackingCsvSeverity.Warning);
    }

    [Fact]
    public void A_station_named_outright_is_resolved_against_the_model()
    {
        var plan = PlanOf("12.09.2026 09:00,,2,,Ion Popescu,,,");

        plan.Reports[0].Kind.ShouldBe(TripPositionEventKind.AtStation);
        plan.Reports[0].ViewerStationName.ShouldBe("2");
        plan.Reports[0].DepthM.ShouldBeNull();
    }

    [Fact]
    public void A_station_the_model_does_not_have_is_refused()
    {
        var plan = PlanOf("12.09.2026 09:00,,99,,Ion Popescu,,,");

        plan.Reports.ShouldBeEmpty();
        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.StationNotInModel);
    }

    [Fact]
    public void A_depth_nobody_declared_lands_on_the_nearest_station()
    {
        var plan = PlanOf("12.09.2026 09:00,105,,,Ion Popescu,,,");

        // 100 m below the entrance is station 2; 105 is nearer it than station 3 at 150.
        plan.Reports[0].Kind.ShouldBe(TripPositionEventKind.AtDepth);
        plan.Reports[0].ViewerStationName.ShouldBe("2");
        plan.Reports[0].DepthM.ShouldBe(105m);
    }

    [Fact]
    public void What_the_cave_declared_a_depth_to_be_beats_what_measuring_it_would_find()
    {
        // The club knows which of several stations near one another is the place people mean by
        // that number; the geometry does not.
        var subject = Subject([new(105m, "3", "Sala Mare")]);

        var plan = PlanOf("12.09.2026 09:00,105,,,Ion Popescu,,,", subject);

        plan.Reports[0].ViewerStationName.ShouldBe("3");
        plan.Reports[0].DepthM.ShouldBe(105m);
    }

    [Fact]
    public void A_declaration_naming_a_station_the_model_no_longer_has_is_passed_over_not_honoured()
    {
        // A station name nothing resolves is a marker that silently never appears, which is worse
        // than a station a metre off: measuring answers instead.
        var subject = Subject([new(105m, "99", "Sala Mare")]);

        var plan = PlanOf("12.09.2026 09:00,105,,,Ion Popescu,,,", subject);

        plan.Reports[0].ViewerStationName.ShouldBe("2");
    }

    [Fact]
    public void A_place_the_cave_named_is_recorded_as_both_its_station_and_its_depth()
    {
        var subject = Subject([new(96m, "2", "Meandru")]);

        var plan = PlanOf("12.09.2026 09:00,,,Meandru,Ion Popescu,,,", subject);

        plan.Reports[0].Kind.ShouldBe(TripPositionEventKind.AtDepth);
        plan.Reports[0].ViewerStationName.ShouldBe("2");
        plan.Reports[0].DepthM.ShouldBe(96m);
    }

    [Fact]
    public void A_place_name_the_cave_has_not_declared_is_told_apart_from_one_it_declared_twice()
    {
        var unknown = PlanOf(
            "12.09.2026 09:00,,,Meandru,Ion Popescu,,,",
            Subject([new(96m, "2", "Sala Mare")]));
        unknown.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.PlaceLabelUnknown);

        // Two declarations sharing a label is something to fix in the cave; a misspelled label is
        // something to fix in the sheet, and the reviewer is told which they have.
        var twice = PlanOf(
            "12.09.2026 09:00,,,Meandru,Ion Popescu,,,",
            Subject([new(96m, "2", "Meandru"), new(150m, "3", "meandru")]));
        twice.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.PlaceLabelAmbiguous);
    }

    [Fact]
    public void Going_in_and_coming_out_need_no_model_at_all()
    {
        // Which is what keeps a sheet's entry and exit rows importable into a watch whose survey
        // has gone missing.
        var subject = Subject() with { HasModel = false, Stations = [] };

        var plan = PlanOf(
            "12.09.2026 08:00,0,,,Ion Popescu,,,intrare\r\n"
            + "12.09.2026 16:00,0,,,Ion Popescu,,,iesire", subject);

        plan.Reports.Select(r => r.Kind)
            .ShouldBe([TripPositionEventKind.Entered, TripPositionEventKind.Exited]);
        plan.Reports.ShouldAllBe(r => r.ViewerStationName == null && r.DepthM == null);
        plan.Refused.ShouldBeEmpty();
    }

    [Fact]
    public void A_place_claimed_against_a_watch_with_no_survey_is_refused_with_that_reason()
    {
        var subject = Subject() with { HasModel = false, Stations = [] };

        var plan = PlanOf("12.09.2026 09:00,105,,,Ion Popescu,,,", subject);

        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.ModelMissing);
    }

    [Fact]
    public void A_report_the_log_already_holds_is_marked_as_replacing_it_rather_than_adding_one()
    {
        // Person and instant are the key an import upserts on, which is what makes re-importing a
        // corrected sheet safe: the second run changes the same rows instead of doubling them.
        var at = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var subject = Subject(existing: [(Ion, at)]);

        var plan = PlanOf(
            "12.09.2026 09:00,105,,,\"Ion Popescu; Maria Pop\",,,", subject);

        plan.Reports.Single(r => r.CaverId == Ion).Replaces.ShouldBeTrue();
        plan.Reports.Single(r => r.CaverId == Maria).Replaces.ShouldBeFalse();
        plan.Replaces.ShouldBe(1);
        plan.Creates.ShouldBe(1);
    }

    [Fact]
    public void Two_rows_that_are_the_same_report_are_reported_rather_than_written_over_each_other()
    {
        var plan = PlanOf(
            "12.09.2026 09:00,105,,,Ion Popescu,,prima,\r\n"
            + "12.09.2026 09:00,150,,,Ion Popescu,,a doua,");

        plan.Reports.Count.ShouldBe(2);
        plan.Reports[1].Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.DuplicateInFile && d.Detail == "2");
    }

    [Fact]
    public void A_row_the_reading_already_refused_carries_its_reasons_through()
    {
        var plan = PlanOf("12.09.2026,105,,,Ion Popescu,,,");

        plan.Reports.ShouldBeEmpty();
        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentWithoutTime);
    }
}
