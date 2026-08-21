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
        ((short)NotificationDigest.Immediate).ShouldBe((short)0);
        ((short)NotificationDigest.Daily).ShouldBe((short)1);
    }
}
