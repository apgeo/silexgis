// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The landform picture's settings, classes and place among the other pictures: what it may be
/// asked with, how its settings are written down, and that adding it changed nothing about how
/// every other picture is written down.
/// </summary>
public sealed class TerrainGeomorphonsTests
{
    [Fact]
    public void The_ten_classes_are_numbered_one_to_ten_in_the_order_the_reference_implementations_write()
    {
        TerrainGeomorphons.Classes.ShouldBe(
            ["flat", "peak", "ridge", "shoulder", "spur", "slope", "hollow", "footslope", "valley", "pit"]);
        TerrainGeomorphons.ClassOf(1).ShouldBe("flat");
        TerrainGeomorphons.ClassOf(7).ShouldBe("hollow");
        TerrainGeomorphons.ClassOf(10).ShouldBe("pit");

        // A hole, a value between classes, or anything outside them is not a class.
        TerrainGeomorphons.ClassOf(0).ShouldBeNull();
        TerrainGeomorphons.ClassOf(11).ShouldBeNull();
        TerrainGeomorphons.ClassOf(2.5).ShouldBeNull();
        TerrainGeomorphons.ClassOf(double.NaN).ShouldBeNull();
    }

    [Fact]
    public void Entrances_are_classed_by_the_landform_they_sit_on_and_unclassified_cells_count_nowhere()
    {
        TerrainClassStatistics.Codes(TerrainMeasure.Landform).ShouldBe(TerrainGeomorphons.Classes);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Landform, 1).ShouldBe(0);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Landform, 7).ShouldBe(6);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Landform, 10).ShouldBe(9);
        TerrainClassStatistics.ClassOf(TerrainMeasure.Landform, 0).ShouldBeNull();
        TerrainClassStatistics.ClassOf(TerrainMeasure.Landform, 3.5).ShouldBeNull();

        // Two slopes, a valley, and three cells the program left unclassified (the edge marker zero,
        // an out-of-range value and a hole): six readings, three counted.
        var counts = TerrainClassStatistics.Histogram(
            TerrainMeasure.Landform, [6, 6, 9, 0, 255, double.NaN]);

        counts.Length.ShouldBe(10);
        counts.Sum().ShouldBe(3);
        counts[5].ShouldBe(2);
        counts[8].ShouldBe(1);
    }

    [Fact]
    public void Settings_are_checked_with_the_picture_they_belong_to()
    {
        static string? Problem(double? search = null, double? skip = null, double? flatness = null) =>
            TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
            {
                Derivative = TerrainDerivative.Geomorphons,
                GeomorphonSearchMetres = search,
                GeomorphonSkipMetres = skip,
                GeomorphonFlatnessDegrees = flatness,
            });

        Problem().ShouldBeNull();
        Problem(search: 2000, skip: 100, flatness: 2.5).ShouldBeNull();
        Problem(search: 0).ShouldNotBeNull();
        Problem(search: 10_001).ShouldNotBeNull();
        Problem(search: double.NaN).ShouldNotBeNull();
        Problem(skip: -1).ShouldNotBeNull();
        Problem(search: 300, skip: 300).ShouldNotBeNull();
        Problem(flatness: -0.1).ShouldNotBeNull();
        Problem(flatness: 46).ShouldNotBeNull();

        // Another picture's request is not refused for a landform setting it does not read.
        TerrainDerivativeRules.Problem(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            GeomorphonSearchMetres = 0,
        }).ShouldBeNull();
    }

    [Fact]
    public void A_landform_request_writes_its_usual_settings_out_and_every_other_picture_is_written_as_before()
    {
        var landforms = TerrainDerivativeRegistry.Normalise(
            new TerrainDerivativeSettings { Derivative = TerrainDerivative.Geomorphons });
        landforms.GeomorphonSearchMetres.ShouldBe(TerrainGeomorphons.DefaultSearchMetres);
        landforms.GeomorphonSkipMetres.ShouldBe(0d);
        landforms.GeomorphonFlatnessDegrees.ShouldBe(TerrainGeomorphons.DefaultFlatnessDegrees);

        // Naming the usual values is the same request as leaving them out.
        TerrainDerivativeRegistry.Fingerprint(new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Geomorphons,
            GeomorphonSearchMetres = 500,
            GeomorphonSkipMetres = 0,
            GeomorphonFlatnessDegrees = 1,
        }).ShouldBe(TerrainDerivativeRegistry.Fingerprint(landforms));

        // Every other picture carries none of them, whatever was sent, so no picture already stored
        // changes its fingerprint because this one was added.
        foreach (var kind in Enum.GetValues<TerrainDerivative>().Where(k => k != TerrainDerivative.Geomorphons))
        {
            var described = TerrainDerivativeRegistry.Describe(new TerrainDerivativeSettings
            {
                Derivative = kind,
                GeomorphonSearchMetres = 900,
                ColourRamp = kind == TerrainDerivative.ColourRelief
                    ? [new TerrainColourStop(0, 0, 0, 0), new TerrainColourStop(1, 1, 1, 1)]
                    : [],
            });
            described.ShouldNotContain("geomorphon", Case.Insensitive);
        }

        // The written form of a steepness picture, exactly as it was before landforms existed.
        TerrainDerivativeRegistry.Describe(new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope })
            .ShouldBe("{\"derivative\":1,\"lighting\":0,\"azimuthDegrees\":315,\"altitudeDegrees\":45,\"zFactor\":1,"
                + "\"surfaceFit\":0,\"slopeUnit\":0,\"ruggednessFit\":0,\"computeEdges\":true,\"colourRamp\":[]}");
    }

    [Fact]
    public void Only_the_landform_picture_needs_the_analysis_program()
    {
        foreach (var kind in Enum.GetValues<TerrainDerivative>())
        {
            TerrainDerivativeRegistry.NeedsAnalysisTool(kind).ShouldBe(kind == TerrainDerivative.Geomorphons);
        }
    }

    [Theory]
    [InlineData(500, 30, true, 17)]
    [InlineData(500, 92.8, true, 5)]
    [InlineData(10, 30, true, 1)]
    [InlineData(10, 30, false, 0)]
    [InlineData(0, 30, false, 0)]
    public void Distances_in_metres_become_each_raster_s_own_cells(double metres, double pixel, bool atLeastOne, int cells) =>
        TerrainGeomorphons.Cells(metres, pixel, atLeastOne).ShouldBe(cells);
}
