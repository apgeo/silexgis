// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class DensityGridTests
{
    [Fact]
    public void The_finest_publishable_cell_is_the_protection_grid_itself()
    {
        DensityGrid.MinimumCellMeters(5000).ShouldBe(5000);

        // Exactly at the floor is permitted; a hair under it is not. Stated as a pair because a
        // rule written with the comparison the wrong way round would still pass either half alone.
        DensityGrid.IsCellPermitted(5000, 5000).ShouldBeTrue();
        DensityGrid.IsCellPermitted(4999.999, 5000).ShouldBeFalse();
        DensityGrid.IsCellPermitted(50_000, 5000).ShouldBeTrue();

        // And it moves with the setting rather than with a number written here.
        DensityGrid.IsCellPermitted(5000, 20_000).ShouldBeFalse();
        DensityGrid.IsCellPermitted(20_000, 20_000).ShouldBeTrue();

        DensityGrid.IsCellPermitted(double.NaN, 5000).ShouldBeFalse();
        DensityGrid.IsCellPermitted(0, 5000).ShouldBeFalse();
        DensityGrid.IsCellPermitted(-5000, 5000).ShouldBeFalse();
    }

    [Fact]
    public void A_cell_at_the_floor_cannot_separate_two_points_the_protection_rule_merged()
    {
        // This is the property the floor exists for: whatever a snapped coordinate is, the cell it
        // lands in is decided by the same rounding that produced it, so two coordinates the
        // protection rule made identical stay identical here.
        const double grid = 5000;
        var cell = DensityGrid.CellDegrees(DensityGrid.MinimumCellMeters(grid));

        var a = new Point(25.44721, 45.53127) { SRID = 4326 };
        var b = new Point(25.46, 45.54) { SRID = 4326 };

        var snappedA = LocationProtection.Snap(a, grid);
        var snappedB = LocationProtection.Snap(b, grid);
        snappedA.X.ShouldBe(snappedB.X, 1e-12);
        snappedA.Y.ShouldBe(snappedB.Y, 1e-12);

        DensityGrid.CellIndex(snappedA.X, cell).ShouldBe(DensityGrid.CellIndex(snappedB.X, cell));
        DensityGrid.CellIndex(snappedA.Y, cell).ShouldBe(DensityGrid.CellIndex(snappedB.Y, cell));

        // And the snap does not move a point out of the cell its exact position falls in, which is
        // what makes the count the same for a caller who may place it and one who may not.
        DensityGrid.CellIndex(snappedA.X, cell).ShouldBe(DensityGrid.CellIndex(a.X, cell));
        DensityGrid.CellIndex(snappedA.Y, cell).ShouldBe(DensityGrid.CellIndex(a.Y, cell));
    }

    [Fact]
    public void Cell_indices_are_anchored_at_zero_rather_than_at_the_window()
    {
        var cell = DensityGrid.CellDegrees(5000);

        // The same coordinate asked about from two different windows is in the same cell — there
        // is no window in the arithmetic at all, which is the point.
        DensityGrid.CellIndex(25.44721, cell).ShouldBe(DensityGrid.CellIndex(25.44721, cell));
        DensityGrid.CellIndex(0, cell).ShouldBe(0);
        DensityGrid.CellIndex(cell * 0.5, cell).ShouldBe(1);      // half rounds away from zero
        DensityGrid.CellIndex(-cell * 0.5, cell).ShouldBe(-1);
        DensityGrid.CellIndex(cell * 2.4, cell).ShouldBe(2);
    }

    [Fact]
    public void A_window_reports_how_many_cells_it_would_build_before_it_builds_any()
    {
        var cell = DensityGrid.CellDegrees(5000);

        var (first, last) = DensityGrid.CellRange(0, cell * 3, cell);
        first.ShouldBe(0);
        last.ShouldBe(3);

        DensityGrid.CellCount(0, 0, cell * 3, cell * 3, cell).ShouldBe(16);

        // A window narrower than one cell is still one cell, never none.
        DensityGrid.CellCount(0, 0, cell * 0.01, cell * 0.01, cell).ShouldBe(1);

        // The whole world at the finest permitted cell is what the count is checked against.
        DensityGrid.CellCount(-180, -90, 180, 90, cell).ShouldBeGreaterThan(5_000);
    }

    [Fact]
    public void An_impossibly_wide_window_counts_as_impossibly_many_cells_rather_than_wrapping()
    {
        var cell = DensityGrid.CellDegrees(5000);

        // A window this size is not a window, but nothing stops a caller sending one, and the
        // count is what every caller gates on. Multiplied as whole numbers the two spans overflow
        // and come back *negative*, so a guard reading "more cells than we serve?" says no and the
        // enormous query it exists to refuse is run instead. The count therefore saturates.
        var count = DensityGrid.CellCount(-70_000_000, -70_000_000, 70_000_000, 70_000_000, cell);

        count.ShouldBe(long.MaxValue);
        count.ShouldBeGreaterThan(5_000);
    }

    [Fact]
    public void The_kernel_spreads_a_count_over_its_neighbourhood_and_conserves_it()
    {
        var cell = DensityGrid.CellDegrees(5000);
        var bandwidth = 10_000d;

        // Twenty-one cells in a row on the equator, with everything in the middle one.
        var cells = Enumerable.Range(-10, 21)
            .Select(i => (i * cell, 0d, i == 0 ? 8 : 0))
            .ToList();

        var surface = DensityGrid.KernelSurface(cells, bandwidth);

        // The peak is at the cell that holds the count, and it falls away on both sides. A kernel
        // that had simply copied the counts would give nought either side of the middle.
        surface[10].ShouldBeGreaterThan(surface[11]);
        surface[11].ShouldBeGreaterThan(surface[12]);
        surface[11].ShouldBeGreaterThan(0d);
        surface[10].ShouldBe(surface[10]);
        surface[9].ShouldBe(surface[11], 1e-9);

        // And it is a density, not a count: the peak is the eight caves spread over a kernel of
        // this width, which is what a Gaussian of this bandwidth puts at its own centre.
        var expectedPeak = 8d * 1_000_000d / (2d * Math.PI * bandwidth * bandwidth);
        surface[10].ShouldBe(expectedPeak, expectedPeak * 0.01);

        // An empty grid is a flat nought rather than a division by nothing.
        DensityGrid.KernelSurface([(0d, 0d, 0)], bandwidth).ShouldBe([0d]);
    }

    [Fact]
    public void The_kernel_is_never_narrower_than_the_cell_the_counts_were_binned_into()
    {
        // Smoothing cannot recover what the binning removed, so the floor under the bandwidth is
        // the cell — not the protection grid, because the cell is already at least that wide.
        DensityGrid.MinimumBandwidthMeters(5000).ShouldBe(5000);
        DensityGrid.MinimumBandwidthMeters(20_000).ShouldBe(20_000);
        DensityGrid.DefaultBandwidthMeters(5000).ShouldBeGreaterThan(DensityGrid.MinimumBandwidthMeters(5000));
    }
}
