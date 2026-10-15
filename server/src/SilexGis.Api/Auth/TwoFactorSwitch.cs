// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>
/// The one place that turns an account's second factor off and keeps Identity's own flag in step
/// with the three methods, shared by the account holder's settings, the phone-number removal that
/// takes the texted code with it, and an administrator's reset.
/// </summary>
internal static class TwoFactorSwitch
{
    /// <summary>
    /// Keeps Identity's single flag equal to "any method is on". Everything that makes a sign-in
    /// stop and ask for a second factor reads that flag, so it can never drift from these three.
    /// Saves whatever the caller has staged on the context along with it.
    /// </summary>
    public static async Task SyncMasterSwitchAsync(
        SilexGisUser user, UserManager<SilexGisUser> userManager, SilexGisDbContext db, CancellationToken ct)
    {
        var any = TwoFactorPolicy.AnyEnabled(user.ToTwoFactorState());
        if (user.TwoFactorEnabled != any)
        {
            // Only the transition to "nothing left" is worth a warning: switching one method off
            // while another remains has not weakened the account. Every enable and disable path
            // passes through here, so this one place covers them all.
            if (!any)
            {
                NotificationQueue.Enqueue(
                    db, user.Id, NotificationCategory.SecurityAlerts,
                    MessageTemplateCatalog.NotifySecurityTwoFactorDisabled,
                    new Dictionary<string, string>());
            }

            await userManager.SetTwoFactorEnabledAsync(user, any);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Switches every method off and removes what each one held: the authenticator's shared key is
    /// replaced, so an app that still shows codes produces ones nothing accepts, and the recovery
    /// codes are gone, so a written-down set cannot be redeemed later. Setting it up again starts
    /// from nothing, with a new key and a new set.
    /// </summary>
    public static async Task SwitchAllOffAsync(
        SilexGisUser user, UserManager<SilexGisUser> userManager, SilexGisDbContext db, CancellationToken ct)
    {
        user.TwoFactorAuthenticatorEnabled = false;
        user.TwoFactorEmailEnabled = false;
        user.TwoFactorSmsEnabled = false;
        user.PreferredTwoFactorMethod = null;

        // Replacing the set with zero new codes is how Identity's public surface empties it; the
        // store keeps the codes under a name of its own that nothing outside it should spell out.
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await SyncMasterSwitchAsync(user, userManager, db, ct);
    }
}
