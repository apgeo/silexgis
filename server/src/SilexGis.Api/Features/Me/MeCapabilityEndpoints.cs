// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Features.Permissions;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>The rulesets the caller reaches, for showing where a right came from.</summary>
public sealed record MyPermissionGroupDto(Guid Id, string Name, string Slug, bool IsProtected);

/// <summary>
/// What the caller may do, in the shape a user interface needs: per domain, with no
/// particular row in view.
/// </summary>
/// <remarks>
/// Purely a hint for what to show. Every actual decision is made server-side on the row
/// in question — a domain-level "may write features" says nothing about any one cave,
/// and a client that treated it as permission would simply get a 403 on the write.
/// Row-level answers come from the per-object effective-access route instead.
/// </remarks>
public static class MeCapabilityEndpoints
{
    public static RouteGroupBuilder MapMeCapabilityEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me/capabilities", GetAsync)
            .WithTags("Me")
            .WithSummary("The caller's domain-level rights, for interface gating.");
        api.MapGet("/me/permission-groups", GetGroupsAsync)
            .WithTags("Me")
            .WithSummary("The permission groups the caller reaches, directly or through a caving group.");
        return api;
    }

    private static async Task<Results<Ok<CapabilitiesDto>, UnauthorizedHttpResult>> GetAsync(
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        return ctx is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(PermissionGroupEndpoints.CapabilitiesOf(ctx));
    }

    private static async Task<Results<Ok<List<MyPermissionGroupDto>>, UnauthorizedHttpResult>> GetGroupsAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The caller's own memberships: the ones naming them, and the ones their caving
        // groups hold. All Users is implicit and belongs in the answer even though no row
        // records it — leaving it out would make "where does this right come from?"
        // unanswerable for the rights every account has.
        var cavingGroupIds = ctx.CavingGroupIds.ToArray();
        var reached = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => (m.MemberKind == AccessSubjectKind.User && m.MemberId == ctx.UserId)
                || (m.MemberKind == AccessSubjectKind.CavingGroup && cavingGroupIds.Contains(m.MemberId)))
            .Select(m => m.PermissionGroupId)
            .Distinct()
            .ToListAsync(ct);

        var groups = await db.PermissionGroups.AsNoTracking()
            .Where(g => reached.Contains(g.Id) || g.Slug == SeededPermissionGroups.AllUsersSlug)
            .OrderBy(g => g.Name)
            .Select(g => new MyPermissionGroupDto(g.Id, g.Name, g.Slug, g.IsProtected))
            .ToListAsync(ct);

        return TypedResults.Ok(groups);
    }
}
