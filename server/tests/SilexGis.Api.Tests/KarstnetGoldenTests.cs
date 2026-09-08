// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Shouldly;

namespace SilexGis.Api.Tests;

/// <summary>
/// The committed reference topology metrics, checked for internal consistency.
/// </summary>
/// <remarks>
/// <para>
/// Two published karst networks are committed under Fixtures/Karstnet together with the metrics
/// the reference implementation of the published method computes for them. The reference
/// implementation is Python and is a developer tool only — never a runtime dependency, never on
/// a request path — so its output is committed and this suite runs on a machine with no Python
/// installed. Regeneration and drift-checking are the job of scripts/karstnet-oracle.mjs.
/// </para>
/// <para>
/// What these tests are for, and what they are not. Nothing here touches the .NET implementation:
/// every assertion below reads the committed reference output and checks it against itself. The
/// metrics are defined by a paper with a published correction, and two defensible readings of it
/// give different numbers, so the assertions state the relationships the reference's reading
/// implies — the cyclomatic number over the reduced graph, the ratios over all its nodes, the
/// sample form of the spread, the entropies normalised. Their job is to fail loudly when a
/// regeneration of the oracle quietly changes what it says, which is the one thing a golden file
/// cannot report about itself.
/// </para>
/// <para>
/// The implementation is held to these numbers elsewhere, by
/// <c>SurveyTopologyTests.The_figures_match_the_reference_implementation_on_the_published_networks</c>,
/// which measures the same two networks with the production analyzer and compares every figure.
/// If that test is ever narrowed, this class goes on passing and proves nothing about the code —
/// so narrowing it is the change to be careful about, not this one.
/// </para>
/// </remarks>
public class KarstnetGoldenTests
{
    public static TheoryData<string> Networks() => new() { "huttes", "sakany" };

    /// <summary>
    /// The counts the reference reports, pinned. Not a duplicate of the golden file: it is the
    /// tripwire that says a regeneration changed the oracle rather than the implementation, which
    /// is the one difference a golden file cannot report about itself.
    /// </summary>
    [Theory]
    [InlineData("huttes", 41, 41, 12, 12)]
    [InlineData("sakany", 1716, 1784, 344, 412)]
    public void The_reference_networks_are_the_size_they_have_always_been(
        string network, int stations, int shots, int reducedNodes, int reducedEdges)
    {
        var golden = Golden(network);

        Complete(golden).GetProperty("nodeCount").GetInt32().ShouldBe(stations);
        Complete(golden).GetProperty("edgeCount").GetInt32().ShouldBe(shots);
        Reduced(golden).GetProperty("nodeCount").GetInt32().ShouldBe(reducedNodes);
        Reduced(golden).GetProperty("edgeCount").GetInt32().ShouldBe(reducedEdges);
    }

    /// <summary>
    /// The cyclomatic number is edges minus nodes plus components, and it is counted on the
    /// reduced graph rather than the complete one. Counting it on the complete graph gives the
    /// same answer here only by accident — contracting a chain removes one node and one edge each
    /// time — but every ratio built on it below divides by the reduced node count, so the two are
    /// not interchangeable.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void The_cyclomatic_number_is_edges_minus_nodes_plus_components(string network)
    {
        var reduced = Reduced(Golden(network));

        var expected = reduced.GetProperty("edgeCount").GetInt32()
            - reduced.GetProperty("nodeCount").GetInt32()
            + reduced.GetProperty("componentCount").GetInt32();

        reduced.GetProperty("cyclomaticNumber").GetInt32().ShouldBe(expected);
    }

    /// <summary>
    /// The three connectivity ratios, each over the reduced graph and each over ALL its nodes —
    /// including the degree-2 nodes a loop needs to keep its shape. Excluding those is the
    /// plausible-looking mistake: it changes every one of the three and matches nothing in the
    /// literature the ratios come from.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void The_connectivity_ratios_are_taken_over_every_node_of_the_reduced_graph(string network)
    {
        var golden = Golden(network);
        var reduced = Reduced(golden);
        var howard = golden.GetProperty("howard");

        double nodes = reduced.GetProperty("nodeCount").GetInt32();
        double edges = reduced.GetProperty("edgeCount").GetInt32();
        double cycles = reduced.GetProperty("cyclomaticNumber").GetInt32();

        howard.GetProperty("alpha").GetDouble().ShouldBe(cycles / (2 * nodes - 5), Tolerance);
        howard.GetProperty("beta").GetDouble().ShouldBe(edges / nodes, Tolerance);
        howard.GetProperty("gamma").GetDouble().ShouldBe(edges / (3 * (nodes - 2)), Tolerance);
    }

    /// <summary>
    /// Extremities are degree exactly one and junctions are degree three or more, so a degree-2
    /// node is neither — it is a node the reduction had to keep to preserve a loop. The degrees
    /// also have to add up: their sum is twice the edge count, which is the cheapest proof that
    /// the adjacency behind them counted each shot once rather than once per direction.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void The_degree_histogram_accounts_for_every_node_and_every_edge_end(string network)
    {
        var golden = Golden(network);
        var reduced = Reduced(golden);
        var histogram = golden.GetProperty("degree").GetProperty("histogram");

        var nodesByDegree = histogram.EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name, CultureInfo.InvariantCulture), p => p.Value.GetInt32());

        nodesByDegree.Values.Sum().ShouldBe(reduced.GetProperty("nodeCount").GetInt32());
        nodesByDegree.Sum(p => p.Key * p.Value)
            .ShouldBe(2 * reduced.GetProperty("edgeCount").GetInt32());

        nodesByDegree.Where(p => p.Key == 1).Sum(p => p.Value)
            .ShouldBe(reduced.GetProperty("extremityCount").GetInt32());
        nodesByDegree.Where(p => p.Key > 2).Sum(p => p.Value)
            .ShouldBe(reduced.GetProperty("junctionCount").GetInt32());
    }

    /// <summary>
    /// The spread of the degrees the reference reports is the sample estimate, dividing by one
    /// less than the count. No standard library picks a default either way, and the population
    /// form is close enough on a large network to pass an eyeball and wrong enough on a small one
    /// to matter — so which one the oracle used is worth pinning.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void The_spread_of_the_degrees_is_the_sample_estimate(string network)
    {
        var golden = Golden(network);
        var degree = golden.GetProperty("degree");

        var degrees = degree.GetProperty("histogram").EnumerateObject()
            .SelectMany(p => Enumerable.Repeat((double)int.Parse(p.Name, CultureInfo.InvariantCulture), p.Value.GetInt32()))
            .ToArray();

        var mean = degrees.Average();
        var sampleSd = Math.Sqrt(degrees.Sum(d => (d - mean) * (d - mean)) / (degrees.Length - 1));

        degree.GetProperty("mean").GetDouble().ShouldBe(mean, Tolerance);
        degree.GetProperty("standardDeviation").GetDouble().ShouldBe(sampleSd, Tolerance);
        degree.GetProperty("coefficientOfVariation").GetDouble().ShouldBe(sampleSd / mean, Tolerance);

        // The same two figures again, reported under the names the published method uses. They
        // are the same numbers; a reader of one panel should not have to wonder.
        var metrics = golden.GetProperty("metrics");
        metrics.GetProperty("meanDegree").GetDouble().ShouldBe(mean, Tolerance);
        metrics.GetProperty("degreeCoefficientOfVariation").GetDouble()
            .ShouldBe(sampleSd / mean, Tolerance);
    }

    /// <summary>
    /// Branches are not reduced-graph edges, and assuming they are is a silent error: a branch
    /// that closes on itself is split into three edges and several branches sharing one pair of
    /// endpoints are each split into two, so the reduced graph carries at least as many edges as
    /// there are branches and usually more. The length statistics are over the branches; the
    /// counts, degrees and ratios are over the edges.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void There_are_never_fewer_reduced_edges_than_branches(string network)
    {
        var golden = Golden(network);
        var branches = golden.GetProperty("branches");

        var branchCount = branches.GetProperty("count").GetInt32();
        branchCount.ShouldBeGreaterThan(0);
        Reduced(golden).GetProperty("edgeCount").GetInt32().ShouldBeGreaterThanOrEqualTo(branchCount);

        // A branch whose two ends are the same station has no straight-line distance to be
        // measured against, so its tortuosity is undefined. Those branches are excluded from the
        // mean and counted, because a mean quietly taken over fewer branches than were surveyed
        // is a confident wrong number.
        branches.GetProperty("loopingCount").GetInt32().ShouldBeInRange(0, branchCount);
    }

    /// <summary>
    /// Both entropies are normalised to the number of bins their histogram uses, so both land in
    /// zero to one whatever the network. An implementation that takes a plain natural-log Shannon
    /// entropy instead produces a number above one on any network with more than three occupied
    /// bins, which is what makes this cheap assertion worth having.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void The_entropies_are_normalised_and_the_tortuosity_is_at_least_one(string network)
    {
        var metrics = Golden(network).GetProperty("metrics");

        metrics.GetProperty("lengthEntropy").GetDouble().ShouldBeInRange(0.0, 1.0);
        metrics.GetProperty("orientationEntropy").GetDouble().ShouldBeInRange(0.0, 1.0);

        // A branch is at least as long as the straight line between its ends.
        metrics.GetProperty("meanTortuosity").GetDouble().ShouldBeGreaterThanOrEqualTo(1.0);
    }

    /// <summary>
    /// Every figure the panel will show is present and defined on both reference networks, so a
    /// regeneration that starts reporting nothing for one of them fails here rather than showing
    /// a blank tile.
    /// </summary>
    [Theory]
    [MemberData(nameof(Networks))]
    public void Every_published_metric_has_a_value_on_both_reference_networks(string network)
    {
        var metrics = Golden(network).GetProperty("metrics");

        string[] published =
        [
            "meanLengthM", "lengthCoefficientOfVariation", "lengthEntropy", "orientationEntropy",
            "meanTortuosity", "averageShortestPathLength", "centralPointDominance", "meanDegree",
            "degreeCoefficientOfVariation", "correlationOfVertexDegree",
        ];

        foreach (var name in published)
        {
            metrics.TryGetProperty(name, out var value).ShouldBeTrue($"{network} is missing {name}");
            value.ValueKind.ShouldBe(JsonValueKind.Number, $"{network} has no value for {name}");
        }
    }

    /// <summary>
    /// The two networks are not the same shape, and the metrics say so. A tree-like network and
    /// one threaded with loops must separate on the ratios that exist to tell them apart; if they
    /// do not, the numbers are arithmetic without meaning. This is the weak form of that check —
    /// the strong one is a maze and a branchwork built to differ in one respect only.
    /// </summary>
    [Fact]
    public void A_looped_network_and_a_nearly_treelike_one_separate_on_the_ratios()
    {
        var sparse = Golden("huttes");
        var looped = Golden("sakany");

        Reduced(sparse).GetProperty("cyclomaticNumber").GetInt32()
            .ShouldBeLessThan(Reduced(looped).GetProperty("cyclomaticNumber").GetInt32());

        sparse.GetProperty("howard").GetProperty("alpha").GetDouble()
            .ShouldBeLessThan(looped.GetProperty("howard").GetProperty("alpha").GetDouble());
        sparse.GetProperty("howard").GetProperty("beta").GetDouble()
            .ShouldBeLessThan(looped.GetProperty("howard").GetProperty("beta").GetDouble());

        sparse.GetProperty("degree").GetProperty("mean").GetDouble()
            .ShouldBeLessThan(looped.GetProperty("degree").GetProperty("mean").GetDouble());
    }

    private const double Tolerance = 1e-9;

    private static JsonElement Complete(JsonElement golden) => golden.GetProperty("completeGraph");

    private static JsonElement Reduced(JsonElement golden) => golden.GetProperty("reducedGraph");

    private static JsonElement Golden(string network)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Karstnet", network + ".golden.json");

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }
}
