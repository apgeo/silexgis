// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
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
        // No existing children on create, so the reconcile helpers reduce to pure inserts.
        await ReconcileCaveLinksAsync(db, user, trip.Id, request.CaveIds, ct);
        var added = await ReconcileParticipantsAsync(db, trip.Id, TripParticipantKind.Participant, request.Participants, ct);
        added.AddRange(await ReconcileParticipantsAsync(db, trip.Id, TripParticipantKind.Proposer, request.Proposers ?? [], ct));
        await NotifyParticipantsAsync(db, user, trip, added, ct);
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

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        var problem = await ValidateReferencesAsync(db, permissions, user, request, ct);
        if (problem is not null)
        {
            return problem;
        }

        Apply(trip, request);
        await ReconcileCaveLinksAsync(db, user, trip.Id, request.CaveIds, ct);
        var added = await ReconcileParticipantsAsync(db, trip.Id, TripParticipantKind.Participant, request.Participants, ct);
        added.AddRange(await ReconcileParticipantsAsync(db, trip.Id, TripParticipantKind.Proposer, request.Proposers ?? [], ct));
        await NotifyParticipantsAsync(db, user, trip, added, ct);
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
        trip.Type = request.Type;
        trip.TripDate = request.TripDate;
        trip.TripDateEnd = request.TripDateEnd;
        trip.EntryTime = request.EntryTime;
        trip.ExitTime = request.ExitTime;
        trip.Description = request.Description;
        trip.Results = request.Results;
        trip.WeatherConditions = request.WeatherConditions;
        trip.LocationText = request.LocationText;
        trip.OrganizingClub = request.OrganizingClub;
        trip.Geom = request.Geom?.ToGeometryOrNull();
        trip.TeamId = request.TeamId;
        trip.Visibility = request.Visibility;
    }

    // Reconcile links with a diff (add/remove only what changed) rather than delete-all +
    // recreate-all. ExecuteDelete bypasses the audit interceptor and a full recreate logs a
    // "created" event for every unchanged child on every save, so the diff keeps the entity's
    // history timeline honest. Also preserves cave links the caller could not see: those were
    // redacted out of the DTO they edited, so a full-replace list would silently drop them.
    private static async Task ReconcileCaveLinksAsync(
        SilexGisDbContext db, UserContext user, Guid tripId, IReadOnlyList<Guid> requestedCaveIds, CancellationToken ct)
    {
        var existing = await db.TripLogCaves.Where(x => x.TripLogId == tripId).ToListAsync(ct);
        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(db, user, existing.Select(x => x.CaveId), ct);
        var desired = requestedCaveIds
            .Concat(existing.Where(x => redacted.Contains(x.CaveId)).Select(x => x.CaveId))
            .ToHashSet();

        foreach (var link in existing.Where(x => !desired.Contains(x.CaveId)))
        {
            db.TripLogCaves.Remove(link);
        }

        foreach (var caveId in desired.Where(id => existing.All(x => x.CaveId != id)))
        {
            db.TripLogCaves.Add(new TripLogCave { TripLogId = tripId, CaveId = caveId });
        }
    }

    // Reconciles one kind (attendees or proposers) independently; the same person may be both,
    // as two rows of different kind. Diffs like the cave links so unchanged rows don't churn.
    /// <summary>
    /// Brings a trip's participants of one kind in line with what was asked for, and reports the
    /// registered users genuinely newly listed — the only point at which that is knowable, since
    /// afterwards an added row is indistinguishable from one that was already there.
    /// </summary>
    private static async Task<List<Guid>> ReconcileParticipantsAsync(
        SilexGisDbContext db, Guid tripId, TripParticipantKind kind, IReadOnlyList<TripParticipantWrite> requested, CancellationToken ct)
    {
        var existing = await db.TripLogParticipants.Where(x => x.TripLogId == tripId && x.Kind == kind).ToListAsync(ct);
        var desired = requested
            .Select(p => (p.UserId, NameText: p.UserId is null ? p.NameText!.Trim() : null))
            .ToList();

        // Keep one existing row per matching desired slot (by user id / guest name); remove the
        // rest and add the desired entries that had no match — so unchanged participants neither
        // churn nor generate spurious history events.
        foreach (var participant in existing)
        {
            var idx = desired.FindIndex(d => d.UserId == participant.UserId && d.NameText == participant.NameText);
            if (idx >= 0)
            {
                desired.RemoveAt(idx);
            }
            else
            {
                db.TripLogParticipants.Remove(participant);
            }
        }

        foreach (var (userId, nameText) in desired)
        {
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                Kind = kind,
                UserId = userId,
                NameText = nameText,
            });
        }

        // Guests are free text with no account, so there is nobody to tell.
        return [.. desired.Where(d => d.UserId is not null).Select(d => d.UserId!.Value)];
    }

    /// <summary>
    /// Tells the people newly listed on a trip. Queued before the caller's save, so a notification
    /// exists only if the trip write it describes actually committed.
    /// </summary>
    /// <remarks>
    /// The message names the trip and its date and nothing else. A trip's linked caves are
    /// deliberately absent: a cave link beside a trip's own location is exactly the disclosure the
    /// cave-link redaction rules exist to prevent, and a notification is no less an outbound copy
    /// of that data than a DTO is.
    /// </remarks>
    private static async Task NotifyParticipantsAsync(
        SilexGisDbContext db,
        UserContext user,
        TripLog trip,
        IEnumerable<Guid> addedUserIds,
        CancellationToken ct)
    {
        // The same person can be listed as both participant and proposer in one request.
        var recipients = addedUserIds.Distinct().Where(id => id != user.UserId).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var actorName = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty;

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.TripParticipation,
                MessageTemplateCatalog.NotifyTripParticipation,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                });
        }
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

        var userIds = request.Participants.Concat(request.Proposers ?? [])
            .Where(x => x.UserId is not null).Select(x => x.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var found = await ProfileDirectory.ExistingIdsAsync(db, userIds, ct);
            if (found.Count != userIds.Count)
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

        var participantRows = await db.TripLogParticipants.AsNoTracking()
            .Where(x => tripIds.Contains(x.TripLogId))
            .Select(x => new { x.TripLogId, x.Kind, x.UserId, x.NameText })
            .ToListAsync(ct);

        // Resolved rather than joined: the label a participant may be shown under is a rule with
        // one home, and it is never their address. A participant recorded as free text keeps it.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, participantRows.Where(x => x.UserId is not null).Select(x => x.UserId!.Value), ct);

        var participants = participantRows
            .Select(x => new
            {
                x.TripLogId,
                x.Kind,
                x.UserId,
                x.NameText,
                DisplayName = x.UserId is { } participant ? labels.GetValueOrDefault(participant) : null,
            })
            .ToList();

        // Exact trip geometry + protected-cave link would disclose the cave; hide those links.
        var redacted = await CaveLinkRedaction.RedactedCaveIdsAsync(
            db, user, caveLinks.Select(x => x.CaveId), ct);

        return [.. trips.Select(trip => new TripLogDto(
            trip.Id,
            trip.Title,
            trip.Type,
            trip.TripDate,
            trip.TripDateEnd,
            trip.EntryTime,
            trip.ExitTime,
            trip.Description,
            trip.Results,
            trip.WeatherConditions,
            trip.LocationText,
            trip.OrganizingClub,
            trip.Geom is null ? null : GeoJsonGeometry.From(trip.Geom),
            [.. caveLinks.Where(x => x.TripLogId == trip.Id && !redacted.Contains(x.CaveId)).Select(x => x.CaveId)],
            [.. participants.Where(x => x.TripLogId == trip.Id && x.Kind == TripParticipantKind.Participant)
                .Select(x => new TripParticipantDto(x.UserId, x.NameText, x.DisplayName ?? x.NameText))],
            [.. participants.Where(x => x.TripLogId == trip.Id && x.Kind == TripParticipantKind.Proposer)
                .Select(x => new TripParticipantDto(x.UserId, x.NameText, x.DisplayName ?? x.NameText))],
            trip.OwnerUserId,
            trip.TeamId,
            trip.Visibility,
            trip.CreatedAt,
            trip.UpdatedAt))];
    }
}
