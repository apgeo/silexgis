// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>What a request to lay down the development dataset did, so a caller can say why.</summary>
public enum SpeleoLocDevSeedOutcome
{
    /// <summary>The dataset is present: it was written, or it already was.</summary>
    Seeded,

    /// <summary>
    /// The host is not in development and no operator asked for it. Nothing was read or written.
    /// </summary>
    NotPermitted,

    /// <summary>
    /// The installation has no bootstrap administrator, so there is no first party for the
    /// second one to be told apart from, and nothing this dataset could demonstrate.
    /// </summary>
    NoAdministrator,
}

/// <summary>
/// Development data that makes the location-protection rules observable at all.
/// </summary>
/// <remarks>
/// The demo dataset alone cannot demonstrate them. Every object it creates is owned by the
/// bootstrap administrator, who is both the owner and a full administrator, so the two
/// short-circuits in the exact-location rule — "is a full administrator" and "owns the row" —
/// are both satisfied for the only account a stock installation has. A protected cave
/// therefore shows its exact position to every signed-in caller, and no test, no human and no
/// client developer can see what protection actually does.
/// <para>
/// What this adds is the missing second party: a caving group, a plain account that owns
/// nothing and administers nothing, both the administrator and that account in the group, and
/// the group stamped onto the demo cave that was created group-visible while belonging to no
/// group — a row that until now no caller at all could reach through the group rule.
/// </para>
/// <para>
/// The plain account deliberately holds the Viewer role, which maps to no permission group and
/// therefore to no ruleset. That is the point: a role that reads past visibility would make
/// every "this account cannot see it" demonstration pass for the wrong reason.
/// </para>
/// <para>
/// Idempotent — every section guards itself, so running it twice changes nothing and running it
/// on a database seeded by an older build tops up what that build did not write.
/// </para>
/// </remarks>
public static class SpeleoLocDevSeeder
{
    /// <summary>The caving group the demo administrator and the plain account share.</summary>
    public const string GroupName = "Demo Caving Club";

    public const string GroupSlug = "demo-caving-club";

    /// <summary>
    /// The plain account's address. Fixed, because a client developer has to be able to sign in
    /// with it without first being told what it was set to.
    /// </summary>
    public const string MemberEmail = "member@dev.local";

    /// <summary>
    /// The password that account gets when nothing names another one. It is written down in the
    /// installation guide, so it is a password only in the sense that the login form asks for
    /// one — which is why nothing may create this account outside a development posture, and
    /// why an installation that wants the dataset anyway can name its own value instead.
    /// </summary>
    public const string DefaultMemberPassword = "dev-member-pass-1";

    /// <summary>Configuration key naming a password to use instead of the default one.</summary>
    public const string MemberPasswordKey = "SpeleoLocDev:MemberPassword";

    /// <summary>
    /// Configuration key that permits the dataset outside a Development host. Nothing else does:
    /// the command is otherwise refused, whatever arguments it is given.
    /// </summary>
    public const string AllowKey = "SpeleoLocDev:Allow";

    public const string MemberDisplayName = "Demo Member";

    /// <summary>
    /// The demo cave that was created visible-to-a-caving-group without ever naming one. Until
    /// a group is stamped on it the group rule cannot match it, because that rule requires a
    /// non-null group id on both sides.
    /// </summary>
    public const string GroupVisibleCaveCode = "DEMO-0006";

    /// <summary>
    /// Seeds the group, the plain account and the memberships.
    /// </summary>
    /// <remarks>
    /// Refused outright unless the host is in Development or an operator has said in
    /// configuration that this installation wants it. Unlike the demonstration dataset, this one
    /// mints a login — an account whose password is printed in a public installation guide — so
    /// the guidance not to run it on an installation people use cannot be left as a sentence of
    /// prose beside the command. The command is on the same page as `seed-demo`, which is safe,
    /// and the difference between them is invisible at the moment somebody pastes it.
    /// </remarks>
    public static async Task<SpeleoLocDevSeedOutcome> SeedAsync(
        IServiceProvider services, CancellationToken ct = default)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!environment.IsDevelopment() && !configuration.GetValue(AllowKey, false))
        {
            return SpeleoLocDevSeedOutcome.NotPermitted;
        }

        var db = services.GetRequiredService<SilexGisDbContext>();
        var userManager = services.GetRequiredService<UserManager<SilexGisUser>>();

        var admins = await userManager.GetUsersInRoleAsync(GlobalRoles.Admin);
        if (admins.Count == 0)
        {
            return SpeleoLocDevSeedOutcome.NoAdministrator;
        }

        var password = configuration[MemberPasswordKey] is { Length: > 0 } chosen
            ? chosen
            : DefaultMemberPassword;

        var admin = admins[0];
        var group = await EnsureGroupAsync(db, admin.Id, ct);
        var memberUserId = await EnsureMemberAccountAsync(db, userManager, password, ct);

        await EnsureMembershipAsync(db, group.Id, admin.Id, CavingGroupRole.Owner, ct);
        await EnsureMembershipAsync(db, group.Id, memberUserId, CavingGroupRole.Member, ct);
        await BindGroupVisibleCaveAsync(db, group.Id, ct);

        await db.SaveChangesAsync(ct);
        return SpeleoLocDevSeedOutcome.Seeded;
    }

    private static async Task<CavingGroup> EnsureGroupAsync(
        SilexGisDbContext db, Guid adminUserId, CancellationToken ct)
    {
        var existing = await db.CavingGroups.FirstOrDefaultAsync(g => g.Slug == GroupSlug, ct);
        if (existing is not null)
        {
            return existing;
        }

        var group = new CavingGroup
        {
            Name = GroupName,
            Slug = GroupSlug,
            Type = CavingGroupType.CavingClub,
            Description = "Development caving group: the second party that makes protection visible.",
        };
        db.CavingGroups.Add(group);

        // The same default permission list a group created through the application gets, so the
        // demonstration is of the real thing rather than of a shape the application never makes.
        // Staged only on this branch: the helper appends a suffix rather than reusing a row when
        // a name collides, so calling it for a group that already exists would quietly make a
        // second pair of permission groups.
        await CavingGroupPermissionSeeder.SeedForNewGroupAsync(db, group, adminUserId, ct);
        return group;
    }

    private static async Task<Guid> EnsureMemberAccountAsync(
        SilexGisDbContext db, UserManager<SilexGisUser> userManager, string password, CancellationToken ct)
    {
        if (await userManager.FindByEmailAsync(MemberEmail) is { } existing)
        {
            return existing.Id;
        }

        var member = new SilexGisUser
        {
            UserName = MemberEmail,
            Email = MemberEmail,
            EmailConfirmed = true,
            DisplayName = MemberDisplayName,
        };

        var result = await userManager.CreateAsync(member, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The development member account could not be created: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        _ = await userManager.AddToRoleAsync(member, GlobalRoles.Viewer);

        // Viewer maps to no permission group on purpose — see the note on this class. Nothing is
        // called to give the account one.
        CaverDirectory.CreateForNewAccount(db, member.Id, member.DisplayName, member.UserName, member.Email);
        await db.SaveChangesAsync(ct);
        return member.Id;
    }

    /// <summary>
    /// Puts an account's person in the group. Membership hangs off the roster entry rather than
    /// the account, so an account without one contributes nothing to access and has to be
    /// repaired before it can be enrolled.
    /// </summary>
    private static async Task EnsureMembershipAsync(
        SilexGisDbContext db, Guid groupId, Guid userId, CavingGroupRole role, CancellationToken ct)
    {
        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? db.Cavers.Local.FirstOrDefault(c => c.UserId == userId);
        if (caver is null)
        {
            return;
        }

        var alreadyIn = await db.CavingGroupMemberships
            .AnyAsync(m => m.CavingGroupId == groupId && m.CaverId == caver.Id, ct);
        if (alreadyIn)
        {
            return;
        }

        db.CavingGroupMemberships.Add(new CavingGroupMembership
        {
            CavingGroupId = groupId,
            CaverId = caver.Id,
            Role = role,
        });
    }

    private static async Task BindGroupVisibleCaveAsync(SilexGisDbContext db, Guid groupId, CancellationToken ct)
    {
        var caveId = await db.Caves
            .Where(c => c.IdentificationCode == GroupVisibleCaveCode)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (caveId is null)
        {
            return;
        }

        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId.Value, ct);
        if (feature is not null && feature.CavingGroupId is null)
        {
            feature.CavingGroupId = groupId;
        }
    }
}
