// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which notifications have to name the thing they are about.
/// </summary>
/// <remarks>
/// A notification carries a rendered name and a path frozen at the moment it was queued, and by
/// the time somebody reads it they may have lost access to what it names. The target reference is
/// the only thing a reader's current access can be re-decided against, so a message queued without
/// one can never be re-checked — and the rule fails silently, for exactly the rows nobody audits.
/// <para>
/// The reference is nevertheless an optional argument, because requiring it in the type would
/// break producers being written elsewhere. These tests are what stops "optional in the signature"
/// becoming "unenforced in the contract": every message is classified, and an exemption has to be
/// written down with its reason.
/// </para>
/// </remarks>
public class NotificationTargetPolicyTests
{
    [Fact]
    public void Every_notification_message_is_either_required_to_name_its_subject_or_excused_by_name()
    {
        // Adding a message to the catalogue is what makes this fail: it is required to carry a
        // target by default, and whoever adds it has to either give its producer one or say here
        // why it has none.
        //
        // This classifies keys; it cannot see a producer, so it says nothing about whether the
        // call that emits a key actually passes a target. That half is asserted where producers
        // can be driven — the notification delivery tests read the columns back after a real
        // request, and the inbox tests revoke a grant and require the row to degrade, which it
        // cannot do without one.
        var notifications = MessageTemplateCatalog.All
            .Where(d => d.Key.StartsWith(NotificationTargetPolicy.TemplatePrefix, StringComparison.Ordinal))
            .Select(d => d.Key)
            .ToList();

        notifications.ShouldNotBeEmpty();

        foreach (var key in notifications)
        {
            var excused = NotificationTargetPolicy.Exemptions.ContainsKey(key);
            NotificationTargetPolicy.RequiresTarget(key).ShouldBe(!excused, key);
        }
    }

    [Fact]
    public void Every_excused_message_names_a_message_that_exists_and_says_why()
    {
        // Two ways the list rots: it keeps naming a message somebody deleted, or it grows an entry
        // with no reason beside it, at which point nobody can tell an accepted gap from an
        // oversight.
        foreach (var (key, reason) in NotificationTargetPolicy.Exemptions)
        {
            MessageTemplateCatalog.Find(key).ShouldNotBeNull(key);
            reason.ShouldNotBeNullOrWhiteSpace(key);
            reason.Length.ShouldBeGreaterThan(30, $"{key} is excused without saying why");
        }
    }

    [Fact]
    public void The_messages_that_still_name_nothing_are_exactly_these()
    {
        // The list is meant to shrink, so it is pinned: filling a target in on a producer is a
        // deliberate deletion from here, and nothing drifts onto it unnoticed. Of the eleven,
        // three are permanent — a warning about the account itself has nothing to open — and the
        // rest are named as temporary in the policy's own reasons.
        NotificationTargetPolicy.Exemptions.Keys.ShouldBe(
            [
                MessageTemplateCatalog.NotifySecurityPasswordChanged,
                MessageTemplateCatalog.NotifySecurityEmailChanged,
                MessageTemplateCatalog.NotifySecurityTwoFactorDisabled,
                MessageTemplateCatalog.NotifyJobCompleted,
                MessageTemplateCatalog.NotifyJobFailed,
                MessageTemplateCatalog.NotifyDigest,
                MessageTemplateCatalog.NotifyTripParticipation,
                MessageTemplateCatalog.NotifyTripPlanInvitation,
                MessageTemplateCatalog.NotifyTripPlanChanged,
                MessageTemplateCatalog.NotifyTripPlanCancelled,
                MessageTemplateCatalog.NotifyTripInviteeCannotOpenCave,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void A_message_that_is_not_a_notification_is_never_required_to_name_a_subject()
    {
        // The rule is about notifications. A password-reset mail answers something the recipient
        // just did and points at nothing they could have lost access to.
        NotificationTargetPolicy.RequiresTarget(MessageTemplateCatalog.EmailPasswordReset).ShouldBeFalse();
        NotificationTargetPolicy.RequiresTarget(MessageTemplateCatalog.SmsTwoFactorCode).ShouldBeFalse();
    }
}
