// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What the arbitration of one raster map or pin decided to write.</summary>
internal enum SyncRasterAct
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

/// <summary>
/// One raster map the first step found writable, as plain values that can be kept and applied
/// later: what the phone sent, what to do with it, and the revision it was arbitrated against.
/// </summary>
/// <param name="SupersededVersionId">
/// The revision of the picture new bytes stack onto, when an edit brings a different picture.
/// </param>
internal sealed record SyncRasterMapProposal(
    SyncRasterAct Act,
    SyncUploadRasterMapDto Map,
    DateTimeOffset? BaseRevision = null,
    Guid? SupersededVersionId = null,
    string? Sha256 = null);

/// <summary>One pin the first step found writable, as plain values.</summary>
internal sealed record SyncRasterPinProposal(
    SyncRasterAct Act, SyncUploadRasterPinDto Pin, DateTimeOffset? BaseRevision = null);

/// <summary>The words the two raster sections refuse and answer in.</summary>
internal static class SyncRasterAnswers
{
    public const string CaveNotFound = "sync.cave_not_found";
    public const string AreaNotFound = "sync.area_not_found";
    public const string MapNotFound = "sync.raster_map_not_found";
    public const string PlaceNotFound = "sync.place_not_found";
    public const string PinTaken = "sync.pin_exists";
    public const string CaveFixed = "sync.map_cave_fixed";
    public const string NotAnImage = "sync.raster_not_an_image";

    public static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status, DateTimeOffset? revision = null) =>
        new(id, status, revision, null, null);

    public static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);

    public static SyncUploadRowResultDto Conflict(Guid id, DateTimeOffset? revision) =>
        new(id, SyncRowStatus.Conflict, revision, "sync.conflict",
            "This row changed on the server after you last read it.");

    public static SyncUploadRowResultDto Taken(Guid id) =>
        Refuse(id, "sync.id_conflict", "That identifier is already in use here.");

    /// <summary>
    /// A live cave the caller may read. Absent and unreadable are answered alike, so a map cannot be
    /// used to learn whether a hidden cave exists.
    /// </summary>
    public static async Task<Feature?> ReadableCaveAsync(
        SyncUploadRun run, IAccessService access, Guid caveId, CancellationToken ct)
    {
        var cave = await run.Db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(run.Access, AccessAction.Read, cave, ct)).Allowed
            ? cave
            : null;
    }

    /// <summary>
    /// A live feature inside <paramref name="caveId"/> the caller may read — a place or an area of
    /// that cave. Absent, unreadable and in another cave are answered alike.
    /// </summary>
    public static async Task<bool> InCaveAndReadableAsync(
        SyncUploadRun run, IAccessService access, Guid featureId, Guid caveId, CancellationToken ct)
    {
        var feature = await run.Db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == featureId, ct);
        return feature is not null
            && feature.AncestorIds.Contains(caveId)
            && (await access.DecideAsync(run.Access, AccessAction.Read, feature, ct)).Allowed;
    }
}

/// <summary>
/// The upload's <c>rasterMaps</c> section: a phone's pictures of caves, each arbitrated on its own
/// against the maps this server holds.
/// </summary>
/// <remarks>
/// <para>
/// A map belongs to its cave, and changing it takes the right to change the cave. The cave has to
/// be one the uploader may read — one that is not here and one the uploader may not read are
/// answered alike. A cave the uploader may read but not place is accepted: a map carries no
/// position, and who is later given it is decided per reader by the rasters stream.
/// </para>
/// <para>
/// A new map brings its picture as staged bytes, which become a document owned by the uploader and
/// bound to the set's club, and a link of that document to the cave with the map role — which is
/// how the browser lists the map without a screen of its own. An edit that names staged bytes of
/// another hash gives the picture a new revision; the earlier one stays in the document's history.
/// Removing the map removes its picture's document with it, so the browser's list follows, and an
/// undo brings both back. A map stays with the cave it was made for.
/// </para>
/// </remarks>
internal sealed class SyncRasterMapsSection(
    DocumentWriteService documents,
    ContentIntake intake,
    IAccessService access) : SyncUploadSection<SyncUploadRasterMapDto, SyncRasterMapProposal>
{
    public override string Name => SyncUploadSections.RasterMaps;

    protected override IReadOnlyList<SyncUploadRasterMapDto>? ItemsOf(SyncUploadRequest request) =>
        request.RasterMaps;

    protected override IEnumerable<Guid> Refers(SyncRasterMapProposal proposal) =>
        new[] { proposal.Map.CaveId, proposal.Map.CaveAreaId }.OfType<Guid>().Distinct();

    protected override DateTimeOffset? StandingRevision(SyncRasterMapProposal proposal) => proposal.BaseRevision;

    protected override SyncHeldFacts Facts(SyncRasterMapProposal proposal) => new(
        proposal.Act == SyncRasterAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.RasterMap, proposal.Map.Id),
        proposal.Act == SyncRasterAct.Create ? null : proposal.BaseRevision,
        [.. Refers(proposal).Select(id => new SyncHeldTarget(SyncHeldTargetKind.Feature, id))],
        false);

    /// <summary>A held new map, or a held new picture, keeps its staged bytes while it waits.</summary>
    protected override Guid? HeldUpload(SyncRasterMapProposal proposal) =>
        proposal.Act == SyncRasterAct.Delete ? null : proposal.Map.StagedUploadId;

    protected override SyncUploadRasterMapDto ItemOf(SyncRasterMapProposal proposal) => proposal.Map;

    protected override SyncUploadRasterMapDto? Rebased(SyncUploadRasterMapDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    public override async Task<SyncResolution<SyncRasterMapProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadRasterMapDto item, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var sha256 = item.Sha256?.ToLowerInvariant();

        // Read past the filter that hides removed rows, for the reason the feature rows are: a map
        // removed here and still on the phone must be answered as gone, not re-created.
        var existing = await db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == item.Id, ct);

        if (existing is null)
        {
            if (item.Deleted)
            {
                return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged));
            }

            if (item.BaseRevision is not null)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_not_found",
                    "This map is not on the server. Send it as a new one."));
            }

            var cave = await SyncRasterAnswers.ReadableCaveAsync(run, access, item.CaveId!.Value, ct);
            if (cave is null)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.CaveNotFound, "That cave is not on this server."));
            }

            if (item.CaveAreaId is { } areaId
                && !await SyncRasterAnswers.InCaveAndReadableAsync(run, access, areaId, cave.Id, ct))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.AreaNotFound, "That area is not in that cave."));
            }

            if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not add maps to this cave."));
            }

            // Who may add a document under this set's club is asked before anything is said about
            // the bytes: an account that may not add one learns nothing about what it staged.
            if (run.Set.CavingGroupId is { } groupId
                && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, groupId))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.caving_group_forbidden",
                    "You are not entitled to add documents to that caving group."));
            }

            if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, run.Set.CavingGroupId))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, CreateRules.ForbiddenCode, "You may not add documents here."));
            }

            if (BytesRefusal(item, sha256, await SyncDocumentsSection.StagedAsync(db, ctx, item.StagedUploadId, tracked: false, ct)) is { } refused)
            {
                return Answer(refused);
            }

            return Write(new SyncRasterMapProposal(SyncRasterAct.Create, item, Sha256: sha256));
        }

        // Readability first: a caller who may not read the map's cave learns only that its
        // identifier is taken.
        var existingCave = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == existing.CaveFeatureId, ct);
        if (existingCave is null || !(await access.DecideAsync(ctx, AccessAction.Read, existingCave, ct)).Allowed)
        {
            return Answer(SyncRasterAnswers.Taken(item.Id));
        }

        if (existing.DeletedAt is not null)
        {
            return item.Deleted
                ? Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged))
                : Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_deleted",
                    "This map was removed on the server. Drop it locally."));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, existingCave, ct)).Allowed)
        {
            return Answer(item.Deleted
                ? SyncRasterAnswers.Refuse(item.Id, "sync.row_delete_forbidden", "You may not remove this map.")
                : SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not write this map."));
        }

        if (item.BaseRevision is null)
        {
            // Sent as new and already here: the phone did not hear the first answer.
            return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SyncRevisions.Same(existing.UpdatedAt, item.BaseRevision.Value))
        {
            return Answer(SyncRasterAnswers.Conflict(item.Id, existing.UpdatedAt));
        }

        if (item.Deleted)
        {
            return Write(new SyncRasterMapProposal(SyncRasterAct.Delete, item, existing.UpdatedAt));
        }

        if (item.CaveId != existing.CaveFeatureId)
        {
            return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.CaveFixed,
                "A map stays with the cave it was made for. Remove it and send a new one."));
        }

        if (item.CaveAreaId is { } newArea
            && newArea != existing.CaveAreaFeatureId
            && !await SyncRasterAnswers.InCaveAndReadableAsync(run, access, newArea, existing.CaveFeatureId, ct))
        {
            return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.AreaNotFound, "That area is not in that cave."));
        }

        // A different picture arrives as staged bytes of another hash than the current revision's.
        if (item.StagedUploadId is not null)
        {
            var current = await DocumentQueries.CurrentFileAsync(db, existing.DocumentId, ct);
            if (current is not null && !string.Equals(current.File.Sha256, sha256, StringComparison.Ordinal))
            {
                if (BytesRefusal(item, sha256, await SyncDocumentsSection.StagedAsync(db, ctx, item.StagedUploadId, tracked: false, ct)) is { } refused)
                {
                    return Answer(refused);
                }

                return Write(new SyncRasterMapProposal(
                    SyncRasterAct.Update, item, existing.UpdatedAt, current.Version.Id, sha256));
            }
        }

        return Write(new SyncRasterMapProposal(SyncRasterAct.Update, item, existing.UpdatedAt));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncRasterMapProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var item = proposal.Map;
        if (proposal.Act == SyncRasterAct.Create)
        {
            return await ApplyCreateAsync(run, proposal, ct);
        }

        // Read again by id and written only while still at the revision it was arbitrated
        // against, so a proposal applied later cannot overwrite a change made in between.
        var existing = await db.RasterMaps.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == item.Id, ct);
        if (existing is null
            || existing.DeletedAt is not null
            || !SyncRevisions.Same(existing.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(SyncRasterAnswers.Conflict(item.Id, existing?.UpdatedAt), null);
        }

        if (proposal.Act == SyncRasterAct.Delete)
        {
            existing.DeletedAt = DateTimeOffset.UtcNow;

            // The picture goes with the map, the way a removed document goes: stamped, so it can
            // be brought back, and so the cave's list of files follows.
            var picture = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == existing.DocumentId, ct);
            // Only by a caller who may remove the document itself: the right to write the cave is
            // not the right to trash somebody's file, and a picture left behind is only unlisted
            // from nothing.
            if (picture is { DeletedAt: null }
                && (await access.DecideAsync(run.Access, AccessAction.Delete, picture, ct)).Allowed)
            {
                picture.DeletedAt = existing.DeletedAt;
                picture.DeletedByUserId = run.Access.UserId;
            }

            return new SyncApplied(
                SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Deleted),
                Line(item.Id, existing.DocumentId, null, ImportDecisionAction.Attach));
        }

        Guid? versionId = null;
        if (proposal.SupersededVersionId is { } superseded)
        {
            var (staged, content, refused) = await TakeStagedAsync(db, run.Access, item, proposal.Sha256, ct);
            if (refused is not null)
            {
                return new SyncApplied(refused, null);
            }

            if (content!.Kind != FileKind.Image)
            {
                return new SyncApplied(NotAnImage(item.Id), null);
            }

            db.UploadSessions.Remove(staged!);
            try
            {
                // Saves, inside this batch's transaction; everything tracked above goes with it.
                var made = await documents.AddVersionAsync(superseded, content, run.Access.UserId, ct);
                versionId = made.Version.Id;
            }
            catch (DocumentWriteException)
            {
                // Another revision landed since the row was checked; the staged bytes stay, so the
                // phone re-reads the map and sends the row again against it.
                return new SyncApplied(SyncRasterAnswers.Conflict(item.Id, existing.UpdatedAt), null);
            }
        }

        existing.CaveAreaFeatureId = item.CaveAreaId;
        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            existing.Title = item.Title.Trim();
        }

        existing.MapType = item.MapType!.Value;
        existing.SortOrder = item.SortOrder ?? existing.SortOrder;
        existing.ClientUpdatedAt = item.ClientUpdatedAt;

        // A change to the map moves its own revision even when only the picture changed.
        db.Entry(existing).Property(m => m.UpdatedAt).IsModified = true;
        return new SyncApplied(
            SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Updated),
            Line(item.Id, existing.DocumentId, versionId, ImportDecisionAction.Attach));
    }

    private async Task<SyncApplied> ApplyCreateAsync(
        SyncUploadRun run, SyncRasterMapProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var item = proposal.Map;

        // Two copies of a phone's map can arrive in different batches at once; the second finds
        // the first rather than failing on the primary key.
        if (await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().AnyAsync(m => m.Id == item.Id, ct))
        {
            return new SyncApplied(SyncRasterAnswers.Taken(item.Id), null);
        }

        var (staged, content, refused) = await TakeStagedAsync(db, run.Access, item, proposal.Sha256, ct);
        if (refused is not null)
        {
            return new SyncApplied(refused, null);
        }

        if (content!.Kind != FileKind.Image)
        {
            return new SyncApplied(NotAnImage(item.Id), null);
        }

        var title = string.IsNullOrWhiteSpace(item.Title) ? string.Empty : item.Title.Trim();

        // The picture is a document of its own under a server-minted identifier — the phone's
        // identifier is the map's — owned by the uploader and bound to the set's club, as every
        // document the upload creates is.
        var made = documents.Create(
            content,
            title,
            ownerUserId: run.Access.UserId,
            uploadedBy: run.Access.UserId,
            documentDate: null,
            cavingGroupId: run.Set.CavingGroupId);
        var documentId = made.Version.DocumentId;

        db.RasterMaps.Add(new RasterMap
        {
            Id = item.Id,
            CaveFeatureId = item.CaveId!.Value,
            CaveAreaFeatureId = item.CaveAreaId,
            Title = title,
            MapType = item.MapType!.Value,
            SortOrder = item.SortOrder ?? 0,
            DocumentId = documentId,
            OwnerUserId = run.Access.UserId,
            ClientUpdatedAt = item.ClientUpdatedAt,
        });

        // The browser lists a cave's maps by the files hung on it with the map role.
        var link = new Attachment
        {
            FileId = made.File.Id,
            FeatureId = item.CaveId.Value,
            Role = AttachmentRole.Map2d,
            Caption = string.IsNullOrEmpty(title) ? null : title,
            SortOrder = item.SortOrder ?? 0,
            AddedBy = run.Access.UserId,
        };
        db.Attachments.Add(link);

        // The bytes are the new file's own content now. The staged row goes, and its blob stays:
        // left behind, the row would have the sweep delete a document's bytes when it ran out.
        db.UploadSessions.Remove(staged!);

        return new SyncApplied(
            SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Created),
            new ImportBatchItem
            {
                RasterMapId = item.Id,
                DocumentId = documentId,
                DocumentVersionId = made.Version.Id,
                AttachmentIds = ImportBatchAttachments.Write([link.Id]),
                Action = ImportDecisionAction.Create,
            });
    }

    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct)
    {
        var written = results.Where(r => SyncUploadTally.IsWritten(r.Status)).Select(r => r.Id).ToList();
        if (written.Count == 0)
        {
            return results;
        }

        // Stamped by the save, so read back rather than predicted.
        var stamps = await run.Db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
            .Where(m => written.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.UpdatedAt, ct);
        return [.. results.Select(r => SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var at)
            ? r with { Revision = at }
            : r)];
    }

    /// <summary>
    /// The staged bytes a row names, re-read and described as every upload is, and checked once
    /// more against the row. Tracked, so the caller can retire the staged row in the same save.
    /// </summary>
    private async Task<(UploadSession? Staged, StoredContent? Content, SyncUploadRowResultDto? Refused)> TakeStagedAsync(
        SilexGisDbContext db, AccessContext ctx, SyncUploadRasterMapDto item, string? sha256, CancellationToken ct)
    {
        var staged = await SyncDocumentsSection.StagedAsync(db, ctx, item.StagedUploadId, tracked: true, ct);
        if (BytesRefusal(item, sha256, staged) is { } refused)
        {
            return (null, null, refused);
        }

        var content = await intake.FromStoredAsync(staged!.StoragePath, item.FileName ?? staged.OriginalName, null, ct);

        // The hash kept on the staged row was computed from these same bytes when they were
        // staged; one that differs now means the stored blob changed underneath it.
        return string.Equals(content.Sha256, staged.Sha256, StringComparison.Ordinal)
            ? (staged, content, null)
            : (null, null, SyncRasterAnswers.Refuse(item.Id, SyncCodes.FileHashMismatch,
                "The staged bytes are not the file this row describes."));
    }

    private static SyncUploadRowResultDto? BytesRefusal(SyncUploadRasterMapDto item, string? sha256, UploadSession? staged)
    {
        if (staged is null)
        {
            return SyncRasterAnswers.Refuse(item.Id, SyncCodes.FileBytesMissing,
                "The bytes of this picture are not waiting here. Upload them, then send the row again.");
        }

        return !string.Equals(staged.Sha256, sha256, StringComparison.Ordinal)
            || (item.SizeBytes is { } size && size != staged.DeclaredSizeBytes)
            ? SyncRasterAnswers.Refuse(item.Id, SyncCodes.FileHashMismatch, "The staged bytes are not the file this row describes.")
            : null;
    }

    private static SyncUploadRowResultDto NotAnImage(Guid id) =>
        SyncRasterAnswers.Refuse(id, SyncRasterAnswers.NotAnImage, "A raster map's picture has to be an image.");

    private static SyncResolution<SyncRasterMapProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncRasterMapProposal>.Answer(result);

    private static SyncResolution<SyncRasterMapProposal> Write(SyncRasterMapProposal proposal) =>
        SyncResolution<SyncRasterMapProposal>.Write(proposal);

    private static ImportBatchItem Line(Guid mapId, Guid documentId, Guid? versionId, ImportDecisionAction action) => new()
    {
        RasterMapId = mapId,
        DocumentId = documentId,
        DocumentVersionId = versionId,
        Action = action,
    };
}

/// <summary>
/// The upload's <c>rasterPins</c> section: the places pinned on a phone's maps, each arbitrated on
/// its own, applied after the maps and the places so a pin sent beside both finds them.
/// </summary>
/// <remarks>
/// A pin is part of its map: adding, moving or removing one needs the right to change the map's
/// cave. A pin names a place, and the place has to be one the uploader may read and one that lies
/// in the map's cave — a place that is not here, one the uploader may not read and one in another
/// cave are answered alike. A place carries one live pin per map.
/// </remarks>
internal sealed class SyncRasterPinsSection(IAccessService access)
    : SyncUploadSection<SyncUploadRasterPinDto, SyncRasterPinProposal>
{
    public override string Name => SyncUploadSections.RasterPins;

    protected override IReadOnlyList<SyncUploadRasterPinDto>? ItemsOf(SyncUploadRequest request) =>
        request.RasterPins;

    protected override IEnumerable<Guid> Refers(SyncRasterPinProposal proposal) =>
        new[] { (Guid?)proposal.Pin.RasterMapId, proposal.Pin.PlaceId }.OfType<Guid>().Distinct();

    protected override DateTimeOffset? StandingRevision(SyncRasterPinProposal proposal) => proposal.BaseRevision;

    /// <summary>
    /// A pin's pixel is where its place lies on the picture, so it is held back from a reviewer
    /// who may not be given the place's position, as a feature's point is.
    /// </summary>
    protected override SyncHeldFacts Facts(SyncRasterPinProposal proposal) => new(
        proposal.Act == SyncRasterAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.RasterPin, proposal.Pin.Id),
        proposal.Act == SyncRasterAct.Create ? null : proposal.BaseRevision,
        [
            new SyncHeldTarget(SyncHeldTargetKind.RasterMap, proposal.Pin.RasterMapId),
            .. proposal.Pin.PlaceId is { } place ? [new SyncHeldTarget(SyncHeldTargetKind.Feature, place)] : Array.Empty<SyncHeldTarget>(),
        ],
        CarriesPosition: proposal.Act != SyncRasterAct.Delete);

    protected override SyncUploadRasterPinDto ItemOf(SyncRasterPinProposal proposal) => proposal.Pin;

    protected override SyncUploadRasterPinDto? Rebased(SyncUploadRasterPinDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    public override async Task<SyncResolution<SyncRasterPinProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadRasterPinDto item, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var existing = await db.RasterPins.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == item.Id, ct);

        if (existing is null)
        {
            if (item.Deleted)
            {
                return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged));
            }

            if (item.BaseRevision is not null)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_not_found",
                    "This pin is not on the server. Send it as a new one."));
            }

            var map = await db.RasterMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == item.RasterMapId, ct);
            var cave = map is null ? null : await SyncRasterAnswers.ReadableCaveAsync(run, access, map.CaveFeatureId, ct);
            if (map is null || cave is null)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.MapNotFound,
                    "The map this pin belongs to is not on this server."));
            }

            if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not add to this map."));
            }

            if (!await SyncRasterAnswers.InCaveAndReadableAsync(run, access, item.PlaceId!.Value, map.CaveFeatureId, ct))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PlaceNotFound, "That place is not in this map's cave."));
            }

            if (await LivePinOfAsync(db, map.Id, item.PlaceId.Value, except: item.Id, ct))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PinTaken,
                    "That place is already pinned on this map."));
            }

            return Write(new SyncRasterPinProposal(SyncRasterAct.Create, item));
        }

        var owning = await db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(m => m.Id == existing.RasterMapId, ct);
        var owningCave = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == owning.CaveFeatureId, ct);

        // A pin belongs to one map for good, and one in a cave the caller may not read is somebody
        // else's: either way the identifier is taken.
        if (existing.RasterMapId != item.RasterMapId
            || owningCave is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, owningCave, ct)).Allowed)
        {
            return Answer(SyncRasterAnswers.Taken(item.Id));
        }

        if (existing.DeletedAt is not null || owning.DeletedAt is not null)
        {
            return item.Deleted
                ? Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged))
                : Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_deleted",
                    "This pin was removed on the server. Drop it locally."));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, owningCave, ct)).Allowed)
        {
            return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not write this map."));
        }

        if (item.BaseRevision is null)
        {
            return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SyncRevisions.Same(existing.UpdatedAt, item.BaseRevision.Value))
        {
            return Answer(SyncRasterAnswers.Conflict(item.Id, existing.UpdatedAt));
        }

        if (item.Deleted)
        {
            return Write(new SyncRasterPinProposal(SyncRasterAct.Delete, item, existing.UpdatedAt));
        }

        if (item.PlaceId != existing.PlaceFeatureId)
        {
            if (!await SyncRasterAnswers.InCaveAndReadableAsync(run, access, item.PlaceId!.Value, owning.CaveFeatureId, ct))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PlaceNotFound, "That place is not in this map's cave."));
            }

            if (await LivePinOfAsync(db, owning.Id, item.PlaceId.Value, except: item.Id, ct))
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PinTaken,
                    "That place is already pinned on this map."));
            }
        }

        return Write(new SyncRasterPinProposal(SyncRasterAct.Update, item, existing.UpdatedAt));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncRasterPinProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var item = proposal.Pin;
        if (proposal.Act == SyncRasterAct.Create)
        {
            // Two copies of a pin can arrive in different batches at once; the unique index on the
            // live (map, place) pair is the last word, so ask again just before writing.
            if (await LivePinOfAsync(db, item.RasterMapId, item.PlaceId!.Value, except: item.Id, ct))
            {
                return new SyncApplied(SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PinTaken,
                    "That place is already pinned on this map."), null);
            }

            db.RasterPins.Add(new RasterPin
            {
                Id = item.Id,
                RasterMapId = item.RasterMapId,
                PlaceFeatureId = item.PlaceId.Value,
                X = item.X!.Value,
                Y = item.Y!.Value,
            });
            return new SyncApplied(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Created), Line(item.Id, ImportDecisionAction.Create));
        }

        var existing = await db.RasterPins.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == item.Id, ct);
        if (existing is null
            || existing.DeletedAt is not null
            || !SyncRevisions.Same(existing.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(SyncRasterAnswers.Conflict(item.Id, existing?.UpdatedAt), null);
        }

        if (proposal.Act == SyncRasterAct.Delete)
        {
            existing.DeletedAt = DateTimeOffset.UtcNow;
            return new SyncApplied(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Deleted), Line(item.Id, ImportDecisionAction.Attach));
        }

        existing.PlaceFeatureId = item.PlaceId!.Value;
        existing.X = item.X!.Value;
        existing.Y = item.Y!.Value;
        return new SyncApplied(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Updated), Line(item.Id, ImportDecisionAction.Attach));
    }

    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct)
    {
        var written = results.Where(r => SyncUploadTally.IsWritten(r.Status)).Select(r => r.Id).ToList();
        if (written.Count == 0)
        {
            return results;
        }

        var stamps = await run.Db.RasterPins.AsNoTracking().IgnoreQueryFilters()
            .Where(p => written.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.UpdatedAt, ct);
        return [.. results.Select(r => SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var at)
            ? r with { Revision = at }
            : r)];
    }

    /// <summary>Whether another live pin already marks the place on the map.</summary>
    private static Task<bool> LivePinOfAsync(
        SilexGisDbContext db, Guid mapId, Guid placeId, Guid except, CancellationToken ct) =>
        db.RasterPins.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(p => p.RasterMapId == mapId && p.PlaceFeatureId == placeId && p.DeletedAt == null && p.Id != except, ct);

    private static SyncResolution<SyncRasterPinProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncRasterPinProposal>.Answer(result);

    private static SyncResolution<SyncRasterPinProposal> Write(SyncRasterPinProposal proposal) =>
        SyncResolution<SyncRasterPinProposal>.Write(proposal);

    private static ImportBatchItem Line(Guid id, ImportDecisionAction action) => new()
    {
        RasterPinId = id,
        Action = action,
    };
}
