// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Reads capture facts onto image files that predate reading them at upload. Idempotent: it
/// snapshots the set of not-yet-placed images once (so files without GPS are not retried
/// forever), then fills whatever it can in batches. Newer uploads read themselves, so this is a
/// one-off catch-up.
/// </summary>
/// <remarks>
/// What it skips is what a person decided, and nothing else. A picture somebody placed by hand —
/// or deliberately took the position off — must not have the camera's own guess put back by a
/// pass over the bytes; that guess is precisely what they overruled. Everything else with no
/// point is a candidate, including a file whose recorded fix was lost along the way, because
/// recovering exactly that is what this pass is for.
/// </remarks>
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
            .Where(f => f.Kind == FileKind.Image
                && f.Geom == null
                && f.PositionSource != PhotoPositionSource.Manual)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var chunk in candidateIds.Chunk(BatchSize))
        {
            var files = await db.StoredFiles.Where(f => chunk.Contains(f.Id)).ToListAsync(ct);
            foreach (var file in files)
            {
                var capture = geotagReader.Read(fileStore.GetAbsolutePath(file.StoragePath));

                // The camera facts are filled in whether or not there was a fix: a picture with
                // no GPS still has a lens and a shutter, and this pass is the only thing that
                // will ever read an upload that predates the panel.
                file.Metadata = (capture.Exif ?? PhotoExif.None).IntoMetadata(file.Metadata);

                if (capture.Point is not null)
                {
                    file.Geom = capture.Point;
                    file.PositionSource = PhotoPositionSource.Exif;
                    file.AltitudeMeters = capture.AltitudeMeters;
                    file.DirectionDegrees = capture.DirectionDegrees;
                    file.DirectionIsMagnetic = capture.DirectionIsMagnetic;
                    file.PositionDop = capture.Dop;
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
