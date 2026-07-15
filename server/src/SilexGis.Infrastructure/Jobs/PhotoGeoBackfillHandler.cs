// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Reads EXIF GPS onto image files that predate geotag-at-upload. Idempotent: it snapshots the
/// set of not-yet-geotagged images once (so files without GPS are not retried forever), then
/// fills whatever it can in batches. Newer uploads geotag themselves, so this is a one-off catch-up.
/// </summary>
public sealed class PhotoGeoBackfillHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    IPhotoGeotagReader geotagReader) : IProcessingJobHandler
{
    private const int BatchSize = 200;

    public string Kind => ProcessingJobKinds.PhotoGeoBackfill;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var candidateIds = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Kind == FileKind.Image && f.Geom == null)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var chunk in candidateIds.Chunk(BatchSize))
        {
            var files = await db.StoredFiles.Where(f => chunk.Contains(f.Id)).ToListAsync(ct);
            foreach (var file in files)
            {
                var point = geotagReader.TryReadPoint(fileStore.GetAbsolutePath(file.StoragePath));
                if (point is not null)
                {
                    file.Geom = point;
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
