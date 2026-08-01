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
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>One rule written straight onto an object, as the Permissions tab edits it.</summary>
public sealed record ObjectAccessEntryDto(
    long Id,
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    string? SubjectName,
    AccessEffect Effect,
    AccessAction Actions,
    AccessScopeKind ScopeKind);

public sealed record ObjectAccessEntryWrite(
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    AccessEffect Effect,
    AccessAction Actions,
    AccessScopeKind ScopeKind);

public sealed record ObjectAccessReplaceRequest(IReadOnlyList<ObjectAccessEntryWrite> Entries);

public sealed class ObjectAccessReplaceRequestValidator : AbstractValidator<ObjectAccessReplaceRequest>
{
    public ObjectAccessReplaceRequestValidator()
    {
        RuleFor(x => x.Entries).NotNull();
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(x => x.SubjectId).NotEmpty();
            entry.RuleFor(x => x.SubjectKind).IsInEnum();
            entry.RuleFor(x => x.Effect).IsInEnum();
            entry.RuleFor(x => x.Actions)
                .Must(a => a != AccessAction.None)
                .WithMessage("A rule needs at least one action.");
            // The tab offers exactly two reaches: this object, or it and everything
            // contained in it. Wider scopes are ruleset territory.
            entry.RuleFor(x => x.ScopeKind)
                .Must(s => s is AccessScopeKind.Object or AccessScopeKind.Subtree)
                .WithMessage("A per-object rule covers this object or its subtree.");
        });
        RuleFor(x => x.Entries)
            .Must(e => e.Select(x => (x.SubjectKind, x.SubjectId, x.Effect, x.ScopeKind)).Distinct().Count() == e.Count)
            .WithMessage("Duplicate rules for the same subject, effect and reach.");
    }
}

/// <summary>
/// The rules written directly onto one object, and what the caller may do to it. This is
/// the per-object half of the one access model: same table, same precedence, same
/// explainer as a ruleset — the difference is only where the rule lives.
/// </summary>
/// <remarks>
/// A one-off grant here therefore gets everything the model has, which the old ACL never
/// did: an explicit deny, a reach that covers everything contained in the object, and an
/// answer to "why can this person see it?".
/// </remarks>
public static class ObjectAccessEndpoints
{
    /// <summary>
    /// The one route name of the whole feature world. Caves, entrances, centerlines and
    /// generic features share it because a rule on a feature is keyed by its id alone —
    /// the kind adds nothing the route needs and would only invite callers to guess wrong.
    /// </summary>
    private const string FeatureTargetName = "feature";

    private const string TargetVocabulary =
        "entityType is 'feature' (any feature, any kind) or one of 'tripLog', 'geofile', " +
        "'georeferencedMap', 'mapView' (case-insensitive).";

    public const string NotFoundCode = "access.entity_not_found";

    public static RouteGroupBuilder MapObjectAccessEndpoints(this RouteGroupBuilder api)
    {
        var objects = api.MapGroup("/objects/{entityType}/{id:guid}").WithTags("Permissions");

        objects.MapGet("/access", GetAsync)
            .WithSummary("Rules written directly onto this object (ManagePermissions).")
            .WithDescription(TargetVocabulary);
        objects.MapPut("/access", ReplaceAsync).WithValidation<ObjectAccessReplaceRequest>()
            .WithSummary("Replaces this object's direct rules, bounded by what the caller holds.")
            .WithDescription(TargetVocabulary);
        objects.MapGet("/effective-access", EffectiveAsync)
            .WithSummary("What the caller may do here; ?explain=true names the deciding rule.")
            .WithDescription(TargetVocabulary);

        return api;
    }

    private static async Task<Results<Ok<List<ObjectAccessEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
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

        return TypedResults.Ok(await LoadAsync(db, user, target.Value, ct));
    }

    private static async Task<Results<Ok<List<ObjectAccessEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ReplaceAsync(
        string entityType,
        Guid id,
        ObjectAccessReplaceRequest request,
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

        // A subtree rule only means anything where there is a hierarchy to descend.
        if (target.EntityType is not null
            && request.Entries.Any(e => e.ScopeKind == AccessScopeKind.Subtree))
        {
            return ApiProblems.BadRequest(AccessEntryRules.ScopeInvalidCode,
                "Only features contain other objects, so only they take a subtree rule.");
        }

        foreach (var entry in request.Entries)
        {
            var exists = entry.SubjectKind == AccessSubjectKind.User
                ? (await ProfileDirectory.ExistingIdsAsync(db, [entry.SubjectId], ct)).Count > 0
                : await db.CavingGroups.AnyAsync(g => g.Id == entry.SubjectId, ct);
            if (!exists)
            {
                return ApiProblems.BadRequest("access.subject_unknown", "A rule's subject does not exist.");
            }
        }

        var staged = new List<AccessEntry>(request.Entries.Count);
        foreach (var write in request.Entries)
        {
            var entry = ToEntity(target, write, ctx.UserId);
            if (await AccessEntryMapping.RejectAsync(db, access, ctx, entry, ct) is { } rejected)
            {
                return rejected;
            }

            staged.Add(entry);
        }

        // Who already had a rule here, read before the replace wipes it: a full-replace
        // that leaves an existing grant untouched is not news, and mailing everyone on
        // every edit would train people to ignore the message that matters.
        var current = await DirectRulesOf(db, target).ToListAsync(ct);
        var alreadyGranted = current
            .Where(e => e.Effect == AccessEffect.Allow)
            .Select(e => (e.SubjectKind, e.SubjectId))
            .ToList();

        db.AccessEntries.RemoveRange(current);
        db.AccessEntries.AddRange(staged);

        var newlyGranted = request.Entries
            .Where(e => e.Effect == AccessEffect.Allow
                && !alreadyGranted.Any(a => a.SubjectKind == e.SubjectKind && a.SubjectId == e.SubjectId))
            .ToList();
        await NotifyGranteesAsync(db, user, target, newlyGranted, ct);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, user, target, ct));
    }

    /// <summary>
    /// What the caller may do here, and — on request — which rule decided each action.
    /// The explanation is redacted where the caller may not read the anchor: a refusal
    /// must not disclose the name of an area somebody hid, nor the shape of the
    /// installation's policy.
    /// </summary>
    private static async Task<Results<Ok<EffectiveAccessDto>, UnauthorizedHttpResult, ProblemHttpResult>> EffectiveAsync(
        string entityType,
        Guid id,
        bool? explain,
        SilexGisDbContext db,
        IAccessService access,
        AccessExplainer explainer,
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

        var entity = target!.Value.Entity;
        var effective = await access.EffectiveAsync(ctx, entity, ct);
        if (!effective.HasFlag(AccessAction.Read))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (explain != true)
        {
            return TypedResults.Ok(new EffectiveAccessDto(effective, null));
        }

        var facts = await access.FactsOfAsync(entity, ct);
        var explanations = await explainer.ExplainAsync(ctx, AccessDomains.Of(entity), facts, ct);
        return TypedResults.Ok(new EffectiveAccessDto(effective,
        [
            .. explanations.Select(e => new AccessExplanationDto(
                e.Action,
                e.Allowed,
                System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(e.Source.ToString()),
                e.Level,
                e.RuleName,
                e.Redacted)),
        ]));
    }

    /// <summary>
    /// Managing implies knowing the object exists, so a caller who cannot manage gets 403
    /// only when they can read it and 404 otherwise. Null means the caller may proceed.
    /// </summary>
    private static async Task<ProblemHttpResult?> GuardManageAsync(
        IAccessService access, AccessContext ctx, AccessTarget target, CancellationToken ct)
    {
        if ((await access.DecideAsync(ctx, AccessAction.ManagePermissions, target.Entity, ct)).Allowed)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, target.Entity, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(NotFoundCode);
    }

    /// <summary>
    /// Tells the people who just gained access. A grant to a caving group reaches each of
    /// its account-holding members, since that is how most people actually receive access.
    /// </summary>
    /// <remarks>
    /// Naming the record is safe here and nowhere near the location rules: a grant confers
    /// Read on it, names are shown in every list to anyone with Read, and what location
    /// protection hides is coordinates and address fields — never the name. The message
    /// carries the name and a link, and no geometry of any kind.
    /// </remarks>
    private static async Task NotifyGranteesAsync(
        SilexGisDbContext db,
        UserContext user,
        AccessTarget target,
        IReadOnlyList<ObjectAccessEntryWrite> granted,
        CancellationToken ct)
    {
        if (granted.Count == 0)
        {
            return;
        }

        var recipients = new HashSet<Guid>(granted
            .Where(e => e.SubjectKind == AccessSubjectKind.User)
            .Select(e => e.SubjectId));

        var cavingGroupIds = granted
            .Where(e => e.SubjectKind == AccessSubjectKind.CavingGroup)
            .Select(e => e.SubjectId).ToList();
        if (cavingGroupIds.Count > 0)
        {
            // Only the members who hold an account: a grant reaches people who can sign in
            // to use it, and there is nobody to notify for the rest of the roster.
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

    private static string NameOf(AccessTarget target) => target.Entity switch
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

    private static string LinkTo(AccessTarget target) => target.EntityType switch
    {
        null => $"/features/{target.Entity.Id}",
        AttachedEntityType.TripLog => $"/trip-logs/{target.Entity.Id}",
        AttachedEntityType.Geofile or AttachedEntityType.GeoreferencedMap => "/geodata",
        AttachedEntityType.MapView => "/map",
        _ => "/",
    };

    /// <summary>
    /// Loads the entity a (type, id) route pair names, refusing unknown or non-target
    /// types. Features are read through the shared visibility filter, so one the caller
    /// cannot see is "not found" here exactly as it is everywhere else (and soft-deleted
    /// ones never resolve at all).
    /// </summary>
    private static async Task<(AccessTarget? Target, ProblemHttpResult? Problem)> ResolveAsync(
        SilexGisDbContext db, AccessContext ctx, string entityType, Guid id, CancellationToken ct)
    {
        if (!TryParseTarget(entityType, out var parsedType))
        {
            return (null, ApiProblems.BadRequest("access.entity_type_unknown", $"Unknown entity type '{entityType}'."));
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
            ? (null, ApiProblems.NotFound(NotFoundCode))
            : (new AccessTarget(entity, parsedType), null);
    }

    /// <summary>
    /// Parses the route's target vocabulary, case-insensitively (the JSON contract writes
    /// these names camelCase, and a client that read one out of a payload must be able to
    /// put it back in a URL). A parsed <c>null</c> type means the feature world; caving
    /// groups, permission groups and feature sets are deliberately absent — rules on those
    /// are authored on their own pages, where the rest of their administration lives.
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
    /// The rules this surface owns: direct ones, anchored on this object — at object or
    /// subtree reach. Ruleset entries are somebody else's page and survive a replace here
    /// untouched.
    /// </summary>
    private static IQueryable<AccessEntry> DirectRulesOf(SilexGisDbContext db, AccessTarget target)
    {
        var id = target.Entity.Id;
        var domain = AccessDomains.Of(target.Entity);
        var query = db.AccessEntries.Where(e =>
            e.SubjectKind != null
            && e.Domain == domain
            && (e.ScopeKind == AccessScopeKind.Object || e.ScopeKind == AccessScopeKind.Subtree));
        return target.EntityType is null
            ? query.Where(e => e.ScopeFeatureId == id)
            : query.Where(e => e.ScopeId == id);
    }

    private static AccessEntry ToEntity(AccessTarget target, ObjectAccessEntryWrite write, Guid grantedBy) => new()
    {
        SubjectKind = write.SubjectKind,
        SubjectId = write.SubjectId,
        Effect = write.Effect,
        Domain = AccessDomains.Of(target.Entity),
        Actions = write.Actions,
        ScopeKind = write.ScopeKind,
        ScopeFeatureId = target.EntityType is null ? target.Entity.Id : null,
        ScopeId = target.EntityType is null ? null : target.Entity.Id,
        GrantedBy = grantedBy,
    };

    private static async Task<List<ObjectAccessEntryDto>> LoadAsync(
        SilexGisDbContext db, UserContext user, AccessTarget target, CancellationToken ct)
    {
        var rows = await DirectRulesOf(db, target).AsNoTracking()
            .OrderBy(e => e.SubjectKind).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var userIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.User)
            .Select(x => x.SubjectId!.Value).ToList();
        var cavingGroupIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.CavingGroup)
            .Select(x => x.SubjectId!.Value).ToList();
        // Resolved rather than projected: the label a subject may be shown under is a rule
        // with one home, and it is never their address.
        var userNames = await ProfileDirectory.ResolveLabelsAsync(db, user, userIds, ct);
        var cavingGroupNames = await db.CavingGroups.AsNoTracking()
            .Where(g => cavingGroupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return
        [
            .. rows.Select(e => new ObjectAccessEntryDto(
                e.Id,
                e.SubjectKind!.Value,
                e.SubjectId!.Value,
                e.SubjectKind == AccessSubjectKind.User
                    ? userNames.GetValueOrDefault(e.SubjectId!.Value)
                    : cavingGroupNames.GetValueOrDefault(e.SubjectId!.Value),
                e.Effect,
                e.Actions,
                e.ScopeKind)),
        ];
    }

    /// <summary>
    /// One resolved target. <see cref="EntityType"/> is null for features — the shape that
    /// anchors rules by the feature FK — and set for the id-anchored entities.
    /// </summary>
    private readonly record struct AccessTarget(IProtectedEntity Entity, AttachedEntityType? EntityType);
}
