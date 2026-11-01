// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Which entrances and centerlines of a readable cave a caller is told of. The one home of
/// both rules: the cave's own routes list through them, and so does every other surface that
/// names a cave's parts, so none of them can tell more than the cave's page does.
/// </summary>
public static class CavePartReads
{
    /// <summary>
    /// Every entrance of the cave, main entrance first, with its feature row. Readability is
    /// decided on the cave alone, by the caller, before asking. Re-filtering the entrance rows
    /// would be wrong, not merely redundant: ACL grants are keyed to the row they were made on,
    /// so a caller who reads a private cave through a grant holds none on its entrances.
    /// Positions on the rows are the caller's to obfuscate.
    /// </summary>
    public static Task<List<CaveEntrance>> EntrancesAsync(SilexGisDbContext db, Guid caveId, CancellationToken ct) =>
        db.CaveEntrances.AsNoTracking()
            .Include(e => e.Feature)
            .Where(e => e.CaveFeatureId == caveId)
            .OrderByDescending(e => e.IsMain).ThenBy(e => e.Feature.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// The cave's centerlines the caller may see, oldest first, with their subtype rows. The
    /// cave's visibility cascades to its centerlines at read time, so the row-local filter is
    /// the whole read rule. And though the cave stays readable, its centerlines trace its exact
    /// position: whoever may not see that sees no centerlines at all, and is told nothing
    /// about how many exist.
    /// </summary>
    public static async Task<List<Feature>> CenterlinesAsync(
        SilexGisDbContext db, FeatureProtection protection, AccessContext ctx, Guid caveId, CancellationToken ct)
    {
        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Centerline && f.Centerline!.CaveFeatureId == caveId)
            .Include(f => f.Centerline)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
        var exact = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(f => f.Id)], ct);
        return [.. rows.Where(f => exact.Contains(f.Id))];
    }
}
