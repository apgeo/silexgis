// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// Where a survey file's own numbers land in the world. No database and no file — a placement is
/// arithmetic over one projector answer, and the question these ask is whether that arithmetic
/// agrees with the projector it was derived from.
/// </summary>
public class SurveyPlacementTests
{
    /// <summary>UTM zone 35N, whose central meridian runs through the caves this was built for.</summary>
    private const int Utm35N = 32635;

    /// <summary>The zone's central meridian, where a transverse Mercator is most shrunk.</summary>
    private const double CentralMeridianEasting = 500_000;

    private const double Northing = 5_040_000;

    private static SurveyPlacement Placed(double easting, double northing) =>
        SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(Utm35N, OriginLongitude: null, OriginLatitude: null, OriginHeightM: 0),
            () => (easting, northing));

    [Fact]
    public void A_station_ten_kilometres_from_the_anchor_lands_where_the_projection_puts_it()
    {
        // A projected grid is deliberately not to scale: a transverse Mercator is shrunk by 400
        // parts per million along its central meridian so that it is not stretched too far at the
        // edge of its zone. A grid metre taken for a ground metre is therefore wrong by 40 cm per
        // kilometre from the anchor — four metres out here, and metres more across a large system —
        // and every bit of it in the same direction, which is what makes it worth measuring rather
        // than tolerating. Ten kilometres is an ordinary distance across a big cave.
        const double offsetM = 10_000;

        var projector = new ProjCoordinateProjector();
        var placement = Placed(CentralMeridianEasting, Northing);

        var (longitude, latitude, _) =
            placement.ToWorld(CentralMeridianEasting, Northing + offsetM, 0);
        var projected = projector.ToWgs84(Utm35N, CentralMeridianEasting, Northing + offsetM)
            .ShouldNotBeNull();

        // Compared in metres rather than in degrees, so the assertion is about the distance the
        // survey is wrong by. The crude metres-per-degree here only sizes that comparison; it is
        // not a second copy of the placement's own arithmetic.
        var northError = (latitude - projected.Latitude) * 111_132;
        var eastError = (longitude - projected.Longitude) * 111_320
            * Math.Cos(projected.Latitude * Math.PI / 180);

        Math.Sqrt((northError * northError) + (eastError * eastError)).ShouldBeLessThan(1.0);
    }

    [Fact]
    public void The_anchor_itself_is_exactly_what_the_projector_said()
    {
        var projector = new ProjCoordinateProjector();
        var projected = projector.ToWgs84(Utm35N, CentralMeridianEasting, Northing).ShouldNotBeNull();

        var placement = Placed(CentralMeridianEasting, Northing);
        var (longitude, latitude, altitude) =
            placement.ToWorld(CentralMeridianEasting, Northing, 12);

        longitude.ShouldBe(projected.Longitude, tolerance: 1e-12);
        latitude.ShouldBe(projected.Latitude, tolerance: 1e-12);
        altitude.ShouldBe(12);
    }

    [Fact]
    public void A_survey_in_plain_metres_is_scaled_by_nothing()
    {
        // Nothing projected it, so its metres are already ground metres. A hundred metres east of a
        // zero point at the equator is a hundred metres east, to well within the ellipsoid
        // expansion's own error.
        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: null, OriginLongitude: 0, OriginLatitude: 0, OriginHeightM: 700),
            () => (100, 0));

        var (longitude, latitude, altitude) = placement.ToWorld(100, 0, -30);

        (longitude * 111_319.49).ShouldBe(100, tolerance: 0.01);
        latitude.ShouldBe(0, tolerance: 1e-12);
        altitude.ShouldBe(670);
    }

    [Fact]
    public void A_file_declared_local_whose_numbers_are_a_projected_grid_is_refused()
    {
        // A UTM 35N export placed as metres from a point lands 360 km east and 5,000 km north of
        // that point: every stage succeeds and the cave is drawn nowhere anyone will look.
        Should.Throw<SurveySourceException>(() => SurveyPlacement.Resolve(
                new ProjCoordinateProjector(),
                new SurveySourceDeclaration(
                    SourceEpsg: null, OriginLongitude: 25.2, OriginLatitude: 45.5, OriginHeightM: 1900),
                () => (360_150, 5_042_130)))
            .Message.ShouldContain("projected");
    }

    [Fact]
    public void A_local_file_tens_of_kilometres_across_is_still_placed()
    {
        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: null, OriginLongitude: 25.2, OriginLatitude: 45.5, OriginHeightM: 1900),
            () => (30_000, -20_000));

        placement.Anchor.Longitude.ShouldBe(25.2);
    }

    // ---- noticing a wrong declaration ------------------------------------------

    /// <summary>A survey's middle in UTM zone 34N, and where that puts the cave it belongs to.</summary>
    private static (double X, double Y) Zone34Footprint => (650_000, 4_990_000);

    private static (double Longitude, double Latitude) CaveUnder(int epsg, (double X, double Y) footprint)
    {
        var at = new ProjCoordinateProjector().ToWgs84(epsg, footprint.X, footprint.Y).ShouldNotBeNull();
        return (at.Longitude, at.Latitude);
    }

    [Fact]
    public void A_grid_file_declared_local_is_refused_with_the_grid_that_places_it_at_its_cave()
    {
        var footprint = Zone34Footprint;

        var refusal = Should.Throw<SurveySourceException>(() => SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: null, OriginLongitude: 22.9, OriginLatitude: 45.0, OriginHeightM: 800,
                CavePosition: CaveUnder(32634, footprint)),
            () => footprint));

        var advice = refusal.Advice.ShouldNotBeNull();
        advice.SourceEpsg.ShouldBe(32634);
        advice.SuggestedOffByKm.ShouldNotBeNull().ShouldBeLessThan(0.01);
        refusal.Message.ShouldContain("EPSG:32634");
    }

    [Fact]
    public void A_file_declared_in_the_neighbouring_zone_is_placed_and_carries_the_right_one()
    {
        // The mistake that nothing refuses: read in the next zone the same numbers land hundreds
        // of kilometres to one side, in a perfectly ordinary place.
        var footprint = Zone34Footprint;

        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: 32635, OriginLongitude: null, OriginLatitude: null, OriginHeightM: 800,
                CavePosition: CaveUnder(32634, footprint)),
            () => footprint);

        var advice = placement.Advice.ShouldNotBeNull();
        advice.SourceEpsg.ShouldBe(32634);
        advice.OffByKm.ShouldNotBeNull().ShouldBeGreaterThan(300);
    }

    [Fact]
    public void A_local_file_declared_on_a_grid_is_advised_to_be_read_as_local_metres()
    {
        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: 32635, OriginLongitude: null, OriginLatitude: null, OriginHeightM: 800,
                CavePosition: (25.2, 45.5)),
            () => (120, -40));

        var advice = placement.Advice.ShouldNotBeNull();
        advice.UseLocalMetres.ShouldBeTrue();
    }

    [Fact]
    public void A_file_declared_in_its_own_grid_carries_no_advice()
    {
        var footprint = Zone34Footprint;

        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: 32634, OriginLongitude: null, OriginLatitude: null, OriginHeightM: 800,
                CavePosition: CaveUnder(32634, footprint)),
            () => footprint);

        placement.Advice.ShouldBeNull();
    }

    [Fact]
    public void Without_a_cave_position_nothing_is_advised()
    {
        var footprint = Zone34Footprint;

        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(
                SourceEpsg: 32635, OriginLongitude: null, OriginLatitude: null, OriginHeightM: 800),
            () => footprint);

        placement.Advice.ShouldBeNull();
    }
}
