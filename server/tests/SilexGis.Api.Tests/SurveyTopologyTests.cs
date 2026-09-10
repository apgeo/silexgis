// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Geometry;

namespace SilexGis.Api.Tests;

/// <summary>
/// The topology figures, measured against the reference implementation's own output and against
/// two networks built to differ in one respect only.
/// </summary>
/// <remarks>
/// <para>
/// Two things have to hold and neither implies the other. Matching the reference on a published
/// network proves the arithmetic is the arithmetic the literature means — it is the only way to
/// settle which reading of a corrected published method is the right one, because both readings
/// produce numbers that look entirely reasonable. Separating a maze from a branchwork proves the
/// numbers say something about caves: an implementation can match a reference on one network and
/// still be reporting figures that do not distinguish the two shapes the figures exist to tell
/// apart.
/// </para>
/// <para>
/// The reference implementation is Python and is a developer tool only. Its output is committed
/// beside the networks it was computed from, so this suite runs on a machine with no Python.
/// </para>
/// </remarks>
public class SurveyTopologyTests
{
    /// <summary>
    /// Every figure the reference reports for the two published networks, against the same figure
    /// computed here. A tolerance rather than an exact comparison because the two implementations
    /// sum thousands of leg lengths in different orders, not because the definitions are allowed
    /// to differ.
    /// </summary>
    [Theory]
    [InlineData("Huttes", "huttes")]
    [InlineData("Sakany", "sakany")]
    public void The_figures_match_the_reference_implementation_on_the_published_networks(
        string network, string goldenName)
    {
        var golden = Golden(goldenName);
        var measured = Measure(ReferenceNetwork(network));
        measured.ShouldNotBeNull();

        var complete = golden.GetProperty("completeGraph");
        measured.NodeCount.ShouldBe(complete.GetProperty("nodeCount").GetInt32());
        measured.EdgeCount.ShouldBe(complete.GetProperty("edgeCount").GetInt32());
        measured.ComponentCount.ShouldBe(complete.GetProperty("componentCount").GetInt32());

        var reduced = golden.GetProperty("reducedGraph");
        measured.ReducedNodeCount.ShouldBe(reduced.GetProperty("nodeCount").GetInt32());
        measured.ReducedEdgeCount.ShouldBe(reduced.GetProperty("edgeCount").GetInt32());
        measured.ReducedComponentCount.ShouldBe(reduced.GetProperty("componentCount").GetInt32());
        measured.CyclomaticNumber.ShouldBe(reduced.GetProperty("cyclomaticNumber").GetInt32());
        measured.ExtremityCount.ShouldBe(reduced.GetProperty("extremityCount").GetInt32());
        measured.JunctionCount.ShouldBe(reduced.GetProperty("junctionCount").GetInt32());

        var howard = golden.GetProperty("howard");
        measured.Alpha.ShouldNotBeNull().ShouldBe(howard.GetProperty("alpha").GetDouble(), Tolerance);
        measured.Beta.ShouldNotBeNull().ShouldBe(howard.GetProperty("beta").GetDouble(), Tolerance);
        measured.Gamma.ShouldNotBeNull().ShouldBe(howard.GetProperty("gamma").GetDouble(), Tolerance);

        var degree = golden.GetProperty("degree");
        measured.MeanDegree.ShouldNotBeNull().ShouldBe(degree.GetProperty("mean").GetDouble(), Tolerance);
        measured.DegreeStandardDeviation.ShouldNotBeNull()
            .ShouldBe(degree.GetProperty("standardDeviation").GetDouble(), Tolerance);
        measured.DegreeCoefficientOfVariation.ShouldNotBeNull()
            .ShouldBe(degree.GetProperty("coefficientOfVariation").GetDouble(), Tolerance);

        var branches = golden.GetProperty("branches");
        measured.BranchCount.ShouldBe(branches.GetProperty("count").GetInt32());
        measured.LoopingBranchCount.ShouldBe(branches.GetProperty("loopingCount").GetInt32());
        measured.MinBranchLengthM.ShouldNotBeNull().ShouldBe(branches.GetProperty("minLengthM").GetDouble(), Tolerance);
        measured.MaxBranchLengthM.ShouldNotBeNull().ShouldBe(branches.GetProperty("maxLengthM").GetDouble(), Tolerance);

        var metrics = golden.GetProperty("metrics");
        measured.MeanBranchLengthM.ShouldNotBeNull().ShouldBe(metrics.GetProperty("meanLengthM").GetDouble(), Tolerance);
        measured.BranchLengthCoefficientOfVariation.ShouldNotBeNull()
            .ShouldBe(metrics.GetProperty("lengthCoefficientOfVariation").GetDouble(), Tolerance);
        measured.LengthEntropy.ShouldNotBeNull().ShouldBe(metrics.GetProperty("lengthEntropy").GetDouble(), Tolerance);
        measured.OrientationEntropy.ShouldNotBeNull()
            .ShouldBe(metrics.GetProperty("orientationEntropy").GetDouble(), Tolerance);
        measured.MeanTortuosity.ShouldNotBeNull().ShouldBe(metrics.GetProperty("meanTortuosity").GetDouble(), Tolerance);
        measured.AverageShortestPathLength.ShouldNotBeNull()
            .ShouldBe(metrics.GetProperty("averageShortestPathLength").GetDouble(), Tolerance);
        measured.CentralPointDominance.ShouldNotBeNull()
            .ShouldBe(metrics.GetProperty("centralPointDominance").GetDouble(), Tolerance);
        measured.CorrelationOfVertexDegree.ShouldNotBeNull()
            .ShouldBe(metrics.GetProperty("correlationOfVertexDegree").GetDouble(), Tolerance);

        measured.AverageClusteringCoefficient.ShouldNotBeNull().ShouldBe(
            golden.GetProperty("graphLibraryOnly").GetProperty("averageClusteringCoefficient").GetDouble(),
            Tolerance);
    }

    /// <summary>
    /// A maze and a branchwork of the same size and the same passage length, differing only in how
    /// the passages are joined. Every figure that exists to tell those two apart must do so; a
    /// suite that only matched a reference could pass while reporting numbers that say nothing.
    /// </summary>
    [Fact]
    public void A_maze_and_a_branchwork_separate_on_the_figures_that_exist_to_tell_them_apart()
    {
        var maze = Measure(Maze()).ShouldNotBeNull();
        var branchwork = Measure(Branchwork()).ShouldNotBeNull();

        // A branchwork has no loop at all; a maze is nothing but loops.
        branchwork.CyclomaticNumber.ShouldBe(0);
        maze.CyclomaticNumber.ShouldBeGreaterThan(5);

        branchwork.Alpha.ShouldNotBeNull().ShouldBe(0.0);
        maze.Alpha.ShouldNotBeNull().ShouldBeGreaterThan(branchwork.Alpha!.Value);
        maze.Beta.ShouldNotBeNull().ShouldBeGreaterThan(branchwork.Beta.ShouldNotBeNull());
        maze.Gamma.ShouldNotBeNull().ShouldBeGreaterThan(branchwork.Gamma.ShouldNotBeNull());

        // A maze's junctions are joined to other junctions; a branchwork's are mostly joined to
        // the dead ends that hang off them.
        maze.MeanDegree.ShouldNotBeNull().ShouldBeGreaterThan(branchwork.MeanDegree.ShouldNotBeNull());
        maze.ExtremityCount.ShouldBeLessThan(branchwork.ExtremityCount);

        // Everything in a branchwork passes through its trunk; in a maze there is always another
        // way round, so no junction dominates.
        maze.CentralPointDominance.ShouldNotBeNull()
            .ShouldBeLessThan(branchwork.CentralPointDominance.ShouldNotBeNull());

        // The one figure that is not part of the published method, kept because it separates the
        // two shapes on its own: a maze's cells close, a tree's branches never meet again.
        maze.AverageClusteringCoefficient.ShouldNotBeNull().ShouldBeGreaterThan(0.0);
        branchwork.AverageClusteringCoefficient.ShouldNotBeNull().ShouldBe(0.0);
    }

    /// <summary>
    /// A file whose legs were all splays, or none of whose legs stood at a station, has no network
    /// to measure and is answered with nothing rather than with a row of zeroes — zero junctions
    /// would be a claim about the cave.
    /// </summary>
    [Fact]
    public void A_file_with_no_network_is_measured_as_nothing_rather_than_as_zero()
    {
        var stations = new List<CaveStation>
        {
            new() { Id = 1, Name = "a", Position = new CaveVector3(0, 0, 0) },
            new() { Id = 2, Name = "b", Position = new CaveVector3(10, 0, 0) },
        };
        var shots = new List<CaveShot>
        {
            new()
            {
                FromPosition = new CaveVector3(0, 0, 0),
                ToPosition = new CaveVector3(10, 0, 0),
                Flags = CaveShotFlags.Splay,
            },
        };

        Measure(new CaveModel { Stations = stations, Shots = shots }).ShouldBeNull();
    }

    /// <summary>
    /// A single straight passage: one branch, two dead ends, no loop. The smallest network whose
    /// every figure can be worked out by hand, so that an implementation that drifts on a large
    /// network fails here too rather than only on a number nobody can check.
    /// </summary>
    [Fact]
    public void One_straight_passage_is_one_branch_between_two_dead_ends()
    {
        var chain = Chain([
            new CaveVector3(0, 0, 0),
            new CaveVector3(0, 10, 0),
            new CaveVector3(0, 20, 0),
            new CaveVector3(0, 30, 0),
        ]);

        var measured = Measure(chain).ShouldNotBeNull();

        measured.NodeCount.ShouldBe(4);
        measured.EdgeCount.ShouldBe(3);
        measured.ReducedNodeCount.ShouldBe(2);
        measured.ReducedEdgeCount.ShouldBe(1);
        measured.CyclomaticNumber.ShouldBe(0);
        measured.ExtremityCount.ShouldBe(2);
        measured.JunctionCount.ShouldBe(0);
        measured.BranchCount.ShouldBe(1);
        measured.LoopingBranchCount.ShouldBe(0);
        measured.MeanBranchLengthM.ShouldNotBeNull().ShouldBe(30.0, Tolerance);
        measured.MeanTortuosity.ShouldNotBeNull().ShouldBe(1.0, Tolerance);
        measured.MeanDegree.ShouldNotBeNull().ShouldBe(1.0, Tolerance);
        measured.AverageShortestPathLength.ShouldNotBeNull().ShouldBe(1.0, Tolerance);

        // One branch has no spread to measure and one bearing has nothing to be spread over.
        measured.LengthEntropy.ShouldNotBeNull().ShouldBe(0.0);
        measured.OrientationEntropy.ShouldNotBeNull().ShouldBe(0.0);
    }

    /// <summary>
    /// A triangle of passage with a dead-end side passage off each corner, surveyed with an extra
    /// station part-way along every run so that the contraction has real work to do. Its every
    /// figure can be worked out on paper, and they are asserted as the literal numbers rather than
    /// against a stored answer.
    /// </summary>
    /// <remarks>
    /// The reason this exists beside the reference comparison: the reference output is a file in
    /// this repository, and a regeneration of it moves the expected numbers and the measured ones
    /// together. A ratio that drifted by a constant factor — dividing by three times the node count
    /// rather than by three times two fewer, say — would then pass the reference comparison and
    /// every relative assertion about a maze against a branchwork, because both sides moved. Hand
    /// arithmetic written out here does not move.
    ///
    /// Contracted, the network is six nodes and six connections in one piece: the three corners,
    /// each meeting three passages, and the three dead ends. So there is one independent loop
    /// (6 - 6 + 1), alpha is 1/7 (1 over twice six less five), beta is 1 (six over six), gamma is
    /// 0.5 (six over three times four), and an average node meets two passages (twelve ends over
    /// six nodes).
    /// </remarks>
    [Fact]
    public void A_triangle_with_a_side_passage_at_each_corner_matches_hand_arithmetic()
    {
        // Three corners, the mid-point of each side, then a mid-point and a dead end on each stub.
        var a = new CaveVector3(0, 0, 0);
        var b = new CaveVector3(30, 0, 0);
        var c = new CaveVector3(15, 26, 0);

        var positions = new List<CaveVector3>
        {
            a, b, c,
            new(15, 0, 0), new(22.5, 13, 0), new(7.5, 13, 0),
            new(0, -10, 0), new(0, -20, 0),
            new(40, 0, 0), new(50, 0, 0),
            new(15, 36, 0), new(15, 46, 0),
        };

        var legs = new List<(int, int)>
        {
            (0, 3), (3, 1), (1, 4), (4, 2), (2, 5), (5, 0),
            (0, 6), (6, 7),
            (1, 8), (8, 9),
            (2, 10), (10, 11),
        };

        var measured = Measure(Network(positions, legs)).ShouldNotBeNull();

        measured.NodeCount.ShouldBe(12);
        measured.EdgeCount.ShouldBe(12);
        measured.ComponentCount.ShouldBe(1);

        measured.ReducedNodeCount.ShouldBe(6);
        measured.ReducedEdgeCount.ShouldBe(6);
        measured.ReducedComponentCount.ShouldBe(1);
        measured.CyclomaticNumber.ShouldBe(1);
        measured.JunctionCount.ShouldBe(3);
        measured.ExtremityCount.ShouldBe(3);
        measured.BranchCount.ShouldBe(6);
        measured.LoopingBranchCount.ShouldBe(0);

        measured.Alpha.ShouldNotBeNull().ShouldBe(1.0 / 7.0, Tolerance);
        measured.Beta.ShouldNotBeNull().ShouldBe(1.0, Tolerance);
        measured.Gamma.ShouldNotBeNull().ShouldBe(0.5, Tolerance);
        measured.MeanDegree.ShouldNotBeNull().ShouldBe(2.0, Tolerance);
    }

    private const double Tolerance = 1e-8;

    private static SurveyTopology? Measure(CaveModel model) =>
        SurveyTopologyAnalyzer.Measure(
            CenterlineGraph.Build(model), Guid.Empty, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>A four-by-four grid of passages: every cell closes, nothing is a dead end.</summary>
    private static CaveModel Maze()
    {
        const int side = 4;
        var positions = new List<CaveVector3>();
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++) { positions.Add(new CaveVector3(x * 10, y * 10, 0)); }
        }

        var legs = new List<(int, int)>();
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int here = (y * side) + x;
                if (x + 1 < side) { legs.Add((here, here + 1)); }
                if (y + 1 < side) { legs.Add((here, here + side)); }
            }
        }
        return Network(positions, legs);
    }

    /// <summary>A trunk with side passages hanging off it, each ending: the same station count and
    /// the same total passage length as the maze, joined the other way.</summary>
    private static CaveModel Branchwork()
    {
        var positions = new List<CaveVector3>();
        var legs = new List<(int, int)>();

        // The trunk runs north; the tributaries leave it east and west alternately.
        const int trunk = 8;
        for (int i = 0; i < trunk; i++) { positions.Add(new CaveVector3(0, i * 10, 0)); }
        for (int i = 1; i < trunk; i++) { legs.Add((i - 1, i)); }

        for (int i = 1; i < trunk; i++)
        {
            int sign = i % 2 == 0 ? 1 : -1;
            positions.Add(new CaveVector3(sign * 10, i * 10, 0));
            legs.Add((i, positions.Count - 1));
        }
        return Network(positions, legs);
    }

    private static CaveModel Chain(IReadOnlyList<CaveVector3> points)
    {
        var legs = new List<(int, int)>();
        for (int i = 1; i < points.Count; i++) { legs.Add((i - 1, i)); }
        return Network(points, legs);
    }

    private static CaveModel Network(IReadOnlyList<CaveVector3> positions, IReadOnlyList<(int From, int To)> legs)
    {
        var stations = positions
            .Select((p, i) => new CaveStation
            {
                Id = (uint)(i + 1),
                Name = "s" + i.ToString(CultureInfo.InvariantCulture),
                Position = p,
            })
            .ToList();

        var shots = legs
            .Select(l => new CaveShot { FromPosition = positions[l.From], ToPosition = positions[l.To] })
            .ToList();

        return new CaveModel { Stations = stations, Shots = shots };
    }

    /// <summary>
    /// One of the published networks, read from the node and link files committed beside the
    /// reference output. Nodes are one coordinate triple per line; links name the two nodes they
    /// join, counting from one.
    /// </summary>
    private static CaveModel ReferenceNetwork(string network)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Karstnet");

        var positions = File.ReadAllLines(Path.Combine(directory, network + "_nodes.dat"))
            .Where(line => line.Trim().Length > 0)
            .Select(line =>
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return new CaveVector3(
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture));
            })
            .ToList();

        var legs = File.ReadAllLines(Path.Combine(directory, network + "_links.dat"))
            .Where(line => line.Trim().Length > 0)
            .Select(line =>
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return (int.Parse(parts[0], CultureInfo.InvariantCulture) - 1,
                        int.Parse(parts[1], CultureInfo.InvariantCulture) - 1);
            })
            .ToList();

        return Network(positions, legs);
    }

    private static JsonElement Golden(string network)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Karstnet", network + ".golden.json");

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }
}
