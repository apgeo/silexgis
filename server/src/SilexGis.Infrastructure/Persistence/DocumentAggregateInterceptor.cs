// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Keeps a document's updated_at the revision of everything said about it: a new revision of its
/// bytes and an edit of a photo's details live in rows of their own, and either one moves the
/// owning document row's updated_at in the same save.
/// </summary>
/// <remarks>
/// <para>
/// The document's updated_at is what a phone arbitrates an edit on and what the phones' catalogue
/// re-sends a document on. A revision uploaded in the browser, or a caption written there, that
/// left it standing would let a phone that read the document earlier write over that change with
/// no conflict, and would never reach the phones that hold the old answer.
/// </para>
/// <para>
/// A document that is not tracked is loaded rather than stood in for by a stub: a stub stays in
/// the tracker after the save, and a later read through the same context would be handed its
/// empty title in place of the real row.
/// </para>
/// </remarks>
public sealed class DocumentAggregateInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && TouchedDocuments(context) is { Count: > 0 } ids)
        {
            var missing = Untracked(context, ids);
            if (missing.Count > 0)
            {
                _ = context.Set<Document>().IgnoreQueryFilters().Where(d => missing.Contains(d.Id)).ToList();
            }

            Stamp(context, ids);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && TouchedDocuments(context) is { Count: > 0 } ids)
        {
            var missing = Untracked(context, ids);
            if (missing.Count > 0)
            {
                _ = await context.Set<Document>().IgnoreQueryFilters()
                    .Where(d => missing.Contains(d.Id))
                    .ToListAsync(cancellationToken);
            }

            Stamp(context, ids);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>The documents whose revision rows or photo details this save adds or changes.</summary>
    private static HashSet<Guid> TouchedDocuments(DbContext context)
    {
        var touched = new HashSet<Guid>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var id = entry.Entity switch
            {
                DocumentVersion version => version.DocumentId,
                PhotoDetails details => details.DocumentId,
                _ => (Guid?)null,
            };
            if (id is { } documentId && documentId != Guid.Empty)
            {
                touched.Add(documentId);
            }
        }

        return touched;
    }

    private static List<Guid> Untracked(DbContext context, HashSet<Guid> ids)
    {
        var tracked = context.ChangeTracker.Entries<Document>().Select(e => e.Entity.Id).ToHashSet();
        return [.. ids.Where(id => !tracked.Contains(id))];
    }

    private static void Stamp(DbContext context, HashSet<Guid> ids)
    {
        // The timestamp interceptor has already run by the time this one fires, so the value is
        // stamped here rather than left to it.
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<Document>().Where(e => ids.Contains(e.Entity.Id)).ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Detached)
            {
                continue; // creation stamps the document itself; a removal needs no touch
            }

            entry.Entity.UpdatedAt = now;
            entry.Property(d => d.UpdatedAt).IsModified = true;
        }
    }
}
