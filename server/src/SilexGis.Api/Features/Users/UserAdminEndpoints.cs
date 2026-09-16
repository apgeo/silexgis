// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Users;

/// <summary>One account, as whoever administers the installation needs to see it.</summary>
/// <remarks>
/// The address is always present here, unlike on the member directory beside it. The directory
/// honours each person's own choice about what to show; this surface is the operator's register
/// of who can sign in, and an account whose address is hidden from it cannot be administered —
/// it is the only handle an operator has on an account whose owner has left.
/// </remarks>
public sealed record AdminUserDto(
    Guid Id,
    string Label,
    string? Email,
    bool EmailConfirmed,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    bool IsFullAdministrator,
    Guid? CaverId,
    DateTimeOffset CreatedAt);

/// <summary>What an administrator may change about an account.</summary>
/// <remarks>
/// Deliberately not a whole-account write. Names, addresses and the rest belong to the person and
/// are edited through their own profile; what an operator needs is the ability to shut an account
/// out and to let it back in. Widening this later is a decision about whose data a profile is, so
/// it is left to be taken deliberately rather than arrived at by adding fields.
/// </remarks>
public sealed record AdminUserUpdateRequest(bool Locked);

/// <summary>
/// Account administration: who can sign in to this installation, and the two things an operator
/// must be able to do about it — shut an account out, and remove one.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the directory beside it because the audience and the rule are both different: the
/// directory answers "who is in this club, as they chose to be seen", and this answers "which
/// accounts exist and can act". It is gated by the <c>Users</c> domain, which was seeded and until
/// now enforced nowhere at all.
/// </para>
/// <para>
/// Both destructive verbs run inside the lockout guard's protocol — stage the change, ask whether
/// any full administrator can still sign in, roll back if not. Locking is guarded exactly as
/// deleting is, because an installation locked out of its own administration is in the same state
/// whether the account was removed or merely shut out, and the guard already counts a locked
/// account as not live.
/// </para>
/// </remarks>
public static class UserAdminEndpoints
{
    public static RouteGroupBuilder MapUserAdminEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users");

        users.MapGet("/", ListAsync)
            .WithSummary("Accounts of this installation (Read on the Users domain).");
        users.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One account (Read on the Users domain).");
        users.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<AdminUserUpdateRequest>()
            .WithSummary("Locks or unlocks an account (Write on the Users domain).");
        users.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an account (Delete on the Users domain).");

        return api;
    }

    private static async Task<Results<Ok<List<AdminUserDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Users, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var administrators = await FullAdministratorIdsAsync(db, ct);
        var rows = await db.Users.AsNoTracking().OrderBy(u => u.Email).ThenBy(u => u.Id).ToListAsync(ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);

        return TypedResults.Ok(rows.ConvertAll(u => ToDto(u, administrators, caverIds)));
    }

    private static async Task<Results<Ok<AdminUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Users, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return ApiProblems.NotFound("user.not_found");
        }

        var administrators = await FullAdministratorIdsAsync(db, ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);
        return TypedResults.Ok(ToDto(user, administrators, caverIds));
    }

    /// <summary>
    /// Shuts an account out, or lets it back in.
    /// </summary>
    /// <remarks>
    /// Locking is written as a lockout far in the future rather than as a flag of its own, because
    /// that is the state every other part of the application already reads: the sign-in path, the
    /// token exchange and the live-administrator query all ask about <c>LockoutEnd</c>, and a
    /// second way of saying the same thing is a second way for them to disagree.
    /// </remarks>
    private static async Task<Results<Ok<AdminUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        AdminUserUpdateRequest request,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        FullAdminGuard fullAdminGuard,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Users, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return ApiProblems.NotFound("user.not_found");
        }

        // Refused before anything is staged rather than through the guard below, because the guard
        // answers "would the installation still have an administrator" and this is a different
        // question with a different answer: an operator locking themselves out while a colleague
        // remains would pass that check and still be a mistake nobody asked to make.
        if (request.Locked && user.Id == ctx.UserId)
        {
            return ApiProblems.BadRequest(
                "user.cannot_lock_self",
                "You cannot lock your own account.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        user.LockoutEnabled = true;
        user.LockoutEnd = request.Locked ? DateTimeOffset.UtcNow.AddYears(100) : null;
        if (!request.Locked)
        {
            // Letting somebody back in clears what shut them out in the first place; leaving the
            // count behind means the next mistyped password locks them straight out again.
            user.AccessFailedCount = 0;
        }

        await db.SaveChangesAsync(ct);

        if (request.Locked && !await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(
                FullAdminGuard.LastFullAdminCode,
                "Locking this account would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);

        // Re-read through the manager so the security stamp the sign-in path checks is the one
        // this change produced.
        await userManager.UpdateSecurityStampAsync(user);

        var administrators = await FullAdministratorIdsAsync(db, ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);
        return TypedResults.Ok(ToDto(user, administrators, caverIds));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        FullAdminGuard fullAdminGuard,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Users, AccessAction.Delete, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return ApiProblems.NotFound("user.not_found");
        }

        if (user.Id == ctx.UserId)
        {
            return ApiProblems.BadRequest(
                "user.cannot_delete_self",
                "You cannot delete your own account.");
        }

        // The person and the account are separate rows on purpose: a club's roster outlives the
        // logins attached to it, and somebody who leaves takes their account with them while the
        // trips they were on keep naming them. So the caver is unlinked rather than removed, and
        // the directory entry stays.
        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.UserId == id, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (caver is not null)
        {
            caver.UserId = null;
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);

        if (!await fullAdminGuard.AnyLiveFullAdminAsync(ct))
        {
            await transaction.RollbackAsync(ct);
            return ApiProblems.Conflict(
                FullAdminGuard.LastFullAdminCode,
                "Deleting this account would leave no signed-in-capable Full Administrator.");
        }

        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>The accounts that hold full administration and can sign in.</summary>
    /// <remarks>
    /// Read from the one query that defines that membership rather than from a second copy, so
    /// this surface cannot say somebody is an administrator while the guard beside it disagrees.
    /// A locked administrator is therefore reported as not being one, which is the truth this
    /// screen exists to show: what matters is who could actually act.
    /// </remarks>
    private static async Task<HashSet<Guid>> FullAdministratorIdsAsync(
        SilexGisDbContext db, CancellationToken ct) =>
        [.. await FullAdministrators.LiveMemberIdsAsync(db, ct)];

    private static async Task<Dictionary<Guid, Guid>> CaverIdsByUserAsync(
        SilexGisDbContext db, CancellationToken ct) =>
        await db.Cavers.AsNoTracking()
            .Where(c => c.UserId != null)
            .ToDictionaryAsync(c => c.UserId!.Value, c => c.Id, ct);

    private static AdminUserDto ToDto(
        SilexGisUser user, HashSet<Guid> administrators, Dictionary<Guid, Guid> caverIds) =>
        new(
            user.Id,
            ProfileProtection.Label(user),
            user.Email,
            user.EmailConfirmed,
            user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
            user.LockoutEnd,
            administrators.Contains(user.Id),
            caverIds.TryGetValue(user.Id, out var caverId) ? caverId : null,
            user.CreatedAt);
}
