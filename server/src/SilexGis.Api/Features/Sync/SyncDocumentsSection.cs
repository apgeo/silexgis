// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What a checked document row is going to do.</summary>
internal enum SyncDocumentAct
{
    /// <summary>A new document under the phone's id, its first revision made of staged bytes.</summary>
    Create,

    /// <summary>What is said about the document changes; its bytes do not.</summary>
    Describe,

    /// <summary>New bytes become the current revision; the earlier one is kept.</summary>
    Supersede,

    /// <summary>The document is removed.</summary>
    Delete,
}

/// <summary>
/// One checked document row, as plain values only — no tracked entity — so that a proposal can be
/// kept and applied later, after whatever happened in between.
/// </summary>
/// <param name="BaseRevision">The revision arbitrated against; absent for a create.</param>
/// <param name="SupersededVersionId">The revision new bytes stack onto, for <see cref="SyncDocumentAct.Supersede"/>.</param>
/// <param name="Sha256">The row's hash, lower-cased.</param>
internal sealed record SyncDocumentProposal(
    SyncDocumentAct Act,
    SyncUploadDocumentDto Row,
    DateTimeOffset? BaseRevision,
    Guid? SupersededVersionId,
    string? Sha256);

/// <summary>
/// The <c>documents</c> section: photos and documents a phone created, changed or removed, with the
/// bytes of new content sent beforehand as a staged upload the row names.
/// </summary>
/// <remarks>
/// A row is arbitrated the way a feature row is — the phone's id adopted on a create, the document's
/// revision compared on an edit, the right to remove asked separately from the right to edit — and
/// its bytes are judged by the base version the row names, or failing that by hash. A row naming
/// the current version and no staged bytes, or stating the current revision's hash, is an edit of
/// what is said about the file; staged bytes of another hash are a new revision, and the earlier
/// one stays in the document's history. Bytes are taken only from a staged upload of the caller's own, and only while the hash
/// the server computed for them is the hash the row states: a phone naming the wrong transfer must
/// not turn somebody's photograph into a different one.
/// </remarks>
internal sealed class SyncDocumentsSection(
    DocumentWriteService documents,
    UploadIngestService ingest,
    ContentIntake intake,
    IAccessService access) : SyncUploadSection<SyncUploadDocumentDto, SyncDocumentProposal>
{
    public override string Name => SyncUploadSections.Documents;

    protected override DateTimeOffset? StandingRevision(SyncDocumentProposal proposal) => proposal.BaseRevision;

    protected override SyncHeldFacts Facts(SyncDocumentProposal proposal) => proposal.Act == SyncDocumentAct.Create
        ? SyncHeldFacts.None
        : new(new SyncHeldTarget(SyncHeldTargetKind.Document, proposal.Row.Id), proposal.BaseRevision, [], false);

    protected override Guid? HeldUpload(SyncDocumentProposal proposal) => proposal.Row.StagedUploadId;

    protected override SyncUploadDocumentDto ItemOf(SyncDocumentProposal proposal) => proposal.Row;

    protected override SyncUploadDocumentDto? Rebased(SyncUploadDocumentDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    protected override IReadOnlyList<SyncUploadDocumentDto>? ItemsOf(SyncUploadRequest request) => request.Documents;

    /// <summary>
    /// An edit is accepted by the title and the caption it states, and — when it brings new bytes —
    /// by the new file, taken whole. A member the phone did not state says nothing and is not offered.
    /// </summary>
    protected override IReadOnlyList<string> AcceptableFields(SyncDocumentProposal proposal)
    {
        if (proposal.Act is not (SyncDocumentAct.Describe or SyncDocumentAct.Supersede))
        {
            return [];
        }

        var fields = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(proposal.Row.Title))
        {
            fields.Add(SyncHeldFields.Title);
        }

        if (proposal.Row.Description is not null)
        {
            fields.Add(SyncHeldFields.Description);
        }

        if (proposal.Act == SyncDocumentAct.Supersede)
        {
            fields.Add(SyncHeldFields.File);
        }

        return fields;
    }

    /// <summary>
    /// What is left out is not stated, so the write keeps what the document says now; new bytes
    /// left out make the edit one of what is said about the file, and the staged bytes stay unadopted.
    /// </summary>
    protected override Task<SyncDocumentProposal> NarrowedAsync(
        SyncUploadRun run, SyncDocumentProposal proposal, IReadOnlyList<string> fields, CancellationToken ct)
    {
        if (proposal.Act is not (SyncDocumentAct.Describe or SyncDocumentAct.Supersede))
        {
            return Task.FromResult(proposal);
        }

        var row = proposal.Row with
        {
            Title = fields.Contains(SyncHeldFields.Title) ? proposal.Row.Title : null,
            Description = fields.Contains(SyncHeldFields.Description) ? proposal.Row.Description : null,
        };
        return Task.FromResult(proposal.Act == SyncDocumentAct.Supersede && !fields.Contains(SyncHeldFields.File)
            ? proposal with { Act = SyncDocumentAct.Describe, Row = row, SupersededVersionId = null }
            : proposal with { Row = row });
    }

    protected override async Task<IReadOnlyList<HeldFieldComparison>?> CompareAsync(
        SyncHeldComparing comparing, SyncDocumentProposal proposal, CancellationToken ct)
    {
        var fields = AcceptableFields(proposal);
        if (fields.Count == 0)
        {
            return null;
        }

        var db = comparing.Db;
        var document = await db.Documents.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == proposal.Row.Id, ct);
        if (document is null)
        {
            return null;
        }

        var current = await DocumentQueries.CurrentFileAsync(db, document.Id, ct);
        var isImage = current?.File.Kind == FileKind.Image;
        var caption = isImage
            ? await db.PhotoDetails.AsNoTracking().Where(p => p.DocumentId == document.Id).Select(p => p.Caption).FirstOrDefaultAsync(ct)
            : null;
        var compared = new List<HeldFieldComparison>(fields.Count);
        foreach (var field in fields)
        {
            switch (field)
            {
                case SyncHeldFields.Title:
                    compared.Add(SyncHeldComparisons.Of(field, document.Title, proposal.Row.Title!.Trim()));
                    break;

                // Only a picture has a caption; what a phone says of any other file is not written.
                case SyncHeldFields.Description when isImage:
                    compared.Add(SyncHeldComparisons.Of(field, caption, NullIfBlank(proposal.Row.Description)));
                    break;

                case SyncHeldFields.File:
                    compared.Add(new HeldFieldComparison(
                        field,
                        current is null ? null : JsonSerializer.SerializeToNode(
                            new { fileName = current.File.OriginalName, sizeBytes = current.File.SizeBytes }, SyncUploadJson.Options),
                        JsonSerializer.SerializeToNode(
                            new { fileName = proposal.Row.FileName, sizeBytes = proposal.Row.SizeBytes }, SyncUploadJson.Options),
                        Differs: true));
                    break;
            }
        }

        return compared;
    }

    /// <summary>Checks one row. Reads only, and untracked.</summary>
    public override async Task<SyncResolution<SyncDocumentProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadDocumentDto row, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var sha256 = row.Sha256?.ToLowerInvariant();

        // Past the filter that hides removed documents, for the reason feature rows are read past
        // theirs: a document removed here and still on the phone is answered as gone, rather than
        // looking absent and being created again under the same identifier.
        var existing = await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == row.Id, ct);

        if (existing is null)
        {
            if (row.Deleted)
            {
                return Answer(Ok(row.Id, SyncRowStatus.Unchanged));
            }

            if (row.BaseRevision is not null)
            {
                return Answer(Refuse(row.Id, "sync.row_not_found",
                    "This document is not on the server. Send it as a new document."));
            }

            // Who may add a document under this set's club is asked before anything is said about
            // the bytes: an account that may not add one learns nothing about what it staged.
            if (run.Set.CavingGroupId is { } groupId
                && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, groupId))
            {
                // The upload's own code for the same refusal, met here per row because the set's
                // binding was checked for features before any section ran, not for documents.
                return Answer(Refuse(row.Id, "sync.caving_group_forbidden",
                    "You are not entitled to add documents to that caving group."));
            }

            if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, run.Set.CavingGroupId))
            {
                return Answer(Refuse(row.Id, CreateRules.ForbiddenCode, "You may not add documents here."));
            }

            if (BytesRefusal(row, sha256, await StagedAsync(db, ctx, row.StagedUploadId, tracked: false, ct)) is { } refused)
            {
                return Answer(refused);
            }

            return Write(new SyncDocumentProposal(SyncDocumentAct.Create, row, null, null, sha256));
        }

        var current = await DocumentQueries.CurrentFileAsync(db, existing.Id, ct);

        // Readability first: a caller who may not read the document learns only that its
        // identifier is taken, exactly as for a feature row.
        if (!await DocumentAccessRules.CanReadAsync(db, access, ctx, existing, current?.File, ct))
        {
            return Answer(Refuse(row.Id, "sync.id_conflict", "That identifier is already in use here."));
        }

        if (existing.DeletedAt is not null)
        {
            return row.Deleted
                ? Answer(Ok(row.Id, SyncRowStatus.Unchanged))
                : Answer(Refuse(row.Id, "sync.row_deleted", "This document was removed on the server. Drop it locally."));
        }

        // Removing a document is a right of its own, held in practice by its owner and by
        // administrators; an account that may only edit is refused, and removes its links instead.
        if (row.Deleted)
        {
            if (!(await access.DecideAsync(ctx, AccessAction.Delete, existing, ct)).Allowed)
            {
                return Answer(Refuse(row.Id, "sync.row_delete_forbidden", "You may not remove this document."));
            }
        }
        else if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, existing, current?.File, ct))
        {
            return Answer(Refuse(row.Id, "sync.row_forbidden", "You may not change this document."));
        }

        if (row.BaseRevision is null)
        {
            // A create resent after its answer was lost: nothing is written over what has happened
            // to the document since.
            return Answer(Ok(row.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SameRevision(existing.UpdatedAt, row.BaseRevision.Value))
        {
            return Answer(Conflict(row.Id, existing.UpdatedAt));
        }

        if (row.Deleted)
        {
            return Write(new SyncDocumentProposal(SyncDocumentAct.Delete, row, existing.UpdatedAt, null, sha256));
        }

        if (current is null)
        {
            // A document with no content of its own is nothing a phone could have listed, and
            // there is no revision for new bytes to stack onto.
            return Answer(Refuse(row.Id, SyncCodes.DocumentUnavailable, "This document has no file to replace."));
        }

        // With its base version named, the row says itself whether it brings bytes: a phone that
        // was handed a copy without location data, or only a rendition, holds a file whose hash
        // was never the stored one, and judging by hash alone would read its caption edit as new
        // content and put its stripped copy in place of the original.
        if (row.BaseVersionId is { } baseVersionId && baseVersionId != current.Version.Id)
        {
            return Answer(Conflict(row.Id, existing.UpdatedAt));
        }

        var bringsBytes = row.BaseVersionId is null || row.StagedUploadId is not null;
        if (bringsBytes && !string.Equals(current.File.Sha256, sha256, StringComparison.Ordinal))
        {
            if (BytesRefusal(row, sha256, await StagedAsync(db, ctx, row.StagedUploadId, tracked: false, ct)) is { } refused)
            {
                return Answer(refused);
            }

            return Write(new SyncDocumentProposal(
                SyncDocumentAct.Supersede, row, existing.UpdatedAt, current.Version.Id, sha256));
        }

        var caption = current.File.Kind == FileKind.Image
            ? await db.PhotoDetails.AsNoTracking()
                .Where(p => p.DocumentId == existing.Id)
                .Select(p => p.Caption)
                .FirstOrDefaultAsync(ct)
            : null;
        return Describes(row, existing.Title, caption, current.File.Kind)
            ? Write(new SyncDocumentProposal(SyncDocumentAct.Describe, row, existing.UpdatedAt, null, sha256))
            : Answer(Ok(row.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
    }

    /// <summary>
    /// Writes one checked row. Everything is read again rather than carried over, and nothing is
    /// written unless the document is still at the revision it was arbitrated against and the
    /// staged bytes are still there and still the bytes the row names.
    /// </summary>
    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncDocumentProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var row = proposal.Row;

        if (proposal.Act == SyncDocumentAct.Create)
        {
            return await ApplyCreateAsync(run, proposal, ct);
        }

        var document = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == row.Id, ct);
        if (document is null
            || document.DeletedAt is not null
            || !SameRevision(document.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(Conflict(row.Id, document?.UpdatedAt), null);
        }

        switch (proposal.Act)
        {
            case SyncDocumentAct.Delete:
                document.DeletedAt = DateTimeOffset.UtcNow;
                document.DeletedByUserId = run.Access.UserId;
                return new SyncApplied(Ok(row.Id, SyncRowStatus.Deleted), Line(row.Id, null, ImportDecisionAction.Attach));

            case SyncDocumentAct.Describe:
            {
                var current = await DocumentQueries.CurrentFileAsync(db, document.Id, ct);
                await DescribeAsync(db, document, row, current?.File.Kind, ct);
                Touch(db, document);
                return new SyncApplied(Ok(row.Id, SyncRowStatus.Updated), Line(row.Id, null, ImportDecisionAction.Attach));
            }

            default:
                return await ApplySupersedeAsync(run, document, proposal, ct);
        }
    }

    /// <summary>
    /// A document's revision is stamped as it is saved, so it is read back once every row has
    /// landed: the value the phone sends as its base revision next time.
    /// </summary>
    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct)
    {
        var written = results.Where(r => SyncUploadTally.IsWritten(r.Status)).Select(r => r.Id).ToList();
        if (written.Count == 0)
        {
            return results;
        }

        var stamps = await run.Db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Where(d => written.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.UpdatedAt, ct);
        return [.. results.Select(r =>
            SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var stamp) ? r with { Revision = stamp } : r)];
    }

    private async Task<SyncApplied> ApplyCreateAsync(
        SyncUploadRun run, SyncDocumentProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var row = proposal.Row;
        var ctx = run.Access;

        // Two copies of a phone's document can arrive in different batches at once; the second
        // finds the first rather than failing on the primary key.
        if (await db.Documents.AsNoTracking().IgnoreQueryFilters().AnyAsync(d => d.Id == row.Id, ct))
        {
            return new SyncApplied(Refuse(row.Id, "sync.id_conflict", "That identifier is already in use here."), null);
        }

        var (staged, content, refused) = await TakeStagedAsync(db, ctx, row, proposal.Sha256, ct);
        if (refused is not null)
        {
            return new SyncApplied(refused, null);
        }

        // Asked before the document exists, so it is never its own duplicate. A copy the caller
        // may read does not stop a phone's document — the phone has already named it and holds it
        // under that name — and a copy it may not read is stored again without a word, the pair
        // left on record for somebody who can see both, as for every other upload.
        var hiddenDuplicate = await ingest.HiddenDuplicateAsync(ctx, content!.Sha256, ct);

        // Owned by the uploader and bound to the set's club, as everything else the upload creates
        // is. No shelf and no visibility are named, so the document gets what a plain upload
        // without a destination gets.
        var made = documents.Create(
            content,
            row.Title,
            ownerUserId: ctx.UserId,
            uploadedBy: ctx.UserId,
            documentDate: null,
            cavingGroupId: run.Set.CavingGroupId,
            documentId: row.Id);
        KeepCapturedAt(made.File, row);
        if (hiddenDuplicate is { } concealed)
        {
            ingest.NoteHiddenDuplicate(content, row.Id, concealed, ctx.UserId);
        }

        var document = db.Documents.Local.First(d => d.Id == row.Id);

        // Kept as the phone said it, never as a caver: who that phone user is, is read through the
        // phone-user record whenever the document is shown.
        document.CreatedByDeviceUserId = row.CreatedByDeviceUserId;
        await DescribeAsync(db, document, row with { Title = null }, content.Kind, ct);

        // The bytes are the new file's own content now. The staged row goes, and its blob stays:
        // left behind, the row would have the sweep delete a document's bytes when it ran out.
        db.UploadSessions.Remove(staged!);

        return new SyncApplied(
            Ok(row.Id, SyncRowStatus.Created),
            Line(row.Id, made.Version.Id, ImportDecisionAction.Create));
    }

    private async Task<SyncApplied> ApplySupersedeAsync(
        SyncUploadRun run, Document document, SyncDocumentProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var row = proposal.Row;
        var (staged, content, refused) = await TakeStagedAsync(db, run.Access, row, proposal.Sha256, ct);
        if (refused is not null)
        {
            return new SyncApplied(refused, null);
        }

        // Everything else the row says lands with the bytes, and the document's revision moves:
        // a revision of its own is what the catalogue re-sends a document on.
        await DescribeAsync(db, document, row, content!.Kind, ct);
        Touch(db, document);
        db.UploadSessions.Remove(staged!);

        DocumentFile made;
        try
        {
            // Saves, inside this batch's transaction; everything tracked above goes with it.
            made = await documents.AddVersionAsync(proposal.SupersededVersionId!.Value, content, run.Access.UserId, ct);
        }
        catch (DocumentWriteException)
        {
            // Another revision landed since the row was checked. The staged bytes stay where they
            // were, so the phone re-reads the document and sends the row again against it.
            var now = await db.Documents.AsNoTracking().IgnoreQueryFilters()
                .Where(d => d.Id == row.Id).Select(d => (DateTimeOffset?)d.UpdatedAt).FirstOrDefaultAsync(ct);
            return new SyncApplied(Conflict(row.Id, now), null);
        }

        KeepCapturedAt(made.File, row);
        return new SyncApplied(
            Ok(row.Id, SyncRowStatus.Updated),
            Line(row.Id, made.Version.Id, ImportDecisionAction.Attach));
    }

    /// <summary>
    /// The staged bytes a row names, re-read, described as every upload is — hash, type from the
    /// content, the camera's facts — and checked once more against the row. Tracked, so the caller
    /// can retire the staged row in the same save that records the document.
    /// </summary>
    private async Task<(UploadSession? Staged, StoredContent? Content, SyncUploadRowResultDto? Refused)> TakeStagedAsync(
        SilexGisDbContext db, AccessContext ctx, SyncUploadDocumentDto row, string? sha256, CancellationToken ct)
    {
        var staged = await StagedAsync(db, ctx, row.StagedUploadId, tracked: true, ct);
        if (BytesRefusal(row, sha256, staged) is { } refused)
        {
            return (null, null, refused);
        }

        var content = await intake.FromStoredAsync(staged!.StoragePath, row.FileName ?? staged.OriginalName, null, ct);

        // The hash kept on the staged row was computed from these same bytes when they were
        // staged; one that differs now means the stored blob changed underneath it, and the row
        // is refused rather than made into a document of bytes nobody checked.
        return string.Equals(content.Sha256, staged.Sha256, StringComparison.Ordinal)
            ? (staged, content, null)
            : (null, null, Refuse(row.Id, SyncCodes.FileHashMismatch, "The staged bytes are not the file this row describes."));
    }

    /// <summary>
    /// The caller's own staged upload of that id, while it is still waiting: completed and
    /// described, and not past its retention. Anything else — another account's, one still
    /// arriving, one expired but not yet swept — is no staged upload at all.
    /// </summary>
    internal static Task<UploadSession?> StagedAsync(
        SilexGisDbContext db, AccessContext ctx, Guid? stagedUploadId, bool tracked, CancellationToken ct)
    {
        if (stagedUploadId is not { } id)
        {
            return Task.FromResult<UploadSession?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        var sessions = tracked ? db.UploadSessions : db.UploadSessions.AsNoTracking();
        return sessions.FirstOrDefaultAsync(
            s => s.Id == id && s.UserId == ctx.UserId && s.StagedForSync && s.StagedAt != null && s.ExpiresAt > now,
            ct);
    }

    private static SyncUploadRowResultDto? BytesRefusal(SyncUploadDocumentDto row, string? sha256, UploadSession? staged)
    {
        if (staged is null)
        {
            return Refuse(row.Id, SyncCodes.FileBytesMissing,
                "The bytes of this file are not waiting here. Upload them, then send the row again.");
        }

        return !string.Equals(staged.Sha256, sha256, StringComparison.Ordinal)
            || (row.SizeBytes is { } size && size != staged.DeclaredSizeBytes)
            ? Refuse(row.Id, SyncCodes.FileHashMismatch, "The staged bytes are not the file this row describes.")
            : null;
    }

    /// <summary>Whether the row says anything about the document that differs from what is here.</summary>
    private static bool Describes(SyncUploadDocumentDto row, string title, string? caption, FileKind kind) =>
        (!string.IsNullOrWhiteSpace(row.Title) && row.Title != title)
        || (kind == FileKind.Image && row.Description is not null && NullIfBlank(row.Description) != caption);

    /// <summary>
    /// Applies the row's title and, for a picture, its caption — the phone's "description".
    /// Tracked, not saved.
    /// </summary>
    /// <remarks>
    /// An absent member says nothing, so a phone that does not know a caption leaves one somebody
    /// wrote in the browser as it stands; an empty one clears it. A title cannot be cleared: a
    /// document always has a name, and a blank one keeps the name it has.
    /// </remarks>
    private static async Task DescribeAsync(
        SilexGisDbContext db, Document document, SyncUploadDocumentDto row, FileKind? kind, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(row.Title))
        {
            document.Title = row.Title.Trim();
        }

        if (kind != FileKind.Image || row.Description is null)
        {
            return;
        }

        var caption = NullIfBlank(row.Description);
        var details = db.PhotoDetails.Local.FirstOrDefault(p => p.DocumentId == document.Id)
            ?? await db.PhotoDetails.FirstOrDefaultAsync(p => p.DocumentId == document.Id, ct);
        if (details is null)
        {
            if (caption is not null)
            {
                db.PhotoDetails.Add(new PhotoDetails { DocumentId = document.Id, Caption = caption });
            }

            return;
        }

        details.Caption = caption;
    }

    /// <summary>
    /// The phone's capture time, kept only where the file's own metadata stated none: the file is
    /// the better witness, and a phone's clock is only a fallback.
    /// </summary>
    private static void KeepCapturedAt(StoredFile file, SyncUploadDocumentDto row)
    {
        if (file.ContentCreatedAt is null && row.CapturedAt is { } capturedAt)
        {
            file.ContentCreatedAt = capturedAt.ToUniversalTime();
        }
    }

    /// <summary>
    /// Moves the document's revision when what changed lives beside it — a caption, a new
    /// revision — so the catalogue, which re-sends a document on its revision, sends it again.
    /// </summary>
    private static void Touch(SilexGisDbContext db, Document document) =>
        db.Entry(document).Property(d => d.UpdatedAt).IsModified = true;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Compared at the precision the database keeps, for the reason a feature row's revision is.
    /// </summary>
    private static bool SameRevision(DateTimeOffset stored, DateTimeOffset claimed) =>
        stored.UtcTicks / 10 == claimed.UtcTicks / 10;

    private static ImportBatchItem Line(Guid documentId, Guid? versionId, ImportDecisionAction action) => new()
    {
        DocumentId = documentId,
        DocumentVersionId = versionId,
        Action = action,
    };

    private static SyncResolution<SyncDocumentProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncDocumentProposal>.Answer(result);

    private static SyncResolution<SyncDocumentProposal> Write(SyncDocumentProposal proposal) =>
        SyncResolution<SyncDocumentProposal>.Write(proposal);

    private static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status, DateTimeOffset? revision = null) =>
        new(id, status, revision, null, null);

    private static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);

    private static SyncUploadRowResultDto Conflict(Guid id, DateTimeOffset? revision) =>
        new(id, SyncRowStatus.Conflict, revision, "sync.conflict",
            "This document changed on the server after you last read it.");
}
