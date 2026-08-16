// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The raster chain, on rasters built here rather than committed as fixtures.
///
/// <para>
/// Every assertion is a number read back out of the file that was written — its coordinate system,
/// the ground it covers, its pixel size, the value it uses for a hole — and never the absence of an
/// exception. Everything this step can get wrong produces a perfectly readable raster: one in the
/// wrong place, or one where a hole has become a plateau at minus thirty-two thousand metres. Both
/// of those go on to mesh into ground that draws without complaint.
/// </para>
///
/// <para>
/// The inputs are made here, in the test, for the same reason: a committed binary fixture proves
/// only that the file which was committed still opens, and cannot be read to see what it claims.
/// </para>
/// </summary>
public sealed class TerrainRasterPreparationTests : IDisposable
{
    /// <summary>The Romanian national projected grid, in metres.</summary>
    private const int Stereo70 = 31700;

    private const int LongitudeLatitude = 4326;

    private readonly string root =
        Path.Combine(Path.GetTempPath(), $"silexgis-prepare-{Guid.NewGuid():N}");

    private readonly GdalTerrainRasterPreparer preparer = new();

    public TerrainRasterPreparationTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    [Fact]
    public void A_raster_in_the_national_grid_comes_out_in_longitude_and_latitude()
    {
        // Thirty-metre pixels a little north-west of the grid's origin, which puts them in Romania.
        var input = Raster(
            "stereo70.tif", Stereo70, originX: 430_000, originY: 640_000,
            pixelSize: 30, width: 32, height: 32, sample: static (_, _) => 800f);

        var prepared = preparer.Prepare(Request([input]), CancellationToken.None).ShouldHaveSingleItem();

        // Named after the raster it was made from, because a build keeps them side by side.
        Path.GetFileName(prepared.Path).ShouldBe("stereo70.tif");
        AuthorityOf(prepared.Path).ShouldBe(LongitudeLatitude.ToString());

        // The grid's origin is 46 degrees north, 25 degrees east with a false easting and northing
        // of half a million metres, so seventy kilometres west and a hundred and forty north of it
        // lands here. The window is wide because a grid-free datum shift is good to a few metres,
        // not because the answer is uncertain by a tenth of a degree.
        prepared.West.ShouldBeInRange(24.0, 24.2);
        prepared.East.ShouldBeInRange(24.0, 24.2);
        prepared.North.ShouldBeInRange(47.2, 47.3);
        prepared.South.ShouldBeInRange(47.2, 47.3);
        prepared.East.ShouldBeGreaterThan(prepared.West);
        prepared.North.ShouldBeGreaterThan(prepared.South);

        // Degrees now, not metres: thirty metres is about a third of a thousandth of a degree.
        prepared.PixelSizeDegrees.ShouldBeInRange(0.0001, 0.001);

        SampleAt(prepared.Path, (prepared.West + prepared.East) / 2, (prepared.South + prepared.North) / 2)
            .ShouldBe(800f, 0.5f);
    }

    [Fact]
    public void A_hole_comes_out_as_the_one_value_the_pipeline_uses_for_one()
    {
        // A raster that says nothing-known with a number of its own, as most published elevation
        // data does, and a square of it that means exactly that.
        const double ItsOwnVoidValue = -32768d;
        var input = Raster(
            "voids.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 32, height: 32,
            sample: static (x, y) => x >= 8 && x < 24 && y >= 8 && y < 24 ? (float)ItsOwnVoidValue : 600f,
            noData: ItsOwnVoidValue);

        var prepared = preparer.Prepare(Request([input]), CancellationToken.None).ShouldHaveSingleItem();

        prepared.VoidValue.ShouldBe(TerrainRasterPreparation.VoidValue);
        DeclaredVoidValue(prepared.Path).ShouldBe(TerrainRasterPreparation.VoidValue);

        // Read out of the middle of the hole. Left alone this reads as minus thirty-two thousand
        // metres of elevation, which meshes as a cliff to well below the centre of the earth.
        var hole = SampleAt(prepared.Path, 25.016, 45.984);
        hole.ShouldBe((float)TerrainRasterPreparation.VoidValue, 0.5f);
        hole.ShouldNotBe((float)ItsOwnVoidValue);

        // Retained as the form that can be read a window at a time over a network, which is what
        // makes keeping these worth the disk they take.
        LayoutOf(prepared.Path).ShouldBe("COG");
    }

    [Fact]
    public void Asking_for_a_coarser_pixel_halves_the_raster()
    {
        var input = Raster(
            "fine.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 64, height: 64, sample: static (_, _) => 400f);

        var asIs = preparer.Prepare(Request([input]), CancellationToken.None).ShouldHaveSingleItem();
        asIs.Width.ShouldBe(64);
        asIs.Height.ShouldBe(64);
        asIs.PixelSizeDegrees.ShouldBe(0.001, 1e-9);

        var coarser = preparer
            .Prepare(
                Request([input], targetPixelSizeDegrees: 0.002, directory: "coarser"),
                CancellationToken.None)
            .ShouldHaveSingleItem();

        coarser.PixelSizeDegrees.ShouldBe(0.002, 1e-9);

        // Half as many pixels each way. The output grid is snapped to a whole multiple of the pixel
        // size, so a raster whose corner does not sit on that grid gains a pixel at the edge.
        coarser.Width.ShouldBeInRange(32, 33);
        coarser.Height.ShouldBeInRange(32, 33);

        // The same ground, at a coarser step.
        coarser.West.ShouldBe(asIs.West, 0.002);
        coarser.North.ShouldBe(asIs.North, 0.002);
    }

    /// <summary>
    /// The case the whole feature exists for: a coarse blanket with a fine survey of one corner of
    /// it. They stay two rasters, each at its own pixel size.
    /// </summary>
    /// <remarks>
    /// The alternative — one sheet at the finest pixel any source offers — is what this asserts
    /// against, and it is not a matter of taste. It stretches the coarse data over the whole
    /// rectangle at a pixel size it has no detail to fill, so what is meshed from it is advertised
    /// as fine everywhere and is invented everywhere except over the survey; and it multiplies what
    /// is written by the square of the ratio between the two pixel sizes, which for half-metre data
    /// under a thirty-metre blanket is more than three thousand.
    /// </remarks>
    [Fact]
    public void Each_raster_is_prepared_on_its_own_and_keeps_its_own_pixel_size()
    {
        var coarse = Raster(
            "blanket.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.004, width: 32, height: 32, sample: static (_, _) => 100f);
        var fine = Raster(
            "island.tif", LongitudeLatitude, originX: 25.02, originY: 45.98,
            pixelSize: 0.001, width: 32, height: 32, sample: static (_, _) => 500f);

        var prepared = preparer.Prepare(Request([coarse, fine]), CancellationToken.None);

        prepared.Count.ShouldBe(2);
        prepared.Select(p => Path.GetFileName(p.Path))
            .ShouldBe(["blanket.tif", "island.tif"], ignoreOrder: true);

        var blanket = prepared.Single(p => Path.GetFileName(p.Path) == "blanket.tif");
        var island = prepared.Single(p => Path.GetFileName(p.Path) == "island.tif");

        // Each keeps what it had. Neither was dragged onto the other's grid.
        blanket.PixelSizeDegrees.ShouldBe(0.004, 1e-9);
        island.PixelSizeDegrees.ShouldBe(0.001, 1e-9);

        // And each covers only its own ground, so nothing was stretched to fill a shared rectangle.
        blanket.Width.ShouldBe(32);
        island.Width.ShouldBe(32);
        island.East.ShouldBeLessThan(blanket.East);

        SampleAt(blanket.Path, 25.10, 45.90).ShouldBe(100f, 0.5f);
        SampleAt(island.Path, 25.03, 45.97).ShouldBe(500f, 0.5f);
    }

    [Fact]
    public void Rasters_in_different_coordinate_systems_each_come_out_in_longitude_and_latitude()
    {
        // The realistic mixture: an open dataset published in longitude and latitude, with a survey
        // of one hillside published in the national projected grid on top of it.
        var blanket = Raster(
            "wide.tif", LongitudeLatitude, originX: 24.0, originY: 47.4,
            pixelSize: 0.01, width: 32, height: 32, sample: static (_, _) => 100f);
        var survey = Raster(
            "survey.tif", Stereo70, originX: 430_000, originY: 640_000,
            pixelSize: 30, width: 32, height: 32, sample: static (_, _) => 800f);

        var prepared = preparer.Prepare(Request([blanket, survey]), CancellationToken.None);

        prepared.Count.ShouldBe(2);
        foreach (var raster in prepared)
        {
            AuthorityOf(raster.Path).ShouldBe(LongitudeLatitude.ToString());
            DeclaredVoidValue(raster.Path).ShouldBe(TerrainRasterPreparation.VoidValue);
        }

        var converted = prepared.Single(p => Path.GetFileName(p.Path) == "survey.tif");
        SampleAt(converted.Path, (converted.West + converted.East) / 2,
            (converted.South + converted.North) / 2).ShouldBe(800f, 0.5f);

        // The survey lands inside the blanket's ground, which is what makes the pair the mixture it
        // is meant to be rather than two unrelated rasters.
        var wide = prepared.Single(p => Path.GetFileName(p.Path) == "wide.tif");
        converted.West.ShouldBeGreaterThan(wide.West);
        converted.East.ShouldBeLessThan(wide.East);
    }

    /// <summary>
    /// A raster whose band holds whole numbers is prepared like any other, and its values arrive.
    /// </summary>
    /// <remarks>
    /// Two-byte integers are the band type of a great deal of published elevation — the worldwide
    /// three-arcsecond set, and many national models — while the thirty-metre set this application
    /// fetches for itself is floating point. Mixing the two is the ordinary shape of "a better
    /// source over a coarser one", so a chain that could only handle one number type would fail on
    /// exactly the case the feature exists for, and would do it by quietly losing a raster rather
    /// than by refusing one.
    /// </remarks>
    [Fact]
    public void A_raster_of_whole_numbers_is_prepared_beside_one_of_fractions()
    {
        var blanket = Raster(
            "float-blanket.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.004, width: 32, height: 32, sample: static (_, _) => 100f);
        var survey = Raster(
            "int-survey.tif", LongitudeLatitude, originX: 25.02, originY: 45.98,
            pixelSize: 0.001, width: 32, height: 32, sample: static (_, _) => 800f,
            type: DataType.GDT_Int16);

        var prepared = preparer.Prepare(Request([blanket, survey]), CancellationToken.None);

        prepared.Count.ShouldBe(2);

        var converted = prepared.Single(p => Path.GetFileName(p.Path) == "int-survey.tif");
        SampleAt(converted.Path, 25.03, 45.97).ShouldBe(800f, 0.5f);
        BandTypeOf(converted.Path).ShouldBe(DataType.GDT_Float32);
    }

    /// <summary>
    /// A file whose bytes are a document naming other files is refused, however it is named.
    /// </summary>
    /// <remarks>
    /// The raster library identifies a file by its content, so a virtual mosaic called
    /// <c>.tif</c> opens as a virtual mosaic and is then read by dereferencing whatever absolute
    /// paths it names — which for an uploaded file would be a request, from an account that need
    /// only be allowed to start a build, to read a location on the server's own disk. Naming a
    /// location for the server to read is a full administrator's act in this application and is
    /// checked as one everywhere else, so it cannot be reachable by renaming a file.
    /// </remarks>
    [Fact]
    public void A_file_whose_bytes_name_other_files_is_refused_however_it_is_named()
    {
        var real = Raster(
            "elsewhere.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 16, height: 16, sample: static (_, _) => 700f);

        var disguised = Path.Combine(root, "innocent.tif");
        File.WriteAllText(
            disguised,
            $"""
            <VRTDataset rasterXSize="16" rasterYSize="16">
              <SRS>EPSG:4326</SRS>
              <GeoTransform>25.0, 0.001, 0.0, 46.0, 0.0, -0.001</GeoTransform>
              <VRTRasterBand dataType="Float32" band="1">
                <SimpleSource>
                  <SourceFilename relativeToVRT="0">{real}</SourceFilename>
                  <SourceBand>1</SourceBand>
                </SimpleSource>
              </VRTRasterBand>
            </VRTDataset>
            """);

        var refused = Should.Throw<TerrainBuildException>(
            () => preparer.Prepare(Request([disguised]), CancellationToken.None));

        refused.Code.ShouldBe(TerrainBuildFailures.SourceUnreadable);
        refused.Message.ShouldContain("innocent.tif");
        Directory.Exists(Path.Combine(root, "prepared")).ShouldBeFalse();

        // The positive half: the raster it pointed at goes through when it is handed over itself, so
        // what was refused was the disguise and not the ground.
        preparer.Prepare(Request([real], directory: "honest"), CancellationToken.None)
            .ShouldHaveSingleItem();
    }

    /// <summary>
    /// What is prepared is bounded by the ground the build asked for, both ways round.
    /// </summary>
    /// <remarks>
    /// The rectangle an administrator drags is the only thing standing between a mis-drawn box and
    /// hours of work and tens of gigabytes. A directory of a country's rasters read whole would
    /// prepare the country — retained for the life of the build — for a box over one hillside.
    /// </remarks>
    [Fact]
    public void Ground_outside_the_area_asked_for_is_not_prepared()
    {
        var wide = Raster(
            "county.tif", LongitudeLatitude, originX: 24.0, originY: 47.0,
            pixelSize: 0.01, width: 64, height: 64, sample: static (_, _) => 300f);
        var elsewhere = Raster(
            "another-county.tif", LongitudeLatitude, originX: 30.0, originY: 46.0,
            pixelSize: 0.01, width: 64, height: 64, sample: static (_, _) => 900f);

        var asked = new TerrainArea(24.10, 46.80, 24.20, 46.90);
        var prepared = preparer.Prepare(Request([wide, elsewhere], area: asked), CancellationToken.None);

        // The one that covers none of it was left out entirely rather than converted and ignored.
        prepared.ShouldHaveSingleItem();
        Path.GetFileName(prepared[0].Path).ShouldBe("county.tif");
        File.Exists(Path.Combine(root, "prepared", "another-county.tif")).ShouldBeFalse();

        // And the one that does cover it was cut down to it, give or take the small margin kept so
        // that nothing has to interpolate from an edge with nothing beyond it.
        var margin = TerrainRasterPreparation.ClipMarginDegrees + 0.01;
        prepared[0].West.ShouldBeInRange(asked.West - margin, asked.West);
        prepared[0].East.ShouldBeInRange(asked.East, asked.East + margin);
        prepared[0].South.ShouldBeInRange(asked.South - margin, asked.South);
        prepared[0].North.ShouldBeInRange(asked.North, asked.North + margin);
        prepared[0].Width.ShouldBeLessThan(64);

        // Still real data inside it, so what was asserted above is a cut and not an empty file.
        SampleAt(prepared[0].Path, 24.15, 46.85).ShouldBe(300f, 0.5f);
    }

    /// <summary>
    /// Ground inside a prepared raster's rectangle that no source pixel falls on comes out as the
    /// value meaning "nothing known", not as elevation zero.
    /// </summary>
    /// <remarks>
    /// Every raster written here is a north-up rectangle, and a source whose own grid is turned —
    /// which any reprojected one is, and many surveys are to begin with — cannot fill the corners of
    /// the rectangle drawn round it. Filled with zero those corners are ordinary data: a plain at
    /// sea level, meshed as smooth ground, with a cliff to it from the hillside beside it.
    /// </remarks>
    [Fact]
    public void Ground_no_pixel_of_the_source_covers_comes_out_as_a_hole()
    {
        var turned = Raster(
            "turned.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 32, height: 32, sample: static (_, _) => 650f,
            rotationDegrees: 10);

        var prepared = preparer.Prepare(Request([turned]), CancellationToken.None).ShouldHaveSingleItem();

        // The north-west corner of the rectangle, which for a grid turned ten degrees off north is
        // beyond every edge of the data.
        var corner = SampleAt(
            prepared.Path,
            prepared.West + (prepared.PixelSizeDegrees / 2),
            prepared.North - (prepared.PixelSizeDegrees / 2));

        corner.ShouldBe((float)TerrainRasterPreparation.VoidValue, 0.5f);

        // And the middle of it is the data, so what was read above is a corner and not an empty file.
        SampleAt(prepared.Path, (prepared.West + prepared.East) / 2,
            (prepared.South + prepared.North) / 2).ShouldBe(650f, 0.5f);
    }

    [Fact]
    public void A_build_whose_rasters_all_lie_elsewhere_is_refused_rather_than_prepared_empty()
    {
        var elsewhere = Raster(
            "far-away.tif", LongitudeLatitude, originX: 30.0, originY: 46.0,
            pixelSize: 0.01, width: 32, height: 32, sample: static (_, _) => 900f);

        var refused = Should.Throw<TerrainBuildException>(() => preparer.Prepare(
            Request([elsewhere], area: new TerrainArea(24.1, 46.8, 24.2, 46.9)),
            CancellationToken.None));

        refused.Code.ShouldBe(TerrainBuildFailures.NoRasters);
        refused.Message.ShouldContain("area");
    }

    /// <summary>
    /// A set of prepared rasters counts as done only when every one of them is there and whole.
    /// </summary>
    /// <remarks>
    /// This is the check a build resumed after its host restarted leans on. A run killed with three
    /// of ten rasters converted leaves three perfectly good files behind, and a check that found one
    /// and stopped looking would declare the other seven finished — ground that is simply absent,
    /// which meshes as smooth and reports nothing.
    /// </remarks>
    [Fact]
    public void A_prepared_set_is_finished_only_when_all_of_it_is_there_and_whole()
    {
        var first = Raster(
            "one.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 48, height: 48, sample: static (x, y) => 400f + x + y);
        var second = Raster(
            "two.tif", LongitudeLatitude, originX: 25.1, originY: 46.0,
            pixelSize: 0.001, width: 48, height: 48, sample: static (x, y) => 500f + x + y);

        var request = Request([first, second]);
        var prepared = preparer.Prepare(request, CancellationToken.None);
        prepared.Count.ShouldBe(2);

        // The positive case first, so what follows means something.
        preparer.DescribePrepared(request).ShouldNotBeNull().Count.ShouldBe(2);

        // One of them with its tail taken off — what a process killed mid-write leaves.
        var whole = new FileInfo(prepared[1].Path).Length;
        using (var truncate = new FileStream(prepared[1].Path, FileMode.Open, FileAccess.Write))
        {
            truncate.SetLength(whole / 3);
        }

        preparer.DescribePrepared(request).ShouldBeNull();

        // And one of them simply not there, which is what an interrupted run leaves.
        File.Delete(prepared[1].Path);
        preparer.DescribePrepared(request).ShouldBeNull();

        // Preparing again fills in what is missing and leaves what was already whole alone.
        var untouched = File.GetLastWriteTimeUtc(prepared[0].Path);
        preparer.Prepare(request, CancellationToken.None).Count.ShouldBe(2);
        File.GetLastWriteTimeUtc(prepared[0].Path).ShouldBe(untouched);
        preparer.DescribePrepared(request).ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public void A_raster_that_does_not_say_where_it_is_is_refused_by_name()
    {
        var noReference = Raster(
            "unplaced.tif", epsg: null, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 16, height: 16, sample: static (_, _) => 300f);

        var refused = Should.Throw<TerrainBuildException>(
            () => preparer.Prepare(Request([noReference]), CancellationToken.None));

        refused.Code.ShouldBe(TerrainBuildFailures.RasterNotGeoreferenced);
        refused.Message.ShouldContain("coordinate system");
        refused.Message.ShouldContain("unplaced.tif");

        // The same file with a coordinate system on it goes through, so what was refused was the
        // missing reference and not the file, the size or the path.
        var placed = Raster(
            "placed.tif", LongitudeLatitude, originX: 25.0, originY: 46.0,
            pixelSize: 0.001, width: 16, height: 16, sample: static (_, _) => 300f);

        var prepared = preparer
            .Prepare(Request([placed], directory: "placed-out"), CancellationToken.None)
            .ShouldHaveSingleItem();

        File.Exists(prepared.Path).ShouldBeTrue();
        AuthorityOf(prepared.Path).ShouldBe(LongitudeLatitude.ToString());
    }

    [Fact]
    public void A_raster_with_no_grid_at_all_is_refused_in_those_terms()
    {
        var noGrid = Path.Combine(root, "ungridded.tif");
        GdalBase.ConfigureAll();
        var driver = Gdal.GetDriverByName("GTiff");
        using (var dataset = driver.Create(noGrid, 16, 16, 1, DataType.GDT_Float32, null))
        {
            dataset.GetRasterBand(1).WriteRaster(0, 0, 16, 16, new float[16 * 16], 16, 16, 0, 0);
            dataset.FlushCache();
        }

        var refused = Should.Throw<TerrainBuildException>(
            () => preparer.Prepare(Request([noGrid]), CancellationToken.None));

        refused.Code.ShouldBe(TerrainBuildFailures.RasterNotGeoreferenced);
        refused.Message.ShouldContain("georeferencing");
        refused.Message.ShouldContain("ungridded.tif");
    }

    [Fact]
    public void Nothing_is_written_when_there_was_nothing_to_prepare()
    {
        var request = Request([]);

        var refused = Should.Throw<TerrainBuildException>(
            () => preparer.Prepare(request, CancellationToken.None));

        refused.Code.ShouldBe(TerrainBuildFailures.NoRasters);
        Directory.Exists(request.OutputDirectory).ShouldBeFalse();
        preparer.DescribePrepared(request).ShouldBeNull();
    }

    private TerrainRasterPrepareRequest Request(
        IReadOnlyList<string> inputs,
        TerrainArea? area = null,
        double? targetPixelSizeDegrees = null,
        string directory = "prepared") =>
        new(inputs, Path.Combine(root, directory), area, targetPixelSizeDegrees);

    /// <summary>
    /// A single-band elevation raster on disk, with an optional coordinate system and an optional
    /// value meaning nothing-known.
    /// </summary>
    private string Raster(
        string name,
        int? epsg,
        double originX,
        double originY,
        double pixelSize,
        int width,
        int height,
        Func<int, int, float> sample,
        double? noData = null,
        DataType type = DataType.GDT_Float32,
        double rotationDegrees = 0)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(root, name);
        var driver = Gdal.GetDriverByName("GTiff");

        using (var dataset = driver.Create(path, width, height, 1, type, null))
        {
            // A grid turned off north is written the way any grid is: the two off-diagonal terms of
            // the affine stop being zero. Nothing else about the file changes.
            var turn = rotationDegrees * Math.PI / 180;
            dataset.SetGeoTransform(
            [
                originX, pixelSize * Math.Cos(turn), pixelSize * Math.Sin(turn),
                originY, pixelSize * Math.Sin(turn), -pixelSize * Math.Cos(turn),
            ]);

            if (epsg is { } code)
            {
                using var reference = new SpatialReference("");
                reference.ImportFromEPSG(code);
                reference.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);
            }

            var band = dataset.GetRasterBand(1);
            if (noData is { } value)
            {
                band.SetNoDataValue(value);
            }

            var pixels = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    pixels[(y * width) + x] = sample(x, y);
                }
            }

            // Written as fractions whatever the band holds; the library converts on the way in,
            // which is what makes a whole-number band writable from one buffer shape.
            band.WriteRaster(0, 0, width, height, pixels, width, height, 0, 0);
            dataset.FlushCache();
        }

        return path;
    }

    private static string? AuthorityOf(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        using var reference = new SpatialReference(dataset.GetProjection());
        return reference.GetAuthorityCode(null);
    }

    private static double DeclaredVoidValue(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        dataset.GetRasterBand(1).GetNoDataValue(out var value, out var declared);
        declared.ShouldNotBe(0);
        return value;
    }

    private static DataType BandTypeOf(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        return dataset.GetRasterBand(1).DataType;
    }

    private static string? LayoutOf(string path)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        return dataset.GetMetadataItem("LAYOUT", "IMAGE_STRUCTURE");
    }

    /// <summary>The value at one point on the ground, read out of the file.</summary>
    private static float SampleAt(string path, double longitude, double latitude)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        var geoTransform = new double[6];
        dataset.GetGeoTransform(geoTransform);

        var x = (int)((longitude - geoTransform[0]) / geoTransform[1]);
        var y = (int)((latitude - geoTransform[3]) / geoTransform[5]);
        x.ShouldBeInRange(0, dataset.RasterXSize - 1);
        y.ShouldBeInRange(0, dataset.RasterYSize - 1);

        var buffer = new float[1];
        dataset.GetRasterBand(1).ReadRaster(x, y, 1, 1, buffer, 1, 1, 0, 0);
        return buffer[0];
    }
}
