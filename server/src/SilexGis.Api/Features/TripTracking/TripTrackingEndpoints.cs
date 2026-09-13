// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Live tracking of a trip's party: an admin records where each caver is (a station of the
/// trip's survey model, or a depth the resolver turns into one), the log is append-only, and
/// what a reader learns from a position is decided here, server-side, by the same
/// exact-location rules that guard the survey model itself. A caller who may not place the
/// cave sees that tracking exists and who is in or out — never a station name or a depth.
///
/// Every position-bearing row snapshots the model's cave, and that snapshot is what the
/// withholding is evaluated against — so protection outlives the model itself, and a row
/// whose anchor is gone is withheld from everyone. Fail closed, never open.
/// </summary>
public static class TripTrackingEndpoints
{
    public static RouteGroupBuilder MapTripTrackingEndpoints(this RouteGroupBuilder api)
    {
        var tracking = api.MapGroup("/trip-logs/{tripLogId:guid}/tracking").WithTags("TripTracking");

        tracking.MapGet("/", GetAsync)
            .WithSummary("The trip's tracking state: config, teams, and each participant's latest position — positions withheld without exact-location rights on the model's cave.");
        tracking.MapPut("/", PutConfigAsync).WithValidation<TrackingConfigRequest>()
            .WithSummary("Arm, close or reconfigure tracking (trip write access, If-Match against the trip).");
        tracking.MapPost("/teams", CreateTeamAsync).WithValidation<TrackingTeamRequest>()
            .WithSummary("Create a titled team on the trip.");
        tracking.MapPut("/teams/{teamId:guid}", RenameTeamAsync).WithValidation<TrackingTeamRequest>()
            .WithSummary("Rename a team.");
        tracking.MapDelete("/teams/{teamId:guid}", DeleteTeamAsync)
            .WithSummary("Delete a team; events keep their caver and lose only the label.");
        tracking.MapPost("/events", CreateEventsAsync).WithValidation<TrackingEventRequest>()
            .WithSummary("Record one report for one or many cavers at once — never a roster edit.");
        tracking.MapGet("/events", ListEventsAsync)
            .WithSummary("The trip's tracking events, newest first; position fields follow the same withholding as the state read.");
        tracking.MapDelete("/events/{eventId:guid}", DeleteEventAsync)
            .WithSummary("Remove a wrong report; corrections are delete-and-re-enter, never edits.");
        tracking.MapPost("/resolve-depth", ResolveDepthAsync).WithValidation<TrackingResolveDepthRequest>()
            .WithSummary("Preview which stations a depth could mean, under the trip's depth filter.");

        return api;
    }

    // ---- shared guards -------------------------------------------------------------------

    /// <summary>The trip when the caller may read it; null for missing and unreadable alike.</summary>
    private static async Task<TripLog?> ReadableTripAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid tripLogId, CancellationToken ct)
    {
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        if (trip is null) return null;
        return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed ? trip : null;
    }

    /// <summary>
    /// 404 for a trip the caller was never shown, 403 for one they may read but not run —
    /// the same masking every trip write route uses.
    /// </summary>
    private static async Task<ProblemHttpResult?> WriteGuardAsync(
        IAccessService access, AccessContext ctx, TripLog? trip, CancellationToken ct)
    {
        if (trip is null) return ApiProblems.NotFound("trip_log.not_found");
        if ((await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed) return null;
        return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound("trip_log.not_found");
    }

    /// <summary>
    /// The survey model and its cave, only when the caller may both read the cave and place
    /// it. Missing, unreadable and location-closed all answer null — one shape, no leak.
    /// </summary>
    private static async Task<(SurveyModel Model, Feature Cave)?> UsableModelAsync(
        SilexGisDbContext db, IAccessService access, FeatureProtection protection, AccessContext ctx,
        Guid? surveyModelId, CancellationToken ct)
    {
        if (surveyModelId is null) return null;
        var model = await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == surveyModelId, ct);
        if (model is null) return null;
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == model.CaveFeatureId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null) return null;
        return await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct) ? (model, cave) : null;
    }

    private static async Task<IReadOnlyList<TrackingDepthResolver.Station>> StationsOfAsync(
        SilexGisDbContext db, Guid surveyModelId, CancellationToken ct)
    {
        var rows = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == surveyModelId)
            .Select(s => new { s.Name, s.SurveyName, s.Position, s.Flags })
            .ToListAsync(ct);
        return [.. rows.Select(r => new TrackingDepthResolver.Station(
            r.Name, r.SurveyName, r.Position.Coordinate.Z, (r.Flags & SurveyStationFlags.Entrance) != 0))];
    }

    /// <summary>
    /// Which of the given cave snapshots this caller may see positions in. Per cave, because
    /// history can span models and every position row carries its own anchor.
    /// </summary>
    private static async Task<HashSet<Guid>> OpenCaveIdsAsync(
        SilexGisDbContext db, IAccessService access, FeatureProtection protection, AccessContext ctx,
        IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        var open = new HashSet<Guid>();
        if (caveIds.Count == 0) return open;
        var caves = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .ToListAsync(ct);
        foreach (var cave in caves)
        {
            if (await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct)) open.Add(cave.Id);
        }
        return open;
    }

    /// <summary>A row that claims a place, however partially — anything here is location data.</summary>
    private static bool HasPosition(TripPositionEvent e) =>
        e.StationName is not null || e.DepthEnteredM is not null || e.SurveyModelId is not null;

    /// <summary>
    /// Whether this caller may see the row's position. A position row whose cave snapshot is
    /// gone answers false for everyone: with nothing left to evaluate protection against,
    /// the only safe answer is no answer.
    /// </summary>
    private static bool PositionOpen(TripPositionEvent e, HashSet<Guid> openCaves) =>
        !HasPosition(e) || (e.CaveFeatureId is not null && openCaves.Contains(e.CaveFeatureId.Value));

    // ---- reads ---------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingStateDto>, ProblemHttpResult>> GetAsync(
        Guid tripLogId, HttpContext http, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return ApiProblems.NotFound("trip_log.not_found");
        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null) return ApiProblems.NotFound("trip_log.not_found");

        var tracking = await db.TripTrackings.AsNoTracking().FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        var teams = await db.TripTeams.AsNoTracking()
            .Where(t => t.TripLogId == tripLogId).OrderBy(t => t.Title).ToListAsync(ct);
        var rosterCavers = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId).Select(p => p.CaverId).Distinct().ToListAsync(ct);

        // A tracked trip's whole event log is small (reports arrive by relayed word, minutes
        // apart) — fold the latest-per-caver in memory rather than in SQL.
        var events = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == tripLogId)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var caveIds = events.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value)
            .Concat(tracking?.CaveFeatureId is { } configCave ? [configCave] : Array.Empty<Guid>())
            .Distinct().ToList();
        var openCaves = await OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        // The config's reference station and depth filter are station vocabulary of the
        // config's cave — withheld under exactly the rule the event positions follow.
        var configHasVocabulary = tracking is not null
            && (tracking.SurveyModelId is not null || tracking.ReferenceStationName is not null || tracking.DepthFilter.Length > 0);
        var configOpen = !configHasVocabulary
            || (tracking!.CaveFeatureId is not null && openCaves.Contains(tracking.CaveFeatureId.Value));

        var withheldAny = configHasVocabulary && !configOpen;
        var byCaver = events.GroupBy(e => e.CaverId).ToDictionary(g => g.Key, g => g.ToList());
        var participants = new List<TrackingParticipantDto>();
        foreach (var caverId in rosterCavers.OrderBy(c => c))
        {
            byCaver.TryGetValue(caverId, out var own);
            var last = own?.Count > 0 ? own[^1] : null;
            // A note or an exit says something happened, not where — the displayed position
            // stays the latest report that actually claimed a place.
            var lastPositioned = own?.LastOrDefault(HasPosition);
            var lastTeamed = own?.LastOrDefault(e => e.TeamId is not null);
            var positionOpen = lastPositioned is null || PositionOpen(lastPositioned, openCaves);
            if (!positionOpen) withheldAny = true;
            participants.Add(new TrackingParticipantDto(
                caverId,
                lastTeamed?.TeamId,
                last?.Kind,
                last?.RecordedAt,
                positionOpen ? lastPositioned?.StationName : null,
                positionOpen ? lastPositioned?.DepthEnteredM : null,
                last?.Kind == TripPositionEventKind.Exited));
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(new TrackingStateDto(
            tracking?.State ?? TripTrackingState.Off,
            configOpen ? tracking?.SurveyModelId : null,
            configOpen ? tracking?.ReferenceStationName : null,
            configOpen ? tracking?.DepthFilter ?? [] : [],
            tracking?.ArmedAt,
            tracking?.ClosedAt,
            withheldAny,
            [.. teams.Select(t => new TrackingTeamDto(t.Id, t.Title))],
            participants));
    }

    private static async Task<Results<Ok<PagedResult<TrackingEventDto>>, ProblemHttpResult>> ListEventsAsync(
        Guid tripLogId, Guid? caverId, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
        SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return ApiProblems.NotFound("trip_log.not_found");
        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null) return ApiProblems.NotFound("trip_log.not_found");

        var query = db.TripPositionEvents.AsNoTracking().Where(e => e.TripLogId == tripLogId);
        if (caverId is not null) query = query.Where(e => e.CaverId == caverId);
        if (from is not null) query = query.Where(e => e.RecordedAt >= from);
        if (to is not null) query = query.Where(e => e.RecordedAt <= to);

        var (p, ps) = Paging.Normalize(page, pageSize);
        // The id is v7 (time-ordered) and unique: a batch write stamps many rows with one
        // recorded_at and one created_at, and without a unique tiebreaker page boundaries
        // repeat or drop rows.
        var result = await query
            .OrderByDescending(e => e.RecordedAt).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .ToPagedAsync(p, ps, e => e, ct);

        var caveIds = result.Items.Where(e => e.CaveFeatureId is not null)
            .Select(e => e.CaveFeatureId!.Value).Distinct().ToList();
        var openCaves = await OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        var dtos = result.Items.Select(e =>
        {
            var open = PositionOpen(e, openCaves);
            return new TrackingEventDto(
                e.Id, e.CaverId, e.TeamId, e.Kind,
                open ? e.SurveyModelId : null,
                open ? e.StationName : null,
                open ? e.DepthEnteredM : null,
                e.Note, e.RecordedAt);
        }).ToList();

        return TypedResults.Ok(new PagedResult<TrackingEventDto>(dtos, result.Page, result.PageSize, result.TotalItems));
    }

    // ---- writes --------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingStateDto>, ProblemHttpResult>> PutConfigAsync(
        Guid tripLogId, TrackingConfigRequest request, HttpContext http, SilexGisDbContext db,
        IAccessService access, FeatureProtection protection, IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        // Two coordinators can both hold the tracking panel; the config write rides the
        // trip's version like every other trip-level arrangement.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip!.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        var target = request.State!.Value;
        var tracking = await db.TripTrackings.FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        var current = tracking?.State ?? TripTrackingState.Off;
        if (!TripTrackingRules.MayTransition(current, target))
        {
            return ApiProblems.Conflict("tracking.state_invalid", $"Tracking does not move from {current} to {target}.");
        }

        // Absent fields keep what is stored (an empty string clears the reference, an empty
        // list the filter) — closing tracking must not silently rewrite the configuration
        // earlier reports were resolved under.
        var modelChanging = request.SurveyModelId is not null && request.SurveyModelId != tracking?.SurveyModelId;
        var effectiveModelId = request.SurveyModelId ?? tracking?.SurveyModelId;
        if (target == TripTrackingState.Armed && effectiveModelId is null)
        {
            return ApiProblems.Conflict("tracking.model_missing", "Tracking needs a survey model to place cavers in.");
        }

        var referenceChanging = request.ReferenceStationName is not null && request.ReferenceStationName.Length > 0;
        Guid? snapshotCave = tracking?.CaveFeatureId;
        if (modelChanging || referenceChanging)
        {
            // Choosing the station vocabulary — the model, or a reference station inside it —
            // is a placing act and takes the placing right. Closing, re-arming and filter
            // edits do not; a co-writer without exact view can still end a watch.
            var usable = await UsableModelAsync(db, access, protection, ctx!, effectiveModelId, ct);
            if (usable is null) return ApiProblems.Conflict("tracking.model_unavailable",
                "The survey model does not exist here, or its cave cannot be placed by this account.");
            snapshotCave = usable.Value.Cave.Id;
            if (referenceChanging)
            {
                var known = await db.SurveyStations.AsNoTracking()
                    .AnyAsync(s => s.SurveyModelId == usable.Value.Model.Id && s.Name == request.ReferenceStationName, ct);
                if (!known) return ApiProblems.Conflict("tracking.reference_unknown",
                    "The reference station is not a station of the chosen model.");
            }
        }

        if (tracking is null)
        {
            tracking = new Domain.Entities.TripTracking { TripLogId = tripLogId };
            db.TripTrackings.Add(tracking);
        }

        if (current != TripTrackingState.Armed && target == TripTrackingState.Armed)
        {
            tracking.ArmedAt = DateTimeOffset.UtcNow;
            tracking.ClosedAt = null;
        }
        if (current != TripTrackingState.Closed && target == TripTrackingState.Closed)
        {
            tracking.ClosedAt = DateTimeOffset.UtcNow;
        }
        tracking.State = target;
        if (request.SurveyModelId is not null)
        {
            tracking.SurveyModelId = request.SurveyModelId;
            tracking.CaveFeatureId = snapshotCave;
            if (modelChanging && !referenceChanging)
            {
                // The old reference and filter named the old model's stations; carrying them
                // to a different model would resolve depths against names that mean nothing.
                tracking.ReferenceStationName = null;
                tracking.DepthFilter = [];
            }
        }
        if (request.ReferenceStationName is not null)
        {
            tracking.ReferenceStationName = request.ReferenceStationName.Length == 0 ? null : request.ReferenceStationName;
        }
        if (request.DepthFilter is not null)
        {
            tracking.DepthFilter = [.. request.DepthFilter];
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first-ever config writes raced to insert the same one-per-trip row; the
            // loser is told to look again rather than being answered with a stack trace.
            return ApiProblems.Conflict("tracking.concurrent_write", "Another tracking write landed first — reload and retry.");
        }

        return await GetAsync(tripLogId, http, db, access, protection, accessAccessor, ct);
    }

    private static async Task<Results<Ok<TrackingTeamDto>, ProblemHttpResult>> CreateTeamAsync(
        Guid tripLogId, TrackingTeamRequest request, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var team = new TripTeam { TripLogId = tripLogId, Title = request.Title! };
        db.TripTeams.Add(team);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TrackingTeamDto(team.Id, team.Title));
    }

    private static async Task<Results<Ok<TrackingTeamDto>, ProblemHttpResult>> RenameTeamAsync(
        Guid tripLogId, Guid teamId, TrackingTeamRequest request, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var team = await db.TripTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.TripLogId == tripLogId, ct);
        if (team is null) return ApiProblems.NotFound("tracking.team_not_found");
        team.Title = request.Title!;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TrackingTeamDto(team.Id, team.Title));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTeamAsync(
        Guid tripLogId, Guid teamId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var team = await db.TripTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.TripLogId == tripLogId, ct);
        if (team is null) return ApiProblems.NotFound("tracking.team_not_found");
        db.TripTeams.Remove(team);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TrackingEventDto>>, ProblemHttpResult>> CreateEventsAsync(
        Guid tripLogId, TrackingEventRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor, IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null || user is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var tracking = await db.TripTrackings.AsNoTracking().FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking is null || tracking.State != TripTrackingState.Armed)
        {
            return ApiProblems.Conflict("tracking.not_armed", "Reports land on armed tracking only.");
        }

        var now = DateTimeOffset.UtcNow;
        var recordedAt = request.RecordedAt ?? now;
        if (recordedAt > now + TripTrackingRules.RecordedAtSkew)
        {
            return ApiProblems.BadRequest("tracking.recorded_in_future", "A report cannot be about the future.");
        }

        var caverIds = request.CaverIds!.Distinct().ToList();
        var participants = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId && caverIds.Contains(p.CaverId))
            .Select(p => p.CaverId).Distinct().ToListAsync(ct);
        if (participants.Count != caverIds.Count)
        {
            return ApiProblems.BadRequest("tracking.caver_not_participant",
                "Every reported caver has to be on the trip's roster first.");
        }

        if (request.TeamId is not null)
        {
            var teamKnown = await db.TripTeams.AsNoTracking()
                .AnyAsync(t => t.Id == request.TeamId && t.TripLogId == tripLogId, ct);
            if (!teamKnown) return ApiProblems.NotFound("tracking.team_not_found");
        }

        var kind = request.Kind!.Value;
        Guid? surveyModelId = null;
        Guid? caveFeatureId = null;
        string? stationName = null;
        decimal? depthEntered = null;

        if (kind is TripPositionEventKind.AtStation or TripPositionEventKind.AtDepth)
        {
            if (tracking.SurveyModelId is null)
            {
                return ApiProblems.Conflict("tracking.model_missing", "Tracking has no survey model to place cavers in.");
            }
            var usable = await UsableModelAsync(db, access, protection, ctx!, tracking.SurveyModelId, ct);
            if (usable is null) return ApiProblems.Conflict("tracking.model_unavailable",
                "The survey model does not exist here, or its cave cannot be placed by this account.");
            surveyModelId = usable.Value.Model.Id;
            caveFeatureId = usable.Value.Cave.Id;

            if (kind == TripPositionEventKind.AtStation)
            {
                var known = await db.SurveyStations.AsNoTracking()
                    .AnyAsync(s => s.SurveyModelId == surveyModelId && s.Name == request.StationName, ct);
                if (!known) return ApiProblems.BadRequest("tracking.station_unknown",
                    "The station is not one of the chosen model's stations.");
                stationName = request.StationName;
            }
            else
            {
                var stations = await StationsOfAsync(db, surveyModelId.Value, ct);
                var referenceZ = TrackingDepthResolver.ReferenceZ(stations, tracking.ReferenceStationName);
                if (referenceZ is null) return ApiProblems.Conflict("tracking.reference_unknown",
                    "The depth datum cannot be established for the chosen model.");
                var candidates = TrackingDepthResolver.Resolve(
                    stations, referenceZ.Value, (double)request.DepthM!.Value, tracking.DepthFilter, take: 1);
                if (candidates.Count == 0) return ApiProblems.Conflict("tracking.no_station_at_depth",
                    "No station matches that depth under the trip's depth filter.");
                stationName = candidates[0].Name;
                depthEntered = request.DepthM;
            }
        }

        var created = new List<TripPositionEvent>();
        foreach (var caverId in caverIds)
        {
            created.Add(new TripPositionEvent
            {
                TripLogId = tripLogId,
                CaverId = caverId,
                TeamId = request.TeamId,
                Kind = kind,
                SurveyModelId = surveyModelId,
                CaveFeatureId = caveFeatureId,
                StationName = stationName,
                DepthEnteredM = depthEntered,
                Note = request.Note,
                RecordedAt = recordedAt,
                RecordedByUserId = user!.UserId,
            });
        }
        db.TripPositionEvents.AddRange(created);
        await db.SaveChangesAsync(ct);

        IReadOnlyList<TrackingEventDto> dtos = [.. created.Select(e => new TrackingEventDto(
            e.Id, e.CaverId, e.TeamId, e.Kind, e.SurveyModelId, e.StationName, e.DepthEnteredM, e.Note, e.RecordedAt))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEventAsync(
        Guid tripLogId, Guid eventId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var row = await db.TripPositionEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.TripLogId == tripLogId, ct);
        if (row is null) return ApiProblems.NotFound("tracking.event_not_found");
        db.TripPositionEvents.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TrackingDepthCandidateDto>>, ProblemHttpResult>> ResolveDepthAsync(
        Guid tripLogId, TrackingResolveDepthRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var tracking = await db.TripTrackings.AsNoTracking().FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking?.SurveyModelId is null)
        {
            return ApiProblems.Conflict("tracking.model_missing", "Tracking has no survey model to place cavers in.");
        }
        var usable = await UsableModelAsync(db, access, protection, ctx!, tracking.SurveyModelId, ct);
        if (usable is null) return ApiProblems.Conflict("tracking.model_unavailable",
            "The survey model does not exist here, or its cave cannot be placed by this account.");

        var stations = await StationsOfAsync(db, usable.Value.Model.Id, ct);
        var referenceZ = TrackingDepthResolver.ReferenceZ(stations, tracking.ReferenceStationName);
        if (referenceZ is null) return ApiProblems.Conflict("tracking.reference_unknown",
            "The depth datum cannot be established for the chosen model.");

        var candidates = TrackingDepthResolver.Resolve(
            stations, referenceZ.Value, (double)request.DepthM!.Value, tracking.DepthFilter, request.Take ?? 5);
        IReadOnlyList<TrackingDepthCandidateDto> dtos = [.. candidates.Select(c =>
            new TrackingDepthCandidateDto(c.Name, c.SurveyName, Math.Round(c.DepthM, 1), Math.Round(c.DeltaM, 1)))];
        return TypedResults.Ok(dtos);
    }
}
