// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>A rejected document write, carrying the stable error code the API surfaces.</summary>
public sealed class DocumentWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Bytes that are already in the file store, described well enough to record them.
/// Callers write the content first (uploads can be large; nothing is buffered), then hand
/// the resulting facts over.
/// </summary>
public sealed record StoredContent(
    string StoragePath,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Sha256,
    FileKind Kind,
    Point? Geom = null);

/// <summary>
/// The single mutator of a document's derived state: which revision is current, what
/// number the next one takes, and the attachments and taggings that follow the current
/// revision when it moves. Every path that stores a file goes through here, so "what is
/// the current version" has exactly one answer — the database enforces the same thing
/// with a partial unique index, and the integrity verifier re-derives it on a schedule.
///
/// Sequence semantics live in <see cref="DocumentVersionRules"/> (pure, unit-tested);
/// this class loads state, delegates and persists.
///
/// Creating rows leaves them tracked but unsaved, because callers legitimately batch a
/// document with the entity that owns it — a geofile, a raster, a survey model — into one
/// unit of work. Superseding a revision is the exception: it owns its own transaction,
/// because a demotion that committed without its replacement would leave a document with
/// no current version at all.
/// </summary>
public sealed class DocumentWriteService(SilexGisDbContext db)
{
    private const string NotHeadCode = "file.not_head";
    private const string NotHeadMessage = "A newer version already exists; upload onto the current version.";
    private const string VersionInUseCode = "file.version_in_use";
    private const int TitleMaxLength = 300;

    /// <summary>Last-resort title when the upload supplied nothing usable to name it by.</summary>
    private const string UntitledTitle = "Untitled";

    /// <summary>Unique indexes whose violation means someone else moved the version sequence.</summary>
    private const string CurrentVersionIndex = "ix_document_versions_current";
    private const string VersionNumberIndex = "ix_document_versions_document_id_version_number";

    /// <summary>
    /// Records new content as a brand-new document: one revision, current from the start,
    /// carrying one file. Tracked, not saved — the caller commits it.
    /// </summary>
    public DocumentFile Create(
        StoredContent content, string? title, Guid ownerUserId, Guid? uploadedBy, DateOnly? documentDate = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = new Document
        {
            Title = TitleOf(title, content.OriginalName),
            OwnerUserId = ownerUserId,
        };
        db.Documents.Add(document);

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = DocumentVersionRules.FirstVersionNumber,
            IsCurrent = true,
            UploadedBy = uploadedBy,
            DocumentDate = documentDate,
        };
        db.DocumentVersions.Add(version);

        return new DocumentFile(AddFile(version.Id, content), version);
    }

    /// <summary>
    /// Adds another physical file to an existing revision — a rendition derived from what
    /// is already there, such as a cloud-optimized copy of an uploaded raster. It is the
    /// same content in another encoding, not a new revision, so nothing about the version
    /// sequence moves. Tracked, not saved.
    /// </summary>
    public StoredFile AddFile(Guid documentVersionId, StoredContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var file = new StoredFile
        {
            DocumentVersionId = documentVersionId,
            StoragePath = content.StoragePath,
            OriginalName = content.OriginalName,
            MimeType = content.MimeType,
            SizeBytes = content.SizeBytes,
            Sha256 = content.Sha256,
            Kind = content.Kind,
            Geom = content.Geom,
        };
        db.StoredFiles.Add(file);

        if (content.Kind == FileKind.Image)
        {
            // An image is one page by definition. Paged formats get their page rows from
            // text extraction, which is the only thing that knows the real count.
            db.DocumentPages.Add(new DocumentPage { FileId = file.Id, PageNumber = 1 });
        }

        return file;
    }

    /// <summary>
    /// Supersedes a document's current revision with a new one carrying
    /// <paramref name="content"/>, and moves everything that pointed at the old revision's
    /// file onto the new one. Owns its transaction; returns the new file.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>file.not_head</c> when <paramref name="supersededVersionId"/> is not the current
    /// revision — including the case where a concurrent upload got there first.
    /// </exception>
    public async Task<DocumentFile> AddVersionAsync(
        Guid supersededVersionId, StoredContent content, Guid? uploadedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var superseded = await db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == supersededVersionId, ct);
        if (superseded is null
            || !DocumentVersionRules.MayStackOnto(
                new DocumentVersionRules.VersionState(superseded.Id, superseded.VersionNumber, superseded.IsCurrent)))
        {
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        // Demote exactly the revision the caller claimed to be stacking onto, and treat "no
        // row changed" as the conflict. That makes the head check and the demotion one
        // indivisible act: the read above is only a fast path, because between it and this
        // statement another upload may already have taken the current flag. Demoting whatever
        // happens to be current instead would silently discard that upload and strand its
        // attachments on a revision no longer served.
        //
        // The row lock this takes is also what serialises two uploads racing onto the same
        // revision: the loser blocks here, then re-evaluates the predicate against the
        // committed row and finds nothing to do.
        //
        // A statement of its own is required regardless: a partial unique index is checked per
        // statement, so the flag has to be free before it is taken, and leaving the demotion
        // and the insert to one flush leaves their order to the change tracker, which sorts by
        // key rather than by intent. The surrounding transaction is what keeps the gap
        // invisible — a demotion that committed without its replacement would leave the
        // document with no current version at all.
        var demoted = await db.DocumentVersions
            .Where(v => v.Id == superseded.Id && v.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsCurrent, false), ct);
        if (demoted == 0)
        {
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        // Read after the demotion, never before. The next version number and the set of rows
        // to repoint must describe the document as it stands now that this call owns the
        // current flag; numbers derived from an older snapshot can skip a revision, and a
        // repoint set derived from one can miss rows another upload has already moved.
        var sequence = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == superseded.DocumentId)
            .Select(v => new DocumentVersionRules.VersionState(v.Id, v.VersionNumber, v.IsCurrent))
            .ToListAsync(ct);
        var supersededFileIds = await db.StoredFiles.AsNoTracking()
            .Where(f => f.DocumentVersionId == superseded.Id)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var version = new DocumentVersion
        {
            DocumentId = superseded.DocumentId,
            VersionNumber = DocumentVersionRules.NextVersionNumber(sequence),
            IsCurrent = true,
            UploadedBy = uploadedBy,
            // Version detail carries across: a re-scan of a 1974 survey is still from 1974,
            // and the uploader corrects the date afterwards if this revision is genuinely
            // from another day.
            DocumentDate = superseded.DocumentDate,
            Label = superseded.Label,
        };
        db.DocumentVersions.Add(version);
        var file = AddFile(version.Id, content);

        // Repoint attachments from the superseded revision to the new one, tracked so the
        // change is audited — entity timelines get a "document updated" event from the
        // Attachment FileId diff. Selecting by FileId moves feature-targeted rows and
        // polymorphic-pair rows alike; the target side of each row is untouched.
        //
        // A revision may hold several files (a scan and the rendition derived from it), and
        // they all collapse onto the single file of the new revision. Two rows that attached
        // two of those files to the same object would become the same attachment twice, so
        // the later ones are dropped instead of duplicated — the earliest row, which carries
        // the caption and sort order the object has been showing, is the one kept.
        var attachments = await db.Attachments
            .Where(a => supersededFileIds.Contains(a.FileId))
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var attachedTargets = new HashSet<(Guid?, AttachedEntityType?, Guid?)>();
        foreach (var attachment in attachments)
        {
            if (attachedTargets.Add((attachment.FeatureId, attachment.EntityType, attachment.EntityId)))
            {
                attachment.FileId = file.Id;
            }
            else
            {
                db.Attachments.Remove(attachment);
            }
        }

        // Tags belong to the document, not a revision of it. A file is only ever a
        // polymorphic-pair target (never a feature), so the pair filter reaches every tagging.
        // The same collapse applies, and here it is not merely untidy: one tag may sit on one
        // target only once, and that is a unique index — reassigning the second row would
        // violate it and fail the whole upload.
        var taggings = await db.Taggings
            .Where(t => t.EntityType == AttachedEntityType.StoredFile
                && t.EntityId != null && supersededFileIds.Contains(t.EntityId.Value))
            .OrderBy(t => t.Id)
            .ToListAsync(ct);
        var movedTagIds = new HashSet<long>();
        foreach (var tagging in taggings)
        {
            if (movedTagIds.Add(tagging.TagId))
            {
                tagging.EntityId = file.Id;
            }
            else
            {
                db.Taggings.Remove(tagging);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsVersionSequenceViolation(e))
        {
            // Something else claimed this document's current flag or the version number this
            // call computed. The scoped demotion above makes that unreachable through this
            // method, so this is the backstop for a version written by any other route.
            // Deliberately narrow: a unique violation on an unrelated index is a different
            // failure, and reporting it as "a newer version already exists" would send the
            // caller to re-upload onto the revision they already used.
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        await transaction.CommitAsync(ct);
        return new DocumentFile(file, version);
    }

    /// <summary>
    /// Deletes a superseded revision whole — its files and their pages go with it. This is
    /// the "version 1 held something that had to go" purge path; the current revision is
    /// never deletable, because a document with no current version has nothing to serve.
    /// Returns the removed files so the caller can drop their bytes and cached derivatives.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>file.head_undeletable</c> for the current revision; <c>file.version_in_use</c>
    /// when something still points at one of its files.
    /// </exception>
    public async Task<IReadOnlyList<StoredFile>> DeleteVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new DocumentWriteException("file.not_found", "The version no longer exists.");

        if (version.IsCurrent)
        {
            throw new DocumentWriteException(
                "file.head_undeletable",
                "The current version cannot be deleted; upload a new version instead.");
        }

        // A revision goes whole, so every file under it has to be free — not just the one the
        // caller named. A revision holding a server-derived rendition an entity still points
        // at is the common case: the entity's foreign key restricts, so deleting would fail
        // the statement outright rather than report anything useful.
        var files = await db.StoredFiles.Where(f => f.DocumentVersionId == versionId).ToListAsync(ct);
        var fileIds = files.Select(f => f.Id).ToList();
        if (await AnythingPointsAtAsync(fileIds, ct))
        {
            throw new DocumentWriteException(
                VersionInUseCode,
                "This version is still in use and cannot be deleted.");
        }

        // Removed through the change tracker rather than left to the cascade: a deleted file
        // is a forensic event the timeline keeps, and a database cascade writes no audit row.
        db.StoredFiles.RemoveRange(files);
        db.DocumentVersions.Remove(version);
        await db.SaveChangesAsync(ct);
        return files;
    }

    /// <summary>
    /// Deletes a whole document — every revision, file and page under it. For the entities
    /// that own their upload outright (a geofile, a raster map, an avatar), where deleting
    /// the entity means the content is gone. Returns the removed files so the caller can
    /// drop their bytes, or nothing when the document has outgrown that ownership.
    /// </summary>
    /// <remarks>
    /// The caller's claim on the document rests entirely on <paramref name="fileId"/>: that
    /// is the file its entity holds. Everything else under the same document may have grown
    /// a life of its own — a later revision someone attached elsewhere, a rendition another
    /// entity points at — and taking those with it would either drop attachment rows through
    /// a cascade that writes no audit trail, or fail the statement against a restricting
    /// foreign key. When that has happened nothing is deleted and the content stays reachable
    /// through its uploader, the same "it belongs to that other thing now, leave it alone"
    /// answer the avatar path already gives for a single file.
    /// </remarks>
    public async Task<IReadOnlyList<StoredFile>> DeleteDocumentOfFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var documentId = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .FirstOrDefaultAsync(ct);
        if (documentId == Guid.Empty)
        {
            return [];
        }

        var versionIds = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .Select(v => v.Id)
            .ToListAsync(ct);
        var files = await db.StoredFiles.Where(f => versionIds.Contains(f.DocumentVersionId)).ToListAsync(ct);

        var siblingIds = files.Where(f => f.Id != fileId).Select(f => f.Id).ToList();
        if (await AnythingPointsAtAsync(siblingIds, ct))
        {
            return [];
        }

        var versions = await db.DocumentVersions.Where(v => v.DocumentId == documentId).ToListAsync(ct);
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);

        db.StoredFiles.RemoveRange(files);
        db.DocumentVersions.RemoveRange(versions);
        if (document is not null)
        {
            db.Documents.Remove(document);
        }

        return files;
    }

    /// <summary>
    /// Whether anything still points at one of these files. Deleting a referenced file is
    /// never harmless: the attachment foreign key cascades, so the row disappears with no
    /// audit trail; the entity foreign keys (geofile, raster map, survey model) restrict, so
    /// the statement fails and the caller sees an unhandled database error instead of a
    /// stable code; taggings and avatars carry no foreign key at all and would simply be left
    /// pointing at nothing.
    /// </summary>
    private async Task<bool> AnythingPointsAtAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        if (fileIds.Count == 0)
        {
            return false;
        }

        return await db.Attachments.AsNoTracking().AnyAsync(a => fileIds.Contains(a.FileId), ct)
            || await db.Taggings.AsNoTracking().AnyAsync(
                t => t.EntityType == AttachedEntityType.StoredFile
                    && t.EntityId != null && fileIds.Contains(t.EntityId.Value), ct)
            || await db.Geofiles.AsNoTracking().AnyAsync(g => fileIds.Contains(g.FileId), ct)
            || await db.GeoreferencedMaps.AsNoTracking().AnyAsync(m => fileIds.Contains(m.FileId), ct)
            || await db.SurveyModels.AsNoTracking().AnyAsync(s => fileIds.Contains(s.FileId), ct)
            || await db.Users.AsNoTracking().AnyAsync(
                u => u.AvatarFileId != null && fileIds.Contains(u.AvatarFileId.Value), ct);
    }

    /// <summary>
    /// Whether a failed save is the version sequence being taken by someone else, rather
    /// than any other write failure that happens to reach the database as a conflict.
    /// </summary>
    private static bool IsVersionSequenceViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
        && violation.ConstraintName is CurrentVersionIndex or VersionNumberIndex;

    /// <summary>
    /// A document always has a readable title, and a client-supplied file name is not a
    /// reliable source of one: a multipart part may carry a blank name, and a name that is
    /// nothing but an extension leaves nothing behind once the extension is stripped. Callers
    /// derive their titles from those names, so the fallback lives here rather than being
    /// re-checked at every upload site — and rejecting the write at this point would strand
    /// bytes that are already in the file store.
    /// </summary>
    private static string TitleOf(string? title, string? originalName)
    {
        var candidate = string.IsNullOrWhiteSpace(title) ? originalName : title;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = UntitledTitle;
        }

        candidate = candidate.Trim();
        return candidate.Length > TitleMaxLength ? candidate[..TitleMaxLength] : candidate;
    }
}
