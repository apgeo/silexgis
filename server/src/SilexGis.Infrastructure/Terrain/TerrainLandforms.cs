// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Computes the pictures of the ground that the raster library in this process cannot — today the
/// landform classes (geomorphons) — by handing an elevation raster to WhiteboxTools and reading
/// the classes back.
/// </summary>
/// <remarks>
/// <para>
/// What crosses to the program is elevation and nothing else: one raster of heights, in a directory
/// made for the run, and a handful of numbers. No cave position, no database credential and no path
/// outside that directory goes with it, so the program — run in a sandbox or in a container of its
/// own — learns nothing an anonymous reader of the elevation source could not.
/// </para>
/// <para>
/// The elevation is handed over on a metric grid. Lines of sight are measured in cells and the
/// flatness angle is a rise over a distance, so a grid in degrees, whose cells are narrower east to
/// west than north to south, would bend every angle by the cosine of the latitude. Each raster is
/// reprojected into the transverse Mercator zone of its own centre at a pixel of the same ground
/// area, classed there, and brought back by nearest neighbour onto exactly the grid it came from —
/// classes are names, and an interpolated class is not a class.
/// </para>
/// <para>
/// Each prepared raster is classed by itself, like every other picture of a build. A cell nearer a
/// raster's edge than the search distance sees only part of its lines of sight; that band is
/// classed from what it can see and may disagree with its neighbour across the seam.
/// </para>
/// </remarks>
public sealed class TerrainLandforms(IWhiteboxTools tools, IOptions<WhiteboxOptions> options)
{
    /// <summary>The name WhiteboxTools knows the landform classification by.</summary>
    public const string Tool = "Geomorphons";

    private const string ElevationFile = "elevation.tif";
    private const string ClassesFile = "landforms.tif";
    private const string BackOnGridFile = "landforms-4326.tif";

    /// <summary>Metres in a degree of latitude, near enough for choosing a pixel size.</summary>
    private const double MetresPerDegree = 111_320d;

    /// <summary>The hole marker handed to the program; any height a cave area can have sits far above it.</summary>
    private const double ElevationHole = -32768d;

    /// <summary>
    /// How the finished classes are written: cloud-optimised like every other picture, but with
    /// overviews made by nearest neighbour. The format's default averages neighbouring cells with a
    /// cubic curve, and a zoomed-out map reads the overviews, so a boundary between a ridge and a
    /// valley would be stored, and painted, as the classes whose numbers lie between them.
    /// </summary>
    internal static readonly string[] CloudOptimisedClasses =
        [.. GdalDemDerivatives.CloudOptimised, "-co", "OVERVIEW_RESAMPLING=NEAREST"];

    static TerrainLandforms() => GdalRuntime.Configure();

    /// <summary>Whether this installation can compute these pictures at all.</summary>
    public bool IsAvailable => tools.IsAvailable;

    /// <summary>Why it cannot, as a stable code, or null when it can.</summary>
    public string? UnavailableReason => tools.IsAvailable ? null : tools.UnavailableReason;

    /// <summary>Computes one landform raster from one prepared elevation raster.</summary>
    /// <exception cref="TerrainBuildException">
    /// The program is not available, did not finish in time, failed, or wrote something that is not
    /// a raster of classes. Its message is this application's own sentence; what the program printed
    /// is only in the log tail, because it can name paths on the server.
    /// </exception>
    /// <exception cref="JobFailedException">The analysis service could not be reached; the queue tries again.</exception>
    public async Task<ComputedTerrainRaster> ComputeAsync(TerrainDerivativeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        GdalDemDerivatives.RequireAbsolute(request.SourcePath, nameof(request.SourcePath));
        GdalDemDerivatives.RequireAbsolute(request.OutputPath, nameof(request.OutputPath));

        if (request.Settings.Derivative != TerrainDerivative.Geomorphons)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.Settings.Derivative, "Only landform classes are computed by the analysis program.");
        }

        if (TerrainDerivativeRules.Problem(request.Settings) is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        if (!tools.IsAvailable)
        {
            throw Unavailable(null);
        }

        ct.ThrowIfCancellationRequested();
        var settings = TerrainDerivativeRegistry.Normalise(request.Settings);
        var name = Path.GetFileName(request.SourcePath);
        var directory = Path.GetDirectoryName(request.OutputPath)!;
        Directory.CreateDirectory(directory);

        // The run's own directory, beside the finished raster on the disk sized for terrain: the
        // program reads and writes nothing else, and nothing in it outlives the run.
        var run = Path.Combine(directory, ".landforms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var partial = request.OutputPath + TerrainRasterFiles.PartialSuffix;
        GdalDemDerivatives.Delete(partial);
        try
        {
            // The raster library's scratch files go to the run's directory only while synchronous
            // work holds it: the setting is the calling thread's, and a hold kept across the
            // program's run would be released on whichever thread the run resumes on, leaving the
            // thread that started it pointing at a directory the end of this method deletes.
            ComputedTerrainRaster grid;
            double pixelMetres;
            using (GdalScratchDirectory.At(run))
            {
                grid = SourceGrid(request.SourcePath);
                pixelMetres = PixelMetres(grid.PixelSizeDegrees, (grid.South + grid.North) / 2d);
                Warp(
                    request.SourcePath,
                    Path.Combine(run, ElevationFile),
                    [
                        "-of", "GTiff", "-co", "COMPRESS=NONE", "-co", "TILED=NO",
                        "-t_srs", "EPSG:" + Whole(MetricZone((grid.West + grid.East) / 2d, (grid.South + grid.North) / 2d)),
                        "-tr", Invariant(pixelMetres), Invariant(pixelMetres),
                        "-r", "bilinear", "-ot", "Float32", "-dstnodata", Invariant(ElevationHole),
                    ],
                    $"The elevation in {name} could not be put on a metric grid for the landform classes.");
            }

            var search = TerrainGeomorphons.Cells(settings.GeomorphonSearchMetres!.Value, pixelMetres, atLeastOne: true);
            var skip = Math.Min(
                search - 1,
                TerrainGeomorphons.Cells(settings.GeomorphonSkipMetres ?? 0d, pixelMetres, atLeastOne: false));
            var arguments = new List<WhiteboxArgument>
            {
                WhiteboxArgument.Input("dem", ElevationFile),
                WhiteboxArgument.Output("output", ClassesFile),
                WhiteboxArgument.Number("search", search),
                WhiteboxArgument.Number("threshold", settings.GeomorphonFlatnessDegrees!.Value),
                WhiteboxArgument.Number("skip", skip),
                WhiteboxArgument.Switch("forms"),
            };

            WhiteboxRunResult result;
            try
            {
                result = await tools.RunAsync(new WhiteboxRunRequest(run, Tool, arguments, options.Value.Timeout), ct);
            }
            catch (WhiteboxUnavailableException e) when (e.InnerException is HttpRequestException unreachable)
            {
                // An analysis service that does not answer is usually one being restarted. Kept as
                // a failure the queue tries again, under a sentence fit to show.
                throw new JobFailedException(
                    "The analysis service did not answer; the landform classes will be tried again.", unreachable);
            }
            catch (WhiteboxUnavailableException e)
            {
                throw Unavailable(e);
            }
            catch (TimeoutException e)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.AnalysisTimedOut,
                    $"The landform classes of {name} took longer than this installation allows a run. "
                    + "A shorter search distance takes less time.",
                    inner: e);
            }

            if (!result.Succeeded)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed,
                    $"The landform classes could not be computed from {name}.",
                    Tail(result.OutputTail));
            }

            ComputedTerrainRaster computed;
            using (GdalScratchDirectory.At(run))
            {
                computed = PlaceOnGrid(Path.Combine(run, ClassesFile), Path.Combine(run, BackOnGridFile), partial, grid, name);
            }

            File.Move(partial, request.OutputPath, overwrite: true);
            return computed with { Path = request.OutputPath };
        }
        catch
        {
            GdalDemDerivatives.Delete(partial);
            throw;
        }
        finally
        {
            Discard(run);
        }
    }

    /// <summary>
    /// The transverse Mercator zone (WGS 84 / UTM) whose central meridian is nearest a point, as its
    /// EPSG code: 326xx north of the equator, 327xx south of it.
    /// </summary>
    internal static int MetricZone(double longitude, double latitude)
    {
        var zone = Math.Clamp((int)Math.Floor((longitude + 180d) / 6d) + 1, 1, 60);
        return (latitude >= 0d ? 32600 : 32700) + zone;
    }

    /// <summary>
    /// The side of a square metric pixel holding the same ground area as a pixel of
    /// <paramref name="degrees"/> at <paramref name="latitude"/>, to the centimetre.
    /// </summary>
    internal static double PixelMetres(double degrees, double latitude)
    {
        var northSouth = degrees * MetresPerDegree;
        var eastWest = northSouth * Math.Cos(latitude * Math.PI / 180d);
        return Math.Max(0.01, Math.Round(Math.Sqrt(northSouth * eastWest), 2));
    }

    /// <summary>
    /// Brings the program's classes back onto the grid the elevation came from and writes them as
    /// the finished, cloud-optimised raster.
    /// </summary>
    private static ComputedTerrainRaster PlaceOnGrid(
        string classes, string onGrid, string partial, ComputedTerrainRaster grid, string name)
    {
        // Back onto the grid the elevation came from, cell for cell, as whole class numbers
        // with zero standing for a hole: nothing else can be a class.
        Warp(
            classes,
            onGrid,
            [
                "-of", "GTiff", "-t_srs", "EPSG:" + Whole(TerrainRasterPreparation.TargetEpsg),
                "-te", Invariant(grid.West), Invariant(grid.South), Invariant(grid.East), Invariant(grid.North),
                "-ts", Whole(grid.Width), Whole(grid.Height),
                "-r", "near", "-ot", "Byte", "-dstnodata", "0",
            ],
            $"The landform classes of {name} could not be put back on the elevation's grid.");

        using (var placed = Gdal.OpenEx(onGrid, (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY), null, null, null))
        using (var cloudOptimised = new GDALTranslateOptions(CloudOptimisedClasses))
        {
            Gdal.ErrorReset();
            using var written = placed is null
                ? null
                : Gdal.wrapper_GDALTranslate(partial, placed, cloudOptimised, null, null);
            if (written is null)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed,
                    $"The landform classes of {name} could not be written.",
                    GdalDemDerivatives.Reason(null));
            }
        }

        return GdalDemDerivatives.Describe(partial)
            ?? throw new TerrainBuildException(
                TerrainBuildFailures.DerivativeFailed,
                $"What was computed from {name} is not a raster this application can go on with.",
                GdalDemDerivatives.Reason(null));
    }

    private static ComputedTerrainRaster SourceGrid(string path)
    {
        using (GdalDemDerivatives.OpenSource(path))
        {
            // Opened first so a missing or unreadable source fails with the coded reason the other
            // pictures give; described after, for the grid the classes are put back onto.
        }

        return GdalDemDerivatives.Describe(path)
            ?? throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                $"The elevation raster to class could not be read: {Path.GetFileName(path)}.");
    }

    private static void Warp(string source, string destination, string[] arguments, string sentence)
    {
        Gdal.ErrorReset();
        try
        {
            using var input = Gdal.OpenEx(source, (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY), null, null, null);
            using var warpOptions = new GDALWarpAppOptions(arguments);
            using var output = input is null ? null : Gdal.Warp(destination, [input], warpOptions, null, null);
            if (output is null)
            {
                throw new TerrainBuildException(TerrainBuildFailures.DerivativeFailed, sentence, GdalDemDerivatives.Reason(null));
            }
        }
        catch (Exception e) when (e is not TerrainBuildException and not OperationCanceledException)
        {
            throw new TerrainBuildException(TerrainBuildFailures.DerivativeFailed, sentence, GdalDemDerivatives.Reason(e.Message), e);
        }
    }

    private static TerrainBuildException Unavailable(Exception? inner) => new(
        TerrainBuildFailures.AnalysisUnavailable,
        "Landform classes are computed by WhiteboxTools, which this installation does not run at the moment. "
        + "Ask again once the administrator has set it up.",
        inner: inner);

    /// <summary>The end of what the program printed, short enough to keep in a log.</summary>
    private static string? Tail(string? printed)
    {
        const int Keep = 2000;
        if (string.IsNullOrWhiteSpace(printed))
        {
            return null;
        }

        var text = printed.Trim();
        return text.Length <= Keep ? text : text[^Keep..];
    }

    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in the picture's version directory, which the next version's commit removes.
        }
    }
}
