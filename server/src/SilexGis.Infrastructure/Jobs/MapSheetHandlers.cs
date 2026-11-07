// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Payload of <see cref="ProcessingJobKinds.MapSheetRender"/> and
/// <see cref="ProcessingJobKinds.MapPlacementWarp"/>. The page and the layers to draw are on the
/// map itself; only the resolution somebody asked for travels here, because the map records the
/// resolution that was actually used.
/// </summary>
public sealed record MapSheetPayload(Guid GeoreferencedMapId, double? Dpi = null);

/// <summary>
/// The work both sheet jobs share: a folder to work in, storing what was produced beside the
/// uploaded document, and publishing a placed sheet.
/// </summary>
public sealed class MapSheetPublisher(
    SilexGisDbContext db,
    DocumentWriteService documents,
    IFileStore fileStore,
    MapWarpService warper,
    IOptions<FilesOptions> files,
    IOptionsMonitor<GeoreferencedMapOptions> options)
{
    public GeoreferencedMapOptions Options => options.CurrentValue;

    /// <summary>
    /// A fresh folder for one job's intermediate files, beside the file store rather than in the
    /// system's temporary directory: a drawn sheet runs to gigabytes, which a memory-backed
    /// temporary directory cannot hold and a container's own layer should not.
    /// </summary>
    public string WorkDirectory(long jobId)
    {
        var root = Path.GetFullPath(files.Value.Root, AppContext.BaseDirectory);
        var path = Path.Combine(root, "scratch", string.Create(CultureInfo.InvariantCulture, $"map-sheet-{jobId}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Forgets whatever this job had added but not saved, so that recording its failure does not
    /// also record files that name bytes the failure left half-written.
    /// </summary>
    public void ForgetUnsaved()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public static void Remove(string workDirectory)
    {
        try
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover scratch folder is untidy, not wrong; the next job uses a new one.
        }
    }

    /// <summary>Stores a produced file as another file of the uploaded document's revision.</summary>
    public async Task<StoredFile> StoreAsync(
        StoredFile upload, string path, string suffix, string mimeType, FileKind kind, CancellationToken ct)
    {
        string storagePath;
        await using (var produced = File.OpenRead(path))
        {
            storagePath = await fileStore.SaveAsync(produced, Path.GetExtension(suffix), ct);
        }

        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        return documents.AddFile(upload.DocumentVersionId, new StoredContent(
            storagePath,
            Path.GetFileNameWithoutExtension(upload.OriginalName) + suffix,
            mimeType,
            new FileInfo(fileStore.GetAbsolutePath(storagePath)).Length,
            sha256,
            kind));
    }

    /// <summary>
    /// Warps the map's master by its placement and makes the result what the map draws. Tracked,
    /// not saved.
    /// </summary>
    public async Task PublishAsync(GeoreferencedMap map, StoredFile upload, string workDirectory, CancellationToken ct)
    {
        var placement = MapPlacement.FromJson(map.Placement)
            ?? throw new VectorIOException("The sheet has no placement.");
        var master = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == map.MasterFileId, ct)
            ?? throw new VectorIOException("The sheet has not been drawn.");

        var cogPath = Path.Combine(workDirectory, "placed.cog.tif");
        var outcome = warper.Publish(fileStore.GetAbsolutePath(master.StoragePath), placement, cogPath, workDirectory, Options, ct);
        var cog = await StoreAsync(upload, cogPath, ".cog.tif", "image/tiff", FileKind.Raster, ct);

        map.FileId = cog.Id;
        map.Bbox = outcome.Bbox4326;
        map.PlacementRmsMetres = outcome.RmsMetres is { } rms ? Math.Round((decimal)rms, 3) : null;
        map.Status = RasterStatus.Ready;
        map.ProcessingError = null;
    }

    /// <summary>The sheet's state when its placement is gone or failed: drawn, but not on the map.</summary>
    public static void Unpublish(GeoreferencedMap map)
    {
        map.FileId = map.SourceFileId ?? map.FileId;
        map.Bbox = null;
        map.PlacementRmsMetres = null;
        map.Status = RasterStatus.Unplaced;
    }
}

/// <summary>
/// Draws the page of an uploaded portable document that the map names into a kept master picture
/// and a small preview, and publishes the sheet when it has a placement — its own, from the
/// document, or one somebody set before it was drawn again.
/// </summary>
public sealed class MapSheetRenderHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    PdfSheetRenderService renderer,
    MapSheetPublisher publisher) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.MapSheetRender;

    public async Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
    {
        if (AbandonedJob.PayloadOf<MapSheetPayload>(job) is not { } payload)
        {
            return;
        }

        await db.GeoreferencedMaps
            .Where(m => m.Id == payload.GeoreferencedMapId && m.Status == RasterStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, RasterStatus.Failed)
                .SetProperty(m => m.ProcessingError, AbandonedJob.Reason), ct);
    }

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<MapSheetPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty map-sheet payload.");
        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == payload.GeoreferencedMapId, ct)
            ?? throw new InvalidOperationException($"Georeferenced map {payload.GeoreferencedMapId} no longer exists.");
        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == map.SourceFileId, ct)
            ?? throw new InvalidOperationException($"Map {map.Id} has no source document.");

        map.Status = RasterStatus.Processing;
        await db.SaveChangesAsync(ct);

        var options = publisher.Options;
        var work = publisher.WorkDirectory(job.Id);
        var drawn = false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(options.RenderTimeLimitMinutes));
        try
        {
            var layers = map.RenderLayers is null ? null : JsonSerializer.Deserialize<string[]>(map.RenderLayers);
            SheetRenderResult result;
            try
            {
                result = renderer.Render(
                    new SheetRenderRequest(
                        fileStore.GetAbsolutePath(upload.StoragePath), map.SourcePage ?? 1, layers, payload.Dpi, work),
                    options,
                    limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new VectorIOException(
                    $"Drawing the page took longer than {options.RenderTimeLimitMinutes} minutes; try a lower resolution.");
            }

            var master = await publisher.StoreAsync(upload, result.MasterPath, ".master.tif", "image/tiff", FileKind.Raster, ct);
            var preview = await publisher.StoreAsync(upload, result.PreviewPath, ".preview.webp", "image/webp", FileKind.Image, ct);

            map.SourcePage ??= 1;
            map.SourcePageCount = result.Info.PageCount;
            map.SourceLayers = JsonSerializer.Serialize(result.Info.Layers);
            map.RenderDpi = Math.Round((decimal)result.Dpi, 2);
            map.RenderWidth = result.Width;
            map.RenderHeight = result.Height;
            map.RenderWarnings = result.Warnings;
            map.MasterFileId = master.Id;
            map.PreviewFileId = preview.Id;

            // The document's own place is used unless somebody has placed the sheet by hand, which
            // outranks it until they remove their placement.
            map.EmbeddedPlacement = result.Info.Embedded is { } embedded
                ? PdfSheetRenderService.EmbeddedPlacement(embedded, result.Info.PointsWide, result.Info.PointsHigh).ToJson()
                : null;
            if (map.PlacementSource != PlacementSource.Manual)
            {
                map.Placement = map.EmbeddedPlacement;
                map.PlacementSource = map.EmbeddedPlacement is null ? null : PlacementSource.Embedded;
            }

            // The drawing is kept whatever happens to the placement: it is what a new placement
            // is warped from, and drawing it is the expensive half.
            MapSheetPublisher.Unpublish(map);
            map.Status = RasterStatus.Processing;
            await db.SaveChangesAsync(ct);
            drawn = true;

            if (map.Placement is not null)
            {
                await publisher.PublishAsync(map, upload, work, ct);
            }
            else
            {
                map.Status = RasterStatus.Unplaced;
            }

            map.ProcessingError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            publisher.ForgetUnsaved();
            if (drawn)
            {
                // Drawn, but its place could not be used: waiting for a placement, with the reason.
                MapSheetPublisher.Unpublish(map);
            }
            else
            {
                // Nothing of this run was kept, so nothing it set on the map may stay either: it
                // would name files that were never saved.
                await db.Entry(map).ReloadAsync(CancellationToken.None);
                map.Status = RasterStatus.Failed;
            }

            map.ProcessingError = Clip(e is VectorIOException ? e.Message : "Drawing the map sheet failed unexpectedly.");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            MapSheetPublisher.Remove(work);
        }
    }

    internal static string Clip(string message) => message.Length <= 2000 ? message : message[..2000];
}

/// <summary>
/// Publishes a drawn sheet where its placement says. A placement the library cannot use leaves
/// the sheet drawn and unplaced, with the reason, rather than failed: nothing is wrong with the
/// drawing, and a new placement is all it takes.
/// </summary>
public sealed class MapPlacementWarpHandler(SilexGisDbContext db, MapSheetPublisher publisher) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.MapPlacementWarp;

    public async Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
    {
        if (AbandonedJob.PayloadOf<MapSheetPayload>(job) is not { } payload)
        {
            return;
        }

        await db.GeoreferencedMaps
            .Where(m => m.Id == payload.GeoreferencedMapId && m.Status == RasterStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, RasterStatus.Unplaced)
                .SetProperty(m => m.ProcessingError, AbandonedJob.Reason), ct);
    }

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<MapSheetPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty map-placement payload.");
        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == payload.GeoreferencedMapId, ct)
            ?? throw new InvalidOperationException($"Georeferenced map {payload.GeoreferencedMapId} no longer exists.");
        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == map.SourceFileId, ct)
            ?? throw new InvalidOperationException($"Map {map.Id} has no source document.");

        var work = publisher.WorkDirectory(job.Id);
        try
        {
            await publisher.PublishAsync(map, upload, work, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            publisher.ForgetUnsaved();
            MapSheetPublisher.Unpublish(map);
            map.ProcessingError = MapSheetRenderHandler.Clip(
                e is VectorIOException ? e.Message : "Placing the map sheet failed unexpectedly.");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            MapSheetPublisher.Remove(work);
        }
    }
}
