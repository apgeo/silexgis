// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class SpatialAutocorrelationTests
{
    [Fact]
    public void A_grid_split_into_a_full_half_and_an_empty_one_reads_as_clustered()
    {
        // Known by construction: like sits beside like everywhere except along one seam.
        var cells = Lattice(10, 10, (x, _) => x < 5 ? 10d : 0d);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Clustered);
        result.Global.Index!.Value.ShouldBeGreaterThan(0.5d);
        result.Global.ZScore!.Value.ShouldBeGreaterThan(SpatialAutocorrelation.SignificanceZ);
        result.Global.PValue!.Value.ShouldBeLessThan(0.05d);
    }

    [Fact]
    public void A_regularly_spaced_arrangement_reads_as_dispersed()
    {
        // The opposite arrangement, and the reason both halves of the pair are asserted: a
        // statistic that returned a constant would pass the clustered test alone. Full cells sit
        // one clear cell apart in every direction, so no full cell touches another and unlike
        // sits beside unlike everywhere.
        var cells = Lattice(12, 12, (x, y) => x % 3 == 1 && y % 3 == 1 ? 10d : 0d);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Dispersed);
        result.Global.Index!.Value.ShouldBeLessThan(-0.1d);
        result.Global.ZScore!.Value.ShouldBeLessThan(-SpatialAutocorrelation.SignificanceZ);
    }

    [Fact]
    public void A_chequerboard_reads_as_neither_because_its_diagonal_neighbours_match()
    {
        // Not a defect and worth pinning, because it is the one arrangement where the choice of
        // neighbourhood decides the answer. Corners count as neighbours here, and a chequerboard's
        // four diagonal neighbours carry the same value as the cell while its four side neighbours
        // carry the opposite, so the two cancel and there is genuinely nothing for this
        // neighbourhood to detect. A reading of "dispersed" here would mean the corners had been
        // dropped from the neighbour list, which would also break every hot patch that touches at
        // a corner into two.
        var cells = Lattice(10, 10, (x, y) => (x + y) % 2 == 0 ? 10d : 0d);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Random);
        Math.Abs(result.Global.Index!.Value).ShouldBeLessThan(0.2d);
    }

    [Fact]
    public void A_scatter_with_no_arrangement_reads_as_neither()
    {
        // A stated seed, and a generator written out here so the same seed means the same numbers
        // on every machine and every runtime: a fixture whose values do not reproduce is not a
        // fixture. Nothing about these values is spatial, so a working statistic must fail to
        // detect an arrangement in them.
        var random = new Lcg(20260909);
        var cells = Lattice(12, 12, (_, _) => random.Next());

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Random);
        Math.Abs(result.Global.ZScore!.Value).ShouldBeLessThan(SpatialAutocorrelation.SignificanceZ);
        result.Global.PValue!.Value.ShouldBeGreaterThan(0.05d);
    }

    [Fact]
    public void The_expectation_a_reading_is_compared_against_is_below_zero_not_at_it()
    {
        var cells = Lattice(6, 6, (x, _) => x);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.CellCount.ShouldBe(36);
        result.Global.ExpectedIndex!.Value.ShouldBe(-1d / 35d, 1e-12);
    }

    [Fact]
    public void A_surface_carrying_one_value_everywhere_is_undetermined_rather_than_random()
    {
        // "Nothing was tested" and "something was tested and came out like chance" are different
        // statements, and a constant surface is the first one.
        var cells = Lattice(6, 6, (_, _) => 3d);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Undetermined);
        result.Global.Index.ShouldBeNull();
        result.Global.ZScore.ShouldBeNull();
        result.HotSpotZScores.ShouldAllBe(z => z == null);
    }

    [Fact]
    public void Too_few_cells_to_test_are_refused_rather_than_answered()
    {
        var cells = Lattice(1, 3, (_, y) => y);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.Pattern.ShouldBe(SpatialPatternKind.Undetermined);
        result.Global.CellCount.ShouldBe(3);
        result.Global.Index.ShouldBeNull();
    }

    [Fact]
    public void The_smallest_window_publishes_no_reading_because_its_variance_collapses()
    {
        // Four cells in a two-by-two block: the smallest window this method accepts, and the one
        // where the randomisation variance is not merely small but exactly nought. Every cell
        // touches every other, so the weight moments cancel the expectation term term for term and
        // there is no scale left to measure a departure against. The window is reachable from an
        // ordinary request — a study area a couple of cells across returns exactly this — so the
        // combination has to be pinned: a reading published here with no z-score beside it would
        // be rendered as a tested one, and nought standard errors at p nought is the strongest
        // claim a surface can make.
        var cells = Lattice(2, 2, (x, y) => (x * 2) + y);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.CellCount.ShouldBe(4);
        result.Global.NeighbourPairCount.ShouldBe(12);
        result.Global.Pattern.ShouldBe(SpatialPatternKind.Undetermined);
        result.Global.Index.ShouldBeNull();
        result.Global.ZScore.ShouldBeNull();
        result.Global.PValue.ShouldBeNull();
    }

    [Fact]
    public void A_grid_offering_the_same_cell_twice_is_refused()
    {
        // Not a hypothetical tidiness rule. Keeping the first occurrence and letting the duplicate
        // look its own neighbours up would give it out-edges that nothing has in-edges to, so the
        // adjacency would stop being symmetric — and both weight moments the variance is computed
        // from are written as the reductions that hold only for a symmetric binary matrix. The
        // figures would then come from wrong moments with nothing in the answer recording it.
        var cells = Lattice(3, 3, (_, _) => 1d);
        cells.Add(new GridCellValue(1, 1, 5d));

        Should.Throw<ArgumentException>(() => SpatialAutocorrelation.Analyse(cells));
    }

    [Fact]
    public void Cells_the_grid_did_not_return_are_neighbours_of_nothing()
    {
        // Two blocks of four, far apart on the lattice. Every cell has exactly the three
        // neighbours inside its own block: twenty-four ordered adjacencies and no more, which is
        // only true if the gap between the blocks is treated as absent ground rather than as empty
        // cells that happen to hold nought.
        var cells = new List<GridCellValue>();
        foreach (var (ox, oy) in new[] { (0L, 0L), (50L, 50L) })
        {
            for (var dx = 0; dx < 2; dx++)
            {
                for (var dy = 0; dy < 2; dy++)
                {
                    cells.Add(new GridCellValue(ox + dx, oy + dy, dx + dy));
                }
            }
        }

        var result = SpatialAutocorrelation.Analyse(cells);

        result.Global.NeighbourPairCount.ShouldBe(24);
    }

    [Fact]
    public void The_hot_spots_are_one_reading_per_cell_given_and_never_more()
    {
        // The resolution property, asserted here so it is a rule of the arithmetic rather than a
        // habit of one caller: nothing in this statistic subdivides a cell or interpolates between
        // two, so a hot-spot surface can carry no detail its grid did not.
        var cells = Lattice(8, 8, (x, y) => x < 3 && y < 3 ? 12d : 0d);

        var result = SpatialAutocorrelation.Analyse(cells);

        result.HotSpotZScores.Count.ShouldBe(cells.Count);

        var hottest = IndexOfHighest(result.HotSpotZScores);
        cells[hottest].X.ShouldBeLessThan(3);
        cells[hottest].Y.ShouldBeLessThan(3);
        result.HotSpotZScores[hottest]!.Value
            .ShouldBeGreaterThan(SpatialAutocorrelation.SignificanceZ);

        // And the far corner, which no full cell touches, is cold rather than merely unremarkable.
        var farCorner = cells.FindIndex(c => c.X == 7 && c.Y == 7);
        result.HotSpotZScores[farCorner]!.Value.ShouldBeLessThan(0d);
    }

    private static List<GridCellValue> Lattice(int width, int height, Func<int, int, double> value)
    {
        var cells = new List<GridCellValue>(width * height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells.Add(new GridCellValue(x, y, value(x, y)));
            }
        }

        return cells;
    }

    private static int IndexOfHighest(IReadOnlyList<double?> scores)
    {
        var best = -1;
        for (var i = 0; i < scores.Count; i++)
        {
            if (scores[i] is { } z && (best < 0 || z > scores[best]!.Value))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>A linear congruential generator with stated constants, so the fixture reproduces.</summary>
    private struct Lcg(uint seed)
    {
        private uint state = seed;

        public double Next()
        {
            state = (state * 1664525u) + 1013904223u;
            return state / (double)uint.MaxValue;
        }
    }
}
