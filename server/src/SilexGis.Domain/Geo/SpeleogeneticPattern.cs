// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// The kind of cave a set of measurements suggests.
/// </summary>
/// <remarks>
/// These are the four patterns the karst literature separates on the figures a survey can actually
/// produce, plus the two honest non-answers. They are a reading of the measurements and never a
/// statement about how the cave formed: two caves of the same shape can have reached it by
/// different routes, and nothing measurable here tells them apart.
/// </remarks>
public enum SpeleogeneticPatternKind : short
{
    /// <summary>Too little was measured to try. Distinct from <see cref="Undetermined"/>: this is
    /// "the question was not asked", not "the question was asked and the figures did not
    /// answer".</summary>
    Insufficient = 0,

    /// <summary>The rules were applied and no pattern came out ahead. A cave that genuinely sits
    /// between two patterns lands here, and so does one whose figures pull both ways.</summary>
    Undetermined = 1,

    /// <summary>A tree of passages descending with the water that cut them: steep, canyon-shaped,
    /// many dead ends, few or no loops.</summary>
    VadoseBranchwork = 2,

    /// <summary>Passage held near a level by the water table: flat, tube-shaped, still
    /// branching rather than looping.</summary>
    WaterTable = 3,

    /// <summary>Passage that rises and falls repeatedly without going anywhere in particular —
    /// phreatic loops, formed below the water table.</summary>
    Looping = 4,

    /// <summary>A network of passages on a small number of trends, closing on itself repeatedly:
    /// dissolution guided by joints rather than by a route to an outlet.</summary>
    AngularMaze = 5,
}

/// <summary>
/// One measured figure a rule read, and what it was.
/// </summary>
/// <remarks>
/// The point of naming the figures on the trace is that a reader can check the rule rather than
/// believe it. A rule reporting only that it fired is an assertion; a rule reporting that it fired
/// on 0.31 loops per junction is a reading somebody can disagree with.
/// </remarks>
public enum PatternFigure : short
{
    /// <summary>Independent loops per node of the reduced network. A ratio rather than the raw
    /// count, because a large branchwork holds more loops than a small maze and would win a
    /// comparison of counts while being the opposite shape.</summary>
    LoopsPerNode = 1,

    /// <summary>Nodes of the reduced network where the survey stopped, as a fraction of all of
    /// them.</summary>
    DeadEndFraction = 2,

    /// <summary>How often two passages from one junction are themselves joined.</summary>
    Clustering = 3,

    /// <summary>How evenly the passage bearings are spread over the half-circle, nought to
    /// one.</summary>
    OrientationEntropy = 4,

    /// <summary>Average steepness of the passage regardless of direction, degrees.</summary>
    MeanAbsoluteDip = 5,

    /// <summary>Steepest climb measured, degrees.</summary>
    MaximumDip = 6,

    /// <summary>Steepest descent measured, degrees, negative.</summary>
    MinimumDip = 7,

    /// <summary>Vertical range over length of passage, nought to one.</summary>
    Verticality = 8,

    /// <summary>The middle station's width over its height. Above one is a passage wider than it
    /// is tall.</summary>
    MedianWidthHeightRatio = 9,
}

/// <summary>One figure a rule read, with the value it had. <paramref name="Value"/> is null when
/// the cave did not produce that figure, which is what makes a rule unassessable rather than
/// silent.</summary>
public sealed record PatternFigureValue(PatternFigure Figure, double? Value);

/// <summary>
/// The rules, each named for what it observes rather than for what it concludes.
/// </summary>
public enum PatternRule : short
{
    /// <summary>The network closes on itself often.</summary>
    NetworkIsLooped = 1,

    /// <summary>The network barely closes on itself at all.</summary>
    NetworkIsTreeLike = 2,

    /// <summary>Much of the network is places the survey stopped.</summary>
    NetworkEndsOften = 3,

    /// <summary>Passages leaving a junction are themselves joined — the network rings.</summary>
    NetworkRings = 4,

    /// <summary>The passage runs on a small number of trends.</summary>
    BearingsAreConcentrated = 5,

    /// <summary>The passage is steep on average.</summary>
    PassageIsSteep = 6,

    /// <summary>The passage is level on average and the cave gains little height overall.</summary>
    PassageIsLevel = 7,

    /// <summary>The passage climbs and descends steeply while the cave as a whole stays near one
    /// level.</summary>
    ProfileOscillates = 8,

    /// <summary>The passage cross-section is wider than it is tall.</summary>
    SectionIsWide = 9,

    /// <summary>The passage cross-section is taller than it is wide.</summary>
    SectionIsTall = 10,
}

/// <summary>What became of one rule.</summary>
public enum PatternRuleOutcome : short
{
    /// <summary>The cave did not produce the figures the rule reads, so the rule said nothing. Not
    /// the same as the rule having looked and disagreed, and reported separately for that
    /// reason.</summary>
    NotAssessable = 0,

    /// <summary>The rule looked and did not fire.</summary>
    DidNotFire = 1,

    /// <summary>The rule fired.</summary>
    Fired = 2,
}

/// <summary>
/// One rule, what it did, what it read and what it would have counted towards.
/// </summary>
/// <param name="Rule">Which rule.</param>
/// <param name="Outcome">Whether it fired, stayed silent, or had nothing to read.</param>
/// <param name="Supports">The patterns it counts towards when it fires. A rule may support more
/// than one: "few loops" separates the branching caves from the mazes without choosing between
/// them.</param>
/// <param name="Weight">What it counts for. Rules that separate the patterns on their own carry
/// more than rules that only narrow the field.</param>
/// <param name="Figures">The figures it read, with their values.</param>
public sealed record PatternRuleTrace(
    PatternRule Rule,
    PatternRuleOutcome Outcome,
    IReadOnlyList<SpeleogeneticPatternKind> Supports,
    double Weight,
    IReadOnlyList<PatternFigureValue> Figures);

/// <summary>What one pattern scored, summed over the rules that fired for it.</summary>
public sealed record PatternScore(SpeleogeneticPatternKind Kind, double Score);

/// <summary>
/// Something a reader has to know before using the suggestion.
/// </summary>
public enum PatternCaveat : short
{
    /// <summary>The figures were derived from a reduction of the drawn centreline rather than from
    /// the flags the surveyor's own software wrote. A suggestion resting on an approximation is a
    /// guess built on a guess for a measurement, and saying so is the whole reason the basis is
    /// carried this far.</summary>
    FiguresAreApproximated = 1,

    /// <summary>Reading the file lost legs or merged stations, so the network measured is smaller
    /// than the network surveyed.</summary>
    NetworkIsIncomplete = 2,

    /// <summary>Nothing says whether reading the file lost anything, which is not the same as
    /// nothing having been lost.</summary>
    NetworkCompletenessIsUnknown = 3,

    /// <summary>The line work carried no altitudes, so every rule about the profile had nothing to
    /// read.</summary>
    NoAltitudes = 4,

    /// <summary>No station measured a complete cross-section, so the shape rules had nothing to
    /// read.</summary>
    NoCrossSections = 5,

    /// <summary>The passage network was never measured, so the rules about its shape had nothing to
    /// read.</summary>
    NoNetworkFigures = 6,
}

/// <summary>
/// What kind of cave the measurements suggest, and every rule that was applied to reach it.
/// </summary>
/// <param name="Pattern">The suggestion.</param>
/// <param name="Basis">Which body of line work the figures came from.</param>
/// <param name="IsApproximation">True when the basis is the shape-based reduction.</param>
/// <param name="Scores">What every pattern scored, highest first. Present so the suggestion can be
/// checked rather than believed: the pattern named above is the head of this list and nothing
/// else.</param>
/// <param name="Rules">Every rule, in a fixed order, whether it fired or not. A rule that is never
/// shown staying silent is not a rule, it is a constant, so the ones that did not fire are reported
/// beside the ones that did.</param>
/// <param name="AssessableRuleCount">How many rules had figures to read. This is the denominator the
/// suggestion is true of.</param>
/// <param name="FiredRuleCount">How many of those fired.</param>
/// <param name="Caveats">What a reader has to know before using this.</param>
public sealed record PatternSuggestion(
    SpeleogeneticPatternKind Pattern,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    IReadOnlyList<PatternScore> Scores,
    IReadOnlyList<PatternRuleTrace> Rules,
    int AssessableRuleCount,
    int FiredRuleCount,
    IReadOnlyList<PatternCaveat> Caveats);

/// <summary>
/// The figures a suggestion is made from, gathered from wherever they were measured.
/// </summary>
/// <remarks>
/// Plain numbers rather than the rows they were read out of, so that the rules can be exercised on
/// a cave nobody surveyed. Every one of them is nullable and null means "not measured": a rule
/// whose figures are missing reports that it could not be assessed and takes no part, rather than
/// reading a substituted zero and firing on it.
/// </remarks>
/// <param name="Basis">Which body of line work produced the figures.</param>
/// <param name="ReducedNodeCount">Nodes of the reduced network — the places passages meet or
/// end.</param>
/// <param name="CyclomaticNumber">Independent loops in the reduced network.</param>
/// <param name="ExtremityCount">Reduced-network nodes with exactly one passage.</param>
/// <param name="Clustering">How often two passages from one junction are themselves joined.</param>
/// <param name="OrientationEntropy">How evenly the bearings are spread, nought to one.</param>
/// <param name="HasAltitudes">Whether the line work carried a third coordinate. When false every
/// vertical figure below must be null, and the profile rules say they could not be assessed rather
/// than reporting a level cave.</param>
/// <param name="Verticality">Vertical range over length of passage.</param>
/// <param name="MeanAbsoluteDipDegrees">Average steepness regardless of direction.</param>
/// <param name="MinimumDipDegrees">Steepest descent, negative.</param>
/// <param name="MaximumDipDegrees">Steepest climb.</param>
/// <param name="MedianWidthHeightRatio">Middle station's width over its height.</param>
/// <param name="DroppedShotCount">Legs the file held that the reading could not use; null when the
/// file has not been read, which is not the same as nothing having been lost.</param>
/// <param name="MergedStationCount">Stations the reading collapsed into one; null on the same
/// terms.</param>
public sealed record PatternEvidence(
    SurveySegmentBasis Basis,
    int? ReducedNodeCount = null,
    int? CyclomaticNumber = null,
    int? ExtremityCount = null,
    double? Clustering = null,
    double? OrientationEntropy = null,
    bool HasAltitudes = false,
    double? Verticality = null,
    double? MeanAbsoluteDipDegrees = null,
    double? MinimumDipDegrees = null,
    double? MaximumDipDegrees = null,
    double? MedianWidthHeightRatio = null,
    int? DroppedShotCount = null,
    int? MergedStationCount = null);

/// <summary>
/// The pattern classifier: rules over a cave's network and profile figures, producing a suggested
/// pattern <b>and the rules that produced it</b>.
///
/// <para>
/// <b>The trace is the deliverable and the label is a summary of it.</b> "Angular maze" on its own
/// is an assertion a reader cannot check. The rules that fired, the figures they fired on, and the
/// rules that looked and stayed silent are what make this a reading rather than a verdict — and
/// they are what lets somebody who knows the cave say which rule is wrong. So the label is computed
/// from the trace here, in one pass, and never alongside it: a label the trace does not support is
/// the one failure this design exists to prevent.
/// </para>
/// <para>
/// <b>Every threshold is a convention, not a measurement.</b> The patterns come from the karst
/// literature and the figures come from the survey, but the numbers that divide one from the next
/// are chosen, and choosing them differently moves caves across the boundary. They are named
/// constants here so that a reader can see what was chosen, and the suggestion reports the figures
/// beside the thresholds so that a cave sitting just over a line is visible as such.
/// </para>
/// <para>
/// <b>A missing figure never becomes a zero.</b> A cave drawn in plan has no steepness, not a
/// steepness of nought; a cave whose walls were never measured has no cross-section shape, not a
/// square one. Rules reading a figure that is absent report that they could not be assessed and
/// take no part in the score, and the count of rules that could be assessed is reported so a reader
/// can see how much of the reasoning was actually available.
/// </para>
/// <para>
/// <b>Which figures are read is a deliberately short list.</b> Several published network figures
/// barely move between caves of opposite shape — the planar connectivity ratio is near constant on
/// real networks, and the mean shortest path is dominated by how big the cave is rather than what
/// shape it is — so a threshold on one of those produces a confident label out of noise. The
/// bearing spread is read at its low end only, where a cave running on two joint sets is genuinely
/// distinguishable; near the top of its range almost every cave saturates and a rule there would
/// fire on all of them.
/// </para>
/// <para>
/// <b>Pure arithmetic over numbers.</b> It knows nothing about where the figures came from and
/// nothing about who may see them. Whether the cave may be described at all is settled before
/// anything reaches here.
/// </para>
/// </summary>
public static class SpeleogeneticPattern
{
    /// <summary>Loops per reduced node at or above which the network counts as looped.</summary>
    public const double LoopedNetworkThreshold = 0.20;

    /// <summary>Loops per reduced node below which the network counts as tree-like.</summary>
    public const double TreeLikeNetworkThreshold = 0.05;

    /// <summary>Fraction of reduced nodes that are dead ends at or above which the network counts as
    /// ending often.</summary>
    public const double DeadEndFractionThreshold = 0.40;

    /// <summary>Bearing spread at or below which the passage counts as running on a few
    /// trends.</summary>
    public const double ConcentratedBearingsThreshold = 0.85;

    /// <summary>Average steepness, degrees, at or above which the passage counts as steep.</summary>
    public const double SteepDipDegrees = 25;

    /// <summary>Average steepness, degrees, at or below which the passage counts as level.</summary>
    public const double LevelDipDegrees = 10;

    /// <summary>Vertical range over length at or below which a cave counts as staying near one
    /// level.</summary>
    public const double LevelVerticality = 0.05;

    /// <summary>Vertical range over length at or below which a cave counts as going nowhere
    /// vertically overall, which is a weaker claim than staying level and is the one a looping
    /// profile has to satisfy.</summary>
    public const double NetLevelVerticality = 0.15;

    /// <summary>Steepness, degrees, a climb and a descent must both reach before the profile counts
    /// as oscillating.</summary>
    public const double OscillationDipDegrees = 20;

    /// <summary>Width over height at or above which a cross-section counts as wide.</summary>
    public const double WideSectionRatio = 1.2;

    /// <summary>Width over height at or below which a cross-section counts as tall.</summary>
    public const double TallSectionRatio = 0.7;

    /// <summary>
    /// How many rules must have had figures to read before a pattern is named at all.
    /// </summary>
    /// <remarks>
    /// One or two readings agreeing is not a classification, it is a coincidence with a trace
    /// attached — and a confident label on a cave nobody measured is worse than no label, because
    /// the trace makes it look considered.
    /// </remarks>
    public const int MinimumAssessableRules = 3;

    private static readonly SpeleogeneticPatternKind[] Branching =
        [SpeleogeneticPatternKind.VadoseBranchwork, SpeleogeneticPatternKind.WaterTable];

    private static readonly SpeleogeneticPatternKind[] Maze = [SpeleogeneticPatternKind.AngularMaze];

    private static readonly SpeleogeneticPatternKind[] Vadose = [SpeleogeneticPatternKind.VadoseBranchwork];

    private static readonly SpeleogeneticPatternKind[] WaterTable = [SpeleogeneticPatternKind.WaterTable];

    private static readonly SpeleogeneticPatternKind[] Looping = [SpeleogeneticPatternKind.Looping];

    private static readonly SpeleogeneticPatternKind[] Phreatic =
        [SpeleogeneticPatternKind.WaterTable, SpeleogeneticPatternKind.Looping];

    /// <summary>
    /// The patterns a score is kept for — every named pattern, so that one scoring nothing is
    /// visible as having scored nothing rather than being absent.
    /// </summary>
    private static readonly SpeleogeneticPatternKind[] Scored =
    [
        SpeleogeneticPatternKind.VadoseBranchwork,
        SpeleogeneticPatternKind.WaterTable,
        SpeleogeneticPatternKind.Looping,
        SpeleogeneticPatternKind.AngularMaze,
    ];

    /// <summary>
    /// Apply every rule to the figures and read the suggestion off what fired.
    /// </summary>
    public static PatternSuggestion Classify(PatternEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        // Line work with no third coordinate has no profile, and the reduction that produces the
        // approximate figures substitutes zero for an altitude it does not have. Taking those at
        // face value would report a plan drawing as a cave that never changes level — a claim the
        // drawing cannot make — so the profile figures are dropped rather than read.
        var verticality = evidence.HasAltitudes ? evidence.Verticality : null;
        var meanAbsoluteDip = evidence.HasAltitudes ? evidence.MeanAbsoluteDipDegrees : null;
        var minimumDip = evidence.HasAltitudes ? evidence.MinimumDipDegrees : null;
        var maximumDip = evidence.HasAltitudes ? evidence.MaximumDipDegrees : null;

        var loopsPerNode = Ratio(evidence.CyclomaticNumber, evidence.ReducedNodeCount);
        var deadEndFraction = Ratio(evidence.ExtremityCount, evidence.ReducedNodeCount);

        var rules = new List<PatternRuleTrace>
        {
            Rule(PatternRule.NetworkIsLooped, Maze, weight: 2,
                [new(PatternFigure.LoopsPerNode, loopsPerNode)],
                loopsPerNode >= LoopedNetworkThreshold),

            Rule(PatternRule.NetworkIsTreeLike, Branching, weight: 1,
                [new(PatternFigure.LoopsPerNode, loopsPerNode)],
                loopsPerNode < TreeLikeNetworkThreshold),

            Rule(PatternRule.NetworkEndsOften, Branching, weight: 1,
                [new(PatternFigure.DeadEndFraction, deadEndFraction)],
                deadEndFraction >= DeadEndFractionThreshold),

            Rule(PatternRule.NetworkRings, Maze, weight: 1,
                [new(PatternFigure.Clustering, evidence.Clustering)],
                // Strictly above nought, with no tolerance: the figure is a count of joined pairs
                // over a count of pairs, so a network with no ring lands on exact zero and one with
                // a single ring lands measurably off it.
                evidence.Clustering > 0),

            Rule(PatternRule.BearingsAreConcentrated, Maze, weight: 1,
                [new(PatternFigure.OrientationEntropy, evidence.OrientationEntropy)],
                evidence.OrientationEntropy <= ConcentratedBearingsThreshold),

            Rule(PatternRule.PassageIsSteep, Vadose, weight: 2,
                [new(PatternFigure.MeanAbsoluteDip, meanAbsoluteDip)],
                meanAbsoluteDip >= SteepDipDegrees),

            Rule(PatternRule.PassageIsLevel, WaterTable, weight: 2,
                [
                    new(PatternFigure.MeanAbsoluteDip, meanAbsoluteDip),
                    new(PatternFigure.Verticality, verticality),
                ],
                meanAbsoluteDip <= LevelDipDegrees && verticality <= LevelVerticality),

            Rule(PatternRule.ProfileOscillates, Looping, weight: 2,
                [
                    new(PatternFigure.MaximumDip, maximumDip),
                    new(PatternFigure.MinimumDip, minimumDip),
                    new(PatternFigure.Verticality, verticality),
                ],
                maximumDip >= OscillationDipDegrees
                    && minimumDip <= -OscillationDipDegrees
                    && verticality <= NetLevelVerticality),

            Rule(PatternRule.SectionIsWide, Phreatic, weight: 1,
                [new(PatternFigure.MedianWidthHeightRatio, evidence.MedianWidthHeightRatio)],
                evidence.MedianWidthHeightRatio >= WideSectionRatio),

            Rule(PatternRule.SectionIsTall, Vadose, weight: 1,
                [new(PatternFigure.MedianWidthHeightRatio, evidence.MedianWidthHeightRatio)],
                evidence.MedianWidthHeightRatio <= TallSectionRatio),
        };

        var assessable = rules.Count(r => r.Outcome != PatternRuleOutcome.NotAssessable);
        var fired = rules.Count(r => r.Outcome == PatternRuleOutcome.Fired);

        var scores = Scored
            .Select(kind => new PatternScore(
                kind,
                rules.Where(r => r.Outcome == PatternRuleOutcome.Fired && r.Supports.Contains(kind))
                    .Sum(r => r.Weight)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Kind)
            .ToList();

        return new PatternSuggestion(
            Winner(scores, assessable),
            evidence.Basis,
            evidence.Basis == SurveySegmentBasis.SkeletonHeuristic,
            scores,
            rules,
            assessable,
            fired,
            Caveats(evidence));
    }

    /// <summary>
    /// The suggestion, read off the scores and nothing else.
    /// </summary>
    /// <remarks>
    /// A clear winner is a pattern that scored more than every other. A tie at the top is reported
    /// as undetermined rather than broken by the order the patterns happen to be listed in, because
    /// a cave whose figures pull two ways is genuinely between two patterns and saying so is more
    /// use than picking one.
    /// </remarks>
    private static SpeleogeneticPatternKind Winner(IReadOnlyList<PatternScore> scores, int assessable)
    {
        if (assessable < MinimumAssessableRules)
        {
            return SpeleogeneticPatternKind.Insufficient;
        }

        var best = scores[0];
        return best.Score > 0 && (scores.Count < 2 || scores[1].Score < best.Score)
            ? best.Kind
            : SpeleogeneticPatternKind.Undetermined;
    }

    private static PatternRuleTrace Rule(
        PatternRule rule,
        IReadOnlyList<SpeleogeneticPatternKind> supports,
        double weight,
        IReadOnlyList<PatternFigureValue> figures,
        bool fired)
    {
        // A rule with a figure it could not read has not disagreed with anything: it reports that
        // it could not be assessed, and the comparisons above have already answered false against
        // the null that produced it rather than against a substituted number.
        var outcome = figures.Any(f => f.Value is null)
            ? PatternRuleOutcome.NotAssessable
            : fired ? PatternRuleOutcome.Fired : PatternRuleOutcome.DidNotFire;

        return new PatternRuleTrace(rule, outcome, supports, weight, figures);
    }

    private static IReadOnlyList<PatternCaveat> Caveats(PatternEvidence evidence)
    {
        var caveats = new List<PatternCaveat>();

        if (evidence.Basis == SurveySegmentBasis.SkeletonHeuristic)
        {
            caveats.Add(PatternCaveat.FiguresAreApproximated);
        }

        if (evidence.DroppedShotCount is null || evidence.MergedStationCount is null)
        {
            caveats.Add(PatternCaveat.NetworkCompletenessIsUnknown);
        }
        else if (evidence.DroppedShotCount > 0 || evidence.MergedStationCount > 0)
        {
            caveats.Add(PatternCaveat.NetworkIsIncomplete);
        }

        if (evidence.ReducedNodeCount is null or 0)
        {
            caveats.Add(PatternCaveat.NoNetworkFigures);
        }

        if (!evidence.HasAltitudes)
        {
            caveats.Add(PatternCaveat.NoAltitudes);
        }

        if (evidence.MedianWidthHeightRatio is null)
        {
            caveats.Add(PatternCaveat.NoCrossSections);
        }

        return caveats;
    }

    /// <summary>A count over a count, or null unless both exist and the denominator is positive.
    /// A ratio of nothing to nothing is not nought.</summary>
    private static double? Ratio(int? numerator, int? denominator) =>
        numerator is { } n && denominator is { } d && d > 0 ? (double)n / d : null;
}
