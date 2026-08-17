// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// Everything one camp draws on a map, decided here rather than assembled by whoever is drawing it.
///
/// Three things go on the same canvas and they are governed differently, which is the whole reason
/// this is one endpoint instead of three requests a page stitches together:
///
/// <list type="bullet">
/// <item>the camp's working area — the shape somebody drew on the plan. It is the camp's own
/// property, governed by the camp's own visibility, and it is no more a position than a permit
/// boundary is;</item>
/// <item>the member trips' sketches, out of the trips <em>this caller</em> may read, so two people
/// looking at the same camp see different sketches and both are right. A sketch is served exactly
/// wherever its trip is readable — that is the trip's own long-standing bargain, and the page says
/// so in words;</item>
/// <item>the entrances of the caves those trips name — the only coordinates here that anybody is
/// protected from.</item>
/// </list>
///
/// An entrance whose exact position the caller may not see is left off entirely rather than drawn
/// snapped to the protection grid. Snapping is what a map does when the snapped point stands alone;
/// here it would be drawn beside the exact sketch of the very trip that went there, and a coarse
/// point next to an exact outline of the same visit is not coarse at all. It is the same decision,
/// for the same reason, that takes a protected cave out of a trip's own list of the caves it names.
/// </summary>
public static class ExpeditionMapEndpoints
{
    /// <summary>
    /// Safety cap on entrance points in one answer. Ordered by id before the cap bites, so a camp
    /// over the cap draws the same points every time it is opened rather than an arbitrary subset
    /// that changes under the reader.
    /// </summary>
    private const int MaxPoints = 2000;

    public static RouteGroupBuilder MapExpeditionMapEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/expeditions").WithTags("Expeditions")
            .MapGet("/{id:guid}/map", GetAsync)
            .WithSummary(
                "What one camp draws on a map, as GeoJSON: its working area, the sketches of the "
                + "member trips this caller may read, and the entrances of the caves those trips "
                + "name whose exact position this caller may see.");

        return api;
    }

    private static async Task<Results<Ok<FeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var camp = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (camp is null || !(await access.DecideAsync(ctx, AccessAction.Read, camp, ct)).Allowed)
        {
            // The same words every other door into a camp uses: an address that answered
            // differently for a camp that is not there and one that is not yours is an address
            // anybody can probe for the existence of a camp they are not admitted to.
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        var features = new List<GeoFeature>();
        if (camp.Geom is not null)
        {
            features.Add(GeoFeature.Of(camp.Geom, new Dictionary<string, object?>
            {
                ["kind"] = "area",
                ["id"] = camp.Id,
                ["name"] = camp.Name,
            }));
        }

        // The member trips out of the trips this caller may read. The camp's readability has
        // already been decided above, so this narrowing discloses nothing about the camp — it is
        // the same per-caller filtering the camp's trip listing applies, and it has to be, or the
        // map would show trips the list does not.
        var memberTripIds = db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == id)
            .Select(m => m.TripLogId);
        var trips = await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(x => memberTripIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.Geom })
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        foreach (var trip in trips.Where(x => x.Geom is not null))
        {
            features.Add(GeoFeature.Of(trip.Geom!, new Dictionary<string, object?>
            {
                ["kind"] = "trip",
                ["id"] = trip.Id,
                ["name"] = trip.Title,
            }));
        }

        features.AddRange(await EntrancePointsAsync(db, protection, ctx, [.. trips.Select(x => x.Id)], ct));
        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    /// <summary>
    /// The entrances of the caves the given trips name, out of the ones this caller may both read
    /// and place exactly. Role-agnostic over the trip roles: the question is which caves the camp
    /// went to, not what was done in them.
    /// </summary>
    private static async Task<List<GeoFeature>> EntrancePointsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var namedIds = await TripRoleLinks
            .FeatureIdsNamedIn(db, db.TripLogs.AsNoTracking().Where(t => tripIds.Contains(t.Id)).Select(t => t.Id))
            .Distinct()
            .ToListAsync(ct);
        if (namedIds.Count == 0)
        {
            return [];
        }

        // A cave's own row carries no position; its entrances do. The cave goes through the
        // visibility walk, and its entrances then follow from it: an entrance has no access
        // control of its own, and grants are keyed to the row they were made on, so a caller
        // who reads a private cave through a grant on that cave holds none on its entrances.
        // Re-filtering the entrance rows here would drop them and show a reader less of the camp
        // than the cave's own page shows them. Nothing is disclosed by it: the exact-view gate
        // below decides every coordinate, and an entrance the caller may not place exactly is
        // left out entirely rather than blurred.
        var caves = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => namedIds.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        if (caves.Count == 0)
        {
            return [];
        }

        var caveIds = caves.Keys.ToList();
        var rows = await db.Features.AsNoTracking()
            .Where(f => f.Kind == FeatureKind.CaveEntrance
                && f.Geom != null
                && caveIds.Contains(f.Entrance!.CaveFeatureId))
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.Geom,
                CaveId = f.Entrance!.CaveFeatureId,
                f.Entrance!.IsMain,
            })
            .OrderBy(f => f.Id)
            .Take(MaxPoints)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var exactViewIds = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(r => r.Id)], ct);

        return
        [
            .. rows
                .Where(row => exactViewIds.Contains(row.Id) && row.Geom is Point)
                .Select(row => GeoFeature.Of(row.Geom!, new Dictionary<string, object?>
                {
                    ["kind"] = "entrance",
                    ["id"] = row.Id,
                    ["caveId"] = row.CaveId,
                    ["name"] = row.Name ?? caves.GetValueOrDefault(row.CaveId),
                    ["caveName"] = caves.GetValueOrDefault(row.CaveId),
                    ["isMain"] = row.IsMain,
                })),
        ];
    }
}
