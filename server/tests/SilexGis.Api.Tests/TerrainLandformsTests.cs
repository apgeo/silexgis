// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The landform picture's round trip through the analysis program, with a stand-in for the program:
/// what is handed over (elevation on a metric grid, and nothing else), how the classes come back
/// (on exactly the grid the elevation came from, as whole class numbers), and how each way a run
/// can go wrong reaches the picture's row.
/// </summary>
public sealed class TerrainLandformsTests : IDisposable
{
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;
    private const int Size = 64;

    private readonly string root = Path.Combine(TestScratch.Root, $"silexgis-landforms-{Guid.NewGuid():N}");
    private readonly string source;
    private readonly string output;

    public TerrainLandformsTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "prepared"));
        source = Path.Combine(root, "prepared", "tile.tif");
        output = Path.Combine(root, "derivatives", "v1", "tile.tif");
        WriteElevation(source);
    }

    public void Dispose()
    {
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
    public async Task Only_elevation_on_a_metric_grid_is_handed_over_and_the_classes_come_back_on_the_source_grid()
    {
        var tools = new ClassingTools();
        var computed = await Landforms(tools).ComputeAsync(Request(), CancellationToken.None);

        // What crossed: one elevation raster, in metres, and the numbers that say how to read it.
        tools.Handed.ShouldBe(["elevation.tif"]);
        tools.HandedEpsg.ShouldBe(32635); // 25° east, north of the equator: UTM zone 35N
        tools.Arguments.Single(a => a.Name == "search").Value.ShouldBe(
            TerrainGeomorphons.Cells(500, TerrainLandforms.PixelMetres(Pixel, North - (Size * Pixel / 2)), true)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
        tools.Arguments.ShouldContain(a => a.Name == "forms" && a.Kind == WhiteboxArgumentKind.Flag);

        // What came back: the grid of the source, cell for cell, as whole class numbers.
        computed.Path.ShouldBe(output);
        computed.Width.ShouldBe(Size);
        computed.Height.ShouldBe(Size);
        computed.West.ShouldBe(West, 1e-9);
        computed.North.ShouldBe(North, 1e-9);
        computed.PixelSizeDegrees.ShouldBe(Pixel, 1e-12);

        using var written = Gdal.Open(output, Access.GA_ReadOnly);
        using var band = written.GetRasterBand(1);
        band.DataType.ShouldBe(DataType.GDT_Byte);
        var row = new byte[Size];
        band.ReadRaster(0, Size / 2, Size, 1, row, Size, 1, 0, 0);
        row[0].ShouldBe((byte)1);
        row[Size - 1].ShouldBe((byte)10);
        row.ShouldAllBe(v => v <= TerrainGeomorphons.LastClass);

        // Nothing of the run is left beside the finished raster.
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(output)!).ShouldBe([output]);
    }

    [Fact]
    public async Task A_run_that_cannot_happen_or_fails_names_its_own_reason_and_never_what_the_program_printed()
    {
        var notRun = await Should.ThrowAsync<TerrainBuildException>(
            () => Landforms(new NoWhiteboxTools()).ComputeAsync(Request(), CancellationToken.None));
        notRun.Code.ShouldBe(TerrainBuildFailures.AnalysisUnavailable);

        var tooLong = await Should.ThrowAsync<TerrainBuildException>(
            () => Landforms(new ThrowingTools(new TimeoutException("stopped"))).ComputeAsync(Request(), CancellationToken.None));
        tooLong.Code.ShouldBe(TerrainBuildFailures.AnalysisTimedOut);

        var failed = await Should.ThrowAsync<TerrainBuildException>(
            () => Landforms(new FakeWhiteboxTools { FailWith = 3 }).ComputeAsync(Request(), CancellationToken.None));
        failed.Code.ShouldBe(TerrainBuildFailures.DerivativeFailed);
        failed.Message.ShouldNotContain(root);
        failed.LogTail.ShouldNotBeNull();

        // An analysis service that does not answer is tried again by the queue, under a sentence fit to show.
        var silent = await Should.ThrowAsync<JobFailedException>(() => Landforms(new ThrowingTools(
                new WhiteboxUnavailableException("no answer", new HttpRequestException("refused"))))
            .ComputeAsync(Request(), CancellationToken.None));
        JobRetry.IsPassing(silent).ShouldBeTrue();
        silent.Message.ShouldNotContain("http");

        // A refusal from the service itself is a fact about the installation, not tried again.
        var refused = await Should.ThrowAsync<TerrainBuildException>(() => Landforms(new ThrowingTools(
                new WhiteboxUnavailableException("cannot run")))
            .ComputeAsync(Request(), CancellationToken.None));
        refused.Code.ShouldBe(TerrainBuildFailures.AnalysisUnavailable);
        JobRetry.IsPassing(refused).ShouldBeFalse();

        File.Exists(output).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(output)!).ShouldBeEmpty();
    }

    /// <summary>
    /// A map zoomed out reads a raster's overviews, not its cells. Overviews made by averaging would
    /// store a boundary between a ridge (3) and a valley (9) as the classes numbered between them —
    /// landforms nobody computed, painted under a legend that names them. So every overview may
    /// hold only values the full-resolution raster holds.
    /// </summary>
    [Fact]
    public async Task The_overviews_of_a_landform_raster_hold_only_classes_its_cells_hold()
    {
        // Wide enough that the cloud-optimised raster gets overviews at all.
        const int wide = 1200;
        var large = Path.Combine(root, "prepared", "wide.tif");
        WriteElevation(large, wide);
        var tools = new ClassingTools { Classes = (x, _, _) => (x / 3) % 2 == 0 ? 3 : 9 };
        var finished = Path.Combine(root, "derivatives", "v2", "wide.tif");

        await Landforms(tools).ComputeAsync(
            new TerrainDerivativeRequest(large, finished, new TerrainDerivativeSettings { Derivative = TerrainDerivative.Geomorphons }),
            CancellationToken.None);

        using var written = Gdal.Open(finished, Access.GA_ReadOnly);
        using var band = written.GetRasterBand(1);
        var cells = Values(band);
        cells.Except([(byte)0]).Order().ToArray().ShouldBe([(byte)3, (byte)9]);

        band.GetOverviewCount().ShouldBeGreaterThan(0);
        for (var level = 0; level < band.GetOverviewCount(); level++)
        {
            using var overview = band.GetOverview(level);
            Values(overview).Except(cells).ToArray().ShouldBeEmpty($"overview {level} holds a class no cell holds");
        }
    }

    /// <summary>
    /// The raster library's scratch directory is set for one thread. The program runs while this
    /// method waits, and the method resumes on another thread: a setting held across that wait
    /// would be left behind on the thread that started the run, pointing at the run's directory,
    /// which is deleted when the run ends — and every later writer on that thread would fail.
    /// </summary>
    [Fact]
    public async Task The_thread_that_starts_a_run_keeps_its_scratch_directory_when_the_run_waits()
    {
        var tools = new ClassingTools { Yields = true };
        string? before = null;
        string? after = null;
        Task<ComputedTerrainRaster>? computing = null;

        // A thread of its own, with nothing to resume on: the run carries on elsewhere once it waits,
        // and what it left on this thread can be read here.
        var starter = new Thread(() =>
        {
            GdalBase.ConfigureAll();
            before = Gdal.GetThreadLocalConfigOption("CPL_TMPDIR", null);
            computing = Landforms(tools).ComputeAsync(Request(), CancellationToken.None);
            after = Gdal.GetThreadLocalConfigOption("CPL_TMPDIR", null);
        });
        starter.Start();
        starter.Join();

        await computing.ShouldNotBeNull();
        tools.Yielded.ShouldBeTrue();
        after.ShouldBe(before);
    }

    /// <summary>
    /// The whole round trip through the real program, on a machine that has it: an invented bowl on
    /// a grid in degrees goes to the metric grid, through WhiteboxTools, and comes back on its own
    /// grid as whole class numbers, its lowest point a pit.
    /// </summary>
    [WhiteboxFact]
    public async Task The_real_program_classes_an_invented_bowl_back_onto_its_own_grid()
    {
        var bowl = Path.Combine(root, "prepared", "bowl.tif");
        WriteElevation(bowl, Size, (x, y) =>
        {
            var dx = x - (Size / 2);
            var dy = y - (Size / 2);

            // Rising with the square of the distance, so every line of sight from the centre curves up.
            return 500f + (2f * ((dx * dx) + (dy * dy)));
        });

        var sandboxed = OperatingSystem.IsLinux() && ExecutableSearch.OnPath("bwrap") is not null;
        var options = Options.Create(new WhiteboxOptions
        {
            Mode = WhiteboxOptions.ModeLocal,
            WhiteboxPath = WhiteboxFactAttribute.Program ?? "",
            AllowUnsandboxed = !sandboxed,
        });
        var tools = new LocalWhiteboxTools(options, NullLogger<LocalWhiteboxTools>.Instance);
        tools.IsAvailable.ShouldBeTrue(tools.UnavailableReason);

        var computed = await new TerrainLandforms(tools, options).ComputeAsync(
            new TerrainDerivativeRequest(bowl, output, new TerrainDerivativeSettings { Derivative = TerrainDerivative.Geomorphons }),
            CancellationToken.None);

        computed.Width.ShouldBe(Size);
        computed.Height.ShouldBe(Size);
        computed.West.ShouldBe(West, 1e-9);
        computed.North.ShouldBe(North, 1e-9);

        using var written = Gdal.Open(output, Access.GA_ReadOnly);
        using var band = written.GetRasterBand(1);
        band.DataType.ShouldBe(DataType.GDT_Byte);
        var classes = Values(band);
        classes.ShouldAllBe(c => c <= TerrainGeomorphons.LastClass);
        classes.Count(c => c != 0).ShouldBeGreaterThan(Size * Size / 2);

        var centre = new byte[1];
        band.ReadRaster(Size / 2, Size / 2, 1, 1, centre, 1, 1, 0, 0);
        centre[0].ShouldBe((byte)10);
    }

    /// <summary>
    /// The sentence a silent service is thrown with promises another attempt; once the queue has
    /// given up, the picture must say to ask again instead, under the analysis code.
    /// </summary>
    [Fact]
    public void A_service_silent_through_every_attempt_leaves_the_picture_saying_to_ask_again()
    {
        var silent = new JobFailedException(
            "The analysis service did not answer; the landform classes will be tried again.",
            new HttpRequestException("refused"));

        var (code, message) = TerrainDerivativeHandler.FinalFailure(silent);

        code.ShouldBe(TerrainBuildFailures.AnalysisUnavailable);
        message.ShouldBe("The analysis service did not answer. Ask for the picture again once it is running.");
        message.ShouldNotContain("tried again");

        TerrainDerivativeHandler.FinalFailure(new TerrainBuildException(TerrainBuildFailures.AnalysisTimedOut, "too long"))
            .ShouldBe((TerrainBuildFailures.AnalysisTimedOut, "too long"));
        TerrainDerivativeHandler.FinalFailure(new InvalidOperationException("/srv/secret/path"))
            .Message.ShouldNotContain("/srv");
    }

    [Theory]
    [InlineData(25.0, 46.1, 32635)]
    [InlineData(-0.5, 51.5, 32630)]
    [InlineData(-70.0, -33.0, 32719)]
    [InlineData(179.9, 10.0, 32660)]
    public void The_metric_grid_is_the_transverse_mercator_zone_of_the_raster_centre(double longitude, double latitude, int epsg) =>
        TerrainLandforms.MetricZone(longitude, latitude).ShouldBe(epsg);

    private TerrainDerivativeRequest Request() => new(
        source, output, new TerrainDerivativeSettings { Derivative = TerrainDerivative.Geomorphons });

    private static TerrainLandforms Landforms(IWhiteboxTools tools) =>
        new(tools, Options.Create(new WhiteboxOptions { Mode = WhiteboxOptions.ModeLocal }));

    private static byte[] Values(Band band)
    {
        var values = new byte[band.XSize * band.YSize];
        band.ReadRaster(0, 0, band.XSize, band.YSize, values, band.XSize, band.YSize, 0, 0);
        return values;
    }

    private static void WriteElevation(string path) => WriteElevation(path, Size);

    private static void WriteElevation(string path, int size, Func<int, int, float>? height = null)
    {
        height ??= (x, y) => 800f + (3f * x) + (2f * y);
        GdalBase.ConfigureAll();
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, size, size, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        using var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(-9999);
        var heights = new float[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                heights[(y * size) + x] = height(x, y);
            }
        }

        band.WriteRaster(0, 0, size, size, heights, size, size, 0, 0);
        dataset.FlushCache();
    }

    /// <summary>
    /// Stands in for the program: records what it was handed and writes "classes" that are, unless
    /// told otherwise, the column of the metric grid in ten equal bands, west to east, so where they
    /// land is checkable.
    /// </summary>
    private sealed class ClassingTools : IWhiteboxTools
    {
        /// <summary>The class of a cell of the metric grid, from its column, row and the grid's width.</summary>
        public Func<int, int, int, int> Classes { get; init; } = (x, _, width) => 1 + Math.Min(9, x * 10 / width);

        /// <summary>Whether a run first gives its thread back, as a real run waiting on a program does.</summary>
        public bool Yields { get; init; }

        public bool Yielded { get; private set; }

        public List<string> Handed { get; } = [];

        public int HandedEpsg { get; private set; }

        public IReadOnlyList<WhiteboxArgument> Arguments { get; private set; } = [];

        public string Mode => WhiteboxOptions.ModeLocal;

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public string? Version => "WhiteboxTools 0.0-classing";

        public async Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct)
        {
            if (Yields)
            {
                await Task.Yield();
                Yielded = true;
            }

            WhiteboxRequestRules.Problem(request.Tool, request.Arguments).ShouldBeNull();
            Handed.AddRange(Directory.EnumerateFileSystemEntries(request.WorkDirectory).Select(f => Path.GetFileName(f)));
            Arguments = request.Arguments;

            var input = Path.Combine(request.WorkDirectory, request.Arguments.Single(a => a.Kind == WhiteboxArgumentKind.Input).Value!);
            var outputName = request.Arguments.Single(a => a.Kind == WhiteboxArgumentKind.Output).Value!;
            using var dem = Gdal.Open(input, Access.GA_ReadOnly);
            using (var reference = dem.GetSpatialRef())
            {
                HandedEpsg = int.Parse(reference.GetAuthorityCode(null), System.Globalization.CultureInfo.InvariantCulture);
            }

            var width = dem.RasterXSize;
            var height = dem.RasterYSize;
            using var classes = Gdal.GetDriverByName("GTiff").Create(
                Path.Combine(request.WorkDirectory, outputName), width, height, 1, DataType.GDT_Float32, null);
            var transform = new double[6];
            dem.GetGeoTransform(transform);
            classes.SetGeoTransform(transform);
            classes.SetProjection(dem.GetProjection());
            using var band = classes.GetRasterBand(1);
            band.SetNoDataValue(-32768);
            var values = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    values[(y * width) + x] = Classes(x, y, width);
                }
            }

            band.WriteRaster(0, 0, width, height, values, width, height, 0, 0);
            classes.FlushCache();
            return new WhiteboxRunResult(true, 0, [outputName], "");
        }
    }

    private sealed class ThrowingTools(Exception failure) : IWhiteboxTools
    {
        public string Mode => WhiteboxOptions.ModeService;

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public string? Version => null;

        public Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct) => throw failure;
    }
}
