// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Keeps the feature aggregate's one concurrency/sync token honest: a subtype-row change
/// (cave morphometry, entrance attributes, centerline metadata) must bump the owning
/// feature row's updated_at, or ETags and future sync would miss the edit. The write
/// service already touches features it loads; this interceptor is the belt-and-braces
/// for subtype edits saved without their feature tracked.
/// </summary>
public sealed class FeatureAggregateInterceptor : SaveChangesInterceptor
{
    // A feature row that is not tracked is loaded rather than stood in for by a stub. A stub (an
    // empty Feature carrying only the id) stays in the tracker after the save, so a later read
    // through the same context is handed its empty name, kind and visibility in place of the real
    // row; and the audit diff sees it as an edit of a generic feature whose updated_at moved from
    // year one.
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && TouchedFeatures(context) is { Count: > 0 } ids)
        {
            var missing = Untracked(context, ids);
            if (missing.Count > 0)
            {
                _ = context.Set<Feature>().IgnoreQueryFilters().Where(f => missing.Contains(f.Id)).ToList();
            }

            Stamp(context, ids);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && TouchedFeatures(context) is { Count: > 0 } ids)
        {
            var missing = Untracked(context, ids);
            if (missing.Count > 0)
            {
                _ = await context.Set<Feature>().IgnoreQueryFilters()
                    .Where(f => missing.Contains(f.Id))
                    .ToListAsync(cancellationToken);
            }

            Stamp(context, ids);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>The features whose subtype row this save changes.</summary>
    private static HashSet<Guid> TouchedFeatures(DbContext context)
    {
        var touched = new HashSet<Guid>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var id = entry.Entity switch
            {
                Cave cave => cave.Id,
                CaveEntrance entrance => entrance.Id,
                Centerline centerline => centerline.Id,
                _ => (Guid?)null,
            };
            if (id is not null)
            {
                touched.Add(id.Value);
            }
        }

        return touched;
    }

    private static List<Guid> Untracked(DbContext context, HashSet<Guid> ids)
    {
        var tracked = context.ChangeTracker.Entries<Feature>().Select(e => e.Entity.Id).ToHashSet();
        return [.. ids.Where(id => !tracked.Contains(id))];
    }

    private static void Stamp(DbContext context, HashSet<Guid> ids)
    {
        // The timestamp interceptor has already run by the time this one fires, so the
        // value is stamped here, not delegated.
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<Feature>().Where(e => ids.Contains(e.Entity.Id)).ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Detached)
            {
                continue; // creation stamps both timestamps; deletion needs no touch
            }

            entry.Entity.UpdatedAt = now;
            entry.Property(f => f.UpdatedAt).IsModified = true;
        }
    }
}
