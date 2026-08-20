// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Cavers;

/// <summary>One person in the roster, reduced to what the caller may see.</summary>
public sealed record CaverDto(
    Guid Id,
    string Name,
    Guid? UserId,
    string? Email,
    string? Phone,
    string? Notes,
    IReadOnlyList<CaverMembershipDto> CavingGroups);

public sealed record CaverMembershipDto(Guid CavingGroupId, string Name, CavingGroupRole Role);

public sealed record CaverWriteRequest(string FullName, string? Email, string? Phone, string? Notes);

public sealed record CaverAccountLinkRequest(Guid UserId);

public sealed record CaverMergeRequest(Guid SourceCaverId);

public sealed class CaverWriteRequestValidator : AbstractValidator<CaverWriteRequest>
{
    public CaverWriteRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(320).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CaverAccountLinkRequestValidator : AbstractValidator<CaverAccountLinkRequest>
{
    public CaverAccountLinkRequestValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class CaverMergeRequestValidator : AbstractValidator<CaverMergeRequest>
{
    public CaverMergeRequestValidator() => RuleFor(x => x.SourceCaverId).NotEmpty();
}

/// <summary>
/// The roster of people. Names are readable by any signed-in caller, the way a club's member list
/// always has been; contact details follow the account holder's own settings where there is an
/// account, and are otherwise limited to whoever keeps the roster.
/// </summary>
/// <remarks>
/// Roster-keeping is Write over the caver domain, and the checks all run through one helper so
/// the disclosure rule and the edit rule can never drift apart.
/// </remarks>
public static class CaverEndpoints
{
    public static RouteGroupBuilder MapCaverEndpoints(this RouteGroupBuilder api)
    {
        var cavers = api.MapGroup("/cavers").WithTags("Cavers");

        cavers.MapGet("/", ListAsync).WithSummary("The roster, filtered by an optional name search.");
        cavers.MapGet("/{id:guid}", GetAsync).WithSummary("One person, with their caving groups.");
        cavers.MapPost("/", CreateAsync).WithValidation<CaverWriteRequest>()
            .WithSummary("Adds a person to the roster.");
        cavers.MapPut("/{id:guid}", UpdateAsync).WithValidation<CaverWriteRequest>()
            .WithSummary("Edits a person's roster entry.");
        cavers.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Removes a person, refused while trips or a camp's roster still name them.");
        cavers.MapPost("/{id:guid}/account-link", LinkAccountAsync).WithValidation<CaverAccountLinkRequest>()
            .WithSummary("Attaches a user account to this person.");
        cavers.MapDelete("/{id:guid}/account-link", UnlinkAccountAsync)
            .WithSummary("Detaches the user account, keeping the person.");
        cavers.MapPost("/{id:guid}/merge", MergeAsync).WithValidation<CaverMergeRequest>()
            .WithSummary("Folds another entry for the same person into this one.");

        return api;
    }

    /// <summary>
    /// Who may edit the roster. The definition lives with the shared caver directory so every
    /// surface a caver appears on — this slice included — asks the same question; every other
    /// decision here, including how much of a person's contact details are disclosed, defers to it.
    /// </summary>
    private static bool CanKeepRoster(AccessContext ctx) => CaverDirectory.CanKeepRoster(ctx);

    /// <summary>
    /// The domain check for the roster. A rule may be scoped to a single person, so the object
    /// level is consulted whenever an id is in hand; without one the check is domain-wide.
    /// </summary>
    private static bool Holds(AccessContext? ctx, AccessAction action, Guid? caverId = null) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.Cavers,
            action,
            caverId is { } id ? new AccessTargetFacts { ObjectId = id } : null).Allowed;

    private static async Task<Results<Ok<List<CaverDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        string? search,
        bool? unlinked,
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

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var query = db.Cavers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.FullName), EF.Functions.Unaccent(pattern)));
        }

        if (unlinked == true)
        {
            query = query.Where(c => c.UserId == null);
        }

        var cavers = await query.OrderBy(c => c.FullName).Take(200).ToListAsync(ct);
        return TypedResults.Ok(await ProjectAsync(db, user, ctx, cavers, ct));
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
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

        if (!Holds(ctx, AccessAction.Read))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var caver = await db.Cavers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        var projected = await ProjectAsync(db, user, ctx, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Created<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CaverWriteRequest request,
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

        if (!CreateRules.MayCreate(ctx, AccessDomain.Cavers))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var caver = new Caver
        {
            FullName = request.FullName.Trim(),
            Email = Trimmed(request.Email),
            Phone = Trimmed(request.Phone),
            Notes = Trimmed(request.Notes),
        };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, ctx, [caver], ct);
        return TypedResults.Created($"/api/v1/cavers/{caver.Id}", projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CaverWriteRequest request,
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

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        // Editing your own entry is allowed: it is your name on trips. Everyone else's is
        // roster-keeping.
        var canKeepRoster = CanKeepRoster(ctx);
        if (!canKeepRoster && caver.UserId != ctx.UserId)
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        caver.FullName = request.FullName.Trim();
        caver.Email = Trimmed(request.Email);
        caver.Phone = Trimmed(request.Phone);
        if (canKeepRoster)
        {
            // Remarks are written about a person, not by them, so the subject cannot rewrite them.
            caver.Notes = Trimmed(request.Notes);
        }

        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, ctx, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, FullAdminGuard fullAdminGuard,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!Holds(ctx, AccessAction.Delete, id))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        // Checked here rather than left to the foreign key, so the answer is a reason and a
        // remedy instead of a constraint violation: a duplicate entry is merged, not deleted.
        if (await db.TripLogParticipants.AnyAsync(p => p.CaverId == id, ct))
        {
            return ApiProblems.BadRequest(
                "caver.referenced_by_trips",
                "This person is named on trips. Merge their duplicate entry instead of deleting it.");
        }

        // A stay at a camp is the same kind of fact and gets the same refusal: it records where
        // somebody was for a fortnight, and tidying a duplicate entry away must not quietly take
        // that record with it. Kept as its own branch rather than folded into the one above so the
        // answer names what actually blocks the delete; like that one it counts nothing and names
        // nothing, so it cannot become a way to learn about camps the caller may not read.
        if (await db.ExpeditionRoster.AnyAsync(r => r.CaverId == id, ct))
        {
            return ApiProblems.BadRequest(
                "caver.referenced_by_expeditions",
                "This person is on a camp's roster. Merge their duplicate entry instead of deleting it.");
        }

        // Deleting the person cascades their memberships, which can sever an account's
        // only path into Full Administrators.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Resource-link members naming the person have no FK; they go with the entry.
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Caver && m.EntityId == id)
            .ExecuteDeleteAsync(ct);
        db.Cavers.Remove(caver);
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "Deleting this person would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> LinkAccountAsync(
        Guid id,
        CaverAccountLinkRequest request,
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

        if (!CanKeepRoster(ctx))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        var existing = await ProfileDirectory.ExistingIdsAsync(db, [request.UserId], ct);
        if (!existing.Contains(request.UserId))
        {
            return ApiProblems.BadRequest("caver.user_unknown", "The user does not exist.");
        }

        if (await db.Cavers.AnyAsync(c => c.UserId == request.UserId && c.Id != id, ct))
        {
            return ApiProblems.BadRequest(
                "caver.account_already_linked", "That account already belongs to someone in the roster.");
        }

        // Overwriting a live link would strip the current holder of everything this
        // person's memberships carry — a permission edit disguised as a correction.
        // Detaching first makes that its own explicit, audited, guarded act.
        if (caver.UserId is { } linked && linked != request.UserId)
        {
            return ApiProblems.Conflict(
                "caver.account_linked", "This person already has an account. Detach it first.");
        }

        // A membership is a grant path, so attaching an account to a person whose caving
        // groups reach Full Administrators hands over the escape hatch. Reserved to
        // people who already hold it — otherwise roster-keeping would be a route to it.
        if (!ctx.IsFullAdmin && await fullAdminGuard.CaverReachesFullAdministratorsAsync(id, ct))
        {
            return ApiProblems.Forbidden(FullAdminGuard.GrantsFullAdminCode);
        }

        caver.UserId = request.UserId;
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, ctx, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> UnlinkAccountAsync(
        Guid id,
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

        if (!CanKeepRoster(ctx))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        // Unlinking moves rights: the account loses everything that flowed through this
        // person's memberships — including, possibly, its Full Administrators reach.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        caver.UserId = null;
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "Unlinking this account would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);

        var projected = await ProjectAsync(db, user, ctx, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> MergeAsync(
        Guid id,
        CaverMergeRequest request,
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

        if (!CanKeepRoster(ctx))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        if (request.SourceCaverId == id)
        {
            return ApiProblems.BadRequest("caver.merge_self", "A person cannot be merged into themselves.");
        }

        // A merge repoints memberships onto the survivor, so folding in a person whose
        // caving groups reach Full Administrators would hand the survivor's account the
        // escape hatch — the same grant the account-link guard reserves.
        if (!ctx.IsFullAdmin
            && await fullAdminGuard.CaverReachesFullAdministratorsAsync(request.SourceCaverId, ct))
        {
            return ApiProblems.Forbidden(FullAdminGuard.GrantsFullAdminCode);
        }

        var target = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        var source = await db.Cavers.FirstOrDefaultAsync(c => c.Id == request.SourceCaverId, ct);
        if (target is null || source is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        // Two accounts mean two people, whatever the names say. Unlink one first if they really
        // are the same person, so the merge cannot silently discard an account link.
        if (source.UserId is not null && target.UserId is not null)
        {
            return ApiProblems.BadRequest(
                "caver.merge_two_accounts", "Both entries have an account. Detach one before merging.");
        }

        var sourceTrips = await db.TripLogParticipants.Where(p => p.CaverId == source.Id).ToListAsync(ct);
        var targetSlots = await db.TripLogParticipants
            .Where(p => p.CaverId == target.Id)
            .Select(p => new { p.TripLogId, p.RoleId })
            .ToListAsync(ct);

        foreach (var row in sourceTrips)
        {
            // The survivor may already be on that trip doing that job; the duplicate row goes
            // rather than colliding with the uniqueness of (trip, role, person). Matched on the
            // role and not just the trip, so somebody who surveyed under one entry and led under
            // the other keeps both jobs instead of losing one to the merge. Where two rows do
            // collide, the survivor's own times and note stand: the entry being merged away is
            // the duplicate, and preferring what it says would let a stray entry overwrite what
            // somebody deliberately recorded against the person who is being kept.
            if (targetSlots.Any(s => s.TripLogId == row.TripLogId && s.RoleId == row.RoleId))
            {
                db.TripLogParticipants.Remove(row);
            }
            else
            {
                row.CaverId = target.Id;
            }
        }

        // What each entry said about coming on a trip folds the way the trips above do and not the
        // way the camps below do, because a person holds one standing answer about one trip and
        // two rows saying different things is exactly what that uniqueness exists to prevent.
        // Where both entries answered the same trip the survivor's own answer stands: the entry
        // being merged away is the duplicate, and preferring what it says would let a stray
        // half-remembered "maybe" overwrite the yes somebody deliberately gave.
        var sourceAnswers = await db.TripInvitations.Where(x => x.CaverId == source.Id).ToListAsync(ct);
        var targetAnswered = await db.TripInvitations
            .Where(x => x.CaverId == target.Id)
            .Select(x => x.TripLogId)
            .ToListAsync(ct);

        foreach (var answer in sourceAnswers)
        {
            if (targetAnswered.Contains(answer.TripLogId))
            {
                db.TripInvitations.Remove(answer);
            }
            else
            {
                answer.CaverId = target.Id;
            }
        }

        // A camp's roster follows the fold whole, and unlike the trips above nothing is dropped.
        // There is no uniqueness to collide with — a person may leave a camp and come back, so two
        // rows for one person in one role on one camp is an ordinary record of two stays — and
        // where the survivor already holds an overlapping stay on the same camp the answer is both
        // rows, overlapping, which is exactly what the table allows. Collapsing them would mean
        // deciding that two intervals recorded against two entries were one stay, and the merge
        // has no evidence of that: a duplicate entry exists precisely because somebody wrote the
        // same fortnight down twice, or wrote two different ones down. Whoever keeps the camp can
        // correct an interval afterwards; a row this path dropped is not recoverable.
        var sourceStays = await db.ExpeditionRoster.Where(r => r.CaverId == source.Id).ToListAsync(ct);
        foreach (var stay in sourceStays)
        {
            stay.CaverId = target.Id;
        }

        var sourceMemberships = await db.CavingGroupMemberships.Where(m => m.CaverId == source.Id).ToListAsync(ct);
        var targetGroups = await db.CavingGroupMemberships
            .Where(m => m.CaverId == target.Id)
            .Select(m => m.CavingGroupId)
            .ToListAsync(ct);

        foreach (var membership in sourceMemberships)
        {
            if (targetGroups.Contains(membership.CavingGroupId))
            {
                db.CavingGroupMemberships.Remove(membership);
            }
            else
            {
                membership.CaverId = target.Id;
            }
        }

        // Resource-link members follow the fold like trips and memberships do: what
        // named the duplicate now names the survivor, except where the survivor is
        // already a whole member of the same link — the duplicate row goes rather than
        // colliding with one-whole-member-per-target.
        var sourceLinkMembers = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Caver && m.EntityId == source.Id)
            .ToListAsync(ct);
        var targetLinkIds = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Caver && m.EntityId == target.Id)
            .Select(m => m.ResLinkId)
            .ToListAsync(ct);

        foreach (var linkMember in sourceLinkMembers)
        {
            if (targetLinkIds.Contains(linkMember.ResLinkId))
            {
                db.ResLinkMembers.Remove(linkMember);
            }
            else
            {
                linkMember.EntityId = target.Id;
            }
        }

        target.UserId ??= source.UserId;
        target.Email ??= source.Email;
        target.Phone ??= source.Phone;
        target.Notes = string.IsNullOrWhiteSpace(source.Notes)
            ? target.Notes
            : string.IsNullOrWhiteSpace(target.Notes) ? source.Notes : $"{target.Notes}\n{source.Notes}";

        // Merging repoints memberships and can move the account link — both carry
        // inherited rights, so the whole fold must not orphan Full Administrators.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Cleared first: the account link is unique, and both rows exist until the save.
        source.UserId = null;
        await db.SaveChangesAsync(ct);

        db.Cavers.Remove(source);
        await db.SaveChangesAsync(ct);
        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(FullAdminGuard.LastFullAdminCode,
                "This merge would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);

        var projected = await ProjectAsync(db, user, ctx, [target], ct);
        return TypedResults.Ok(projected[0]);
    }

    /// <summary>
    /// Applies the disclosure rule to a batch, resolving each linked account's own settings so
    /// the roster can never show more than that person's profile would.
    /// </summary>
    private static async Task<List<CaverDto>> ProjectAsync(
        SilexGisDbContext db,
        UserContext user,
        AccessContext ctx,
        IReadOnlyList<Caver> cavers,
        CancellationToken ct)
    {
        if (cavers.Count == 0)
        {
            return [];
        }

        var canKeepRoster = CanKeepRoster(ctx);
        var ids = cavers.Select(c => c.Id).ToList();

        var memberships = await (
            from membership in db.CavingGroupMemberships.AsNoTracking()
            join grp in db.CavingGroups.AsNoTracking() on membership.CavingGroupId equals grp.Id
            where ids.Contains(membership.CaverId)
            select new { membership.CaverId, membership.CavingGroupId, grp.Name, membership.Role })
            .ToListAsync(ct);

        var accountIds = cavers.Where(c => c.UserId is not null).Select(c => c.UserId!.Value).ToList();
        var profiles = await ProfileDirectory.ResolveAsync(db, user, accountIds, ct);
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, ids, ct);

        return [.. cavers.Select(caver =>
        {
            // For an account holder the profile projection has already applied their own
            // settings; the disclosure rule itself has one home in CaverProtection.
            var profile = caver.UserId is { } userId ? profiles.GetValueOrDefault(userId) : null;
            var projected = CaverProtection.Project(caver, canKeepRoster, profile);

            return new CaverDto(
                projected.Id,
                labels.GetValueOrDefault(caver.Id) ?? projected.FullName,
                projected.UserId,
                projected.Email,
                projected.Phone,
                projected.Notes,
                [.. memberships
                    .Where(m => m.CaverId == caver.Id)
                    .OrderBy(m => m.Name)
                    .Select(m => new CaverMembershipDto(m.CavingGroupId, m.Name, m.Role))]);
        })];
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
