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
/// would otherwise need ~7 round-trips (four paged list calls fetching rows only to read
/// their totals, plus three recent-record calls) to paint above the fold.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>Recent-activity rows returned per entity kind, before the merged cap.</summary>
    private const int PerKindActivityLimit = 5;

    /// <summary>Rows in the merged feed. Kept small: this is a glanceable block, not a list page.</summary>
    private const int MergedActivityLimit = 10;

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

        var caves = db.Caves.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Cave);
        var features = db.SurfaceFeatures.AsNoTracking()
            .VisibleTo(user, db.ObjectAcls, AttachedEntityType.SurfaceFeature);
        var trips = db.TripLogs.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog);
        var geofiles = db.Geofiles.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Geofile);

        var counts = new DashboardCountsDto(
            await caves.CountAsync(ct),
            await features.CountAsync(ct),
            await trips.CountAsync(ct),
            await geofiles.CountAsync(ct));

        // Each kind is trimmed server-side before the merge, so the feed costs three small
        // indexed reads rather than sorting whole tables in memory.
        var recentCaves = await caves
            .OrderByDescending(c => c.UpdatedAt)
            .Take(PerKindActivityLimit)
            .Select(c => new DashboardActivityItemDto(DashboardActivityKind.Cave, c.Id, c.Name, c.UpdatedAt))
            .ToListAsync(ct);

        var recentFeatures = await features
            .OrderByDescending(f => f.UpdatedAt)
            .Take(PerKindActivityLimit)
            .Select(f => new DashboardActivityItemDto(
                DashboardActivityKind.SurfaceFeature, f.Id, f.Name, f.UpdatedAt))
            .ToListAsync(ct);

        var recentTrips = await trips
            .OrderByDescending(t => t.UpdatedAt)
            .Take(PerKindActivityLimit)
            .Select(t => new DashboardActivityItemDto(DashboardActivityKind.TripLog, t.Id, t.Title, t.UpdatedAt))
            .ToListAsync(ct);

        var recentActivity = recentCaves
            .Concat(recentFeatures)
            .Concat(recentTrips)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MergedActivityLimit)
            .ToList();

        return TypedResults.Ok(new DashboardSummaryDto(counts, recentActivity));
    }
}
