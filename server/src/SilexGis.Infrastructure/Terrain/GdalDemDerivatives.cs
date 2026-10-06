// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using OSGeo.GDAL;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Shaded relief, steepness, facing and the rest of the pictures the bundled raster library can
/// draw from an elevation raster, computed inside this process.
/// </summary>
/// <remarks>
/// <para>
/// In-process rather than by invoking a command-line tool, for the same reason the rest of the
/// raster chain is: the library is already here, and shelling out would add an installation
/// requirement to a step that would otherwise have none. What that costs is the honest limit of
/// this type — it can compute exactly what this library has a mode for, and nothing else. Three
/// things a reader might reasonably expect are therefore absent and are not approximated by
/// anything here: geomorphons, curvature, and flow accumulation and the wetness index derived from
/// it. The enumeration this takes its instructions from says the same thing in the same words, so
/// that neither half can quietly grow a member the other cannot honour.
/// </para>
/// <para>
/// The elevation these are computed from is a grid of degrees, whose cells are not square on the
/// ground. The three pictures that depend on distance across the ground — shaded relief, steepness
/// and facing — are therefore computed a run of rows at a time, each run told how far a degree
/// really spans at its own latitude, and written out on the grid they were read from. Nothing is
/// reprojected: that would resample the heights into a metric grid and the picture back out of it,
/// softening the relief twice, and would leave a facing measured from the metric grid's north
/// rather than from true north.
/// </para>
/// <para>
/// The library is used in return-code mode throughout this assembly: nothing calls
/// <c>Gdal.UseExceptions</c>, so a call that fails answers with <c>null</c> and leaves its reason in
/// an error slot of its own that nothing reads unless it is asked to. Every call below therefore
/// clears that slot immediately before it and reads it immediately after — the slot holds only the
/// last thing recorded, so a reason fetched a step later names a different failure, or none.
/// </para>
/// <para>
/// Every handle opened here is disposed before the method that opened it returns and nothing is
/// shared between calls. The wrappers are thin covers over native memory the garbage collector
/// cannot see, and a dataset handed between threads faults the whole process in a way .NET cannot
/// catch. This type holds no state and is therefore safe to share, but two computations running at
/// once are two separate sets of handles: nothing here serialises them, so whatever drives it must
/// either be single-threaded — which the queue that runs terrain work is — or hold a gate of its own.
/// </para>
/// </remarks>
public sealed class GdalDemDerivatives : ITerrainDerivativeComputer
{
    static GdalDemDerivatives() => GdalRuntime.Configure();

    /// <summary>The formats a source raster may be, which is what the preparation step writes.</summary>
    /// <remarks>
    /// Named rather than left to the library, which identifies a file by its content and among the
    /// things it can identify is a virtual mosaic: a small document naming other files by absolute
    /// path, that it would then read on this server's behalf. Sources here are files this
    /// application wrote into a build's own directory, so the strictness costs nothing and removes
    /// the question entirely.
    /// </remarks>
    private static readonly string[] SourceDrivers = ["GTiff", "COG"];

    /// <summary>Whether this installation's raster library can write what these computations produce.</summary>
    /// <remarks>
    /// The computation itself is a function of the library rather than a driver, so there is nothing
    /// to ask about it; what can be absent from a stripped build is the writer. Asserted by a test,
    /// because the whole plan for these layers rests on it and the alternative to finding out here
    /// is finding out from a job that fails on a machine nobody develops on.
    /// </remarks>
    public static bool DerivativeWriterAvailable => Gdal.GetDriverByName("COG") is not null;

    /// <inheritdoc />
    public ComputedTerrainRaster Compute(TerrainDerivativeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAbsolute(request.SourcePath, nameof(request.SourcePath));
        RequireAbsolute(request.OutputPath, nameof(request.OutputPath));

        if (TerrainDerivativeRules.Problem(request.Settings) is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        ct.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(request.OutputPath)!;
        Directory.CreateDirectory(directory);

        // The cloud-optimised writer cannot know a raster's overviews until it has written the image
        // once, so it works through a scratch file the size of its own output. Left to itself the
        // library puts that wherever the machine calls temporary, which on a server sized for terrain
        // is a small memory-backed disk shared with every other process. Here it goes where the
        // finished raster is going anyway, which is the disk somebody sized for this.
        using var scratch = GdalScratchDirectory.At(directory);

        var partial = request.OutputPath + TerrainRasterFiles.PartialSuffix;
        var rows = request.OutputPath + RowsSuffix + TerrainRasterFiles.PartialSuffix;
        var name = Path.GetFileName(request.SourcePath);
        var sentence = $"The ground could not be pictured from {name}.";
        var ramp = request.Settings.Derivative == TerrainDerivative.ColourRelief
            ? WriteColourRamp(request.Settings.ColourRamp, directory)
            : null;

        Delete(partial);
        Delete(rows);

        try
        {
            // Closed before the file is looked at or moved: what is written is not all on disk until
            // the handle that wrote it is gone.
            using (var source = OpenSource(request.SourcePath))
            {
                if (MeasuredAcrossTheGround(request.Settings.Derivative)
                    && Grid.Of(source) is { } grid)
                {
                    ComputeInRowBands(source, grid, rows, partial, request.Settings, sentence, ct);
                }
                else
                {
                    ComputeWhole(source, partial, request.Settings, ramp, sentence);
                }
            }

            var computed = Describe(partial)
                ?? throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed,
                    $"What was computed from {name} is not a raster this application can go on with.",
                    Reason(null));

            // Nothing appears at the finished name until the whole file has been written, closed and
            // read back. Bytes sitting at that name are indistinguishable from a finished raster, and
            // half a hillshade drawn over a map looks like a hole in the ground rather than a hole in
            // the file.
            File.Move(partial, request.OutputPath, overwrite: true);
            return computed with { Path = request.OutputPath };
        }
        catch
        {
            Delete(partial);
            throw;
        }
        finally
        {
            Delete(rows);

            if (ramp is not null)
            {
                Delete(ramp);
            }
        }
    }

    /// <summary>What the rows of a picture are gathered in while its runs are computed.</summary>
    /// <remarks>
    /// Beside the finished raster, under the name a file still being written always has here, so
    /// that it lands on the disk somebody sized for terrain and is never picked up as a raster by
    /// anything that reads the directory.
    /// </remarks>
    private const string RowsSuffix = ".rows";

    /// <summary>The whole raster in one call, exactly as the library's own tool would compute it.</summary>
    /// <remarks>
    /// For the pictures that involve no distance across the ground — ruggedness, topographic
    /// position and roughness are differences in height between a cell and its neighbours, and a
    /// colour relief is a height looked up in a ramp — and for a raster already placed in metres,
    /// whose cells the library measures correctly by itself.
    /// </remarks>
    private static void ComputeWhole(
        Dataset source, string partial, TerrainDerivativeSettings settings, string? ramp, string sentence)
    {
        using var options = new GDALDEMProcessingOptions(
            [.. ModeArguments(settings), .. CloudOptimised]);
        using var written = Produce(
            () => Gdal.wrapper_GDALDEMProcessing(
                partial, source, Mode(settings.Derivative), ramp, options, null, null),
            sentence);

        written.FlushCache();
    }

    /// <summary>
    /// A raster in degrees, computed a run of rows at a time so that every row is measured with
    /// the width a cell really has at its own latitude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each run is read with one row of its neighbours above and below, so the cells along its
    /// edges are computed from the same nine heights they would be in a single pass over the whole
    /// raster; those two extra rows are computed and thrown away. What is kept is written, in
    /// order, into one plain raster beside the output, and that is then written out
    /// cloud-optimised in a single step — the cloud-optimised writer cannot be appended to.
    /// </para>
    /// <para>
    /// Steepness and shaded relief are given the two distances a degree spans at the middle of
    /// the run, which is what the limit on a run's height is for. Facing is different, and not by
    /// choice: the library's facing mode takes no distance at all — it reads a direction straight
    /// off the differences between neighbouring cells as though every cell were square, refuses
    /// the two scales when they are offered alongside its own switches and ignores them
    /// otherwise, and takes no notice of the cell size the raster declares either. So the
    /// direction it answers is corrected here for the shape of the cell, row by row, which is
    /// exact and needs nothing from the ground but its latitude.
    /// </para>
    /// </remarks>
    private static void ComputeInRowBands(
        Dataset source,
        Grid grid,
        string rows,
        string partial,
        TerrainDerivativeSettings settings,
        string sentence,
        CancellationToken ct)
    {
        Dataset? gathered = null;
        try
        {
            foreach (var band in TerrainGroundScale.RowBands(grid.Width, grid.Height, grid.CellHeight))
            {
                ct.ThrowIfCancellationRequested();

                var top = Math.Max(0, band.FirstRow - 1);
                var bottom = Math.Min(grid.Height, band.FirstRow + band.RowCount + 1);
                var middle = grid.North - ((band.FirstRow + (band.RowCount / 2d)) * grid.CellHeight);

                using var window = Produce(
                    () => Rows(source, top, bottom - top, grid.Width), sentence);
                using var options = new GDALDEMProcessingOptions(
                [
                    .. ModeArguments(settings),
                    .. GroundDistances(settings.Derivative, middle),
                    .. InMemory,
                ]);
                using var pictured = Produce(
                    () => Gdal.wrapper_GDALDEMProcessing(
                        string.Empty, window, Mode(settings.Derivative), null, options, null, null),
                    sentence);

                gathered ??= Produce(() => Gathering(rows, source, pictured, grid), sentence);
                Keep(pictured, gathered, band, band.FirstRow - top, grid, settings.Derivative, sentence);
            }

            if (gathered is null)
            {
                throw new TerrainBuildException(TerrainBuildFailures.DerivativeFailed, sentence);
            }

            gathered.FlushCache();

            using var cloudOptimised = new GDALTranslateOptions(CloudOptimised);
            using var written = Produce(
                () => Gdal.wrapper_GDALTranslate(partial, gathered, cloudOptimised, null, null),
                sentence);

            written.FlushCache();
        }
        finally
        {
            // Before the caller deletes the file: a handle still open on it is a file that cannot
            // be removed on one of the two systems this runs on.
            gathered?.Dispose();
        }
    }

    /// <summary>A run of whole rows of the source, copied into memory.</summary>
    /// <remarks>
    /// A copy rather than a view onto the file, because what is handed to the computation must
    /// not outlive or share a handle with the raster it was cut from, and a run is bounded in
    /// size by the rule that plans them.
    /// </remarks>
    private static Dataset? Rows(Dataset source, int firstRow, int rowCount, int width)
    {
        using var options = new GDALTranslateOptions(
        [
            .. InMemory,
            "-srcwin",
            "0",
            Whole(firstRow),
            Whole(width),
            Whole(rowCount),
        ]);

        return Gdal.wrapper_GDALTranslate(string.Empty, source, options, null, null);
    }

    /// <summary>
    /// The plain raster a picture's rows are gathered in: the source's grid, holding what the
    /// computation produces.
    /// </summary>
    /// <remarks>
    /// Its type and its marker for a hole are taken from the first run computed rather than
    /// stated here, so that what is gathered is what the library would have written in one pass —
    /// whole bytes with zero for a hole for a shaded relief, floating point with the library's own
    /// marker for the rest.
    /// </remarks>
    private static Dataset? Gathering(string path, Dataset source, Dataset pictured, Grid grid)
    {
        using var first = pictured.GetRasterBand(1);
        var driver = Gdal.GetDriverByName("GTiff");
        var gathered = driver?.Create(
            path, grid.Width, grid.Height, pictured.RasterCount, first.DataType, ["BIGTIFF=IF_SAFER"]);
        if (gathered is null)
        {
            return null;
        }

        gathered.SetGeoTransform(grid.Transform);
        gathered.SetProjection(source.GetProjection());

        for (var index = 1; index <= pictured.RasterCount; index++)
        {
            using var from = pictured.GetRasterBand(index);
            using var to = gathered.GetRasterBand(index);
            from.GetNoDataValue(out var hole, out var marked);
            if (marked != 0)
            {
                to.SetNoDataValue(hole);
            }
        }

        return gathered;
    }

    /// <summary>
    /// Writes the rows of a run that belong to it — everything but the neighbours read along
    /// with it — into the raster the picture is gathered in.
    /// </summary>
    private static void Keep(
        Dataset pictured,
        Dataset gathered,
        TerrainRowBand band,
        int firstInWindow,
        Grid grid,
        TerrainDerivative derivative,
        string sentence)
    {
        // Floating point whatever the band holds: the library converts on the way out and on the
        // way in, and a shaded relief's whole bytes survive both exactly.
        var cells = new float[grid.Width * band.RowCount];

        for (var index = 1; index <= pictured.RasterCount; index++)
        {
            using var from = pictured.GetRasterBand(index);
            using var to = gathered.GetRasterBand(index);

            Gdal.ErrorReset();
            if (from.ReadRaster(
                    0, firstInWindow, grid.Width, band.RowCount, cells, grid.Width, band.RowCount, 0, 0)
                != CPLErr.CE_None)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
            }

            if (derivative == TerrainDerivative.Aspect)
            {
                from.GetNoDataValue(out var hole, out var marked);
                FaceTheGround(cells, band, grid, marked != 0 ? (float)hole : null);
            }

            Gdal.ErrorReset();
            if (to.WriteRaster(
                    0, band.FirstRow, grid.Width, band.RowCount, cells, grid.Width, band.RowCount, 0, 0)
                != CPLErr.CE_None)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
            }
        }
    }

    /// <summary>
    /// Turns each facing computed as though its cell were square into the direction that ground
    /// really faces, using the shape a cell has at its own row's latitude.
    /// </summary>
    /// <remarks>
    /// A cell holding the marker for a hole is left holding it: it is not a direction, and turning
    /// it would produce a perfectly plausible one.
    /// </remarks>
    private static void FaceTheGround(float[] cells, TerrainRowBand band, Grid grid, float? hole)
    {
        for (var row = 0; row < band.RowCount; row++)
        {
            var latitude = grid.North - ((band.FirstRow + row + 0.5d) * grid.CellHeight);
            var stretch = TerrainGroundScale.CellStretch(latitude, grid.CellWidth, grid.CellHeight);
            var start = row * grid.Width;

            for (var column = 0; column < grid.Width; column++)
            {
                var counted = cells[start + column];
                if (hole is { } marker && counted == marker)
                {
                    continue;
                }

                // Rounded to the precision it is stored at before it is compared: a direction a
                // hair short of a full turn rounds up to one, and a full turn is north.
                var facing = (float)TerrainGroundScale.FacingOnTheGround(counted, stretch);
                cells[start + column] = facing >= 360f ? 0f : facing;
            }
        }
    }

    /// <summary>
    /// Where a raster in degrees sits and how large its cells are, or nothing when it is not a
    /// north-up grid of degrees.
    /// </summary>
    /// <param name="Transform">The six numbers that place the grid, as the library holds them.</param>
    private sealed record Grid(
        int Width, int Height, double North, double CellWidth, double CellHeight, double[] Transform)
    {
        /// <remarks>
        /// Asked of the file rather than assumed, even though everything this is pointed at today
        /// is in longitude and latitude with north at the top. The scales depend on the answer,
        /// and getting it wrong does not fail: it produces a complete picture of ground a hundred
        /// thousand times steeper or flatter than it is. A raster that is rotated has rows that
        /// are not parallels, so it has no latitude per row to be corrected by; it is computed in
        /// one pass and judged by the check every computed raster goes through.
        /// </remarks>
        public static Grid? Of(Dataset source)
        {
            if (!IsGeographic(source))
            {
                return null;
            }

            var transform = new double[6];
            source.GetGeoTransform(transform);

            return transform[1] > 0 && transform[5] < 0 && transform[2] == 0 && transform[4] == 0
                ? new Grid(
                    source.RasterXSize,
                    source.RasterYSize,
                    transform[3],
                    transform[1],
                    -transform[5],
                    transform)
                : null;
        }
    }

    /// <summary>What the library calls each of these computations.</summary>
    /// <remarks>
    /// Spelled exactly as the library's own tool takes them, including the capitals: an unrecognised
    /// mode is refused rather than guessed at.
    /// </remarks>
    private static string Mode(TerrainDerivative derivative) => derivative switch
    {
        TerrainDerivative.Hillshade => "hillshade",
        TerrainDerivative.Slope => "slope",
        TerrainDerivative.Aspect => "aspect",
        TerrainDerivative.RuggednessIndex => "TRI",
        TerrainDerivative.PositionIndex => "TPI",
        TerrainDerivative.Roughness => "roughness",
        TerrainDerivative.ColourRelief => "color-relief",
        _ => throw new ArgumentOutOfRangeException(nameof(derivative), derivative, null),
    };

    /// <summary>
    /// The settings of one picture, in the form the library's own tools take them: everything
    /// that decides what is computed, and nothing about where the ground is or how it is written.
    /// </summary>
    private static IEnumerable<string> ModeArguments(TerrainDerivativeSettings settings)
    {
        if (settings.ComputeEdges)
        {
            yield return "-compute_edges";
        }

        switch (settings.Derivative)
        {
            case TerrainDerivative.Hillshade:
                yield return "-z";
                yield return Invariant(settings.ZFactor);
                yield return "-alt";
                yield return Invariant(settings.AltitudeDegrees);
                yield return "-alg";
                yield return Fit(settings.SurfaceFit);

                if (settings.Lighting == TerrainHillshadeLighting.Multidirectional)
                {
                    // Four lights at fixed directions, so there is no one direction to give — and
                    // the library refuses the request outright rather than ignoring a direction
                    // offered alongside this. Which is why the settings stored beside a finished
                    // raster have to say which lighting was used: a recorded direction on a
                    // multidirectional shading would be a fact about a picture that never had one.
                    yield return "-multidirectional";
                }
                else
                {
                    yield return "-az";
                    yield return Invariant(settings.AzimuthDegrees);
                }

                break;

            case TerrainDerivative.Slope:
                yield return "-alg";
                yield return Fit(settings.SurfaceFit);

                if (settings.SlopeUnit == TerrainSlopeUnit.Percent)
                {
                    yield return "-p";
                }

                break;

            case TerrainDerivative.Aspect:
                yield return "-alg";
                yield return Fit(settings.SurfaceFit);

                // Flat ground faces no direction. Left to itself the library marks it with the value
                // that stands for a hole, so a plateau reads as missing data rather than as a plateau
                // — and every downstream picture of it is a gap. Zero is north, which is a lie of a
                // kind, but a visible one that no reader mistakes for absence.
                yield return "-zero_for_flat";
                break;

            case TerrainDerivative.RuggednessIndex:
                yield return "-alg";
                yield return settings.RuggednessFit == TerrainRuggednessFit.Wilson
                    ? "Wilson"
                    : "Riley";
                break;

            case TerrainDerivative.PositionIndex:
            case TerrainDerivative.Roughness:
            case TerrainDerivative.ColourRelief:
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(settings), settings.Derivative, null);
        }
    }

    /// <summary>
    /// How far a degree spans in each direction at this latitude, for the two computations that
    /// divide a height by a distance across the ground.
    /// </summary>
    /// <remarks>
    /// Steepness and shaded relief divide a height difference in metres by a distance, and the
    /// distance they use is whatever the raster's own coordinates say. Told nothing, the library
    /// guesses a scale for a raster in degrees from the latitude of its middle, which is close
    /// for a small raster and a degree and a half of steepness out at the top and bottom of one
    /// five degrees tall. Stated here for the run of rows being computed, east–west and
    /// north–south separately, it is the ground's own.
    ///
    /// Facing is given none. The library's facing mode has no use for a distance, refuses these
    /// two alongside its own switches, and is corrected for the shape of a cell after it has
    /// answered.
    /// </remarks>
    private static IEnumerable<string> GroundDistances(TerrainDerivative derivative, double latitude)
    {
        if (derivative is not (TerrainDerivative.Hillshade or TerrainDerivative.Slope))
        {
            yield break;
        }

        yield return "-xscale";
        yield return Invariant(TerrainGroundScale.MetresPerDegreeOfLongitude(latitude));
        yield return "-yscale";
        yield return Invariant(TerrainGroundScale.MetresPerDegreeOfLatitude(latitude));
    }

    /// <summary>
    /// How a finished picture is written: the same way the elevation it was computed from is.
    /// </summary>
    /// <remarks>
    /// Cloud-optimised, so that a browser can read the part of it that is on screen over a range
    /// request instead of the whole file, and compressed, because a shaded relief of a region is
    /// mostly smooth.
    /// </remarks>
    private static readonly string[] CloudOptimised =
        ["-of", "COG", "-co", "COMPRESS=DEFLATE", "-co", "BIGTIFF=IF_SAFER"];

    /// <summary>A result held in memory rather than written anywhere.</summary>
    private static readonly string[] InMemory = ["-of", "MEM"];

    /// <summary>
    /// Whether this picture depends on how far apart cells are on the ground, and so has to be
    /// told when they are not as far apart east–west as they are north–south.
    /// </summary>
    private static bool MeasuredAcrossTheGround(TerrainDerivative derivative) =>
        derivative is TerrainDerivative.Hillshade or TerrainDerivative.Slope or TerrainDerivative.Aspect;

    private static string Fit(TerrainSurfaceFit fit) =>
        fit == TerrainSurfaceFit.ZevenbergenThorne ? "ZevenbergenThorne" : "Horn";

    /// <summary>
    /// Writes the colour ramp out in the plain text form the library's colour mode reads.
    /// </summary>
    /// <remarks>
    /// A file rather than an argument because the mode takes no other form of it. It is written into
    /// the directory the finished raster is going to, not into the machine's temporary directory:
    /// that directory is where this application already knows it may write, and on a server sized for
    /// terrain the temporary one is small and shared. Removed once the computation has finished,
    /// whether or not it succeeded.
    /// </remarks>
    private static string WriteColourRamp(IReadOnlyList<TerrainColourStop> ramp, string directory)
    {
        var path = Path.Combine(directory, $"colours-{Guid.NewGuid():N}.txt");
        var text = new StringBuilder();

        foreach (var stop in ramp.OrderBy(stop => stop.Elevation))
        {
            // Space-separated, invariant, one stop a line, exactly as the mode's own documentation
            // describes it. A decimal comma here would turn one stop into two numbers and the file
            // would be refused as malformed, several steps away from the machine setting that caused it.
            text.Append(Invariant(stop.Elevation)).Append(' ')
                .Append(Byte(stop.Red)).Append(' ')
                .Append(Byte(stop.Green)).Append(' ')
                .Append(Byte(stop.Blue)).Append(' ')
                .Append(Byte(stop.Alpha)).Append('\n');
        }

        File.WriteAllText(path, text.ToString());
        return path;

        static string Byte(byte value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the raster places itself in degrees rather than in a projected unit.</summary>
    private static bool IsGeographic(Dataset source)
    {
        try
        {
            Gdal.ErrorReset();
            using var reference = source.GetSpatialRef();
            return reference is not null && reference.IsGeographic() != 0;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Opens an elevation raster, refusing anything that is not one of the accepted forms.</summary>
    private static Dataset OpenSource(string path)
    {
        if (!File.Exists(path))
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                $"The elevation raster to picture is no longer there: {Path.GetFileName(path)}.");
        }

        Gdal.ErrorReset();
        Dataset? source;
        try
        {
            source = Gdal.OpenEx(
                path,
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: SourceDrivers,
                open_options: null,
                sibling_files: null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                $"The elevation raster to picture could not be read: {Path.GetFileName(path)}.",
                Reason(e.Message),
                e);
        }

        return source ?? throw new TerrainBuildException(
            TerrainBuildFailures.SourceUnreadable,
            $"The elevation raster to picture could not be read: {Path.GetFileName(path)}.",
            Reason(null));
    }

    /// <summary>What a computed raster turned out to be, or null when it is not usable.</summary>
    /// <remarks>
    /// Deliberately not the check the prepared elevation rasters go through. That one insists on one
    /// floating-point band whose hole marker is a particular negative number, which is right for
    /// elevation and wrong for every picture drawn from it: a shaded relief is whole bytes, a colour
    /// relief is three or four bands of them, and neither can hold that marker. What is asked here is
    /// only what everything downstream relies on — that it is placed on the earth, that it has a
    /// band, and that a value can actually be got out of the far corner, which is the part a file cut
    /// short does not have.
    /// </remarks>
    private static ComputedTerrainRaster? Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            Gdal.ErrorReset();
            using var written = Gdal.OpenEx(
                path,
                (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
                allowed_drivers: ["GTiff", "COG"],
                open_options: null,
                sibling_files: null);

            if (written is null || written.RasterCount < 1
                || written.RasterXSize <= 0 || written.RasterYSize <= 0)
            {
                return null;
            }

            var transform = new double[6];
            written.GetGeoTransform(transform);

            // A north-up grid with a real pixel size. The rotation terms are not supported by
            // anything downstream and a zero-sized pixel places the raster nowhere.
            if (transform[1] <= 0 || transform[5] >= 0 || transform[2] != 0 || transform[4] != 0)
            {
                return null;
            }

            using (var band = written.GetRasterBand(1))
            {
                if (!Readable(band, written.RasterXSize - 1, written.RasterYSize - 1))
                {
                    return null;
                }
            }

            var west = transform[0];
            var north = transform[3];
            var east = west + (transform[1] * written.RasterXSize);
            var south = north + (transform[5] * written.RasterYSize);

            return new ComputedTerrainRaster(
                path,
                written.RasterXSize,
                written.RasterYSize,
                transform[1],
                west,
                south,
                east,
                north,
                new FileInfo(path).Length);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Whether the value at one pixel can actually be got out of the file.</summary>
    /// <remarks>
    /// Asked at the far corner because that is the part a file cut short does not have. A raster
    /// whose header survived and whose data did not opens, measures and describes itself correctly;
    /// only reading it finds out. Asked for as a floating-point value whatever the band really
    /// holds — the library converts on the way out — so that one question serves a shaded relief,
    /// which is whole bytes, and a slope, which is not.
    /// </remarks>
    private static bool Readable(Band band, int x, int y)
    {
        var pixel = new float[1];
        Gdal.ErrorReset();
        return band.ReadRaster(x, y, 1, 1, pixel, 1, 1, 0, 0) == CPLErr.CE_None;
    }

    /// <summary>
    /// Runs one call into the raster library and answers what it produced, turning either way it can
    /// refuse into the same typed failure.
    /// </summary>
    /// <remarks>
    /// A refusal ordinarily comes back as nothing at all, with the reason left in a slot of the
    /// library's own. "Ordinarily" is why this catches as well: some paths through the native library
    /// raise instead. Left uncaught that arrives at the caller as a defect of this server rather than
    /// as a fact about the file.
    /// </remarks>
    private static Dataset Produce(Func<Dataset?> call, string sentence)
    {
        Dataset? produced;
        Gdal.ErrorReset();
        try
        {
            produced = call();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.DerivativeFailed, sentence, Reason(e.Message), e);
        }

        return produced ?? throw new TerrainBuildException(
            TerrainBuildFailures.DerivativeFailed, sentence, Reason(null));
    }

    /// <summary>What the library last said went wrong, falling back to what was raised instead.</summary>
    private static string? Reason(string? fallback)
    {
        var recorded = Gdal.GetLastErrorMsg();
        var reason = string.IsNullOrWhiteSpace(recorded) ? fallback : recorded.Trim();
        return string.IsNullOrWhiteSpace(reason) ? null : reason;
    }

    /// <summary>
    /// Numbers handed to the raster tools are written the way those tools read them, never the way
    /// this machine happens to be set up to write them: a decimal comma turns one argument into two.
    /// </summary>
    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void RequireAbsolute(string path, string argument)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException(
                "Paths given to the raster chain are absolute; a relative one would be read against "
                + "whatever directory the process happens to be running in.",
                argument);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A fragment that cannot be removed is not worth failing over; it is named so that it is
            // never mistaken for a finished raster, and the next run at the same name deletes it.
        }
    }
}
