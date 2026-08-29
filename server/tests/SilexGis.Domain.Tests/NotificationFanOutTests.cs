// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What one notification fans out into, as a table.
/// </summary>
/// <remarks>
/// The sentence under test is that a decision not to send is the <em>absence</em> of a delivery
/// row rather than a row saying so. "Suppress" survives as the name of a routing answer precisely
/// because it no longer names a stored state: nothing is suppressed, because a delivery is a thing
/// that leaves the system and a refusal never leaves it.
/// </remarks>
public class NotificationFanOutTests
{
    /// <summary>Every channel a delivery row could name, so this table grows when one is added.</summary>
    private static readonly NotificationChannel[] AllChannels = Enum.GetValues<NotificationChannel>();

    [Theory]
    [InlineData(NotificationRoute.Send, NotificationDeliveryStatus.Pending)]
    [InlineData(NotificationRoute.Defer, NotificationDeliveryStatus.Deferred)]
    public void An_answer_that_is_not_a_refusal_becomes_one_delivery_in_that_state(
        NotificationRoute route, NotificationDeliveryStatus expected)
    {
        var planned = NotificationFanOut.Plan([(NotificationChannel.Email, route)]).ShouldHaveSingleItem();

        planned.Channel.ShouldBe(NotificationChannel.Email);
        planned.Status.ShouldBe(expected);
    }

    [Fact]
    public void A_refusal_becomes_no_delivery_at_all()
    {
        // Not a delivery marked as refused: no row. The notification itself is untouched by this
        // and stays in the recipient's inbox, which is where somebody who switched a channel off
        // reads the very events they switched it off for.
        NotificationFanOut.Plan([(NotificationChannel.Email, NotificationRoute.Suppress)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_notification_no_channel_answered_for_fans_out_to_nothing()
    {
        // Zero channels is an ordinary outcome rather than a failure — a notification nothing
        // carries anywhere has still happened and is still readable.
        NotificationFanOut.Plan([]).ShouldBeEmpty();
    }

    [Fact]
    public void Every_channel_that_would_send_gets_its_own_row()
    {
        var planned = NotificationFanOut.Plan(
            AllChannels.Select(channel => (channel, NotificationRoute.Send)));

        planned.Count.ShouldBe(AllChannels.Length);
        planned.Select(p => p.Channel).ShouldBe(AllChannels, ignoreOrder: true);
        planned.ShouldAllBe(p => p.Status == NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public void Every_channel_refusing_leaves_nothing_to_send()
    {
        NotificationFanOut.Plan(
            AllChannels.Select(channel => (channel, NotificationRoute.Suppress)))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_channels_that_refused_are_missing()
    {
        // The positive and the negative in one table: the first channel is willing and the rest
        // are not, so a fan-out that dropped everything and one that dropped nothing both fail.
        var answers = AllChannels
            .Select((channel, index) => (channel, index == 0 ? NotificationRoute.Send : NotificationRoute.Suppress))
            .ToList();

        var planned = NotificationFanOut.Plan(answers);

        planned.ShouldHaveSingleItem().Channel.ShouldBe(AllChannels[0]);
        planned.Select(p => p.Channel).ShouldNotContain(
            channel => answers.Any(a => a.channel == channel && a.Item2 == NotificationRoute.Suppress));
    }
}
