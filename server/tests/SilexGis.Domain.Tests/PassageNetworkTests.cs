// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The network counts a pattern suggestion is read from.
/// </summary>
public class PassageNetworkTests
{
    [Fact]
    public void A_chain_of_passage_is_a_tree_with_two_ends()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("B", "C"), ("C", "D")]);

        figures.ShouldNotBeNull();
        figures.NodeCount.ShouldBe(4);
        figures.EdgeCount.ShouldBe(3);
        figures.ComponentCount.ShouldBe(1);
        figures.CyclomaticNumber.ShouldBe(0);
        figures.ExtremityCount.ShouldBe(2);

        // The two stations in the middle are places the passage goes through, not places it meets
        // or ends, so the network reduces to its two ends.
        figures.ReducedNodeCount.ShouldBe(2);
    }

    [Fact]
    public void A_grid_closes_on_itself_once_per_square()
    {
        // Three by three: nine stations, twelve connections, four squares.
        var legs = new List<(string?, string?)>();
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                if (x + 1 < 3) legs.Add(($"{x}_{y}", $"{x + 1}_{y}"));
                if (y + 1 < 3) legs.Add(($"{x}_{y}", $"{x}_{y + 1}"));
            }
        }

        var figures = PassageNetwork.Measure(legs);

        figures.ShouldNotBeNull();
        figures.NodeCount.ShouldBe(9);
        figures.EdgeCount.ShouldBe(12);
        figures.CyclomaticNumber.ShouldBe(4);
        figures.ExtremityCount.ShouldBe(0);
    }

    /// <summary>
    /// A ring has no junction and no end, and reducing it to nothing would divide a real loop count
    /// by zero.
    /// </summary>
    [Fact]
    public void A_closed_ring_reduces_to_one_place_rather_than_to_none()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("B", "C"), ("C", "A")]);

        figures.ShouldNotBeNull();
        figures.CyclomaticNumber.ShouldBe(1);
        figures.ExtremityCount.ShouldBe(0);
        figures.ReducedNodeCount.ShouldBe(1);
    }

    [Fact]
    public void Separate_pieces_of_cave_are_counted_separately()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("C", "D")]);

        figures.ShouldNotBeNull();
        figures.ComponentCount.ShouldBe(2);
        figures.CyclomaticNumber.ShouldBe(0);
        figures.ExtremityCount.ShouldBe(4);
    }

    /// <summary>
    /// Two teams surveying the same passage measured one connection twice, not two connections. The
    /// network is the same shape either way, and counting the second would invent a loop.
    /// </summary>
    [Fact]
    public void A_passage_surveyed_twice_is_one_connection()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("B", "A"), ("A", "B")]);

        figures.ShouldNotBeNull();
        figures.EdgeCount.ShouldBe(1);
        figures.CyclomaticNumber.ShouldBe(0);
    }

    [Fact]
    public void A_leg_from_a_station_to_itself_joins_nothing()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("B", "B")]);

        figures.ShouldNotBeNull();
        figures.EdgeCount.ShouldBe(1);
        figures.CyclomaticNumber.ShouldBe(0);
    }

    /// <summary>
    /// Line work that was never a survey file has no station names, and a network measured over
    /// none of it is not measured at all. Reporting nought loops and nought ends would describe a
    /// cave nobody looked at.
    /// </summary>
    /// <summary>
    /// The figure the maze rules read: two passages leaving one junction that are themselves
    /// joined. A grid rings at every junction, and it does so through stations that sit between the
    /// junctions — which is why the figure is measured on the contracted network and would be
    /// nought if it were read off the raw stations.
    /// </summary>
    [Fact]
    public void A_grid_rings_at_its_junctions()
    {
        var legs = new List<(string?, string?)>();
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                if (x + 1 < 3) legs.Add(($"{x}_{y}", $"{x + 1}_{y}"));
                if (y + 1 < 3) legs.Add(($"{x}_{y}", $"{x}_{y + 1}"));
            }
        }

        var figures = PassageNetwork.Measure(legs);

        figures.ShouldNotBeNull();
        figures.Clustering.ShouldNotBeNull();
        figures.Clustering!.Value.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The same rule shown staying silent: three passages meeting at one place and going nowhere
    /// near each other again ring not at all. Nought here is a measurement — the junction was
    /// looked at and none of its pairs were joined — and it is why the rule reading this figure is
    /// a rule rather than a constant.
    /// </summary>
    [Fact]
    public void Passages_that_meet_once_and_never_again_do_not_ring()
    {
        var figures = PassageNetwork.Measure(
            [("J", "a1"), ("a1", "a2"), ("J", "b1"), ("b1", "b2"), ("J", "c1"), ("c1", "c2")]);

        figures.ShouldNotBeNull();
        figures.Clustering.ShouldBe(0);
    }

    /// <summary>
    /// A single corridor has no place where two passages meet, so it has no pairs to be joined or
    /// not joined. Reporting nought would be a measurement of something nobody could measure, and
    /// the rule that reads this figure would then fire nowhere while looking as if it had looked.
    /// </summary>
    [Fact]
    public void A_corridor_has_no_junction_to_ring()
    {
        var figures = PassageNetwork.Measure([("A", "B"), ("B", "C"), ("C", "D")]);

        figures.ShouldNotBeNull();
        figures.Clustering.ShouldBeNull();
    }

    /// <summary>
    /// A run of passage that leaves a junction and comes back to it joins that junction to nothing
    /// else, so it is dropped rather than recorded as a place joined to itself — which would read
    /// as a ring between two neighbouring passages the cave does not have. What is left is a
    /// junction with one other place to reach, which has no pair to compare and so no figure.
    /// </summary>
    [Fact]
    public void A_loop_hanging_off_one_junction_is_not_a_ring_between_passages()
    {
        var figures = PassageNetwork.Measure(
            [("J", "x"), ("x", "y"), ("y", "J"), ("J", "end")]);

        figures.ShouldNotBeNull();
        figures.CyclomaticNumber.ShouldBe(1);
        figures.Clustering.ShouldBeNull();
    }

    [Fact]
    public void Line_work_with_no_station_names_is_not_measured()
    {
        PassageNetwork.Measure([(null, null), ("A", null), (null, "B")]).ShouldBeNull();
        PassageNetwork.Measure([]).ShouldBeNull();
    }
}
