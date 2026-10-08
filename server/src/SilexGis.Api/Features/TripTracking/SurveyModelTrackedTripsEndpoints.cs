// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Which trips were tracked on one survey model, asked from the model's side.
/// </summary>
/// <remarks>
/// <para>
/// A surface that draws several trips' parties on one model (a movie of the replay) needs the
/// trips that could appear on it. Asked trip by trip that would be a read per trip the caller can
/// see, each folding a whole log, and it would still miss trips reachable by no path the client
/// knows — a trip's roles need not name the cave its watch is on. So the model answers it, in a
/// fixed number of queries whatever the number of trips.
/// </para>
/// <para>
/// A trip is tracked on the model when its watch points at it now, or when any of its reports was
/// recorded against it — a watch re-pointed at a newer survey leaves its earlier reports on the
/// older one, and those are exactly what a replay on the older one draws.
/// </para>
/// <para>
/// What it discloses is decided by the rules the rest of the slice already runs, not restated
/// here: the model is gated as its own read gates it (404 for unknown and not visible alike), the
/// trips are those the caller may read, filtered in the query, and each report counts only when
/// the per-row withholding the event log applies lets the caller see its place. A trip whose only
/// tie to the model is withheld from the caller is not listed at all — listing it with nothing in
/// it would say that it was tracked there.
/// </para>
/// </remarks>
public static class SurveyModelTrackedTripsEndpoints
{
    public static RouteGroupBuilder MapSurveyModelTrackedTripsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/survey-models/{surveyModelId:guid}/tracked-trips", ListAsync)
            .WithTags("TripTracking")
            .WithSummary("The trips tracked on this survey model that the caller may read — watching it now, or with reports recorded against it — latest activity first; report counts follow the event log's per-report withholding.");
        return api;
    }

    private static async Task<Results<Ok<List<TrackedTripDto>>, ProblemHttpResult>> ListAsync(
        Guid surveyModelId, SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return ApiProblems.NotFound("survey_model.not_found");
        if (await TripTrackingEndpoints.UsableModelAsync(db, access, protection, ctx, surveyModelId, ct) is null)
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        // Candidates: readable trips with any tie to the model. The visibility filter is part of
        // this query, so a trip the caller may not read is never loaded, let alone counted. A watch
        // that was configured on the model and never armed tracked nothing there, and is no tie.
        var watching = db.TripTrackings
            .Where(t => t.SurveyModelId == surveyModelId && t.State != TripTrackingState.Off)
            .Select(t => t.TripLogId);
        var reporting = db.TripPositionEvents.Where(e => e.SurveyModelId == surveyModelId).Select(e => e.TripLogId);
        var trips = await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(t => watching.Contains(t.Id) || reporting.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.TripDate, t.TripDateEnd })
            .ToListAsync(ct);
        if (trips.Count == 0) return TypedResults.Ok(new List<TrackedTripDto>());

        var tripIds = trips.Select(t => t.Id).ToList();
        var trackings = await db.TripTrackings.AsNoTracking()
            .Where(t => tripIds.Contains(t.TripLogId))
            .ToDictionaryAsync(t => t.TripLogId, ct);

        // Counted where the rows are: one line per trip and cave snapshot, with how many reports
        // it stands for and the first and last of them, so what comes back grows with the number
        // of trips and of caves and never with the length of a log. The grouping is by cave
        // snapshot because that is the one column on which rows of the same trip can differ in
        // what a caller may be told — every row here names this model, so every row claims a
        // place, and whether a claimed place is open is decided by the cave it was claimed in.
        // The set is the ordinary filtered one: a report taken off the log and a deleted trip's
        // reports are not in it, here as everywhere.
        var reportGroups = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.SurveyModelId == surveyModelId && tripIds.Contains(e.TripLogId))
            .GroupBy(e => new { e.TripLogId, e.CaveFeatureId })
            .Select(g => new ReportGroup(
                g.Key.TripLogId,
                g.Key.CaveFeatureId,
                g.Count(),
                g.Min(e => e.RecordedAt),
                g.Max(e => e.RecordedAt)))
            .ToListAsync(ct);

        // One pass over the union of anchors — the reports' cave snapshots and the watches' — so
        // the access checks are per cave, never per trip.
        var caveIds = reportGroups.Where(g => g.CaveFeatureId is not null).Select(g => g.CaveFeatureId!.Value)
            .Concat(trackings.Values.Where(t => t.CaveFeatureId is not null).Select(t => t.CaveFeatureId!.Value))
            .Distinct().ToList();
        var openCaves = await TrackingWithholding.OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        // The withholding rule is asked, not restated: of one row standing for its whole group,
        // carrying the two columns every row of the group shares. A group whose cave snapshot is
        // gone therefore answers closed for everyone, as each of its rows would.
        var openReports = reportGroups
            .Where(g => TrackingWithholding.PositionOpen(
                new TripPositionEvent { SurveyModelId = surveyModelId, CaveFeatureId = g.CaveFeatureId },
                openCaves))
            .GroupBy(g => g.TripLogId)
            .ToDictionary(g => g.Key, g => (
                Count: g.Sum(x => x.Count),
                First: g.Min(x => x.First),
                Last: g.Max(x => x.Last)));

        var dtos = new List<TrackedTripDto>();
        foreach (var trip in trips)
        {
            trackings.TryGetValue(trip.Id, out var tracking);
            // Which model a watch is on is configuration vocabulary of the watch's cave, told to a
            // caller only under the rule the state read tells it by — the watch's own snapshot open.
            var watches = tracking is not null
                && tracking.SurveyModelId == surveyModelId
                && tracking.CaveFeatureId is { } watchCave
                && openCaves.Contains(watchCave);
            var hasReports = openReports.TryGetValue(trip.Id, out var counted);
            if (!(watches && tracking!.State != TripTrackingState.Off) && !hasReports) continue;

            dtos.Add(new TrackedTripDto(
                trip.Id,
                trip.Title,
                trip.TripDate,
                trip.TripDateEnd,
                tracking?.State ?? TripTrackingState.Off,
                tracking?.ArmedAt,
                tracking?.ClosedAt,
                watches,
                hasReports ? counted.Count : 0,
                hasReports ? counted.First : null,
                hasReports ? counted.Last : null));
        }

        // Latest activity first: the last report counted, else when the watch was armed, else the
        // trip's own date. Title, then id, break ties so the order is total.
        return TypedResults.Ok(dtos
            .OrderByDescending(ActivityOf)
            .ThenBy(t => t.Title, StringComparer.InvariantCulture)
            .ThenBy(t => t.TripLogId)
            .ToList());
    }

    /// <summary>
    /// The reports one trip made on the model inside one cave snapshot: how many, and the first
    /// and the last of them.
    /// </summary>
    private sealed record ReportGroup(
        Guid TripLogId, Guid? CaveFeatureId, int Count, DateTimeOffset First, DateTimeOffset Last);

    private static DateTimeOffset ActivityOf(TrackedTripDto trip) =>
        trip.LastReportAt
        ?? trip.ArmedAt
        ?? (trip.TripDate is { } day
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue);
}
