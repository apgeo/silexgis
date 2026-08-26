// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Settings;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What it takes for an announcement to leave by a channel that charges for every message.
/// </summary>
/// <remarks>
/// The whole of the protection is that two separate things must both be true — something has to
/// implement the transport, and the installation has to have said it will pay for it — and that
/// they are intersected in one place rather than checked wherever somebody remembers to. These
/// pin the second one, because the first will stop being true the day somebody wires a sender in,
/// and a guard that quietly depended on it would come apart in that change without anything
/// failing.
/// </remarks>
public class AnnouncementPaidChannelTests
{
    /// <summary>An installation where the paid transport really is wired up and working.</summary>
    private const NotificationChannelKind EverythingInstalled = NotificationChannelKind.InApp
        | NotificationChannelKind.Email
        | NotificationChannelKind.Sms;

    [Fact]
    public void A_fresh_installation_will_not_spend_money_on_announcements()
    {
        var shipped = new AnnouncementSettings();

        shipped.PaidChannelsEnabled.ShouldBeFalse();
        shipped.PaidChannelsAllowed.ShouldBe(NotificationChannelKind.None);
        shipped.EffectiveDailyPaidMessageCap.ShouldBe(100);
    }

    [Fact]
    public void A_wired_up_paid_transport_is_still_unreachable_until_somebody_says_it_may_be_used()
    {
        // The transport exists and works. Nothing else is in the way — and the answer is still no.
        var settings = new AnnouncementSettings();

        NotificationMatrix
            .Usable(NotificationCategory.GroupAnnouncement, EverythingInstalled, settings.PaidChannelsAllowed)
            .ShouldBe(NotificationChannelKind.InApp | NotificationChannelKind.Email);
    }

    [Fact]
    public void Nobody_can_choose_a_paid_channel_this_installation_has_not_agreed_to()
    {
        var settings = new AnnouncementSettings();

        // Not hidden — refused. A choice that cannot be stored is a choice no later code has to
        // remember to ignore.
        NotificationMatrix.CanChoose(
            NotificationCategory.GroupAnnouncement,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            EverythingInstalled,
            settings.PaidChannelsAllowed).ShouldBeFalse();

        // And a row somehow already saying otherwise is worth nothing when it is read.
        NotificationMatrix.Resolve(
            NotificationCategory.GroupAnnouncement,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            EverythingInstalled,
            settings.PaidChannelsAllowed).ShouldBe(NotificationChannelChoice.Off);
    }

    [Fact]
    public void Switching_it_on_is_what_makes_it_reachable()
    {
        // The positive half, and the reason the three above are evidence rather than a restatement
        // of "nothing is installed": with the same transport and the same category, one saved
        // answer is the whole difference.
        var settings = new AnnouncementSettings { PaidChannelsEnabled = true };

        settings.PaidChannelsAllowed.ShouldBe(NotificationChannelKind.Sms);

        NotificationMatrix
            .Usable(NotificationCategory.GroupAnnouncement, EverythingInstalled, settings.PaidChannelsAllowed)
            .ShouldBe(EverythingInstalled);

        // Two answers are needed before a text is sent, and this is where they meet. The
        // installation's switch decides whether the channel may be reached at all; the account's
        // own answer decides whether it is. So with nothing stored it is still off — a confirmed
        // sign-in number is not consent to be texted — and it is one saved choice that turns it on.
        NotificationMatrix.Resolve(
            NotificationCategory.GroupAnnouncement,
            NotificationChannelKind.Sms,
            null,
            EverythingInstalled,
            settings.PaidChannelsAllowed).ShouldBe(NotificationChannelChoice.Off);

        NotificationMatrix.Resolve(
            NotificationCategory.GroupAnnouncement,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            EverythingInstalled,
            settings.PaidChannelsAllowed).ShouldBe(NotificationChannelChoice.Immediate);
    }

    [Fact]
    public void Switching_it_on_widens_nothing_but_the_one_category_that_may_cost_money() =>
        NotificationCategories.All
            .Where(category => category is not NotificationCategory.GroupAnnouncement)
            .ShouldAllBe(category => NotificationMatrix.Usable(
                category,
                EverythingInstalled,
                new AnnouncementSettings { PaidChannelsEnabled = true }.PaidChannelsAllowed)
                    == (NotificationChannelKind.InApp | NotificationChannelKind.Email));

    [Fact]
    public void A_caller_that_does_not_ask_the_installation_spends_nothing() =>
        // The argument is optional so that adding it did not have to be threaded through every
        // reader at once, and the default is the answer that costs nothing — so a reader that has
        // not been updated makes a paid channel unreachable rather than silently billable.
        NotificationMatrix
            .Usable(NotificationCategory.GroupAnnouncement, EverythingInstalled)
            .ShouldBe(NotificationChannelKind.InApp | NotificationChannelKind.Email);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_daily_ceiling_of_nothing_is_a_slip_and_is_refused(int typed) =>
        // A mistyped environment variable and a mistyped form field must fail the same way, and
        // there is no legitimate reading of "will pay for messages, up to none of them": the
        // switch above already says that unambiguously.
        new AnnouncementSettings { DailyPaidMessageCap = typed }
            .EffectiveDailyPaidMessageCap
            .ShouldBe(AnnouncementSettings.DefaultDailyPaidMessageCap);

    [Fact]
    public void A_ceiling_somebody_chose_is_the_one_that_counts() =>
        new AnnouncementSettings { DailyPaidMessageCap = 7 }.EffectiveDailyPaidMessageCap.ShouldBe(7);
}
