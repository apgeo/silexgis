// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;

namespace SilexGis.Api.Features.Notifications;

/// <summary>What a signed-in page needs to know about how notifications reach it.</summary>
/// <param name="BadgeTransport">
/// How the header's unread count learns that it has changed. <c>poll</c> — the only transport this
/// build implements — means the page asks again on a timer.
/// </param>
public sealed record NotificationConfigDto(string BadgeTransport);

/// <summary>
/// The settings a signed-in page needs in order to decide how it keeps its notification state
/// current.
/// </summary>
/// <remarks>
/// This is deliberately not part of the inbox listing. The listing answers what happened; this
/// answers how the page should watch for the next thing. The answer states what this build can do
/// rather than an operator's choice: a name for a transport that does not exist could only ever be
/// selected by mistake, and selecting it left the count with nothing to move it. It follows the
/// per-feature <c>/config</c> shape the upload and map surfaces already use, so that a second
/// transport arrives as a second answer here and one arm in the client's hook.
/// </remarks>
public static class NotificationConfigEndpoints
{
    internal const string Default = "poll";

    public static RouteGroupBuilder MapNotificationConfigEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/notifications").WithTags("Notifications")
            .MapGet("/config", Config)
            .WithSummary("How a page keeps its unread count current: the transport this build supports.");

        return api;
    }

    private static Ok<NotificationConfigDto> Config() => TypedResults.Ok(new NotificationConfigDto(Default));
}
