// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What makes two requests for a picture of the ground one request, and what makes a stored
/// picture out of date.
/// </summary>
public sealed class TerrainDerivativeRegistryRulesTests
{
    /// <summary>
    /// Settings the chosen picture never reads do not make it a different picture.
    /// </summary>
    /// <remarks>
    /// Worth an assertion rather than trusting the record's defaults, because the failure is silent
    /// and expensive: the same shaded relief stored twice under two fingerprints, computed twice
    /// over every raster of a build, occupying twice the disk, and offered to a reader as two
    /// layers that differ in nothing they could see.
    /// </remarks>
    [Fact]
    public void A_setting_the_picture_does_not_read_does_not_make_it_a_different_picture()
    {
        var lit = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade };
        var same = lit with { SlopeUnit = TerrainSlopeUnit.Percent, RuggednessFit = TerrainRuggednessFit.Wilson };

        TerrainDerivativeRegistry.Fingerprint(same)
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(lit));
    }

    [Fact]
    public void A_setting_the_picture_does_read_makes_it_a_different_picture()
    {
        var northWest = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade };
        var southEast = northWest with { AzimuthDegrees = 135d };

        TerrainDerivativeRegistry.Fingerprint(southEast)
            .ShouldNotBe(TerrainDerivativeRegistry.Fingerprint(northWest));
    }

    /// <summary>
    /// A light direction means nothing to a shading lit from four fixed directions at once.
    /// </summary>
    /// <remarks>
    /// The computation emits the four-light switch instead of a direction, so two multidirectional
    /// requests differing only in azimuth would produce byte-identical rasters — stored twice, and
    /// each labelled with a light direction its picture never had.
    /// </remarks>
    [Fact]
    public void A_light_direction_does_not_divide_a_shading_lit_from_every_side()
    {
        var northWest = new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            Lighting = TerrainHillshadeLighting.Multidirectional,
        };
        var southEast = northWest with { AzimuthDegrees = 135d };

        TerrainDerivativeRegistry.Fingerprint(southEast)
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(northWest));
        TerrainDerivativeRegistry.Normalise(southEast).AzimuthDegrees.ShouldBe(315d);
    }

    /// <summary>
    /// Only shaded relief is drawn with a height exaggeration, so it divides nothing else.
    /// </summary>
    [Fact]
    public void A_height_exaggeration_does_not_divide_a_picture_that_is_not_shaded_relief()
    {
        var plain = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope };
        var exaggerated = plain with { ZFactor = 3d };

        TerrainDerivativeRegistry.Fingerprint(exaggerated)
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(plain));
        TerrainDerivativeRegistry.Normalise(exaggerated).ZFactor.ShouldBe(1d);
    }

    /// <summary>A height exaggeration does change the shaded relief it is applied to.</summary>
    [Fact]
    public void A_height_exaggeration_makes_a_different_shaded_relief()
    {
        var plain = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade };
        var exaggerated = plain with { ZFactor = 3d };

        TerrainDerivativeRegistry.Fingerprint(exaggerated)
            .ShouldNotBe(TerrainDerivativeRegistry.Fingerprint(plain));
    }

    [Fact]
    public void Two_different_pictures_of_the_same_ground_are_two_requests()
    {
        var shaded = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade };
        var steepness = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope };

        TerrainDerivativeRegistry.Fingerprint(steepness)
            .ShouldNotBe(TerrainDerivativeRegistry.Fingerprint(shaded));
    }

    [Fact]
    public void A_colour_ramp_is_part_of_what_a_colour_relief_is()
    {
        var low = new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
            ColourRamp = [new TerrainColourStop(0, 0, 0, 0), new TerrainColourStop(1000, 255, 255, 255)],
        };
        var high = low with
        {
            ColourRamp = [new TerrainColourStop(0, 0, 0, 0), new TerrainColourStop(2000, 255, 255, 255)],
        };

        TerrainDerivativeRegistry.Fingerprint(high)
            .ShouldNotBe(TerrainDerivativeRegistry.Fingerprint(low));
    }

    /// <summary>
    /// A picture is current exactly while the elevation it was drawn from is the elevation being
    /// served, and stale in both of the other cases.
    /// </summary>
    /// <remarks>
    /// The third case is the one that is easy to get wrong: with no build active at all there is
    /// nothing for a shaded relief to agree with, and answering "current" there would put a picture
    /// of ground the installation no longer serves on the map with nothing on it saying so.
    /// </remarks>
    [Fact]
    public void A_picture_is_current_only_while_the_ground_it_was_drawn_from_is_the_ground_served()
    {
        var drawn = Guid.CreateVersion7();
        var replaced = Guid.CreateVersion7();

        TerrainDerivativeRegistry.IsStale(drawn, drawn).ShouldBeFalse();
        TerrainDerivativeRegistry.IsStale(drawn, replaced).ShouldBeTrue();
        TerrainDerivativeRegistry.IsStale(drawn, null).ShouldBeTrue();
    }
}
