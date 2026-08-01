// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Dashboard;

/// <summary>
/// Read-only aggregate behind the dashboard page: registry counts + a recent-activity feed,
/// every part filtered to what the caller may see. Exists as one endpoint because the page
/// would otherwise need several round-trips (paged list calls fetching rows only to read
/// their totals, plus recent-record calls) to paint above the fold.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Rows in the merged recent-activity feed. Kept small: this is a glanceable block, not a
    /// list page. Each source is also read up to this same limit before the merge, and that is
    /// a requirement rather than a coincidence: a record in the newest N overall is necessarily
    /// in its own source's newest N, so reading fewer per source would let a burst of activity
    /// in one of them push out rows that are newer than the ones displayed.
    /// </summary>
    private const int ActivityLimit = 10;

    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/summary", GetSummaryAsync)
            .WithTags("Dashboard")
            .WithSummary("Visibility-filtered registry counts and recent activity for the dashboard.");
        return api;
    }

    private static async Task<Results<Ok<DashboardSummaryDto>, UnauthorizedHttpResult>> GetSummaryAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var features = db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls);
        var trips = db.TripLogs.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog);
        var geofiles = db.Geofiles.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Geofile);

        // Every feature kind lives in one table, so the per-kind figures are one grouped scan
        // rather than a count query per kind.
        var featureCounts = await features
            .GroupBy(f => f.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count, ct);

        var counts = new DashboardCountsDto(
            featureCounts.GetValueOrDefault(FeatureKind.Cave),
            featureCounts.GetValueOrDefault(FeatureKind.Generic),
            await trips.CountAsync(ct),
            await geofiles.CountAsync(ct));

        // Each source is trimmed server-side before the merge, so the feed reads two short
        // pages rather than sorting whole tables in memory.
        var recentFeatures = await features
            .OrderByDescending(f => f.UpdatedAt)
            .Take(ActivityLimit)
            .Select(f => new { f.Kind, f.Id, f.Name, f.UpdatedAt })
            .ToListAsync(ct);

        var recentTrips = await trips
            .OrderByDescending(t => t.UpdatedAt)
            .Take(ActivityLimit)
            .Select(t => new DashboardActivityItemDto(DashboardActivityKind.TripLog, t.Id, t.Title, t.UpdatedAt))
            .ToListAsync(ct);

        var recentActivity = recentFeatures
            .Select(f => new DashboardActivityItemDto(
                DashboardActivityKinds.Of(f.Kind), f.Id, f.Name, f.UpdatedAt))
            .Concat(recentTrips)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(ActivityLimit)
            .ToList();

        return TypedResults.Ok(new DashboardSummaryDto(counts, recentActivity));
    }
}
