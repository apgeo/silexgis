// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

public static class TripLogEndpoints
{
    public static RouteGroupBuilder MapTripLogEndpoints(this RouteGroupBuilder api)
    {
        var trips = api.MapGroup("/trip-logs").WithTags("TripLogs");

        trips.MapGet("/", ListAsync)
            .WithSummary("Paged trip logs with date/cave filters; visibility-filtered.");
        trips.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single trip log with caves and participants.");
        trips.MapPost("/", CreateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary("Creates a trip log (Editor role and above); the caller becomes owner.");
        trips.MapPut("/{id:guid}", UpdateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary("Full update incl. caves/participants replacement (Write permission).");
        trips.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a trip log with its links and attachments.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<TripLogDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        Guid? caveId,
        string? search,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.TripLogs.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.TripLog);

        if (from is not null)
        {
            query = query.Where(x => x.TripDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.TripDate <= to);
        }

        if (caveId is not null)
        {
            // Filtering trips by a location-protected cave would place the cave through
            // the trips' geometries — behave as if nothing is linked.
            if (await CaveLinkRedaction.ShouldRedactAsync(db, user, caveId, ct))
            {
                var (emptyPage, emptySize) = Paging.Normalize(page, pageSize);
                return TypedResults.Ok(new PagedResult<TripLogDto>([], emptyPage, emptySize, 0));
            }

            query = query.Where(x => db.TripLogCaves.Any(l => l.TripLogId == x.Id && l.CaveId == caveId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern)));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.TripDate).ThenByDescending(x => x.CreatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await MapWithChildrenAsync(db, user, rows, ct);
        return TypedResults.Ok(new PagedResult<TripLogDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null || !await permissions.CanAsync(user, trip, ObjectPermission.Read, ct))
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        var items = await MapWithChildrenAsync(db, user!, [trip], ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TripLogWriteRequest request,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.CanCreateContent)
        {
            return ApiProblems.Forbidden("trip_log.create_requires_editor");
        }

        var problem = await ValidateReferencesAsync(db, permissions, user, request, ct);
        if (problem is not null)
        {
            return problem;
        }

        var trip = new TripLog { Title = request.Title, OwnerUserId = user.UserId };
        Apply(trip, request);
        db.TripLogs.Add(trip);
        await ReplaceChildrenAsync(db, trip.Id, request, ct);
        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, user, [trip], ct);
        return TypedResults.Created($"/api/v1/trip-logs/{trip.Id}", items[0]);
    }

    private static async Task<Results<Ok<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, trip, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, trip, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct) is { } stale)
        {
            return stale;
        }

        var problem = await ValidateReferencesAsync(db, permissions, user, request, ct);
        if (problem is not null)
        {
            return problem;
        }

        Apply(trip, request);
        await db.TripLogCaves.Where(x => x.TripLogId == trip.Id).ExecuteDeleteAsync(ct);
        await db.TripLogParticipants.Where(x => x.TripLogId == trip.Id).ExecuteDeleteAsync(ct);
        await ReplaceChildrenAsync(db, trip.Id, request, ct);
        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, user, [trip], ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, trip, ObjectPermission.Delete, ct))
        {
            return await permissions.CanAsync(user, trip, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct) is { } stale)
        {
            return stale;
        }

        // Cave/participant links cascade; polymorphic rows are cleaned here.
        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == trip.Id)
            .ExecuteDeleteAsync(ct);
        await db.Taggings
            .Where(x => x.EntityType == AttachedEntityType.TripLog && x.EntityId == trip.Id)
            .ExecuteDeleteAsync(ct);
        db.TripLogs.Remove(trip);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---- shared pieces ----

    private static void Apply(TripLog trip, TripLogWriteRequest request)
    {
        trip.Title = request.Title;
        trip.TripDate = request.TripDate;
        trip.TripDateEnd = request.TripDateEnd;
        trip.Description = request.Description;
        trip.LocationText = request.LocationText;
        trip.Geom = request.Geom?.ToGeometryOrNull();
        trip.TeamId = request.TeamId;
        trip.Visibility = request.Visibility;
    }

    private static async Task ReplaceChildrenAsync(
        SilexGisDbContext db, Guid tripId, TripLogWriteRequest request, CancellationToken ct)
    {
        foreach (var caveId in request.CaveIds.Distinct())
        {
            db.TripLogCaves.Add(new TripLogCave { TripLogId = tripId, CaveId = caveId });
        }

        foreach (var participant in request.Participants)
        {
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                UserId = participant.UserId,
                NameText = participant.UserId is null ? participant.NameText!.Trim() : null,
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>Geometry validity, cave visibility, participant-user existence.</summary>
    private static async Task<ProblemHttpResult?> ValidateReferencesAsync(
        SilexGisDbContext db, IPermissionService permissions, UserContext user, TripLogWriteRequest request, CancellationToken ct)
    {
        if (request.Geom is not null && request.Geom.ToGeometryOrNull() is null)
        {
            return ApiProblems.BadRequest("trip_log.geometry_invalid", "Geometry is malformed or invalid.");
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("trip_log.team_membership_required");
        }

        foreach (var caveId in request.CaveIds.Distinct())
        {
            var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caveId, ct);
            if (cave is null || !await permissions.CanAsync(user, cave, ObjectPermission.Read, ct))
            {
                return ApiProblems.BadRequest("trip_log.cave_not_found", "A linked cave does not exist.");
            }
        }

        var userIds = request.Participants.Where(x => x.UserId is not null).Select(x => x.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var found = await db.Users.CountAsync(u => userIds.Contains(u.Id), ct);
            if (found != userIds.Count)
            {
                return ApiProblems.BadRequest("trip_log.participant_unknown", "A participant user does not exist.");
            }
        }

        return null;
    }

    /// <summary>Batch-loads caves/participants and applies cave-link redaction.</summary>
    private static async Task<List<TripLogDto>> MapWithChildrenAsync(
        SilexGisDbContext db, UserContext user, IReadOnlyList<TripLog> trips, CancellationToken ct)
    {
        var tripIds = trips.Select(x => x.Id).ToList();

        var caveLinks = await db.TripLogCaves.AsNoTracking()
            .Where(x => tripIds.Contains(x.TripLogId))
            .ToListAsync(ct);

        var participants = await db.TripLogParticipants.AsNoTracking()
            .Where(x => tripIds.Contains(x.TripLogId))
            .GroupJoin(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, users) => new { p, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new
            {
                x.p.TripLogId,
                x.p.UserId,
                x.p.NameText,
                DisplayName = u == null ? null : (u.DisplayName ?? u.UserName),
            })
            .ToListAsync(ct);

        // Exact trip geometry + protected-cave link would disclose the cave; hide those links.
        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(
            db, user, caveLinks.Select(x => x.CaveId), ct);

        return [.. trips.Select(trip => new TripLogDto(
            trip.Id,
            trip.Title,
            trip.TripDate,
            trip.TripDateEnd,
            trip.Description,
            trip.LocationText,
            trip.Geom is null ? null : GeoJsonGeometry.From(trip.Geom),
            [.. caveLinks.Where(x => x.TripLogId == trip.Id && !redacted.Contains(x.CaveId)).Select(x => x.CaveId)],
            [.. participants.Where(x => x.TripLogId == trip.Id)
                .Select(x => new TripParticipantDto(x.UserId, x.NameText, x.DisplayName ?? x.NameText))],
            trip.OwnerUserId,
            trip.TeamId,
            trip.Visibility,
            trip.CreatedAt,
            trip.UpdatedAt))];
    }
}
