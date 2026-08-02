// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.RasterCog"/> jobs.</summary>
public sealed record RasterCogPayload(Guid GeoreferencedMapId);

/// <summary>
/// Normalizes an uploaded georeferenced raster to a Cloud-Optimized GeoTIFF: the COG
/// becomes a new stored file the map points at (the original upload is kept — files are
/// immutable), and the map gets its footprint and Ready status.
/// <para>
/// The COG joins the upload's own revision rather than starting a document of its own or
/// superseding the upload: it is the same content in another encoding, produced by the
/// server with no author of its own to name.
/// </para>
/// </summary>
public sealed class RasterCogHandler(
    SilexGisDbContext db,
    DocumentWriteService documents,
    IFileStore fileStore,
    RasterCogService rasterService) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.RasterCog;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<RasterCogPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty raster-cog payload.");

        var map = await db.GeoreferencedMaps.FirstOrDefaultAsync(m => m.Id == payload.GeoreferencedMapId, ct)
            ?? throw new InvalidOperationException($"Georeferenced map {payload.GeoreferencedMapId} no longer exists.");
        var upload = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == map.FileId, ct)
            ?? throw new InvalidOperationException($"Stored file {map.FileId} no longer exists.");

        map.Status = RasterStatus.Processing;
        await db.SaveChangesAsync(ct);

        var workPath = Path.Combine(Path.GetTempPath(), $"silexgis-cog-{Guid.NewGuid():N}.tif");
        try
        {
            var info = rasterService.ConvertToCog(fileStore.GetAbsolutePath(upload.StoragePath), workPath);

            string storagePath;
            string sha256;
            await using (var produced = File.OpenRead(workPath))
            {
                storagePath = await fileStore.SaveAsync(produced, ".tif", ct);
            }

            await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
            {
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
            }

            var cogFile = documents.AddFile(upload.DocumentVersionId, new StoredContent(
                storagePath,
                Path.GetFileNameWithoutExtension(upload.OriginalName) + ".cog.tif",
                "image/tiff",
                new FileInfo(fileStore.GetAbsolutePath(storagePath)).Length,
                sha256,
                FileKind.Raster));

            map.FileId = cogFile.Id;
            map.Bbox = info.Bbox4326;
            map.Status = RasterStatus.Ready;
            map.ProcessingError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            map.Status = RasterStatus.Failed;
            map.ProcessingError = e is VectorIOException ? e.Message : "Raster processing failed unexpectedly.";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (File.Exists(workPath))
            {
                File.Delete(workPath);
            }
        }
    }
}
