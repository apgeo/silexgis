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
/// CavingGroups: Manager+ creates them (creator becomes caving group owner); caving group owners/admins manage
/// membership and metadata. CavingGroup lists are visible to all authenticated users — caving groups
/// are an organizational structure, not protected content.
/// </summary>
public static class CavingGroupEndpoints
{
    public static RouteGroupBuilder MapCavingGroupEndpoints(this RouteGroupBuilder api)
    {
        var cavingGroups = api.MapGroup("/caving-groups").WithTags("CavingGroups");

        cavingGroups.MapGet("/", ListAsync).WithSummary("All caving groups with member counts.");
        cavingGroups.MapGet("/{id:guid}", GetAsync).WithSummary("Single caving group.");
        cavingGroups.MapPost("/", CreateAsync).WithValidation<CavingGroupWriteRequest>()
            .WithSummary("Creates a caving group (Manager role and above); the caller becomes caving group owner.");
        cavingGroups.MapPut("/{id:guid}", UpdateAsync).WithValidation<CavingGroupWriteRequest>()
            .WithSummary("Updates caving group metadata (caving group admin/owner).");
        cavingGroups.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a caving group (caving group owner or Admin); objects keep owner-based access.");
        cavingGroups.MapGet("/{id:guid}/members", ListMembersAsync).WithSummary("Roster of the caving group, people with their roles.");
        cavingGroups.MapPost("/{id:guid}/members", UpsertMemberAsync).WithValidation<CavingGroupMemberWriteRequest>()
            .WithSummary("Adds a member or changes their role (caving group admin/owner).");
        cavingGroups.MapDelete("/{id:guid}/members/{caverId:guid}", RemoveMemberAsync)
            .WithSummary("Removes a member (caving group admin/owner; owners cannot be removed).");

        return api;
    }

    private static async Task<Results<Ok<List<CavingGroupDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var cavingGroups = await db.CavingGroups.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new CavingGroupDto(t.Id, t.Name, t.Slug, t.Type, t.Description, t.Website,
                db.CavingGroupMemberships.Count(m => m.CavingGroupId == t.Id)))
            .ToListAsync(ct);
        return TypedResults.Ok(cavingGroups);
    }

    private static async Task<Results<Ok<CavingGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
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
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.IsAdmin && !user.Roles.Contains(GlobalRoles.Manager))
        {
            return ApiProblems.Forbidden("caving_group.create_requires_manager");
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
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/caving-groups/{cavingGroup.Id}",
            new CavingGroupDto(cavingGroup.Id, cavingGroup.Name, cavingGroup.Slug, cavingGroup.Type, cavingGroup.Description, cavingGroup.Website, 1));
    }

    private static async Task<Results<Ok<CavingGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CavingGroupWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cavingGroup = await db.CavingGroups.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (cavingGroup is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (user is null || (!user.IsAdmin && !user.IsCavingGroupAdmin(id)))
        {
            return ApiProblems.Forbidden("caving_group.requires_caving_group_admin");
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
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var cavingGroup = await db.CavingGroups.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (cavingGroup is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        var isOwner = user is not null && await (
            from membership in db.CavingGroupMemberships
            join caver in db.Cavers on membership.CaverId equals caver.Id
            select new { membership.CavingGroupId, membership.Role, caver.UserId })
            .AnyAsync(m => m.CavingGroupId == id && m.UserId == user.UserId && m.Role == CavingGroupRole.Owner, ct);
        if (user is null || (!user.IsAdmin && !isOwner))
        {
            return ApiProblems.Forbidden("caving_group.requires_owner");
        }

        // Objects bound to the caving group fall back to owner/visibility access (caving_group_id set null
        // by the FKs configured on protected tables); memberships cascade.
        db.CavingGroups.Remove(cavingGroup);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<CavingGroupMemberDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListMembersAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
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
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The name is read rather than an existence check, because the notification names the caving group.
        var cavingGroupName = await db.CavingGroups.Where(t => t.Id == id).Select(t => t.Name).FirstOrDefaultAsync(ct);
        if (cavingGroupName is null)
        {
            return ApiProblems.NotFound("caving_group.not_found");
        }

        if (!user.IsAdmin && !user.IsCavingGroupAdmin(id))
        {
            return ApiProblems.Forbidden("caving_group.requires_caving_group_admin");
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
            // Owners can only be demoted by an Admin (a caving group admin cannot dethrone the owner).
            if (member.Role == CavingGroupRole.Owner && !user.IsAdmin)
            {
                return ApiProblems.Forbidden("caving_group.owner_immutable");
            }

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
                });
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
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
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

        // Members may leave on their own; otherwise caving group admin/owner (or Admin) required.
        var selfRemoval = memberUserId == user.UserId;
        if (!selfRemoval && !user.IsAdmin && !user.IsCavingGroupAdmin(id))
        {
            return ApiProblems.Forbidden("caving_group.requires_caving_group_admin");
        }

        if (member.Role == CavingGroupRole.Owner && !user.IsAdmin)
        {
            return ApiProblems.Forbidden("caving_group.owner_immutable");
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
                });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
