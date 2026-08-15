// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Tests;

/// <summary>
/// A build made inside the application and a terrain source an operator configured by hand are two
/// ways of describing the same thing, and both have to tell the 3D scene the same story about what
/// their heights are measured from.
///
/// <para>
/// This is asserted rather than assumed because getting it wrong is a forty-metre error with
/// nothing on screen to say so: every cave sits that far above or below the hillside it is really
/// on, which reads as somebody having typed an altitude wrong rather than as two parts of the
/// system disagreeing about a datum. The rule has one home, and these tests exist to prove it stays
/// one home — a second copy of the conditional would agree today and drift the first time either
/// side is edited.
/// </para>
/// </summary>
public class TerrainBuildHeightTests
{
    /// <summary>An extent is required to have a build at all; nothing here depends on where it is.</summary>
    private static Polygon SomeExtent() =>
        NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePolygon(
        [
            new Coordinate(22.5, 46.5),
            new Coordinate(22.6, 46.5),
            new Coordinate(22.6, 46.6),
            new Coordinate(22.5, 46.6),
            new Coordinate(22.5, 46.5),
        ]);

    private static TerrainBuild Build(TerrainHeightDatum datum, double geoidHeightM) => new()
    {
        Extent = SomeExtent(),
        RequestedMaxDepth = 14,
        HeightDatum = datum,
        GeoidHeightM = geoidHeightM,
    };

    [Fact]
    public void A_build_of_sea_level_heights_asks_for_no_correction()
    {
        // What an unconverted elevation model produces, and the default a build starts from: the
        // tile heights are the same kind of number a cave survey carries, so nothing has to move.
        var build = Build(TerrainHeightDatum.Orthometric, geoidHeightM: 0);

        build.HeightDatum.ShouldBe(TerrainHeightDatum.Orthometric);
        build.SurveyHeightOffsetM.ShouldBe(0);
    }

    [Fact]
    public void A_build_converted_when_it_was_baked_asks_for_the_local_undulation()
    {
        Build(TerrainHeightDatum.Ellipsoidal, geoidHeightM: 43.03).SurveyHeightOffsetM.ShouldBe(43.03);
    }

    [Fact]
    public void An_undulation_recorded_against_sea_level_heights_is_ignored_rather_than_applied()
    {
        // The combination that would otherwise move every cave by a number nobody asked to use:
        // whether the correction applies is decided by the datum alone, never by the presence of
        // an undulation somebody typed in and then changed their mind about.
        Build(TerrainHeightDatum.Orthometric, geoidHeightM: 43.03).SurveyHeightOffsetM.ShouldBe(0);
    }

    [Theory]
    [InlineData(TerrainHeightDatum.Orthometric, 0.0)]
    [InlineData(TerrainHeightDatum.Orthometric, 43.03)]
    [InlineData(TerrainHeightDatum.Ellipsoidal, 0.0)]
    [InlineData(TerrainHeightDatum.Ellipsoidal, 43.03)]
    [InlineData(TerrainHeightDatum.Ellipsoidal, 39.39)]
    public void A_build_and_a_configured_source_resolve_the_same_offset(
        TerrainHeightDatum datum, double geoidHeightM)
    {
        // The two callers of the one rule, side by side. An installation may hold both at once —
        // a pyramid built here and an operator's own, described in the environment — and a cave
        // drawn against either has to land in the same place relative to the ground.
        var configured = new TerrainOptions
        {
            Url = "/terrain/",
            HeightDatum = datum,
            GeoidHeightM = geoidHeightM,
        };

        Build(datum, geoidHeightM).SurveyHeightOffsetM
            .ShouldBe(configured.SurveyHeightOffsetM);
        Build(datum, geoidHeightM).SurveyHeightOffsetM
            .ShouldBe(GeoidOffset.SurveyToSceneOffsetM(datum, geoidHeightM));
    }
}
