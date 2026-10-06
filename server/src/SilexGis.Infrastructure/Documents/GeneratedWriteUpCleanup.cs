// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// What becomes of the write-up the application generated for a record when that record is
/// deleted: it goes with it, whole, unless something else has taken hold of it.
/// </summary>
/// <remarks>
/// <para>
/// A generated write-up is a derivative of what it was written for — built from it,
/// byte-identical while that does not change, named after it, and regenerable from nothing else —
/// and means nothing once the record is gone. So its document, its version and its stored file go
/// in the same unit of work as the record. A photograph, and a report a club wrote and uploaded
/// by hand into the same report slot, are library material that may be filed elsewhere and
/// outlive the record that first held them: only the rows that pinned them go, and removing those
/// is the caller's own cleanup rather than anything done here.
/// </para>
/// <para>
/// One body for every kind of record that has such a write-up, because the rule is about the
/// write-up and not about what it describes. A trip and a camp lose theirs in the same way, and
/// stated once per kind the rule would eventually be stated differently: one of them would keep a
/// document the other removes, and nothing would fail to say so.
/// </para>
/// <para>
/// Two steps, with the caller's own cleanup between them. The write-ups are found while the rows
/// that pin files to the record still exist, because those rows are what name them; and they are
/// removed once those rows are gone, so that an attachment still pointing at one is somebody
/// else's. Asked in the other order the second step keeps everything — the record's own pin reads
/// as somebody else holding it — which is the direction to be wrong in.
/// </para>
/// <para>
/// Rows only, and nothing here saves. The files whose rows went are handed back so the caller can
/// drop their bytes once its save has landed: bytes go after rows, never with them.
/// </para>
/// </remarks>
public static class GeneratedWriteUpCleanup
{
    /// <summary>
    /// The stored files of the generated write-ups filed in a record's report slot.
    /// </summary>
    /// <param name="generatedPrefix">
    /// The mark a generated write-up of this record carries at the front of its name. The name is
    /// the only mark there is: the document is filed through the same ingest as any upload, so no
    /// column says where it came from.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The mark is empty. An empty mark would recognise every report in the slot as generated, a
    /// club's own included, and the step after this one deletes what this one recognises.
    /// </exception>
    public static async Task<IReadOnlyList<Guid>> FiledOnAsync(
        SilexGisDbContext db,
        AttachedEntityType entityType,
        Guid entityId,
        string generatedPrefix,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedPrefix);

        return await (
            from attachment in db.Attachments.AsNoTracking()
            join file in db.StoredFiles.AsNoTracking() on attachment.FileId equals file.Id
            where attachment.EntityType == entityType
                && attachment.EntityId == entityId
                && attachment.Role == AttachmentRole.Report
                && file.OriginalName.StartsWith(generatedPrefix)
            select file.Id)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Removes the documents of those write-ups that nothing else holds, and answers the files
    /// whose rows went.
    /// </summary>
    /// <remarks>
    /// One that something other than its record has taken hold of — attached to another entity,
    /// or filed in a cabinet — has become library material by that act and keeps everything.
    /// Deleting it under them would take a document off a cave, or off a club's shelf, because
    /// the record it was first written for went.
    /// </remarks>
    public static async Task<IReadOnlyList<StoredFile>> RemoveUnheldAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(fileIds);

        var removed = new List<StoredFile>();
        foreach (var fileId in fileIds)
        {
            if (await HeldElsewhereAsync(db, fileId, ct))
            {
                continue;
            }

            // A tag on the file carries no foreign key and would be left pointing at nothing.
            await db.Taggings
                .Where(t => t.EntityType == AttachedEntityType.StoredFile && t.EntityId == fileId)
                .ExecuteDeleteAsync(ct);
            removed.AddRange(await documents.DeleteDocumentOfFileAsync(fileId, ct));
        }

        return removed;
    }

    /// <summary>
    /// Whether a generated write-up has been taken hold of by something other than the record it
    /// was written for: attached to another entity, or filed in a cabinet. Asked once the record's
    /// own attachment rows are gone, so any attachment left is somebody else's.
    /// </summary>
    private static async Task<bool> HeldElsewhereAsync(SilexGisDbContext db, Guid fileId, CancellationToken ct)
    {
        if (await db.Attachments.AsNoTracking().AnyAsync(a => a.FileId == fileId, ct))
        {
            return true;
        }

        var documentId = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .FirstOrDefaultAsync(ct);
        return documentId != Guid.Empty
            && await db.CabinetDocuments.AsNoTracking().AnyAsync(c => c.DocumentId == documentId, ct);
    }
}
