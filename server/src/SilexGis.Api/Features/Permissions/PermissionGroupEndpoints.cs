// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// Permission groups: named rulesets with a trustee list — the concept that replaced
/// global roles. A group holds entries (rules) and members (users, caving groups); a
/// user reaches a rule directly, through a caving group, or through the implicit
/// All Users membership every account has.
/// </summary>
/// <remarks>
/// Everything here is governed by the PermissionGroups domain, which the Administrators
/// seed deliberately excludes: running an installation and rewriting its security model
/// are different jobs. Two guards apply throughout — protected groups cannot be renamed
/// or deleted, and no write may hand out a right the author does not hold.
/// </remarks>
public static class PermissionGroupEndpoints
{
    public const string ProtectedCode = "permission_group.protected";
    public const string NotFoundCode = "permission_group.not_found";
    public const string NameTakenCode = "permission_group.name_taken";
    public const string DenyFullAdministratorsCode = "permission_group.deny_full_administrators";

    public static RouteGroupBuilder MapPermissionGroupEndpoints(this RouteGroupBuilder api)
    {
        var groups = api.MapGroup("/permission-groups").WithTags("PermissionGroups");

        groups.MapGet("/catalog", CatalogAsync)
            .WithSummary("Domains, scopes and the actions valid in each — the rule editor's vocabulary.");
        groups.MapPost("/preview", PreviewAsync).WithValidation<AccessPreviewRequest>()
            .WithSummary("The domain-level rights a given user or caving group would hold, explained.");
        groups.MapGet("/", ListAsync).WithSummary("All permission groups with member and rule counts.");
        groups.MapGet("/{id:guid}", GetAsync).WithSummary("One permission group.");
        groups.MapPost("/", CreateAsync).WithValidation<PermissionGroupWriteRequest>()
            .WithSummary("Creates a permission group.");
        groups.MapPut("/{id:guid}", UpdateAsync).WithValidation<PermissionGroupWriteRequest>()
            .WithSummary("Renames or re-describes a permission group (protected ones refuse).");
        groups.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a permission group and its rules (protected ones refuse).");
        groups.MapGet("/{id:guid}/entries", GetEntriesAsync).WithSummary("The group's rules.");
        groups.MapPut("/{id:guid}/entries", ReplaceEntriesAsync)
            .WithValidation<AccessEntryReplaceRequest>()
            .WithSummary("Replaces the group's rules, bounded by what the caller holds.");
        groups.MapGet("/{id:guid}/members", GetMembersAsync).WithSummary("Trustees: users and caving groups.");
        groups.MapPost("/{id:guid}/members", AddMemberAsync)
            .WithValidation<PermissionGroupMemberWriteRequest>()
            .WithSummary("Adds a trustee.");
        groups.MapDelete("/{id:guid}/members/{memberKind}/{memberId:guid}", RemoveMemberAsync)
            .WithSummary("Removes a trustee (never the last way into full administration).");

        return api;
    }

    // ---- vocabulary and preview ----

    private static async Task<Results<Ok<AccessCatalogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CatalogAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden();
        }

        var sets = await db.FeatureSets.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new AccessCatalogFeatureSetDto(s.Id, s.Name))
            .ToListAsync(ct);
        return TypedResults.Ok(new AccessCatalogDto(AccessCatalog.Domains(), sets));
    }

    /// <summary>
    /// What the model would answer for somebody else — the check an editor runs before
    /// saving a rule, and the only honest way to show what a deny actually costs. The
    /// explanations decide as the subject but name anchors for the caller: it is the
    /// person looking whose right to read a rule governs what the answer may name.
    /// </summary>
    private static async Task<Results<Ok<AccessPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewAsync(
        AccessPreviewRequest request,
        SilexGisDbContext db,
        AccessExplainer explainer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden();
        }

        var subject = request.SubjectKind == AccessSubjectKind.User
            ? await AccessContextResolver.ResolveAsync(db, request.SubjectId, ct)
            : await AccessContextResolver.ResolveForCavingGroupAsync(db, request.SubjectId, ct);

        var explained = await explainer.ExplainDomainsAsync(subject, ctx, ct);
        return TypedResults.Ok(new AccessPreviewDto(
            CapabilitiesOf(subject).Domains,
            [
                .. explained.SelectMany(d => d.Explanations.Select(e => new AccessPreviewExplanationDto(
                    AccessCatalog.Name(d.Domain),
                    e.Action,
                    e.Allowed,
                    System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(e.Source.ToString()),
                    e.Level,
                    e.RuleName,
                    e.Redacted))),
            ]));
    }

    /// <summary>Domain-level rights, with no row in view — what UI gating needs.</summary>
    internal static CapabilitiesDto CapabilitiesOf(AccessContext ctx)
    {
        var domains = new Dictionary<string, AccessAction>();
        foreach (var domain in Enum.GetValues<AccessDomain>())
        {
            var held = AccessAction.None;
            foreach (var action in AccessActions.All)
            {
                if (AccessEvaluator.Decide(ctx, domain, action, null).Allowed)
                {
                    held |= action;
                }
            }

            domains[AccessCatalog.Name(domain)] = held;
        }

        return new CapabilitiesDto(domains);
    }

    // ---- groups ----

    private static async Task<Results<Ok<List<PermissionGroupDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden();
        }

        return TypedResults.Ok(await Project(db.PermissionGroups.AsNoTracking().OrderBy(g => g.Name), db)
            .ToListAsync(ct));
    }

    private static async Task<Results<Ok<PermissionGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden();
        }

        var group = await Project(db.PermissionGroups.AsNoTracking().Where(g => g.Id == id), db).FirstOrDefaultAsync(ct);
        return group is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(group);
    }

    private static async Task<Results<Created<PermissionGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        PermissionGroupWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Create))
        {
            return ApiProblems.Forbidden();
        }

        var slug = Tag.Slugify(request.Name);
        if (slug.Length == 0)
        {
            return ApiProblems.BadRequest("permission_group.name_invalid",
                "The name must contain letters or digits.");
        }

        if (await db.PermissionGroups.AnyAsync(g => g.Slug == slug || g.Name == request.Name.Trim(), ct))
        {
            return ApiProblems.BadRequest(NameTakenCode, "A permission group with this name already exists.");
        }

        var group = new PermissionGroup
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
        };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/permission-groups/{group.Id}",
            new PermissionGroupDto(group.Id, group.Name, group.Slug, group.Description,
                group.IsProtected, group.IsSeeded, 0, 0));
    }

    private static async Task<Results<Ok<PermissionGroupDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        PermissionGroupWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        // Renaming a protected group would move the anchor the resolver and the guards
        // key on — the whole reason it is protected.
        if (group.IsProtected && !string.Equals(group.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            return ApiProblems.Conflict(ProtectedCode, "This permission group cannot be renamed.");
        }

        group.Name = request.Name.Trim();
        group.Description = request.Description;
        await db.SaveChangesAsync(ct);

        var dto = await Project(db.PermissionGroups.AsNoTracking().Where(g => g.Id == id), db).FirstAsync(ct);
        return TypedResults.Ok(dto);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Delete, id))
        {
            return ApiProblems.Forbidden();
        }

        if (group.IsProtected)
        {
            return ApiProblems.Conflict(ProtectedCode, "This permission group cannot be deleted.");
        }

        // Entries and trustee rows cascade; both only ever narrow access when they go.
        db.PermissionGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---- rules ----

    private static async Task<Results<Ok<List<AccessEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> GetEntriesAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.PermissionGroups.AnyAsync(g => g.Id == id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden();
        }

        var entries = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == id)
            .OrderBy(e => e.Domain).ThenBy(e => e.ScopeKind).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(await AccessEntryMapping.ProjectAsync(db, ctx, entries, ct));
    }

    private static async Task<Results<Ok<List<AccessEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ReplaceEntriesAsync(
        Guid id,
        AccessEntryReplaceRequest request,
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

        var group = await db.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        // Full Administrators is the escape hatch precisely because membership, not a
        // rule, is its grant; letting it hold rules would invite the belief that a deny
        // could reach it.
        if (string.Equals(group.Slug, SeededPermissionGroups.FullAdministratorsSlug, StringComparison.Ordinal)
            && request.Entries.Count > 0)
        {
            return ApiProblems.Conflict(ProtectedCode,
                "Full Administrators holds no rules — its membership is the grant.");
        }

        var staged = new List<AccessEntry>(request.Entries.Count);
        foreach (var write in request.Entries)
        {
            var entry = AccessEntryMapping.ToEntity(write, ctx.UserId);
            entry.PermissionGroupId = id;
            if (await AccessEntryMapping.RejectAsync(db, access, ctx, entry, ct) is { } problem)
            {
                return problem;
            }

            staged.Add(entry);
        }

        var current = await db.AccessEntries.Where(e => e.PermissionGroupId == id).ToListAsync(ct);
        db.AccessEntries.RemoveRange(current);
        db.AccessEntries.AddRange(staged);
        await db.SaveChangesAsync(ct);

        var saved = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == id)
            .OrderBy(e => e.Domain).ThenBy(e => e.ScopeKind).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(await AccessEntryMapping.ProjectAsync(db, ctx, saved, ct));
    }

    // ---- trustees ----

    private static async Task<Results<Ok<List<PermissionGroupMemberDto>>, UnauthorizedHttpResult, ProblemHttpResult>> GetMembersAsync(
        Guid id,
        SilexGisDbContext db,
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

        if (!await db.PermissionGroups.AnyAsync(g => g.Id == id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Read, id))
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.PermissionGroupMembers.AsNoTracking()
            .Where(m => m.PermissionGroupId == id)
            .Select(m => new { m.MemberKind, m.MemberId })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.MemberKind == AccessSubjectKind.User).Select(r => r.MemberId).ToList();
        var groupIds = rows.Where(r => r.MemberKind == AccessSubjectKind.CavingGroup).Select(r => r.MemberId).ToList();
        // Resolved rather than projected: what a person may be shown as is a rule with
        // one home, and it is never their address.
        var userNames = await ProfileDirectory.ResolveLabelsAsync(db, user, userIds, ct);
        var groupNames = await db.CavingGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return TypedResults.Ok(rows
            .Select(r => new PermissionGroupMemberDto(
                r.MemberKind,
                r.MemberId,
                r.MemberKind == AccessSubjectKind.User
                    ? userNames.GetValueOrDefault(r.MemberId)
                    : groupNames.GetValueOrDefault(r.MemberId)))
            .OrderBy(m => m.MemberName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> AddMemberAsync(
        Guid id,
        PermissionGroupMemberWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var group = await db.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        // The protected groups are not ordinary trustee surfaces. All Users has no
        // membership rows at all — every account is an implicit member. And Full
        // Administrators carries no entries, so the entry-based no-amplification bound
        // below would wave its trustee list through — yet adding a trustee there hands
        // out everything at once. Only someone who already holds everything may do that.
        if (string.Equals(group.Slug, SeededPermissionGroups.AllUsersSlug, StringComparison.Ordinal))
        {
            return ApiProblems.Conflict(ProtectedCode, "Every account is an implicit member of this group.");
        }

        if (string.Equals(group.Slug, SeededPermissionGroups.FullAdministratorsSlug, StringComparison.Ordinal)
            && !ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
        }

        var exists = request.MemberKind == AccessSubjectKind.User
            ? (await ProfileDirectory.ExistingIdsAsync(db, [request.MemberId], ct)).Count > 0
            : await db.CavingGroups.AnyAsync(g => g.Id == request.MemberId, ct);
        if (!exists)
        {
            return ApiProblems.BadRequest("permission_group.member_unknown", "That trustee does not exist.");
        }

        // Adding a trustee hands them everything the ruleset grants, so the same
        // no-amplification bound that governs rules governs this.
        if (await ExceedsOwnRightsAsync(db, ctx, id, ct))
        {
            return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
        }

        if (!await db.PermissionGroupMembers.AnyAsync(m => m.PermissionGroupId == id
                && m.MemberKind == request.MemberKind && m.MemberId == request.MemberId, ct))
        {
            db.PermissionGroupMembers.Add(new PermissionGroupMember
            {
                PermissionGroupId = id,
                MemberKind = request.MemberKind,
                MemberId = request.MemberId,
            });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveMemberAsync(
        Guid id,
        string memberKind,
        Guid memberId,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FullAdminGuard fullAdminGuard,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Taken as a string and parsed case-insensitively: the client echoes the
        // camelCase wire value ("user", "cavingGroup") back into the URL, which the
        // default enum route binding would reject as an unhandled 500.
        if (!RouteEnums.TryParseSubjectKind(memberKind, out var parsedKind))
        {
            return ApiProblems.BadRequest(
                "permission_group.member_kind_unknown", $"Unknown member kind '{memberKind}'.");
        }

        var group = await db.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!Holds(ctx, AccessAction.Write, id))
        {
            return ApiProblems.Forbidden();
        }

        // The escape hatch's member list cuts both ways: stripping administrators is as
        // much a security-model rewrite as appointing them, so it too is reserved to
        // someone who already holds full administration.
        if (string.Equals(group.Slug, SeededPermissionGroups.FullAdministratorsSlug, StringComparison.Ordinal)
            && !ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
        }

        var member = await db.PermissionGroupMembers.FirstOrDefaultAsync(m => m.PermissionGroupId == id
            && m.MemberKind == parsedKind && m.MemberId == memberId, ct);
        if (member is null)
        {
            return ApiProblems.NotFound("permission_group.member_not_found");
        }

        // This is the one path that can empty Full Administrators outright, so it is the
        // one that most needs the live-membership guard.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.PermissionGroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "Removing this trustee would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    // ---- shared ----

    /// <summary>
    /// The domain check for the permission model itself. A rule may be scoped to one
    /// permission group, so the object level is consulted whenever an id is in hand.
    /// </summary>
    private static bool Holds(AccessContext ctx, AccessAction action, Guid? permissionGroupId = null) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.PermissionGroups,
            action,
            permissionGroupId is { } id ? new AccessTargetFacts { ObjectId = id } : null).Allowed;

    /// <summary>
    /// Whether this ruleset grants anything the caller does not already hold. Adding a
    /// trustee is exactly as powerful as writing the rules themselves, so it carries the
    /// same bound.
    /// </summary>
    private static async Task<bool> ExceedsOwnRightsAsync(
        SilexGisDbContext db, AccessContext ctx, Guid permissionGroupId, CancellationToken ct)
    {
        if (ctx.IsFullAdmin)
        {
            return false;
        }

        var entries = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == permissionGroupId)
            .ToListAsync(ct);
        foreach (var entry in entries)
        {
            var facts = await AccessEntryMapping.AnchorFactsAsync(db, entry, ct);
            if (AccessEntryRules.ExceededActions(ctx, entry.ToSnapshot(), facts) != AccessAction.None)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Projects to the wire shape. Takes the source already ordered and filtered: sorting
    /// a projected record is not something the database can be asked to do, so the order
    /// has to be established while these are still rows.
    /// </summary>
    private static IQueryable<PermissionGroupDto> Project(
        IQueryable<PermissionGroup> source, SilexGisDbContext db) =>
        source.Select(g => new PermissionGroupDto(
            g.Id, g.Name, g.Slug, g.Description, g.IsProtected, g.IsSeeded,
            db.PermissionGroupMembers.Count(m => m.PermissionGroupId == g.Id),
            db.AccessEntries.Count(e => e.PermissionGroupId == g.Id)));
}
