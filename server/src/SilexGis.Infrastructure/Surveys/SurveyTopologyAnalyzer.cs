// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using Therion.Blender.Geometry;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Measures the shape of a passage network as the figures the karst literature uses.
/// </summary>
/// <remarks>
/// <para>
/// The definitions are not free choices. Each one below follows the published method as its
/// reference implementation reads it, because the published method has a correction whose prose
/// admits two readings that give different numbers, and the two that matter most are pinned here:
/// the length spread is a normalised entropy over ten bins of a fixed nought-to-a-hundred range
/// after the branch lengths are scaled by the longest of them, not a Shannon entropy over the
/// observed values; and the orientation spread is length-weighted over eighteen bins of a fixed
/// nought-to-a-hundred-and-eighty range and is computed over the complete graph, while every other
/// figure here is computed over the reduced one. The committed reference outputs in the test suite
/// are what hold this to those readings.
/// </para>
/// <para>
/// The reduced graph the counts are taken over is not the branch list. A branch that closes on
/// itself is split into three edges and branches sharing both endpoints are each split into two,
/// through stations invented for the purpose, so that what is counted is a simple graph and the
/// loops survive the simplification rather than being collapsed away by it. Skipping the split
/// silently changes the node count, the edge count, the cyclomatic number and every ratio and
/// degree figure built on them, and nothing about the result looks wrong.
/// </para>
/// </remarks>
public static class SurveyTopologyAnalyzer
{
    /// <summary>
    /// Reads one survey's network shape. Returns null when there is no network to measure — a
    /// file whose legs were all splays, or none of whose legs stood at a station.
    /// </summary>
    public static SurveyTopology? Measure(CenterlineGraph graph, Guid surveyModelId, DateTime computedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.DistinctEdges.Count == 0) { return null; }

        var reduced = graph.Reduce();
        var branches = reduced.Branches;
        var simple = SimpleGraph.FromBranches(branches);

        int n = simple.NodeCount;
        int e = simple.EdgeCount;
        int p = simple.ComponentCount;

        var degrees = simple.Degrees();
        double meanDegree = degrees.Length == 0 ? 0 : degrees.Average();
        double? degreeSd = SampleStandardDeviation(degrees.Select(d => (double)d).ToArray());

        var branchLengths = branches.Select(b => b.Length).ToArray();
        var tortuosities = branches.Select(b => b.Tortuosity).Where(t => t is not null).Select(t => t!.Value).ToArray();

        return new SurveyTopology
        {
            SurveyModelId = surveyModelId,
            NodeCount = graph.Components.Sum(c => c.StationCount),
            EdgeCount = graph.DistinctEdges.Count,
            ComponentCount = graph.Components.Count,

            ReducedNodeCount = n,
            ReducedEdgeCount = e,
            ReducedComponentCount = p,
            CyclomaticNumber = e - n + p,
            ExtremityCount = degrees.Count(d => d == 1),
            JunctionCount = degrees.Count(d => d > 2),

            // Below three nodes the two planar denominators are zero or negative, and a ratio
            // against them would be a number with no meaning rather than a small one.
            Alpha = n > 2 ? (e - n + p) / (double)((2 * n) - 5) : null,
            Beta = n > 0 ? e / (double)n : null,
            Gamma = n > 2 ? e / (double)(3 * (n - 2)) : null,

            MeanDegree = degrees.Length > 0 ? meanDegree : null,
            DegreeStandardDeviation = degreeSd,
            DegreeCoefficientOfVariation = degreeSd is not null && meanDegree != 0 ? degreeSd / meanDegree : null,
            CorrelationOfVertexDegree = DegreeCorrelation(simple, degrees, degreeSd, meanDegree),

            BranchCount = branches.Count,
            LoopingBranchCount = branches.Count - tortuosities.Length,
            MeanBranchLengthM = branchLengths.Length > 0 ? branchLengths.Average() : null,
            BranchLengthCoefficientOfVariation = CoefficientOfVariation(branchLengths),
            MinBranchLengthM = branchLengths.Length > 0 ? branchLengths.Min() : null,
            MaxBranchLengthM = branchLengths.Length > 0 ? branchLengths.Max() : null,

            LengthEntropy = LengthEntropy(branchLengths),
            OrientationEntropy = OrientationEntropy(graph),
            MeanTortuosity = tortuosities.Length > 0 ? tortuosities.Average() : null,
            AverageShortestPathLength = AverageShortestPathLength(simple),
            CentralPointDominance = CentralPointDominance(simple),
            AverageClusteringCoefficient = AverageClustering(simple),

            ComputedAt = computedAtUtc,
        };
    }

    /// <summary>
    /// Spread of the branch lengths across ten equal bins of the range they are scaled into,
    /// normalised so that a network whose branches fill every bin evenly scores 1 and one whose
    /// branches are all much the same length scores near 0. Zero-length branches take no part —
    /// they are an artefact of two stations recorded at one point, not a passage. A single branch
    /// scores 0: one value has nothing to be spread across.
    /// </summary>
    private static double? LengthEntropy(IReadOnlyList<double> lengths)
    {
        var nonZero = lengths.Where(v => v > 0).ToArray();
        if (nonZero.Length == 0) { return null; }
        if (nonZero.Length == 1) { return 0.0; }

        double max = nonZero.Max();
        var counts = new int[10];
        foreach (double v in nonZero)
        {
            counts[Bin(v / max * 100.0, 0.0, 100.0, 10)]++;
        }
        return NormalisedEntropy(counts.Select(c => (double)c));
    }

    /// <summary>
    /// Spread of the passage bearings across eighteen ten-degree bins of the half-circle,
    /// weighted by how much horizontal passage runs on each bearing rather than by how many legs
    /// were shot — a hundred short legs down one passage are one passage. Bearings are folded into
    /// nought to a hundred and eighty because a leg has no direction of travel: which end the file
    /// listed first is not a fact about the cave. Vertical legs have no bearing and are left out
    /// of both the weights and the counts.
    /// </summary>
    private static double? OrientationEntropy(CenterlineGraph graph)
    {
        var geometries = graph.EdgeGeometries()
            .Where(g => g.PlanLength > 0 && !double.IsNaN(g.AzimuthDegrees))
            .ToArray();

        if (geometries.Length <= 1) { return geometries.Length == 1 ? 0.0 : null; }

        var weights = new double[18];
        foreach (var g in geometries)
        {
            double folded = g.AzimuthDegrees % 180.0;
            weights[Bin(folded, 0.0, 180.0, 18)] += g.PlanLength;
        }
        return NormalisedEntropy(weights);
    }

    /// <summary>
    /// Which of <paramref name="binCount"/> equal bins spanning <paramref name="low"/> to
    /// <paramref name="high"/> a value falls in, with the top of the range counted in the last bin
    /// rather than falling off the end. The range is fixed rather than taken from the data: bins
    /// placed around the observed values would make two networks' entropies incomparable, which is
    /// the whole point of computing them.
    /// </summary>
    private static int Bin(double value, double low, double high, int binCount)
    {
        int index = (int)Math.Floor((value - low) / (high - low) * binCount);
        return Math.Clamp(index, 0, binCount - 1);
    }

    /// <summary>Entropy of a histogram divided by the entropy of a flat one over the same number
    /// of bins, so the answer is on a nought-to-one scale whatever the bin count.</summary>
    private static double NormalisedEntropy(IEnumerable<double> counts)
    {
        var values = counts.ToArray();
        double total = values.Sum();
        if (total <= 0) { return 0.0; }

        double entropy = 0.0;
        foreach (double c in values)
        {
            if (c <= 0) { continue; }
            double p = c / total;
            entropy -= p * Math.Log(p);
        }
        return entropy / Math.Log(values.Length);
    }

    /// <summary>
    /// Spread as the sample estimate, dividing by one less than the count. Nothing in .NET picks a
    /// default either way, and the population form is close enough on a large network to pass an
    /// eyeball and wrong enough on a small one to matter.
    /// </summary>
    private static double? SampleStandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2) { return values.Count == 1 ? 0.0 : null; }
        double mean = values.Average();
        double sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / (values.Count - 1));
    }

    private static double? CoefficientOfVariation(IReadOnlyList<double> values)
    {
        var sd = SampleStandardDeviation(values);
        if (sd is null) { return null; }
        double mean = values.Average();
        return mean == 0 ? null : sd / mean;
    }

    /// <summary>
    /// Correlation between the degrees at the two ends of a branch, each branch counted from both
    /// ends. A network where every node has the same degree has no variation to correlate, and is
    /// reported as perfectly correlated rather than as an undefined nought-over-nought.
    /// </summary>
    private static double? DegreeCorrelation(SimpleGraph graph, int[] degrees, double? degreeSd, double meanDegree)
    {
        if (degreeSd is null) { return null; }
        if (meanDegree != 0 && degreeSd.Value == 0) { return 1.0; }
        if (graph.EdgeCount == 0) { return null; }

        var x = new List<double>(graph.EdgeCount * 2);
        var y = new List<double>(graph.EdgeCount * 2);
        foreach (var (a, b) in graph.Edges)
        {
            x.Add(degrees[a]); y.Add(degrees[b]);
            x.Add(degrees[b]); y.Add(degrees[a]);
        }

        double mx = x.Average(), my = y.Average();
        double cov = 0, vx = 0, vy = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - mx, dy = y[i] - my;
            cov += dx * dy; vx += dx * dx; vy += dy * dy;
        }
        return vx <= 0 || vy <= 0 ? null : cov / Math.Sqrt(vx * vy);
    }

    /// <summary>
    /// Mean number of branches between two places, counted in branches rather than metres —
    /// "how many junctions apart" rather than "how far to walk", which is the figure the published
    /// method uses. Pairs in different components have no path at all, so each component is
    /// averaged on its own and the components are combined in proportion to their size.
    /// </summary>
    private static double? AverageShortestPathLength(SimpleGraph graph)
    {
        if (graph.NodeCount == 0) { return null; }

        double weighted = 0.0;
        foreach (var component in graph.ComponentMembers())
        {
            if (component.Count <= 1) { continue; }

            double total = 0.0;
            foreach (int source in component)
            {
                var distance = graph.BreadthFirstDistances(source);
                foreach (int target in component)
                {
                    if (distance[target] >= 0) { total += distance[target]; }
                }
            }
            double mean = total / (component.Count * (component.Count - 1));
            weighted += mean * component.Count;
        }
        return weighted / graph.NodeCount;
    }

    /// <summary>
    /// How far the most travelled-through node stands above the rest: the mean shortfall of every
    /// node's betweenness from the highest. Zero when no place is more central than any other — a
    /// ring, a grid — and approaching one when every route passes through a single junction.
    /// </summary>
    private static double? CentralPointDominance(SimpleGraph graph)
    {
        if (graph.NodeCount < 2) { return null; }

        var betweenness = graph.Betweenness();
        double max = betweenness.Max();
        return betweenness.Sum(b => max - b) / (graph.NodeCount - 1);
    }

    /// <summary>How often two branches leaving one node are themselves joined, averaged over every
    /// node. A node with fewer than two branches contributes nothing but still counts in the
    /// average, as the graph library the reference figures come from does it.</summary>
    private static double? AverageClustering(SimpleGraph graph)
    {
        if (graph.NodeCount == 0) { return null; }

        double total = 0.0;
        for (int v = 0; v < graph.NodeCount; v++)
        {
            var neighbours = graph.Neighbours(v);
            int k = neighbours.Count;
            if (k < 2) { continue; }

            int links = 0;
            for (int i = 0; i < k; i++)
            {
                for (int j = i + 1; j < k; j++)
                {
                    if (graph.AreAdjacent(neighbours[i], neighbours[j])) { links++; }
                }
            }
            total += 2.0 * links / (k * (k - 1));
        }
        return total / graph.NodeCount;
    }
}
