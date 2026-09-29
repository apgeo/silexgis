// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// What a cave's depths mean: the station each declared depth stands for, and the word people use
/// for the place.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cave declares this at all.</b> A watch turns a reported depth into a station by
/// measuring — the nearest station to the datum under the trip's filter — and measurement answers
/// the wrong question wherever the cave's shape disagrees with its arithmetic: parallel shafts
/// share a depth, a meander wanders up and down, and the station nearest a round number may be
/// somewhere nobody goes. A declaration is the cave's own answer, written once by somebody who
/// knows it, and read before any measuring is attempted.
/// </para>
/// <para>
/// <b>Read with the cave, written with the cave.</b> Reading takes Read on the cave and writing
/// takes Write, with no separate right of its own: a declaration is a fact about the cave's shape,
/// of exactly the same kind as its name or its length, and a reader who may be told the one may be
/// told the other. A cave nobody may read answers the same not-found an absent cave answers.
/// </para>
/// <para>
/// <b>Location protection does not reach it, and the reason is worth stating.</b> These rows carry
/// no coordinate: a depth is a distance below an entrance and a station name is a label inside a
/// survey, neither of which places a cave on the earth. What protects a survey's geometry is the
/// gate on the survey model itself, which is untouched here.
/// </para>
/// </remarks>
public static class CaveDepthPlaceEndpoints
{
    public static RouteGroupBuilder MapCaveDepthPlaceEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("CaveDepthPlaces");

        caves.MapGet("/{caveId:guid}/depth-places", ListAsync)
            .WithSummary("The depths this cave has declared the meaning of, shallowest first.");

        caves.MapPut("/{caveId:guid}/depth-places", WriteAsync)
            .WithValidation<CaveDepthPlaceWriteRequest>()
            .WithSummary(
                "Declares what one depth of this cave means, replacing the declaration for that "
                + "depth where there is one (Write on the cave).");

        caves.MapDelete("/{caveId:guid}/depth-places/{id:guid}", DeleteAsync)
            .WithSummary("Withdraws one declaration (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<CaveDepthPlaceDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Read, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var rows = await db.CaveDepthPlaces.AsNoTracking()
            .Where(x => x.CaveFeatureId == caveId)
            // Shallowest first: the order the cave is described in, and the order a chooser has to
            // offer, because somebody picking where a party is thinks downwards from the entrance.
            .OrderBy(x => x.DepthM)
            .ThenBy(x => x.Id)
            .Select(x => new CaveDepthPlaceDto(x.Id, x.DepthM, x.ViewerStationName, x.PlaceLabel))
            .ToListAsync(ct);

        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<CaveDepthPlaceDto>, ProblemHttpResult>> WriteAsync(
        Guid caveId, CaveDepthPlaceWriteRequest request, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Write, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // Keyed the way the table keys it — magnitude, one decimal — before it is looked up or
        // written. The request accepts the sign field notes use and any decimals a person types;
        // the column holds neither. Looking up the raw value would find nothing beside the row at
        // 120 for a write of 120.04 or −120, and the insert that followed would then be refused by
        // the unique index for the very row it failed to find.
        var depth = DeclaredDepthPlaces.Key(request.DepthM!.Value);
        var station = request.StationName!.Trim();
        var label = string.IsNullOrWhiteSpace(request.PlaceLabel) ? null : request.PlaceLabel.Trim();

        // The depth is the key, so a second write at the same depth is a correction rather than a
        // second answer. Done by reading first rather than by catching the unique index: the index
        // is there to keep the table honest if this is ever bypassed, and an upsert that relied on
        // it would report a conflict for the ordinary act of changing one's mind.
        var existing = await db.CaveDepthPlaces
            .FirstOrDefaultAsync(x => x.CaveFeatureId == caveId && x.DepthM == depth, ct);

        if (existing is null)
        {
            existing = new CaveDepthPlace
            {
                CaveFeatureId = caveId,
                DepthM = depth,
                ViewerStationName = station,
                PlaceLabel = label,
            };
            db.CaveDepthPlaces.Add(existing);
        }
        else
        {
            existing.ViewerStationName = station;
            existing.PlaceLabel = label;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsRaceOnDepth(e))
        {
            // With the key normalised above, the index can only fire when two first-time writes
            // at one depth cross between the read and this save. That is a conflict worth its
            // name rather than an unexplained failure: the caller reads the list and writes again.
            return ApiProblems.Conflict("cave_depth_place.concurrent_write",
                "That depth was declared by somebody else while this was being written. Read the list and write again.");
        }

        return TypedResults.Ok(
            new CaveDepthPlaceDto(existing.Id, existing.DepthM, existing.ViewerStationName, existing.PlaceLabel));
    }

    /// <summary>The unique index on (cave, depth) refused the write, and nothing else did.</summary>
    private static bool IsRaceOnDepth(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_cave_depth_places_cave_depth",
        };

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid caveId, Guid id, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Write, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var row = await db.CaveDepthPlaces
            .FirstOrDefaultAsync(x => x.Id == id && x.CaveFeatureId == caveId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound("cave_depth_place.not_found");
        }

        db.CaveDepthPlaces.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The cave, when this caller may act on it in the way asked. Missing and refused answer alike
    /// — one shape, so reaching for a cave nobody may see learns nothing about whether it exists.
    /// </summary>
    private static async Task<Feature?> CaveAsync(
        SilexGisDbContext db, IAccessService access, AccessContext? ctx, Guid caveId,
        AccessAction action, CancellationToken ct)
    {
        var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(ctx, action, cave, ct)).Allowed ? cave : null;
    }
}
