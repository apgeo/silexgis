// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a rule set makes of a candidate, and — the part that must never become clever — how a
/// tie is broken.
/// </summary>
public class TermRuleEvaluatorTests
{
    private static TermRule Rule(
        string id,
        ImportTargetKind target,
        string[] terms,
        TermMatchMode mode = TermMatchMode.WholeWord,
        bool matchDescription = false,
        string? featureType = null,
        string? caveType = null) => new()
        {
            Id = id,
            Name = id,
            MatchMode = mode,
            MatchName = true,
            MatchDescription = matchDescription,
            Terms = new Dictionary<string, IReadOnlyList<string>> { ["ro"] = terms },
            Target = target,
            FeatureTypeCode = featureType,
            CaveTypeCode = caveType,
        };

    [Fact]
    public void Order_decides_a_tie_and_the_loser_is_reported_rather_than_dropped()
    {
        // "Peștera de la Izbuc" is a cave that a spring rule also claims. Nothing weighs the
        // two matches; the cave rule wins because it is higher in the set, which is what makes
        // a club able to tune the answer by dragging one row.
        var cave = Rule("cave", ImportTargetKind.Cave, ["peșteră", "peștera"], caveType: "cave");
        var spring = Rule("spring", ImportTargetKind.SurfaceFeature, ["izbuc"], featureType: "water_flow");

        var proposal = TermRuleEvaluator.Evaluate(
            [cave, spring], new CandidateText("Peștera de la Izbuc", null), []);

        proposal.Winner!.Rule.Id.ShouldBe("cave");
        proposal.HasConflict.ShouldBeTrue();
        proposal.Contenders.Select(c => c.Rule.Id).ShouldBe(["spring"]);

        // The same two rules the other way round give the other answer, deterministically.
        TermRuleEvaluator.Evaluate([spring, cave], new CandidateText("Peștera de la Izbuc", null), [])
            .Winner!.Rule.Id.ShouldBe("spring");
    }

    [Fact]
    public void A_disabled_rule_claims_nothing_but_stays_in_the_set()
    {
        var disabled = Rule("cave", ImportTargetKind.Cave, ["peșteră"], caveType: "cave") with { Enabled = false };
        var spring = Rule("spring", ImportTargetKind.SurfaceFeature, ["izbuc"], featureType: "water_flow");

        var proposal = TermRuleEvaluator.Evaluate(
            [disabled, spring], new CandidateText("Peștera de la Izbuc", null), []);

        proposal.Winner!.Rule.Id.ShouldBe("spring");
        proposal.HasConflict.ShouldBeFalse();
    }

    [Fact]
    public void The_name_is_read_before_the_description()
    {
        // Within one rule a match on the label beats the same term in a note, and the field is
        // reported — stripping only ever touches the name, so the difference matters.
        var rule = Rule("cave", ImportTargetKind.Cave, ["peșteră"], matchDescription: true, caveType: "cave");

        TermRuleEvaluator.Evaluate([rule], new CandidateText("Ursilor", "o peșteră mare"), [])
            .Winner!.Field.ShouldBe(CandidateField.Description);
        TermRuleEvaluator.Evaluate([rule], new CandidateText("Peștera Ursilor", "o peșteră mare"), [])
            .Winner!.Field.ShouldBe(CandidateField.Name);
    }

    [Fact]
    public void A_rule_that_reads_only_the_name_ignores_the_description_entirely()
    {
        var rule = Rule("cave", ImportTargetKind.Cave, ["peșteră"], caveType: "cave");
        TermRuleEvaluator.Evaluate([rule], new CandidateText("Ursilor", "o peșteră mare"), [])
            .Winner.ShouldBeNull();
    }

    [Fact]
    public void Language_selection_narrows_which_terms_take_part()
    {
        var rule = new TermRule
        {
            Id = "cave",
            Name = "cave",
            Terms = new Dictionary<string, IReadOnlyList<string>>
            {
                ["ro"] = ["peșteră"],
                ["en"] = ["cave"],
                [TermRule.AnyLanguage] = ["p."],
            },
            MatchMode = TermMatchMode.Contains,
            Target = ImportTargetKind.Cave,
            CaveTypeCode = "cave",
        };

        // An import told to read Romanian does not claim an English label…
        TermRuleEvaluator.Evaluate([rule], new CandidateText("Bear Cave", null), ["ro"]).Winner.ShouldBeNull();
        TermRuleEvaluator.Evaluate([rule], new CandidateText("Peștera Ursilor", null), ["ro"]).Winner.ShouldNotBeNull();

        // …but the language-independent abbreviations always take part, because a GPS writes
        // those the same way whoever is holding it.
        TermRuleEvaluator.Evaluate([rule], new CandidateText("P. Ursilor", null), ["en"]).Winner.ShouldNotBeNull();

        // Naming no language at all uses everything the set holds.
        TermRuleEvaluator.Evaluate([rule], new CandidateText("Bear Cave", null), []).Winner.ShouldNotBeNull();
    }

    [Fact]
    public void The_dry_run_counts_only_the_rule_that_won()
    {
        // A rule that also matched but lost must not be reported as having claimed anything,
        // or the counts would add up to more candidates than the file holds.
        var cave = Rule("cave", ImportTargetKind.Cave, ["peșteră"], caveType: "cave");
        var spring = Rule("spring", ImportTargetKind.SurfaceFeature, ["izbuc"], featureType: "water_flow");
        var proposals = new[]
        {
            TermRuleEvaluator.Evaluate([cave, spring], new CandidateText("Peștera de la Izbuc", null), []),
            TermRuleEvaluator.Evaluate([cave, spring], new CandidateText("Izbuc Mare", null), []),
            TermRuleEvaluator.Evaluate([cave, spring], new CandidateText("Parcare", null), []),
        };

        var counts = TermRuleEvaluator.HitCounts(proposals);
        counts["cave"].ShouldBe(1);
        counts["spring"].ShouldBe(1);
        counts.Values.Sum().ShouldBe(2);
    }
}
