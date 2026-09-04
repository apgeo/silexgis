// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The pattern classifier. Two things are pinned here and they matter in different ways.
///
/// <para>
/// <b>Every rule is shown firing and shown staying silent.</b> A rule only ever watched firing is
/// indistinguishable from a constant that always fires, and a classifier built out of constants
/// produces the same confident label for every cave. So each rule has a pair of cases that differ in
/// the one figure it reads.
/// </para>
/// <para>
/// <b>The label agrees with the trace.</b> A label the trace does not support is the failure this
/// design exists to prevent, so the suggestion is recomputed here out of the rules the answer
/// carries and compared against the label the answer states.
/// </para>
/// </summary>
public class SpeleogeneticPatternTests
{
    /// <summary>
    /// A cave sitting between every threshold, so that a single changed figure moves exactly the
    /// rule under test. On its own it fires nothing, which is asserted below rather than assumed.
    /// </summary>
    private static PatternEvidence Evidence(
        int? reducedNodeCount = 100,
        int? cyclomaticNumber = 10,
        int? extremityCount = 10,
        double? clustering = 0,
        double? orientationEntropy = 0.95,
        bool hasAltitudes = true,
        double? verticality = 0.30,
        double? meanAbsoluteDip = 15,
        double? minimumDip = -5,
        double? maximumDip = 5,
        double? medianWidthHeightRatio = 1.0,
        SurveySegmentBasis basis = SurveySegmentBasis.SurveyFlags,
        int? droppedShotCount = 0,
        int? mergedStationCount = 0) =>
        new(basis, reducedNodeCount, cyclomaticNumber, extremityCount, clustering, orientationEntropy,
            hasAltitudes, verticality, meanAbsoluteDip, minimumDip, maximumDip, medianWidthHeightRatio,
            droppedShotCount, mergedStationCount);

    private static PatternRuleTrace TraceOf(PatternEvidence evidence, PatternRule rule) =>
        SpeleogeneticPattern.Classify(evidence).Rules.Single(r => r.Rule == rule);

    // -----------------------------------------------------------------------
    // The baseline fires nothing, which is what makes the pairs below meaningful
    // -----------------------------------------------------------------------

    [Fact]
    public void A_cave_between_every_threshold_fires_no_rule_and_is_not_given_a_pattern()
    {
        var suggestion = SpeleogeneticPattern.Classify(Evidence());

        suggestion.FiredRuleCount.ShouldBe(0);
        suggestion.AssessableRuleCount.ShouldBe(suggestion.Rules.Count);
        suggestion.Rules.ShouldAllBe(r => r.Outcome == PatternRuleOutcome.DidNotFire);
        suggestion.Pattern.ShouldBe(SpeleogeneticPatternKind.Undetermined);
        suggestion.Scores.ShouldAllBe(s => s.Score == 0);
    }

    [Fact]
    public void Every_rule_is_reported_exactly_once_whether_it_fired_or_not()
    {
        // The silent rules are part of the answer: a reader who cannot see that the steepness rule
        // looked and disagreed cannot tell it apart from a cave with no altitudes.
        var reported = SpeleogeneticPattern.Classify(Evidence()).Rules.Select(r => r.Rule).ToList();

        reported.ShouldBe(Enum.GetValues<PatternRule>().OrderBy(r => (short)r).ToList());
    }

    // -----------------------------------------------------------------------
    // One pair per rule: the figure that fires it, and the figure that does not
    // -----------------------------------------------------------------------

    [Fact]
    public void A_network_full_of_loops_fires_the_looped_rule_and_one_with_few_does_not()
    {
        TraceOf(Evidence(cyclomaticNumber: 30), PatternRule.NetworkIsLooped)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(cyclomaticNumber: 10), PatternRule.NetworkIsLooped)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_network_with_almost_no_loop_fires_the_tree_rule_and_a_looped_one_does_not()
    {
        TraceOf(Evidence(cyclomaticNumber: 2), PatternRule.NetworkIsTreeLike)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(cyclomaticNumber: 30), PatternRule.NetworkIsTreeLike)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_network_that_is_mostly_dead_ends_fires_the_ends_rule_and_one_that_is_not_does_not()
    {
        TraceOf(Evidence(extremityCount: 50), PatternRule.NetworkEndsOften)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(extremityCount: 10), PatternRule.NetworkEndsOften)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_network_whose_junctions_ring_fires_the_ring_rule_and_a_branching_one_does_not()
    {
        TraceOf(Evidence(clustering: 0.13), PatternRule.NetworkRings)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(clustering: 0), PatternRule.NetworkRings)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void Passage_on_a_few_trends_fires_the_bearing_rule_and_passage_running_everywhere_does_not()
    {
        // Read at the low end only. The figure saturates near one on almost every cave, so a rule
        // on its high end would fire on all of them and say nothing.
        TraceOf(Evidence(orientationEntropy: 0.70), PatternRule.BearingsAreConcentrated)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(orientationEntropy: 0.95), PatternRule.BearingsAreConcentrated)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void Steep_passage_fires_the_steep_rule_and_gently_graded_passage_does_not()
    {
        TraceOf(Evidence(meanAbsoluteDip: 40), PatternRule.PassageIsSteep)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(meanAbsoluteDip: 15), PatternRule.PassageIsSteep)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void Level_passage_in_a_cave_that_gains_no_height_fires_the_level_rule()
    {
        TraceOf(Evidence(meanAbsoluteDip: 4, verticality: 0.02), PatternRule.PassageIsLevel)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);

        // Level passage in a cave that nevertheless descends a long way is a staircase of level
        // galleries, not a water-table cave, so the second figure has to be shown refusing it.
        TraceOf(Evidence(meanAbsoluteDip: 4, verticality: 0.30), PatternRule.PassageIsLevel)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_profile_that_climbs_and_descends_steeply_while_going_nowhere_fires_the_looping_rule()
    {
        TraceOf(Evidence(maximumDip: 35, minimumDip: -30, verticality: 0.10), PatternRule.ProfileOscillates)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);

        // A cave that only descends steeply is a pitch series, not a set of loops.
        TraceOf(Evidence(maximumDip: 5, minimumDip: -30, verticality: 0.10), PatternRule.ProfileOscillates)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_wide_cross_section_fires_the_wide_rule_and_a_square_one_does_not()
    {
        TraceOf(Evidence(medianWidthHeightRatio: 2.4), PatternRule.SectionIsWide)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(medianWidthHeightRatio: 1.0), PatternRule.SectionIsWide)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    [Fact]
    public void A_tall_cross_section_fires_the_tall_rule_and_a_square_one_does_not()
    {
        TraceOf(Evidence(medianWidthHeightRatio: 0.4), PatternRule.SectionIsTall)
            .Outcome.ShouldBe(PatternRuleOutcome.Fired);
        TraceOf(Evidence(medianWidthHeightRatio: 1.0), PatternRule.SectionIsTall)
            .Outcome.ShouldBe(PatternRuleOutcome.DidNotFire);
    }

    // -----------------------------------------------------------------------
    // A figure that was never measured is not a figure of nought
    // -----------------------------------------------------------------------

    [Fact]
    public void A_plan_drawing_leaves_the_profile_rules_unassessed_rather_than_reporting_a_level_cave()
    {
        // The reduction behind the approximate figures writes zero for an altitude it does not
        // have, so vertical figures offered for line work with no third coordinate are dropped
        // rather than read: a cave drawn in plan is not a cave that never changes level.
        var suggestion = SpeleogeneticPattern.Classify(
            Evidence(hasAltitudes: false, verticality: 0, meanAbsoluteDip: 0, minimumDip: 0, maximumDip: 0));

        foreach (var rule in new[] { PatternRule.PassageIsSteep, PatternRule.PassageIsLevel, PatternRule.ProfileOscillates })
        {
            var trace = suggestion.Rules.Single(r => r.Rule == rule);
            trace.Outcome.ShouldBe(PatternRuleOutcome.NotAssessable);
            trace.Figures.ShouldAllBe(f => f.Value == null);
        }

        suggestion.Caveats.ShouldContain(PatternCaveat.NoAltitudes);
        suggestion.Scores.Single(s => s.Kind == SpeleogeneticPatternKind.WaterTable).Score.ShouldBe(0);
    }

    [Fact]
    public void A_cave_whose_walls_were_never_measured_leaves_the_shape_rules_unassessed()
    {
        var suggestion = SpeleogeneticPattern.Classify(Evidence(medianWidthHeightRatio: null));

        suggestion.Rules.Single(r => r.Rule == PatternRule.SectionIsWide)
            .Outcome.ShouldBe(PatternRuleOutcome.NotAssessable);
        suggestion.Rules.Single(r => r.Rule == PatternRule.SectionIsTall)
            .Outcome.ShouldBe(PatternRuleOutcome.NotAssessable);
        suggestion.Caveats.ShouldContain(PatternCaveat.NoCrossSections);
    }

    [Fact]
    public void A_cave_with_no_network_figures_leaves_the_network_rules_unassessed()
    {
        var suggestion = SpeleogeneticPattern.Classify(Evidence(
            reducedNodeCount: null, cyclomaticNumber: null, extremityCount: null,
            clustering: null, orientationEntropy: null));

        suggestion.Rules
            .Where(r => r.Rule is PatternRule.NetworkIsLooped or PatternRule.NetworkIsTreeLike
                or PatternRule.NetworkEndsOften or PatternRule.NetworkRings or PatternRule.BearingsAreConcentrated)
            .ShouldAllBe(r => r.Outcome == PatternRuleOutcome.NotAssessable);
        suggestion.Caveats.ShouldContain(PatternCaveat.NoNetworkFigures);
    }

    [Fact]
    public void A_network_of_no_nodes_gives_no_loop_ratio_rather_than_a_ratio_of_nought()
    {
        // Nought loops over nought nodes is not a tree-like network, and firing the tree rule on it
        // would classify a cave that was never surveyed as a branchwork.
        var trace = TraceOf(Evidence(reducedNodeCount: 0, cyclomaticNumber: 0), PatternRule.NetworkIsTreeLike);

        trace.Outcome.ShouldBe(PatternRuleOutcome.NotAssessable);
        trace.Figures.ShouldHaveSingleItem().Value.ShouldBeNull();
    }

    [Fact]
    public void Too_few_rules_to_assess_is_answered_as_insufficient_and_not_as_undetermined()
    {
        // "Nothing was measured" and "everything was measured and it did not decide" are different
        // answers, and collapsing them would let a cave nobody surveyed look like a hard case.
        var suggestion = SpeleogeneticPattern.Classify(new PatternEvidence(
            SurveySegmentBasis.SurveyFlags, MedianWidthHeightRatio: 2.4));

        suggestion.AssessableRuleCount.ShouldBeLessThan(SpeleogeneticPattern.MinimumAssessableRules);
        suggestion.Pattern.ShouldBe(SpeleogeneticPatternKind.Insufficient);

        // And the same cave with enough of its network measured is given a pattern, so the refusal
        // above is a refusal of this cave rather than a classifier that never answers.
        SpeleogeneticPattern.Classify(Evidence(cyclomaticNumber: 30, clustering: 0.13, orientationEntropy: 0.7))
            .Pattern.ShouldBe(SpeleogeneticPatternKind.AngularMaze);
    }

    // -----------------------------------------------------------------------
    // Two caves of opposite shape are read differently
    // -----------------------------------------------------------------------

    /// <summary>A joint-guided network: closing on itself repeatedly, few ends, level, wide.</summary>
    private static PatternEvidence Maze(SurveySegmentBasis basis = SurveySegmentBasis.SurveyFlags) =>
        Evidence(reducedNodeCount: 40, cyclomaticNumber: 20, extremityCount: 2, clustering: 0.13,
            orientationEntropy: 0.62, verticality: 0.03, meanAbsoluteDip: 6, minimumDip: -8,
            maximumDip: 8, medianWidthHeightRatio: 1.4, basis: basis);

    /// <summary>A tree of steep canyon passages: no loops, many ends, taller than wide.</summary>
    private static PatternEvidence Branchwork() =>
        Evidence(reducedNodeCount: 60, cyclomaticNumber: 0, extremityCount: 33, clustering: 0,
            orientationEntropy: 0.93, verticality: 0.35, meanAbsoluteDip: 34, minimumDip: -55,
            maximumDip: 10, medianWidthHeightRatio: 0.5);

    [Fact]
    public void A_maze_and_a_branchwork_are_suggested_differently()
    {
        SpeleogeneticPattern.Classify(Maze()).Pattern.ShouldBe(SpeleogeneticPatternKind.AngularMaze);
        SpeleogeneticPattern.Classify(Branchwork()).Pattern.ShouldBe(SpeleogeneticPatternKind.VadoseBranchwork);
    }

    [Fact]
    public void The_maze_is_suggested_by_the_rules_a_maze_is_recognised_by()
    {
        var fired = SpeleogeneticPattern.Classify(Maze()).Rules
            .Where(r => r.Outcome == PatternRuleOutcome.Fired)
            .Select(r => r.Rule)
            .ToList();

        fired.ShouldContain(PatternRule.NetworkIsLooped);
        fired.ShouldContain(PatternRule.NetworkRings);
        fired.ShouldContain(PatternRule.BearingsAreConcentrated);
        fired.ShouldNotContain(PatternRule.NetworkIsTreeLike);
    }

    [Theory]
    [MemberData(nameof(ShapedCaves))]
    public void The_label_is_never_more_than_the_rules_that_fired_support(PatternEvidence evidence)
    {
        // The check the whole design turns on. The answer's label is recomputed here from the
        // answer's own trace, so a label arrived at any other way fails rather than being believed.
        var suggestion = SpeleogeneticPattern.Classify(evidence);

        double ScoreOf(SpeleogeneticPatternKind kind) => suggestion.Rules
            .Where(r => r.Outcome == PatternRuleOutcome.Fired && r.Supports.Contains(kind))
            .Sum(r => r.Weight);

        foreach (var score in suggestion.Scores)
        {
            score.Score.ShouldBe(ScoreOf(score.Kind));
        }

        suggestion.Scores.ShouldBe(suggestion.Scores.OrderByDescending(s => s.Score).ToList());
        suggestion.Pattern.ShouldBe(suggestion.Scores[0].Kind);
        ScoreOf(suggestion.Pattern).ShouldBeGreaterThan(0);
        suggestion.Scores.Skip(1).ShouldAllBe(s => s.Score < suggestion.Scores[0].Score);
    }

    public static TheoryData<PatternEvidence> ShapedCaves() => new() { Maze(), Branchwork() };

    // -----------------------------------------------------------------------
    // What the reader has to be told about the figures themselves
    // -----------------------------------------------------------------------

    [Fact]
    public void A_suggestion_made_from_approximated_figures_says_so_and_one_made_from_the_files_own_flags_does_not()
    {
        var approximated = SpeleogeneticPattern.Classify(Maze(SurveySegmentBasis.SkeletonHeuristic));
        approximated.IsApproximation.ShouldBeTrue();
        approximated.Basis.ShouldBe(SurveySegmentBasis.SkeletonHeuristic);
        approximated.Caveats.ShouldContain(PatternCaveat.FiguresAreApproximated);

        // The suggestion itself is unchanged — the caveat is about how much to trust it, not about
        // what it says — and the flag-based cave must not carry the sentence.
        var measured = SpeleogeneticPattern.Classify(Maze());
        measured.Pattern.ShouldBe(approximated.Pattern);
        measured.IsApproximation.ShouldBeFalse();
        measured.Caveats.ShouldNotContain(PatternCaveat.FiguresAreApproximated);
    }

    [Fact]
    public void A_network_that_lost_legs_when_it_was_read_says_so_and_a_whole_one_does_not()
    {
        SpeleogeneticPattern.Classify(Evidence(droppedShotCount: 3, mergedStationCount: 0))
            .Caveats.ShouldContain(PatternCaveat.NetworkIsIncomplete);
        SpeleogeneticPattern.Classify(Evidence(droppedShotCount: 0, mergedStationCount: 0))
            .Caveats.ShouldNotContain(PatternCaveat.NetworkIsIncomplete);
    }

    [Fact]
    public void A_file_that_was_never_read_says_completeness_is_unknown_rather_than_whole()
    {
        // Null there means nothing has counted what was lost, which is not the same statement as
        // nothing having been lost.
        var suggestion = SpeleogeneticPattern.Classify(
            Evidence(droppedShotCount: null, mergedStationCount: null));

        suggestion.Caveats.ShouldContain(PatternCaveat.NetworkCompletenessIsUnknown);
        suggestion.Caveats.ShouldNotContain(PatternCaveat.NetworkIsIncomplete);
    }
}
