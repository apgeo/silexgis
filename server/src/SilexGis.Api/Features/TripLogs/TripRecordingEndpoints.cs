// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>Where a phone recording stands as a browser reader sees it.</summary>
public enum TripRecordingStanding
{
    /// <summary>No trip report has been made from it: it is open on the phone, or its conversion was left to a person.</summary>
    Waiting = 0,

    /// <summary>Made into a trip report, with nothing left to look at.</summary>
    Converted = 1,

    /// <summary>Made into a trip report that still has an unanswered flag.</summary>
    NeedsAttention = 2,
}

/// <summary>One recording on a cave, as a browser lists it.</summary>
/// <param name="TripLogId">
/// The report made from it, named only when the caller may read that report; a converted recording
/// whose report the caller may not read says it is converted and not where.
/// </param>
/// <param name="CanConvert">Whether the caller may make it into a report by hand: not yet one, and theirs to change.</param>
public sealed record TripRecordingListItemDto(
    Guid Id,
    string Title,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int PointCount,
    TripRecordingStanding Standing,
    Guid? TripLogId,
    bool CanConvert);

/// <summary>The phone recordings of caves, for the cave page.</summary>
public static class TripRecordingEndpoints
{
    public static RouteGroupBuilder MapTripRecordingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/trip-recordings").WithTags("TripLogs")
            .MapGet("/", ListAsync)
            .WithName("listTripRecordings")
            .WithSummary(
                "The phone recordings of one cave this caller may read, most recent first, each with "
                + "where it stands: waiting, made into a trip report, or made into one that needs "
                + "looking at. A cave the caller may not be told about, or may not place, answers an "
                + "empty page, exactly as the cave's trips do.");
        return api;
    }

    private static async Task<Results<Ok<PagedResult<TripRecordingListItemDto>>, UnauthorizedHttpResult>> ListAsync(
        Guid caveId,
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (pageNo, size) = Paging.Normalize(page, pageSize);

        // A recording names its cave, so listing a cave's recordings is naming the cave: the same
        // two gates the trips of a cave answer under, and an answer that does not say which gate
        // closed.
        if ((await TripCaveDisclosure.DisclosableCaveIdsAsync(db, protection, ctx, [caveId], ct)).Count == 0)
        {
            return TypedResults.Ok(new PagedResult<TripRecordingListItemDto>([], pageNo, size, 0));
        }

        var query = db.TripRecordings.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(r => r.CaveFeatureId == caveId)
            .OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((pageNo - 1) * size).Take(size).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var pointCounts = await db.TripRecordingPoints.AsNoTracking()
            .Where(p => ids.Contains(p.RecordingId))
            .GroupBy(p => p.RecordingId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var tripIds = rows.Where(r => r.ConvertedTripLogId != null).Select(r => r.ConvertedTripLogId!.Value).Distinct().ToList();
        var readableTrips = (await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct)).ToHashSet();
        // The open flag is a fact about the trip, so it is read only for trips the caller may read.
        var raised = (await db.TripAttentions.AsNoTracking()
            .Where(a => readableTrips.Contains(a.TripLogId) && a.ClearedAt == null)
            .Select(a => a.TripLogId)
            .ToListAsync(ct)).ToHashSet();

        var items = new List<TripRecordingListItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var converted = r.ConvertedTripLogId;
            var standing = converted is null
                ? TripRecordingStanding.Waiting
                : raised.Contains(converted.Value) ? TripRecordingStanding.NeedsAttention : TripRecordingStanding.Converted;
            var canConvert = converted is null
                && (await access.DecideAsync(ctx, AccessAction.Write, r, ct)).Allowed;
            items.Add(new TripRecordingListItemDto(
                r.Id,
                r.Title,
                r.StartedAt,
                r.EndedAt,
                pointCounts.GetValueOrDefault(r.Id),
                standing,
                converted is { } trip && readableTrips.Contains(trip) ? trip : null,
                canConvert));
        }

        return TypedResults.Ok(new PagedResult<TripRecordingListItemDto>(items, pageNo, size, total));
    }
}
