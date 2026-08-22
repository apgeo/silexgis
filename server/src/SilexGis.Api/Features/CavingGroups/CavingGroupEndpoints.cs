// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.CavingGroups;

public sealed record CavingGroupDto(
    Guid Id, string Name, string Slug, CavingGroupType Type, string? Description, string? Website, int MemberCount);

/// <summary>A roster row: the person, their account when they have one, and their label.</summary>
public sealed record CavingGroupMemberDto(Guid CaverId, string Name, Guid? UserId, CavingGroupRole Role);

public sealed record CavingGroupWriteRequest(string Name, CavingGroupType Type, string? Description, string? Website);

public sealed record CavingGroupMemberWriteRequest(Guid CaverId, CavingGroupRole Role);

public sealed class CavingGroupWriteRequestValidator : AbstractValidator<CavingGroupWriteRequest>
{
    public CavingGroupWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Website).MaximumLength(255);
    }
}

public sealed class CavingGroupMemberWriteRequestValidator : AbstractValidator<CavingGroupMemberWriteRequest>
{
    public CavingGroupMemberWriteRequestValidator()
    {
        RuleFor(x => x.CaverId).NotEmpty();
        RuleFor(x => x.Role).IsInEnum();
    }
}

/// <summary>
/// CavingGroups: whoever holds Create in the caving-group domain makes them (the creator joins as
/// caving group owner); Write governs metadata and the roster, ManagePermissions the owner seat.
/// Reads are held domain-wide by every account, so caving groups stay what they are — an
/// organizational structure, not protected content.
/// </summary>
/// <remarks>
/// The membership role stored on a roster row is organizational metadata only: it labels who runs
/// the club, and no authorization decision anywhere reads it. Rights come from access entries.
/// </remarks>
public static class CavingGroupEndpoints
{
    public static RouteGroupBuilder MapCavingGroupEndpoints(this RouteGroupBuilder api)
    {
        var cavingGroups = api.MapGroup("/caving-groups").WithTags("CavingGroups");

        cavingGroups.MapGet("/", ListAsync).WithSummary("All caving groups with member counts.");
        cavingGroups.MapGet("/{id:guid}", GetAsync).WithSummary("Single caving group.");
        cavingGroups.MapPost("/", CreateAsync).WithValidation<CavingGroupWriteRequest>()
            .WithSummary("Creates a caving group; the caller becomes caving group owner.");
        cavingGroups.MapPut("/{id:guid}", UpdateAsync).WithValidation<CavingGroupWriteRequest>()
            .WithSummary("Updates caving group metadata.");
        cavingGroups.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a caving group; objects keep owner-based access.");
        cavingGroups.MapGet("/{id:guid}/members", ListMembersAsync).WithSummary("Roster of the caving group, people with their roles.");
        cavingGroups.MapPost("/{id:guid}/members", UpsertMemberAsync).WithValidation<CavingGroupMemberWriteRequest>()
            .WithSummary("Adds a member or changes their role.");
        cavingGroups.MapDelete("/{id:guid}/members/{caverId:guid}", RemoveMemberAsync)
            .WithSummary("Removes a member; the owner seat needs permission management.");

        return api;
    }

    private static async Task<Results<Ok<List<CavingGroupDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var cavingGroups = await db.CavingGroups.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new CavingGroupDto(t.Id, t.Name, t.Slug, t.Type, t.Description, t.Website,
                db.CavingGroupMemberships.Count(m => m.CavingGroupId == t.Id)))
            .ToListAsync(ct);
        return TypedResults.Ok(cavingGroups);
    }

    private static async Task<Results<Ok<CavingGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var cavingGroup = await db.CavingGroups.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new CavingGroupDto(t.Id, t.Name, t.Slug, t.Type, t.Description, t.Website,
                db.CavingGroupMemberships.Count(m => m.CavingGroupId == t.Id)))
            .FirstOrDefaultAsync(ct);
        return cavingGroup is null ? ApiProblems.NotFound("caving_group.not_found") : TypedResults.Ok(cavingGroup);
    }

    private static async Task<Results<Created<CavingGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CavingGroupWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.CavingGroups))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var slug = Tag.Slugify(request.Name);
        if (slug.Length == 0)
        {
            return ApiProblems.BadRequest("caving_group.name_invalid", "The caving group name must contain letters or digits.");
        }

        if (await db.CavingGroups.AnyAsync(t => t.Slug == slug, ct))
        {
            return ApiProblems.BadRequest("caving_group.name_taken", "A caving group with this name already exists.");
        }

        var cavingGroup = new CavingGroup { Name = request.Name.Trim(), Slug = slug, Type = request.Type, Description = request.Description, Website = request.Website };
        db.CavingGroups.Add(cavingGroup);
        // The creator joins as owner through their own roster entry, made now if this account
        // never had one.
        var creator = await CaverDirectory.EnsureForUserAsync(db, user, user.UserId, ct);
        db.CavingGroupMemberships.Add(new CavingGroupMembership
        {
            CavingGroupId = cavingGroup.Id,
            CaverId = creator.Id,
            Role = CavingGroupRole.Owner,
        });

        // The group's default permission list: the "«name» — members" starter ruleset
        // (members read/write/create club content and see club caves' exact locations)
        // and the creator's "«name» — managers" group — all ordinary, editable data.
        await CavingGroupPermissionSeeder.SeedForNewGroupAsync(db, cavingGroup, user.UserId, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/caving-groups/{cavingGroup.Id}",
            new CavingGroupDto(cavingGroup.Id, cavingGroup.Name, cavingGroup.Slug, cavingGroup.Type, cavingGroup.Description, cavingGroup.Website, 1));
    }

    private static async Task<Results<Ok<CavingGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CavingGroupWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cavingGroup = await db.CavingGroups.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (cavingGroup is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        cavingGroup.Name = request.Name.Trim();
        cavingGroup.Type = request.Type;
        cavingGroup.Description = request.Description;
        cavingGroup.Website = request.Website;
        await db.SaveChangesAsync(ct);

        var count = await db.CavingGroupMemberships.CountAsync(m => m.CavingGroupId == id, ct);
        return TypedResults.Ok(new CavingGroupDto(cavingGroup.Id, cavingGroup.Name, cavingGroup.Slug, cavingGroup.Type, cavingGroup.Description, cavingGroup.Website, count));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FullAdminGuard fullAdminGuard,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cavingGroup = await db.CavingGroups.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (cavingGroup is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!Holds(ctx, AccessAction.Delete, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        // A DENY anchored on this group would be silently cancelled by deleting it —
        // that must stay an explicit act on the entry itself, never a delete side
        // effect. Allow entries anchored here only ever narrow when removed, so they
        // (and trustee rows naming the group) go with it, tracked and audited.
        var anchoredDenies = await db.AccessEntries.AnyAsync(e =>
            e.Effect == AccessEffect.Deny
            && ((e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId == id)
                || (e.Domain == AccessDomain.CavingGroups
                    && e.ScopeKind == AccessScopeKind.Object && e.ScopeId == id)), ct);
        if (anchoredDenies)
        {
            return ApiProblems.Conflict("caving_group.deny_entries_exist",
                "Deny entries are anchored on this caving group; remove them first.");
        }

        var anchoredEntries = await db.AccessEntries.Where(e =>
                (e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId == id)
                || (e.Domain == AccessDomain.CavingGroups
                    && e.ScopeKind == AccessScopeKind.Object && e.ScopeId == id)
                || (e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId == id))
            .ToListAsync(ct);
        var trusteeRows = await db.PermissionGroupMembers
            .Where(m => m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == id)
            .ToListAsync(ct);

        // Objects bound to the caving group fall back to owner/visibility access (caving_group_id set null
        // by the FKs configured on protected tables); memberships cascade. The whole removal commits
        // only if a live Full Administrator remains reachable — this group could have been the last
        // path into that protected permission group.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.AccessEntries.RemoveRange(anchoredEntries);
        db.PermissionGroupMembers.RemoveRange(trusteeRows);
        // Resource-link members naming the group have no FK; they go with it.
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.CavingGroup && m.EntityId == id)
            .ExecuteDeleteAsync(ct);
        db.CavingGroups.Remove(cavingGroup);
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "Deleting this caving group would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<CavingGroupMemberDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListMembersAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (!await db.CavingGroups.AnyAsync(t => t.Id == id, ct))
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        var members = await (
            from membership in db.CavingGroupMemberships.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on membership.CaverId equals caver.Id
            where membership.CavingGroupId == id
            select new { membership.CaverId, caver.UserId, membership.Role })
            .ToListAsync(ct);

        // Resolved rather than joined: the label a person may be shown under is a rule with one
        // home, and it prefers their account's chosen name over the roster spelling.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, members.Select(m => m.CaverId), ct);

        return TypedResults.Ok(members
            .Select(m => new CavingGroupMemberDto(
                m.CaverId, labels.GetValueOrDefault(m.CaverId) ?? string.Empty, m.UserId, m.Role))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<Results<Ok<CavingGroupMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpsertMemberAsync(
        Guid id,
        CavingGroupMemberWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The name is read rather than an existence check, because the notification names the caving group.
        var cavingGroupName = await db.CavingGroups.Where(t => t.Id == id).Select(t => t.Name).FirstOrDefaultAsync(ct);
        if (cavingGroupName is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == request.CaverId, ct);
        if (caver is null)
        {
            return ApiProblems.BadRequest("caving_group.caver_unknown", "The caver does not exist.");
        }

        var member = await db.CavingGroupMemberships
            .FirstOrDefaultAsync(m => m.CavingGroupId == id && m.CaverId == request.CaverId, ct);
        var joined = member is null;
        if (member is null)
        {
            member = new CavingGroupMembership { CavingGroupId = id, CaverId = request.CaverId, Role = request.Role };
            db.CavingGroupMemberships.Add(member);
        }
        else
        {
            // The role is organizational metadata and nothing more — it labels people in
            // the roster and never gates anything, so editing it is an ordinary roster
            // write. Rights over the group flow from access entries alone.
            member.Role = request.Role;
        }

        // Queued before the save, so the notification and the membership commit together — never
        // for someone acting on their own membership, and never at all for a person with no
        // account, who has nowhere to receive it.
        if (caver.UserId is { } memberUserId && memberUserId != user.UserId)
        {
            var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
            NotificationQueue.Enqueue(
                db,
                memberUserId,
                NotificationCategory.CavingGroupMembership,
                joined ? MessageTemplateCatalog.NotifyCavingGroupJoined : MessageTemplateCatalog.NotifyCavingGroupRoleChanged,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty,
                    ["cavingGroupName"] = cavingGroupName,
                },
                // The group's name above is what it was called at the time. Whether this reader
                // may still be shown it is decided when they read the message, against this.
                NotificationTargetKind.CavingGroup,
                id);
        }

        await db.SaveChangesAsync(ct);

        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, [member.CaverId], ct);
        return TypedResults.Ok(new CavingGroupMemberDto(
            member.CaverId, labels.GetValueOrDefault(member.CaverId) ?? caver.FullName, caver.UserId, member.Role));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveMemberAsync(
        Guid id,
        Guid caverId,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        FullAdminGuard fullAdminGuard,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var member = await db.CavingGroupMemberships
            .FirstOrDefaultAsync(m => m.CavingGroupId == id && m.CaverId == caverId, ct);
        if (member is null)
        {
            return ApiProblems.NotFound("caving_group.member_not_found");
        }

        var memberUserId = await db.Cavers.Where(c => c.Id == caverId).Select(c => c.UserId).FirstOrDefaultAsync(ct);

        // Members may leave on their own — nobody is held in a club against their will;
        // removing anyone else is a roster edit.
        var selfRemoval = memberUserId == user.UserId;
        if (!selfRemoval && !Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        db.CavingGroupMemberships.Remove(member);

        // Someone leaving of their own accord already knows; only a removal by someone else is
        // news, and only when there is an account to tell.
        if (!selfRemoval && memberUserId is { } removedUserId)
        {
            var cavingGroupName = await db.CavingGroups.Where(t => t.Id == id).Select(t => t.Name).FirstAsync(ct);
            var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
            NotificationQueue.Enqueue(
                db,
                removedUserId,
                NotificationCategory.CavingGroupMembership,
                MessageTemplateCatalog.NotifyCavingGroupRemoved,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty,
                    ["cavingGroupName"] = cavingGroupName,
                },
                NotificationTargetKind.CavingGroup,
                id);
        }

        // Leaving a group can sever someone's only path into Full Administrators; the
        // installation must never end up without a live one.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "Removing this member would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The domain check for caving groups. A rule may be scoped to a single group, so the object
    /// level is consulted whenever an id is in hand; without one the check is domain-wide and only
    /// unnarrowed global rules can answer it.
    /// </summary>
    private static bool Holds(AccessContext? ctx, AccessAction action, Guid? cavingGroupId = null) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.CavingGroups,
            action,
            cavingGroupId is { } id ? new AccessTargetFacts { ObjectId = id } : null).Allowed;
}
