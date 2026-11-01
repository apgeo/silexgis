// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.PlaceStations;

namespace SilexGis.Api.Features.Sync;

/// <summary>One place-to-station mapping the first step found writable, as plain values.</summary>
internal sealed record SyncPlaceStationProposal(
    SyncRasterAct Act, SyncUploadPlaceStationDto Mapping, DateTimeOffset? BaseRevision = null);

/// <summary>
/// The upload's <c>placeStations</c> section: places a phone mapped to a station of a survey — a
/// server survey model, or a survey file the phone uploaded itself — or to the far end of a splay
/// from one. Provisional, like the pins on phones' maps: the whole pin, marker and map sync is to be
/// analysed again, and this is kept plain so it can be reshaped then.
/// </summary>
/// <remarks>
/// <para>
/// A mapping says where its place is, so writing one needs the right to change the place. The
/// survey has to be one the uploader is shown: a survey model only on a cave whose exact position
/// is open to it, as every survey-model surface asks; a survey file by the document's own rules,
/// filed on the place's cave or one of its containers. A survey not here, one not shown and one of
/// another cave are answered alike, and so are the places. A place carries one live mapping per
/// survey. A mapping stays with its place and its survey for good: the same identifier sent with
/// another is refused as taken.
/// </para>
/// <para>
/// A mapping is stored as a link of the place-at-station relation under the phone's identifier, and
/// is written only through the place-at-station store. Taking the upload back takes its mappings
/// back, as it does every other row.
/// </para>
/// </remarks>
internal sealed class SyncPlaceStationsSection(IAccessService access, FeatureProtection protection)
    : SyncUploadSection<SyncUploadPlaceStationDto, SyncPlaceStationProposal>
{
    public const string SurveyNotFound = "sync.survey_not_found";
    public const string MappingTaken = "sync.place_station_exists";

    public override string Name => SyncUploadSections.PlaceStations;

    protected override IReadOnlyList<SyncUploadPlaceStationDto>? ItemsOf(SyncUploadRequest request) =>
        request.PlaceStations;

    protected override IEnumerable<Guid> Refers(SyncPlaceStationProposal proposal) =>
        new[] { proposal.Mapping.PlaceId, proposal.Mapping.DocumentId, proposal.Mapping.SurveyModelId }
            .OfType<Guid>().Distinct();

    protected override DateTimeOffset? StandingRevision(SyncPlaceStationProposal proposal) => proposal.BaseRevision;

    /// <summary>
    /// A station of a georeferenced survey is where its place lies, so it is held back from a
    /// reviewer who may not be given the place's position, as a pin's pixel is.
    /// </summary>
    protected override SyncHeldFacts Facts(SyncPlaceStationProposal proposal) => new(
        proposal.Act == SyncRasterAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.PlaceStation, proposal.Mapping.Id),
        proposal.Act == SyncRasterAct.Create ? null : proposal.BaseRevision,
        [
            .. proposal.Mapping.PlaceId is { } place ? [new SyncHeldTarget(SyncHeldTargetKind.Feature, place)] : Array.Empty<SyncHeldTarget>(),
            .. proposal.Mapping.DocumentId is { } document ? [new SyncHeldTarget(SyncHeldTargetKind.Document, document)] : Array.Empty<SyncHeldTarget>(),
        ],
        CarriesPosition: proposal.Act != SyncRasterAct.Delete);

    protected override SyncUploadPlaceStationDto ItemOf(SyncPlaceStationProposal proposal) => proposal.Mapping;

    protected override SyncUploadPlaceStationDto? Rebased(SyncUploadPlaceStationDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    public override async Task<SyncResolution<SyncPlaceStationProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadPlaceStationDto item, CancellationToken ct)
    {
        var db = run.Db;
        var store = new PlaceStationStore(db);
        var existing = await PlaceStationQueries.Rows(db).AsNoTracking().FirstOrDefaultAsync(r => r.Id == item.Id, ct);

        // A mapping is a link under the phone's identifier, so a link of any other kind holding that
        // identifier has taken it.
        if (existing is null && await db.ResLinks.AsNoTracking().AnyAsync(l => l.Id == item.Id, ct))
        {
            return Answer(SyncRasterAnswers.Taken(item.Id));
        }

        if (existing is null)
        {
            if (item.Deleted)
            {
                return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged));
            }

            if (item.BaseRevision is not null)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_not_found",
                    "This place's station is not on the server. Send it as a new one."));
            }

            var place = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == item.PlaceId, ct);
            if (await SurveyProblemAsync(run, item, place, ct) is { } problem)
            {
                return Answer(problem);
            }

            if (!(await access.DecideAsync(run.Access, AccessAction.Write, place!, ct)).Allowed)
            {
                return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not change this place."));
            }

            if (await store.LiveMappingOfAsync(place!.Id, item.SurveyModelId, item.DocumentId, except: item.Id, ct))
            {
                return Answer(Duplicate(item.Id));
            }

            return Write(new SyncPlaceStationProposal(SyncRasterAct.Create, item));
        }

        var owning = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == existing.PlaceId, ct);

        // A mapping stays with its place and its survey, and one of a place the caller may not read
        // is somebody else's: either way the identifier is taken.
        if (owning is null
            || !(await access.DecideAsync(run.Access, AccessAction.Read, owning, ct)).Allowed
            || (!item.Deleted
                && (item.PlaceId != existing.PlaceId
                    || item.SurveyModelId != existing.SurveyModelId
                    || item.DocumentId != existing.DocumentId)))
        {
            return Answer(SyncRasterAnswers.Taken(item.Id));
        }

        if (existing.DeletedAt is not null)
        {
            return item.Deleted
                ? Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged))
                : Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_deleted",
                    "This place's station was removed on the server. Drop it locally."));
        }

        if (!(await access.DecideAsync(run.Access, AccessAction.Write, owning, ct)).Allowed)
        {
            return Answer(SyncRasterAnswers.Refuse(item.Id, "sync.row_forbidden", "You may not change this place."));
        }

        if (item.BaseRevision is null)
        {
            return Answer(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SyncRevisions.Same(existing.UpdatedAt, item.BaseRevision.Value))
        {
            return Answer(SyncRasterAnswers.Conflict(item.Id, existing.UpdatedAt));
        }

        return Write(new SyncPlaceStationProposal(
            item.Deleted ? SyncRasterAct.Delete : SyncRasterAct.Update, item, existing.UpdatedAt));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncPlaceStationProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var item = proposal.Mapping;
        var store = new PlaceStationStore(db);

        if (proposal.Act == SyncRasterAct.Create)
        {
            // Every write of a place's mappings queues on the place, so "one live mapping per place
            // in a survey" asked below is asked of what every earlier writer has committed. Two
            // copies of one mapping in different batches: the second finds what the first wrote.
            await store.LockAsync(item.PlaceId!.Value, ct);
            if (await db.ResLinks.AsNoTracking().AnyAsync(l => l.Id == item.Id, ct))
            {
                return new SyncApplied(SyncRasterAnswers.Taken(item.Id), null);
            }

            if (await store.LiveMappingOfAsync(item.PlaceId.Value, item.SurveyModelId, item.DocumentId, except: item.Id, ct))
            {
                return new SyncApplied(Duplicate(item.Id), null);
            }

            await store.AddAsync(
                item.Id, item.PlaceId.Value, item.SurveyModelId, item.DocumentId, item.Station!, item.Splay, run.Access.UserId, ct);
            return new SyncApplied(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Created), Line(item.Id, ImportDecisionAction.Create));
        }

        var existing = await store.FindAsync(item.Id, ct);
        if (existing is null
            || existing.Link.DeletedAt is not null
            || !SyncRevisions.Same(existing.Link.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(SyncRasterAnswers.Conflict(item.Id, existing?.Link.UpdatedAt), null);
        }

        await store.LockAsync(existing.PlaceId, ct);
        if (proposal.Act == SyncRasterAct.Delete)
        {
            store.Remove(existing, DateTimeOffset.UtcNow);
            return new SyncApplied(SyncRasterAnswers.Ok(item.Id, SyncRowStatus.Deleted), Line(item.Id, ImportDecisionAction.Attach));
        }

        var current = existing.Station;
        if (current is null
            || current.Value.Station != item.Station
            || !Domain.ResLinks.PlaceStationAnchor.SameSplay(current.Value.Splay, item.Splay))
        {
            store.SetStation(existing, item.Station!, item.Splay);
        }

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

        // A mapping's revision is its link's write time, stamped by the save.
        var stamps = await run.Db.ResLinks.AsNoTracking()
            .Where(l => written.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.UpdatedAt, ct);
        return [.. results.Select(r => SyncUploadTally.IsWritten(r.Status) && stamps.TryGetValue(r.Id, out var at)
            ? r with { Revision = at }
            : r)];
    }

    /// <summary>
    /// Why a new mapping's survey or place cannot be used, or null when both can. Absent, not shown
    /// and elsewhere are answered alike for each, so a mapping cannot be used to learn what exists.
    /// </summary>
    private async Task<SyncUploadRowResultDto?> SurveyProblemAsync(
        SyncUploadRun run, SyncUploadPlaceStationDto item, Feature? place, CancellationToken ct)
    {
        var db = run.Db;
        var placeReadable = place is not null
            && (await access.DecideAsync(run.Access, AccessAction.Read, place, ct)).Allowed;

        if (item.SurveyModelId is { } modelId)
        {
            var model = await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);
            var cave = model is null || !SyncSurveyModelEndpoints.Listed(model.Format)
                ? null
                : await SyncRasterAnswers.ReadableCaveAsync(run, access, model.CaveFeatureId, ct);
            if (cave is null || (await SyncWithhold.WithheldIdsAsync(protection, run.Access, [cave.Id], ct)).Count > 0)
            {
                return SyncRasterAnswers.Refuse(item.Id, SurveyNotFound, "The survey this station is in is not on this server.");
            }

            return placeReadable && place!.AncestorIds.Contains(cave.Id)
                ? null
                : SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PlaceNotFound, "That place is not in this survey's cave.");
        }

        var documentId = item.DocumentId!.Value;
        var readable = await SyncDocuments.ReadableAsync(db, run.Access, db.Documents.AsNoTracking().Where(d => d.Id == documentId), ct);
        if (!await readable.AnyAsync(ct))
        {
            return SyncRasterAnswers.Refuse(item.Id, SurveyNotFound, "The survey this station is in is not on this server.");
        }

        // The survey file has to be filed on the place's cave, or on something containing it: a
        // station of one cave's survey is no position for a place of another.
        var filedOn = placeReadable
            && await (
                from link in db.Attachments.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                where version.DocumentId == documentId
                    && link.FeatureId != null
                    && place!.AncestorIds.Contains(link.FeatureId.Value)
                select link.Id).AnyAsync(ct);
        return filedOn
            ? null
            : SyncRasterAnswers.Refuse(item.Id, SyncRasterAnswers.PlaceNotFound, "That place is not in the cave this survey file is filed on.");
    }

    private static SyncUploadRowResultDto Duplicate(Guid id) =>
        SyncRasterAnswers.Refuse(id, MappingTaken, "That place already has a station in this survey.");

    private static SyncResolution<SyncPlaceStationProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncPlaceStationProposal>.Answer(result);

    private static SyncResolution<SyncPlaceStationProposal> Write(SyncPlaceStationProposal proposal) =>
        SyncResolution<SyncPlaceStationProposal>.Write(proposal);

    private static ImportBatchItem Line(Guid id, ImportDecisionAction action) => new()
    {
        PlaceStationId = id,
        Action = action,
    };
}
