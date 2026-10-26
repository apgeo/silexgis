// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What the arbitration of one recording or scan decided to write.</summary>
internal enum SyncTripAct
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

/// <summary>
/// One recording the first step found writable, as plain values that can be kept and applied
/// later: what the phone sent, what to do with it, and the revision it was arbitrated against.
/// </summary>
internal sealed record SyncTripRecordingProposal(
    SyncTripAct Act, SyncUploadTripRecordingDto Recording, DateTimeOffset? BaseRevision = null);

/// <summary>One scan the first step found writable, as plain values.</summary>
internal sealed record SyncTripPointProposal(
    SyncTripAct Act, SyncUploadTripPointDto Point, DateTimeOffset? BaseRevision = null);

/// <summary>
/// The answers and checks the two trip sections share, so a recording and a scan are refused,
/// arbitrated and stamped by the same words.
/// </summary>
internal static class SyncTripAnswers
{
    public const string CaveNotFound = "sync.cave_not_found";
    public const string RecordingNotFound = "sync.recording_not_found";
    public const string PlaceNotFound = "sync.place_not_found";

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
    /// Whether a place may be named by a scan this caller sends: a live feature the caller may
    /// read. A place that is not here and one the caller may not read are answered alike, so a
    /// scan cannot be used to learn whether a hidden place exists.
    /// </summary>
    public static async Task<bool> PlaceIsNamableAsync(
        SyncUploadRun run, IAccessService access, Guid placeId, CancellationToken ct)
    {
        var place = await run.Db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == placeId, ct);
        return place is not null && (await access.DecideAsync(run.Access, AccessAction.Read, place, ct)).Allowed;
    }
}

/// <summary>
/// The upload's <c>tripRecordings</c> section: a phone's recordings of trips, arbitrated one by one
/// against the recordings this server holds, under the trip domain's rules.
/// </summary>
/// <remarks>
/// <para>
/// A recording lands bound to the set's club, visible to that club, owned by the uploader; a set
/// that names no club keeps it private to the uploader. It names its cave, and the cave has to be
/// one the uploader may read — a cave that is not here and one the uploader may not read are
/// answered alike. A cave the uploader may read but not place is accepted: a recording carries no
/// position, and who is later given it is decided per reader by the trips stream.
/// </para>
/// <para>
/// Removing a recording needs the right to remove it, and editing it the right to edit it, the way
/// the rest of the API asks for the right the act needs.
/// </para>
/// </remarks>
internal sealed class SyncTripRecordingsSection(IAccessService access)
    : SyncUploadSection<SyncUploadTripRecordingDto, SyncTripRecordingProposal>
{
    public override string Name => SyncUploadSections.TripRecordings;

    protected override IEnumerable<Guid> Refers(SyncTripRecordingProposal proposal) =>
        proposal.Recording.CaveId is { } caveId ? [caveId] : [];

    protected override DateTimeOffset? StandingRevision(SyncTripRecordingProposal proposal) => proposal.BaseRevision;

    protected override SyncHeldFacts Facts(SyncTripRecordingProposal proposal) => new(
        proposal.Act == SyncTripAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.Recording, proposal.Recording.Id),
        proposal.Act == SyncTripAct.Create ? null : proposal.BaseRevision,
        [.. Refers(proposal).Select(id => new SyncHeldTarget(SyncHeldTargetKind.Feature, id))],
        false);

    protected override SyncUploadTripRecordingDto ItemOf(SyncTripRecordingProposal proposal) => proposal.Recording;

    protected override SyncUploadTripRecordingDto? Rebased(SyncUploadTripRecordingDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    protected override IReadOnlyList<SyncUploadTripRecordingDto>? ItemsOf(SyncUploadRequest request) =>
        request.TripRecordings;

    public override async Task<SyncResolution<SyncTripRecordingProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadTripRecordingDto item, CancellationToken ct)
    {
        var ctx = run.Access;

        // Read past the filter that hides removed rows, for the reason the feature rows are: a
        // recording removed here and still on the phone must be answered as gone, not re-created.
        var existing = await run.Db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == item.Id, ct);

        if (existing is null)
        {
            if (item.Deleted)
            {
                return Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged));
            }

            if (item.BaseRevision is not null)
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_not_found",
                    "This recording is not on the server. Send it as a new one."));
            }

            if (await CaveRefusalAsync(run, item, ct) is { } noCave)
            {
                return Answer(noCave);
            }

            var group = run.Set.CavingGroupId;
            if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, group))
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, CreateRules.ForbiddenCode, "You may not add trips here."));
            }

            if (group is { } groupId && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.TripLogs, groupId))
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, "sync.caving_group_forbidden",
                    "You are not entitled to add trips to that caving group."));
            }

            return Write(new SyncTripRecordingProposal(SyncTripAct.Create, item));
        }

        // Readability first: a caller who may not read the recording learns only that its
        // identifier is taken.
        if (!(await access.DecideAsync(ctx, AccessAction.Read, existing, ct)).Allowed)
        {
            return Answer(SyncTripAnswers.Taken(item.Id));
        }

        if (existing.DeletedAt is not null)
        {
            return item.Deleted
                ? Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged))
                : Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_deleted",
                    "This recording was removed on the server. Drop it locally."));
        }

        var needed = item.Deleted ? AccessAction.Delete : AccessAction.Write;
        if (!(await access.DecideAsync(ctx, needed, existing, ct)).Allowed)
        {
            return Answer(item.Deleted
                ? SyncTripAnswers.Refuse(item.Id, "sync.row_delete_forbidden", "You may not remove this recording.")
                : SyncTripAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not write this recording."));
        }

        if (item.BaseRevision is null)
        {
            // Sent as new and already here: the phone did not hear the first answer.
            return Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SyncRevisions.Same(existing.UpdatedAt, item.BaseRevision.Value))
        {
            return Answer(SyncTripAnswers.Conflict(item.Id, existing.UpdatedAt));
        }

        if (item.Deleted)
        {
            return Write(new SyncTripRecordingProposal(SyncTripAct.Delete, item, existing.UpdatedAt));
        }

        if (item.CaveId != existing.CaveFeatureId && await CaveRefusalAsync(run, item, ct) is { } movedToNoCave)
        {
            return Answer(movedToNoCave);
        }

        return Write(new SyncTripRecordingProposal(SyncTripAct.Update, item, existing.UpdatedAt));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncTripRecordingProposal proposal, CancellationToken ct)
    {
        var item = proposal.Recording;
        if (proposal.Act == SyncTripAct.Create)
        {
            var group = run.Set.CavingGroupId;
            run.Db.TripRecordings.Add(new TripRecording
            {
                Id = item.Id,
                CaveFeatureId = item.CaveId!.Value,
                Title = item.Title ?? string.Empty,
                Description = item.Description,
                Log = item.Log,
                StartedAt = item.StartedAt!.Value,
                EndedAt = item.EndedAt,
                CreatedByDeviceUserId = item.CreatedByDeviceUserId,
                OwnerUserId = run.Access.UserId,
                CavingGroupId = group,
                Visibility = group is null ? Visibility.Private : Visibility.CavingGroup,
                ClientUpdatedAt = item.ClientUpdatedAt,
            });
            return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Created), Line(item.Id, ImportDecisionAction.Create));
        }

        // Read again by id and written only while still at the revision it was arbitrated
        // against, so a proposal applied later cannot overwrite a change made in between.
        var existing = await run.Db.TripRecordings.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == item.Id, ct);
        if (existing is null
            || existing.DeletedAt is not null
            || !SyncRevisions.Same(existing.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(SyncTripAnswers.Conflict(item.Id, existing?.UpdatedAt), null);
        }

        if (proposal.Act == SyncTripAct.Delete)
        {
            existing.DeletedAt = DateTimeOffset.UtcNow;
            return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Deleted), Line(item.Id, ImportDecisionAction.Attach));
        }

        existing.CaveFeatureId = item.CaveId!.Value;
        existing.Title = item.Title ?? string.Empty;
        existing.Description = item.Description;
        existing.Log = item.Log;
        existing.StartedAt = item.StartedAt!.Value;
        existing.EndedAt = item.EndedAt;
        existing.ClientUpdatedAt = item.ClientUpdatedAt;
        return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Updated), Line(item.Id, ImportDecisionAction.Attach));
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
        var stamps = await run.Db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
            .Where(r => written.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.UpdatedAt, ct);
        return [.. results.Select(r => SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var at)
            ? r with { Revision = at }
            : r)];
    }

    private async Task<SyncUploadRowResultDto?> CaveRefusalAsync(
        SyncUploadRun run, SyncUploadTripRecordingDto item, CancellationToken ct)
    {
        var cave = await run.Db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == item.CaveId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(run.Access, AccessAction.Read, cave, ct)).Allowed
            ? null
            : SyncTripAnswers.Refuse(item.Id, SyncTripAnswers.CaveNotFound, "That cave is not on this server.");
    }

    private static SyncResolution<SyncTripRecordingProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncTripRecordingProposal>.Answer(result);

    private static SyncResolution<SyncTripRecordingProposal> Write(SyncTripRecordingProposal proposal) =>
        SyncResolution<SyncTripRecordingProposal>.Write(proposal);

    private static ImportBatchItem Line(Guid id, ImportDecisionAction action) => new()
    {
        TripRecordingId = id,
        Action = action,
    };
}

/// <summary>
/// The upload's <c>tripPoints</c> section: the scans on a phone's recordings, each arbitrated on
/// its own, applied after the recordings so a scan sent beside its new recording finds it.
/// </summary>
/// <remarks>
/// A scan is part of its recording: adding, editing or removing one needs the right to edit the
/// recording. A scan names its place, and the place has to be one the uploader may read — a place
/// that is not here and one the uploader may not read are answered alike.
/// </remarks>
internal sealed class SyncTripPointsSection(IAccessService access)
    : SyncUploadSection<SyncUploadTripPointDto, SyncTripPointProposal>
{
    public override string Name => SyncUploadSections.TripPoints;

    protected override IEnumerable<Guid> Refers(SyncTripPointProposal proposal) =>
        new[] { (Guid?)proposal.Point.RecordingId, proposal.Point.PlaceId }.OfType<Guid>().Distinct();

    protected override DateTimeOffset? StandingRevision(SyncTripPointProposal proposal) => proposal.BaseRevision;

    protected override SyncHeldFacts Facts(SyncTripPointProposal proposal) => new(
        proposal.Act == SyncTripAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.Point, proposal.Point.Id),
        proposal.Act == SyncTripAct.Create ? null : proposal.BaseRevision,
        [
            new SyncHeldTarget(SyncHeldTargetKind.Recording, proposal.Point.RecordingId),
            .. proposal.Point.PlaceId is { } place ? [new SyncHeldTarget(SyncHeldTargetKind.Feature, place)] : Array.Empty<SyncHeldTarget>(),
        ],
        false);

    protected override SyncUploadTripPointDto ItemOf(SyncTripPointProposal proposal) => proposal.Point;

    protected override SyncUploadTripPointDto? Rebased(SyncUploadTripPointDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    protected override IReadOnlyList<SyncUploadTripPointDto>? ItemsOf(SyncUploadRequest request) =>
        request.TripPoints;

    public override async Task<SyncResolution<SyncTripPointProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadTripPointDto item, CancellationToken ct)
    {
        var ctx = run.Access;
        var existing = await run.Db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == item.Id, ct);

        if (existing is null)
        {
            if (item.Deleted)
            {
                return Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged));
            }

            if (item.BaseRevision is not null)
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_not_found",
                    "This scan is not on the server. Send it as a new one."));
            }

            var recording = await run.Db.TripRecordings.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == item.RecordingId, ct);
            if (recording is null || !(await access.DecideAsync(ctx, AccessAction.Read, recording, ct)).Allowed)
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, SyncTripAnswers.RecordingNotFound,
                    "The recording this scan belongs to is not on this server."));
            }

            if (!(await access.DecideAsync(ctx, AccessAction.Write, recording, ct)).Allowed)
            {
                return Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not add to this recording."));
            }

            if (await PlaceRefusalAsync(run, item, ct) is { } noPlace)
            {
                return Answer(noPlace);
            }

            return Write(new SyncTripPointProposal(SyncTripAct.Create, item));
        }

        var owning = await run.Db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(r => r.Id == existing.RecordingId, ct);

        // A scan belongs to one recording for good, and one the caller may not read is somebody
        // else's: either way the identifier is taken.
        if (existing.RecordingId != item.RecordingId
            || !(await access.DecideAsync(ctx, AccessAction.Read, owning, ct)).Allowed)
        {
            return Answer(SyncTripAnswers.Taken(item.Id));
        }

        if (existing.DeletedAt is not null || owning.DeletedAt is not null)
        {
            return item.Deleted
                ? Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged))
                : Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_deleted",
                    "This scan was removed on the server. Drop it locally."));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, owning, ct)).Allowed)
        {
            return Answer(SyncTripAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not write this recording."));
        }

        if (item.BaseRevision is null)
        {
            return Answer(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SyncRevisions.Same(existing.UpdatedAt, item.BaseRevision.Value))
        {
            return Answer(SyncTripAnswers.Conflict(item.Id, existing.UpdatedAt));
        }

        if (item.Deleted)
        {
            return Write(new SyncTripPointProposal(SyncTripAct.Delete, item, existing.UpdatedAt));
        }

        if (item.PlaceId != existing.PlaceFeatureId && await PlaceRefusalAsync(run, item, ct) is { } movedToNoPlace)
        {
            return Answer(movedToNoPlace);
        }

        return Write(new SyncTripPointProposal(SyncTripAct.Update, item, existing.UpdatedAt));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncTripPointProposal proposal, CancellationToken ct)
    {
        var item = proposal.Point;
        if (proposal.Act == SyncTripAct.Create)
        {
            run.Db.TripRecordingPoints.Add(new TripRecordingPoint
            {
                Id = item.Id,
                RecordingId = item.RecordingId,
                PlaceFeatureId = item.PlaceId,
                ScannedAt = item.ScannedAt!.Value,
                Notes = item.Notes,
                CreatedByDeviceUserId = item.CreatedByDeviceUserId,
            });
            return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Created), Line(item.Id, ImportDecisionAction.Create));
        }

        var existing = await run.Db.TripRecordingPoints.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == item.Id, ct);
        if (existing is null
            || existing.DeletedAt is not null
            || !SyncRevisions.Same(existing.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(SyncTripAnswers.Conflict(item.Id, existing?.UpdatedAt), null);
        }

        if (proposal.Act == SyncTripAct.Delete)
        {
            existing.DeletedAt = DateTimeOffset.UtcNow;
            return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Deleted), Line(item.Id, ImportDecisionAction.Attach));
        }

        existing.PlaceFeatureId = item.PlaceId;
        existing.ScannedAt = item.ScannedAt!.Value;
        existing.Notes = item.Notes;
        return new SyncApplied(SyncTripAnswers.Ok(item.Id, SyncRowStatus.Updated), Line(item.Id, ImportDecisionAction.Attach));
    }

    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct)
    {
        var written = results.Where(r => SyncUploadTally.IsWritten(r.Status)).Select(r => r.Id).ToList();
        if (written.Count == 0)
        {
            return results;
        }

        var stamps = await run.Db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
            .Where(p => written.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.UpdatedAt, ct);
        return [.. results.Select(r => SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var at)
            ? r with { Revision = at }
            : r)];
    }

    private async Task<SyncUploadRowResultDto?> PlaceRefusalAsync(
        SyncUploadRun run, SyncUploadTripPointDto item, CancellationToken ct) =>
        item.PlaceId is { } placeId && !await SyncTripAnswers.PlaceIsNamableAsync(run, access, placeId, ct)
            ? SyncTripAnswers.Refuse(item.Id, SyncTripAnswers.PlaceNotFound, "That place is not on this server.")
            : null;

    private static SyncResolution<SyncTripPointProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncTripPointProposal>.Answer(result);

    private static SyncResolution<SyncTripPointProposal> Write(SyncTripPointProposal proposal) =>
        SyncResolution<SyncTripPointProposal>.Write(proposal);

    private static ImportBatchItem Line(Guid id, ImportDecisionAction action) => new()
    {
        TripRecordingPointId = id,
        Action = action,
    };
}
