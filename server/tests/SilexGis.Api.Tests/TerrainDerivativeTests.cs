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
/// only asks whether a file appeared. The fixture here is a tilted plane — one steepness, one
/// facing, chosen in advance — because both can be worked out with a calculator and neither is
/// what a wrapper that quietly computed the wrong thing, or passed no scale at all, would produce.
/// </para>
///
/// <para>
/// Every raster below is placed in longitude and latitude, which is what the elevation preparation
/// step writes, and the planes are tilted <b>on the ground</b>: the heights are laid out so that
/// the hillside really stands at the angle asked for, given how far apart the cells of a grid of
/// degrees are at that latitude — nearly half as far again north to south as east to west in the
/// Carpathians, and twice as far at 60° north. The distances come from the ellipsoid, written out
/// here from its two axes rather than borrowed from the code under test, so that a mistake in the
/// one cannot excuse the same mistake in the other.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class TerrainDerivativeTests : IDisposable
{
    /// <summary>Somewhere in the Carpathians.</summary>
    private const double West = 25.0;
    private const double Carpathians = 46d;

    /// <summary>Roughly a hundred metres of ground, north to south.</summary>
    private const double Pixel = 0.001;

    /// <summary>
    /// How close a steepness or a facing has to be to the ground's own, in degrees.
    /// </summary>
    /// <remarks>
    /// A tenth of what a reader could tell apart on a map and several times what is measured, so
    /// that it fails on a scale taken from a sphere or from the wrong latitude and not on the last
    /// digit of a float.
    /// </remarks>
    private const double Close = 0.05d;

    private const int Size = 24;

    /// <summary>The northern edge of a hillside in the Carpathians, which is centred on them.</summary>
    private const double North = Carpathians + (Size * Pixel / 2d);

    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-derivative-" + Guid.NewGuid().ToString("N"));

    private readonly GdalDemDerivatives derivatives = new();

    public TerrainDerivativeTests()
    {
        Directory.CreateDirectory(root);
        GdalRuntime.Configure();
    }

    public void Dispose()
    {
        Gdal.SetConfigOption("CPL_TMPDIR", TestScratch.Root);

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

    /// <summary>
    /// Ground tilted a known amount in a known direction is pictured as exactly that.
    /// </summary>
    /// <remarks>
    /// The directions are the ones that tell a correct computation from a plausible one. Due north
    /// is right whatever is done about the width of a cell; due east is where a width taken as
    /// equal to the height understates the steepness most; and anything between the compass points
    /// is where a facing read off square cells is turned. The last two rows are the same ground at
    /// 60° north, where a cell is twice as tall as wide and every one of those errors is larger.
    /// </remarks>
    [Theory]
    [InlineData(Carpathians, 45d, 90d)]
    [InlineData(Carpathians, 55d, 90d)]
    [InlineData(Carpathians, 35d, 60d)]
    [InlineData(Carpathians, 30d, 0d)]
    [InlineData(Carpathians, 45d, 225d)]
    [InlineData(Carpathians, 20d, 315d)]
    [InlineData(60d, 45d, 90d)]
    [InlineData(60d, 35d, 60d)]
    public void Ground_tilted_a_known_amount_in_a_known_direction_is_pictured_as_exactly_that(
        double latitude, double steepness, double facing)
    {
        var source = Hillside("tilted.tif", latitude, steepness, facing);

        var slope = Compute(source, "slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });

        Interior(slope).ShouldAllBe(value => Math.Abs(value - steepness) < Close);

        var aspect = Compute(source, "aspect.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        });

        Interior(aspect).ShouldAllBe(value => Turn(value, facing) < Close);
    }

    [Fact]
    public void Steepness_can_be_asked_for_as_a_percentage_instead_of_an_angle()
    {
        // Facing east, so that the answer depends on how wide a cell is taken to be.
        var source = Hillside("percent.tif", Carpathians, steepness: 45d, facing: 90d);

        var slope = Compute(source, "slope-percent.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            SlopeUnit = TerrainSlopeUnit.Percent,
        });

        // Rise equal to run is a hundred per cent, which is the same plane the previous test found
        // to be at forty-five degrees — the check that the unit changes the number and not the ground.
        Interior(slope).ShouldAllBe(value => Math.Abs(value - 100d) < 0.2d);
    }

    [Fact]
    public void Both_ways_of_working_out_a_slope_agree_on_a_plane()
    {
        var steepness = Math.Atan(0.5d) * 180d / Math.PI;
        var source = Hillside("fits.tif", Carpathians, steepness, facing: 60d);

        foreach (var fit in new[] { TerrainSurfaceFit.Horn, TerrainSurfaceFit.ZevenbergenThorne })
        {
            var slope = Compute(source, $"slope-{fit}.tif", new TerrainDerivativeSettings
            {
                Derivative = TerrainDerivative.Slope,
                SurfaceFit = fit,
            });

            var aspect = Compute(source, $"aspect-{fit}.tif", new TerrainDerivativeSettings
            {
                Derivative = TerrainDerivative.Aspect,
                SurfaceFit = fit,
            });

            // The two weightings differ on real, noisy ground and are both exact on a plane. The
            // expected values are the ground's own, so an argument that never arrived would not
            // be excused by both runs reading the same default.
            Interior(slope).ShouldAllBe(value => Math.Abs(value - steepness) < Close, fit.ToString());
            Interior(aspect).ShouldAllBe(value => Turn(value, 60d) < Close, fit.ToString());
        }
    }

    /// <summary>
    /// The computation this replaced gets the same ground wrong, by an amount nobody would call
    /// rounding.
    /// </summary>
    /// <remarks>
    /// Run here as it used to be — one distance for a degree in both directions, and a facing read
    /// straight off the grid — over exactly the hillsides the tests above are made of. Without
    /// this, those tests could be passing because the fixture is too gentle to tell the two
    /// computations apart, and the correction would be proven by nothing.
    /// </remarks>
    [Fact]
    public void One_distance_for_a_degree_in_both_directions_misreads_the_same_ground()
    {
        var eastward = Hillside("old-east.tif", Carpathians, steepness: 55d, facing: 90d);
        var between = Hillside("old-between.tif", Carpathians, steepness: 35d, facing: 60d);

        // A hillside standing at 55° and facing east, reported at about 45°.
        var steepness = AsItWasComputed(eastward, "slope", "-s", "111320");
        steepness.ShouldAllBe(value => Math.Abs(value - 55d) > 9d);
        steepness.ShouldAllBe(value => Math.Abs(value - 44.8d) < 0.5d);

        // And one facing 60° east of north, reported as facing 50°.
        var facing = AsItWasComputed(between, "aspect");
        facing.ShouldAllBe(value => Turn(value, 60d) > 9d);

        // The same two files, through what replaced it.
        Interior(Compute(eastward, "new-east.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        })).ShouldAllBe(value => Math.Abs(value - 55d) < Close);

        Interior(Compute(between, "new-between.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        })).ShouldAllBe(value => Turn(value, 60d) < Close);
    }

    /// <summary>
    /// A raster many runs of rows tall is right at its top and bottom rows, not only in the middle.
    /// </summary>
    /// <remarks>
    /// Five degrees from north to south, which is what a national elevation model handed over as
    /// one file looks like. A cell at its northern edge is a tenth narrower than one at its
    /// southern edge, so one scale for the whole raster — however well chosen — is a degree and a
    /// half of steepness out at both ends. The truth is worked out per row, because over this much
    /// latitude a field that rises evenly in degrees is not one steepness on the ground.
    /// </remarks>
    [Fact]
    public void A_raster_five_degrees_tall_is_right_at_its_northern_and_southern_edges()
    {
        const int columns = 48;
        const int rows = 1200;
        const double cell = 15d / 3600d;
        const double middle = 48.5d;
        var north = middle + (rows * cell / 2d);

        // Rising evenly in degrees towards the south-west: 45° and facing north-east at the middle.
        var perDegreeEast = -Math.Tan(Radians(45d)) * Math.Sin(Radians(45d)) * MetresPerDegreeEast(middle);
        var perDegreeNorth = -Math.Tan(Radians(45d)) * Math.Cos(Radians(45d)) * MetresPerDegreeNorth(middle);
        var source = Surface(
            "tall.tif", north, columns, rows, cell,
            (east, northing) => 400_000d + (perDegreeEast * east) + (perDegreeNorth * (northing - middle)));

        var slope = Compute(source, "tall-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        });
        var aspect = Compute(source, "tall-aspect.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Aspect,
        });

        var steepness = Rows(slope);
        var facing = Rows(aspect);
        var worstSteepness = 0d;
        var worstFacing = 0d;
        var spread = (Lowest: double.MaxValue, Highest: double.MinValue);

        for (var row = 1; row < rows - 1; row++)
        {
            var latitude = north - ((row + 0.5d) * cell);
            var eastward = perDegreeEast / MetresPerDegreeEast(latitude);
            var northward = perDegreeNorth / MetresPerDegreeNorth(latitude);
            var trueSteepness = Math.Atan(Math.Sqrt((eastward * eastward) + (northward * northward))) * 180d / Math.PI;
            var trueFacing = Math.Atan2(-eastward, -northward) * 180d / Math.PI;
            spread = (Math.Min(spread.Lowest, trueSteepness), Math.Max(spread.Highest, trueSteepness));

            for (var column = 1; column < columns - 1; column++)
            {
                worstSteepness = Math.Max(
                    worstSteepness, Math.Abs(steepness[(row * columns) + column] - trueSteepness));
                worstFacing = Math.Max(
                    worstFacing, Turn(facing[(row * columns) + column], trueFacing));
            }
        }

        // The ground really does change by more than the tolerance from one end to the other, so a
        // single answer for the whole raster could not have passed.
        (spread.Highest - spread.Lowest).ShouldBeGreaterThan(1d);

        worstSteepness.ShouldBeLessThan(0.1d);
        worstFacing.ShouldBeLessThan(0.1d);
    }

    /// <summary>
    /// Where one run of rows ends and the next begins there is no seam: no missing line, and no
    /// row computed from the wrong neighbours.
    /// </summary>
    /// <remarks>
    /// The ground here steepens steadily from north to south, so every row has a steepness of its
    /// own and a row written one place out, or computed without the row above it, is a wrong
    /// number rather than the same number in the wrong place. The outermost cells are left
    /// uncomputed on purpose: with them computed, the library fills a missing neighbour by
    /// carrying the slope on, which on smooth ground hides exactly the fault this is looking for.
    /// </remarks>
    [Fact]
    public void A_raster_taller_than_one_run_of_rows_is_pictured_without_a_seam()
    {
        const int columns = 20;
        const int rows = 500;
        const double cell = 3d / 3600d;
        var north = Carpathians + (rows * cell / 2d);

        TerrainGroundScale.RowBands(columns, rows, cell).Count.ShouldBeGreaterThan(3);

        // Heights that fall away as the square of the distance south of the northern edge: flat at
        // the top, and one step steeper with every row.
        const double bend = 150_000d;
        var source = Surface(
            "seam.tif", north, columns, rows, cell,
            (_, northing) => 30_000d - (bend * (north - northing) * (north - northing)));

        var slope = Compute(source, "seam-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
            ComputeEdges = false,
        });

        var steepness = Rows(slope);
        for (var row = 1; row < rows - 1; row++)
        {
            var latitude = north - ((row + 0.5d) * cell);
            var expected = Math.Atan(2d * bend * (north - latitude) / MetresPerDegreeNorth(latitude))
                * 180d / Math.PI;

            for (var column = 1; column < columns - 1; column++)
            {
                Math.Abs(steepness[(row * columns) + column] - expected)
                    .ShouldBeLessThan(0.02d, $"row {row}");
            }
        }

        // From nothing at the top to a real hillside at the bottom, so neighbouring rows differ by
        // several times what is tolerated above.
        ((double)steepness[((rows - 2) * columns) + 5]).ShouldBeGreaterThan(30d);
    }

    /// <summary>
    /// A hillside lit along its own surface is dark, and one lit square-on is as bright as a
    /// picture gets.
    /// </summary>
    /// <remarks>
    /// The one arrangement in which a shaded relief has an answer that can be stated in advance.
    /// Ground standing at 45° and facing east is square-on to a light in the east standing 45°
    /// above the horizon, and edge-on to the same light in the west. Read as gentler than it is —
    /// which is what one distance for both directions does to an east-facing slope — the same
    /// ground catches about a sixth of the light from the west.
    /// </remarks>
    [Fact]
    public void A_shaded_relief_lights_a_hillside_by_how_it_really_stands()
    {
        var source = Hillside("lit.tif", Carpathians, steepness: 45d, facing: 90d);

        var squareOn = Compute(source, "lit-east.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            AzimuthDegrees = 90d,
            AltitudeDegrees = 45d,
        });

        var edgeOn = Compute(source, "lit-west.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Hillshade,
            AzimuthDegrees = 270d,
            AltitudeDegrees = 45d,
        });

        Interior(squareOn).ShouldAllBe(value => value >= 254f);
        Interior(edgeOn).ShouldAllBe(value => value <= 2f);
    }

    [Fact]
    public void Flat_ground_faces_north_rather_than_reading_as_missing()
    {
        var source = Hillside("flat.tif", Carpathians, steepness: 0d, facing: 0d);

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
        var source = Hillside("shade.tif", Carpathians, steepness: 45d, facing: 180d);

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
        var source = Hillside("position.tif", Carpathians, steepness: 45d, facing: 180d);

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
        // on a plane rising by one step per row is two steps across three rows. A height
        // difference and not a steepness, so the step is a row's worth of rise whatever the
        // width of a cell.
        var step = Pixel * MetresPerDegreeNorth(Carpathians);
        Interior(roughness).ShouldAllBe(value => Math.Abs(value - (2d * step)) < step * 1e-3d);
    }

    [Fact]
    public void Ruggedness_is_recorded_as_whichever_of_the_two_definitions_was_used()
    {
        var source = Hillside("rugged.tif", Carpathians, steepness: 45d, facing: 200d);

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
        var source = Hillside("colour.tif", Carpathians, steepness: 0d, facing: 0d, baseHeight: 700d);

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
        var source = Hillside("placed.tif", Carpathians, steepness: 45d, facing: 180d);

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

        // Nor of the raster its rows were gathered in on the way: whatever else carries this
        // picture's name in the directory is something this computation wrote and did not remove.
        Directory.GetFiles(root).Select(Path.GetFileName)
            .Where(name => name!.StartsWith("placed-slope.tif", StringComparison.Ordinal))
            .ShouldBe(["placed-slope.tif"]);
    }

    /// <summary>
    /// A picture that cannot be finished leaves nothing of its working behind.
    /// </summary>
    /// <remarks>
    /// The failure is arranged at the last step, after every row has been computed and gathered:
    /// a directory is sitting at the name the finished raster is written under before it is
    /// renamed, so the writer has nowhere to write. That is the point at which the most has been
    /// left on disk, and a build's workspace is on a volume nothing sweeps.
    /// </remarks>
    [Fact]
    public void A_picture_that_cannot_be_finished_leaves_none_of_its_working_behind()
    {
        var source = Hillside("unfinished.tif", Carpathians, steepness: 45d, facing: 90d);
        var output = Path.Combine(root, "unfinished-slope.tif");
        var inTheWay = output + TerrainRasterFiles.PartialSuffix;
        Directory.CreateDirectory(inTheWay);

        var failure = Should.Throw<TerrainBuildException>(() => derivatives.Compute(
            new TerrainDerivativeRequest(
                source, output, new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope }),
            CancellationToken.None));

        failure.Code.ShouldBe(TerrainBuildFailures.DerivativeFailed);
        File.Exists(output).ShouldBeFalse();
        Directory.GetFiles(root).Select(Path.GetFileName)
            .Where(name => name!.StartsWith("unfinished-slope.tif", StringComparison.Ordinal))
            .ShouldBeEmpty();

        // With nothing in the way the same request is computed, so the failure above was the
        // obstruction and not the request.
        Directory.Delete(inTheWay);
        Compute(source, "unfinished-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        }).SizeBytes.ShouldBeGreaterThan(0);
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
        var source = Hillside("present.tif", Carpathians, steepness: 45d, facing: 180d);
        Compute(source, "present-slope.tif", new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Slope,
        }).SizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Settings_that_cannot_be_computed_are_refused_before_anything_is_written()
    {
        var source = Hillside("refused.tif", Carpathians, steepness: 45d, facing: 180d);
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

    /// <summary>Every cell of a computed raster, row after row from the north.</summary>
    private static float[] Rows(ComputedTerrainRaster raster)
    {
        using var dataset = Gdal.Open(raster.Path, Access.GA_ReadOnly);
        using var read = dataset.GetRasterBand(1);

        var values = new float[raster.Width * raster.Height];
        read.ReadRaster(0, 0, raster.Width, raster.Height, values, raster.Width, raster.Height, 0, 0);
        return values;
    }

    /// <summary>
    /// The interior of a picture computed by the library directly, with arguments given here.
    /// </summary>
    /// <remarks>
    /// For showing what a computation this application no longer performs would have answered.
    /// Held in memory and never written: it is evidence, not a picture.
    /// </remarks>
    private static float[] AsItWasComputed(string source, string mode, params string[] arguments)
    {
        using var elevation = Gdal.Open(source, Access.GA_ReadOnly);
        using var options = new GDALDEMProcessingOptions(["-of", "MEM", .. arguments]);
        using var pictured = Gdal.wrapper_GDALDEMProcessing(
            string.Empty, elevation, mode, null, options, null, null);
        pictured.ShouldNotBeNull(Gdal.GetLastErrorMsg());

        using var read = pictured.GetRasterBand(1);
        var width = pictured.RasterXSize - 2;
        var height = pictured.RasterYSize - 2;
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

    /// <summary>How far apart two compass bearings are, the short way round.</summary>
    private static double Turn(double one, double other)
    {
        var apart = Math.Abs(one - other) % 360d;
        return apart > 180d ? 360d - apart : apart;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;

    // The ellipsoid the rasters are placed on, by its two axes.
    private const double EquatorialRadius = 6_378_137d;
    private const double PolarRadius = 6_356_752.314245d;

    /// <summary>How many metres a degree of latitude spans at this latitude.</summary>
    private static double MetresPerDegreeNorth(double latitude)
    {
        var across = EquatorialRadius * Math.Cos(Radians(latitude));
        var along = PolarRadius * Math.Sin(Radians(latitude));
        return Math.Pow(EquatorialRadius * PolarRadius, 2d)
            / Math.Pow((across * across) + (along * along), 1.5d) * Math.PI / 180d;
    }

    /// <summary>How many metres a degree of longitude spans at this latitude.</summary>
    private static double MetresPerDegreeEast(double latitude)
    {
        var across = EquatorialRadius * Math.Cos(Radians(latitude));
        var along = PolarRadius * Math.Sin(Radians(latitude));
        return EquatorialRadius * EquatorialRadius / Math.Sqrt((across * across) + (along * along))
            * Math.Cos(Radians(latitude)) * Math.PI / 180d;
    }

    /// <summary>
    /// A plane standing at a chosen angle and facing a chosen direction on the ground, centred on
    /// a chosen latitude.
    /// </summary>
    /// <remarks>
    /// The whole point of the fixture: one plane has one steepness and one facing everywhere on it,
    /// both of which are the two numbers given here and neither of which needs the library to work
    /// out. The heights fall away in the direction faced, by the tangent of the steepness for every
    /// metre of ground — and a metre of ground is a different fraction of a degree east to west
    /// than north to south, which is what the two distances are for. Small enough that the width
    /// of a cell does not change across it by anything that matters.
    /// </remarks>
    private string Hillside(
        string name, double latitude, double steepness, double facing, double baseHeight = 5000d)
    {
        var fall = Math.Tan(Radians(steepness));
        var perDegreeEast = -fall * Math.Sin(Radians(facing)) * MetresPerDegreeEast(latitude);
        var perDegreeNorth = -fall * Math.Cos(Radians(facing)) * MetresPerDegreeNorth(latitude);
        var half = Size * Pixel / 2d;

        return Surface(
            name, latitude + half, Size, Size, Pixel,
            (east, northing) => baseHeight
                + (perDegreeEast * (east - half))
                + (perDegreeNorth * (northing - latitude)));
    }

    /// <summary>
    /// An elevation raster in longitude and latitude whose heights are given by a formula.
    /// </summary>
    /// <param name="north">The latitude of its northern edge. Row zero is the northernmost.</param>
    /// <param name="height">
    /// The height of a cell, from how many degrees east of the raster's western edge its centre is
    /// and the latitude of its centre.
    /// </param>
    private string Surface(
        string name, double north, int columns, int rows, double cell, Func<double, double, double> height)
    {
        var path = Path.Combine(root, name);
        var driver = Gdal.GetDriverByName("GTiff");

        using var dataset = driver.Create(path, columns, rows, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, cell, 0, north, 0, -cell]);

        using (var reference = new SpatialReference(null))
        {
            reference.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var samples = new float[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            var latitude = north - ((row + 0.5d) * cell);
            for (var column = 0; column < columns; column++)
            {
                samples[(row * columns) + column] = (float)height((column + 0.5d) * cell, latitude);
            }
        }

        using (var band = dataset.GetRasterBand(1))
        {
            band.SetNoDataValue(TerrainRasterPreparation.VoidValue);
            band.WriteRaster(0, 0, columns, rows, samples, columns, rows, 0, 0);
        }

        dataset.FlushCache();
        return path;
    }
}
