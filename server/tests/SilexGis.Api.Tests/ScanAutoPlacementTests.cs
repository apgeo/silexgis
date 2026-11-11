// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// A scan placed from the coordinate system its file named, against the real projector: where
/// the tiles' zero point lands, which way the tiles face, when the file gives no placement at
/// all, and what a later tiling does to a placement somebody corrected by hand. No database.
/// </summary>
public class ScanAutoPlacementTests
{
    /// <summary>UTM zone 35N; an easting east of its central meridian, so the grid is visibly turned.</summary>
    private const int Utm35N = 32635;
    private const double Easting = 600_000;
    private const double Northing = 5_040_000;

    private static readonly ProjCoordinateProjector Projector = new();

    /// <summary>
    /// UTM zone 35N spelled out as well-known text, as a LAS 1.4 writer puts it in the header,
    /// with no authority named: the system has to be recognised from its definition.
    /// </summary>
    private const string Utm35NWkt =
        """PROJCS["WGS 84 / UTM zone 35N",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Transverse_Mercator"],PARAMETER["latitude_of_origin",0],PARAMETER["central_meridian",27],PARAMETER["scale_factor",0.9996],PARAMETER["false_easting",500000],PARAMETER["false_northing",0],UNIT["metre",1],AXIS["Easting",EAST],AXIS["Northing",NORTH]]""";

    /// <summary>WGS 84 itself: a geographic system, which has no easting to carry a zero point through.</summary>
    private const string Wgs84Wkt =
        """GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]]""";

    private static PointCloudFacts Facts(int? epsg, double[]? shift) => FactsWith(epsg is { } code ? new PointCloudCrs(code, null, null) : null, shift);

    private static PointCloudFacts FactsWith(PointCloudCrs? crs, double[]? shift) => new()
    {
        LasVersion = "1.4",
        Scale = [0.01, 0.01, 0.01],
        Offset = [0, 0, 0],
        Min = [Easting - 100, Northing - 100, 900],
        Max = [Easting + 100, Northing + 100, 960],
        Crs = crs,
        OriginShift = shift,
    };

    [Fact]
    public void A_tiled_file_in_a_projected_grid_is_placed_where_its_zero_point_lands_facing_grid_north()
    {
        var placed = ScanAutoPlacement.FromFileCrs(Projector, Facts(Utm35N, [Easting, Northing, 930])).ShouldNotBeNull();
        var expected = Projector.ToWgs84(Utm35N, Easting, Northing).ShouldNotBeNull();

        placed.Longitude.ShouldBe(expected.Longitude, tolerance: 1e-9);
        placed.Latitude.ShouldBe(expected.Latitude, tolerance: 1e-9);
        placed.HeightM.ShouldBe(930);
        placed.Source.ShouldBe(ScanPlacementSource.FileCrs);

        // East of the central meridian grid north leans east of true north, so the heading is a
        // small positive bearing — the convergence itself, not its counter-turn.
        expected.ConvergenceDeg.ShouldBeGreaterThan(0.5);
        placed.HeadingDeg.ShouldBe(expected.ConvergenceDeg, tolerance: 1e-9);
    }

    [Fact]
    public void A_file_whose_header_spells_its_grid_out_as_text_is_placed_where_the_code_would_place_it()
    {
        // No code at all: what a LAS 1.4 file from a current writer carries.
        var textOnly = new PointCloudCrs(null, null, Utm35NWkt);
        var placed = ScanAutoPlacement.FromFileCrs(Projector, FactsWith(textOnly, [Easting, Northing, 930])).ShouldNotBeNull();
        var expected = Projector.ToWgs84(Utm35N, Easting, Northing).ShouldNotBeNull();
        placed.Longitude.ShouldBe(expected.Longitude, tolerance: 1e-9);
        placed.Latitude.ShouldBe(expected.Latitude, tolerance: 1e-9);
        placed.HeightM.ShouldBe(930);
        placed.HeadingDeg.ShouldBe(expected.ConvergenceDeg, tolerance: 1e-9);
        placed.Source.ShouldBe(ScanPlacementSource.FileCrs);

        // The text is recognised as the register's zone, so the scan records the code too.
        var model = new SurveyModel { Name = "scan", Format = SurveyModelFormat.Laz };
        ScanAutoPlacement.AfterTiling(Projector, model, FactsWith(textOnly, [Easting, Northing, 930]));
        model.PlacementSource.ShouldBe(ScanPlacementSource.FileCrs);
        model.SourceEpsg.ShouldBe(Utm35N);

        // Text describing a geographic system places nothing, as the geographic code does.
        ScanAutoPlacement.FromFileCrs(Projector, FactsWith(new PointCloudCrs(null, null, Wgs84Wkt), [25.4, 45.5, 930])).ShouldBeNull();

        // Text that is not a coordinate system at all.
        ScanAutoPlacement.FromFileCrs(Projector, FactsWith(new PointCloudCrs(null, null, "a scanner's notes"), [Easting, Northing, 930])).ShouldBeNull();

        // A code the register does not know beside text it can read: the text places the scan,
        // and the code recorded is the one the text was recognised as, not the unknown one.
        var unknownCodeWithText = new PointCloudCrs(999_999, null, Utm35NWkt);
        ScanAutoPlacement.FromFileCrs(Projector, FactsWith(unknownCodeWithText, [Easting, Northing, 930])).ShouldNotBeNull();
        var rescued = new SurveyModel { Name = "scan", Format = SurveyModelFormat.Laz };
        ScanAutoPlacement.AfterTiling(Projector, rescued, FactsWith(unknownCodeWithText, [Easting, Northing, 930]));
        rescued.SourceEpsg.ShouldBe(Utm35N);
    }

    [Fact]
    public void A_file_gives_no_placement_without_a_projected_code_or_before_it_is_tiled()
    {
        // No coordinate system: the file is in a scanner's own metres.
        ScanAutoPlacement.FromFileCrs(Projector, Facts(null, [Easting, Northing, 930])).ShouldBeNull();

        // A system but no tiles yet: the worker has not chosen the frame's zero point.
        ScanAutoPlacement.FromFileCrs(Projector, Facts(Utm35N, null)).ShouldBeNull();

        // WGS 84 itself: a geographic system has no easting to transform, and reading degrees as
        // one would put a cave in the Gulf of Guinea with a nonsense heading.
        ScanAutoPlacement.FromFileCrs(Projector, Facts(4326, [25.4, 45.5, 930])).ShouldBeNull();

        // A code nobody has defined.
        ScanAutoPlacement.FromFileCrs(Projector, Facts(999_999, [Easting, Northing, 930])).ShouldBeNull();
    }

    [Fact]
    public void A_later_tiling_places_a_file_placed_scan_again_and_leaves_a_hand_placed_one_alone()
    {
        var model = new SurveyModel { Name = "scan", Format = SurveyModelFormat.Laz };

        // First tiling: unplaced, the file places it.
        ScanAutoPlacement.AfterTiling(Projector, model, Facts(Utm35N, [Easting, Northing, 930]));
        model.PlacementSource.ShouldBe(ScanPlacementSource.FileCrs);
        model.SourceEpsg.ShouldBe(Utm35N);
        var first = model.Anchor.ShouldNotBeNull();

        // Second tiling with a zero point the worker chose differently: the placement follows.
        ScanAutoPlacement.AfterTiling(Projector, model, Facts(Utm35N, [Easting + 50, Northing, 935]));
        model.Anchor.ShouldNotBeNull().X.ShouldBeGreaterThan(first.X);
        model.AnchorHeightM.ShouldBe(935);

        // Somebody corrects it by hand; a third tiling must not undo their work.
        ScanAutoPlacement.Apply(model, ScanPlacement.Manual(25.4472, 45.5312, 952, 300));
        ScanAutoPlacement.AfterTiling(Projector, model, Facts(Utm35N, [Easting, Northing, 930]));
        model.Anchor.ShouldBe(new Point(25.4472, 45.5312) { SRID = 4326 });
        model.AnchorHeightM.ShouldBe(952);
        model.PlacementHeadingDeg.ShouldBe(300);
        model.PlacementSource.ShouldBe(ScanPlacementSource.Manual);

        // And a file with no system places nothing, leaving an unplaced scan unplaced.
        var local = new SurveyModel { Name = "local", Format = SurveyModelFormat.Laz };
        ScanAutoPlacement.AfterTiling(Projector, local, Facts(null, [1, 2, 3]));
        local.Anchor.ShouldBeNull();
        local.PlacementSource.ShouldBeNull();
    }
}
