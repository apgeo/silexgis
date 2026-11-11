// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// Raw SQL behind the cave mirror (raw SQL lives only in *Sql.cs files): holding a cave's row
/// while its entrances are read and its mirror is worked out from them.
/// </summary>
public static class CaveMirrorSql
{
    /// <summary>
    /// Locks the cave's feature row until the surrounding transaction ends. The feature row,
    /// rather than the cave subtype row, because it is the row every versioned write on a cave or
    /// on one of its entrances holds first; taking the same row keeps every such write waiting in
    /// one order instead of in two.
    /// </summary>
    public static Task LockCaveAsync(SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM features WHERE id = {caveFeatureId} FOR UPDATE", ct);
}
