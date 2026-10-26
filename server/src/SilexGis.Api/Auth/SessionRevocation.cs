// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>
/// Ends every session an account holds that could outlive the credential it was obtained with.
/// </summary>
/// <remarks>
/// Changing or resetting a password is the one action available to someone whose device has been
/// lost or whose account has been taken, and until this existed it ended nothing: an access token
/// carries no security stamp to compare against, and the token exchange looks the account up by
/// subject without asking when the credential behind it was last changed. So a credential handed to
/// a device outlived the password it was obtained with for as long as its refresh window ran —
/// weeks, on a client that keeps one across time offline.
/// <para>
/// The rule is deliberately account-wide rather than confined to one client or one device: the
/// person acting cannot see what sessions exist, cannot name the device that was taken, and is
/// acting precisely because they no longer control it. Signing back in on the devices that were
/// never lost is a small cost paid on an action nobody performs casually.
/// </para>
/// <para>
/// Both stores are cleared. Tokens are what a client presents; an authorization is the grant they
/// hang off, and one left valid is a standing permission to be issued more.
/// </para>
/// <para>
/// One thing this deliberately does not stop, and the reason to say so here rather than let a
/// reader assume otherwise: an <em>access</em> token already in a client's hands keeps working
/// until it expires — at most fifteen minutes. Access tokens are self-contained signed JWTs
/// checked against their signature, not looked up in the store this empties. Closing that window
/// means validating every API request against the token entry, which is a database read on every
/// call; the fifteen minutes is accepted instead. What the caller is protected from is the part
/// that lasts: no new access token can be minted, because the refresh tokens are revoked here and
/// the browser session that could authorize a fresh one is refused by the rotated security stamp.
/// </para>
/// <para>
/// Personal access keys are revoked here when the caller asks for it, and then have no such window:
/// a key is looked up on every request, so the stamp written here refuses it on the device's very
/// next call. Whether they go is the account holder's choice on a password change or reset, and
/// off unless chosen: a caver changes a password for many reasons besides a lost phone, and a
/// key quietly withdrawn from a phone that was never lost stops its uploads in the field with
/// nothing on the phone to say why. Someone who has lost a device ticks the box, or withdraws
/// that one key from the keys page. An administrator's reset of an account's second factor keeps
/// revoking them unconditionally — it is the recovery path for an account somebody else may hold.
/// Each key is stamped through a tracked save so that its revocation is in the change history
/// like one made by hand.
/// </para>
/// </remarks>
internal static class SessionRevocation
{
    public static async Task EndAllSessionsAsync(
        IOpenIddictTokenManager tokens,
        IOpenIddictAuthorizationManager authorizations,
        SilexGisDbContext db,
        DateTimeOffset now,
        Guid accountId,
        bool revokeAccessKeys,
        CancellationToken ct)
    {
        var subject = accountId.ToString();

        await tokens.RevokeBySubjectAsync(subject, ct);
        await authorizations.RevokeBySubjectAsync(subject, ct);
        if (!revokeAccessKeys)
        {
            return;
        }

        var keys = await db.ApiAccessKeys
            .Where(k => k.UserId == accountId && k.RevokedAt == null)
            .ToListAsync(ct);
        if (keys.Count > 0)
        {
            foreach (var key in keys)
            {
                key.RevokedAt = now;
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
