// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.TerrainDerivative"/> jobs.</summary>
/// <remarks>
/// The key of the row that asked for the picture and nothing else. Everything about what to
/// compute is on that row, so a job re-run after the settings were corrected computes what the row
/// now says rather than what the queue was told when it was written.
/// </remarks>
public sealed record TerrainDerivativePayload(Guid TerrainDerivativeLayerId);

/// <summary>
/// Draws one picture of the ground from every elevation raster of one build, and records what it
/// wrote against the row that asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on the job that normalises an uploaded raster — read the row, mark it in flight, work,
/// then write the outcome — but it stores its output differently, and deliberately. That one puts
/// its file in the store uploads go to and hangs it on the revision of the upload it converted.
/// This has no upload and no revision: it is drawn by the server from public elevation and has no
/// author to name. Its files go in the build's own folder, where the build's lifetime already
/// governs them.
/// </para>
/// <para>
/// Each recomputation writes into a directory named for the version it is producing, and the
/// previous one is taken away only once the new one is whole. A picture is served while it is being
/// recomputed, and overwriting in place would mean serving half a hillshade over ground somebody is
/// reading; and a computation that fails half way would leave a picture that had been correct
/// replaced by a partial one, with nothing but the row's status to say so.
/// </para>
/// </remarks>
public sealed class TerrainDerivativeHandler(
    SilexGisDbContext db,
    TerrainWorkspace workspace,
    TerrainRasterIndex index,
    ITerrainDerivativeComputer computer) : IProcessingJobHandler
{
    private static readonly GeometryFactory Geometry =
        new(new PrecisionModel(), TerrainRasterPreparation.TargetEpsg);

    public string Kind => ProcessingJobKinds.TerrainDerivative;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<TerrainDerivativePayload>(
                job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty terrain-derivative payload.");

        var layer = await db.TerrainDerivativeLayers
            .FirstOrDefaultAsync(l => l.Id == payload.TerrainDerivativeLayerId, ct);
        if (layer is null)
        {
            // Asked for and then withdrawn, or its build was deleted and took it with it. Nothing
            // to compute and nothing wrong: a job that fails here would report a defect where
            // somebody simply changed their mind.
            return;
        }

        layer.Status = TerrainDerivativeStatus.Computing;
        layer.ProcessingJobId = job.Id;
        layer.ErrorCode = null;
        layer.Message = null;
        await db.SaveChangesAsync(ct);

        var version = layer.Version + 1;
        var directory = VersionDirectory(layer.TerrainBuildId, layer.Id, version);
        try
        {
            var settings = JsonSerializer.Deserialize<TerrainDerivativeSettings>(
                    layer.Settings, JsonSerializerOptions.Web)
                ?? throw new TerrainBuildException(
                    TerrainBuildFailures.DerivativeFailed,
                    "What this picture was to be drawn with is no longer readable.");

            var coverage = await index.CoverageForAsync(layer.TerrainBuildId, ct);
            if (coverage.Rasters.Count == 0)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.NoRasters,
                    "That elevation build has no prepared rasters to draw the ground from.");
            }

            Directory.CreateDirectory(directory);

            var computed = new List<ComputedTerrainRaster>(coverage.Rasters.Count);
            var sources = new List<string>(coverage.Rasters.Count);
            foreach (var raster in coverage.Rasters)
            {
                ct.ThrowIfCancellationRequested();

                var output = Path.Combine(
                    directory, Path.GetFileNameWithoutExtension(raster.Path) + ".tif");
                computed.Add(computer.Compute(
                    new TerrainDerivativeRequest(raster.Path, output, settings), ct));
                sources.Add(raster.Path);
            }

            // Loaded and removed rather than deleted by a statement of its own, so that taking the
            // previous version's rows away, putting this one's in, and saying the picture is ready
            // are one commit. Two statements would leave a moment in which the picture is finished
            // and holds no files, and a reader arriving in it sees an empty layer rather than a
            // stale one.
            var previous = await db.TerrainDerivativeRasters
                .Where(r => r.TerrainDerivativeLayerId == layer.Id)
                .ToListAsync(ct);
            db.TerrainDerivativeRasters.RemoveRange(previous);

            var finished = DateTimeOffset.UtcNow;
            for (var i = 0; i < computed.Count; i++)
            {
                db.TerrainDerivativeRasters.Add(new TerrainDerivativeRaster
                {
                    TerrainDerivativeLayerId = layer.Id,
                    SourcePath = sources[i],
                    Path = computed[i].Path,
                    Width = computed[i].Width,
                    Height = computed[i].Height,
                    PixelSizeDegrees = computed[i].PixelSizeDegrees,
                    Footprint = Rectangle(computed[i]),
                    SizeBytes = computed[i].SizeBytes,
                    ComputedAt = finished,
                });
            }

            layer.Version = version;
            layer.SizeBytes = computed.Sum(c => c.SizeBytes);
            layer.ComputedAt = finished;
            layer.Status = TerrainDerivativeStatus.Ready;
            await db.SaveChangesAsync(ct);

            // Only now: what the previous version left is what was being served until this line.
            DiscardOtherVersions(layer.TerrainBuildId, layer.Id, version);
        }
        catch (Exception e)
        {
            layer.Status = TerrainDerivativeStatus.Failed;
            layer.ErrorCode = e is TerrainBuildException failed
                ? failed.Code
                : TerrainBuildFailures.DerivativeFailed;

            // Only a reason this application wrote is shown. Anything else is a native message or a
            // stack, which says nothing to the person reading and can quote a path off the server.
            layer.Message = e is TerrainBuildException named
                ? named.Message
                : "The ground could not be pictured from that elevation.";

            // Uncancelled on purpose: a run stopped part way must still leave a row that says so,
            // and one left reading "computing" is indistinguishable from one still running.
            await db.SaveChangesAsync(CancellationToken.None);

            Discard(directory);
            throw;
        }
    }

    /// <summary>Where this version of this picture's files go.</summary>
    /// <remarks>
    /// Under the build, then the picture, then the version. The version is part of the path rather
    /// than only a column so that the address of a file changes when its contents do: a reader
    /// holding the previous one keeps reading a whole raster until it asks again, instead of a file
    /// being rewritten underneath it.
    /// </remarks>
    private string VersionDirectory(Guid buildId, Guid layerId, int version) => Path.Combine(
        LayerDirectory(buildId, layerId),
        "v" + version.ToString(CultureInfo.InvariantCulture));

    private string LayerDirectory(Guid buildId, Guid layerId) =>
        Path.Combine(workspace.DerivativesFor(buildId), layerId.ToString("N"));

    private static Polygon Rectangle(ComputedTerrainRaster raster) =>
        Geometry.CreatePolygon(
        [
            new Coordinate(raster.West, raster.South),
            new Coordinate(raster.East, raster.South),
            new Coordinate(raster.East, raster.North),
            new Coordinate(raster.West, raster.North),
            new Coordinate(raster.West, raster.South),
        ]);

    private void DiscardOtherVersions(Guid buildId, Guid layerId, int keep)
    {
        var layerDirectory = LayerDirectory(buildId, layerId);
        if (!Directory.Exists(layerDirectory))
        {
            return;
        }

        var kept = VersionDirectory(buildId, layerId, keep);
        foreach (var directory in Directory.EnumerateDirectories(layerDirectory))
        {
            if (!string.Equals(directory, kept, StringComparison.Ordinal))
            {
                Discard(directory);
            }
        }
    }

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
            // Litter on a volume nothing else is waiting on, and not a reason to fail a picture that
            // is otherwise finished and correct.
        }
    }
}
