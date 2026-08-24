// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;

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
        NotificationCategories.All.ShouldAllBe(c =>
            NotificationCategories.Default(c, NotificationChannelKind.Sms)
                == NotificationChannelChoice.Off);

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
    public void Category_values_are_the_schema_contract()
    {
        ((short)NotificationCategory.CavingGroupMembership).ShouldBe((short)0);
        ((short)NotificationCategory.PermissionGranted).ShouldBe((short)1);
        ((short)NotificationCategory.TripParticipation).ShouldBe((short)2);
        ((short)NotificationCategory.JobCompleted).ShouldBe((short)3);
        ((short)NotificationCategory.SecurityAlerts).ShouldBe((short)4);
        ((short)NotificationCategory.TripPlanning).ShouldBe((short)5);
        ((short)NotificationCategory.CommentReply).ShouldBe((short)7);
        ((short)NotificationCategory.CommentOnMine).ShouldBe((short)8);

        ((short)NotificationChannelKind.InApp).ShouldBe((short)1);
        ((short)NotificationChannelKind.Email).ShouldBe((short)2);
        ((short)NotificationChannelKind.Sms).ShouldBe((short)4);

        ((short)NotificationChannelChoice.Off).ShouldBe((short)0);
        ((short)NotificationChannelChoice.Immediate).ShouldBe((short)1);
        ((short)NotificationChannelChoice.Daily).ShouldBe((short)2);
    }
}
