// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

public sealed record AclEntryDto(
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    string? SubjectName,
    AccessAction Permissions);

public sealed record AclEntryWrite(AccessSubjectKind SubjectKind, Guid SubjectId, AccessAction Permissions);

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
                .Must(p => p != AccessAction.None)
                .WithMessage("A grant needs at least one permission.");
        });
        RuleFor(x => x.Entries)
            .Must(e => e.Select(x => (x.SubjectKind, x.SubjectId)).Distinct().Count() == e.Count)
            .WithMessage("Duplicate subjects in the grant list.");
    }
}

/// <summary>
/// Per-object grant management and the caller's effective permissions, stored as
/// DIRECT access entries at object scope (allow effect) — the one-off-grant shape of
/// the unified access-entry model. This surface deliberately reads and replaces ONLY
/// the rows it can represent: direct + object-scoped + allow. Denies and wider-scoped
/// direct entries live in the same table but belong to the richer permissions surface
/// and survive a full-replace here untouched.
///
/// Grant writes are bounded by the no-amplification rule: a caller may hand out only
/// (action) bits they themselves effectively hold on the object.
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
            .WithSummary("Direct object-scope grant entries of one object (ManagePermissions).")
            .WithDescription(TargetVocabulary);
        objects.MapPut("/acl", ReplaceAclAsync).WithValidation<AclReplaceRequest>()
            .WithSummary("Replaces the object's direct grant entries (ManagePermissions).")
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
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (target, problem) = await ResolveAsync(db, ctx, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await GuardManageAsync(access, ctx, target!.Value, ct) is { } denied)
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
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (resolved, problem) = await ResolveAsync(db, ctx, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var target = resolved!.Value;
        if (await GuardManageAsync(access, ctx, target, ct) is { } denied)
        {
            return denied;
        }

        // Subjects must exist (users or caving groups respectively).
        foreach (var entry in request.Entries)
        {
            var exists = entry.SubjectKind == AccessSubjectKind.User
                ? (await ProfileDirectory.ExistingIdsAsync(db, [entry.SubjectId], ct)).Count > 0
                : await db.CavingGroups.AnyAsync(t => t.Id == entry.SubjectId, ct);
            if (!exists)
            {
                return ApiProblems.BadRequest("acl.subject_unknown", "A grant subject does not exist.");
            }
        }

        // Every entry must be one the model can evaluate faithfully, and no amplification:
        // only action bits the caller effectively holds on this very object can be handed
        // out. Both are decided before anything is written.
        var targetFacts = await access.FactsOfAsync(target.Entity, ct);
        foreach (var entry in request.Entries)
        {
            var proposed = NewEntry(target, entry, ctx.UserId).ToSnapshot();
            if (AccessEntryRules.Validate(proposed) is { } invalid)
            {
                return ApiProblems.BadRequest(invalid,
                    "That combination of actions cannot apply to a single object.");
            }

            if (AccessEntryRules.ExceededActions(ctx, proposed, targetFacts) != AccessAction.None)
            {
                return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
            }
        }

        // Who already had a grant, read before the replace wipes it: a full-replace save that
        // leaves an existing grant untouched is not news, and mailing everyone on every ACL edit
        // would train people to ignore the message that matters.
        var current = await GrantsOf(db, target).ToListAsync(ct);
        var alreadyGranted = current
            .Select(a => new { a.SubjectKind, a.SubjectId })
            .ToList();

        // Full replace of the representable rows; tracked removal so deletions land in
        // the audit trail like every other permission-moving write.
        db.AccessEntries.RemoveRange(current);
        foreach (var entry in request.Entries)
        {
            db.AccessEntries.Add(NewEntry(target, entry, ctx.UserId));
        }

        var newlyGranted = request.Entries
            .Where(e => !alreadyGranted.Any(a => a.SubjectKind == e.SubjectKind && a.SubjectId == e.SubjectId))
            .ToList();
        await NotifyGranteesAsync(db, user, target, newlyGranted, ct);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadEntriesAsync(db, user, target, ct));
    }

    private static async Task<Results<Ok<AccessAction>, UnauthorizedHttpResult, ProblemHttpResult>> EffectiveAsync(
        string entityType,
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (target, problem) = await ResolveAsync(db, ctx, entityType, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var effective = await access.EffectiveAsync(ctx, target!.Value.Entity, ct);
        if (!effective.HasFlag(AccessAction.Read))
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
        IAccessService access, AccessContext ctx, AclTarget target, CancellationToken ct)
    {
        if ((await access.DecideAsync(ctx, AccessAction.ManagePermissions, target.Entity, ct)).Allowed)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, target.Entity, ct)).Allowed
            ? ApiProblems.Forbidden("acl.forbidden")
            : ApiProblems.NotFound("acl.entity_not_found");
    }

    /// <summary>
    /// Tells the people who just gained access to something. A grant to a caving group reaches each of its
    /// members, since a caving group grant is how most people actually receive access.
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
            .Where(e => e.SubjectKind == AccessSubjectKind.User)
            .Select(e => e.SubjectId));

        var cavingGroupIds = granted.Where(e => e.SubjectKind == AccessSubjectKind.CavingGroup).Select(e => e.SubjectId).ToList();
        if (cavingGroupIds.Count > 0)
        {
            // Only the members who hold an account: a grant reaches people who can sign in to
            // use it, and there is nobody to notify for the rest of the roster.
            var members = await db.UsersOfCavingGroupsAsync(cavingGroupIds, ct);
            recipients.UnionWith(members.Select(m => m.UserId));
        }

        // Granting yourself access, or being in a caving group you just granted, is not news.
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
        SilexGisDbContext db, AccessContext ctx, string entityType, Guid id, CancellationToken ct)
    {
        if (!TryParseTarget(entityType, out var parsedType))
        {
            return (null, ApiProblems.BadRequest("acl.entity_type_unknown", $"Unknown entity type '{entityType}'."));
        }

        IProtectedEntity? entity = parsedType switch
        {
            null => await db.Features.AsNoTracking()
                .Where(f => f.Id == id)
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
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
    /// put it back in a URL). A parsed <c>null</c> type means the feature world; caving groups and
    /// stored files are deliberately absent — caving group access comes from membership, and a
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

    /// <summary>
    /// The rows this surface owns: DIRECT entries, allow effect, scoped to exactly this
    /// object. Denies and wider scopes are deliberately excluded — they belong to the
    /// richer permissions surface and must survive a full-replace here.
    /// </summary>
    private static IQueryable<AccessEntry> GrantsOf(SilexGisDbContext db, AclTarget target)
    {
        var id = target.Entity.Id;
        var domain = AccessDomains.Of(target.Entity);
        var query = db.AccessEntries.Where(e =>
            e.SubjectKind != null
            && e.Effect == AccessEffect.Allow
            && e.Domain == domain
            && e.ScopeKind == AccessScopeKind.Object);
        return target.EntityType is null
            ? query.Where(e => e.ScopeFeatureId == id)
            : query.Where(e => e.ScopeId == id);
    }

    private static AccessEntry NewEntry(AclTarget target, AclEntryWrite entry, Guid grantedBy) => new()
    {
        SubjectKind = entry.SubjectKind,
        SubjectId = entry.SubjectId,
        Effect = AccessEffect.Allow,
        Domain = AccessDomains.Of(target.Entity),
        Actions = entry.Permissions,
        ScopeKind = AccessScopeKind.Object,
        ScopeFeatureId = target.EntityType is null ? target.Entity.Id : null,
        ScopeId = target.EntityType is null ? null : target.Entity.Id,
        GrantedBy = grantedBy,
    };

    private static async Task<List<AclEntryDto>> LoadEntriesAsync(
        SilexGisDbContext db, UserContext user, AclTarget target, CancellationToken ct)
    {
        var rows = await GrantsOf(db, target).AsNoTracking().ToListAsync(ct);

        var userIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.User).Select(x => x.SubjectId!.Value).ToList();
        var cavingGroupIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.CavingGroup).Select(x => x.SubjectId!.Value).ToList();
        // Resolved rather than projected: the label a grantee may be shown under is a rule with
        // one home, and it is never their address.
        var userNames = await ProfileDirectory.ResolveLabelsAsync(db, user, userIds, ct);
        var cavingGroupNames = await db.CavingGroups.AsNoTracking()
            .Where(t => cavingGroupIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        // One DTO row per subject with the OR of its action bits — the shape one subject's
        // grant always had on this surface.
        return [.. rows
            .GroupBy(a => (Kind: a.SubjectKind!.Value, Id: a.SubjectId!.Value))
            .Select(g => new AclEntryDto(
                g.Key.Kind,
                g.Key.Id,
                g.Key.Kind == AccessSubjectKind.User
                    ? userNames.GetValueOrDefault(g.Key.Id)
                    : cavingGroupNames.GetValueOrDefault(g.Key.Id),
                g.Aggregate(AccessAction.None, (acc, e) => acc | e.Actions)))];
    }

    /// <summary>
    /// One resolved target. <see cref="EntityType"/> is null for features — the shape
    /// that anchors entries by scope_feature_id — and set for the scope_id entities.
    /// </summary>
    private readonly record struct AclTarget(IProtectedEntity Entity, AttachedEntityType? EntityType);
}
