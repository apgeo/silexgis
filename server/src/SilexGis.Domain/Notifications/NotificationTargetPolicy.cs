// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Messaging;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// Which notifications must name the thing they are about, and which are excused — with the
/// reason each one is excused written down beside it.
/// </summary>
/// <remarks>
/// <para>
/// A notification that carries no target reference can never be re-checked against the reader's
/// access at the moment they read it: the only things left on the row are a name and a path the
/// producer froze when it queued, and a reader may have lost access to that object since. So a
/// row with no target is a row whose protection rule cannot be applied.
/// </para>
/// <para>
/// The target is nevertheless an optional argument rather than a required one, so that a producer
/// written elsewhere still compiles. Optional in the signature must not mean unenforced in the
/// contract, or the rule quietly rots for exactly the rows nobody audits — so the gap is held by
/// this table and the test over it instead of by the type. Every message a producer can emit is
/// either required to carry a target or named here with its reason; a new message is required by
/// default and fails that test until somebody decides which it is.
/// </para>
/// <para>
/// <b>The exemptions are meant to shrink.</b> Some are permanent — a warning that an account's own
/// password changed is about the account and has nothing to open. Others are only true today, and
/// are marked as such.
/// </para>
/// </remarks>
public static class NotificationTargetPolicy
{
    /// <summary>What marks a catalogue entry as a notification rather than a transactional message.</summary>
    public const string TemplatePrefix = "notify.";

    /// <summary>
    /// Messages allowed to carry no target, and why. Anything else in the catalogue whose key
    /// begins with <see cref="TemplatePrefix"/> must be queued with one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Exemptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageTemplateCatalog.NotifySecurityPasswordChanged] =
                "About the account itself: there is no object with a page to open.",
            [MessageTemplateCatalog.NotifySecurityEmailChanged] =
                "About the account itself, and it is sent straight to the previous address rather "
                + "than queued at all, because the whole value of the warning is that it reaches "
                + "the mailbox that was taken away.",
            [MessageTemplateCatalog.NotifySecurityTwoFactorDisabled] =
                "About the account itself: there is no object with a page to open.",
            [MessageTemplateCatalog.NotifyJobCompleted] =
                "Temporary. The worker knows a job's kind but not its subject, which is inside the "
                + "job's own payload, so there is nothing it could name yet.",
            [MessageTemplateCatalog.NotifyJobFailed] =
                "Temporary. Same as the completion notice: the subject is inside the payload.",
            [MessageTemplateCatalog.NotifyDigest] =
                "Not queued by any producer. A daily summary is composed at send time out of the "
                + "notifications it collects, each of which carries its own target.",
            [MessageTemplateCatalog.NotifyTripParticipation] =
                "Temporary. Its producer belongs to work in progress elsewhere and is not editable "
                + "from here; the target is a trip and should be filled in when that work lands.",
            [MessageTemplateCatalog.NotifyTripPlanInvitation] =
                "Temporary. Its producer belongs to work in progress elsewhere; the target is a trip.",
            [MessageTemplateCatalog.NotifyTripPlanChanged] =
                "Temporary. Its producer belongs to work in progress elsewhere; the target is a trip.",
            [MessageTemplateCatalog.NotifyTripPlanCancelled] =
                "Temporary. Its producer belongs to work in progress elsewhere; the target is a trip.",
            [MessageTemplateCatalog.NotifyTripInviteeCannotOpenCave] =
                "Temporary. Its producer belongs to work in progress elsewhere; the target is the "
                + "cave, not the trip.",
        };

    /// <summary>Whether a message must name what it is about.</summary>
    public static bool RequiresTarget(string templateKey) =>
        templateKey.StartsWith(TemplatePrefix, StringComparison.Ordinal)
        && !Exemptions.ContainsKey(templateKey);
}
