// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Teams;

public sealed record TeamDto(
    Guid Id, string Name, string Slug, string? Description, string? Website, int MemberCount);

public sealed record TeamMemberDto(Guid UserId, string? DisplayName, TeamRole Role);

public sealed record TeamWriteRequest(string Name, string? Description, string? Website);

public sealed record TeamMemberWriteRequest(Guid UserId, TeamRole Role);

public sealed class TeamWriteRequestValidator : AbstractValidator<TeamWriteRequest>
{
    public TeamWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Website).MaximumLength(255);
    }
}

public sealed class TeamMemberWriteRequestValidator : AbstractValidator<TeamMemberWriteRequest>
{
    public TeamMemberWriteRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).IsInEnum();
    }
}

/// <summary>
/// Teams: Manager+ creates them (creator becomes team owner); team owners/admins manage
/// membership and metadata. Team lists are visible to all authenticated users — teams
/// are an organizational structure, not protected content.
/// </summary>
public static class TeamEndpoints
{
    public static RouteGroupBuilder MapTeamEndpoints(this RouteGroupBuilder api)
    {
        var teams = api.MapGroup("/teams").WithTags("Teams");

        teams.MapGet("/", ListAsync).WithSummary("All teams with member counts.");
        teams.MapGet("/{id:guid}", GetAsync).WithSummary("Single team.");
        teams.MapPost("/", CreateAsync).WithValidation<TeamWriteRequest>()
            .WithSummary("Creates a team (Manager role and above); the caller becomes team owner.");
        teams.MapPut("/{id:guid}", UpdateAsync).WithValidation<TeamWriteRequest>()
            .WithSummary("Updates team metadata (team admin/owner).");
        teams.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a team (team owner or Admin); objects keep owner-based access.");
        teams.MapGet("/{id:guid}/members", ListMembersAsync).WithSummary("Team members with roles.");
        teams.MapPost("/{id:guid}/members", UpsertMemberAsync).WithValidation<TeamMemberWriteRequest>()
            .WithSummary("Adds a member or changes their role (team admin/owner).");
        teams.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync)
            .WithSummary("Removes a member (team admin/owner; owners cannot be removed).");

        return api;
    }

    private static async Task<Results<Ok<List<TeamDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var teams = await db.Teams.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.Slug, t.Description, t.Website,
                db.TeamMembers.Count(m => m.TeamId == t.Id)))
            .ToListAsync(ct);
        return TypedResults.Ok(teams);
    }

    private static async Task<Results<Ok<TeamDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var team = await db.Teams.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TeamDto(t.Id, t.Name, t.Slug, t.Description, t.Website,
                db.TeamMembers.Count(m => m.TeamId == t.Id)))
            .FirstOrDefaultAsync(ct);
        return team is null ? ApiProblems.NotFound("team.not_found") : TypedResults.Ok(team);
    }

    private static async Task<Results<Created<TeamDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TeamWriteRequest request,
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
            return ApiProblems.Forbidden("team.create_requires_manager");
        }

        var slug = Tag.Slugify(request.Name);
        if (slug.Length == 0)
        {
            return ApiProblems.BadRequest("team.name_invalid", "The team name must contain letters or digits.");
        }

        if (await db.Teams.AnyAsync(t => t.Slug == slug, ct))
        {
            return ApiProblems.BadRequest("team.name_taken", "A team with this name already exists.");
        }

        var team = new Team { Name = request.Name.Trim(), Slug = slug, Description = request.Description, Website = request.Website };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.UserId, Role = TeamRole.Owner });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/teams/{team.Id}",
            new TeamDto(team.Id, team.Name, team.Slug, team.Description, team.Website, 1));
    }

    private static async Task<Results<Ok<TeamDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TeamWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            return ApiProblems.NotFound("team.not_found");
        }

        if (user is null || (!user.IsAdmin && !user.IsTeamAdmin(id)))
        {
            return ApiProblems.Forbidden("team.requires_team_admin");
        }

        team.Name = request.Name.Trim();
        team.Description = request.Description;
        team.Website = request.Website;
        await db.SaveChangesAsync(ct);

        var count = await db.TeamMembers.CountAsync(m => m.TeamId == id, ct);
        return TypedResults.Ok(new TeamDto(team.Id, team.Name, team.Slug, team.Description, team.Website, count));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            return ApiProblems.NotFound("team.not_found");
        }

        var isOwner = user is not null && await db.TeamMembers.AnyAsync(
            m => m.TeamId == id && m.UserId == user.UserId && m.Role == TeamRole.Owner, ct);
        if (user is null || (!user.IsAdmin && !isOwner))
        {
            return ApiProblems.Forbidden("team.requires_owner");
        }

        // Objects bound to the team fall back to owner/visibility access (team_id set null
        // by the FKs configured on protected tables); memberships cascade.
        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<TeamMemberDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListMembersAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.Teams.AnyAsync(t => t.Id == id, ct))
        {
            return ApiProblems.NotFound("team.not_found");
        }

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == id)
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id,
                (m, u) => new TeamMemberDto(m.UserId, u.DisplayName ?? u.UserName, m.Role))
            .ToListAsync(ct);
        return TypedResults.Ok(members);
    }

    private static async Task<Results<Ok<TeamMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpsertMemberAsync(
        Guid id,
        TeamMemberWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.Teams.AnyAsync(t => t.Id == id, ct))
        {
            return ApiProblems.NotFound("team.not_found");
        }

        if (!user.IsAdmin && !user.IsTeamAdmin(id))
        {
            return ApiProblems.Forbidden("team.requires_team_admin");
        }

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (target is null)
        {
            return ApiProblems.BadRequest("team.user_unknown", "The user does not exist.");
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == id && m.UserId == request.UserId, ct);
        if (member is null)
        {
            member = new TeamMember { TeamId = id, UserId = request.UserId, Role = request.Role };
            db.TeamMembers.Add(member);
        }
        else
        {
            // Owners can only be demoted by an Admin (a team admin cannot dethrone the owner).
            if (member.Role == TeamRole.Owner && !user.IsAdmin)
            {
                return ApiProblems.Forbidden("team.owner_immutable");
            }

            member.Role = request.Role;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TeamMemberDto(member.UserId, target.DisplayName ?? target.UserName, member.Role));
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

        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == id && m.UserId == userId, ct);
        if (member is null)
        {
            return ApiProblems.NotFound("team.member_not_found");
        }

        // Members may leave on their own; otherwise team admin/owner (or Admin) required.
        var selfRemoval = userId == user.UserId;
        if (!selfRemoval && !user.IsAdmin && !user.IsTeamAdmin(id))
        {
            return ApiProblems.Forbidden("team.requires_team_admin");
        }

        if (member.Role == TeamRole.Owner && !user.IsAdmin)
        {
            return ApiProblems.Forbidden("team.owner_immutable");
        }

        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
