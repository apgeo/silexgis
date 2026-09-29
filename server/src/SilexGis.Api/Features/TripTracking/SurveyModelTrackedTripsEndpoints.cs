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

        // Only the columns the withholding test and the aggregates read. Every row here carries
        // this model's id, so every row claims a place and every row is subject to the test.
        var reports = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.SurveyModelId == surveyModelId && tripIds.Contains(e.TripLogId))
            .Select(e => new TripPositionEvent
            {
                Id = e.Id,
                TripLogId = e.TripLogId,
                SurveyModelId = e.SurveyModelId,
                CaveFeatureId = e.CaveFeatureId,
                ViewerStationName = e.ViewerStationName,
                DepthEnteredM = e.DepthEnteredM,
                RecordedAt = e.RecordedAt,
            })
            .ToListAsync(ct);

        // One pass over the union of anchors — the reports' cave snapshots and the watches' — so
        // the access checks are per cave, never per trip.
        var caveIds = reports.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value)
            .Concat(trackings.Values.Where(t => t.CaveFeatureId is not null).Select(t => t.CaveFeatureId!.Value))
            .Distinct().ToList();
        var openCaves = await TrackingWithholding.OpenCaveIdsAsync(db, access, protection, ctx, caveIds, ct);

        var openReports = reports
            .Where(e => TrackingWithholding.PositionOpen(e, openCaves))
            .GroupBy(e => e.TripLogId)
            .ToDictionary(g => g.Key, g => (
                Count: g.Count(),
                First: g.Min(e => e.RecordedAt),
                Last: g.Max(e => e.RecordedAt)));

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

    private static DateTimeOffset ActivityOf(TrackedTripDto trip) =>
        trip.LastReportAt
        ?? trip.ArmedAt
        ?? (trip.TripDate is { } day
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue);
}
