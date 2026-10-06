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
    /// The record of a picture that draws no contours is written exactly as it was before
    /// contours existed.
    /// </summary>
    /// <remarks>
    /// The fingerprint is taken over this text, so a field that appeared in it the day a new kind
    /// of picture was added would give every picture already stored a different fingerprint from
    /// the one on its row. Asking again for one of them would then find nothing, and compute and
    /// keep a second copy. The text is spelled out here rather than derived, because the property
    /// being protected is that it does not change.
    /// </remarks>
    [Fact]
    public void A_picture_without_contours_is_recorded_as_it_always_was()
        => TerrainDerivativeRegistry.Describe(
                new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade })
            .ShouldBe(
                "{\"derivative\":0,\"lighting\":0,\"azimuthDegrees\":315,\"altitudeDegrees\":45,"
                + "\"zFactor\":1,\"surfaceFit\":0,\"slopeUnit\":0,\"ruggednessFit\":0,"
                + "\"computeEdges\":true,\"colourRamp\":[]}");

    [Fact]
    public void Contours_asked_for_at_no_spacing_and_at_the_usual_one_are_one_picture()
    {
        var unnamed = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Contours };
        var named = unnamed with
        {
            ContourIntervalMetres = TerrainContourLines.DefaultIntervalMetres,
        };

        TerrainDerivativeRegistry.Fingerprint(unnamed)
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(named));

        // And the record says which spacing was drawn, so the register can.
        TerrainDerivativeRegistry.Describe(unnamed).ShouldContain("\"contourIntervalMetres\":20");
    }

    [Fact]
    public void A_different_spacing_is_a_different_picture_of_contours()
    {
        var close = new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Contours,
            ContourIntervalMetres = 10d,
        };

        TerrainDerivativeRegistry.Fingerprint(close with { ContourIntervalMetres = 50d })
            .ShouldNotBe(TerrainDerivativeRegistry.Fingerprint(close));
    }

    /// <summary>
    /// Settings that contours never read do not divide them, and a spacing divides nothing else.
    /// </summary>
    [Fact]
    public void A_spacing_divides_only_contours_and_contours_are_divided_only_by_it()
    {
        var contours = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Contours };

        TerrainDerivativeRegistry.Fingerprint(contours with
            {
                ComputeEdges = false,
                SurfaceFit = TerrainSurfaceFit.ZevenbergenThorne,
                ZFactor = 4d,
            })
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(contours));

        var shaded = new TerrainDerivativeSettings { Derivative = TerrainDerivative.Hillshade };

        TerrainDerivativeRegistry.Fingerprint(shaded with { ContourIntervalMetres = 50d })
            .ShouldBe(TerrainDerivativeRegistry.Fingerprint(shaded));
        TerrainDerivativeRegistry.Normalise(shaded with { ContourIntervalMetres = 50d })
            .ContourIntervalMetres.ShouldBeNull();
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
        var today = TerrainDerivativeRegistry.MethodRevision(TerrainDerivative.Hillshade);

        TerrainDerivativeRegistry.Staleness(drawn, drawn, TerrainDerivative.Hillshade, 1, today)
            .ShouldBeNull();
        TerrainDerivativeRegistry.Staleness(drawn, replaced, TerrainDerivative.Hillshade, 1, today)
            .ShouldBe(TerrainDerivativeStaleness.ElevationReplaced);
        TerrainDerivativeRegistry.Staleness(drawn, null, TerrainDerivative.Hillshade, 1, today)
            .ShouldBe(TerrainDerivativeStaleness.ElevationReplaced);
    }

    /// <summary>
    /// A picture drawn by arithmetic that has since been corrected is out of date over ground
    /// that has not changed at all.
    /// </summary>
    /// <remarks>
    /// Steepness, facing and shaded relief were first computed with one distance for a degree in
    /// both directions, and a row written then records revision zero. Nothing about the file
    /// changed when the arithmetic did, so the revision is the only thing that can say the
    /// picture is the old one.
    /// </remarks>
    [Theory]
    [InlineData(TerrainDerivative.Slope)]
    [InlineData(TerrainDerivative.Aspect)]
    [InlineData(TerrainDerivative.Hillshade)]
    public void A_picture_drawn_before_its_arithmetic_was_corrected_is_out_of_date(
        TerrainDerivative derivative)
    {
        var build = Guid.CreateVersion7();

        TerrainDerivativeRegistry.Staleness(build, build, derivative, version: 1, methodRevision: 0)
            .ShouldBe(TerrainDerivativeStaleness.MethodRevised);

        // And current once it has been drawn again, so the mark is about the file and not the kind.
        TerrainDerivativeRegistry.Staleness(
                build, build, derivative, 2, TerrainDerivativeRegistry.MethodRevision(derivative))
            .ShouldBeNull();
    }

    /// <summary>
    /// A kind whose arithmetic has not changed is not marked for the others having changed.
    /// </summary>
    [Theory]
    [InlineData(TerrainDerivative.RuggednessIndex)]
    [InlineData(TerrainDerivative.PositionIndex)]
    [InlineData(TerrainDerivative.Roughness)]
    [InlineData(TerrainDerivative.ColourRelief)]
    public void A_picture_whose_arithmetic_never_changed_stays_current(TerrainDerivative derivative)
    {
        var build = Guid.CreateVersion7();

        TerrainDerivativeRegistry.Staleness(build, build, derivative, version: 1, methodRevision: 0)
            .ShouldBeNull();
    }

    /// <summary>
    /// A picture that has never been computed has no rasters to be wrong.
    /// </summary>
    /// <remarks>
    /// A row just asked for carries no revision yet, and a queued picture labelled out of date
    /// before it has been drawn once would be a label that means nothing.
    /// </remarks>
    [Fact]
    public void A_picture_not_yet_computed_is_not_out_of_date_for_its_arithmetic()
    {
        var build = Guid.CreateVersion7();

        TerrainDerivativeRegistry.Staleness(build, build, TerrainDerivative.Slope, version: 0, methodRevision: 0)
            .ShouldBeNull();
        TerrainDerivativeRegistry.IsMethodRevised(TerrainDerivative.Slope, version: 0, methodRevision: 0)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Replaced elevation is the reason given when both hold.
    /// </summary>
    /// <remarks>
    /// Because it is the one asking for the same picture again would not cure: the row is tied to
    /// the build it was drawn from.
    /// </remarks>
    [Fact]
    public void Replaced_elevation_is_named_before_corrected_arithmetic()
        => TerrainDerivativeRegistry.Staleness(
                Guid.CreateVersion7(), Guid.CreateVersion7(), TerrainDerivative.Aspect, 1, 0)
            .ShouldBe(TerrainDerivativeStaleness.ElevationReplaced);
}
