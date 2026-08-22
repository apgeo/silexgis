// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Notifications;

/// <summary>What clicking one opt-out link switches off.</summary>
public enum UnsubscribeKind : short
{
    /// <summary>One category of notification, named by the token. Stored — do not renumber.</summary>
    Category = 0,

    /// <summary>
    /// The daily summary itself, which has no category of its own: it collects every category the
    /// recipient still hears about, so a token naming one of them would switch off something the
    /// reader never asked about. Stored — do not renumber.
    /// </summary>
    DailyDigest = 1,
}

/// <summary>The account an opt-out link belongs to, and what it switches off.</summary>
/// <param name="UserId">Whose settings the link changes.</param>
/// <param name="Kind">Which of the two meanings the link carries.</param>
/// <param name="Category">
/// Meaningful only when <paramref name="Kind"/> is <see cref="UnsubscribeKind.Category"/>.
/// </param>
public readonly record struct UnsubscribeSubject(
    Guid UserId, UnsubscribeKind Kind, NotificationCategory Category);

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
/// A token grants exactly one action — switching one thing off for one account — which is why the
/// endpoint consuming it can be anonymous. A mail client opening a link has no session, and
/// demanding one would make the link useless to the person who most wants it.
/// </para>
/// </remarks>
public interface IUnsubscribeTokens
{
    /// <summary>A link that stops one category of notification.</summary>
    string CreateForCategory(Guid userId, NotificationCategory category);

    /// <summary>
    /// A link that stops the daily summary. A summary is not a category, so it cannot be switched
    /// off as one, and returning the account to one message per event would send more mail than
    /// the link was clicked to stop.
    /// </summary>
    string CreateForDigest(Guid userId);

    /// <summary>False for anything expired, edited, or invented.</summary>
    bool TryRead(string token, out UnsubscribeSubject subject);
}
