// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static partial class TripLogEndpoints
{
    /// <summary>
    /// The request as the write core reads it: geometry parsed out of the wire format, sections
    /// as raw text, nothing left that knows what JSON is.
    /// </summary>
    /// <remarks>
    /// A shape that arrived and could not be read as a geometry is reported rather than dropped,
    /// because the refusal it earns is ordered with the reference checks the core makes and
    /// therefore falls after the decision about whether this caller may create a trip at all.
    /// </remarks>
    private static TripWriteInput ToInput(TripLogWriteRequest request) => new()
    {
        Title = request.Title,
        TripTypeId = request.TripTypeId,
        TripDate = request.TripDate,
        TripDateEnd = request.TripDateEnd,
        EntryTime = request.EntryTime,
        ExitTime = request.ExitTime,
        Description = request.Description,
        Results = request.Results,
        WeatherConditions = request.WeatherConditions,
        LocationText = request.LocationText,
        OrganizingCavingGroupId = request.OrganizingCavingGroupId,
        Geom = request.Geom?.ToGeometryOrNull(),
        MeetingGeom = request.MeetingGeom?.ToGeometryOrNull(),
        GeometryMalformed = Unreadable(request.Geom) || Unreadable(request.MeetingGeom),
        CaveIds = request.CaveIds,
        Participants = [.. request.Participants.Select(ToEntry)],
        Proposers = request.Proposers is { } proposers ? [.. proposers.Select(ToEntry)] : null,
        CavingGroupId = request.CavingGroupId,
        Visibility = request.Visibility,
        DepthReachedM = request.DepthReachedM,
        LengthSurveyedM = request.LengthSurveyedM,
        SurveyStations = request.SurveyStations,
        RopeMetres = request.RopeMetres,
        HadIncident = request.HadIncident,
        MaxParticipants = request.MaxParticipants,
        Sections = SectionsOf(request),
    };

    private static bool Unreadable(GeoJsonGeometry? geometry) =>
        geometry is not null && geometry.ToGeometryOrNull() is null;

    private static TripRosterEntry ToEntry(TripParticipantWrite write) => new()
    {
        CaverId = write.CaverId,
        NewCaverName = write.NewCaverName,
        RoleId = write.RoleId,
        EntryTime = write.EntryTime,
        ExitTime = write.ExitTime,
        Note = write.Note,
    };

    /// <summary>
    /// The three sections as raw text, or null for one the request does not mention. Absent and
    /// "an empty object" are different answers: the first leaves what is stored alone, the
    /// second clears it.
    /// </summary>
    private static TripSectionWrite SectionsOf(TripLogWriteRequest request) => new(
        RawSection(request.FieldData), RawSection(request.Logistics), RawSection(request.Safety));

    private static string? RawSection(JsonElement? section) =>
        section is { ValueKind: JsonValueKind.Object } value ? value.GetRawText() : null;

    /// <summary>
    /// Batch-loads caves/participants, applies cave-link redaction, and holds back the parts of
    /// a trip that answer to a narrower audience than the trip itself.
    /// </summary>
    /// <remarks>
    /// Reachable from the rest of this slice on purpose, and the only way into a trip's contents:
    /// what a caller may be told about a trip is decided here, once, so a surface that renders a
    /// trip some other way — a written-up document, say — inherits every one of these decisions
    /// instead of restating them. A second reading of the tables is how two surfaces come to
    /// disagree about who may see what, long after both were written.
    /// </remarks>
    internal static async Task<List<TripLogDto>> MapWithChildrenAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        UserContext user,
        IReadOnlyList<TripLog> trips,
        CancellationToken ct)
    {
        var tripIds = trips.Select(x => x.Id).ToList();

        // Every role at once — the list means "caves this trip is about", which is what the
        // roles collectively say. Narrowed to caves because that is what the field promises and
        // what its readers resolve; a role naming a spring or a shaft belongs to the roles, not
        // here. Distinct because two roles naming one cave are two rows and one cave.
        var caveLinks = await TripRoleLinks.PairsForAsync(db, tripIds, FeatureKind.Cave, ct);

        // Which camp gathered each trip, narrowed to the camps this caller may read. A camp is
        // governed in its own right, so naming one on a trip a caller may read would hand them
        // the identity of a thing they have no right to open — and the identity is enough to ask
        // for it. Filtered in the statement, not after it, for the reason every other listing
        // here is: a filter applied to results is a filter somebody later forgets to apply.
        var readableCampIds = db.Expeditions.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Expeditions).Select(e => e.Id);
        var campOfTrip = await db.ExpeditionTrips.AsNoTracking()
            .Where(m => tripIds.Contains(m.TripLogId) && readableCampIds.Contains(m.ExpeditionId))
            .Select(m => new { m.TripLogId, m.ExpeditionId })
            .ToDictionaryAsync(m => m.TripLogId, m => m.ExpeditionId, ct);

        // When the pass that watches for overdue parties last finished, asked only when one of
        // these trips actually has a live check — the answer means nothing for a trip nobody
        // arranged one for, and asking anyway would put a query on every listing of every trip.
        //
        // Only a pass that succeeded counts. A pass that failed checked nothing, and a failure
        // that reported itself as "last checked at" would be the exact reassurance this value
        // exists to withhold: an armed alarm whose watcher has not run is unchecked, and a surface
        // has to be able to say so. Null — no pass has ever completed — says the same thing more
        // strongly, so it is never dressed up as "not applicable".
        DateTimeOffset? lastSwept = null;
        HashSet<Guid> onTheTrip = [];
        var liveCallouts = trips
            .Where(t => t.CalloutState is TripCalloutState.Armed or TripCalloutState.Overdue)
            .Select(t => t.Id)
            .ToList();
        if (liveCallouts.Count > 0)
        {
            lastSwept = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.TripCalloutSweep
                    && j.Status == ProcessingJobStatus.Succeeded)
                .MaxAsync(j => j.CompletedAt, ct);

            // Asked from the one place that knows who a trip concerns, and asked only about the
            // trips where the answer could mean anything.
            onTheTrip = await TripAudience.ConcerningAsync(db, liveCallouts, user.UserId, ct);
        }

        var attentions = await TripAttentionReads.ForAsync(db, tripIds, ct);

        var roles = await TripLogWriteService.ShippedRosterRolesAsync(db, ct);
        var participantRows = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where tripIds.Contains(participant.TripLogId)
            select new
            {
                participant.TripLogId,
                participant.RoleId,
                participant.CaverId,
                caver.UserId,
                participant.EntryTime,
                participant.ExitTime,
                participant.Note,
            })
            .ToListAsync(ct);

        // Resolved rather than joined: the label a participant may be shown under is a rule with
        // one home, and it is never their address. People without an account keep their roster name.
        var labels = await CaverDirectory.ResolveLabelsAsync(
            db, user, participantRows.Select(x => x.CaverId), ct);

        var participants = participantRows
            .Select(x => new
            {
                x.TripLogId,
                x.RoleId,
                Dto = new TripParticipantDto(
                    x.CaverId,
                    labels.GetValueOrDefault(x.CaverId) ?? string.Empty,
                    x.UserId,
                    x.RoleId,
                    x.EntryTime,
                    x.ExitTime,
                    x.Note),
            })
            .ToList();

        // Which of the caves these trips name this caller may be told about — readable first,
        // then placeable. Decided for the whole page at once; what is left out is counted per
        // trip below rather than named.
        var disclosableCaves = await TripCaveDisclosure.DisclosableCaveIdsAsync(
            db, protection, ctx, [.. caveLinks.Select(x => x.FeatureId)], ct);

        // What this caller is told of each trip's own words — its account, its results and the
        // answers on its form, with the account of what went wrong kept for whoever may change
        // the trip. Asked of the one place that decides it, for the whole page at once, because
        // this answer is not the only thing that prints those words: a camp's write-up prints
        // them for each trip it gathered, and the two must hand one reader the same thing.
        var told = await TripNarrativeReads.ForAsync(access, ctx, trips, ct);

        // How much of the list its purpose names each of these trips has settled, resolved for the
        // whole page at once rather than per row: which list a trip is measured against belongs to
        // its purpose, so asking per trip would put the same query on a page fifty times. Nothing
        // here narrows the page — a trip with nothing settled is on this list exactly as a trip
        // fully settled is, because how ready a party is is a reading for that party and never a
        // rule about who may see the plan.
        var readiness = await TripChecklistReads.ReadinessForAsync(db, ctx, trips, ct);

        return [.. trips.Select(MapOne)];

        TripLogDto MapOne(TripLog trip)
        {
            var words = told[trip.Id];
            var named = caveLinks.Where(x => x.TripId == trip.Id).Select(x => x.FeatureId).ToList();
            return new TripLogDto(
                trip.Id,
                trip.Title,
                trip.TripTypeId,
                trip.TripDate,
                trip.TripDateEnd,
                trip.EntryTime,
                trip.ExitTime,
                words.Description,
                words.Results,
                trip.WeatherConditions,
                trip.LocationText,
                trip.OrganizingCavingGroupId,
                trip.Geom is null ? null : GeoJsonGeometry.From(trip.Geom),
                [.. named.Where(disclosableCaves.Contains)],
                // Everyone but the proposers, whatever job they did, so a role added to the
                // vocabulary after this was written shows up as somebody who was there rather
                // than as nobody at all. The two lists partition the roster between them.
                [.. participants.Where(x => x.TripLogId == trip.Id && x.RoleId != roles.Proposer).Select(x => x.Dto)],
                [.. participants.Where(x => x.TripLogId == trip.Id && x.RoleId == roles.Proposer).Select(x => x.Dto)],
                trip.OwnerUserId,
                trip.CavingGroupId,
                trip.Visibility,
                trip.CreatedAt,
                trip.UpdatedAt,
                trip.State,
                trip.PublishedAt,
                trip.DepthReachedM,
                trip.LengthSurveyedM,
                trip.SurveyStations,
                trip.RopeMetres,
                trip.HadIncident,
                words.FieldData,
                trip.FieldDataSchemaVersion,
                words.Logistics,
                trip.LogisticsSchemaVersion,
                words.Safety,
                words.SafetySchemaVersion,
                campOfTrip.TryGetValue(trip.Id, out var campId) ? campId : null,
                named.Count(id => !disclosableCaves.Contains(id)),
                trip.MaxParticipants,
                trip.ExpectedReturnAt,
                trip.CalloutAlarmAt,
                trip.CalloutState,
                IsWatched(trip) ? lastSwept : null,
                onTheTrip.Contains(trip.Id),
                trip.MeetingGeom is null ? null : GeoJsonGeometry.From(trip.MeetingGeom),
                readiness.TryGetValue(trip.Id, out var settled)
                    ? new TripChecklistReadinessDto(
                        settled.ChecklistId, settled.Readiness.Ticked, settled.Readiness.Total)
                    : null,
                attentions.GetValueOrDefault(trip.Id));
        }
    }
}
