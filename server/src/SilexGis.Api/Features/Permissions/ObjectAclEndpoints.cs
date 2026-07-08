// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
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

        var entries = await LoadEntriesAsync(db, parsedType, id, ct);
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
                ? await db.Users.AnyAsync(u => u.Id == entry.SubjectId, ct)
                : await db.Teams.AnyAsync(t => t.Id == entry.SubjectId, ct);
            if (!exists)
            {
                return ApiProblems.BadRequest("acl.subject_unknown", "A grant subject does not exist.");
            }
        }

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

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadEntriesAsync(db, parsedType, id, ct));
    }

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
        SilexGisDbContext db, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        var rows = await db.ObjectAcls.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .ToListAsync(ct);

        var userIds = rows.Where(x => x.SubjectKind == AclSubjectKind.User).Select(x => x.SubjectId).ToList();
        var teamIds = rows.Where(x => x.SubjectKind == AclSubjectKind.Team).Select(x => x.SubjectId).ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName, ct);
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
