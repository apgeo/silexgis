// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
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
/// Roster-keeping is currently held by administrators and managers. It moves onto the general
/// permission model when that lands — the checks are deliberately in one helper here so there is
/// a single place to change.
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
            .WithSummary("Removes a person, refused while trips still name them.");
        cavers.MapPost("/{id:guid}/account-link", LinkAccountAsync).WithValidation<CaverAccountLinkRequest>()
            .WithSummary("Attaches a user account to this person.");
        cavers.MapDelete("/{id:guid}/account-link", UnlinkAccountAsync)
            .WithSummary("Detaches the user account, keeping the person.");
        cavers.MapPost("/{id:guid}/merge", MergeAsync).WithValidation<CaverMergeRequest>()
            .WithSummary("Folds another entry for the same person into this one.");

        return api;
    }

    /// <summary>
    /// Who may edit the roster. One place on purpose: this is the check that moves onto the
    /// general permission model, and everything else here defers to it.
    /// </summary>
    private static bool CanKeepRoster(UserContext user) =>
        user.IsAdmin || user.Roles.Contains(GlobalRoles.Manager);

    private static async Task<Results<Ok<List<CaverDto>>, UnauthorizedHttpResult>> ListAsync(
        string? search,
        bool? unlinked,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
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
        return TypedResults.Ok(await ProjectAsync(db, user, cavers, ct));
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var caver = await db.Cavers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        var projected = await ProjectAsync(db, user, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Created<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CaverWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CanKeepRoster(user))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
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

        var projected = await ProjectAsync(db, user, [caver], ct);
        return TypedResults.Created($"/api/v1/cavers/{caver.Id}", projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CaverWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
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
        if (!CanKeepRoster(user) && caver.UserId != user.UserId)
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        caver.FullName = request.FullName.Trim();
        caver.Email = Trimmed(request.Email);
        caver.Phone = Trimmed(request.Phone);
        if (CanKeepRoster(user))
        {
            // Remarks are written about a person, not by them, so the subject cannot rewrite them.
            caver.Notes = Trimmed(request.Notes);
        }

        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CanKeepRoster(user))
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

        db.Cavers.Remove(caver);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> LinkAccountAsync(
        Guid id,
        CaverAccountLinkRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CanKeepRoster(user))
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

        caver.UserId = request.UserId;
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> UnlinkAccountAsync(
        Guid id, SilexGisDbContext db, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CanKeepRoster(user))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caver is null)
        {
            return ApiProblems.NotFound("caver.not_found");
        }

        caver.UserId = null;
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, [caver], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Ok<CaverDto>, UnauthorizedHttpResult, ProblemHttpResult>> MergeAsync(
        Guid id,
        CaverMergeRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CanKeepRoster(user))
        {
            return ApiProblems.Forbidden("caver.requires_roster_keeper");
        }

        if (request.SourceCaverId == id)
        {
            return ApiProblems.BadRequest("caver.merge_self", "A person cannot be merged into themselves.");
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
            .Select(p => new { p.TripLogId, p.Kind })
            .ToListAsync(ct);

        foreach (var row in sourceTrips)
        {
            // The survivor may already be on that trip in that capacity; the duplicate row goes
            // rather than colliding with the uniqueness of (trip, kind, person).
            if (targetSlots.Any(s => s.TripLogId == row.TripLogId && s.Kind == row.Kind))
            {
                db.TripLogParticipants.Remove(row);
            }
            else
            {
                row.CaverId = target.Id;
            }
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

        target.UserId ??= source.UserId;
        target.Email ??= source.Email;
        target.Phone ??= source.Phone;
        target.Notes = string.IsNullOrWhiteSpace(source.Notes)
            ? target.Notes
            : string.IsNullOrWhiteSpace(target.Notes) ? source.Notes : $"{target.Notes}\n{source.Notes}";

        // Cleared first: the account link is unique, and both rows exist until the save.
        source.UserId = null;
        await db.SaveChangesAsync(ct);

        db.Cavers.Remove(source);
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, user, [target], ct);
        return TypedResults.Ok(projected[0]);
    }

    /// <summary>
    /// Applies the disclosure rule to a batch, resolving each linked account's own settings so
    /// the roster can never show more than that person's profile would.
    /// </summary>
    private static async Task<List<CaverDto>> ProjectAsync(
        SilexGisDbContext db, UserContext user, IReadOnlyList<Caver> cavers, CancellationToken ct)
    {
        if (cavers.Count == 0)
        {
            return [];
        }

        var canKeepRoster = CanKeepRoster(user);
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
            // settings, so reading the result is the whole rule — no second interpretation here.
            var profile = caver.UserId is { } userId ? profiles.GetValueOrDefault(userId) : null;

            return new CaverDto(
                caver.Id,
                labels.GetValueOrDefault(caver.Id) ?? caver.FullName,
                caver.UserId,
                caver.UserId is null ? (canKeepRoster ? caver.Email : null) : profile?.Email,
                caver.UserId is null ? (canKeepRoster ? caver.Phone : null) : profile?.PhoneNumber,
                canKeepRoster ? caver.Notes : null,
                [.. memberships
                    .Where(m => m.CaverId == caver.Id)
                    .OrderBy(m => m.Name)
                    .Select(m => new CaverMembershipDto(m.CavingGroupId, m.Name, m.Role))]);
        })];
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
