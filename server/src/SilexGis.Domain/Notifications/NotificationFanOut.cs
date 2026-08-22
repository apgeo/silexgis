// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Notifications;

/// <summary>One delivery row that routing has decided has to exist.</summary>
/// <param name="Channel">Which way out of the system it takes.</param>
/// <param name="Status">Where it starts: due now, or held for the recipient's daily summary.</param>
public readonly record struct PlannedDelivery(NotificationChannel Channel, NotificationDeliveryStatus Status);

/// <summary>
/// Turns one routing answer per channel into the delivery rows a notification fans out into.
/// </summary>
/// <remarks>
/// <para>
/// <b>A decision not to send is the absence of a delivery row.</b> That is the whole rule, and it
/// is stated here once so it cannot be re-stated differently anywhere else. There is no
/// "suppressed" delivery, because a delivery is a thing that leaves the system and a refusal never
/// leaves it — and the notification is in the recipient's inbox either way, which is the point:
/// the events somebody switched a channel off for are exactly the ones an inbox exists to show.
/// </para>
/// <para>
/// Zero rows is therefore an ordinary, successful outcome rather than a failure, and so is zero
/// channels: a notification nothing carries anywhere has still happened and is still readable.
/// </para>
/// <para>
/// Pure, and in Domain beside the rule that produces the answers it consumes, so the fan-out can
/// be read and tested as a table without a database, a mail server or a clock. When the routing
/// answer is a deferral the delivery starts <see cref="NotificationDeliveryStatus.Deferred"/> and
/// its due time is the next summary window — the caller stamps that, because only it has a clock.
/// </para>
/// </remarks>
public static class NotificationFanOut
{
    /// <summary>
    /// The deliveries that must exist, given what each channel answered. The caller supplies at
    /// most one answer per channel; a channel that would carry nothing simply answers
    /// <see cref="NotificationRoute.Suppress"/> and contributes no row.
    /// </summary>
    public static IReadOnlyList<PlannedDelivery> Plan(
        IEnumerable<(NotificationChannel Channel, NotificationRoute Route)> answers) =>
        [.. answers
            .Where(answer => answer.Route is not NotificationRoute.Suppress)
            .Select(answer => new PlannedDelivery(
                answer.Channel,
                answer.Route is NotificationRoute.Defer
                    ? NotificationDeliveryStatus.Deferred
                    : NotificationDeliveryStatus.Pending))];
}
