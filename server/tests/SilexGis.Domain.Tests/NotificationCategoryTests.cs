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
    public void Every_category_is_on_by_default() =>
        // Keeps the fail-closed default arm unreachable: a new category must be named explicitly.
        NotificationCategories.All.ShouldAllBe(c => NotificationCategories.DefaultEnabled(c));

    [Fact]
    public void Security_alerts_cannot_be_switched_off()
    {
        // An attacker holding a live session must not be able to silence the warning that the
        // account is being taken over.
        NotificationCategories.IsUserConfigurable(NotificationCategory.SecurityAlerts).ShouldBeFalse();
        NotificationCategories.IsAlwaysImmediate(NotificationCategory.SecurityAlerts).ShouldBeTrue();
    }

    [Fact]
    public void Every_other_category_is_the_user_s_choice() =>
        NotificationCategories.All
            .Where(c => c != NotificationCategory.SecurityAlerts)
            .ShouldAllBe(c => NotificationCategories.IsUserConfigurable(c)
                && !NotificationCategories.IsAlwaysImmediate(c));

    [Fact]
    public void Category_values_are_the_schema_contract()
    {
        ((short)NotificationCategory.CavingGroupMembership).ShouldBe((short)0);
        ((short)NotificationCategory.PermissionGranted).ShouldBe((short)1);
        ((short)NotificationCategory.TripParticipation).ShouldBe((short)2);
        ((short)NotificationCategory.JobCompleted).ShouldBe((short)3);
        ((short)NotificationCategory.SecurityAlerts).ShouldBe((short)4);
        ((short)NotificationCategory.TripPlanning).ShouldBe((short)5);
        ((short)NotificationDigest.Immediate).ShouldBe((short)0);
        ((short)NotificationDigest.Daily).ShouldBe((short)1);
    }
}
