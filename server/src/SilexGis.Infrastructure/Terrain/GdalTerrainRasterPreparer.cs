// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using OSGeo.GDAL;
using OSGeo.OSR;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The raster chain, run inside this process on the bundled raster library: every raster a build
/// gathered converted on its own into longitude/latitude, with one value standing for a hole, cut
/// down to the ground that was asked for and optionally given a coarser pixel.
/// </summary>
/// <remarks>
/// <para>
/// Each source stays a raster of its own rather than being merged into one sheet. A merge forces a
/// single output grid across a mixed set: a fine survey over a coarse regional fill either has its
/// detail resampled away or drags the whole rectangle up to its own pixel size, which inflates the
/// coarse data into detail it does not have and multiplies what is written by orders of magnitude.
/// Keeping them apart is what lets whatever meshes them go deep over the survey and no deeper than
/// the fill supports elsewhere.
/// </para>
/// <para>
/// In-process rather than by invoking a command-line tool, because the library is already here — the
/// same one that converts uploaded rasters and resolves coordinate systems — and shelling out would
/// add an installation requirement to a step that would otherwise have none.
/// </para>
/// <para>
/// The library is used in return-code mode throughout this assembly: nothing calls
/// <c>Gdal.UseExceptions</c>, so a call that fails answers with <c>null</c> and writes its reason
/// into an error slot of its own that nothing reads unless it is asked to. Every call here therefore
/// clears that slot immediately before it and reads it immediately after, and the words it finds go
/// into the build's log tail rather than into the short reason, which is stored in a column a native
/// stack dump would overflow.
/// </para>
/// <para>
/// Every handle opened here is disposed before the method that opened it returns, and nothing is
/// shared between calls. The wrappers are thin covers over native memory the garbage collector
/// cannot see or account for, and a dataset or a coordinate transformation handed between threads
/// faults the whole process in a way .NET cannot catch.
/// </para>
/// </remarks>
public sealed class GdalTerrainRasterPreparer : ITerrainRasterPreparer
{
    static GdalTerrainRasterPreparer() => GdalRuntime.Configure();

    /// <summary>
    /// Working memory the reprojection is allowed, in megabytes.
    /// </summary>
    /// <remarks>
    /// Bounded on the call rather than by setting the library's process-wide cache, which would also
    /// change how every other thing in this process reads rasters — page rendering, uploads, map
    /// footprints — for the sake of one step. Left unbounded a reprojection of a region's worth of
    /// elevation takes as much memory as it can get, on a machine that is also serving the
    /// application.
    /// </remarks>
    private const int WarpMemoryMegabytes = 512;

    /// <summary>How many points are taken along each edge when a source's outline is transformed.</summary>
    /// <remarks>
    /// The outline of a projected rectangle is not a rectangle once it is in degrees — its edges
    /// bow — so the four corners alone understate it, by more the further from the projection's
    /// centre the raster is. Sampling the edges costs four transformations of a few dozen points
    /// and is what keeps a raster from being trimmed along a side that really did reach further.
    /// </remarks>
    private const int OutlineSamplesPerEdge = 16;

    /// <summary>
    /// The formats a source raster is allowed to be, named rather than left to the library.
    /// </summary>
    /// <remarks>
    /// The library identifies a file by its content, not by its name, and one of the formats it can
    /// identify is a virtual mosaic: a small XML document naming other files, by absolute path, that
    /// it will then read on this server's behalf — including paths that are not files at all, since
    /// the same library reads URLs and archives. Sources here arrive from an upload or from a
    /// directory an operator named, and reading a location off the server's own disk is deliberately
    /// a stricter question in this application than starting a build is. So the drivers are the
    /// elevation formats this pipeline accepts and no others, and a file whose bytes are something
    /// else is refused by name rather than opened.
    /// </remarks>
    private static readonly string[] SourceDrivers =
        ["GTiff", "COG", "HFA", "AAIGrid", "SRTMHGT", "USGSDEM"];

    /// <summary>The formats a prepared raster may be, which is the one this writes.</summary>
    private static readonly string[] PreparedDrivers = ["GTiff", "COG"];

    /// <inheritdoc />
    public IReadOnlyList<PreparedTerrainRaster> Prepare(
        TerrainRasterPrepareRequest request, CancellationToken ct)
    {
        var plan = Plan(request, ct);
        Directory.CreateDirectory(request.OutputDirectory);

        // The cloud-optimised writer cannot know a raster's overviews until it has written the image
        // once, so every conversion below goes through a scratch file the size of its own output —
        // gigabytes, for a build over any real amount of ground. Left to itself the library puts that
        // beside whatever the machine calls temporary, which in a container is the image's own
        // writable layer: it grows with the build, it is not the disk the operator sized for terrain,
        // and it disappears when the container is recreated. Here it goes where the finished raster
        // is going anyway, which is that disk.
        using var scratch = GdalScratchDirectory.At(request.OutputDirectory);

        var produced = new List<PreparedTerrainRaster>(plan.Count);
        foreach (var step in plan)
        {
            ct.ThrowIfCancellationRequested();

            // A run killed partway through leaves finished rasters behind, and they are as good as
            // any this run would write: the plan is a function of the same sources and the same
            // ground, so the file at this name was written for this step or is not accepted at all.
            produced.Add(Finished(step, request) ?? Convert(step, request));
        }

        return produced;
    }

    /// <inheritdoc />
    public IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(TerrainRasterPrepareRequest request)
    {
        IReadOnlyList<PlannedRaster> plan;
        try
        {
            plan = Plan(request, CancellationToken.None);
        }
        catch (TerrainBuildException)
        {
            // A source that cannot be read is not an answer to "is this already done"; it is a
            // failure, and it belongs to the run that follows, which reports it with the file's name.
            return null;
        }

        var prepared = new List<PreparedTerrainRaster>(plan.Count);
        foreach (var step in plan)
        {
            if (Finished(step, request) is not { } whole)
            {
                return null;
            }

            prepared.Add(whole);
        }

        return prepared;
    }

    /// <summary>One source, and what preparing it comes to.</summary>
    /// <param name="Clip">
    /// The ground to cut it down to, or null to convert it whole — which is the case whenever the
    /// area asked for already contains all of it, so that nothing is trimmed by the small error of
    /// an outline that had to be transformed to be compared.
    /// </param>
    private sealed record PlannedRaster(
        string SourcePath,
        string OutputPath,
        TerrainArea? Clip);

    /// <summary>What was learned about one input by opening it.</summary>
    private sealed record ProbedRaster(
        string Path,
        string Projection,
        double PixelSizeDegrees,
        TerrainArea? Outline);

    /// <summary>
    /// Works out what would be written for this request, opening every source and refusing the
    /// whole request if any of them cannot be read.
    /// </summary>
    /// <remarks>
    /// Everything is looked at before anything is written. A pile of rasters where the last one
    /// turns out to carry no coordinate system should fail before an hour of reprojection, not
    /// after it, and it should say which file it was. It is also the one place that decides what a
    /// finished run leaves on disk, so that the question "has this already been done" is answered
    /// against the same set the doing would produce.
    /// </remarks>
    private static IReadOnlyList<PlannedRaster> Plan(
        TerrainRasterPrepareRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAbsolute(request.OutputDirectory, nameof(request.OutputDirectory));

        if (request.InputPaths.Count == 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters,
                "There are no rasters to prepare.");
        }

        var sources = new List<ProbedRaster>(request.InputPaths.Count);
        foreach (var path in request.InputPaths.Order(StringComparer.Ordinal))
        {
            RequireAbsolute(path, nameof(request.InputPaths));
            ct.ThrowIfCancellationRequested();
            sources.Add(Inspect(path));
        }

        var wanted = request.Area?.Grown(TerrainRasterPreparation.ClipMarginDegrees);
        var planned = new List<PlannedRaster>(sources.Count);
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var name = TerrainRasterPreparation.PreparedNameFor(source.Path);
            if (taken.TryGetValue(name, out var already))
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.PrepareFailed,
                    $"Two of this build's rasters would be prepared under the same name: "
                    + $"{Path.GetFileName(already)} and {Path.GetFileName(source.Path)}. "
                    + "Rename one of them and start the build again.");
            }

            taken.Add(name, source.Path);

            if (!Wanted(source, wanted, out var clip))
            {
                continue;
            }

            planned.Add(new PlannedRaster(
                source.Path, Path.Combine(request.OutputDirectory, name), clip));
        }

        if (planned.Count == 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters,
                "None of the rasters this build was given covers the area it asked for.");
        }

        return planned;
    }

    /// <summary>
    /// Whether a source is worth converting for the ground asked for, and how much of it is.
    /// </summary>
    /// <remarks>
    /// A source whose outline could not be worked out is converted whole rather than dropped. The
    /// outline is an estimate — it comes from transforming a boundary — and the cost of the two
    /// mistakes is not symmetric: converting more ground than was asked for wastes disk, while
    /// dropping a raster that did reach the area leaves a hole that meshes as smooth ground with
    /// nothing anywhere reporting it.
    /// </remarks>
    private static bool Wanted(ProbedRaster source, TerrainArea? wanted, out TerrainArea? clip)
    {
        clip = null;

        if (wanted is null || source.Outline is not { } outline)
        {
            return true;
        }

        if (wanted.Holds(outline))
        {
            return true;
        }

        if (wanted.Meeting(outline) is not { } meeting)
        {
            return false;
        }

        // A sliver narrower than a pixel is not data; asked to write it the tools produce a raster
        // with no pixels in it at all, which fails the whole build over a raster that grazed the
        // corner of the rectangle.
        if (meeting.East - meeting.West < source.PixelSizeDegrees
            || meeting.North - meeting.South < source.PixelSizeDegrees)
        {
            return false;
        }

        clip = meeting;
        return true;
    }

    /// <summary>
    /// Opens a raster and establishes that it is one, that it says where on the earth it is, and
    /// what ground it covers.
    /// </summary>
    private static ProbedRaster Inspect(string path)
    {
        var name = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                $"A raster this build was going to prepare is no longer where it was: {name}.");
        }

        using var dataset = OpenSource(path);

        var geoTransform = new double[6];
        dataset.GetGeoTransform(geoTransform);
        if (IsUnplaced(geoTransform))
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.RasterNotGeoreferenced,
                $"A raster carries no georeferencing, so nothing says which ground its pixels cover: {name}.");
        }

        var projection = dataset.GetProjection();
        if (string.IsNullOrEmpty(projection))
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.RasterNotGeoreferenced,
                $"A raster carries no coordinate system, so nothing says where on the earth it is: {name}.");
        }

        return new ProbedRaster(
            path,
            projection,
            PixelSizeInDegrees(geoTransform, projection, name),
            Outline(geoTransform, dataset.RasterXSize, dataset.RasterYSize, projection, name));
    }

    /// <summary>Opens a source raster, refusing anything that is not one of the accepted formats.</summary>
    private static Dataset OpenSource(string path) => Produce(
        () => Gdal.OpenEx(
            path,
            (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY),
            allowed_drivers: SourceDrivers,
            open_options: null,
            sibling_files: null),
        TerrainBuildFailures.SourceUnreadable,
        $"A file could not be opened as an elevation raster: {Path.GetFileName(path)}. "
        + $"The formats read here are {string.Join(", ", TerrainRasterFiles.Accepted)}.");

    /// <summary>Whether a grid says nothing about where the pixels are.</summary>
    /// <remarks>
    /// A raster with no grid attached is not reported as one: reading the grid of a file that has
    /// none answers with the identity — pixels one unit wide, starting at zero, zero — and fails
    /// silently, so anything that reads the numbers alone sees a perfectly well-formed placement in
    /// the Atlantic off the coast of Africa. Both that identity and a zero-width pixel are refused.
    /// Elevation data covering exactly that square with exactly those pixels does not exist, and if
    /// it did it would be a file nobody had placed either.
    /// </remarks>
    private static bool IsUnplaced(double[] geoTransform) =>
        (geoTransform[1] == 0 && geoTransform[2] == 0)
        || (geoTransform is [0, 1, 0, 0, 0, 1]);

    /// <summary>
    /// The pixel size of a raster expressed in degrees, so that rasters described in degrees and
    /// rasters described in a projected grid can be measured against one rectangle.
    /// </summary>
    private static double PixelSizeInDegrees(double[] geoTransform, string projection, string name)
    {
        var pixel = Math.Abs(geoTransform[1]);
        using var reference = OpenReference(projection, name);

        if (reference.IsGeographic() != 0)
        {
            return pixel;
        }

        // A projected grid states its own unit; most are metres, some are feet.
        var metresPerUnit = reference.GetLinearUnits();
        return pixel * (metresPerUnit > 0 ? metresPerUnit : 1d) / TerrainRasterPreparation.MetresPerDegree;
    }

    /// <summary>
    /// The ground a raster covers, in longitude and latitude, or null where that cannot be worked
    /// out from what it declares.
    /// </summary>
    private static TerrainArea? Outline(
        double[] geoTransform, int width, int height, string projection, string name)
    {
        try
        {
            using var source = OpenReference(projection, name);
            using var target = new SpatialReference("");
            if (target.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg) != 0)
            {
                return null;
            }

            // Both sides are asked for longitude-first ordering explicitly. The authority defines
            // 4326 as latitude-first and the library honours that unless told otherwise, so leaving
            // it unsaid returns every pair swapped — which is not an error anywhere, just a raster
            // that appears to be somewhere it is not.
            source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

            using CoordinateTransformation? transform =
                source.IsSame(target, []) != 0 ? null : new CoordinateTransformation(source, target);

            double west = double.MaxValue, south = double.MaxValue;
            double east = double.MinValue, north = double.MinValue;
            var taken = 0;

            foreach (var (pixelX, pixelY) in Boundary(width, height))
            {
                var x = geoTransform[0] + (pixelX * geoTransform[1]) + (pixelY * geoTransform[2]);
                var y = geoTransform[3] + (pixelX * geoTransform[4]) + (pixelY * geoTransform[5]);

                if (transform is not null)
                {
                    var point = new double[3];
                    transform.TransformPoint(point, x, y, 0);

                    // This binding reports a point it could not transform in the values rather than
                    // in a return code: the coordinate comes back infinite.
                    if (!double.IsFinite(point[0]) || !double.IsFinite(point[1]))
                    {
                        continue;
                    }

                    (x, y) = (point[0], point[1]);
                }

                west = Math.Min(west, x);
                east = Math.Max(east, x);
                south = Math.Min(south, y);
                north = Math.Max(north, y);
                taken++;
            }

            // A handful of transformed corners is not an outline. Anything less than most of the
            // boundary means the coordinate system reaches somewhere this one cannot follow, and a
            // rectangle drawn round the part that did transform would be smaller than the raster.
            return taken >= 4 * OutlineSamplesPerEdge && east > west && north > south
                ? new TerrainArea(west, south, east, north)
                : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The projection library still raises on a pair of systems it cannot build a transform
            // between. Not knowing where a raster is, is an answer here: it is converted whole.
            return null;
        }
    }

    /// <summary>The pixel coordinates walked round the edge of a raster.</summary>
    private static IEnumerable<(double X, double Y)> Boundary(int width, int height)
    {
        for (var i = 0; i < OutlineSamplesPerEdge; i++)
        {
            var along = (double)i / OutlineSamplesPerEdge;
            yield return (along * width, 0);
            yield return (width, along * height);
            yield return ((1 - along) * width, height);
            yield return (0, (1 - along) * height);
        }
    }

    /// <summary>
    /// Reads a coordinate system out of what a raster declared.
    /// </summary>
    /// <remarks>
    /// Wrapped because the projection library still raises on a definition it cannot parse even
    /// though the bindings otherwise report failure by returning nothing, and a file carrying an
    /// unreadable coordinate system is a fact about that file rather than a defect here.
    /// </remarks>
    private static SpatialReference OpenReference(string projection, string name)
    {
        try
        {
            return new SpatialReference(projection);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.RasterNotGeoreferenced,
                $"A raster declares a coordinate system that could not be read: {name}.",
                e.Message,
                e);
        }
    }

    /// <summary>
    /// The prepared raster already sitting where this step would write one, if it is whole and is
    /// what this request asks for, and null otherwise.
    /// </summary>
    private PreparedTerrainRaster? Finished(PlannedRaster step, TerrainRasterPrepareRequest request)
    {
        if (Describe(step.OutputPath) is not { } existing)
        {
            return null;
        }

        // A raster left by a run that was asked for a different pixel size is whole and is still
        // the wrong file. Compared against what was asked for rather than against another file,
        // because the tools round a requested size to a grid.
        if (request.TargetPixelSizeDegrees is { } pixel && pixel > 0
            && Math.Abs(existing.PixelSizeDegrees - pixel) > pixel * 1e-6)
        {
            return null;
        }

        return existing;
    }

    /// <summary>
    /// Converts one source and writes the raster everything downstream reads.
    /// </summary>
    private PreparedTerrainRaster Convert(PlannedRaster step, TerrainRasterPrepareRequest request)
    {
        var name = Path.GetFileName(step.SourcePath);
        var partial = step.OutputPath + TerrainRasterFiles.PartialSuffix;
        Delete(partial);

        try
        {
            // Closed before the file is looked at or moved: what is written is not all on disk
            // until the handle that wrote it is gone.
            using (var input = OpenSource(step.SourcePath))
            using (var options = new GDALWarpAppOptions([.. WarpArguments(step, request)]))
            {
                using var written = Produce(
                    () => Gdal.Warp(partial, [input], options, null, null),
                    TerrainBuildFailures.PrepareFailed,
                    $"A raster could not be converted to longitude and latitude: {name}.");

                written.FlushCache();
            }

            var prepared = Describe(partial)
                ?? throw new TerrainBuildException(
                    TerrainBuildFailures.PrepareFailed,
                    $"What the conversion of {name} wrote is not a raster this pipeline can go on with.",
                    Reason(null));

            // Nothing appears at the finished name until the whole file has been written, closed and
            // read back. Bytes sitting at that name are indistinguishable from a finished raster,
            // and a truncated one meshes into ground that is smooth where it should have a hole.
            File.Move(partial, step.OutputPath, overwrite: true);
            return prepared with { Path = step.OutputPath };
        }
        catch
        {
            Delete(partial);
            throw;
        }
    }

    /// <summary>The conversion's arguments, in the form the library's own tools take them.</summary>
    private static IEnumerable<string> WarpArguments(
        PlannedRaster step, TerrainRasterPrepareRequest request)
    {
        // Reprojecting into longitude and latitude is done with what the projection library can work
        // out from the two coordinate systems alone. No transformation grids are installed and none
        // are added, so shifting a national projected grid onto the world datum is a calculation from
        // a handful of averaged parameters, accurate to a few metres and no better. For ground that
        // is looked at — a hillside drawn from above, where a few metres sideways is invisible — that
        // is entirely acceptable. It is not acceptable for survey coordinates, which are a different
        // path through this application entirely, done by the projector the survey import uses, and
        // they must never be routed through here.
        yield return "-t_srs";
        yield return TargetReference;

        yield return "-r";
        yield return TerrainRasterPreparation.Resampling;

        // Every prepared raster is 32-bit floating point whatever the input was. The value standing
        // for a hole is negative, and a byte or unsigned band cannot hold it — it would be written as
        // something inside the range of real elevations and then meshed as ground. It also keeps the
        // sub-metre detail of an airborne survey that an integer band would round away.
        yield return "-ot";
        yield return "Float32";

        yield return "-dstnodata";
        yield return Invariant(TerrainRasterPreparation.VoidValue);

        if (step.Clip is { } clip)
        {
            // In the target reference, which is what this is being warped into, so the numbers are
            // the same degrees the rectangle was drawn in.
            yield return "-te";
            yield return Invariant(clip.West);
            yield return Invariant(clip.South);
            yield return Invariant(clip.East);
            yield return Invariant(clip.North);
        }

        if (request.TargetPixelSizeDegrees is { } pixel && pixel > 0)
        {
            yield return "-tr";
            yield return Invariant(pixel);
            yield return Invariant(pixel);

            // Snapped to a whole multiple of the pixel size, so two rasters prepared separately at
            // the same size sit on one grid instead of half a pixel apart.
            yield return "-tap";
        }

        yield return "-of";
        yield return "COG";
        yield return "-co";
        yield return "COMPRESS=DEFLATE";
        yield return "-co";
        yield return "BIGTIFF=IF_SAFER";

        yield return "-multi";
        yield return "-wo";
        yield return "NUM_THREADS=ALL_CPUS";
        yield return "-wm";
        yield return WarpMemoryMegabytes.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One method behind two questions, deliberately: what a conversion just produced, and whether
    /// what an earlier run left behind may be gone on with. They are the same question and answering
    /// them in two places is how the two answers drift, until a run resumed after a restart accepts a
    /// file the run that wrote it would have refused.
    /// </remarks>
    public PreparedTerrainRaster? Describe(string path)
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
                allowed_drivers: PreparedDrivers,
                open_options: null,
                sibling_files: null);

            if (written is null || written.RasterCount < 1
                || written.RasterXSize <= 0 || written.RasterYSize <= 0)
            {
                return null;
            }

            if (!IsInTargetReference(written))
            {
                return null;
            }

            var geoTransform = new double[6];
            written.GetGeoTransform(geoTransform);
            if (IsUnplaced(geoTransform))
            {
                return null;
            }

            // Bands belong to the dataset that handed them out and are released with it.
            var band = written.GetRasterBand(1);
            band.GetNoDataValue(out var voidValue, out var declared);
            if (declared == 0 || voidValue != TerrainRasterPreparation.VoidValue)
            {
                return null;
            }

            if (!Readable(band, written.RasterXSize - 1, written.RasterYSize - 1))
            {
                return null;
            }

            var pixelWidth = Math.Abs(geoTransform[1]);
            var pixelHeight = Math.Abs(geoTransform[5]);
            var west = geoTransform[0];
            var north = geoTransform[3];

            return new PreparedTerrainRaster(
                path,
                written.RasterXSize,
                written.RasterYSize,
                pixelWidth,
                west,
                north - (pixelHeight * written.RasterYSize),
                west + (pixelWidth * written.RasterXSize),
                north,
                voidValue,
                new FileInfo(path).Length);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Bytes that are not a raster at all can make the library raise rather than answer, and
            // an unreadable file is an unreadable file however it made that known.
            return null;
        }
    }

    /// <summary>Whether a prepared raster says it is in longitude and latitude on the world datum.</summary>
    /// <remarks>
    /// Compared as coordinate systems rather than by the text of the declaration: the same reference
    /// is written out in several spellings depending on which version of which library produced the
    /// file, and comparing strings would reject a perfectly good raster prepared by a slightly
    /// different build.
    /// </remarks>
    private static bool IsInTargetReference(Dataset written)
    {
        var projection = written.GetProjection();
        if (string.IsNullOrEmpty(projection))
        {
            return false;
        }

        try
        {
            using var actual = new SpatialReference(projection);
            using var expected = new SpatialReference("");
            if (expected.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg) != 0)
            {
                return false;
            }

            return actual.IsSame(expected, []) != 0;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Whether the value at one pixel can actually be got out of the file.</summary>
    /// <remarks>
    /// Asked at the far corner because that is the part a file cut short does not have. A raster
    /// whose header survived and whose data did not opens, measures, and describes itself correctly;
    /// only reading it finds out. One pixel, so the cost of asking does not depend on how big the
    /// raster is.
    /// </remarks>
    private static bool Readable(Band band, int x, int y)
    {
        var pixel = new float[1];
        Gdal.ErrorReset();
        return band.ReadRaster(x, y, 1, 1, pixel, 1, 1, 0, 0) == CPLErr.CE_None;
    }

    private static string TargetReference { get; } =
        $"EPSG:{TerrainRasterPreparation.TargetEpsg.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Numbers handed to the raster tools are written the way those tools read them, never the way
    /// this machine happens to be set up to write them: a decimal comma turns one argument into two.
    /// </summary>
    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs one call into the raster library and answers what it produced, turning either way it
    /// can refuse into the same typed failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bindings are used in return-code mode across this assembly — nothing calls
    /// <c>Gdal.UseExceptions</c> — so a refusal ordinarily comes back as nothing at all, with the
    /// reason left in a slot of the library's own that nothing reads unless it is asked to.
    /// "Ordinarily" is why this catches as well: some paths through the native library raise
    /// instead, and being handed bytes that are not a raster at all is one of them. Left uncaught
    /// that arrives at the caller as a defect of this server rather than as a fact about the file,
    /// and the build is recorded with a code saying nothing anybody can act on.
    /// </para>
    /// <para>
    /// The slot is cleared immediately before the call and read immediately after it, because it
    /// holds only the last thing the library recorded and any further call overwrites it — a reason
    /// fetched a step later names a different failure, or none.
    /// </para>
    /// </remarks>
    private static Dataset Produce(Func<Dataset?> call, string code, string sentence)
    {
        Dataset? produced;
        Gdal.ErrorReset();
        try
        {
            produced = call();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new TerrainBuildException(code, sentence, Reason(e.Message), e);
        }

        return produced ?? throw new TerrainBuildException(code, sentence, Reason(null));
    }

    /// <summary>
    /// What the library last said went wrong, falling back to what was raised when it said nothing.
    /// </summary>
    private static string? Reason(string? fallback)
    {
        var recorded = Gdal.GetLastErrorMsg();
        var reason = string.IsNullOrWhiteSpace(recorded) ? fallback : recorded.Trim();
        return string.IsNullOrWhiteSpace(reason) ? null : reason;
    }

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
            // A fragment that cannot be removed is not worth failing a build over; it is named so
            // that nothing will mistake it for finished work.
        }
    }
}
