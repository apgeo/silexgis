// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// Mints and reads the one-click opt-out links that notifications carry.
/// </summary>
/// <remarks>
/// <para>
/// A seam because the two halves live in different layers: the sender is a background service in
/// Infrastructure, while signing is done with the web host's data-protection key ring, which only
/// the API project has. Same shape as the file-delivery tokens.
/// </para>
/// <para>
/// A token grants exactly one action — switching one category off for one account — which is why
/// the endpoint consuming it can be anonymous. A mail client opening a link has no session, and
/// demanding one would make the link useless to the person who most wants it.
/// </para>
/// </remarks>
public interface IUnsubscribeTokens
{
    string Create(Guid userId, NotificationCategory category);

    /// <summary>False for anything expired, edited, or invented.</summary>
    bool TryRead(string token, out Guid userId, out NotificationCategory category);
}
