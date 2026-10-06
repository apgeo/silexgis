// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a picture of contour lines may be asked to be, and how large one is drawn.
/// </summary>
public sealed class TerrainContourLinesTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2.5d)]
    [InlineData(20d)]
    [InlineData(1000d)]
    public void A_spacing_from_a_metre_to_a_thousand_can_be_drawn(double interval)
        => TerrainContourLines.Problem(interval).ShouldBeNull();

    /// <remarks>
    /// Nothing and less than nothing have no lines to draw; a spacing past the relief of any
    /// ground draws a finished picture with no line on it; and a spacing that is not a number at
    /// all would otherwise reach the tracer as one.
    /// </remarks>
    [Theory]
    [InlineData(0d)]
    [InlineData(0.99d)]
    [InlineData(-20d)]
    [InlineData(1000.5d)]
    [InlineData(1e9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_spacing_outside_that_is_refused(double interval)
        => TerrainContourLines.Problem(interval).ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void A_picture_is_drawn_twice_as_fine_as_the_elevation_it_is_traced_from()
    {
        TerrainContourLines.PictureSize(120, 80).ShouldBe((240, 160));

        // A one-degree raster at one arc second, which is what the limit was sized to allow.
        TerrainContourLines.PictureSize(3600, 3600).ShouldBe((7200, 7200));
    }

    /// <summary>
    /// No picture exceeds the limit, whatever it is traced from, and it keeps its proportions.
    /// </summary>
    [Theory]
    [InlineData(5000, 5000)]
    [InlineData(18_000, 18_000)]
    [InlineData(40_000, 2000)]
    [InlineData(3000, 30_000)]
    public void A_large_raster_is_drawn_less_fine_so_the_picture_stays_within_the_limit(
        int width, int height)
    {
        var (across, down) = TerrainContourLines.PictureSize(width, height);

        ((long)across * down).ShouldBeLessThanOrEqualTo(TerrainContourLines.MostCells);

        // As close to the limit as whole cells allow: the limit is a ceiling, not a target missed
        // by half.
        ((long)(across + 2) * (down + 2)).ShouldBeGreaterThan(
            Math.Min(TerrainContourLines.MostCells, 4L * width * height));

        // The same shape as the elevation, to within the rounding of one cell.
        ((double)across / down).ShouldBe((double)width / height, (double)width / height * 0.001);
    }

    /// <summary>
    /// A raster one cell wide still has a picture, and the limit still holds for it.
    /// </summary>
    /// <remarks>
    /// The one shape whose proportions cannot be kept: a side of a single cell has nothing left to
    /// give up, so the other side gives up all of it.
    /// </remarks>
    [Theory]
    [InlineData(1, 100_000_000)]
    [InlineData(100_000_000, 1)]
    [InlineData(1, 1)]
    public void A_raster_a_single_cell_across_still_has_a_picture_within_the_limit(int width, int height)
    {
        var (across, down) = TerrainContourLines.PictureSize(width, height);

        across.ShouldBeGreaterThan(0);
        down.ShouldBeGreaterThan(0);
        ((long)across * down).ShouldBeLessThanOrEqualTo(TerrainContourLines.MostCells);
    }
}
