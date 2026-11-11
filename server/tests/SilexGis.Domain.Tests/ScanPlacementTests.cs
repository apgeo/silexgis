// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The rules a scan's placement follows before any projector or database is involved: what a
/// bearing is stored as, which placements are places on earth, and which point of the file the
/// tiles are written about.
/// </summary>
public class ScanPlacementTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(720, 0)]
    [InlineData(-0.0, 0)]
    [InlineData(90, 90)]
    [InlineData(-90, 270)]
    [InlineData(450, 90)]
    [InlineData(359.5, 359.5)]
    [InlineData(-359.5, 0.5)]
    public void A_bearing_is_stored_in_the_compass_round(double given, double stored) =>
        ScanPlacement.NormalizeHeading(given).ShouldBe(stored, tolerance: 1e-12);

    [Fact]
    public void A_manual_placement_keeps_its_position_and_turns_the_bearing_into_the_round()
    {
        var placed = ScanPlacement.Manual(25.4472, 45.5312, 952, -45);

        placed.Longitude.ShouldBe(25.4472);
        placed.Latitude.ShouldBe(45.5312);
        placed.HeightM.ShouldBe(952);
        placed.HeadingDeg.ShouldBe(315);
        placed.Source.ShouldBe(ScanPlacementSource.Manual);
    }

    [Fact]
    public void A_placement_from_the_file_s_grid_points_the_tiles_at_grid_north()
    {
        // The tiles keep the grid's north because nothing rewrote them, and grid north is the
        // convergence clockwise of true north: a positive convergence is a positive heading, not
        // the counter-turn a mesh conversion applies to its vertices.
        var placed = ScanPlacement.FromFileCrs(26.1, 45.2, 1100, 1.25);

        placed.HeadingDeg.ShouldBe(1.25, tolerance: 1e-12);
        placed.Source.ShouldBe(ScanPlacementSource.FileCrs);

        ScanPlacement.FromFileCrs(26.1, 45.2, 1100, -1.25).HeadingDeg.ShouldBe(358.75, tolerance: 1e-12);
    }

    [Theory]
    [InlineData(25.4, 45.5, 952, true)]
    [InlineData(-180, -90, -500, true)]
    [InlineData(180, 90, 9000, true)]
    [InlineData(180.001, 45, 952, false)]
    [InlineData(25, 90.001, 952, false)]
    [InlineData(25, 45, -501, false)]
    [InlineData(25, 45, 9001, false)]
    [InlineData(double.NaN, 45, 952, false)]
    [InlineData(25, double.PositiveInfinity, 952, false)]
    [InlineData(25, 45, double.NaN, false)]
    public void A_place_on_earth_is_inside_the_world_and_the_band_caves_are_found_in(
        double longitude, double latitude, double heightM, bool onEarth) =>
        ScanPlacement.IsOnEarth(longitude, latitude, heightM).ShouldBe(onEarth);

    [Fact]
    public void The_tiles_zero_point_is_what_the_worker_subtracted_and_nothing_before_tiling()
    {
        var header = new PointCloudFacts
        {
            LasVersion = "1.4",
            Scale = [0.01, 0.01, 0.01],
            Offset = [0, 0, 0],
            Min = [600_000, 5_040_000, 900],
            Max = [600_200, 5_040_300, 960],
        };

        // Before tiling there is no frame: the midpoint of the header's extent is not what the
        // tiles will be written about, because the worker chooses that point when it tiles.
        ScanPlacement.TilesZeroPoint(header).ShouldBeNull();

        var tiled = header with { OriginShift = [600_100, 5_040_150, 930] };
        ScanPlacement.TilesZeroPoint(tiled).ShouldBe((600_100d, 5_040_150d, 930d));

        // A shift that is not three finite numbers is no frame either.
        ScanPlacement.TilesZeroPoint(header with { OriginShift = [1, 2] }).ShouldBeNull();
        ScanPlacement.TilesZeroPoint(header with { OriginShift = [1, double.NaN, 3] }).ShouldBeNull();
    }
}
