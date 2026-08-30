// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Where the installation lives, and the paths a message can point somebody at.
/// </summary>
/// <remarks>
/// A producer writes the path to the thing it is reporting, because a feature slice knows its own
/// routes and has no business knowing where the installation is deployed. Only this layer knows
/// that, so a path becomes a whole address here — and it is one place rather than several because
/// two answers to "where does this installation live" would eventually differ, and the one that
/// weighs what a message costs has to agree character for character with the one that sends it.
/// </remarks>
public static class NotificationLinks
{
    /// <summary>
    /// Where an announcement is read. The inbox, because that is the one page an announcement can
    /// be read on: the group's own page is where one is written, not where one arrives, and a text
    /// message carries nothing but this link — so a link that landed anywhere else would be the
    /// whole message failing to keep its promise.
    /// </summary>
    public const string Inbox = "/notifications";

    private const string LocalInstallation = "http://localhost:8080";

    /// <summary>The address this installation answers on, without a trailing separator.</summary>
    public static string SiteUrl(IConfiguration configuration) =>
        (configuration.GetValue("PublicUrl", LocalInstallation) ?? LocalInstallation).TrimEnd('/');

    /// <summary>
    /// One of this installation's own paths as a whole address. Anything that is already a whole
    /// address is left exactly as it is, so a value that came in complete is not mangled into one
    /// that is not.
    /// </summary>
    public static string Absolute(IConfiguration configuration, string url) =>
        url.StartsWith('/') ? SiteUrl(configuration) + url : url;
}
