// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The category by channel matrix, read as a table: every category against every channel the
/// vocabulary names, because the rules that override a stored row are the kind that hold for five
/// cells and quietly fail for the sixth.
/// </summary>
public class NotificationMatrixTests
{
    private const NotificationChannelKind Everything = NotificationChannelKind.InApp
        | NotificationChannelKind.Email
        | NotificationChannelKind.Sms;

    /// <summary>
    /// An installation that has agreed to pay for the channels that charge, so that every cell of
    /// every ceiling is genuinely reachable here and these tests are about the rules rather than
    /// about what somebody has switched on. That an installation which has agreed to nothing
    /// reaches none of them is a different claim, stated where the switch is.
    /// </summary>
    private const NotificationChannelKind PaidAllowed = NotificationChannelKind.Sms;

    /// <summary>Every cell of the matrix, so a test can state a rule over all of it at once.</summary>
    public static TheoryData<NotificationCategory, NotificationChannelKind> EveryCell()
    {
        var cells = new TheoryData<NotificationCategory, NotificationChannelKind>();
        foreach (var category in NotificationCategories.All)
        {
            foreach (var channel in NotificationChannelKinds.All)
            {
                cells.Add(category, channel);
            }
        }

        return cells;
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void An_account_that_has_never_opened_its_settings_gets_the_documented_defaults(
        NotificationCategory category, NotificationChannelKind channel)
    {
        // The single easiest thing here to break in silence: nothing stored is the ordinary case,
        // not "everything off". Read through the same function the worker reads through, so a
        // default that drifts cannot drift only for the worker.
        //
        // A channel that costs money is the exception, and it is a rule about consent rather than
        // about cost: the only number held is the one confirmed to sign in with, and confirming it
        // proves whose number it is, not that its owner agreed to be texted. So a paid cell starts
        // off however wide the category's ceiling is, and somebody has to choose it.
        var inCeiling = (NotificationCategories.Ceiling(category) & channel) == channel;
        var costsMoney = (NotificationChannelKinds.Paid & channel) == channel;
        var expected = inCeiling && !costsMoney
            ? NotificationChannelChoice.Immediate
            : NotificationChannelChoice.Off;

        NotificationMatrix.Resolve(category, channel, stored: null, Everything, PaidAllowed).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void A_channel_outside_a_category_ceiling_can_never_be_chosen(
        NotificationCategory category, NotificationChannelKind channel)
    {
        if ((NotificationCategories.Ceiling(category) & channel) == channel)
        {
            // The positive half, in the same test: a channel inside the ceiling is choosable and
            // an asked-for choice is what comes back.
            NotificationMatrix.CanChoose(
                category, channel, NotificationChannelChoice.Immediate, Everything, PaidAllowed).ShouldBeTrue();
            NotificationMatrix.Resolve(
                category, channel, NotificationChannelChoice.Immediate, Everything, PaidAllowed)
                .ShouldBe(NotificationChannelChoice.Immediate);
            return;
        }

        foreach (var choice in Enum.GetValues<NotificationChannelChoice>())
        {
            NotificationMatrix.CanChoose(category, channel, choice, Everything, PaidAllowed).ShouldBeFalse();
        }

        NotificationMatrix.Resolve(category, channel, NotificationChannelChoice.Immediate, Everything, PaidAllowed)
            .ShouldBe(NotificationChannelChoice.Off);
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void A_stored_row_for_a_channel_this_installation_does_not_have_is_inert(
        NotificationCategory category, NotificationChannelKind channel)
    {
        // An installation with nothing but its own inbox. A row saved when a transport was
        // configured, or before a ceiling was narrowed, is simply worth nothing when it is read —
        // which is what lets either change happen without a data migration behind it.
        var inboxOnly = NotificationChannelKind.InApp;

        var resolved = NotificationMatrix.Resolve(
            category, channel, NotificationChannelChoice.Immediate, inboxOnly);

        if (channel == NotificationChannelKind.InApp)
        {
            resolved.ShouldBe(NotificationChannelChoice.Immediate);
        }
        else
        {
            resolved.ShouldBe(NotificationChannelChoice.Off);
            NotificationMatrix
                .CanChoose(category, channel, NotificationChannelChoice.Immediate, inboxOnly)
                .ShouldBeFalse();
        }
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void A_category_nobody_may_switch_off_is_on_whatever_is_stored(
        NotificationCategory category, NotificationChannelKind channel)
    {
        var inCeiling = (NotificationCategories.Ceiling(category) & channel) == channel;

        if (NotificationCategories.IsUserConfigurable(category))
        {
            // The positive half: an ordinary category obeys what was stored.
            NotificationMatrix.Resolve(category, channel, NotificationChannelChoice.Off, Everything, PaidAllowed)
                .ShouldBe(NotificationChannelChoice.Off);
            NotificationMatrix
                .CanChoose(category, channel, NotificationChannelChoice.Off, Everything, PaidAllowed)
                .ShouldBe(inCeiling);
            return;
        }

        // A stored "off" for one of these could only have been written by something that got past
        // the write path, so it is ignored rather than obeyed — wherever the category can reach.
        NotificationMatrix.Resolve(category, channel, NotificationChannelChoice.Off, Everything, PaidAllowed)
            .ShouldBe(inCeiling ? NotificationChannelChoice.Immediate : NotificationChannelChoice.Off);
        NotificationMatrix.CanChoose(category, channel, NotificationChannelChoice.Off, Everything, PaidAllowed)
            .ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void A_daily_summary_is_only_ever_offered_where_it_means_something(
        NotificationCategory category, NotificationChannelKind channel)
    {
        // The digest is a state of the mail cell, not a setting of its own, so "summarise a
        // category whose mail is off" cannot be written down at all. What is left to enforce is
        // the other half: a channel that cannot hold anything back, and a category that refuses to
        // be held back, both read a stored summary as "as it happens" rather than losing it.
        var offerable = NotificationChannelKinds.CanDefer(channel)
            && !NotificationCategories.IsAlwaysImmediate(category)
            && (NotificationCategories.Ceiling(category) & channel) == channel;

        NotificationMatrix.CanChoose(category, channel, NotificationChannelChoice.Daily, Everything, PaidAllowed)
            .ShouldBe(offerable);

        var resolved = NotificationMatrix.Resolve(
            category, channel, NotificationChannelChoice.Daily, Everything, PaidAllowed);

        if (offerable)
        {
            resolved.ShouldBe(NotificationChannelChoice.Daily);
        }
        else
        {
            resolved.ShouldNotBe(NotificationChannelChoice.Daily);
        }
    }

    [Fact]
    public void A_category_can_be_switched_off_everywhere_and_is_then_said_to_reach_nobody()
    {
        var category = NotificationCategories.All.First(NotificationCategories.IsUserConfigurable);

        // A reachable state and a legitimate one — somebody may choose to hear nothing about
        // something — but never one to arrive at without being told, which is why it is asked as
        // a question rather than left for a settings page to work out.
        NotificationMatrix
            .ReachesNobody(category, _ => NotificationChannelChoice.Off, Everything, PaidAllowed)
            .ShouldBeTrue();

        // The positive half: one channel left on and the category still reaches its reader.
        NotificationMatrix
            .ReachesNobody(
                category,
                channel => channel == NotificationChannelKind.InApp
                    ? NotificationChannelChoice.Immediate
                    : NotificationChannelChoice.Off,
                Everything, PaidAllowed)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_category_nobody_may_switch_off_reaches_somebody_however_it_is_stored()
    {
        foreach (var category in NotificationCategories.All.Where(c => !NotificationCategories.IsUserConfigurable(c)))
        {
            NotificationMatrix
                .ReachesNobody(
                    category,
                    channel => NotificationMatrix.Resolve(
                        category, channel, NotificationChannelChoice.Off, Everything, PaidAllowed),
                    Everything, PaidAllowed)
                .ShouldBeFalse($"{category} must still reach its reader");
        }
    }

    [Fact]
    public void Narrowing_what_is_usable_is_one_intersection()
    {
        // What an administrator narrowing the ceiling would change: one call, not a rewrite of
        // everything that reads a preference.
        var category = NotificationCategories.All.First();

        NotificationMatrix.Usable(category, Everything, PaidAllowed)
            .ShouldBe(NotificationCategories.Ceiling(category));
        NotificationMatrix.Usable(category, NotificationChannelKind.InApp)
            .ShouldBe(NotificationChannelKind.InApp);
        NotificationMatrix.Usable(category, NotificationChannelKind.None)
            .ShouldBe(NotificationChannelKind.None);
    }
}
