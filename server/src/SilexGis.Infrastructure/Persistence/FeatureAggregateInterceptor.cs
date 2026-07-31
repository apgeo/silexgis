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
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

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

        if (touched.Count == 0)
        {
            return;
        }

        // The timestamp interceptor has already run by the time this one fires, so the
        // value is stamped here, not delegated.
        var now = DateTimeOffset.UtcNow;
        foreach (var id in touched)
        {
            var entry = context.ChangeTracker.Entries<Feature>().FirstOrDefault(e => e.Entity.Id == id);
            if (entry is { State: EntityState.Added or EntityState.Deleted })
            {
                continue; // creation stamps both timestamps; deletion needs no touch
            }

            if (entry is null)
            {
                entry = context.Entry(new Feature { Id = id });
                entry.State = EntityState.Unchanged;
            }

            entry.Entity.UpdatedAt = now;
            entry.Property(f => f.UpdatedAt).IsModified = true;
        }
    }
}
