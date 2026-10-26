// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What a checked link row is going to do.</summary>
internal enum SyncLinkAct
{
    /// <summary>The document is hung on the object.</summary>
    Create,

    /// <summary>The link's role, caption or place among the object's files changes.</summary>
    Update,

    /// <summary>The document is taken down from the object.</summary>
    Delete,
}

/// <summary>
/// One checked link row, as plain values only — no tracked entity — so that a proposal can be kept
/// and applied later, after whatever happened in between.
/// </summary>
/// <param name="TargetId">The feature, recording or scan the document hangs on; the row's target kind says which.</param>
/// <param name="AttachmentIds">The links that already hang the document there; one for an update, every one for a removal.</param>
/// <param name="Role">The role a new link takes — the row's, or the one that follows from the file and the object.</param>
internal sealed record SyncLinkProposal(
    SyncLinkAct Act,
    SyncUploadDocumentLinkDto Row,
    Guid TargetId,
    IReadOnlyList<Guid> AttachmentIds,
    AttachmentRole Role);

/// <summary>
/// The <c>documentLinks</c> section: where a phone hung its photos and documents, and where it took
/// them down.
/// </summary>
/// <remarks>
/// <para>
/// A link is the attachment endpoints' attachment, under the attachment endpoints' rule: hanging a
/// document on an object, or taking it down, needs Write on the object, and hanging it needs the
/// document to be one the caller may read — a link hands the document to everyone who can read
/// the object. On this channel the object must also be one the set hands the caller, with its
/// location: in the set's scope and not withheld. A link on a withheld cave is a row the documents
/// catalogue would never have handed this caller, because a row hanging there says where to look,
/// and a phone may not create what it could not have been given. A feature the same upload created
/// is the exception to both, as it is for the feature rows: the caller placed it a moment ago.
/// </para>
/// <para>
/// The object is chosen by an explicit switch over the target kinds: a feature, a trip recording,
/// or one scan within a recording. A kind the switch does not name is refused rather than read as
/// a feature. A recording is gated by its cave and a scan by its cave and the place it names, each
/// asked of the same answer a feature is — a photo on a scan of a withheld place is as much a row
/// the documents stream would never have handed this caller as a photo on the place itself. A
/// recording and a scan are written under the right to write the recording.
/// </para>
/// </remarks>
internal sealed class SyncDocumentLinksSection(
    FeatureProtection protection,
    IAccessService access) : SyncUploadSection<SyncUploadDocumentLinkDto, SyncLinkProposal>
{
    private IReadOnlySet<Guid> handed = new HashSet<Guid>();
    private IReadOnlySet<Guid> withheld = new HashSet<Guid>();
    private IReadOnlySet<Guid> createdHere = new HashSet<Guid>();

    /// <summary>The cave of each live recording the links name, directly or through a scan.</summary>
    private IReadOnlyDictionary<Guid, Guid> recordingCaves = new Dictionary<Guid, Guid>();

    /// <summary>Each live scan the links name: its recording, and the place it names if any.</summary>
    private IReadOnlyDictionary<Guid, (Guid RecordingId, Guid? PlaceId)> scans =
        new Dictionary<Guid, (Guid, Guid?)>();

    /// <summary>The history names a feature's rows by its kind; nothing else the upload wrote is a feature.</summary>
    private static readonly string[] FeatureTypeNames = [.. Enum.GetValues<FeatureKind>().Select(FeatureAudit.TypeName)];

    public override string Name => SyncUploadSections.DocumentLinks;

    /// <summary>
    /// The identity a link is kept under while it waits for a reviewer. A link has no id the phone
    /// chose — it is the document and the object it hangs on — and the attachment a write would
    /// make gets its id only when it is written, so a held link is known by a value made from the
    /// pair, the same every time the phone sends it.
    /// </summary>
    public static Guid RowIdOf(SyncUploadDocumentLinkDto row)
    {
        Span<byte> key = stackalloc byte[33];
        row.DocumentId.TryWriteBytes(key[..16]);
        row.Target.Id.TryWriteBytes(key[16..32]);
        key[32] = (byte)row.Target.Kind;
        Span<byte> digest = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(key, digest);
        return new Guid(digest[..16]);
    }

    protected override SyncUploadDocumentLinkDto ItemOf(SyncLinkProposal proposal) => proposal.Row;

    protected override SyncHeldFacts Facts(SyncLinkProposal proposal) => new(
        null,
        null,
        [
            new SyncHeldTarget(SyncHeldTargetKind.Document, proposal.Row.DocumentId),
            new SyncHeldTarget(HeldKindOf(proposal.Row.Target.Kind), proposal.Row.Target.Id),
        ],
        false);

    /// <summary>
    /// What a link hangs on, in the words a held proposal names rows by — so whether a reviewer may
    /// be shown a link is decided by the recording or scan it hangs on, not by a feature of that id.
    /// </summary>
    private static SyncHeldTargetKind HeldKindOf(SyncLinkTargetKind kind) => kind switch
    {
        SyncLinkTargetKind.TripRecording => SyncHeldTargetKind.Recording,
        SyncLinkTargetKind.TripPoint => SyncHeldTargetKind.Point,
        _ => SyncHeldTargetKind.Feature,
    };

    protected override IEnumerable<Guid> Refers(SyncLinkProposal proposal) =>
        new[] { proposal.Row.DocumentId, proposal.Row.Target.Id }.Distinct();

    /// <summary>Answered under the document's id, as every other answer about a link is when
    /// nothing was written.</summary>
    protected override (Guid RowId, Guid AnswerId) HeldIdentity(
        SyncLinkProposal proposal, SyncUploadRowResultDto written) =>
        (RowIdOf(proposal.Row), proposal.Row.DocumentId);

    protected override IReadOnlyList<SyncUploadDocumentLinkDto>? ItemsOf(SyncUploadRequest request) => request.DocumentLinks;

    /// <summary>
    /// Asks once, for every feature the links name, what the set hands this caller — through the
    /// scope rule's own home, so a link can never be accepted beside a feature the catalogue would
    /// not list — and which of them this same upload created.
    /// </summary>
    protected override async Task PrepareAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadDocumentLinkDto> items, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var recordingIds = items.Where(l => l.Target.Kind == SyncLinkTargetKind.TripRecording)
            .Select(l => l.Target.Id).Distinct().ToList();
        var scanIds = items.Where(l => l.Target.Kind == SyncLinkTargetKind.TripPoint)
            .Select(l => l.Target.Id).Distinct().ToList();

        // The recordings and scans the rows name, as they stand now: the trip sections ran first
        // and saved as they went. A removed one is not here, which answers a link on it as a link
        // on a row that is not on the server.
        var scanRows = scanIds.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking()
                .Where(p => scanIds.Contains(p.Id) && p.DeletedAt == null)
                .Select(p => new { p.Id, p.RecordingId, p.PlaceFeatureId })
                .ToListAsync(ct);
        scans = scanRows.ToDictionary(p => p.Id, p => (p.RecordingId, p.PlaceFeatureId));
        var allRecordingIds = recordingIds.Concat(scanRows.Select(p => p.RecordingId)).Distinct().ToList();
        recordingCaves = allRecordingIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.TripRecordings.AsNoTracking()
                .Where(r => allRecordingIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.CaveFeatureId, ct);

        // Every feature a gate has to be asked about: the features the rows name, the caves of
        // the recordings and the places of the scans.
        var featureIds = items.Where(l => l.Target.Kind == SyncLinkTargetKind.Feature)
            .Select(l => l.Target.Id)
            .Concat(recordingCaves.Values)
            .Concat(scanRows.Where(p => p.PlaceFeatureId != null).Select(p => p.PlaceFeatureId!.Value))
            .Distinct()
            .ToList();

        var roots = await db.SyncSetMembers.AsNoTracking()
            .Where(m => m.SyncSetId == run.Set.Id)
            .Select(m => m.RootFeatureId)
            .ToListAsync(ct);

        // The feature rows ran first and saved as they went, and every row they wrote is filed in
        // the history under this upload's batch — by the upload, or by the reviewer who accepted
        // it — which is how a link learns that its feature is one the caller created. Only a feature's own history rows count: a phone
        // chooses its documents' identifiers too, and a document created under a guarded cave's
        // identifier must not pass for the cave having been created here.
        var keys = featureIds.Select(id => id.ToString()).ToList();
        var created = (await db.AuditEntries.AsNoTracking()
                .Where(a => a.ImportBatchId == run.Batch.Id
                    && (a.Source == ChangeSource.DeviceSync || a.Source == ChangeSource.ReviewDecision)
                    && a.Action == AuditActions.Created
                    && a.EntityId != null
                    && a.EntityType != null
                    && keys.Contains(a.EntityId)
                    && FeatureTypeNames.Contains(a.EntityType))
                .Select(a => a.EntityId!)
                .ToListAsync(ct))
            .Select(Guid.Parse)
            .ToList();

        // A feature this upload created is exempt from the set's scope and from what is withheld,
        // because the caller placed it a moment ago; it must still be one the caller may read.
        createdHere = (await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => created.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var readable = SyncScope.Readable(db, ctx, roots).Where(f => featureIds.Contains(f.Id));
        var inSet = await readable.Select(f => f.Id).ToListAsync(ct);
        handed = (await (await SyncScope.WithoutWithheldAsync(readable, protection, ctx, ct))
                .Select(f => f.Id)
                .ToListAsync(ct))
            .ToHashSet();
        withheld = inSet.Where(id => !handed.Contains(id)).ToHashSet();
    }

    /// <summary>Checks one link. Reads only, and untracked.</summary>
    public override Task<SyncResolution<SyncLinkProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadDocumentLinkDto row, CancellationToken ct) =>
        row.Target.Kind switch
        {
            SyncLinkTargetKind.Feature or SyncLinkTargetKind.TripRecording or SyncLinkTargetKind.TripPoint
                => ResolveOnTargetAsync(run, row, ct),
            _ => Task.FromResult(Answer(Refuse(row.DocumentId, KindUnsupportedCode,
                "Files cannot be hung on that kind of object from a phone."))),
        };

    /// <summary>The features a link's target is gated by, or null when the target is not here.</summary>
    private IReadOnlyList<Guid>? GatesOf(SyncUploadDocumentLinkDto row)
    {
        var id = row.Target.Id;
        switch (row.Target.Kind)
        {
            case SyncLinkTargetKind.Feature:
                return [id];
            case SyncLinkTargetKind.TripRecording:
                return recordingCaves.TryGetValue(id, out var cave) ? [cave] : null;
            case SyncLinkTargetKind.TripPoint:
                if (!scans.TryGetValue(id, out var scan) || !recordingCaves.TryGetValue(scan.RecordingId, out var scanCave))
                {
                    return null;
                }

                return scan.PlaceId is { } place ? [scanCave, place] : [scanCave];
            default:
                return null;
        }
    }

    private async Task<SyncResolution<SyncLinkProposal>> ResolveOnTargetAsync(
        SyncUploadRun run, SyncUploadDocumentLinkDto row, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var kind = row.Target.Kind;
        var targetId = row.Target.Id;

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == row.DocumentId, ct);
        var current = document is null ? null : await DocumentQueries.CurrentFileAsync(db, document.Id, ct);
        if (document is null
            || current is null
            || !await DocumentAccessRules.CanReadAsync(db, access, ctx, document, current.File, ct))
        {
            // A link taken down from a document that has gone is already down.
            return row.Deleted
                ? Answer(Ok(row.DocumentId, SyncRowStatus.Unchanged))
                : Answer(Refuse(row.DocumentId, SyncCodes.DocumentUnavailable,
                    "This document is not here, or not yours to read. Send the document first."));
        }

        var target = new AttachmentTarget(EntityTypeOf(kind), targetId);
        var gates = GatesOf(row);

        // A recording or a scan the caller may not read is answered as one that is not here, so
        // a link cannot be used to learn that somebody else's recording exists. A feature needs
        // no such question: being handed is being readable.
        if (gates is null || (kind != SyncLinkTargetKind.Feature
            && !await FileAccessRules.CanReadTargetAsync(db, access, ctx, target, ct)))
        {
            return Answer(Refuse(row.DocumentId, TargetNotFoundCode,
                "This row is not on the server, or not in this set."));
        }

        foreach (var gate in gates)
        {
            if (withheld.Contains(gate) && !createdHere.Contains(gate))
            {
                return Answer(Refuse(row.DocumentId, "sync.location_forbidden",
                    "You may not hang files on this row from a phone."));
            }
        }

        foreach (var gate in gates)
        {
            if (!createdHere.Contains(gate) && !handed.Contains(gate))
            {
                return Answer(Refuse(row.DocumentId, TargetNotFoundCode,
                    "This row is not on the server, or not in this set."));
            }
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
        {
            return Answer(Refuse(row.DocumentId, "sync.row_forbidden", "You may not change what hangs on this row."));
        }

        var existing = await LinksAsync(db, row.DocumentId, kind, targetId).AsNoTracking().ToListAsync(ct);
        if (row.Deleted)
        {
            return existing.Count == 0
                ? Answer(Ok(row.DocumentId, SyncRowStatus.Unchanged))
                : Write(new SyncLinkProposal(SyncLinkAct.Delete, row, targetId, [.. existing.Select(a => a.Id)], default));
        }

        if (existing.Count == 0)
        {
            var role = row.Role ?? await DefaultRoleAsync(db, current.File.Kind, kind, targetId, ct);
            return Write(new SyncLinkProposal(SyncLinkAct.Create, row, targetId, [], role));
        }

        var link = existing[0];
        return Changes(row, link)
            ? Write(new SyncLinkProposal(SyncLinkAct.Update, row, targetId, [link.Id], link.Role))
            : Answer(Ok(link.Id, SyncRowStatus.Unchanged, link.UpdatedAt));
    }

    /// <summary>
    /// Writes one checked link, after reading again what it depends on: the document's current file
    /// and the links already there, either of which another upload may have changed in between.
    /// </summary>
    public override async Task<SyncApplied> ApplyAsync(SyncUploadRun run, SyncLinkProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var row = proposal.Row;

        switch (proposal.Act)
        {
            case SyncLinkAct.Create:
            {
                var current = await DocumentQueries.CurrentFileAsync(db, row.DocumentId, ct);
                if (current is null)
                {
                    return new SyncApplied(Refuse(row.DocumentId, SyncCodes.DocumentUnavailable,
                        "This document is not here, or not yours to read. Send the document first."), null);
                }

                // Hung meanwhile by another upload: that link is this one.
                var already = await LinksAsync(db, row.DocumentId, row.Target.Kind, proposal.TargetId)
                    .AsNoTracking().FirstOrDefaultAsync(ct);
                if (already is not null)
                {
                    return new SyncApplied(Ok(already.Id, SyncRowStatus.Unchanged, already.UpdatedAt), null);
                }

                var entityType = EntityTypeOf(row.Target.Kind);
                var attachment = new Attachment
                {
                    FileId = current.File.Id,
                    FeatureId = entityType is null ? proposal.TargetId : null,
                    EntityType = entityType,
                    EntityId = entityType is null ? null : proposal.TargetId,
                    Role = proposal.Role,
                    Caption = NullIfBlank(row.Caption),
                    SortOrder = row.SortOrder ?? 0,
                    AddedBy = run.Access.UserId,
                };
                db.Attachments.Add(attachment);
                return new SyncApplied(
                    Ok(attachment.Id, SyncRowStatus.Created),
                    Line(row.DocumentId, [attachment.Id], ImportDecisionAction.Create));
            }

            case SyncLinkAct.Update:
            {
                var link = await db.Attachments.FirstOrDefaultAsync(a => a.Id == proposal.AttachmentIds[0], ct);
                if (link is null)
                {
                    return new SyncApplied(Conflict(row.DocumentId), null);
                }

                Apply(row, link);
                return new SyncApplied(
                    Ok(link.Id, SyncRowStatus.Updated),
                    Line(row.DocumentId, [link.Id], ImportDecisionAction.Attach));
            }

            default:
            {
                var links = await db.Attachments.Where(a => proposal.AttachmentIds.Contains(a.Id)).ToListAsync(ct);
                if (links.Count == 0)
                {
                    return new SyncApplied(Ok(row.DocumentId, SyncRowStatus.Unchanged), null);
                }

                // Removed through the tracker, so the history keeps every member of each link —
                // what an undo hangs it back up from — and the removal log hands the phones the
                // tombstone.
                db.Attachments.RemoveRange(links);
                return new SyncApplied(
                    Ok(links[0].Id, SyncRowStatus.Deleted),
                    Line(row.DocumentId, [.. links.Select(a => a.Id)], ImportDecisionAction.Attach));
            }
        }
    }

    /// <summary>A link's revision is stamped as it is saved, so it is read back once every link has landed.</summary>
    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct)
    {
        var written = results.Where(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated).Select(r => r.Id).ToList();
        if (written.Count == 0)
        {
            return results;
        }

        var stamps = await run.Db.Attachments.AsNoTracking()
            .Where(a => written.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.UpdatedAt, ct);
        return [.. results.Select(r => stamps.TryGetValue(r.Id, out var stamp) ? r with { Revision = stamp } : r)];
    }

    /// <summary>The polymorphic discriminator a target kind is stored under; null for a feature, which has a column of its own.</summary>
    private static AttachedEntityType? EntityTypeOf(SyncLinkTargetKind kind) => kind switch
    {
        SyncLinkTargetKind.TripRecording => AttachedEntityType.TripRecording,
        SyncLinkTargetKind.TripPoint => AttachedEntityType.TripRecordingPoint,
        _ => null,
    };

    /// <summary>
    /// The links that hang a document on a feature, a recording or a scan, oldest first. A link points at one of the
    /// document's files, never at the document, and moves to the new file when the document gets
    /// new bytes; any file of the document counts, so a link still on an earlier file is found too.
    /// </summary>
    private static IQueryable<Attachment> LinksAsync(
        SilexGisDbContext db, Guid documentId, SyncLinkTargetKind kind, Guid targetId)
    {
        var entityType = EntityTypeOf(kind);
        var onTarget = entityType is null
            ? db.Attachments.Where(a => a.FeatureId == targetId)
            : db.Attachments.Where(a => a.EntityType == entityType && a.EntityId == targetId);
        return from link in onTarget
               join file in db.StoredFiles on link.FileId equals file.Id
               join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
               where version.DocumentId == documentId
               orderby link.CreatedAt, link.Id
               select link;
    }

    private static async Task<AttachmentRole> DefaultRoleAsync(
        SilexGisDbContext db, FileKind fileKind, SyncLinkTargetKind kind, Guid featureId, CancellationToken ct)
    {
        if (kind != SyncLinkTargetKind.Feature)
        {
            return LinkRoleDefaults.For(fileKind, LinkTargetNature.Recording);
        }

        var feature = await (from f in db.Features.AsNoTracking()
                             where f.Id == featureId
                             join t in db.FeatureTypes.AsNoTracking() on f.FeatureTypeId equals (long?)t.Id into types
                             from t in types.DefaultIfEmpty()
                             select new { f.Kind, Code = t == null ? null : t.Code })
            .FirstAsync(ct);
        return LinkRoleDefaults.For(fileKind, LinkRoleDefaults.NatureOf(feature.Kind, feature.Code));
    }

    /// <summary>Whether the row says anything about the link that differs from what is here; an absent member says nothing.</summary>
    private static bool Changes(SyncUploadDocumentLinkDto row, Attachment link) =>
        (row.Role is { } role && role != link.Role)
        || (row.Caption is not null && NullIfBlank(row.Caption) != link.Caption)
        || (row.SortOrder is { } order && order != link.SortOrder);

    private static void Apply(SyncUploadDocumentLinkDto row, Attachment link)
    {
        if (row.Role is { } role)
        {
            link.Role = role;
        }

        if (row.Caption is not null)
        {
            link.Caption = NullIfBlank(row.Caption);
        }

        if (row.SortOrder is { } order)
        {
            link.SortOrder = order;
        }
    }

    private const string TargetNotFoundCode = "sync.target_not_found";
    private const string KindUnsupportedCode = "sync.kind_unsupported";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ImportBatchItem Line(Guid documentId, IReadOnlyList<Guid> attachmentIds, ImportDecisionAction action) => new()
    {
        DocumentId = documentId,
        AttachmentIds = ImportBatchAttachments.Write(attachmentIds),
        Action = action,
    };

    private static SyncResolution<SyncLinkProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncLinkProposal>.Answer(result);

    private static SyncResolution<SyncLinkProposal> Write(SyncLinkProposal proposal) =>
        SyncResolution<SyncLinkProposal>.Write(proposal);

    private static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status, DateTimeOffset? revision = null) =>
        new(id, status, revision, null, null);

    private static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);

    private static SyncUploadRowResultDto Conflict(Guid id) =>
        new(id, SyncRowStatus.Conflict, null, "sync.conflict", "This link changed on the server after you last read it.");
}
