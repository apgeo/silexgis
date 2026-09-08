// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>
/// Seeds the three demo accounts a test installation announces on its sign-in page: a full
/// administrator, an editor and a viewer. Runs at every start and does nothing unless
/// <see cref="TestLoginOptions.Enabled"/> is set.
/// </summary>
/// <remarks>
/// The three are chosen to make the permission model observable to a stranger trying the
/// application out: the administrator short-circuits every check, the editor holds the seeded
/// Editors ruleset (create and edit content, but no user or permission administration, and no
/// right to exact protected positions), and the viewer holds nothing beyond the implicit
/// All Users baseline, so it sees only what visibility alone admits.
/// <para>
/// Idempotent, and it reconciles rather than only creates: a password that no longer matches
/// the configured one is reset, because the sign-in page prints the configured value and a
/// printed credential that does not work is worse than none. Lockout is disabled on these
/// accounts — their password is public, so anybody could lock them at will by failing it,
/// and a locked demo account is a broken demo rather than a protected one.
/// </para>
/// </remarks>
public static class TestLoginSeeder
{
    /// <summary>One demo account: who it is, the legacy role it holds, and the stable
    /// lowercase kind the sign-in page uses to label and describe it.</summary>
    public sealed record TestAccount(string Email, string DisplayName, string Role, string Kind);

    public static readonly IReadOnlyList<TestAccount> Accounts =
    [
        new("admin@test.local", "Test Administrator", GlobalRoles.Admin, "administrator"),
        new("editor@test.local", "Test Editor", GlobalRoles.Editor, "editor"),
        new("viewer@test.local", "Test Viewer", GlobalRoles.Viewer, "viewer"),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<TestLoginOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        var db = services.GetRequiredService<SilexGisDbContext>();
        var userManager = services.GetRequiredService<UserManager<SilexGisUser>>();
        foreach (var account in Accounts)
        {
            await EnsureAccountAsync(db, userManager, account, options.Password, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAccountAsync(
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        TestAccount account,
        string password,
        CancellationToken ct)
    {
        var existing = await userManager.FindByEmailAsync(account.Email);
        if (existing is null)
        {
            var user = new SilexGisUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                DisplayName = account.DisplayName,
            };

            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Test login {account.Email} could not be created: "
                    + string.Join("; ", created.Errors.Select(e => e.Description)));
            }

            // After CreateAsync, not in the initializer: CreateAsync itself stamps
            // LockoutEnabled = true on every new user (Lockout.AllowedForNewUsers), so a value
            // set before it is silently overwritten.
            _ = await userManager.SetLockoutEnabledAsync(user, false);
            _ = await userManager.AddToRoleAsync(user, account.Role);
            await PermissionGroupSeeder.EnsureRoleMembershipsAsync(db, user.Id, account.Role, ct);
            CaverDirectory.CreateForNewAccount(db, user.Id, user.DisplayName, user.UserName, user.Email);
            return;
        }

        if (!await userManager.CheckPasswordAsync(existing, password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            var reset = await userManager.ResetPasswordAsync(existing, token, password);
            if (!reset.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Test login {account.Email} could not be brought to the configured password: "
                    + string.Join("; ", reset.Errors.Select(e => e.Description)));
            }
        }

        if (existing.LockoutEnabled)
        {
            _ = await userManager.SetLockoutEnabledAsync(existing, false);
        }

        // Top up memberships an account created by an older build may not have.
        await PermissionGroupSeeder.EnsureRoleMembershipsAsync(db, existing.Id, account.Role, ct);
    }
}
