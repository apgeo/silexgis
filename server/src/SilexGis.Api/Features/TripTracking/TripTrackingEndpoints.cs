// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Surveys;
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
        tracking.MapPut("/participants/{caverId:guid}", SetParticipantLabelAsync)
            .WithValidation<TrackingParticipantLabelRequest>()
            .WithSummary("Name one participant as a follower of the published page sees them; an empty label returns them to the non-identifying default.");
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
    internal static async Task<TripLog?> ReadableTripAsync(
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
    internal static async Task<ProblemHttpResult?> WriteGuardAsync(
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

    /// <summary>
    /// The station of <paramref name="model"/> that <paramref name="given"/> names, answered in the
    /// survey viewer's own spelling — or null when the model has no such station.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Either spelling is accepted</b>, and which reading wins is not decided here: the ordered
    /// candidates and the reason for their order live with the conversion itself. What this adds is
    /// the lookup — the rows of one model, fetched once for both readings rather than queried twice.
    /// </para>
    /// <para>
    /// What comes back is always the viewer's spelling, whichever arrived: the stored name is read
    /// by the surface that draws the party on the model, and a name it cannot resolve is a marker
    /// that never appears and says nothing about why.
    /// </para>
    /// </remarks>
    private static async Task<string?> ResolveStationAsync(
        SilexGisDbContext db, SurveyModel model, string given, CancellationToken ct)
    {
        var candidates = SurveyStationNames.StoredCandidates(model.Format, model.RootSurveyName, given);
        var found = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id && candidates.Contains(s.Name))
            .Select(s => s.Name)
            .ToListAsync(ct);

        return SurveyStationNames.ViewerNameOfMatch(
            model.Format, model.RootSurveyName, given, found.Contains);
    }

    private static async Task<IReadOnlyList<TrackingDepthResolver.Station>> StationsOfAsync(
        SilexGisDbContext db, SurveyModel model, CancellationToken ct)
    {
        var rows = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .Select(s => new { s.Name, s.SurveyName, s.Position, s.Flags })
            .ToListAsync(ct);
        return [.. rows.Select(r => TrackingDepthResolver.Station.Of(
            model.Format, model.RootSurveyName,
            r.Name, r.SurveyName, r.Position.Coordinate.Z, (r.Flags & SurveyStationFlags.Entrance) != 0))];
    }

    // Which caves are open to this caller, whether a row claims a place, and whether that place
    // may be told to them, all live in TrackingWithholding — the published page asks the same
    // three questions on different terms, and one home is what keeps the two answers the same.

    // ---- reads ---------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingStateDto>, ProblemHttpResult>> GetAsync(
        Guid tripLogId, HttpContext http, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IOptions<TripTrackingOptions> options, CancellationToken ct)
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
        // What a published page calls each of them, where somebody chose. Shown here so the
        // panel that sets the labels can show what it set; nothing about the choice is
        // location data, so it follows the trip's own readability and nothing else.
        var labels = await db.TripTrackingParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId)
            .ToDictionaryAsync(p => p.CaverId, p => p.DisplayLabel, ct);

        // A tracked trip's whole event log is small (reports arrive by relayed word, minutes
        // apart) — fold the latest-per-caver in memory rather than in SQL.
        var events = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == tripLogId)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var caveIds = events.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value)
            .Concat(tracking?.CaveFeatureId is { } configCave ? [configCave] : Array.Empty<Guid>())
            .Distinct().ToList();
        var openCaves = await TrackingWithholding.OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        // The config's reference station and depth filter are station vocabulary of the
        // config's cave — withheld under exactly the rule the event positions follow.
        var configHasVocabulary = tracking is not null
            && (tracking.SurveyModelId is not null || tracking.ReferenceStationName is not null || tracking.DepthFilter.Length > 0);
        var configOpen = !configHasVocabulary
            || (tracking!.CaveFeatureId is not null && openCaves.Contains(tracking.CaveFeatureId.Value));

        // Whether the model this watch is armed on is still here. Asked outright rather than left
        // to be inferred: the id survives the model row (that column carries no foreign key, and
        // the entity says why), so every surface downstream would otherwise have to read a model
        // it cannot fetch, which is equally what a model still being processed, one this caller may
        // not open, and a failed request look like. A watch whose model was deleted is armed,
        // accepts entries and notes, and can place nobody — the one state on this read that looks
        // like ordinary operation and is not.
        var modelMissing = tracking?.SurveyModelId is { } armedOn
            && !await db.SurveyModels.AsNoTracking().AnyAsync(m => m.Id == armedOn, ct);

        var withheldAny = configHasVocabulary && !configOpen;
        var byCaver = events.GroupBy(e => e.CaverId).ToDictionary(g => g.Key, g => g.ToList());
        var participants = new List<TrackingParticipantDto>();
        foreach (var caverId in rosterCavers.OrderBy(c => c))
        {
            byCaver.TryGetValue(caverId, out var own);
            var last = own?.Count > 0 ? own[^1] : null;
            // A note or an exit says something happened, not where — the displayed position
            // stays the latest report that actually claimed a place, and so does its own time.
            // Those two travel together: a station carried beside the time of some later note
            // is a place presented as fresher than it is, which on a live watch is read as a
            // party that has moved.
            var lastPositioned = own?.LastOrDefault(TrackingWithholding.HasPosition);
            var lastTeamed = own?.LastOrDefault(e => e.TeamId is not null);
            var positionOpen = lastPositioned is null || TrackingWithholding.PositionOpen(lastPositioned, openCaves);
            if (!positionOpen) withheldAny = true;
            // Standing is Domain's answer, not a test on the latest report. Which kinds speak to
            // it, and which of those may overturn one already stated, is written down once there
            // together with the argument for it — this read and the published one now ask the
            // same question of the same rule, having each answered it themselves before.
            var standing = TripTrackingRules.StandingOf(own);
            participants.Add(new TrackingParticipantDto(
                caverId,
                lastTeamed?.TeamId,
                last?.Kind,
                last?.RecordedAt,
                // The position's own time rides the position's own withholding: when the
                // station is kept back the time that would date it is kept back with it.
                positionOpen ? lastPositioned?.RecordedAt : null,
                positionOpen ? lastPositioned?.ViewerStationName : null,
                positionOpen ? lastPositioned?.DepthEnteredM : null,
                // Which model that place was measured in, on the same branch as the place itself:
                // the name and the survey it is a name inside are one statement, and handing over
                // half of it is what lets a report from a replaced survey be drawn on the current
                // one. Withheld together for the same reason they are sent together — a model id
                // is station vocabulary of a cave, and this read never leaks one past the gate.
                positionOpen ? lastPositioned?.SurveyModelId : null,
                standing == TripStanding.Underground,
                standing == TripStanding.Out,
                labels.GetValueOrDefault(caverId)));
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(new TrackingStateDto(
            tracking?.State ?? TripTrackingState.Off,
            configOpen ? tracking?.SurveyModelId : null,
            // Said only to a caller who is being told which model it is: to anyone else the id
            // arrives null anyway, and "the survey that watch was on has been deleted" is a fact
            // about a cave whose vocabulary they were just refused.
            configOpen && modelMissing,
            configOpen ? tracking?.ReferenceStationName : null,
            configOpen ? tracking?.DepthFilter ?? [] : [],
            tracking?.ArmedAt,
            tracking?.ClosedAt,
            withheldAny,
            // Said on every read of the trip rather than only when a link is minted: the panel that
            // publishes has to word what the page will show before anybody presses the button.
            options.Value.PublishRealNames,
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
        var openCaves = await TrackingWithholding.OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        var dtos = result.Items.Select(e =>
        {
            var open = TrackingWithholding.PositionOpen(e, openCaves);
            return new TrackingEventDto(
                e.Id, e.CaverId, e.TeamId, e.Kind,
                open ? e.SurveyModelId : null,
                open ? e.ViewerStationName : null,
                open ? e.DepthEnteredM : null,
                e.Note, e.RecordedAt);
        }).ToList();

        return TypedResults.Ok(new PagedResult<TrackingEventDto>(dtos, result.Page, result.PageSize, result.TotalItems));
    }

    // ---- writes --------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingStateDto>, ProblemHttpResult>> PutConfigAsync(
        Guid tripLogId, TrackingConfigRequest request, HttpContext http, SilexGisDbContext db,
        IAccessService access, FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IOptions<TripTrackingOptions> options, CancellationToken ct)
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

        // Arming on a survey that is no longer here is the same refusal, deliberately worded the
        // same way. A closed watch keeps the id of the survey it was on; the survey can be deleted
        // while the watch is closed, and re-arming would then succeed on a reference to nothing —
        // a watch that says Armed, takes reports, and can place nobody, which is the whole failure
        // this work is about. One shape for both, so the answer discloses nothing about whether a
        // survey ever existed to a caller who is not being told the configuration anyway.
        //
        // Existence only, not the placing right: re-arming a watch has never required it, and
        // checking whether a row is there is not learning where a cave is. A caller who changes
        // the model goes through the gate above, which does take that right.
        if (target == TripTrackingState.Armed && !modelChanging && effectiveModelId is { } keeping
            && !await db.SurveyModels.AsNoTracking().AnyAsync(m => m.Id == keeping, ct))
        {
            return ApiProblems.Conflict("tracking.model_missing",
                "The survey model this watch was armed on is no longer here — choose another before arming.");
        }

        var referenceChanging = request.ReferenceStationName is not null && request.ReferenceStationName.Length > 0;
        // The datum, once a station has been named and found, in the spelling it is stored in:
        // the viewer's, like every other station name this feature writes down. Whichever of the
        // two was typed, this is what goes back into the box the administrator typed it into.
        string? referenceResolved = null;
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
            // Swapping the survey under an armed watch is allowed and stays allowed: a corrected or
            // re-imported survey arriving while a party is underground is a thing a coordinator has
            // to be able to follow, and an application that argued with them during a callout would
            // be answered by closing the watch — the worst of the available outcomes. What is
            // refused is the narrower act of moving an armed watch to a model of a *different*
            // cave, which would re-point a live watch at a place the party is not and would move
            // the anchor the config's own station vocabulary is protected by. The reasoning, and
            // why an unarmed watch may be pointed anywhere, live with the rule in Domain.
            // The state the watch will be in when this write lands, not the one it is in now.
            // Asked of `current`, the predicate refused the one act it says is free — ending a
            // watch and re-pointing it in a single write, which is what somebody does when the
            // trip turns out to have been somewhere else — and allowed the one it exists to
            // refuse: arming a closed watch straight onto another cave's survey, which moves the
            // anchor every position on the log is protected by.
            if (modelChanging
                && !TripTrackingRules.MayPointAtCave(target, tracking?.CaveFeatureId, snapshotCave.Value))
            {
                return ApiProblems.Conflict("tracking.model_other_cave",
                    "An armed watch can only be moved to another survey of the same cave — close it first.");
            }
            if (referenceChanging)
            {
                // Resolved rather than compared, for the reason a reported station is: somebody
                // reading a station off the model types the viewer's spelling of it, and for a
                // Therion model that is not guaranteed to be the string the survey rows hold.
                referenceResolved = await ResolveStationAsync(
                    db, usable.Value.Model, request.ReferenceStationName!, ct);
                if (referenceResolved is null) return ApiProblems.Conflict("tracking.reference_unknown",
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
            tracking.ReferenceStationName = request.ReferenceStationName.Length == 0 ? null : referenceResolved;
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

        return await GetAsync(tripLogId, http, db, access, protection, accessAccessor, options, ct);
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

    /// <summary>
    /// Names one participant for the trip's published page, or takes the name back off.
    /// </summary>
    /// <remarks>
    /// A row exists only where somebody typed a label, so clearing one deletes it rather than
    /// storing an empty string: the published read's fallback is the ordinary state, not a
    /// repair for a missing value, and an empty label that lived in the table would be a third
    /// state to reason about with nothing to say.
    /// </remarks>
    private static async Task<Results<Ok<TrackingParticipantLabelDto>, ProblemHttpResult>> SetParticipantLabelAsync(
        Guid tripLogId, Guid caverId, TrackingParticipantLabelRequest request, SilexGisDbContext db,
        IAccessService access, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        // Only somebody the trip names can be named: a label for anybody else would be a
        // person the published page invents.
        var onRoster = await db.TripLogParticipants.AsNoTracking()
            .AnyAsync(p => p.TripLogId == tripLogId && p.CaverId == caverId, ct);
        if (!onRoster)
        {
            return ApiProblems.BadRequest("tracking.caver_not_participant",
                "Only somebody on the trip's roster can be named on its published page.");
        }

        var row = await db.TripTrackingParticipants
            .FirstOrDefaultAsync(p => p.TripLogId == tripLogId && p.CaverId == caverId, ct);
        var label = request.Label?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            if (row is not null) db.TripTrackingParticipants.Remove(row);
            label = null;
        }
        else if (row is null)
        {
            db.TripTrackingParticipants.Add(new TripTrackingParticipant
            {
                TripLogId = tripLogId,
                CaverId = caverId,
                DisplayLabel = label,
            });
        }
        else
        {
            row.DisplayLabel = label;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TrackingParticipantLabelDto(caverId, label));
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
                // Matched by resolving the name against the model rather than by comparing strings:
                // the two sides of this application spell one station of a Therion model
                // differently, so a station pressed on the model is a real station under a name a
                // string comparison against the survey rows would call unknown.
                stationName = await ResolveStationAsync(db, usable.Value.Model, request.StationName!, ct);
                if (stationName is null) return ApiProblems.BadRequest("tracking.station_unknown",
                    "The station is not one of the chosen model's stations.");
            }
            else
            {
                var stations = await StationsOfAsync(db, usable.Value.Model, ct);
                var referenceZ = TrackingDepthResolver.ReferenceZ(stations, tracking.ReferenceStationName);
                if (referenceZ is null) return ApiProblems.Conflict("tracking.reference_unknown",
                    "The depth datum cannot be established for the chosen model.");
                var candidates = TrackingDepthResolver.Resolve(
                    stations, referenceZ.Value, (double)request.DepthM!.Value, tracking.DepthFilter, take: 1);
                if (candidates.Count == 0) return ApiProblems.Conflict("tracking.no_station_at_depth",
                    "No station matches that depth under the trip's depth filter.");
                // The winner under the name the viewer knows it by, which the resolver carried
                // alongside the rows' own. A depth-placed position is drawn on the model exactly
                // like a pressed one, and one stamped in the other spelling would be a marker that
                // silently never appears.
                stationName = candidates[0].ViewerName;
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
                ViewerStationName = stationName,
                DepthEnteredM = depthEntered,
                Note = request.Note,
                RecordedAt = recordedAt,
                RecordedByUserId = user!.UserId,
            });
        }
        db.TripPositionEvents.AddRange(created);
        await db.SaveChangesAsync(ct);

        IReadOnlyList<TrackingEventDto> dtos = [.. created.Select(e => new TrackingEventDto(
            e.Id, e.CaverId, e.TeamId, e.Kind, e.SurveyModelId, e.ViewerStationName, e.DepthEnteredM, e.Note, e.RecordedAt))];
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

        var stations = await StationsOfAsync(db, usable.Value.Model, ct);
        var referenceZ = TrackingDepthResolver.ReferenceZ(stations, tracking.ReferenceStationName);
        if (referenceZ is null) return ApiProblems.Conflict("tracking.reference_unknown",
            "The depth datum cannot be established for the chosen model.");

        var candidates = TrackingDepthResolver.Resolve(
            stations, referenceZ.Value, (double)request.DepthM!.Value, tracking.DepthFilter, request.Take ?? 5);
        // Named as the viewer names them, because this is a preview of what recording the depth
        // would write down, and it is also a list somebody picks a station out of to report it
        // outright. A preview that spells a station one way while the report it leads to stores it
        // another is a preview of something else. The depth filter beside it accepts a prefix of
        // either spelling, so a name copied out of this list still selects what it appears to.
        // The survey name is left as the file labels it: it is the survey's own description of
        // where the station sits, offered to tell two candidates apart, and not a path anything
        // resolves.
        IReadOnlyList<TrackingDepthCandidateDto> dtos = [.. candidates.Select(c =>
            new TrackingDepthCandidateDto(
                c.ViewerName,
                c.SurveyName,
                Math.Round(c.DepthM, 1),
                Math.Round(c.DeltaM, 1)))];
        return TypedResults.Ok(dtos);
    }
}
