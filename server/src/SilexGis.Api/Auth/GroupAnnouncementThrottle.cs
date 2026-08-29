// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Auth;

/// <summary>
/// How often one account may make the installation write to a whole caving group.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint's request budget is not the bound. It is keyed on the caller's address, it refills
/// every minute, and <b>a second replica of the application keeps a second copy of it</b> — so the
/// limit it appears to impose is really that limit times however many replicas are running. Every
/// call it lets through here writes one row per member of a roster and may cost the operator money
/// on the way out, which is exactly the shape a per-process window is wrong for.
/// </para>
/// <para>
/// The marker lives in the account store's own token table, so no schema exists for it and nothing
/// the caller can do clears it.
/// </para>
/// <para>
/// Written as a service rather than a static helper so the endpoint asks for a cooldown rather than
/// reaching into the account store itself.
/// </para>
/// </remarks>
public sealed class GroupAnnouncementThrottle(UserManager<SilexGisUser> userManager)
{
    public async Task<bool> TooSoonAsync(Guid senderId)
    {
        var account = await userManager.FindByIdAsync(senderId.ToString());

        // No account, no cooldown to be too soon for. The request cannot get this far without one,
        // and refusing here would turn a missing row into a permanent block on a person who is
        // signed in.
        return account is not null
            && await SendThrottle.TooSoonAsync(
                userManager, account, SendThrottle.GroupAnnouncementInterval, SendThrottle.GroupAnnouncement);
    }

    /// <summary>
    /// Stamped before the fan-out rather than after. A save that reports a failure may still have
    /// committed, and somebody retrying a "failure" in a loop is exactly the case this bounds.
    /// </summary>
    public async Task MarkSentAsync(Guid senderId)
    {
        var account = await userManager.FindByIdAsync(senderId.ToString());
        if (account is not null)
        {
            await SendThrottle.MarkSentAsync(userManager, account, SendThrottle.GroupAnnouncement);
        }
    }
}
