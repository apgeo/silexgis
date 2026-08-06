// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class GeoidOffsetTests
{
    // Measured EGM2008 undulations over Romanian karst; the sign convention is what these tests
    // exist to pin down, because getting it backwards is an 80 m error that still looks plausible.
    private const double PiatraCraiuluiUndulation = 39.39;
    private const double BanatUndulation = 44.46;

    [Fact]
    public void A_surveyed_altitude_rises_by_the_undulation_when_it_becomes_an_ellipsoidal_height()
    {
        // A Piatra Craiului entrance recorded at 1200 m above the Black Sea datum sits 1239.39 m
        // above the WGS84 ellipsoid — the globe has to place it higher, not lower.
        GeoidOffset.EllipsoidalFromOrthometric(1200, PiatraCraiuluiUndulation)
            .ShouldBe(1239.39, 1e-9);
    }

    [Fact]
    public void A_terrain_sample_drops_by_the_undulation_before_it_can_be_compared_with_a_survey()
    {
        GeoidOffset.OrthometricFromEllipsoidal(1239.39, PiatraCraiuluiUndulation)
            .ShouldBe(1200, 1e-9);
    }

    [Fact]
    public void The_two_directions_round_trip()
    {
        foreach (var altitude in new[] { -12.5, 0d, 87.25, 1420.75 })
        {
            var ellipsoidal = GeoidOffset.EllipsoidalFromOrthometric(altitude, BanatUndulation);
            GeoidOffset.OrthometricFromEllipsoidal(ellipsoidal, BanatUndulation)
                .ShouldBe(altitude, 1e-9);
        }
    }

    [Fact]
    public void A_zero_offset_is_the_identity_so_an_installation_can_opt_out()
    {
        GeoidOffset.EllipsoidalFromOrthometric(950, 0).ShouldBe(950);
        GeoidOffset.OrthometricFromEllipsoidal(950, 0).ShouldBe(950);
    }

    [Fact]
    public void A_terrain_source_serving_sea_level_heights_needs_no_correction_at_all()
    {
        // The failure this decides. A source whose tiles hold heights above sea level speaks the
        // same language a cave survey does: a globe misplaces both by the same undulation, so
        // they agree with each other, which is what putting a cave inside its hillside needs.
        // Correcting anyway lifts the whole cave the undulation clear of the ground it is in.
        GeoidOffset.SurveyToSceneOffsetM(TerrainHeightDatum.Orthometric, BanatUndulation)
            .ShouldBe(0);
        GeoidOffset.SurveyToSceneOffsetM(TerrainHeightDatum.Orthometric, 0).ShouldBe(0);
    }

    [Fact]
    public void A_terrain_source_serving_ellipsoidal_heights_raises_the_survey_by_the_undulation()
    {
        // Tiles converted when they were baked draw the ground where it really is, so a surveyed
        // altitude has to travel the undulation before it will meet that ground.
        GeoidOffset.SurveyToSceneOffsetM(TerrainHeightDatum.Ellipsoidal, PiatraCraiuluiUndulation)
            .ShouldBe(PiatraCraiuluiUndulation);
    }

    [Fact]
    public void The_correction_agrees_with_the_conversion_it_is_made_of()
    {
        // Both live here, and this is the claim that keeps them consistent: raising a survey by
        // the offset a source asks for is the same arithmetic as converting it.
        const double surveyed = 1200;
        var offset = GeoidOffset.SurveyToSceneOffsetM(
            TerrainHeightDatum.Ellipsoidal, PiatraCraiuluiUndulation);
        (surveyed + offset)
            .ShouldBe(GeoidOffset.EllipsoidalFromOrthometric(surveyed, PiatraCraiuluiUndulation));
    }

    [Fact]
    public void The_installation_default_stays_within_a_few_metres_of_every_measured_romanian_value()
    {
        // The default is a single scalar standing in for a surface that varies across the country;
        // this is the claim that makes that acceptable, so it is asserted rather than asserted in
        // prose. 41.5 m against +44.46 (Banat), +43.50 (Apuseni), +39.39 (Piatra Craiului).
        const double installationDefault = 41.5;
        foreach (var measured in new[] { 44.46, 43.50, 39.39 })
        {
            Math.Abs(GeoidOffset.EllipsoidalFromOrthometric(1000, installationDefault)
                     - GeoidOffset.EllipsoidalFromOrthometric(1000, measured))
                .ShouldBeLessThanOrEqualTo(3.0);
        }
    }
}
