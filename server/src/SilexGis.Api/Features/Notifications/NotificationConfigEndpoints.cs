// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;

namespace SilexGis.Api.Features.Notifications;

/// <summary>What this installation has decided about how notifications reach a signed-in page.</summary>
/// <param name="BadgeTransport">
/// How the header's unread count learns that it has changed. <c>poll</c> — the only transport
/// implemented today — means the page asks again on a timer. Any other name means the page does
/// not run a timer at all, and the count moves only when the reader acts or returns to the tab.
/// </param>
public sealed record NotificationConfigDto(string BadgeTransport);

/// <summary>
/// The settings a signed-in page needs in order to decide how it keeps its notification state
/// current.
/// </summary>
/// <remarks>
/// This is deliberately not part of the inbox listing. The listing answers what happened; this
/// answers how the page should watch for the next thing, which is an installation-wide choice an
/// operator makes and not a property of anybody's notifications. It follows the per-feature
/// <c>/config</c> shape the upload and map surfaces already use.
/// </remarks>
public static class NotificationConfigEndpoints
{
    /// <summary>
    /// Transports this server will hand out. Only <c>poll</c> is implemented; <c>sse</c> is
    /// reserved for a server-pushed stream and is accepted so the setting has somewhere to go
    /// before that exists. A name that is not one of these is a typo rather than a choice, and is
    /// answered as <c>poll</c> so a mistyped setting cannot leave the count with no way to move.
    /// </summary>
    private static readonly string[] Known = ["poll", "sse"];

    internal const string Default = "poll";

    public static RouteGroupBuilder MapNotificationConfigEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/notifications").WithTags("Notifications")
            .MapGet("/config", Config)
            .WithSummary("How this installation expects a page to keep its unread count current.");

        return api;
    }

    private static Ok<NotificationConfigDto> Config(IConfiguration configuration, ILoggerFactory loggers)
    {
        var configured = (configuration["Notifications:BadgeTransport"] ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (configured.Length == 0)
        {
            return TypedResults.Ok(new NotificationConfigDto(Default));
        }

        if (!Known.Contains(configured))
        {
            loggers.CreateLogger(typeof(NotificationConfigEndpoints))
                .LogWarning(
                    "Notifications:BadgeTransport is set to '{Configured}', which is not a transport this "
                    + "server knows; falling back to '{Default}'.",
                    configured,
                    Default);
            return TypedResults.Ok(new NotificationConfigDto(Default));
        }

        return TypedResults.Ok(new NotificationConfigDto(configured));
    }
}
