// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

public sealed record AclEntryDto(
    AclSubjectKind SubjectKind,
    Guid SubjectId,
    string? SubjectName,
    ObjectPermission Permissions);

public sealed record AclEntryWrite(AclSubjectKind SubjectKind, Guid SubjectId, ObjectPermission Permissions);

public sealed record AclReplaceRequest(IReadOnlyList<AclEntryWrite> Entries);

public sealed class AclReplaceRequestValidator : AbstractValidator<AclReplaceRequest>
{
    public AclReplaceRequestValidator()
    {
        RuleFor(x => x.Entries).NotNull();
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(x => x.SubjectId).NotEmpty();
            entry.RuleFor(x => x.SubjectKind).IsInEnum();
            entry.RuleFor(x => x.Permissions)
                .Must(p => p != ObjectPermission.None)
                .WithMessage("A grant needs at least one permission.");
        });
        RuleFor(x => x.Entries)
            .Must(e => e.Select(x => (x.SubjectKind, x.SubjectId)).Distinct().Count() == e.Count)
            .WithMessage("Duplicate subjects in the grant list.");
    }
}

/// <summary>
/// Per-object ACL management (the explicit-grant layer) and the caller's effective
/// permissions. Reading/replacing grants requires ManagePermissions on the object.
/// </summary>
public static class ObjectAclEndpoints
{
    public static RouteGroupBuilder MapObjectAclEndpoints(this RouteGroupBuilder api)
    {
        var objects = api.MapGroup("/objects/{entityType}/{id:guid}").WithTags("Permissions");

        objects.MapGet("/acl", GetAclAsync)
            .WithSummary("ACL entries of one object (ManagePermissions).");
        objects.MapPut("/acl", ReplaceAclAsync).WithValidation<AclReplaceRequest>()
            .WithSummary("Replaces the object's ACL entries (ManagePermissions).");
        objects.MapGet("/effective-permissions", EffectiveAsync)
            .WithSummary("The caller's own effective permissions on the object.");

        return api;
    }

    private static async Task<Results<Ok<List<AclEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> GetAclAsync(
        string entityType,
        Guid id,
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

        var (entity, parsedType, problem) = await ResolveAsync(db, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!await permissions.CanAsync(user, entity!, ObjectPermission.ManagePermissions, ct))
        {
            // Managing implies knowing the object exists; non-managers get non-disclosure.
            return await permissions.CanAsync(user, entity!, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden("acl.forbidden")
                : ApiProblems.NotFound("acl.entity_not_found");
        }

        var entries = await LoadEntriesAsync(db, user, parsedType, id, ct);
        return TypedResults.Ok(entries);
    }

    private static async Task<Results<Ok<List<AclEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ReplaceAclAsync(
        string entityType,
        Guid id,
        AclReplaceRequest request,
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

        var (entity, parsedType, problem) = await ResolveAsync(db, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!await permissions.CanAsync(user, entity!, ObjectPermission.ManagePermissions, ct))
        {
            return await permissions.CanAsync(user, entity!, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden("acl.forbidden")
                : ApiProblems.NotFound("acl.entity_not_found");
        }

        // Subjects must exist (users or teams respectively).
        foreach (var entry in request.Entries)
        {
            var exists = entry.SubjectKind == AclSubjectKind.User
                ? (await ProfileDirectory.ExistingIdsAsync(db, [entry.SubjectId], ct)).Count > 0
                : await db.Teams.AnyAsync(t => t.Id == entry.SubjectId, ct);
            if (!exists)
            {
                return ApiProblems.BadRequest("acl.subject_unknown", "A grant subject does not exist.");
            }
        }

        // Who already had a grant, read before the replace wipes it: a full-replace save that
        // leaves an existing grant untouched is not news, and mailing everyone on every ACL edit
        // would train people to ignore the message that matters.
        var alreadyGranted = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.EntityType == parsedType && a.EntityId == id)
            .Select(a => new { a.SubjectKind, a.SubjectId })
            .ToListAsync(ct);

        // Full replace: the ACL is small per object; diffing buys nothing.
        await db.ObjectAcls
            .Where(a => a.EntityType == parsedType && a.EntityId == id)
            .ExecuteDeleteAsync(ct);
        foreach (var entry in request.Entries)
        {
            db.ObjectAcls.Add(new ObjectAcl
            {
                EntityType = parsedType,
                EntityId = id,
                SubjectKind = entry.SubjectKind,
                SubjectId = entry.SubjectId,
                Permissions = entry.Permissions,
                GrantedBy = user.UserId,
            });
        }

        var newlyGranted = request.Entries
            .Where(e => !alreadyGranted.Any(a => a.SubjectKind == e.SubjectKind && a.SubjectId == e.SubjectId))
            .ToList();
        await NotifyGranteesAsync(db, user, parsedType, id, newlyGranted, ct);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadEntriesAsync(db, user, parsedType, id, ct));
    }

    /// <summary>
    /// Tells the people who just gained access to something. A grant to a team reaches each of its
    /// members, since a team grant is how most people actually receive access.
    /// </summary>
    /// <remarks>
    /// Naming the record is safe here and nowhere near the location rules: a grant confers Read on
    /// it, names are shown in every list to anyone with Read, and what location protection hides is
    /// coordinates and address fields — never the name. The message carries the name and a link,
    /// and no geometry of any kind.
    /// </remarks>
    private static async Task NotifyGranteesAsync(
        SilexGisDbContext db,
        UserContext user,
        AttachedEntityType entityType,
        Guid entityId,
        IReadOnlyList<AclEntryWrite> granted,
        CancellationToken ct)
    {
        if (granted.Count == 0)
        {
            return;
        }

        var recipients = new HashSet<Guid>(granted
            .Where(e => e.SubjectKind == AclSubjectKind.User)
            .Select(e => e.SubjectId));

        var teamIds = granted.Where(e => e.SubjectKind == AclSubjectKind.Team).Select(e => e.SubjectId).ToList();
        if (teamIds.Count > 0)
        {
            var members = await db.TeamMembers.AsNoTracking()
                .Where(m => teamIds.Contains(m.TeamId))
                .Select(m => m.UserId)
                .ToListAsync(ct);
            recipients.UnionWith(members);
        }

        // Granting yourself access, or being in a team you just granted, is not news.
        recipients.Remove(user.UserId);
        if (recipients.Count == 0)
        {
            return;
        }

        var objectName = await NameOfAsync(db, entityType, entityId, ct);
        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var actorName = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty;

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.PermissionGranted,
                MessageTemplateCatalog.NotifyPermissionGranted,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorName,
                    ["objectName"] = objectName,
                    ["url"] = LinkTo(entityType, entityId),
                });
        }
    }

    /// <summary>
    /// What the record is called. <see cref="IProtectedEntity"/> carries no name, and the field
    /// differs per type, so the projection is per type rather than shared.
    /// </summary>
    private static async Task<string> NameOfAsync(
        SilexGisDbContext db, AttachedEntityType entityType, Guid id, CancellationToken ct) =>
        entityType switch
        {
            AttachedEntityType.Cave =>
                await db.Caves.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            AttachedEntityType.SurfaceFeature =>
                await db.SurfaceFeatures.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            AttachedEntityType.Geofile =>
                await db.Geofiles.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            AttachedEntityType.TripLog =>
                await db.TripLogs.Where(x => x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            AttachedEntityType.GeoreferencedMap =>
                await db.GeoreferencedMaps.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            _ => null,
        } ?? string.Empty;

    private static string LinkTo(AttachedEntityType entityType, Guid id) => entityType switch
    {
        AttachedEntityType.Cave => $"/caves/{id}",
        AttachedEntityType.TripLog => $"/trip-logs/{id}",
        AttachedEntityType.SurfaceFeature => "/features",
        AttachedEntityType.Geofile or AttachedEntityType.GeoreferencedMap => "/geodata",
        _ => "/",
    };

    private static async Task<Results<Ok<ObjectPermission>, UnauthorizedHttpResult, ProblemHttpResult>> EffectiveAsync(
        string entityType,
        Guid id,
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

        var (entity, _, problem) = await ResolveAsync(db, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var effective = await permissions.EffectiveAsync(user, entity!, ct);
        if (!effective.HasFlag(ObjectPermission.Read))
        {
            return ApiProblems.NotFound("acl.entity_not_found");
        }

        return TypedResults.Ok(effective);
    }

    /// <summary>Loads the protected entity behind a polymorphic (type, id) reference.</summary>
    private static async Task<(IProtectedEntity? Entity, AttachedEntityType Type, ProblemHttpResult? Problem)> ResolveAsync(
        SilexGisDbContext db, string entityType, Guid id, CancellationToken ct)
    {
        if (!Enum.TryParse<AttachedEntityType>(entityType, ignoreCase: true, out var parsedType))
        {
            return (null, default, ApiProblems.BadRequest("acl.entity_type_unknown", $"Unknown entity type '{entityType}'."));
        }

        IProtectedEntity? entity = parsedType switch
        {
            AttachedEntityType.Cave => await db.Caves.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.SurfaceFeature => await db.SurfaceFeatures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.Geofile => await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.TripLog => await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.GeoreferencedMap => await db.GeoreferencedMaps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            _ => null, // entrances inherit cave ACL; teams are not ACL targets
        };

        return entity is null
            ? (null, parsedType, ApiProblems.NotFound("acl.entity_not_found"))
            : (entity, parsedType, null);
    }

    private static async Task<List<AclEntryDto>> LoadEntriesAsync(
        SilexGisDbContext db, UserContext user, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        var rows = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .ToListAsync(ct);

        var userIds = rows.Where(x => x.SubjectKind == AclSubjectKind.User).Select(x => x.SubjectId).ToList();
        var teamIds = rows.Where(x => x.SubjectKind == AclSubjectKind.Team).Select(x => x.SubjectId).ToList();
        // Resolved rather than projected: the label a grantee may be shown under is a rule with
        // one home, and it is never their address.
        var userNames = await ProfileDirectory.ResolveLabelsAsync(db, user, userIds, ct);
        var teamNames = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return [.. rows.Select(a => new AclEntryDto(
            a.SubjectKind,
            a.SubjectId,
            a.SubjectKind == AclSubjectKind.User
                ? userNames.GetValueOrDefault(a.SubjectId)
                : teamNames.GetValueOrDefault(a.SubjectId),
            a.Permissions))];
    }
}
