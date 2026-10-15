// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Takes the access rows naming a row that is being removed for good into the same save, so no
/// delete handler can forget them.
/// </summary>
/// <remarks>
/// <para>
/// The rule itself is <see cref="AccessAnchorCleanup"/>; this is only where it is asked. Before
/// it, every delete flow carried its own copy of "remove the rules anchored here", some carried
/// none, and the integrity check found the difference afterwards. Asked here, a delete that goes
/// through the change tracker cannot leave a rule behind, whichever handler wrote it.
/// </para>
/// <para>
/// It looks at rows in the deleted state only. A soft delete is a modification — the row stays,
/// with a stamp — so the rules naming a binned trip, document or cave stay with it, and a restore
/// gives back exactly the access it had.
/// </para>
/// <para>
/// Registered ahead of the audit interceptor, which is what puts each removed rule into the trail
/// beside the row that took it.
/// </para>
/// </remarks>
public sealed class AccessAnchorCleanupInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is SilexGisDbContext db && Deleted(db) is { Count: > 0 } deleted)
        {
            AccessAnchorCleanup.RemoveRowsNaming(db, deleted);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SilexGisDbContext db && Deleted(db) is { Count: > 0 } deleted)
        {
            await AccessAnchorCleanup.RemoveRowsNamingAsync(db, deleted, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static Dictionary<Type, HashSet<Guid>> Deleted(DbContext db)
    {
        var deleted = new Dictionary<Type, HashSet<Guid>>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            var type = entry.Metadata.ClrType;
            if (entry.State != EntityState.Deleted
                || !AccessAnchors.IsCleanupSource(type)
                || entry.Metadata.FindPrimaryKey() is not { Properties: [var key] }
                || entry.Property(key.Name).CurrentValue is not Guid id)
            {
                continue;
            }

            if (!deleted.TryGetValue(type, out var ids))
            {
                ids = [];
                deleted[type] = ids;
            }

            ids.Add(id);
        }

        return deleted;
    }
}
