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
///
/// A grant targets EITHER a feature — any physical feature, of any kind, addressed by the
/// single route name "feature" and stored against the real feature FK — OR one of the
/// non-feature entities that carry their own access control, stored against the
/// polymorphic (type, id) pair. The two shapes are mutually exclusive per row.
/// </summary>
public static class ObjectAclEndpoints
{
    /// <summary>
    /// The one route name of the whole feature world. Caves, entrances, centerlines and
    /// generic features share it because a grant on a feature is keyed by its id alone —
    /// the kind adds nothing the route needs and would only invite callers to guess wrong.
    /// </summary>
    private const string FeatureTargetName = "feature";

    public static RouteGroupBuilder MapObjectAclEndpoints(this RouteGroupBuilder api)
    {
        var objects = api.MapGroup("/objects/{entityType}/{id:guid}").WithTags("Permissions");

        objects.MapGet("/acl", GetAclAsync)
            .WithSummary("ACL entries of one object (ManagePermissions).")
            .WithDescription(TargetVocabulary);
        objects.MapPut("/acl", ReplaceAclAsync).WithValidation<AclReplaceRequest>()
            .WithSummary("Replaces the object's ACL entries (ManagePermissions).")
            .WithDescription(TargetVocabulary);
        objects.MapGet("/effective-permissions", EffectiveAsync)
            .WithSummary("The caller's own effective permissions on the object.")
            .WithDescription(TargetVocabulary);

        return api;
    }

    private const string TargetVocabulary =
        "entityType is 'feature' (any feature, any kind) or one of 'tripLog', 'geofile', " +
        "'georeferencedMap', 'mapView' (case-insensitive).";

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

        var (target, problem) = await ResolveAsync(db, user, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await GuardManageAsync(permissions, user, target!.Value, ct) is { } denied)
        {
            return denied;
        }

        return TypedResults.Ok(await LoadEntriesAsync(db, user, target.Value, ct));
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

        var (resolved, problem) = await ResolveAsync(db, user, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var target = resolved!.Value;
        if (await GuardManageAsync(permissions, user, target, ct) is { } denied)
        {
            return denied;
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
        var alreadyGranted = await GrantsOf(db, target)
            .Select(a => new { a.SubjectKind, a.SubjectId })
            .ToListAsync(ct);

        // Full replace: the ACL is small per object; diffing buys nothing.
        await GrantsOf(db, target).ExecuteDeleteAsync(ct);
        foreach (var entry in request.Entries)
        {
            db.ObjectAcls.Add(NewGrant(target, entry, user.UserId));
        }

        var newlyGranted = request.Entries
            .Where(e => !alreadyGranted.Any(a => a.SubjectKind == e.SubjectKind && a.SubjectId == e.SubjectId))
            .ToList();
        await NotifyGranteesAsync(db, user, target, newlyGranted, ct);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadEntriesAsync(db, user, target, ct));
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

        var (target, problem) = await ResolveAsync(db, user, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var effective = await permissions.EffectiveAsync(user, target!.Value.Entity, ct);
        if (!effective.HasFlag(ObjectPermission.Read))
        {
            return ApiProblems.NotFound("acl.entity_not_found");
        }

        return TypedResults.Ok(effective);
    }

    /// <summary>
    /// Managing implies knowing the object exists, so a caller who cannot manage gets 403
    /// only when they can read it and 404 otherwise. Null means the caller may proceed.
    /// </summary>
    private static async Task<ProblemHttpResult?> GuardManageAsync(
        IPermissionService permissions, UserContext user, AclTarget target, CancellationToken ct)
    {
        if (await permissions.CanAsync(user, target.Entity, ObjectPermission.ManagePermissions, ct))
        {
            return null;
        }

        return await permissions.CanAsync(user, target.Entity, ObjectPermission.Read, ct)
            ? ApiProblems.Forbidden("acl.forbidden")
            : ApiProblems.NotFound("acl.entity_not_found");
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
        AclTarget target,
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

        var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        var actorName = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty;
        var objectName = NameOf(target);

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
                    ["url"] = LinkTo(target),
                });
        }
    }

    /// <summary>
    /// What the record is called. The name field differs per type and <see cref="IProtectedEntity"/>
    /// carries none, so the row loaded during resolution answers it.
    /// </summary>
    private static string NameOf(AclTarget target) => target.Entity switch
    {
        // A feature's name is optional (a boundary or a system may never have been named);
        // an unnamed one is identified by its kind plus the head of its id, as lists do.
        Feature feature => feature.Name is { Length: > 0 } name
            ? name
            : $"{feature.Kind} {feature.Id.ToString("N")[..8]}",
        TripLog trip => trip.Title,
        Geofile geofile => geofile.Name,
        GeoreferencedMap map => map.Name,
        MapView view => view.Name,
        _ => string.Empty,
    };

    private static string LinkTo(AclTarget target) => target.EntityType switch
    {
        null => $"/features/{target.Entity.Id}",
        AttachedEntityType.TripLog => $"/trip-logs/{target.Entity.Id}",
        AttachedEntityType.Geofile or AttachedEntityType.GeoreferencedMap => "/geodata",
        AttachedEntityType.MapView => "/map",
        _ => "/",
    };

    /// <summary>
    /// Loads the entity a (type, id) route pair names, refusing unknown or non-target types.
    /// Features are read through the shared visibility filter, so one the caller cannot see
    /// is "not found" here exactly as it is everywhere else (and soft-deleted ones never
    /// resolve at all).
    /// </summary>
    private static async Task<(AclTarget? Target, ProblemHttpResult? Problem)> ResolveAsync(
        SilexGisDbContext db, UserContext user, string entityType, Guid id, CancellationToken ct)
    {
        if (!TryParseTarget(entityType, out var parsedType))
        {
            return (null, ApiProblems.BadRequest("acl.entity_type_unknown", $"Unknown entity type '{entityType}'."));
        }

        IProtectedEntity? entity = parsedType switch
        {
            null => await db.Features.AsNoTracking()
                .Where(f => f.Id == id)
                .VisibleTo(user, db.ObjectAcls)
                .FirstOrDefaultAsync(ct),
            AttachedEntityType.TripLog => await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.Geofile => await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.GeoreferencedMap =>
                await db.GeoreferencedMaps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            AttachedEntityType.MapView => await db.MapViews.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct),
            _ => null,
        };

        return entity is null
            ? (null, ApiProblems.NotFound("acl.entity_not_found"))
            : (new AclTarget(entity, parsedType), null);
    }

    /// <summary>
    /// Parses the route's target vocabulary, case-insensitively (the JSON contract writes
    /// these names camelCase, and a client that read one out of a payload must be able to
    /// put it back in a URL). A parsed <c>null</c> type means the feature world; teams and
    /// stored files are deliberately absent — team access comes from membership, and a
    /// file's access follows the objects it is attached to.
    /// </summary>
    private static bool TryParseTarget(string entityType, out AttachedEntityType? type)
    {
        switch (entityType.ToLowerInvariant())
        {
            case FeatureTargetName:
                type = null;
                return true;
            case "triplog":
                type = AttachedEntityType.TripLog;
                return true;
            case "geofile":
                type = AttachedEntityType.Geofile;
                return true;
            case "georeferencedmap":
                type = AttachedEntityType.GeoreferencedMap;
                return true;
            case "mapview":
                type = AttachedEntityType.MapView;
                return true;
            default:
                type = null;
                return false;
        }
    }

    /// <summary>The grant rows of one target: features by their FK, everything else by the pair.</summary>
    private static IQueryable<ObjectAcl> GrantsOf(SilexGisDbContext db, AclTarget target)
    {
        var id = target.Entity.Id;
        return target.EntityType is { } type
            ? db.ObjectAcls.AsNoTracking().Where(a => a.EntityType == type && a.EntityId == id)
            : db.ObjectAcls.AsNoTracking().Where(a => a.FeatureId == id);
    }

    private static ObjectAcl NewGrant(AclTarget target, AclEntryWrite entry, Guid grantedBy) => new()
    {
        FeatureId = target.EntityType is null ? target.Entity.Id : null,
        EntityType = target.EntityType,
        EntityId = target.EntityType is null ? null : target.Entity.Id,
        SubjectKind = entry.SubjectKind,
        SubjectId = entry.SubjectId,
        Permissions = entry.Permissions,
        GrantedBy = grantedBy,
    };

    private static async Task<List<AclEntryDto>> LoadEntriesAsync(
        SilexGisDbContext db, UserContext user, AclTarget target, CancellationToken ct)
    {
        var rows = await GrantsOf(db, target).ToListAsync(ct);

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

    /// <summary>
    /// One resolved ACL target. <see cref="EntityType"/> is null for features — the shape
    /// that keys grants by the feature FK — and set for the polymorphic-pair entities.
    /// </summary>
    private readonly record struct AclTarget(IProtectedEntity Entity, AttachedEntityType? EntityType);
}
