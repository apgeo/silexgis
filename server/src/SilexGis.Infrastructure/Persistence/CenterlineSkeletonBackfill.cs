// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Fills in display skeletons for centerlines stored before the map overlay learned to use
/// them. Runs after migration; a row is pending exactly while its skeleton path count is null,
/// so a finished installation does one cheap COUNT and stops.
/// </summary>
public static class CenterlineSkeletonBackfill
{
    /// <summary>Rows per round trip — the geometries are large, so they are not all held at once.</summary>
    private const int BatchSize = 25;

    /// <summary>Returns the number of centerlines given a skeleton.</summary>
    public static async Task<int> RunAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        var built = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.CaveCenterlines
                .Where(c => c.SkeletonPathCount == null)
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var centerline in batch)
            {
                // Always from the stored survey geometry: the rule is not idempotent, and
                // re-running it on a skeleton keeps eating the dead ends it exposed.
                var skeleton = CenterlineSkeleton.Build(centerline.Geom);
                var worthStoring = CenterlineSkeleton.IsWorthStoring(centerline.Geom, skeleton);
                centerline.PathCount = CenterlineSkeleton.PathCount(centerline.Geom);
                centerline.Skeleton = worthStoring ? skeleton : null;
                centerline.SkeletonPathCount = worthStoring
                    ? CenterlineSkeleton.PathCount(skeleton)
                    : centerline.PathCount;
                built++;
            }

            await db.SaveChangesAsync(ct);
        }

        return built;
    }
}
