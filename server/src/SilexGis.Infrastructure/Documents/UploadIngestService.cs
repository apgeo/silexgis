// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// Where a file is going. All three parts are optional and independent: a file may be filed,
/// attached, both, or neither.
/// </summary>
/// <param name="CabinetId">
/// The shelf it is filed on — or, when <paramref name="FolderSegments"/> is non-empty, the
/// shelf the mirrored subtree hangs under. Null means unfiled, which is a real destination
/// and the default one: filing is a later decision, and an upload that had to answer "where
/// does this belong" before it could start is the thing this whole area exists to stop.
/// </param>
/// <param name="FolderSegments">
/// Folder names from the source, outermost first, to be mirrored as cabinets under
/// <paramref name="CabinetId"/>. Already validated by <see cref="FilingPaths"/> — nothing
/// here re-parses a path.
/// </param>
/// <param name="AttachEntityType">The polymorphic half of an attachment target.</param>
/// <param name="AttachEntityId">The polymorphic half of an attachment target.</param>
/// <param name="AttachFeatureId">The feature half of an attachment target.</param>
/// <param name="AttachRole">
/// What the attachment is for. Null lets the file's own kind decide, which is what an
/// ordinary drop onto a cave's page wants.
/// </param>
/// <param name="CavingGroupId">
/// The club the created document belongs to. Whether the uploader may bind to it is settled
/// before this is reached — that guard needs the request, which this does not have.
/// </param>
public sealed record UploadDestination(
    Guid? CabinetId = null,
    IReadOnlyList<string>? FolderSegments = null,
    AttachedEntityType? AttachEntityType = null,
    Guid? AttachEntityId = null,
    Guid? AttachFeatureId = null,
    AttachmentRole? AttachRole = null,
    Guid? CavingGroupId = null)
{
    /// <summary>Whether anything is to be attached at all.</summary>
    public bool HasAttachment => AttachFeatureId is not null || (AttachEntityType is not null && AttachEntityId is not null);
}

/// <summary>What became of one file, in the shape a batch line is written from.</summary>
/// <param name="Outcome">Stored, skipped or failed.</param>
/// <param name="Reason">Why, when it was not stored. Null for a stored file.</param>
/// <param name="Content">The created document's file and revision, when one was created.</param>
/// <param name="DocumentId">The created document.</param>
/// <param name="CabinetId">The shelf it actually landed on, which for a mirrored tree is not the destination's own.</param>
/// <param name="DuplicateOfDocumentId">
/// The document holding identical content that caused this to be skipped. Only ever set when
/// the uploader may actually read that document.
/// </param>
public sealed record IngestOutcome(
    UploadItemOutcome Outcome,
    string? Reason = null,
    DocumentFile? Content = null,
    Guid? DocumentId = null,
    Guid? CabinetId = null,
    Guid? DuplicateOfDocumentId = null);

/// <summary>
/// The one path from "bytes are in the store" to "a document exists, filed and tagged where
/// it was aimed".
///
/// <para>
/// Four routes reach it — a browser upload, the completion of a resumable one, an entry of an
/// expanded archive, a file on a directory the server walked — and they differ only in how
/// the bytes arrived. Everything after that is identical and is here: duplicate detection,
/// the mirrored folder tree, the shelf's defaults, filing, tagging, attaching, and the line
/// written into the batch's report. Four copies of that would be four places for the
/// duplicate rule to be subtly different, which is the same as not having one.
/// </para>
/// <para>
/// Nothing here decides who may upload. That is Create in the documents domain and is settled
/// by the request — or, for queued work, by the request that queued it. What this <em>does</em>
/// re-decide is filing: whether this caller may write documents at the shelf being filed
/// into, asked here rather than at the four call sites, because a batch that files four
/// hundred documents onto a shelf its owner may not write is the exact shape of an
/// amplification.
/// </para>
/// </summary>
public sealed class UploadIngestService(
    SilexGisDbContext db,
    DocumentWriteService documents,
    CabinetWriteService cabinets,
    IAccessService access)
{
    /// <summary>
    /// How many documents holding the same bytes are examined before the search gives up and
    /// treats the content as new. Exact-hash collisions are a handful in practice — the same
    /// PDF mailed round a club — and a store holding more copies than this of one file has a
    /// problem no upload check is going to fix. The cost of the bound is that the hundredth
    /// copy is not recognised; the cost of not having one is an unbounded per-candidate access
    /// walk on every upload.
    /// </summary>
    private const int DuplicateCandidateLimit = 50;

    /// <summary>
    /// Whether this caller may put documents on this shelf — the question filing comes down
    /// to, and the same one the cabinet endpoints ask. Exposed so a request can be refused
    /// outright, with a status that says so, before any bytes are transferred.
    /// </summary>
    public async Task<bool> MayFileIntoAsync(AccessContext ctx, Guid? cabinetId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (cabinetId is not { } id)
        {
            return true; // unfiled: there is no shelf to hold a rule
        }

        // The tracked copy first. A shelf a folder drop created moments ago in this same unit
        // of work is not in the database yet, and asking only the database would refuse the
        // very filing the drop is doing — which reads as "you may not upload here" for a
        // shelf the caller was authorised for one level up.
        var ancestry = db.Cabinets.Local.FirstOrDefault(c => c.Id == id)?.AncestorIds
            ?? await db.Cabinets.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => c.AncestorIds)
                .FirstOrDefaultAsync(ct);

        return ancestry is not null
            && AccessEvaluator.Decide(
                ctx,
                AccessDomain.Documents,
                AccessAction.Write,
                new AccessTargetFacts { CabinetIds = ancestry }).Allowed;
    }

    /// <summary>
    /// A document already holding exactly these bytes that this caller may read, or null.
    /// </summary>
    /// <remarks>
    /// This is the whole of duplicate detection, and the "may read" is not a nicety. Told that
    /// content already exists, a caller learns it exists — and for a document whose very
    /// presence is the sensitive part, that is the disclosure. So a duplicate the caller
    /// cannot read is reported as no duplicate at all, they get their own copy, and the pair
    /// is recorded for an administrator who can see both. The store pays for a second copy;
    /// nobody learns anything they were not entitled to.
    /// </remarks>
    public async Task<Guid?> VisibleDuplicateAsync(
        AccessContext ctx, string sha256, CancellationToken ct = default)
    {
        foreach (var candidate in await DuplicateCandidatesAsync(sha256, ct))
        {
            var subject = await DocumentQueries.FileSubjectAsync(db, candidate.FileId, ct);
            if (subject is not null
                && await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
            {
                return candidate.DocumentId;
            }
        }

        return null;
    }

    /// <summary>
    /// Records stored bytes as a document at the given destination. Saves.
    /// </summary>
    /// <param name="sourcePath">
    /// The path the source named it by, used for the batch's report and — for the folder
    /// segments already resolved into <paramref name="destination"/> — nothing else.
    /// </param>
    /// <param name="allowDuplicate">
    /// True when the uploader has been shown the duplicate and asked for it anyway. False on
    /// every first attempt, which is what makes the warning unskippable by a client that
    /// forgot to ask: the refusal comes from here, not from the client's own check.
    /// </param>
    public async Task<IngestOutcome> RecordAsync(
        StoredContent content,
        string sourcePath,
        AccessContext ctx,
        UploadDestination destination,
        Guid? uploadBatchId,
        bool allowDuplicate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(destination);

        // Asked before anything is created, and it decides two quite different things: whether
        // to refuse this upload, and — when the answer is "there is a copy but you may not see
        // it" — whether to leave a record for somebody who can.
        var candidates = await DuplicateCandidatesAsync(content.Sha256, ct);
        Guid? visibleDuplicate = null;
        Guid? hiddenDuplicate = null;
        foreach (var candidate in candidates)
        {
            var subject = await DocumentQueries.FileSubjectAsync(db, candidate.FileId, ct);
            if (subject is null)
            {
                continue;
            }

            if (await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
            {
                visibleDuplicate = candidate.DocumentId;
                break;
            }

            hiddenDuplicate ??= candidate.DocumentId;
        }

        if (visibleDuplicate is { } existing && !allowDuplicate)
        {
            return new IngestOutcome(
                UploadItemOutcome.Skipped, UploadItemReasons.Duplicate, DuplicateOfDocumentId: existing);
        }

        Guid? cabinetId;
        try
        {
            cabinetId = await ResolveCabinetAsync(destination, ct);
        }
        catch (CabinetWriteException e)
        {
            return new IngestOutcome(UploadItemOutcome.Failed, e.Code);
        }

        // Re-asked against the shelf the file actually lands on, which for a mirrored tree is
        // one this call just created under a shelf the caller was authorised for. Creating a
        // sub-shelf cannot be a way onto a shelf you could not otherwise write to, so the
        // question is asked where the document goes rather than where the drop was aimed.
        if (!await MayFileIntoAsync(ctx, cabinetId, ct))
        {
            return new IngestOutcome(UploadItemOutcome.Skipped, UploadItemReasons.FilingRefused);
        }

        var defaults = await DefaultsForAsync(cabinetId, ct);

        var stored = documents.Create(
            content,
            content.OriginalName,
            ownerUserId: ctx.UserId,
            uploadedBy: ctx.UserId,
            documentDate: null,
            cavingGroupId: destination.CavingGroupId);

        // Taken from the change tracker rather than queried: the write service creates the
        // document tracked but unsaved, so it is not in the database to be read back yet —
        // which is deliberate, because it lets a caller commit a document and the entity that
        // owns it in one unit of work.
        var document = db.Documents.Local.First(d => d.Id == stored.Version.DocumentId);
        document.UploadBatchId = uploadBatchId;

        // The shelf's defaults fill in what the upload did not say, and never overrule it.
        // A club named on the request wins over a shelf's visibility default, because the
        // request is the more specific statement of the two.
        document.DocumentTypeId ??= defaults.DocumentTypeId;
        if (defaults.Visibility is { } visibility && destination.CavingGroupId is null)
        {
            document.Visibility = visibility;
        }

        if (cabinetId is { } shelf)
        {
            db.CabinetDocuments.Add(new CabinetDocument { CabinetId = shelf, DocumentId = document.Id });
        }

        await ApplyTagsAsync(stored.File.Id, defaults.TagIds, uploadBatchId, ctx.UserId, ct);
        AttachIfAsked(stored.File.Id, destination, ctx.UserId);

        await db.SaveChangesAsync(ct);

        // Written after the document exists, because it names it. A record pointing at a
        // document that was never created would be worse than no record at all.
        if (visibleDuplicate is null && hiddenDuplicate is { } concealed)
        {
            db.DuplicateUploadRecords.Add(new DuplicateUploadRecord
            {
                Sha256 = content.Sha256,
                NewDocumentId = document.Id,
                ExistingDocumentId = concealed,
                UploadedByUserId = ctx.UserId,
                SizeBytes = content.SizeBytes,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        return new IngestOutcome(
            UploadItemOutcome.Stored,
            Content: stored,
            DocumentId: document.Id,
            CabinetId: cabinetId);
    }

    /// <summary>
    /// The shelf a file lands on: the destination's own, or the bottom of a subtree mirroring
    /// the source's folders under it. Creates whatever is missing; an existing shelf of the
    /// right name is reused, which is what makes dropping the same folder twice add to it
    /// rather than duplicate it.
    /// </summary>
    private async Task<Guid?> ResolveCabinetAsync(UploadDestination destination, CancellationToken ct)
    {
        var segments = destination.FolderSegments ?? [];
        if (segments.Count == 0)
        {
            return destination.CabinetId;
        }

        var parentId = destination.CabinetId;
        foreach (var name in segments)
        {
            // Matched among siblings only, because that is the scope a cabinet name is unique
            // in. The local lookup covers shelves created earlier in this same walk, which the
            // database does not carry yet — without it, a folder holding two files would try
            // to create its shelf twice and fail on the unique index.
            var existing = db.Cabinets.Local.FirstOrDefault(c => c.ParentId == parentId && c.Name == name)
                ?? await db.Cabinets.FirstOrDefaultAsync(c => c.ParentId == parentId && c.Name == name, ct);

            if (existing is not null)
            {
                parentId = existing.Id;
                continue;
            }

            var created = await cabinets.CreateAsync(name, description: null, parentId, ct);
            parentId = created.Id;
        }

        return parentId;
    }

    /// <summary>The shelf's effective defaults, or nothing at all when the file is unfiled.</summary>
    private async Task<CabinetDefaults> DefaultsForAsync(Guid? cabinetId, CancellationToken ct)
    {
        if (cabinetId is not { } id)
        {
            return CabinetDefaults.None;
        }

        // The shelf and every shelf above it in one query, matched on the stored ancestry
        // array rather than by walking parents. A shelf created moments ago in this same unit
        // of work is not in the database yet, so the tracked copies are folded in — otherwise
        // a folder drop would silently miss the defaults of the shelves it just created.
        var self = db.Cabinets.Local.FirstOrDefault(c => c.Id == id)
            ?? await db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (self is null)
        {
            return CabinetDefaults.None;
        }

        var ancestorIds = self.AncestorIds;
        var chain = await db.Cabinets.AsNoTracking()
            .Where(c => ancestorIds.Contains(c.Id))
            .ToListAsync(ct);
        foreach (var tracked in db.Cabinets.Local.Where(c => ancestorIds.Contains(c.Id)))
        {
            chain.RemoveAll(c => c.Id == tracked.Id);
            chain.Add(tracked);
        }

        if (chain.TrueForAll(c => c.Id != self.Id))
        {
            chain.Add(self);
        }

        return CabinetDefaultRules.Resolve(id, chain);
    }

    /// <summary>
    /// Applies the shelf's default tags and the batch's own, to the file rather than to the
    /// document.
    /// </summary>
    /// <remarks>
    /// The file is where every other tag in this application sits, and the version write path
    /// already moves taggings onto a new revision when one supersedes the old — so tagging
    /// here inherits that behaviour rather than inventing a second place tags can live.
    /// <para>
    /// A tag id naming a tag that has since been deleted is dropped rather than failing the
    /// upload: a shelf's defaults are a convenience, and an upload refused because somebody
    /// tidied up the tag list would be a very confusing thing to be told.
    /// </para>
    /// </remarks>
    private async Task ApplyTagsAsync(
        Guid fileId, IReadOnlyList<long> defaultTagIds, Guid? uploadBatchId, Guid userId, CancellationToken ct)
    {
        var wanted = new List<long>(defaultTagIds);

        if (uploadBatchId is { } batchId)
        {
            var batchTagId = await db.UploadBatches.AsNoTracking()
                .Where(b => b.Id == batchId)
                .Select(b => b.TagId)
                .FirstOrDefaultAsync(ct);
            if (batchTagId is { } tagId)
            {
                wanted.Add(tagId);
            }
        }

        if (wanted.Count == 0)
        {
            return;
        }

        var live = await db.Tags.AsNoTracking()
            .Where(t => wanted.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var tagId in wanted.Distinct().Where(live.Contains))
        {
            db.Taggings.Add(new Tagging
            {
                TagId = tagId,
                EntityType = AttachedEntityType.StoredFile,
                EntityId = fileId,
                AddedBy = userId,
            });
        }
    }

    /// <summary>
    /// Hangs the new file on the object the upload named, when it named one.
    /// </summary>
    /// <remarks>
    /// This is what makes "upload onto this cave" and "upload into this cabinet" one feature
    /// rather than two: the same request, with a different destination. The caller has already
    /// established that this person may write the target — that check needs the request, which
    /// this does not have.
    /// </remarks>
    private void AttachIfAsked(Guid fileId, UploadDestination destination, Guid userId)
    {
        if (!destination.HasAttachment)
        {
            return;
        }

        db.Attachments.Add(new Attachment
        {
            FileId = fileId,
            FeatureId = destination.AttachFeatureId,
            EntityType = destination.AttachFeatureId is null ? destination.AttachEntityType : null,
            EntityId = destination.AttachFeatureId is null ? destination.AttachEntityId : null,
            Role = destination.AttachRole ?? AttachmentRole.Other,
            AddedBy = userId,
        });
    }

    /// <summary>
    /// Documents whose current file holds exactly these bytes, oldest first — so the copy the
    /// others duplicate is the one reported.
    /// </summary>
    private async Task<IReadOnlyList<DuplicateCandidate>> DuplicateCandidatesAsync(
        string sha256, CancellationToken ct) =>
        await (from file in db.StoredFiles.AsNoTracking()
               join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
               where file.Sha256 == sha256 && version.IsCurrent
               orderby file.CreatedAt, file.Id
               select new DuplicateCandidate(version.DocumentId, file.Id))
            .Take(DuplicateCandidateLimit)
            .ToListAsync(ct);

    private readonly record struct DuplicateCandidate(Guid DocumentId, Guid FileId);
}
