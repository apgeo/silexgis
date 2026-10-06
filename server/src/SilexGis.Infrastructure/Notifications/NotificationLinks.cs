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

    /// <summary>
    /// What a notification that somebody asked on a trip cannot open a cave records the
    /// account of that person under, beside the values its wording uses.
    /// </summary>
    /// <remarks>
    /// An identifier, not a word. It is kept so that where the notification points can be
    /// rebuilt later from what it is about, and it is deliberately on no template's list of
    /// names a wording may use. A wording that uses a name off its list is refused when it is
    /// saved, so no message — shipped, or rewritten by an operator — prints it.
    /// </remarks>
    public const string InviteeAccount = "inviteeUserId";

    /// <summary>
    /// A cave's permissions dialog, opened about one account: where somebody told that a person
    /// cannot open the cave is sent to change that.
    /// </summary>
    /// <remarks>
    /// One home because two things build it and have to agree: the producer, for the copy that
    /// leaves by mail, and the inbox, which rebuilds where a notification points from what it
    /// is about rather than trusting a path frozen when the notification was queued. The page
    /// treats the account as a suggestion and nothing more — it looks the account up under its
    /// reader's own rights, and what is granted is whatever that reader then saves.
    /// </remarks>
    public static string CavePermissionsAbout(Guid caveId, Guid accountId) =>
        $"/caves/{caveId}?permissions=1&grantTo={accountId}";
}
