// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

public class NotificationCategoryTests
{
    [Fact]
    public void All_lists_every_category() =>
        NotificationCategories.All.ShouldBe(Enum.GetValues<NotificationCategory>(), ignoreOrder: true);

    [Fact]
    public void Every_channel_is_one_bit_and_they_add_up_to_everything()
    {
        NotificationChannelKinds.All.ShouldAllBe(c => NotificationChannelKinds.IsSingle(c));
        NotificationChannelKinds.All
            .Aggregate(NotificationChannelKind.None, (set, channel) => set | channel)
            .ShouldBe(NotificationChannelKinds.Everything);

        // Nothing is a channel, and a pair of channels is not one either — a preference row holds
        // exactly one, and this is the predicate the database's own check constraint mirrors.
        NotificationChannelKinds.IsSingle(NotificationChannelKind.None).ShouldBeFalse();
        NotificationChannelKinds
            .IsSingle(NotificationChannelKind.InApp | NotificationChannelKind.Email)
            .ShouldBeFalse();
    }

    [Fact]
    public void Every_category_names_at_least_one_channel_it_may_use() =>
        // Keeps the fail-closed arm of the ceiling unreachable: a category added to the enum and
        // not named there reaches nobody anywhere, which is safe and completely silent.
        NotificationCategories.All.ShouldAllBe(c =>
            NotificationCategories.Ceiling(c) != NotificationChannelKind.None);

    [Fact]
    public void The_inbox_is_on_by_default_for_every_category() =>
        // Chosen deliberately: the inbox costs the reader nothing until they open it, and someone
        // who has never touched their settings should still find their notifications somewhere.
        NotificationCategories.All.ShouldAllBe(c =>
            NotificationCategories.Default(c, NotificationChannelKind.InApp)
                == NotificationChannelChoice.Immediate);

    [Fact]
    public void A_channel_a_category_may_never_use_is_off_by_default() =>
        // Stated over every category and every channel rather than over the one channel most of
        // them cannot use, because that is the rule: a default outside the ceiling would be a
        // preference nothing could act on, written into everybody's account.
        NotificationCategories.All.ShouldAllBe(category =>
            NotificationChannelKinds.All
                .Where(channel => (NotificationCategories.Ceiling(category) & channel) != channel)
                .All(channel => NotificationCategories.Default(category, channel)
                    == NotificationChannelChoice.Off));

    [Fact]
    public void Only_a_message_worth_paying_for_may_ever_cost_money() =>
        // Most categories are a side effect of something that happened, and none of those is worth
        // a charge per recipient. Two are, and they are here for different reasons rather than by
        // family resemblance. The overdue-party alarm, because its reader may be the one person
        // standing at a cave entrance with no data, where a text arrives and a mailbox does not.
        // The announcement, because it is the one message a person writes and aims at a club, and
        // a meeting place changed at short notice has a real argument for a text. Listed
        // exhaustively so that a third arrives as a decision somebody took rather than as a line
        // in a switch nobody read — and note that being named here only makes the channel
        // permissible: it costs money, so it stays off until an account chooses it.
        NotificationCategories.All
            .Where(category =>
                (NotificationCategories.Ceiling(category) & NotificationChannelKinds.Paid)
                    != NotificationChannelKind.None)
            .ShouldBe([NotificationCategory.TripCallout, NotificationCategory.GroupAnnouncement]);

    [Fact]
    public void The_overdue_alarm_may_reach_a_phone_and_reaches_nobody_who_has_not_asked()
    {
        const NotificationChannelKind everything =
            NotificationChannelKind.InApp | NotificationChannelKind.Email | NotificationChannelKind.Sms;
        const NotificationChannelKind paidAllowed = NotificationChannelKind.Sms;

        // The permission half. A text is the one thing that reaches somebody standing at a cave
        // entrance with no data, so the alarm is allowed to travel that way.
        (NotificationCategories.Ceiling(NotificationCategory.TripCallout) & NotificationChannelKind.Sms)
            .ShouldBe(NotificationChannelKind.Sms);

        // And the half that matters more, because getting it wrong would be worse than not having
        // built this at all. Permission is not consent: the only number this installation holds is
        // the one an account confirmed to sign in with, which proves whose number it is and not
        // that its owner agreed to be texted. So an account that has never chosen this is not
        // texted — not on the day an operator configures a gateway, and not because the category
        // happens to be one nobody may switch off.
        NotificationCategories.Default(NotificationCategory.TripCallout, NotificationChannelKind.Sms)
            .ShouldBe(NotificationChannelChoice.Off);
        NotificationMatrix.Resolve(
            NotificationCategory.TripCallout,
            NotificationChannelKind.Sms,
            stored: null,
            everything,
            paidAllowed).ShouldBe(NotificationChannelChoice.Off);

        // Somebody who asks for it gets it, and may stop again — otherwise the first half would be
        // a trap rather than an offer.
        NotificationMatrix.CanChoose(
            NotificationCategory.TripCallout,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            everything,
            paidAllowed).ShouldBeTrue();
        NotificationMatrix.Resolve(
            NotificationCategory.TripCallout,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            everything,
            paidAllowed).ShouldBe(NotificationChannelChoice.Immediate);
        NotificationMatrix.CanChoose(
            NotificationCategory.TripCallout,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Off,
            everything,
            paidAllowed).ShouldBeTrue();

        // An installation that has not agreed to pay for text messages sends none whatever any
        // account has chosen, and the two free channels are untouched by any of this: the alarm
        // still cannot be switched off where it costs nothing.
        NotificationMatrix.Resolve(
            NotificationCategory.TripCallout,
            NotificationChannelKind.Sms,
            NotificationChannelChoice.Immediate,
            everything).ShouldBe(NotificationChannelChoice.Off);
        foreach (var free in new[] { NotificationChannelKind.InApp, NotificationChannelKind.Email })
        {
            NotificationMatrix.Resolve(
                NotificationCategory.TripCallout,
                free,
                NotificationChannelChoice.Off,
                everything,
                paidAllowed).ShouldBe(NotificationChannelChoice.Immediate);
        }
    }

    [Fact]
    public void Not_being_switchable_off_and_not_being_deferrable_are_the_same_categories()
    {
        // They travel together for one reason: a category has a safety argument behind it or it
        // does not, and a warning that arrives tomorrow morning is not a warning. Stated as a
        // property rather than by naming a category, because more than one category has that
        // argument and the second arrives from work happening elsewhere.
        NotificationCategories.All.ShouldAllBe(c =>
            NotificationCategories.IsUserConfigurable(c) != NotificationCategories.IsAlwaysImmediate(c));

        // And the table is not vacuous in either direction.
        NotificationCategories.All.ShouldContain(c => !NotificationCategories.IsUserConfigurable(c));
        NotificationCategories.All.ShouldContain(c => NotificationCategories.IsUserConfigurable(c));
    }

    [Fact]
    public void A_category_nobody_may_switch_off_can_still_be_reached_outside_the_session()
    {
        // The requirement is at least one channel that is not the session: an attacker holding a
        // live one can read the inbox, so mail specifically has to be in the ceiling.
        foreach (var category in NotificationCategories.All.Where(c => !NotificationCategories.IsUserConfigurable(c)))
        {
            (NotificationCategories.Ceiling(category) & NotificationChannelKind.Email)
                .ShouldBe(NotificationChannelKind.Email, $"{category} must be able to reach a mailbox");
        }
    }

    [Fact]
    public void Trip_callout_cannot_be_switched_off_or_held_back()
    {
        // An overdue alarm exists to be heard when nobody is answering. Muted, or held until the
        // next daily summary, it arrives after the night somebody spent underground — so the two
        // switches that would do either are refused to it, not merely defaulted against.
        NotificationCategories.IsUserConfigurable(NotificationCategory.TripCallout).ShouldBeFalse();
        NotificationCategories.IsAlwaysImmediate(NotificationCategory.TripCallout).ShouldBeTrue();
    }

    /// <summary>
    /// Every category outside a stated list of exceptions is the user's own to switch off and to
    /// have summarised.
    /// </summary>
    /// <remarks>
    /// The exceptions are written out here, one line each with its reason, rather than derived
    /// from <see cref="NotificationCategories.IsAlwaysImmediate"/> — deriving them would make this
    /// a tautology and let any later category quietly exempt itself. Adding a third exception
    /// therefore fails this test, which is the point: escaping the user's switches is a decision
    /// somebody takes and writes down.
    /// </remarks>
    [Fact]
    public void Every_other_category_is_the_user_s_choice()
    {
        NotificationCategory[] exceptions =
        [
            // Silencing this is how a live session hides that it is taking the account over.
            NotificationCategory.SecurityAlerts,
            // Silencing this is indistinguishable from a party that came back safely.
            NotificationCategory.TripCallout,
        ];

        NotificationCategories.All
            .Where(c => !exceptions.Contains(c))
            .ShouldAllBe(c => NotificationCategories.IsUserConfigurable(c)
                && !NotificationCategories.IsAlwaysImmediate(c));
    }

    [Fact]
    public void Category_values_are_the_schema_contract()
    {
        ((short)NotificationCategory.CavingGroupMembership).ShouldBe((short)0);
        ((short)NotificationCategory.PermissionGranted).ShouldBe((short)1);
        ((short)NotificationCategory.TripParticipation).ShouldBe((short)2);
        ((short)NotificationCategory.JobCompleted).ShouldBe((short)3);
        ((short)NotificationCategory.SecurityAlerts).ShouldBe((short)4);
        ((short)NotificationCategory.TripPlanning).ShouldBe((short)5);
        ((short)NotificationCategory.TripCallout).ShouldBe((short)6);
        ((short)NotificationCategory.CommentReply).ShouldBe((short)7);
        ((short)NotificationCategory.CommentOnMine).ShouldBe((short)8);
        ((short)NotificationCategory.GroupAnnouncement).ShouldBe((short)9);

        ((short)NotificationChannelKind.InApp).ShouldBe((short)1);
        ((short)NotificationChannelKind.Email).ShouldBe((short)2);
        ((short)NotificationChannelKind.Sms).ShouldBe((short)4);

        ((short)NotificationChannelChoice.Off).ShouldBe((short)0);
        ((short)NotificationChannelChoice.Immediate).ShouldBe((short)1);
        ((short)NotificationChannelChoice.Daily).ShouldBe((short)2);
    }
}
