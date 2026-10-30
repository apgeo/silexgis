// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>The rulesets the caller reaches, for showing where a right came from.</summary>
public sealed record MyPermissionGroupDto(Guid Id, string Name, string Slug, bool IsProtected);

/// <summary>
/// What the caller may do in each domain, with no particular row in view — the shape UI
/// gating needs. Row-level answers come from the per-object access routes instead.
/// </summary>
/// <param name="IsFullAdmin">
/// Whether the caller holds the installation-wide role, which a handful of decisions turn on
/// directly rather than through a domain right — publishing a photograph to the public gallery
/// is one. Stated rather than inferred: a client guessing it from an unrelated domain would
/// offer the control to the wrong people, and hide it from the right ones.
/// </param>
/// <param name="CreateInCavingGroups">
/// For each domain whose rows can belong to a caving group, the caller's own caving groups in
/// which they may create a row bound to that group. A second answer beside the map, and not a
/// correction to it: the map is decided with no row in view, so a right held only at one caving
/// group's scope is absent from it by construction — a member whose club lets them record the
/// club's trips holds no <c>create</c> there, though a trip bound to the club is accepted. This
/// says where such a right does reach, by the rule the create itself is decided by, so a deny or
/// a ruleset edit moves this answer and the write together. It is read to offer a create door
/// and to bound the groups a create form offers, and it speaks of creating only: writing,
/// deleting or sharing under a right held at a group's scope stay questions about a row.
/// Every such domain is a key, with an empty list where the caller may create in none of their
/// groups, so an absent key always means a domain that has no group binding at all.
/// <para>
/// One place where the answer runs ahead of the routes, stated so that nobody draws a door from
/// it there: a vector file and a raster map are created by uploading a file, and neither upload
/// takes a group — each asks for the right with none named and the row is bound afterwards by an
/// edit. A group listed under those two domains is one the rules would admit a bound create in,
/// and no route can make one yet.
/// </para>
/// </param>
public sealed record CapabilitiesDto(
    IReadOnlyDictionary<string, AccessAction> Domains,
    bool IsFullAdmin,
    IReadOnlyDictionary<string, IReadOnlyList<CreatableCavingGroupDto>> CreateInCavingGroups);

/// <summary>
/// One of the caller's own caving groups, named so a form can say which group a new row will
/// belong to rather than calling it "your group".
/// </summary>
public sealed record CreatableCavingGroupDto(Guid Id, string Name);

/// <summary>
/// What the caller may do, in the shape a user interface needs: per domain, with no
/// particular row in view — and, beside that, the caving groups a new row may be bound to.
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
            .WithSummary(
                "The caller's domain-level rights, for interface gating, and the caller's own "
                + "caving groups in which a new row of each group-bindable domain may be created.");
        api.MapGet("/me/permission-groups", GetGroupsAsync)
            .WithTags("Me")
            .WithSummary("The permission groups the caller reaches, directly or through a caving group.");
        return api;
    }

    private static async Task<Results<Ok<CapabilitiesDto>, UnauthorizedHttpResult>> GetAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        return ctx is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await CapabilitiesOfAsync(ctx, db, ct));
    }

    /// <summary>
    /// What the caller may do, as an interface needs it told: the domain-level map, and beside
    /// it the caving groups a create may be bound to.
    /// </summary>
    /// <remarks>
    /// The two are kept as two answers on purpose. Folding a right held at one group's scope
    /// into the map would make every reader of the map wrong at once — a control drawn from
    /// "may write" would appear for somebody who may write a few rows and is refused on the
    /// rest — whereas "with no row in view" is exactly what makes the map safe to gate on. So
    /// the map keeps its meaning, and where a create reaches through a group is said separately,
    /// by the rule the write itself asks.
    /// </remarks>
    private static async Task<CapabilitiesDto> CapabilitiesOfAsync(
        AccessContext ctx, SilexGisDbContext db, CancellationToken ct)
    {
        var creatable = new Dictionary<AccessDomain, IReadOnlyList<Guid>>();
        foreach (var domain in Enum.GetValues<AccessDomain>())
        {
            if (AccessEntryRules.IsTrioDomain(domain))
            {
                creatable[domain] = CreateRules.CavingGroupsToCreateIn(ctx, domain);
            }
        }

        // Named here rather than left for the client to look up: the form that reads this says
        // which group a new row will belong to, and the directory it would otherwise take the
        // name from answers to a read right of its own, which an installation may have taken
        // from this very caller. These are the caller's own memberships, so naming them tells
        // the caller nothing new. One read for every domain at once, and none at all for the
        // account that is in no group.
        //
        // Ordered where the directory of caving groups is ordered, by the database, so the two
        // lists a person may see agree about what "by name" means in their alphabet; then by id,
        // so two groups that share a name keep one order between requests and a form listing
        // them does not reshuffle under somebody who is choosing.
        var groupIds = creatable.Values.SelectMany(ids => ids).Distinct().ToArray();
        var named = groupIds.Length == 0
            ? []
            : await db.CavingGroups.AsNoTracking()
                .Where(group => groupIds.Contains(group.Id))
                .OrderBy(group => group.Name)
                .ThenBy(group => group.Id)
                .Select(group => new CreatableCavingGroupDto(group.Id, group.Name))
                .ToListAsync(ct);

        var createIn = creatable.ToDictionary(
            pair => DomainRights.Name(pair.Key),
            pair => (IReadOnlyList<CreatableCavingGroupDto>)
                [.. named.Where(group => pair.Value.Contains(group.Id))]);

        return new CapabilitiesDto(DomainRights.Of(ctx), ctx.IsFullAdmin, createIn);
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
