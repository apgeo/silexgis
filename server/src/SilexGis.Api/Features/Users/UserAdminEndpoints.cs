// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
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
    bool HasPassword,
    bool TwoFactorEnabled,
    Guid? CaverId,
    DateTimeOffset CreatedAt);

/// <summary>A caving group through which an account reaches a permission group.</summary>
public sealed record AdminUserCavingGroupRefDto(Guid Id, string Name);

/// <summary>A permission group an account belongs to, and how it gets there.</summary>
/// <param name="Direct">The account is named as a member itself.</param>
/// <param name="Through">
/// The caving groups of the account's roster entry that are members — each one a path by which
/// the account holds the group, independent of the direct membership and of each other.
/// </param>
public sealed record AdminUserPermissionGroupDto(
    Guid Id, string Name, bool Direct, IReadOnlyList<AdminUserCavingGroupRefDto> Through);

/// <summary>
/// One account as the register shows it, with the permission groups it belongs to beside it.
/// </summary>
/// <remarks>
/// The groups come only with the single read: the list would have to resolve them for every row
/// of every page, and the page asks for them only when somebody opens an account. They are a
/// reading of the membership the permission-groups page edits, not a second place to edit it.
/// The implicit group every account belongs to is not listed, because no row puts anybody in it.
/// </remarks>
public sealed record AdminUserDetailDto(
    Guid Id,
    string Label,
    string? Email,
    bool EmailConfirmed,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    bool IsFullAdministrator,
    bool HasPassword,
    bool TwoFactorEnabled,
    Guid? CaverId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AdminUserPermissionGroupDto> PermissionGroups);

/// <summary>What an administrator may change about an account.</summary>
/// <remarks>
/// Deliberately not a whole-account write. Names, addresses and the rest belong to the person and
/// are edited through their own profile; what an operator needs is the ability to shut an account
/// out and to let it back in. Widening this later is a decision about whose data a profile is, so
/// it is left to be taken deliberately rather than arrived at by adding fields.
/// </remarks>
public sealed record AdminUserUpdateRequest(bool Locked);

/// <summary>An account an administrator opens for somebody: who it is for, and their address.</summary>
/// <remarks>
/// No password, deliberately. The person chooses their own through the same link a forgotten
/// password produces, so the administrator never knows it and nothing has to be sent to them
/// that would work as a credential for longer than that link does.
/// </remarks>
public sealed record AdminUserCreateRequest(string Email, string DisplayName);

public sealed class AdminUserCreateRequestValidator : AbstractValidator<AdminUserCreateRequest>
{
    public AdminUserCreateRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("A name is required.")
            .MaximumLength(100);
    }
}

/// <summary>
/// The account just opened, and how its owner is going to choose a password.
/// </summary>
/// <param name="User">The account, as the register shows it.</param>
/// <param name="InvitationSent">Whether the invitation went out by mail.</param>
/// <param name="SetPasswordUrl">
/// The link to hand over in person, present only when the invitation could not be mailed. It is
/// the same single-use, time-limited link the message would have carried, and this answer is the
/// only place it ever appears: the token inside it is not stored anywhere it could be read back.
/// </param>
/// <param name="ExpiresHours">How long the link stays good.</param>
public sealed record AdminUserCreatedDto(
    AdminUserDto User, bool InvitationSent, string? SetPasswordUrl, int ExpiresHours);

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
            .WithSummary("Accounts of this installation, searched and paged (Read on the Users domain).");
        users.MapPost("/", CreateAsync)
            .WithValidation<AdminUserCreateRequest>()
            .WithSummary("Opens an account and invites its owner to choose a password (Create on the Users domain).");
        users.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One account (Read on the Users domain).");
        users.MapPost("/{id:guid}/invitation", ReinviteAsync)
            .WithSummary("Sends a fresh invitation to an account whose owner has not chosen a password yet (Write on the Users domain).");
        users.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<AdminUserUpdateRequest>()
            .WithSummary("Locks or unlocks an account (Write on the Users domain).");
        users.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an account (Delete on the Users domain).");
        users.MapPost("/{id:guid}/two-factor/reset", ResetTwoFactorAsync)
            .WithSummary("Switches every second factor of an account off and ends its sessions (Write on the Users domain).");

        return api;
    }

    /// <summary>The register, narrowed by what was typed and read a page at a time.</summary>
    /// <remarks>
    /// The search reads every name an account can be known by — the display name, the given and
    /// family names, the sign-in name and the address — accent-blind, because an operator looking
    /// for somebody types what they remember and the register is the one place where the address
    /// is a legitimate thing to search by.
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<AdminUserDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        string? q,
        int? page,
        int? pageSize,
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

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(u =>
                (u.Email != null && EF.Functions.ILike(u.Email, pattern))
                || (u.UserName != null && EF.Functions.ILike(u.UserName, pattern))
                || (u.DisplayName != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(u.DisplayName), EF.Functions.Unaccent(pattern)))
                || (u.FirstName != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(u.FirstName), EF.Functions.Unaccent(pattern)))
                || (u.LastName != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(u.LastName), EF.Functions.Unaccent(pattern))));
        }

        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var administrators = await FullAdministratorIdsAsync(db, ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);

        return TypedResults.Ok(await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .ToPagedAsync(pageNumber, size, u => ToDto(u, administrators, caverIds), ct));
    }

    /// <summary>
    /// Opens an account for somebody and sends them the link that lets them choose a password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Works whether or not the installation lets people register themselves: closed registration
    /// is exactly the installation where this is the only way in. The account is made as
    /// registration makes one — the same uniqueness of address, the same directory entry, the
    /// same default permission groups — so an invited member and a registered one cannot drift
    /// apart in what they start with.
    /// </para>
    /// <para>
    /// The link is the password-reset link, because that is already the mechanism by which an
    /// address proves it can set an account's password: single use, a day long, ended by any
    /// change to the account's credentials. When no mail server is configured the link comes back
    /// in this answer for the administrator to hand over, since an installation without mail
    /// would otherwise have accounts nobody can ever enter.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<AdminUserCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AdminUserCreateRequest request,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        IOptions<AuthOptions> options,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Users))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var email = request.Email.Trim();
        var user = new SilexGisUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            // The sign-in name starts out as the address, so an address somebody already uses as
            // their sign-in name is taken just as surely as one held as an address. Said plainly
            // here, unlike on the anonymous registration form: the register already lists every
            // address, so an administrator learns nothing from being told.
            return created.Has("DuplicateEmail") || created.Has("DuplicateUserName")
                ? ApiProblems.Conflict("user.email_taken", "An account with this address already exists.")
                : IdentityProblems.From(created, "user.invalid");
        }

        await PermissionGroupSeeder.EnsureMembershipsAsync(
            db, user.Id, options.Value.DefaultPermissionGroupSlugs, ct);
        CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/users/{user.Id}",
            await InviteAsync(user, db, userManager, dispatcher, configuration, ct));
    }

    /// <summary>
    /// Sends a new invitation to an account nobody has entered yet — the way out when the first
    /// link ran out, went to a spam folder or was never handed over.
    /// </summary>
    /// <remarks>
    /// Refused for any account that can already be signed in to, by a password or through an
    /// external provider. For those, a link that sets the password is not an invitation but a way
    /// into somebody else's account, and on an installation without mail it would be handed
    /// straight to the administrator. Resetting a password an owner has chosen stays with the
    /// owner, through their own mailbox.
    /// </remarks>
    private static async Task<Results<Ok<AdminUserCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReinviteAsync(
        Guid id,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
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

        if (user.PasswordHash is not null
            || await db.UserLogins.AnyAsync(l => l.UserId == id, ct)
            || string.IsNullOrEmpty(user.Email))
        {
            return ApiProblems.Conflict(
                "user.already_enterable",
                "This account can already be signed in to; its owner resets their own password.");
        }

        return TypedResults.Ok(await InviteAsync(user, db, userManager, dispatcher, configuration, ct));
    }

    /// <summary>Mints the set-password link, mails it, and hands it back only if the mail did not go.</summary>
    private static async Task<AdminUserCreatedDto> InviteAsync(
        SilexGisUser user,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        IMessageDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var sent = await AccountMessages.SendAccountInvitationAsync(dispatcher, configuration, user, token, ct);

        // "Sent" alone is not delivery: with no mail server configured the dispatcher writes the
        // message to the log and reports success, and the person would wait for nothing.
        var invitationSent = sent.Sent && sent.ChannelConfigured;

        var administrators = await FullAdministratorIdsAsync(db, ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);
        return new AdminUserCreatedDto(
            ToDto(user, administrators, caverIds),
            invitationSent,
            invitationSent ? null : AccountMessages.InvitationUrl(configuration, user.Email!, token),
            AuthenticationSetup.EmailLinkLifetimeHours);
    }

    private static async Task<Results<Ok<AdminUserDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
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
        var dto = ToDto(user, administrators, caverIds);
        return TypedResults.Ok(new AdminUserDetailDto(
            dto.Id,
            dto.Label,
            dto.Email,
            dto.EmailConfirmed,
            dto.IsLockedOut,
            dto.LockoutEnd,
            dto.IsFullAdministrator,
            dto.HasPassword,
            dto.TwoFactorEnabled,
            dto.CaverId,
            dto.CreatedAt,
            await PermissionGroupsOfAsync(db, id, ct)));
    }

    /// <summary>
    /// The permission groups an account sits in, each with the paths that put it there.
    /// </summary>
    /// <remarks>
    /// The caving groups and the membership rows are read through the resolver that computes the
    /// account's rights, so the list cannot name a group the account does not actually hold, or
    /// miss one it does.
    /// </remarks>
    private static async Task<IReadOnlyList<AdminUserPermissionGroupDto>> PermissionGroupsOfAsync(
        SilexGisDbContext db, Guid id, CancellationToken ct)
    {
        var cavingGroupIds = (await AccessContextResolver.ResolveAsync(db, id, ct)).CavingGroupIds.ToArray();

        var rows = await (
            from member in AccessContextResolver.MembershipRowsOf(db, id, cavingGroupIds)
            join permissionGroup in db.PermissionGroups.AsNoTracking()
                on member.PermissionGroupId equals permissionGroup.Id
            select new { permissionGroup.Id, permissionGroup.Name, member.MemberKind, member.MemberId })
            .ToListAsync(ct);

        var cavingGroupNames = await db.CavingGroups.AsNoTracking()
            .Where(g => cavingGroupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return [.. rows
            .GroupBy(r => (r.Id, r.Name))
            .OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AdminUserPermissionGroupDto(
                g.Key.Id,
                g.Key.Name,
                g.Any(r => r.MemberKind == AccessSubjectKind.User),
                [.. g.Where(r => r.MemberKind == AccessSubjectKind.CavingGroup)
                    .Select(r => new AdminUserCavingGroupRefDto(
                        r.MemberId, cavingGroupNames.GetValueOrDefault(r.MemberId, string.Empty)))
                    .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)]))];
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

    /// <summary>
    /// Switches off every second factor an account has, for a member who has lost both their
    /// authenticator and their recovery codes and would otherwise be shut out for good.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What each method held goes with it — the authenticator key is replaced and the recovery
    /// codes are removed — through the same switch the account holder's own "turn it all off"
    /// uses, so the two cannot disagree about what "off" leaves behind. Every session the account
    /// holds is ended, as a password change ends them, because whoever was asked to do this could
    /// have been asked by somebody who is not the owner; and the owner is told by the same security
    /// alert that switching the last method off always sends. The account row is not recorded
    /// field by field, so the act is written to the audit trail by hand. All of it commits
    /// together or not at all.
    /// </para>
    /// <para>
    /// Three refusals. The caller's own account, which they manage from their security settings
    /// where switching a factor off is one deliberate act rather than an administrative one. An
    /// account with nothing on, which has nothing to reset. And a full administrator's account
    /// when the caller is not one: a second factor is what stands between a password and the
    /// highest seat in the installation, and a delegated right over accounts must not be a way of
    /// taking it down.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<AdminUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> ResetTwoFactorAsync(
        Guid id,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        IOpenIddictTokenManager issuedTokens,
        IOpenIddictAuthorizationManager grants,
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

        if (user.Id == ctx.UserId)
        {
            return ApiProblems.BadRequest(
                "user.cannot_reset_own_two_factor",
                "You manage your own second factor from your security settings.");
        }

        // Asked of the account's resolved rights rather than of the register's administrator
        // column, which leaves locked administrators out: a locked full administrator is still one
        // the moment the lock is lifted.
        if (!ctx.IsFullAdmin && (await AccessContextResolver.ResolveAsync(db, id, ct)).IsFullAdmin)
        {
            return ApiProblems.Forbidden(AccessEntryRules.ExceedsOwnRightsCode);
        }

        var before = user.ToTwoFactorState();
        if (!TwoFactorPolicy.AnyEnabled(before))
        {
            return ApiProblems.Conflict(
                "user.two_factor_not_enabled",
                "This account has no second factor switched on, so there is nothing to reset.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Staged first so that the switch's own save writes it, in the same transaction. Only the
        // three switches are named, never anything they held.
        db.AuditEntries.Add(new AuditEntry
        {
            UserId = ctx.UserId,
            Action = AuditActions.TwoFactorReset,
            EntityType = nameof(SilexGisUser),
            EntityId = user.Id.ToString(),
            Changes = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, object?>>
            {
                [nameof(SilexGisUser.TwoFactorAuthenticatorEnabled)] = Switched(before.AuthenticatorEnabled),
                [nameof(SilexGisUser.TwoFactorEmailEnabled)] = Switched(before.EmailEnabled),
                [nameof(SilexGisUser.TwoFactorSmsEnabled)] = Switched(before.SmsEnabled),
            }),
        });

        await TwoFactorSwitch.SwitchAllOffAsync(user, userManager, db, ct);
        await SessionRevocation.EndAllSessionsAsync(issuedTokens, grants, user.Id, ct);
        await transaction.CommitAsync(ct);

        var administrators = await FullAdministratorIdsAsync(db, ct);
        var caverIds = await CaverIdsByUserAsync(db, ct);
        return TypedResults.Ok(ToDto(user, administrators, caverIds));

        static Dictionary<string, object?> Switched(bool was) => new() { ["old"] = was, ["new"] = false };
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
            user.PasswordHash is not null,
            TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState()),
            caverIds.TryGetValue(user.Id, out var caverId) ? caverId : null,
            user.CreatedAt);
}
