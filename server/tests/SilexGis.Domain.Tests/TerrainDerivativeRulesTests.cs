// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What has to hold before a picture of the ground is computed from an elevation raster.
///
/// <para>
/// These are checked here rather than left to the raster library because of how the library refuses:
/// an unusable combination comes back as a sentence about processing modes, from inside a native
/// call, several steps away from the setting that caused it — and some unusable settings are not
/// refused at all but produce a complete, valid picture that says nothing.
/// </para>
/// </summary>
public sealed class TerrainDerivativeRulesTests
{
    [Fact]
    public void An_ordinary_shaded_relief_is_accepted()
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
        }).ShouldBeNull();

    [Theory]
    [InlineData(-10d)]
    [InlineData(0d)]
    [InlineData(120d)]
    public void A_light_that_is_not_above_the_horizon_is_refused(double altitude)
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            AltitudeDegrees = altitude,
        }).ShouldNotBeNull();

    [Theory]
    [InlineData(-1d)]
    [InlineData(360d)]
    [InlineData(double.NaN)]
    public void A_direction_that_is_not_a_compass_bearing_is_refused(double azimuth)
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            AzimuthDegrees = azimuth,
        }).ShouldNotBeNull();

    [Theory]
    [InlineData(0d)]
    [InlineData(-2d)]
    [InlineData(1000d)]
    public void Height_exaggeration_outside_what_can_be_read_is_refused(double factor)
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            ZFactor = factor,
        }).ShouldNotBeNull();

    [Fact]
    public void Steepness_and_facing_do_not_take_a_light_and_are_not_asked_about_one()
    {
        // A direction and a height that would be refused on a shaded relief are simply not part of
        // what a slope is, so they are not grounds to refuse one — the settings record is one record
        // for every picture, and validating every field against every picture would refuse work the
        // library would have done.
        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            AltitudeDegrees = -10d,
            AzimuthDegrees = 900d,
        }).ShouldBeNull();
    }

    [Fact]
    public void A_colour_relief_needs_a_ramp_with_at_least_two_distinct_heights()
    {
        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
        }).ShouldNotBeNull();

        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
            ColourRamp = [new TerrainColourStop(500d, 1, 2, 3)],
        }).ShouldNotBeNull();

        // Two colours at one height are two answers to the same question, and which one wins would
        // be decided by the order they happen to be written in.
        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
            ColourRamp = [new TerrainColourStop(500d, 1, 2, 3), new TerrainColourStop(500d, 9, 9, 9)],
        }).ShouldNotBeNull();

        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
            ColourRamp = [new TerrainColourStop(0d, 1, 2, 3), new TerrainColourStop(500d, 9, 9, 9)],
        }).ShouldBeNull();
    }

    [Fact]
    public void A_ramp_given_to_a_picture_that_paints_nothing_is_refused()
        // Silently ignoring it would leave a stored setting that says the raster was coloured and a
        // raster that was not.
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            ColourRamp = [new TerrainColourStop(0d, 1, 2, 3), new TerrainColourStop(500d, 9, 9, 9)],
        }).ShouldNotBeNull();

    [Fact]
    public void A_picture_this_installation_cannot_compute_is_refused_by_name()
        // The stored value is a number, so a row written by a newer version — or by hand — can name
        // something this one has no mode for. Geomorphons, curvature and flow accumulation are the
        // ones a reader would expect and none of them is in the list.
        => TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = (TerrainDerivative)99,
        }).ShouldNotBeNull();
}
