// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The name a plan is committed under: the same for the same plan wherever and whenever it is
/// computed, and another one the moment anything that would be written is another thing.
/// </summary>
public class TrackingCsvPlanDigestTests
{
    private static readonly Guid Ion = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Maria = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeamOne = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeamTwo = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid SurveyOne = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SurveyTwo = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CaveOne = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid CaveTwo = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid StoredReport = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset Morning = new(2026, 9, 12, 8, 15, 0, TimeSpan.Zero);

    private static TrackingCsvPlannedReport Report() => new()
    {
        Line = 2,
        At = Morning,
        CaverId = Ion,
        CaverWritten = "Ion",
        CaverMatched = "Ion Popescu",
        MatchedBy = CaverNameLadder.Rung.GivenName,
        TeamId = TeamOne,
        Kind = TripPositionEventKind.AtDepth,
        ViewerStationName = "2",
        PlaceLabel = "Meandru",
        DepthM = 96m,
        Note = "apa mare",
        Replaces = false,
    };

    private static TrackingCsvPlan PlanOf(params TrackingCsvPlannedReport[] reports) => new()
    {
        Reports = reports,
        CarriesTeam = true,
        CarriesNote = true,
    };

    /// <summary>
    /// Every property of a planned report, and for each either a change that must give the plan
    /// another name or the statement that it is only shown.
    /// </summary>
    /// <remarks>
    /// Held as one list so that the two questions cannot be answered apart: the test below fails
    /// when a property is added to a planned report and not classified here, which is the moment
    /// somebody has to decide whether a commit should be refused for it.
    /// </remarks>
    private static readonly (string Property, bool Written, Func<TrackingCsvPlannedReport, TrackingCsvPlannedReport> Change)[] Properties =
    [
        (nameof(TrackingCsvPlannedReport.Line), true, r => r with { Line = 3 }),
        (nameof(TrackingCsvPlannedReport.At), true, r => r with { At = Morning.AddSeconds(1) }),
        (nameof(TrackingCsvPlannedReport.CaverId), true, r => r with { CaverId = Maria }),
        (nameof(TrackingCsvPlannedReport.TeamId), true, r => r with { TeamId = TeamTwo }),
        (nameof(TrackingCsvPlannedReport.Kind), true, r => r with { Kind = TripPositionEventKind.AtStation }),
        (nameof(TrackingCsvPlannedReport.ViewerStationName), true, r => r with { ViewerStationName = "3" }),
        (nameof(TrackingCsvPlannedReport.DepthM), true, r => r with { DepthM = 96.1m }),
        (nameof(TrackingCsvPlannedReport.Note), true, r => r with { Note = "apa mica" }),
        (nameof(TrackingCsvPlannedReport.Replaces), true, r => r with { Replaces = true }),
        (nameof(TrackingCsvPlannedReport.KeepsStoredPlace), true, r => r with { KeepsStoredPlace = true }),
        // "No team" written over a report and the report's team left alone are the same null
        // team on the planned row and two different writes.
        (nameof(TrackingCsvPlannedReport.KeepsStoredTeam), true, r => r with { KeepsStoredTeam = true }),
        (nameof(TrackingCsvPlannedReport.CaverWritten), false, r => r with { CaverWritten = "Ion P." }),
        (nameof(TrackingCsvPlannedReport.CaverMatched), false, r => r with { CaverMatched = "Ion Popescu-Ionescu" }),
        (nameof(TrackingCsvPlannedReport.MatchedBy), false, r => r with { MatchedBy = CaverNameLadder.Rung.FullName }),
        (nameof(TrackingCsvPlannedReport.PlaceLabel), false, r => r with { PlaceLabel = "Meandrul" }),
        (nameof(TrackingCsvPlannedReport.Diagnostics), false, r => r with
        {
            Diagnostics = [new TrackingCsvDiagnostic(TrackingCsvSeverity.Warning, TrackingCsvProblem.DuplicateInFile, 2)],
        }),
    ];

    [Fact]
    public void The_same_plan_has_the_same_name_in_every_process()
    {
        // Worked out once, apart from this code, from the written-down form of the plan below: a
        // name that only agreed with itself within one run would refuse every commit made after a
        // restart of the server, or on another of its instances.
        var plan = PlanOf(Report(), Report() with { Line = 3, CaverId = Maria, TeamId = null, Note = null, DepthM = null });

        plan.Digest().ShouldBe("c7d79a5a5d05fb5349ddafad0243f027d39dd9f4363c2c0187c71de7cc04b835");
        plan.Digest().ShouldBe(TrackingCsvPlanDigest.Of(plan));
    }

    [Fact]
    public void Every_property_of_a_planned_report_is_either_named_into_the_plan_or_said_to_be_only_shown()
    {
        var declared = typeof(TrackingCsvPlannedReport)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name != "EqualityContract")
            .Order()
            .ToList();

        Properties.Select(p => p.Property).Order().ToList().ShouldBe(declared);
    }

    [Fact]
    public void Every_property_of_a_plan_is_accounted_for_by_one_of_these_tests()
    {
        // The plan's own values, beside its reports': the ones the name takes in are each changed
        // by a test here, and the rest are derived from the reports or only shown. A property
        // added to the plan fails this until somebody decides which it is.
        string[] named =
        [
            nameof(TrackingCsvPlan.Reports), nameof(TrackingCsvPlan.CarriesTeam), nameof(TrackingCsvPlan.CarriesNote),
            nameof(TrackingCsvPlan.SurveyModelId), nameof(TrackingCsvPlan.CaveFeatureId),
        ];
        string[] derivedOrShown =
        [
            nameof(TrackingCsvPlan.Creates), nameof(TrackingCsvPlan.Replaces),
            nameof(TrackingCsvPlan.Refused), nameof(TrackingCsvPlan.UnmatchedCavers),
        ];

        typeof(TrackingCsvPlan)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name != "EqualityContract")
            .Order()
            .ToList()
            .ShouldBe(named.Concat(derivedOrShown).Order().ToList());
    }

    [Fact]
    public void Changing_any_one_value_that_would_be_written_gives_the_plan_another_name()
    {
        var name = PlanOf(Report()).Digest();

        foreach (var (property, _, change) in Properties.Where(p => p.Written))
        {
            PlanOf(change(Report())).Digest().ShouldNotBe(name, $"{property} reaches a written row");
        }
    }

    [Fact]
    public void A_value_that_is_only_shown_leaves_the_name_alone()
    {
        var name = PlanOf(Report()).Digest();

        foreach (var (property, _, change) in Properties.Where(p => !p.Written))
        {
            PlanOf(change(Report())).Digest().ShouldBe(name, $"{property} is shown and never written");
        }

        // And likewise what was refused and who was not recognised: neither writes anything.
        (PlanOf(Report()) with
        {
            Refused = [new TrackingCsvDiagnostic(TrackingCsvSeverity.Error, TrackingCsvProblem.NoCavers, 5)],
            UnmatchedCavers = ["Vasile"],
        }).Digest().ShouldBe(name);
    }

    [Fact]
    public void A_value_taken_away_is_not_the_same_plan_as_a_value_left_empty()
    {
        var name = PlanOf(Report()).Digest();

        PlanOf(Report() with { TeamId = null }).Digest().ShouldNotBe(name);
        PlanOf(Report() with { ViewerStationName = null }).Digest().ShouldNotBe(name);
        PlanOf(Report() with { DepthM = null }).Digest().ShouldNotBe(name);
        PlanOf(Report() with { Note = null }).Digest().ShouldNotBe(name);
        PlanOf(Report() with { Note = "" }).Digest()
            .ShouldNotBe(PlanOf(Report() with { Note = null }).Digest());
    }

    [Fact]
    public void Which_columns_the_sheet_has_is_part_of_the_name()
    {
        // The same rows from a sheet with no team column replace a report without touching its
        // team; from a sheet with one, they clear it. Those are two plans.
        var name = PlanOf(Report()).Digest();

        (PlanOf(Report()) with { CarriesTeam = false }).Digest().ShouldNotBe(name);
        (PlanOf(Report()) with { CarriesNote = false }).Digest().ShouldNotBe(name);
    }

    [Fact]
    public void The_survey_and_the_cave_a_placed_row_is_anchored_to_are_part_of_the_name()
    {
        // A station name is a place only on its own survey: the watch moved to another one whose
        // stations are named alike would have the same rows written somewhere else.
        var anchored = PlanOf(Report()) with { SurveyModelId = SurveyOne, CaveFeatureId = CaveOne };
        var name = anchored.Digest();

        (anchored with { SurveyModelId = SurveyTwo }).Digest().ShouldNotBe(name);
        (anchored with { CaveFeatureId = CaveTwo }).Digest().ShouldNotBe(name);
        (anchored with { SurveyModelId = null, CaveFeatureId = null }).Digest().ShouldNotBe(name);

        // The twin: a plan that places nobody writes no anchor, so another survey under it is
        // still the plan that was shown.
        var comings = PlanOf(Report() with { Kind = TripPositionEventKind.Entered, ViewerStationName = null, DepthM = null })
            with { SurveyModelId = SurveyOne, CaveFeatureId = CaveOne };
        (comings with { SurveyModelId = SurveyTwo, CaveFeatureId = CaveTwo }).Digest().ShouldBe(comings.Digest());

        // And so does a plan whose only placed row leaves the place the log holds standing: that
        // row is not anchored again, whatever survey the watch is on by now.
        var kept = PlanOf(Report() with { Replaces = true, KeepsStoredPlace = true })
            with { SurveyModelId = SurveyOne, CaveFeatureId = CaveOne };
        (kept with { SurveyModelId = SurveyTwo, CaveFeatureId = CaveTwo }).Digest().ShouldBe(kept.Digest());
    }

    private static TrackingCsvReplacedReport Stood() => new(
        StoredReport, TeamOne, TripPositionEventKind.AtStation, SurveyOne, "upper.2", null, "scris de mana", Corrected: false);

    private static Dictionary<(Guid CaverId, DateTimeOffset At), TrackingCsvReplacedReport> Before(
        TrackingCsvReplacedReport stood) => new() { [(Ion, Morning)] = stood };

    [Fact]
    public void Every_value_shown_of_the_report_a_row_would_replace_is_part_of_the_name()
    {
        // The reviewer agreed to a pair: what is there now and what would be left. A colleague
        // correcting the stored report in between changes the first half and nothing of the plan.
        var plan = PlanOf(Report() with { Replaces = true });
        var name = plan.Digest(Before(Stood()));

        var declared = typeof(TrackingCsvReplacedReport)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order()
            .ToList();
        (string Property, TrackingCsvReplacedReport Changed)[] changes =
        [
            (nameof(TrackingCsvReplacedReport.Id), Stood() with { Id = Guid.Parse("44444444-4444-4444-4444-444444444444") }),
            (nameof(TrackingCsvReplacedReport.TeamId), Stood() with { TeamId = null }),
            (nameof(TrackingCsvReplacedReport.Kind), Stood() with { Kind = TripPositionEventKind.Note }),
            (nameof(TrackingCsvReplacedReport.SurveyModelId), Stood() with { SurveyModelId = SurveyTwo }),
            (nameof(TrackingCsvReplacedReport.StationName), Stood() with { StationName = "deep.3" }),
            (nameof(TrackingCsvReplacedReport.DepthM), Stood() with { DepthM = 50m }),
            (nameof(TrackingCsvReplacedReport.Note), Stood() with { Note = "alta nota" }),
            (nameof(TrackingCsvReplacedReport.Corrected), Stood() with { Corrected = true }),
        ];
        changes.Select(c => c.Property).Order().ToList().ShouldBe(declared);

        foreach (var (property, changed) in changes)
        {
            plan.Digest(Before(changed)).ShouldNotBe(name, $"{property} was shown as what is in the log now");
        }

        // The same report read again is the same plan, and one not handed in at all is another.
        plan.Digest(Before(Stood())).ShouldBe(name);
        plan.Digest().ShouldNotBe(name);
    }

    [Fact]
    public void What_the_log_holds_under_a_row_that_replaces_nothing_is_not_part_of_the_name()
    {
        // Only a row that would replace a report was shown one. Handed a report for a row that
        // adds, the name is the one it has without it.
        var plan = PlanOf(Report());

        plan.Digest(Before(Stood())).ShouldBe(plan.Digest());
    }

    [Fact]
    public void A_place_withheld_from_the_caller_is_not_in_the_name_they_are_given()
    {
        // What goes in is what the caller was shown. With the station, the depth and the survey
        // taken out for them, two stored reports that differ only in where somebody was have one
        // name — so no station can be tried against it.
        var plan = PlanOf(Report() with { Replaces = true });
        static TrackingCsvReplacedReport Withheld(TrackingCsvReplacedReport stood) =>
            stood with { SurveyModelId = null, StationName = null, DepthM = null };

        plan.Digest(Before(Withheld(Stood())))
            .ShouldBe(plan.Digest(Before(Withheld(Stood() with { StationName = "deep.3", DepthM = 120m }))));
    }

    [Fact]
    public void The_order_of_the_reports_and_how_many_there_are_are_part_of_the_name()
    {
        var first = Report();
        var second = Report() with { Line = 3, CaverId = Maria };

        PlanOf(first, second).Digest().ShouldNotBe(PlanOf(second, first).Digest());
        PlanOf(first, second).Digest().ShouldNotBe(PlanOf(first).Digest());
        PlanOf().Digest().ShouldNotBe(PlanOf(first).Digest());
    }

    [Fact]
    public void Text_cannot_be_moved_from_one_value_into_the_next_and_keep_the_name()
    {
        var one = PlanOf(Report() with { ViewerStationName = "ab", Note = "c" });
        var other = PlanOf(Report() with { ViewerStationName = "a", Note = "bc" });

        one.Digest().ShouldNotBe(other.Digest());
    }

    [Fact]
    public void One_instant_and_one_depth_have_one_name_however_they_are_written()
    {
        var name = PlanOf(Report()).Digest();

        PlanOf(Report() with { At = Morning.ToOffset(TimeSpan.FromHours(3)) }).Digest().ShouldBe(name);
        PlanOf(Report() with { DepthM = 96.0m }).Digest().ShouldBe(name);
    }

    [Fact]
    public void A_plan_knows_which_columns_its_sheet_has()
    {
        var subject = new TrackingCsvSubject { Roster = [(Ion, "Ion Popescu")] };

        TrackingCsvPlan Read(string header, string row) =>
            TrackingCsvPlanner.Plan(TrackingCsvParser.Parse(header + "\r\n" + row + "\r\n"), subject);

        var bare = Read("Data si ora,Speologi,Stare", "12.09.2026 08:15,Ion Popescu,intrare");
        bare.Reports.Count.ShouldBe(1);
        bare.CarriesTeam.ShouldBeFalse();
        bare.CarriesNote.ShouldBeFalse();

        var withTeam = Read("Data si ora,Speologi,Echipa,Stare", "12.09.2026 08:15,Ion Popescu,,intrare");
        withTeam.CarriesTeam.ShouldBeTrue();
        withTeam.CarriesNote.ShouldBeFalse();

        // An empty cell under a column and no column at all are the same row and two plans.
        withTeam.Reports[0].TeamId.ShouldBe(bare.Reports[0].TeamId);
        withTeam.Digest().ShouldNotBe(bare.Digest());

        Read("Data si ora,Speologi,Nota,Stare", "12.09.2026 08:15,Ion Popescu,,intrare")
            .CarriesNote.ShouldBeTrue();
    }
}
