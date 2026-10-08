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
        return TrackingCsvPlanner.Plan(parsed, subject ?? Subject());
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

    /// <summary>
    /// Nothing stops two teams of one trip having the same title. A cell that fits both is not a
    /// team the trip lacks: the row is planned as saying nothing about the team — no team on it,
    /// the report's own to be kept — and is told so in words of its own, where a name no team has
    /// is still planned as "no team".
    /// </summary>
    [Fact]
    public void A_team_name_two_of_the_trips_teams_share_is_not_read_as_no_team()
    {
        var teamAgain = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        var subject = Subject() with
        {
            // The second differs only in its capitals: a name is matched folded, so it is the same name.
            Teams = [(TeamOne, "Echipa 1"), (teamAgain, "ECHIPA 1"), (Guid.NewGuid(), "Echipa 2")],
        };

        var shared = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,Echipa 1,,", subject).Reports.ShouldHaveSingleItem();
        shared.TeamId.ShouldBeNull();
        shared.KeepsStoredTeam.ShouldBeTrue();
        shared.Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.TeamAmbiguous
            && d.Severity == TrackingCsvSeverity.Warning
            && d.Detail == "Echipa 1");
        shared.Diagnostics.ShouldNotContain(d => d.Problem == TrackingCsvProblem.TeamNotOnTrip);

        // The three other things a team cell can be are planned as they always were.
        var lacking = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,Echipa 9,,", subject).Reports.ShouldHaveSingleItem();
        lacking.TeamId.ShouldBeNull();
        lacking.KeepsStoredTeam.ShouldBeFalse();
        lacking.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.TeamNotOnTrip);

        var empty = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,,,", subject).Reports.ShouldHaveSingleItem();
        empty.TeamId.ShouldBeNull();
        empty.KeepsStoredTeam.ShouldBeFalse();
        empty.Diagnostics.ShouldBeEmpty();

        var one = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,Echipa 2,,", subject).Reports.ShouldHaveSingleItem();
        one.TeamId.ShouldNotBeNull();
        one.KeepsStoredTeam.ShouldBeFalse();
        one.Diagnostics.ShouldBeEmpty();
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

    /// <summary>
    /// The name the sheet wrote travels with the station it became, and only where it was what
    /// decided the station.
    /// </summary>
    [Fact]
    public void The_place_as_the_sheet_named_it_is_kept_beside_the_station_only_where_it_decided_it()
    {
        var subject = Subject([new(96m, "2", "Meandru")]);

        // Written in the sheet's own case, which is what a reviewer recognises as theirs.
        var named = PlanOf("12.09.2026 09:00,,,meandru,Ion Popescu,,,", subject);
        named.Reports[0].ViewerStationName.ShouldBe("2");
        named.Reports[0].PlaceLabel.ShouldBe("meandru");

        // A depth that lands on the same declared station was not a name the sheet wrote.
        var byDepth = PlanOf("12.09.2026 09:00,96,,,Ion Popescu,,,", subject);
        byDepth.Reports[0].ViewerStationName.ShouldBe("2");
        byDepth.Reports[0].PlaceLabel.ShouldBeNull();

        // A station named outright wins over the place beside it, so the label decided nothing.
        var byStation = PlanOf("12.09.2026 09:00,,3,Meandru,Ion Popescu,,,", subject);
        byStation.Reports[0].ViewerStationName.ShouldBe("3");
        byStation.Reports[0].PlaceLabel.ShouldBeNull();

        // Coming out claims no station, so a place on that row is not shown as resolving to nothing.
        var out_ = PlanOf("12.09.2026 09:00,,,Meandru,Ion Popescu,,,iesire", subject);
        out_.Reports[0].Kind.ShouldBe(TripPositionEventKind.Exited);
        out_.Reports[0].ViewerStationName.ShouldBeNull();
        out_.Reports[0].PlaceLabel.ShouldBeNull();
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
    public void A_report_the_log_holds_several_times_is_refused_rather_than_resolved_onto_one_of_them()
    {
        // Nothing keeps a log from holding two reports about one person at one instant — a typed
        // "entered" and a typed note both filed at 09:00 is an ordinary write-up. The sheet's row is
        // one report, and which of the two it corrects is not the importer's to guess: refused on
        // the row, in the preview, so the reviewer settles it in the log before anything is written.
        var at = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var subject = Subject(existing: [(Ion, at)]) with
        {
            ExistingSeveralTimes = new HashSet<(Guid, DateTimeOffset)> { (Ion, at) },
        };

        var plan = PlanOf(
            "12.09.2026 09:00,105,,,\"Ion Popescu; Maria Pop\",,,", subject);

        // Maria's report on the same row still stands: the refusal costs the person, not the row.
        plan.Reports.Single().CaverId.ShouldBe(Maria);
        var refused = plan.Refused.ShouldHaveSingleItem();
        refused.Problem.ShouldBe(TrackingCsvProblem.AlreadyRecordedSeveralTimes);
        refused.Severity.ShouldBe(TrackingCsvSeverity.Error);
        refused.Line.ShouldBe(2);
        refused.Detail.ShouldBe("Ion Popescu");
    }

    [Fact]
    public void A_person_the_roster_lists_twice_is_one_person_and_not_an_ambiguity()
    {
        // A trip's roster is one row per person per job, so the leader who also proposed the trip
        // arrives twice under one key. That is the person a sheet is most likely to name, and an
        // importer that read the repeat as two candidates refused them with a message naming them
        // against themselves.
        var subject = Subject() with
        {
            Roster = [(Ion, "Ion Popescu"), (Ion, "Ion Popescu"), (Maria, "Maria Pop")],
        };

        var plan = PlanOf("12.09.2026 09:00,105,,,Ion Popescu,,,", subject);

        plan.Refused.ShouldBeEmpty();
        plan.Reports.Single().CaverId.ShouldBe(Ion);
    }

    [Fact]
    public void Two_rows_that_are_the_same_report_become_one_report_the_last_row_wins_and_both_are_told()
    {
        // Decided in the plan, because the preview and the commit have to agree: a plan that
        // counted both rows as creates previewed two reports, and the commit then wrote the first
        // and refused the second as already recorded — by a row the log never held, and with no
        // overwrite box offered because the preview said nothing would be replaced.
        var plan = PlanOf(
            "12.09.2026 09:00,105,,,Ion Popescu,,prima,\r\n"
            + "12.09.2026 09:00,150,,,Ion Popescu,,a doua,");

        plan.Reports.Count.ShouldBe(1);
        plan.Creates.ShouldBe(1);
        plan.Reports[0].Line.ShouldBe(3);
        plan.Reports[0].Note.ShouldBe("a doua");
        plan.Reports[0].Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.DuplicateInFile && d.Detail == "2");
        plan.Refused.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.DuplicateInFile && d.Line == 2 && d.Detail == "3"
            && d.Severity == TrackingCsvSeverity.Warning);
    }

    [Fact]
    public void A_report_about_the_future_is_refused_in_the_plan_so_the_preview_says_so()
    {
        // Measured against the clock the subject carries, not one read here, so the preview and
        // the commit refuse exactly the same rows.
        var subject = Subject() with { Now = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero) };
        var plan = PlanOf(
            "12.09.2026 09:00,100,,,Ion Popescu,,,\r\n"
            + "12.09.2026 10:01,100,,,Ion Popescu,,,\r\n"
            + "14.09.2026 09:00,100,,,Ion Popescu,,,",
            subject);

        // Inside the allowed clock skew is not the future.
        plan.Reports.Select(r => r.Line).ShouldBe([2, 3]);
        plan.Refused.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.MomentInFuture && d.Line == 4
            && d.Severity == TrackingCsvSeverity.Error);
    }

    [Fact]
    public void A_row_the_reading_already_refused_carries_its_reasons_through()
    {
        var plan = PlanOf("12.09.2026,105,,,Ion Popescu,,,");

        plan.Reports.ShouldBeEmpty();
        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentWithoutTime);
    }

    [Fact]
    public void On_a_named_day_a_time_earlier_than_one_further_up_the_sheet_is_imported_and_told()
    {
        // A sheet that ran past midnight: 23:50, then 00:30 and 01:10 written on the same page.
        // Nothing is moved to the next day — that would be right here and wrong for a sheet typed
        // out of order — and every row after the midnight is told, not only the first.
        var parsed = TrackingCsvParser.Parse(
            "Ora,Speologi,Stare\r\n"
            + "22:00,\"Ion Popescu; Maria Pop\",intrare\r\n"
            + "23:50,Ion Popescu,iesire\r\n"
            + "00:30,Ion Popescu,intrare\r\n"
            + "01:10,Ion Popescu,iesire\r\n"
            + "23:55,Maria Pop,iesire\r\n",
            TrackingCsvOptions.Default with { Day = new DateOnly(2026, 9, 12) });
        parsed.Readable.ShouldBeTrue();

        var plan = TrackingCsvPlanner.Plan(parsed, Subject());

        plan.Refused.ShouldBeEmpty();
        plan.Reports.Count.ShouldBe(6);
        var told = plan.Reports
            .Where(r => r.Diagnostics.Any(d => d.Problem == TrackingCsvProblem.ClockRunsBackwards))
            .ToList();
        told.Select(r => (r.CaverId, r.Line)).ShouldBe([(Ion, 4), (Ion, 5)]);
        // Each names the line it falls before, and stays on the day that was named.
        told.SelectMany(r => r.Diagnostics)
            .Where(d => d.Problem == TrackingCsvProblem.ClockRunsBackwards)
            .ShouldAllBe(d => d.Severity == TrackingCsvSeverity.Warning && d.Detail == "3");
        told[0].At.ShouldBe(new DateTimeOffset(2026, 9, 12, 0, 30, 0, TimeSpan.Zero));
        // A row that is later than everything above it is not told, whoever it is about.
        plan.Reports.Single(r => r.CaverId == Maria && r.Line == 6).Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void On_a_named_day_the_first_row_about_somebody_is_measured_against_the_rows_above_it_too()
    {
        // A second party goes in after midnight. Maria has no earlier row of her own, and her row
        // is still twenty-three hours before the one above it: the midnight is the sheet's, not
        // one person's, so it is told — and so is everybody on a row, not only the first name.
        var parsed = TrackingCsvParser.Parse(
            "Ora,Speologi,Stare\r\n"
            + "22:00,Ion Popescu,intrare\r\n"
            + "23:50,Ion Popescu,iesire\r\n"
            + "00:30,Maria Pop,intrare\r\n"
            + "01:10,\"Maria Pop; Ion Popescu\",iesire\r\n",
            TrackingCsvOptions.Default with { Day = new DateOnly(2026, 9, 12) });
        parsed.Readable.ShouldBeTrue();

        var plan = TrackingCsvPlanner.Plan(parsed, Subject());

        plan.Refused.ShouldBeEmpty();
        plan.Reports.Count.ShouldBe(5);
        plan.Reports
            .Where(r => r.Diagnostics.Any(d =>
                d.Problem == TrackingCsvProblem.ClockRunsBackwards
                && d.Severity == TrackingCsvSeverity.Warning && d.Detail == "3"))
            .Select(r => (r.CaverId, r.Line))
            .ShouldBe([(Maria, 4), (Maria, 5), (Ion, 5)]);
        // Still on the day that was named: told, not moved.
        plan.Reports.Single(r => r.Line == 4).At
            .ShouldBe(new DateTimeOffset(2026, 9, 12, 0, 30, 0, TimeSpan.Zero));
        // The twin: the rows before the midnight carry nothing.
        plan.Reports.Where(r => r.Line is 2 or 3).SelectMany(r => r.Diagnostics).ShouldBeEmpty();
    }

    [Fact]
    public void On_a_named_day_a_row_that_was_refused_as_being_in_the_future_is_not_the_clock_the_rest_is_measured_against()
    {
        // One mistyped late time must not make every true row below it look like yesterday's.
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var parsed = TrackingCsvParser.Parse(
            "Ora,Speologi,Stare\r\n"
            + "09:00,Ion Popescu,intrare\r\n"
            + "23:00,Ion Popescu,iesire\r\n"
            + "10:00,Maria Pop,intrare\r\n",
            TrackingCsvOptions.Default with { Day = new DateOnly(2026, 9, 12) });

        var plan = TrackingCsvPlanner.Plan(parsed, Subject() with { Now = now });

        plan.Refused.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentInFuture && d.Line == 3);
        plan.Reports.Select(r => r.Line).ShouldBe([2, 4]);
        plan.Reports.SelectMany(r => r.Diagnostics).ShouldBeEmpty();
    }

    [Fact]
    public void A_row_that_is_only_a_note_becomes_a_note_about_each_person_on_it()
    {
        var plan = PlanOf("12.09.2026 09:00,,,,\"Ion Popescu; Maria Pop\",Echipa 1,apa in crestere,");

        plan.Refused.ShouldBeEmpty();
        plan.Reports.Select(r => r.CaverId).ShouldBe([Ion, Maria]);
        plan.Reports.ShouldAllBe(r =>
            r.Kind == TripPositionEventKind.Note
            && r.Note == "apa in crestere"
            && r.ViewerStationName == null
            && r.DepthM == null
            && r.TeamId == TeamOne);
    }

    [Fact]
    public void A_note_needs_no_model_at_all_while_a_place_beside_it_on_the_sheet_still_does()
    {
        // What somebody said on the telephone does not depend on a survey, so a note imports
        // into a watch whose survey has gone — beside a row of the same sheet that claims a
        // place and is refused for wanting one, which is what shows the watch really had none.
        var subject = Subject() with { HasModel = false, Stations = [] };

        var plan = PlanOf(
            "12.09.2026 09:00,,,,Ion Popescu,,apa in crestere,\r\n"
            + "12.09.2026 10:00,105,,,Ion Popescu,,,", subject);

        var note = plan.Reports.ShouldHaveSingleItem();
        note.Line.ShouldBe(2);
        note.Kind.ShouldBe(TripPositionEventKind.Note);
        note.Note.ShouldBe("apa in crestere");
        note.ViewerStationName.ShouldBeNull();
        note.DepthM.ShouldBeNull();

        var refusal = plan.Refused.ShouldHaveSingleItem();
        refusal.Line.ShouldBe(3);
        refusal.Problem.ShouldBe(TrackingCsvProblem.ModelMissing);
    }

    [Fact]
    public void A_whole_name_in_either_order_and_an_initial_with_a_surname_are_the_same_person()
    {
        var plan = PlanOf(
            "12.09.2026 09:00,100,,,Popescu Ion,,,\r\n"
            + "12.09.2026 10:00,100,,,I. Popescu,,,\r\n"
            + "12.09.2026 11:00,100,,,Pop Maria,,,");

        plan.Refused.ShouldBeEmpty();
        plan.Reports.Select(r => r.CaverId).ShouldBe([Ion, Ion, Maria]);
        plan.Reports.Select(r => r.MatchedBy).ShouldBe(
        [
            CaverNameLadder.Rung.FullNameAnyOrder,
            CaverNameLadder.Rung.InitialAndSurname,
            CaverNameLadder.Rung.FullNameAnyOrder,
        ]);
        // What the sheet wrote is kept beside who it was taken for, so a reviewer sees both.
        plan.Reports[1].CaverWritten.ShouldBe("I. Popescu");
        plan.Reports[1].CaverMatched.ShouldBe("Ion Popescu");
    }

    [Fact]
    public void An_initial_and_a_surname_two_people_answer_to_is_refused_and_names_them_both()
    {
        var subject = Subject() with
        {
            Roster = [(Ion, "Ion Popescu"), (Mihai, "Ioana Popescu"), (Maria, "Maria Pop")],
        };

        var plan = PlanOf(
            "12.09.2026 09:00,100,,,I. Popescu,,,\r\n"
            + "12.09.2026 10:00,100,,,M. Pop,,,", subject);

        // The one that can only be one person is imported beside it.
        plan.Reports.ShouldHaveSingleItem().CaverId.ShouldBe(Maria);
        var refusal = plan.Refused.ShouldHaveSingleItem();
        refusal.Problem.ShouldBe(TrackingCsvProblem.CaverAmbiguous);
        refusal.Line.ShouldBe(2);
        refusal.Detail.ShouldBe("I. Popescu: Ion Popescu, Ioana Popescu");
    }

    [Fact]
    public void A_sheet_that_writes_its_own_dates_is_never_told_its_clock_runs_backwards()
    {
        // Out of order, but every row says its day: there is no unmarked midnight to suspect.
        var plan = PlanOf(
            "12.09.2026 23:50,,,,Ion Popescu,,,iesire\r\n"
            + "12.09.2026 00:30,,,,Ion Popescu,,,intrare");

        plan.Reports.Count.ShouldBe(2);
        plan.Reports.SelectMany(r => r.Diagnostics)
            .ShouldNotContain(d => d.Problem == TrackingCsvProblem.ClockRunsBackwards);
    }

    [Fact]
    public void A_row_whose_state_says_note_is_planned_as_a_note_and_claims_no_station()
    {
        // No model, and a depth beside the word: the word wins, so nothing is resolved and the
        // watch having lost its survey costs the row nothing. The row below it is the same depth
        // without the word, refused for wanting the survey — which is what shows it was missing.
        var subject = Subject() with { HasModel = false, Stations = [] };

        var plan = PlanOf(
            "12.09.2026 09:00,105,,,Ion Popescu,Echipa 1,,nota\r\n"
            + "12.09.2026 10:00,105,,,Ion Popescu,,,", subject);

        var note = plan.Reports.ShouldHaveSingleItem();
        note.Line.ShouldBe(2);
        note.Kind.ShouldBe(TripPositionEventKind.Note);
        note.Note.ShouldBeNull();
        note.ViewerStationName.ShouldBeNull();
        note.DepthM.ShouldBeNull();
        note.TeamId.ShouldBe(TeamOne);
        plan.Refused.ShouldHaveSingleItem().Problem.ShouldBe(TrackingCsvProblem.ModelMissing);
    }

    /// <summary>What the log holds for Ion at nine, as a subject that knows it.</summary>
    private static TrackingCsvSubject Holding(TrackingCsvStoredPlace stood) =>
        Subject(existing: [(Ion, Nine)]) with
        {
            Stored = new Dictionary<(Guid, DateTimeOffset), TrackingCsvStoredPlace> { [(Ion, Nine)] = stood },
        };

    private static readonly DateTimeOffset Nine = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_row_that_repeats_the_depth_a_report_already_holds_keeps_the_station_it_was_placed_at()
    {
        // The report was placed at station "9" on a survey the watch has since left; on the one it
        // is on now, 100 m is station "2". The row says 100 m, which is what the report says, so
        // nothing is resolved again: the plan carries the stored station and marks the place kept.
        var subject = Holding(new TrackingCsvStoredPlace(TripPositionEventKind.AtDepth, "9", 100m));

        var kept = PlanOf("12.09.2026 09:00,100,,,Ion Popescu,,,", subject).Reports.ShouldHaveSingleItem();
        kept.KeepsStoredPlace.ShouldBeTrue();
        kept.Replaces.ShouldBeTrue();
        kept.Kind.ShouldBe(TripPositionEventKind.AtDepth);
        kept.ViewerStationName.ShouldBe("9");
        kept.DepthM.ShouldBe(100m);

        // The twin: another depth is another statement, and is resolved on the survey in force.
        var moved = PlanOf("12.09.2026 09:00,150,,,Ion Popescu,,,", subject).Reports.ShouldHaveSingleItem();
        moved.KeepsStoredPlace.ShouldBeFalse();
        moved.ViewerStationName.ShouldBe("3");
        moved.DepthM.ShouldBe(150m);
    }

    [Fact]
    public void A_row_that_repeats_a_station_the_present_survey_does_not_have_is_kept_and_not_refused()
    {
        var subject = Holding(new TrackingCsvStoredPlace(TripPositionEventKind.AtStation, "old.7", null));

        var plan = PlanOf("12.09.2026 09:00,,old.7,,Ion Popescu,,,", subject);
        plan.Refused.ShouldBeEmpty();
        var kept = plan.Reports.ShouldHaveSingleItem();
        kept.KeepsStoredPlace.ShouldBeTrue();
        kept.Kind.ShouldBe(TripPositionEventKind.AtStation);
        kept.ViewerStationName.ShouldBe("old.7");
        kept.DepthM.ShouldBeNull();

        // The twin, three ways: the same station for somebody whose report the log does not hold,
        // another station for the one it does, and the same cell on a log that was told nothing —
        // each is resolved against the survey, which has no such station.
        PlanOf("12.09.2026 09:00,,old.7,,Maria Pop,,,", subject)
            .Refused.ShouldHaveSingleItem().Problem.ShouldBe(TrackingCsvProblem.StationNotInModel);
        PlanOf("12.09.2026 09:00,,old.8,,Ion Popescu,,,", subject)
            .Refused.ShouldHaveSingleItem().Problem.ShouldBe(TrackingCsvProblem.StationNotInModel);
        PlanOf("12.09.2026 09:00,,old.7,,Ion Popescu,,,", Subject(existing: [(Ion, Nine)]))
            .Refused.ShouldHaveSingleItem().Problem.ShouldBe(TrackingCsvProblem.StationNotInModel);
    }

    [Fact]
    public void A_row_about_two_people_keeps_the_place_of_the_one_it_repeats_and_refuses_the_other_once()
    {
        var subject = Holding(new TrackingCsvStoredPlace(TripPositionEventKind.AtStation, "old.7", null));

        var plan = PlanOf("12.09.2026 09:00,,old.7,,\"Ion Popescu; Maria Pop; Mihai Ionescu\",,,", subject);

        plan.Reports.ShouldHaveSingleItem().CaverId.ShouldBe(Ion);
        var refused = plan.Refused.ShouldHaveSingleItem();
        refused.Problem.ShouldBe(TrackingCsvProblem.StationNotInModel);
        refused.Line.ShouldBe(2);
    }

    [Fact]
    public void A_row_that_says_another_kind_of_thing_than_the_stored_report_is_planned_as_any_row_is()
    {
        // The log holds a depth report at 100 m; the row names station "2", which is where 100 m
        // is. That is a station report now and no longer a depth one, so it is written.
        var subject = Holding(new TrackingCsvStoredPlace(TripPositionEventKind.AtDepth, "2", 100m));

        var report = PlanOf("12.09.2026 09:00,,2,,Ion Popescu,,,", subject).Reports.ShouldHaveSingleItem();
        report.KeepsStoredPlace.ShouldBeFalse();
        report.Kind.ShouldBe(TripPositionEventKind.AtStation);
    }
}
