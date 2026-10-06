// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How large a cell of a raster held in degrees is on the ground, and what is done about it.
/// </summary>
public sealed class TerrainGroundScaleTests
{
    /// <summary>
    /// The length of a degree, against the figures every geodesy reference tabulates for the
    /// ellipsoid the rasters are placed on.
    /// </summary>
    /// <remarks>
    /// Published to the metre, so that is the tolerance. What these guard is not the fifth
    /// significant figure but the formula: a degree of longitude taken from a sphere, or the two
    /// radii of curvature exchanged, is out by hundreds of metres and passes any test that only
    /// asks whether the number shrinks towards the pole.
    /// </remarks>
    [Theory]
    [InlineData(0d, 110_574d, 111_320d)]
    [InlineData(15d, 110_649d, 107_550d)]
    [InlineData(30d, 110_852d, 96_486d)]
    [InlineData(45d, 111_132d, 78_847d)]
    [InlineData(60d, 111_412d, 55_800d)]
    [InlineData(75d, 111_618d, 28_902d)]
    public void A_degree_spans_what_the_ellipsoid_says_it_spans(
        double latitude, double metresOfLatitude, double metresOfLongitude)
    {
        TerrainGroundScale.MetresPerDegreeOfLatitude(latitude).ShouldBe(metresOfLatitude, 1d);
        TerrainGroundScale.MetresPerDegreeOfLongitude(latitude).ShouldBe(metresOfLongitude, 1d);

        // The ground is the same either side of the equator.
        TerrainGroundScale.MetresPerDegreeOfLongitude(-latitude).ShouldBe(metresOfLongitude, 1d);
    }

    [Fact]
    public void A_cell_at_the_pole_still_has_a_width()
    {
        // No width at all would be a division by zero wherever a height is divided by it, and that
        // arrives as a raster of infinities rather than as an error.
        TerrainGroundScale.MetresPerDegreeOfLongitude(90d).ShouldBeGreaterThan(100d);
        double.IsFinite(TerrainGroundScale.CellStretch(90d, 0.001, 0.001)).ShouldBeTrue();
    }

    [Fact]
    public void A_cell_as_tall_as_wide_in_degrees_is_taller_than_wide_on_the_ground()
    {
        TerrainGroundScale.CellStretch(0d, 0.001, 0.001).ShouldBe(110_574d / 111_320d, 1e-4);
        TerrainGroundScale.CellStretch(46d, 0.001, 0.001).ShouldBe(1.4349, 1e-3);
        TerrainGroundScale.CellStretch(60d, 0.001, 0.001).ShouldBe(111_412d / 55_800d, 1e-4);

        // And a cell already drawn narrower in degrees by that much is square.
        TerrainGroundScale.CellStretch(60d, 0.001 * 111_412d / 55_800d, 0.001).ShouldBe(1d, 1e-4);
    }

    /// <summary>
    /// North, south, east and west stay where they are, whatever shape the cell is.
    /// </summary>
    /// <remarks>
    /// Which is why the fault this corrects went unseen on every slope facing one of the four:
    /// only the directions between them are turned.
    /// </remarks>
    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(270d)]
    public void The_four_compass_points_are_not_turned(double facing)
        => TerrainGroundScale.FacingOnTheGround(facing, 1.4349).ShouldBe(facing, 1e-9);

    /// <summary>
    /// A direction between the compass points is turned towards east or west by exactly what the
    /// shape of the cell took away.
    /// </summary>
    /// <remarks>
    /// Worked the other way round from the function under test: ground facing a known direction
    /// is flattened east–west by the stretch, which is what counting on square cells does to it,
    /// and the function has to give the known direction back.
    /// </remarks>
    [Theory]
    [InlineData(60d, 1.4349)]
    [InlineData(135d, 1.4349)]
    [InlineData(225d, 1.9966)]
    [InlineData(300d, 1.9966)]
    [InlineData(10d, 3.5)]
    [InlineData(359d, 1.2)]
    public void A_direction_between_them_is_turned_back_to_where_the_ground_faces(
        double onTheGround, double stretch)
    {
        var radians = onTheGround * Math.PI / 180d;
        var counted = Math.Atan2(Math.Sin(radians) / stretch, Math.Cos(radians)) * 180d / Math.PI;
        counted = counted < 0d ? counted + 360d : counted;

        TerrainGroundScale.FacingOnTheGround(counted, stretch).ShouldBe(onTheGround, 1e-9);
    }

    [Fact]
    public void A_facing_is_always_a_compass_bearing()
    {
        for (var counted = 0d; counted < 360d; counted += 0.37d)
        {
            var facing = TerrainGroundScale.FacingOnTheGround(counted, 1.7);
            facing.ShouldBeGreaterThanOrEqualTo(0d);
            facing.ShouldBeLessThan(360d);
        }
    }

    [Fact]
    public void Square_cells_need_no_correction()
        => TerrainGroundScale.FacingOnTheGround(123.4d, 1d).ShouldBe(123.4d, 1e-9);

    /// <summary>
    /// The runs of rows cover every row exactly once, in order, and none is taller than the limit.
    /// </summary>
    [Theory]
    [InlineData(3600, 3600, 1d / 3600d)]
    [InlineData(3600, 3601, 1d / 3600d)]
    [InlineData(24, 24, 0.001)]
    [InlineData(48, 1200, 15d / 3600d)]
    [InlineData(10, 7, 0.25)]
    public void The_runs_of_rows_cover_a_raster_once_and_stay_under_the_limit(
        int width, int height, double cellHeight)
    {
        var bands = TerrainGroundScale.RowBands(width, height, cellHeight);

        var next = 0;
        foreach (var band in bands)
        {
            band.FirstRow.ShouldBe(next);
            band.RowCount.ShouldBeGreaterThan(0);
            next += band.RowCount;

            // One row is always allowed, however tall its cells are: there is nothing smaller to
            // compute.
            if (band.RowCount > 1)
            {
                (band.RowCount * cellHeight).ShouldBeLessThanOrEqualTo(
                    TerrainGroundScale.TallestBandDegrees + 1e-12);
            }
        }

        next.ShouldBe(height);
    }

    [Fact]
    public void A_one_degree_raster_at_one_arc_second_is_computed_in_ten_runs()
        => TerrainGroundScale.RowBands(3600, 3600, 1d / 3600d).Count.ShouldBe(10);

    [Fact]
    public void A_raster_too_wide_for_a_tall_run_is_computed_in_shorter_ones()
    {
        // Wide enough that a tenth of a degree of it is more cells than a run may hold.
        var bands = TerrainGroundScale.RowBands(400_000, 3600, 1d / 3600d);

        bands[0].RowCount.ShouldBe((int)(TerrainGroundScale.MostCellsPerBand / 400_000));
        ((long)bands[0].RowCount * 400_000).ShouldBeLessThanOrEqualTo(TerrainGroundScale.MostCellsPerBand);
    }
}
