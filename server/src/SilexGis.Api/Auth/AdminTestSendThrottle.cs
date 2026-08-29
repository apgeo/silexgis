// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain.Auth;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// Bounds how often an operator can make the installation send a test text.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint's request budget is not the bound. It is keyed on the caller's address, which is
/// the right shape for credential guessing and the wrong shape for this: it is shared with the
/// sign-in routes, it refills every minute, a second replica of the application keeps a second
/// copy of it, and every call this route lets through sends a text the operator pays for.
/// </para>
/// <para>
/// The marker lives in Identity's own user-token table, so no schema exists for it — and nothing
/// the caller can do clears it. Written as a service rather than a static helper so the endpoint
/// that uses it asks for a cooldown rather than reaching into the account store itself.
/// </para>
/// </remarks>
public sealed class AdminTestSendThrottle(UserManager<SilexGisUser> userManager)
{
    /// <summary>Whether this operator sent a test text too recently to send another.</summary>
    public async Task<bool> TooSoonAsync(Guid operatorId)
    {
        var account = await userManager.FindByIdAsync(operatorId.ToString());
        return account is not null
            && await SendThrottle.TooSoonAsync(
                userManager, account, TwoFactorMethod.Sms, SendThrottle.AdminTestInterval, SendThrottle.AdminTest);
    }

    /// <summary>
    /// Records that a test text was attempted. Stamped before the attempt rather than after: a
    /// gateway that accepts the text and then reports a failure has still sent it, and an operator
    /// retrying a "failure" in a loop is exactly the case this bounds.
    /// </summary>
    public async Task MarkSentAsync(Guid operatorId)
    {
        var account = await userManager.FindByIdAsync(operatorId.ToString());
        if (account is not null)
        {
            await SendThrottle.MarkSentAsync(userManager, account, TwoFactorMethod.Sms, SendThrottle.AdminTest);
        }
    }
}
