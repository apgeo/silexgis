// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Whether a raster's grid actually places its pixels anywhere.
/// </summary>
/// <remarks>
/// The case worth naming is the identity, because it is the one that does not look like a failure.
/// A raster library asked for the grid of a file that carries none does not refuse — it answers
/// with pixels one unit wide starting at the origin, and reports success. A check that only looks
/// for a zero-width pixel therefore passes the file straight through, and it is drawn at one degree
/// per pixel off the west coast of Africa with nothing said anywhere. That is exactly what a scan
/// re-saved out of a GeoTIFF by an image editor looks like: the projection tag survives, the tie
/// points do not.
/// </remarks>
public sealed class RasterPlacementTests
{
    /// <summary>A real placement is left alone, including ones that look unusual.</summary>
    [Theory]
    // A degree grid over Romania: small pixels, north-up, origin off the prime meridian.
    [InlineData(new double[] { 21.5, 0.0002, 0, 46.75, 0, -0.0002 })]
    // A projected grid in metres, which has a pixel size of exactly 1 — the number that makes the
    // identity tempting to test for on its own.
    [InlineData(new double[] { 425000, 1, 0, 5175000, 0, -1 })]
    // A rotated grid. Uncommon, and still a placement.
    [InlineData(new double[] { 21.5, 0.0002, 0.00001, 46.75, 0.00001, -0.0002 })]
    // The identity in every respect but one: the origin has moved, so somebody placed it.
    [InlineData(new double[] { 1, 1, 0, 0, 0, 1 })]
    public void A_grid_that_places_its_pixels_is_accepted(double[] geoTransform) =>
        RasterPlacement.IsUnplaced(geoTransform).ShouldBeFalse();

    /// <summary>The identity — what a reader answers for a file that carries no grid at all.</summary>
    [Fact]
    public void The_identity_grid_is_refused()
    {
        RasterPlacement.IsUnplaced([0, 1, 0, 0, 0, 1]).ShouldBeTrue();

        // Stated separately because this is the regression that mattered: the uploaded-raster path
        // tested only for a zero-width pixel, and the identity has a pixel width of one.
        var identity = new double[] { 0, 1, 0, 0, 0, 1 };
        (identity[1] == 0 && identity[2] == 0).ShouldBeFalse(
            "the identity passes the zero-width test, which is why that test alone was not enough");
    }

    /// <summary>A degenerate grid: pixels with no width at all.</summary>
    [Theory]
    [InlineData(new double[] { 0, 0, 0, 0, 0, 0 })]
    [InlineData(new double[] { 21.5, 0, 0, 46.75, 0, -0.0002 })]
    public void A_grid_with_no_pixel_width_is_refused(double[] geoTransform) =>
        RasterPlacement.IsUnplaced(geoTransform).ShouldBeTrue();

    /// <summary>
    /// Nothing at all, or fewer numbers than a grid has, is treated as unplaced rather than as a
    /// fault — a caller holding four numbers has no placement either, and throwing here would turn
    /// a refusable upload into a server error.
    /// </summary>
    [Fact]
    public void An_absent_or_short_grid_is_refused_rather_than_thrown_over()
    {
        RasterPlacement.IsUnplaced(null).ShouldBeTrue();
        RasterPlacement.IsUnplaced([]).ShouldBeTrue();
        RasterPlacement.IsUnplaced([0, 1, 0, 0, 0]).ShouldBeTrue();
    }
}
