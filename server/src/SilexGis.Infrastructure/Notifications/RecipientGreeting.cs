// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>How a message addresses the person it was written for.</summary>
/// <remarks>
/// One home rather than one per caller, because the obvious fallback is wrong in a way nobody
/// notices: an account created by self-registration has its user name set to its email address,
/// so "display name, or else user name" greets most people by their email address. Never the
/// email address — it is the one thing the profile rules may be hiding.
/// </remarks>
internal static class RecipientGreeting
{
    internal static string For(SilexGisUser user) =>
        !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName!
        : !string.IsNullOrWhiteSpace(user.FirstName) ? user.FirstName!
        : "there";
}
