// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>How a request to delete a document ended.</summary>
public enum DocumentDeleteOutcome
{
    Deleted,

    /// <summary>Not there, or there but not this caller's to know about.</summary>
    NotFound,

    /// <summary>The caller may read it and may not edit it.</summary>
    Forbidden,

    /// <summary>
    /// A survey archive entry, survey model, georeferenced map or geofile is built on it. Those
    /// records are not documents and do not hide with one, so they would keep serving its bytes;
    /// removing them first is the deliberate act.
    /// </summary>
    InUse,
}

/// <summary>How a request to put a deleted document back ended.</summary>
public enum DocumentRestoreOutcome
{
    Restored,
    NotFound,
    Forbidden,

    /// <summary>It is not deleted, so there is nothing to put back.</summary>
    NotDeleted,

    /// <summary>It was deleted longer ago than this installation keeps one.</summary>
    WindowPassed,
}

/// <summary>A deleted document as the list of what can be put back shows it.</summary>
public sealed record DeletedDocumentRow(Document Document, DateTimeOffset? RestorableUntil);

/// <summary>
/// Deleting a document, listing the deleted ones and putting one back — one statement of who may
/// do each, shared by the document routes and the photograph routes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The right that edits is the right that deletes and restores.</b> A document is deleted by
/// marking it, not by removing a row, so everything that decides who may touch it — its owner, its
/// band, its cabinets, the rules written against it, the things its file hangs on — is still there
/// to be asked about a deleted one. Restoring is therefore asked of the same walk as an edit, and
/// not of "whoever deleted it": an administrator who deleted somebody's document by mistake is not
/// the only one who can undo it, and somebody who has since lost the right to edit it cannot bring
/// it back either. Reading is asked first, on its own, because the answer to a restore names the
/// document.
/// </para>
/// <para>
/// Nothing is removed. The marked document keeps every version, file, comment, attachment, link
/// membership and filing it had, so putting it back is exact; only the readers hide it.
/// </para>
/// </remarks>
public sealed class DocumentDeletionService(
    SilexGisDbContext db,
    IAccessService access,
    IOptions<DocumentRetentionOptions> retention,
    TimeProvider clock)
{
    /// <summary>The window a deleted document stays restorable, or null when it is kept.</summary>
    public TimeSpan? Window => retention.Value.Window;

    public async Task<DocumentDeleteOutcome> DeleteAsync(AccessContext ctx, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Tracked, and through the filter: a document that is already deleted is not there to
        // delete again.
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return DocumentDeleteOutcome.NotFound;
        }

        var content = (await DocumentQueries.CurrentFileAsync(db, id, ct))?.File;
        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content, ct))
        {
            // One who may read learns only that they may not edit; one who may not read learns
            // nothing at all.
            return await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content, ct)
                ? DocumentDeleteOutcome.Forbidden
                : DocumentDeleteOutcome.NotFound;
        }

        if (await IsInUseAsync(id, ct))
        {
            return DocumentDeleteOutcome.InUse;
        }

        document.MarkDeleted(clock.GetUtcNow(), ctx.UserId);
        await db.SaveChangesAsync(ct);
        return DocumentDeleteOutcome.Deleted;
    }

    private async Task<bool> IsInUseAsync(Guid documentId, CancellationToken ct)
    {
        var files = db.StoredFiles.Where(f =>
            db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.DocumentId == documentId))
            .Select(f => f.Id);

        return await db.SurveySources.AnyAsync(s => s.DocumentId == documentId, ct)
            || await db.SurveyModels.AnyAsync(m =>
                files.Contains(m.FileId) || (m.ConvertedFileId != null && files.Contains(m.ConvertedFileId.Value)), ct)
            || await db.GeoreferencedMaps.AnyAsync(m => files.Contains(m.FileId), ct)
            || await db.Geofiles.AnyAsync(g => files.Contains(g.FileId), ct);
    }

    public async Task<DocumentRestoreOutcome> RestoreAsync(AccessContext ctx, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Past the filter that hides deleted documents: one of them is what this is for.
        var document = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return DocumentRestoreOutcome.NotFound;
        }

        var content = (await DocumentQueries.CurrentFileAsync(db, id, ct))?.File;
        if (!await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content, ct))
        {
            return DocumentRestoreOutcome.NotFound;
        }

        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content, ct))
        {
            return DocumentRestoreOutcome.Forbidden;
        }

        if (document.DeletedAt is not { } deletedAt)
        {
            return DocumentRestoreOutcome.NotDeleted;
        }

        // Past the window the sweep may already have taken the bytes, so a restore would produce
        // a row pointing at nothing — worse than refusing.
        if (!SoftDeleteRules.IsRestorable(deletedAt, clock.GetUtcNow(), Window))
        {
            return DocumentRestoreOutcome.WindowPassed;
        }

        document.MarkRestored();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The sweep reached this one in the same instant and won: the row is gone.
            return DocumentRestoreOutcome.NotFound;
        }

        return DocumentRestoreOutcome.Restored;
    }

    /// <summary>
    /// The deleted documents this caller could put back, newest deletion first.
    /// </summary>
    /// <param name="photographsOnly">
    /// Narrows to documents whose current file is a picture, for the gallery's own list.
    /// </param>
    /// <remarks>
    /// Decided document by document with the rule the restore route asks — read and edit — because
    /// there is no filter twin for an action other than reading and a second statement of the rule
    /// would be a place for the two to drift. The rows are bounded by
    /// the restore window; on an installation that keeps everything they are bounded by what has
    /// been deleted, which is a list a person is expected to empty.
    /// </remarks>
    public async Task<(IReadOnlyList<DeletedDocumentRow> Page, int Total)> ListAsync(
        AccessContext ctx, bool photographsOnly, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Through the filter, which hides exactly the rows this list is about.
        var deleted = db.Documents.AsNoTracking().IgnoreQueryFilters().Where(d => d.DeletedAt != null);

        // A document past its window is not offered: a restore would be refused, and a row nobody
        // can act on only says the sweep is late.
        if (SoftDeleteRules.PurgeCutoff(clock.GetUtcNow(), Window) is { } cutoff)
        {
            deleted = deleted.Where(d => d.DeletedAt > cutoff);
        }

        if (photographsOnly)
        {
            deleted = deleted.Where(d => db.StoredFiles.Any(f =>
                f.Kind == FileKind.Image
                && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
        }

        var candidates = await deleted
            .OrderByDescending(d => d.DeletedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(ct);

        var mine = candidates;
        if (!ctx.IsFullAdmin && candidates.Count > 0)
        {
            // Each candidate is asked the two questions the restore route asks, of the document
            // walk's single home: reading what a file hangs on is not the right to edit it.
            mine = [];
            foreach (var d in candidates)
            {
                var content = (await DocumentQueries.CurrentFileAsync(db, d.Id, ct))?.File;
                if (await DocumentAccessRules.CanReadAsync(db, access, ctx, d, content, ct)
                    && await DocumentAccessRules.CanWriteAsync(db, access, ctx, d, content, ct))
                {
                    mine.Add(d);
                }
            }
        }

        var shown = mine.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DeletedDocumentRow(d, SoftDeleteRules.RemovedAt(d.DeletedAt!.Value, Window)))
            .ToList();
        return (shown, mine.Count);
    }
}
