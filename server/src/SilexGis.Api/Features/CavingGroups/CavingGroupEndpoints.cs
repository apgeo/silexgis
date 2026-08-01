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

public sealed record CavingGroupMemberDto(Guid UserId, string? DisplayName, CavingGroupRole Role);

public sealed record CavingGroupWriteRequest(string Name, CavingGroupType Type, string? Description, string? Website);

public sealed record CavingGroupMemberWriteRequest(Guid UserId, CavingGroupRole Role);

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
        RuleFor(x => x.UserId).NotEmpty();
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
        cavingGroups.MapGet("/{id:guid}/members", ListMembersAsync).WithSummary("CavingGroup members with roles.");
        cavingGroups.MapPost("/{id:guid}/members", UpsertMemberAsync).WithValidation<CavingGroupMemberWriteRequest>()
            .WithSummary("Adds a member or changes their role (caving group admin/owner).");
        cavingGroups.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync)
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
                db.CavingGroupMembers.Count(m => m.CavingGroupId == t.Id)))
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
                db.CavingGroupMembers.Count(m => m.CavingGroupId == t.Id)))
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
        db.CavingGroupMembers.Add(new CavingGroupMember { CavingGroupId = cavingGroup.Id, UserId = user.UserId, Role = CavingGroupRole.Owner });
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

        var count = await db.CavingGroupMembers.CountAsync(m => m.CavingGroupId == id, ct);
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

        var isOwner = user is not null && await db.CavingGroupMembers.AnyAsync(
            m => m.CavingGroupId == id && m.UserId == user.UserId && m.Role == CavingGroupRole.Owner, ct);
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

        var members = await db.CavingGroupMembers.AsNoTracking()
            .Where(m => m.CavingGroupId == id)
            .Select(m => new { m.UserId, m.Role })
            .ToListAsync(ct);

        // Resolved rather than joined: the label a user may be shown under is a rule with one
        // home. This also keeps a membership whose user row has gone, which the previous inner
        // join silently dropped.
        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, members.Select(m => m.UserId), ct);

        return TypedResults.Ok(members
            .Select(m => new CavingGroupMemberDto(m.UserId, labels.GetValueOrDefault(m.UserId), m.Role))
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

        var existing = await ProfileDirectory.ExistingIdsAsync(db, [request.UserId], ct);
        if (!existing.Contains(request.UserId))
        {
            return ApiProblems.BadRequest("caving_group.user_unknown", "The user does not exist.");
        }

        var member = await db.CavingGroupMembers.FirstOrDefaultAsync(m => m.CavingGroupId == id && m.UserId == request.UserId, ct);
        var joined = member is null;
        if (member is null)
        {
            member = new CavingGroupMember { CavingGroupId = id, UserId = request.UserId, Role = request.Role };
            db.CavingGroupMembers.Add(member);
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

        // Queued before the save, so the notification and the membership commit together — and
        // never for someone acting on their own membership, who does not need telling.
        if (member.UserId != user.UserId)
        {
            var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
            NotificationQueue.Enqueue(
                db,
                member.UserId,
                NotificationCategory.CavingGroupMembership,
                joined ? MessageTemplateCatalog.NotifyCavingGroupJoined : MessageTemplateCatalog.NotifyCavingGroupRoleChanged,
                new Dictionary<string, string>
                {
                    ["actorName"] = actorLabels.GetValueOrDefault(user.UserId) ?? string.Empty,
                    ["cavingGroupName"] = cavingGroupName,
                });
        }

        await db.SaveChangesAsync(ct);

        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, [member.UserId], ct);
        return TypedResults.Ok(new CavingGroupMemberDto(
            member.UserId, labels.GetValueOrDefault(member.UserId), member.Role));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveMemberAsync(
        Guid id,
        Guid userId,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var member = await db.CavingGroupMembers.FirstOrDefaultAsync(m => m.CavingGroupId == id && m.UserId == userId, ct);
        if (member is null)
        {
            return ApiProblems.NotFound("caving_group.member_not_found");
        }

        // Members may leave on their own; otherwise caving group admin/owner (or Admin) required.
        var selfRemoval = userId == user.UserId;
        if (!selfRemoval && !user.IsAdmin && !user.IsCavingGroupAdmin(id))
        {
            return ApiProblems.Forbidden("caving_group.requires_caving_group_admin");
        }

        if (member.Role == CavingGroupRole.Owner && !user.IsAdmin)
        {
            return ApiProblems.Forbidden("caving_group.owner_immutable");
        }

        db.CavingGroupMembers.Remove(member);

        // Someone leaving of their own accord already knows; only a removal by someone else is news.
        if (!selfRemoval)
        {
            var cavingGroupName = await db.CavingGroups.Where(t => t.Id == id).Select(t => t.Name).FirstAsync(ct);
            var actorLabels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
            NotificationQueue.Enqueue(
                db,
                userId,
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
