// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Export;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class CaveExportPlannerTests
{
    private static readonly Guid Open = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Protected1 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Protected2 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// The caves this installation protects — the set a decision has to be made about. It is
    /// deliberately not "the caves this caller may not place exactly": a file outlives the
    /// account that took it, so an owner and an administrator are asked as well.
    /// </summary>
    private static HashSet<Guid> Protected(params Guid[] ids) => [.. ids];

    [Theory]
    [InlineData("exact")]
    [InlineData("exact_position")]
    [InlineData("precise")]
    [InlineData("surveyed")]
    [InlineData("GRID_POSITION")]
    [InlineData("")]
    public void The_exact_position_is_not_among_the_things_an_export_can_be_asked_for(string named)
    {
        var scope = new[] { Protected1 };
        var guarded = Protected(Protected1);

        var refused = CaveExportPlanner.Resolve(
            scope, guarded, new CaveExportChoices(AppliedToAll: named));

        refused.Refused.ShouldBeTrue();
        refused.Plan.ShouldBeNull();
        refused.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentUnknownCode);
        refused.Refusal.NamedTreatment.ShouldBe(named);

        // The same request differing only in naming one of the three resolves, so the
        // refusal above is about the treatment and not about anything else in the request.
        var allowed = CaveExportPlanner.Resolve(
            scope,
            guarded,
            new CaveExportChoices(AppliedToAll: ProtectedPositionTreatments.GridPositionCode));

        allowed.Refused.ShouldBeFalse();
        allowed.Plan!.Positions[Protected1].ShouldBe(CaveExportPosition.Grid);
    }

    [Fact]
    public void An_unreadable_treatment_named_for_one_cave_refuses_and_says_which()
    {
        var result = CaveExportPlanner.Resolve(
            [Protected1, Protected2],
            Protected(Protected1, Protected2),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string>
                {
                    [Protected1] = ProtectedPositionTreatments.OmitCode,
                    [Protected2] = "exact",
                },
                AppliedToAll: ProtectedPositionTreatments.NoPositionCode));

        result.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentUnknownCode);
        result.Refusal.CaveIds.ShouldBe([Protected2]);
        result.Refusal.NamedTreatment.ShouldBe("exact");
    }

    [Fact]
    public void A_protected_cave_nobody_decided_about_refuses_the_whole_export()
    {
        var scope = new[] { Open, Protected1, Protected2 };
        var guarded = Protected(Protected1, Protected2);
        var perCave = new Dictionary<Guid, string> { [Protected1] = ProtectedPositionTreatments.OmitCode };

        var refused = CaveExportPlanner.Resolve(scope, guarded, new CaveExportChoices(PerCave: perCave));

        refused.Refused.ShouldBeTrue();
        refused.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentMissingCode);

        // Only the undecided cave is named: the one that was decided about and the one that
        // needed no decision are not a problem with this request.
        refused.Refusal.CaveIds.ShouldBe([Protected2]);

        var allowed = CaveExportPlanner.Resolve(
            scope,
            guarded,
            new CaveExportChoices(PerCave: perCave, AppliedToAll: ProtectedPositionTreatments.NoPositionCode));

        allowed.Refused.ShouldBeFalse();
        allowed.Plan!.Positions[Protected2].ShouldBe(CaveExportPosition.None);
    }

    [Fact]
    public void One_answer_covers_every_cave_that_needs_one_and_leaves_the_rest_as_they_stand()
    {
        var result = CaveExportPlanner.Resolve(
            [Open, Protected1, Protected2],
            Protected(Protected1, Protected2),
            new CaveExportChoices(AppliedToAll: ProtectedPositionTreatments.GridPositionCode));

        var plan = result.Plan.ShouldNotBeNull();
        plan.Positions[Open].ShouldBe(CaveExportPosition.Exact);
        plan.Positions[Protected1].ShouldBe(CaveExportPosition.Grid);
        plan.Positions[Protected2].ShouldBe(CaveExportPosition.Grid);

        // Nothing was chosen for the cave that exports as it stands, so nothing is recorded
        // against it — the audit trail says what was decided, not what was not.
        plan.Treatments.ShouldNotContainKey(Open);
        plan.Treatments[Protected1].ShouldBe(ProtectedPositionTreatment.GridPosition);
        plan.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void A_cave_named_by_itself_beats_the_one_answer_for_all_and_the_stored_one()
    {
        var result = CaveExportPlanner.Resolve(
            [Protected1, Protected2],
            Protected(Protected1, Protected2),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string> { [Protected1] = ProtectedPositionTreatments.OmitCode },
                AppliedToAll: ProtectedPositionTreatments.GridPositionCode,
                StoredAnswer: ProtectedPositionTreatments.NoPositionCode));

        var plan = result.Plan.ShouldNotBeNull();
        plan.Treatments[Protected1].ShouldBe(ProtectedPositionTreatment.Omit);
        plan.Treatments[Protected2].ShouldBe(ProtectedPositionTreatment.GridPosition);
    }

    [Fact]
    public void The_answer_a_caller_settled_on_earlier_is_used_only_when_the_request_says_nothing()
    {
        var scope = new[] { Protected1 };
        var guarded = Protected(Protected1);

        var stored = CaveExportPlanner.Resolve(
            scope, guarded, new CaveExportChoices(StoredAnswer: ProtectedPositionTreatments.OmitCode));

        stored.Plan!.Treatments[Protected1].ShouldBe(ProtectedPositionTreatment.Omit);

        var overridden = CaveExportPlanner.Resolve(
            scope,
            guarded,
            new CaveExportChoices(
                AppliedToAll: ProtectedPositionTreatments.NoPositionCode,
                StoredAnswer: ProtectedPositionTreatments.OmitCode));

        overridden.Plan!.Treatments[Protected1].ShouldBe(ProtectedPositionTreatment.NoPosition);
    }

    [Fact]
    public void A_stored_answer_nobody_can_read_refuses_rather_than_guessing_at_a_coordinate()
    {
        var result = CaveExportPlanner.Resolve(
            [Protected1],
            Protected(Protected1),
            new CaveExportChoices(StoredAnswer: "whatever_it_used_to_be_called"));

        result.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentUnknownCode);
        result.Refusal.NamedTreatment.ShouldBe("whatever_it_used_to_be_called");

        // The refusal is recoverable by saying what to do this time, so a stale stored
        // answer cannot lock a caller out of exporting.
        CaveExportPlanner.Resolve(
                [Protected1],
                Protected(Protected1),
                new CaveExportChoices(
                    AppliedToAll: ProtectedPositionTreatments.OmitCode,
                    StoredAnswer: "whatever_it_used_to_be_called"))
            .Refused.ShouldBeFalse();
    }

    [Fact]
    public void Caves_left_out_are_recorded_so_the_file_can_declare_that_some_are_missing()
    {
        var result = CaveExportPlanner.Resolve(
            [Open, Protected1, Protected2],
            Protected(Protected1, Protected2),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string> { [Protected2] = ProtectedPositionTreatments.NoPositionCode },
                AppliedToAll: ProtectedPositionTreatments.OmitCode));

        var plan = result.Plan.ShouldNotBeNull();
        plan.Omitted.ShouldBe([Protected1]);
        plan.Positions[Protected1].ShouldBe(CaveExportPosition.Omitted);
        plan.Positions.ShouldContainKey(Protected2);
    }

    [Fact]
    public void An_answer_for_a_cave_outside_the_scope_is_ignored_and_never_named_back()
    {
        var outsideScope = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var refused = CaveExportPlanner.Resolve(
            [Protected1],
            Protected(Protected1),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string> { [outsideScope] = "exact" }));

        // Naming that id back — as an unknown treatment or as a cave with no decision —
        // would tell the asker it exists and is readable, which the visibility filter has
        // already declined to answer. So the refusal is about the cave that is in scope.
        refused.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentMissingCode);
        refused.Refusal.CaveIds.ShouldBe([Protected1]);

        var allowed = CaveExportPlanner.Resolve(
            [Protected1],
            Protected(Protected1),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string>
                {
                    [outsideScope] = "exact",
                    [Protected1] = ProtectedPositionTreatments.NoPositionCode,
                },
                AppliedToAll: null));

        allowed.Refused.ShouldBeFalse();
        allowed.Plan!.Positions.ShouldNotContainKey(outsideScope);
    }

    [Fact]
    public void A_named_answer_is_honoured_even_for_a_cave_that_needed_no_decision()
    {
        var result = CaveExportPlanner.Resolve(
            [Open],
            Protected(),
            new CaveExportChoices(
                PerCave: new Dictionary<Guid, string> { [Open] = ProtectedPositionTreatments.OmitCode }));

        // Each of the three puts less in the file than the exact position would, so
        // honouring one can never disclose more than saying nothing would have.
        result.Plan!.Positions[Open].ShouldBe(CaveExportPosition.Omitted);
        result.Plan.Treatments[Open].ShouldBe(ProtectedPositionTreatment.Omit);
    }

    [Fact]
    public void Nothing_a_request_can_say_puts_a_protected_cave_at_its_surveyed_position()
    {
        // Every answer the vocabulary admits, and the case where none was given. Who is asking
        // is not among the inputs at all, which is the point: the exact position of a protected
        // cave is unreachable from here by construction rather than by a check somebody could
        // later find a way past.
        CaveExportChoices[] everyWayOfAsking =
        [
            new(AppliedToAll: ProtectedPositionTreatments.GridPositionCode),
            new(AppliedToAll: ProtectedPositionTreatments.NoPositionCode),
            new(AppliedToAll: ProtectedPositionTreatments.OmitCode),
            new(StoredAnswer: ProtectedPositionTreatments.GridPositionCode),
            new(),
        ];

        foreach (var choices in everyWayOfAsking)
        {
            var result = CaveExportPlanner.Resolve([Protected1], Protected(Protected1), choices);

            if (result.Plan is { } plan)
            {
                plan.Positions[Protected1].ShouldNotBe(CaveExportPosition.Exact);

                // And the file will say what was done, because a treatment was recorded.
                plan.Treatments.ShouldContainKey(Protected1);
            }
            else
            {
                result.Refusal!.Code.ShouldBe(CaveExportPlanner.TreatmentMissingCode);
            }
        }
    }

    [Fact]
    public void The_grid_treatment_publishes_the_position_the_map_already_publishes()
    {
        var exact = new Point(25.44721, 45.53127) { SRID = 4326 };
        const double gridMeters = 5000;

        var position = ProtectedPositionTreatments.Position(
            ProtectedPositionTreatment.GridPosition, exact, gridMeters);

        // Asserted against the application's own snapping rule rather than against a
        // rounding computed here: the point of the grid treatment is that it is the same
        // coordinate the map shows, and a second rounding would eventually differ from it.
        position.ShouldNotBeNull();
        position.ShouldBe(LocationProtection.Snap(exact, gridMeters));
        position.X.ShouldNotBe(exact.X);
        position.SRID.ShouldBe(exact.SRID);

        ProtectedPositionTreatments.Position(ProtectedPositionTreatment.NoPosition, exact, gridMeters)
            .ShouldBeNull();
        ProtectedPositionTreatments.Position(ProtectedPositionTreatment.Omit, exact, gridMeters)
            .ShouldBeNull();
    }

    [Fact]
    public void Every_treatment_has_a_code_and_every_code_its_treatment()
    {
        var treatments = Enum.GetValues<ProtectedPositionTreatment>();

        ProtectedPositionTreatments.Codes.Count.ShouldBe(treatments.Length);

        foreach (var treatment in treatments)
        {
            var code = ProtectedPositionTreatments.Code(treatment);
            ProtectedPositionTreatments.Codes.ShouldContain(code);
            ProtectedPositionTreatments.TryParse(code, out var parsed).ShouldBeTrue();
            parsed.ShouldBe(treatment);
        }

        ProtectedPositionTreatments.TryParse(null, out _).ShouldBeFalse();
    }
}
