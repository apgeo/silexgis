// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The pictures computed from an elevation raster, checked against ground whose answer is known
/// before anything is run.
///
/// <para>
/// A wrapper around somebody else's raster library is the kind of code that passes every test that
/// only asks whether a file appeared. The fixture here is a flat ramp — one plane, tilted by a
/// chosen amount in a chosen direction — because a plane has exactly one steepness and exactly one
/// facing at every point on it, both of which can be worked out with a calculator and neither of
/// which a wrapper that quietly computed the wrong thing, or passed no scale at all, would produce.
/// </para>
///
/// <para>
/// Every raster below is placed in longitude and latitude, which is what the elevation preparation
/// step writes, and that carries a limit worth stating in a test rather than in a comment: the
/// library's modes take one distance scale for both directions, so north-south steepness is exact
/// while east-west steepness is measured against a degree of longitude assumed to be as wide as a
/// degree of latitude. At the fixture's latitude the ground really is about 1.44 times steeper
/// east-west than the number that comes back. The east-facing test asserts the number that comes
/// back, so that if the scale ever changes the assertion moves with it instead of quietly passing.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class TerrainDerivativeTests : IDisposable
{
    /// <summary>Somewhere in the Carpathians, which is where the fixture's latitude comes from.</summary>
    private const double West = 25.0;
    private const double North = 46.1;

    /// <summary>Roughly a hundred metres of ground, north to south.</summary>
    private const double Pixel = 0.001;

    /// <summary>
    /// The height a plane rises over one pixel to stand at forty-five degrees.
    /// </summary>
    /// <remarks>
    /// A pixel is <see cref="Pixel"/> degrees, and the scale the computation is given turns one
    /// degree into <see cref="TerrainRasterPreparation.MetresPerDegree"/> metres, so a rise of
    /// exactly that many metres per pixel is a rise equal to the run.
    /// </remarks>
    private const double RisePerPixel = Pixel * TerrainRasterPreparation.MetresPerDegree;

    private const int Size = 24;

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "silexgis-derivative-" + Guid.NewGuid().ToString("N"));

    private readonly GdalDemDerivatives derivatives = new();

    public TerrainDerivativeTests()
    {
        Directory.CreateDirectory(root);
        GdalRuntime.Configure();
    }

    public void Dispose()
    {
        Gdal.SetConfigOption("CPL_TMPDIR", Path.GetTempPath());

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Litter, not a failing test.
        }
    }

    [Fact]
    public void The_bundled_library_can_write_what_these_computations_produce()
        // Everything these layers are is a raster this installation has to be able to write.
        => GdalDemDerivatives.DerivativeWriterAvailable.ShouldBeTrue();

    [Fact]
    public void A_plane_tilted_to_the_north_has_one_steepness_and_faces_south()
    {
        var source = Ramp("north.tif", northward: RisePerPixel, eastward: 0);

        var slope = Compute(source, "slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });

        // Forty-five degrees by construction, everywhere on the plane.
        Interior(slope).ShouldAllBe(value => Math.Abs(value - 45d) < 0.01d);

        var aspect = Compute(source, "aspect.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        });

        // Facing is the direction water would run, and on ground that rises to the north that is
        // south: a hundred and eighty degrees clockwise from north.
        Interior(aspect).ShouldAllBe(value => Math.Abs(value - 180d) < 0.01d);
    }

    [Fact]
    public void Steepness_can_be_asked_for_as_a_percentage_instead_of_an_angle()
    {
        var source = Ramp("percent.tif", northward: RisePerPixel, eastward: 0);

        var slope = Compute(source, "slope-percent.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            SlopeUnit = TerrainSlopeUnit.Percent,
        });

        // Rise equal to run is a hundred per cent, which is the same plane the previous test found
        // to be at forty-five degrees — the check that the unit changes the number and not the ground.
        Interior(slope).ShouldAllBe(value => Math.Abs(value - 100d) < 0.02d);
    }

    [Fact]
    public void Both_ways_of_working_out_a_slope_agree_on_a_plane()
    {
        var source = Ramp("fits.tif", northward: RisePerPixel / 2d, eastward: 0);

        var horn = Compute(source, "horn.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            SurfaceFit = TerrainSurfaceFit.Horn,
        });

        var zevenbergen = Compute(source, "zevenbergen.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            SurfaceFit = TerrainSurfaceFit.ZevenbergenThorne,
        });

        // The two weightings differ on real, noisy ground and are both exact on a plane, which makes
        // this the assertion that the choice reaches the library at all rather than being dropped:
        // an argument that never arrives would leave both of these reading the default, and they
        // would agree for the wrong reason. The expected value is worked out here independently.
        var expected = Math.Atan(0.5d) * 180d / Math.PI;
        Interior(horn).ShouldAllBe(value => Math.Abs(value - expected) < 0.01d);
        Interior(zevenbergen).ShouldAllBe(value => Math.Abs(value - expected) < 0.01d);
    }

    [Fact]
    public void A_plane_tilted_to_the_east_faces_west_and_is_measured_against_a_single_scale()
    {
        var source = Ramp("east.tif", northward: 0, eastward: RisePerPixel);

        var aspect = Compute(source, "aspect-east.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        });

        // Ground rising to the east faces west: two hundred and seventy degrees.
        Interior(aspect).ShouldAllBe(value => Math.Abs(value - 270d) < 0.01d);

        var slope = Compute(source, "slope-east.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });

        // And this is the number the single scale produces, not the steepness of the real hillside.
        // A degree of longitude at this latitude is about 0.695 of a degree of latitude, so the
        // ground here actually stands at about 55 degrees while the picture says 45. The assertion
        // is on what is produced, deliberately, because that is what a reader of the layer sees.
        Interior(slope).ShouldAllBe(value => Math.Abs(value - 45d) < 0.01d);
    }

    [Fact]
    public void Flat_ground_faces_north_rather_than_reading_as_missing()
    {
        var source = Ramp("flat.tif", northward: 0, eastward: 0);

        var aspect = Compute(source, "aspect-flat.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        });

        // Flat ground faces no direction, and left to itself the library marks it with the value
        // that stands for a hole — so a plateau would read as absent data and draw as a gap.
        Interior(aspect).ShouldAllBe(value => Math.Abs(value) < 0.01d);
    }

    [Fact]
    public void A_shaded_relief_is_whole_bytes_and_changes_when_the_lighting_does()
    {
        var source = Ramp("shade.tif", northward: RisePerPixel, eastward: 0);

        var single = Compute(source, "shade-single.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
        });

        var many = Compute(source, "shade-many.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            Lighting = TerrainHillshadeLighting.Multidirectional,
        });

        BandType(single.Path).ShouldBe(DataType.GDT_Byte);
        BandType(many.Path).ShouldBe(DataType.GDT_Byte);

        // One plane has one brightness under one light, whatever the light is.
        var lit = Interior(single);
        var many_lit = Interior(many);
        (lit.Max() - lit.Min()).ShouldBeLessThanOrEqualTo(1f, $"single {lit.Min()}..{lit.Max()}");
        (many_lit.Max() - many_lit.Min()).ShouldBeLessThanOrEqualTo(1f, $"many {many_lit.Min()}..{many_lit.Max()}");

        // Four lights instead of one is a different picture of the same ground. Without this the
        // multidirectional argument could be dropped on the floor and every test above would pass.
        Math.Abs((double)lit[0] - many_lit[0]).ShouldBeGreaterThan(
            1d, $"single {lit[0]} many {many_lit[0]}");
    }

    [Fact]
    public void A_plane_stands_exactly_where_its_neighbourhood_says_it_should()
    {
        var source = Ramp("position.tif", northward: RisePerPixel, eastward: 0);

        var position = Compute(source, "tpi.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.PositionIndex,
        });

        // Topographic position is a cell measured against the average of the eight around it, and on
        // a plane that average is the cell itself: zero everywhere, by construction, however steep
        // the plane is. Nothing but the real arithmetic produces that.
        Interior(position).ShouldAllBe(value => Math.Abs(value) < 1e-3d);

        var roughness = Compute(source, "roughness.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Roughness,
        });

        // Roughness is the spread between the highest and lowest of a cell and its neighbours, which
        // on a plane rising by one step per row is two steps across three rows.
        Interior(roughness).ShouldAllBe(
            value => Math.Abs(value - (2d * RisePerPixel)) < RisePerPixel * 1e-4d);
    }

    [Fact]
    public void Ruggedness_is_recorded_as_whichever_of_the_two_definitions_was_used()
    {
        var source = Ramp("rugged.tif", northward: RisePerPixel, eastward: RisePerPixel / 3d);

        var riley = Compute(source, "tri-riley.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.RuggednessIndex,
            RuggednessFit = TerrainRuggednessFit.Riley,
        });

        var wilson = Compute(source, "tri-wilson.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.RuggednessIndex,
            RuggednessFit = TerrainRuggednessFit.Wilson,
        });

        // The two are different definitions giving different numbers for the same ground, which is
        // exactly why which one was used has to be stored beside the raster. If the choice were not
        // reaching the library these two would be identical.
        double one = Interior(riley)[0];
        double other = Interior(wilson)[0];
        one.ShouldBeGreaterThan(0d);
        other.ShouldBeGreaterThan(0d);
        Math.Abs(one - other).ShouldBeGreaterThan(0.5d);
    }

    [Fact]
    public void Elevation_can_be_painted_with_a_ramp_the_caller_supplies()
    {
        var source = Ramp("colour.tif", northward: 0, eastward: 0, baseHeight: 700);

        var relief = Compute(source, "colour-relief.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.ColourRelief,
            ColourRamp =
            [
                new TerrainColourStop(0d, 10, 20, 30),
                new TerrainColourStop(700d, 200, 100, 50),
                new TerrainColourStop(1400d, 250, 250, 250),
            ],
        });

        BandCount(relief.Path).ShouldBeGreaterThanOrEqualTo(3);

        // Ground sitting exactly on the middle stop is painted exactly that stop's colour.
        Interior(relief, band: 1).ShouldAllBe(value => Math.Abs(value - 200d) < 1d);
        Interior(relief, band: 2).ShouldAllBe(value => Math.Abs(value - 100d) < 1d);
        Interior(relief, band: 3).ShouldAllBe(value => Math.Abs(value - 50d) < 1d);

        // The ramp is written to a file for the library to read and is the caller's litter if it is
        // left behind. Nothing but the finished rasters and their sources stays in the directory.
        Directory.GetFiles(root).Select(Path.GetFileName)
            .ShouldAllBe(name => !name!.StartsWith("colours-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_computed_raster_is_placed_where_the_elevation_it_came_from_is()
    {
        var source = Ramp("placed.tif", northward: RisePerPixel, eastward: 0);

        var computed = Compute(source, "placed-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });

        computed.Width.ShouldBe(Size);
        computed.Height.ShouldBe(Size);
        computed.PixelSizeDegrees.ShouldBe(Pixel, 1e-12);
        computed.West.ShouldBe(West, 1e-9);
        computed.North.ShouldBe(North, 1e-9);
        computed.East.ShouldBe(West + (Pixel * Size), 1e-9);
        computed.South.ShouldBe(North - (Pixel * Size), 1e-9);
        computed.SizeBytes.ShouldBeGreaterThan(0);

        // Nothing of the working file is left beside the finished one: bytes sitting at the finished
        // name are indistinguishable from a finished raster, so the working name is never it. Asked
        // about this raster's own working name rather than about the directory, because the setting
        // that says where the raster library may work is one value for the whole process — another
        // piece of work running at the same time can legitimately leave its scratch here.
        File.Exists(computed.Path + TerrainRasterFiles.PartialSuffix).ShouldBeFalse();
        File.Exists(computed.Path).ShouldBeTrue();
    }

    [Fact]
    public void An_elevation_raster_that_is_not_there_is_a_named_failure_and_not_a_crash()
    {
        var missing = Path.Combine(root, "absent.tif");

        var failure = Should.Throw<TerrainBuildException>(() => derivatives.Compute(
            new TerrainDerivativeRequest(
                missing,
                Path.Combine(root, "absent-slope.tif"),
                new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope }),
            CancellationToken.None));

        failure.Code.ShouldBe(TerrainBuildFailures.SourceUnreadable);

        // And the positive case in the same test, so that a failure asserted here cannot be the
        // whole path being broken.
        var source = Ramp("present.tif", northward: RisePerPixel, eastward: 0);
        Compute(source, "present-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        }).SizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Settings_that_cannot_be_computed_are_refused_before_anything_is_written()
    {
        var source = Ramp("refused.tif", northward: RisePerPixel, eastward: 0);
        var output = Path.Combine(root, "refused-shade.tif");

        Should.Throw<ArgumentException>(() => derivatives.Compute(
            new TerrainDerivativeRequest(source, output, new TerrainDerivativeSettings
            {
                Derivative = TerrainDerivative.Hillshade,

                // A light below the horizon lights nothing.
                AltitudeDegrees = -10d,
            }),
            CancellationToken.None));

        File.Exists(output).ShouldBeFalse();

        // The same request with a light above the horizon writes the raster, so the refusal above is
        // about the setting rather than about the path being unusable.
        Compute(source, "refused-shade.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            AltitudeDegrees = 45d,
        }).SizeBytes.ShouldBeGreaterThan(0);
    }

    private ComputedTerrainRaster Compute(string source, string name, TerrainDerivativeSettings settings)
        => derivatives.Compute(
            new TerrainDerivativeRequest(source, Path.Combine(root, name), settings),
            CancellationToken.None);

    /// <summary>Everything but the outermost ring of cells, as plain numbers.</summary>
    /// <remarks>
    /// The edge is computed from a neighbourhood it only half has, so it is deliberately not what
    /// these assertions are made against — what is being checked is the arithmetic over the ground,
    /// not what the library extrapolates past the end of it.
    /// </remarks>
    private static float[] Interior(ComputedTerrainRaster raster, int band = 1)
    {
        using var dataset = Gdal.Open(raster.Path, Access.GA_ReadOnly);
        using var read = dataset.GetRasterBand(band);

        var width = raster.Width - 2;
        var height = raster.Height - 2;
        var values = new float[width * height];
        read.ReadRaster(1, 1, width, height, values, width, height, 0, 0);
        return values;
    }

    private static DataType BandType(string path)
    {
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        using var band = dataset.GetRasterBand(1);
        return band.DataType;
    }

    private static int BandCount(string path)
    {
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        return dataset.RasterCount;
    }

    /// <summary>
    /// A plane, rising by a chosen number of metres per pixel northward and eastward.
    /// </summary>
    /// <remarks>
    /// The whole point of the fixture: one plane has one steepness and one facing everywhere on it,
    /// both of which follow from the two numbers given here and neither of which needs the library
    /// to work out. Row zero is the northern edge, which is what the grid this writes says.
    /// </remarks>
    private string Ramp(string name, double northward, double eastward, double baseHeight = 500d)
    {
        var path = Path.Combine(root, name);
        var driver = Gdal.GetDriverByName("GTiff");

        using var dataset = driver.Create(path, Size, Size, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);

        using (var reference = new SpatialReference(null))
        {
            reference.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var samples = new float[Size * Size];
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                // Rows run southward from row zero, so a plane rising northward is highest at row
                // zero and each row below it is one step lower.
                samples[(row * Size) + column] =
                    (float)(baseHeight + ((Size - 1 - row) * northward) + (column * eastward));
            }
        }

        using (var band = dataset.GetRasterBand(1))
        {
            band.SetNoDataValue(TerrainRasterPreparation.VoidValue);
            band.WriteRaster(0, 0, Size, Size, samples, Size, Size, 0, 0);
        }

        dataset.FlushCache();
        return path;
    }
}
