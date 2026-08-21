// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Kinds of event a user can be notified about. Each one corresponds to something the
/// application already does — no category exists without a producer.
/// Stored as smallint; values are part of the schema contract — do not renumber.
/// </summary>
public enum NotificationCategory : short
{
    /// <summary>Added to or removed from a caving group, or the member's caving group role changed.</summary>
    CavingGroupMembership = 0,

    /// <summary>Someone granted the user access to an object, or changed a grant they hold.</summary>
    PermissionGranted = 1,

    /// <summary>The user was listed as a participant on a trip log.</summary>
    TripParticipation = 2,

    /// <summary>A background job the user started finished or failed (imports, raster conversion).</summary>
    JobCompleted = 3,

    /// <summary>
    /// Changes to the account's own credentials — email, password, two-factor, linked logins.
    /// Cannot be switched off; see <see cref="NotificationCategories.IsUserConfigurable"/>.
    /// </summary>
    SecurityAlerts = 4,

    /// <summary>
    /// A trip being planned concerns the user: they were invited to it, or one they are on
    /// changed or was called off. One category rather than one per message, because a
    /// preference is per category — so the category count is how finely somebody can mute.
    /// </summary>
    TripPlanning = 5,

    /// <summary>
    /// A party is overdue: the time they said they would be back has passed and nobody has
    /// stood the alarm down. Kept apart from the rest of trip planning precisely because a
    /// preference is per category — folded in with reminders and invitations, somebody who
    /// muted the chatter would have muted this too.
    /// Cannot be switched off or deferred; see <see cref="NotificationCategories.IsUserConfigurable"/>.
    /// </summary>
    TripCallout = 6,
}

/// <summary>How often notifications are delivered. Stored as smallint — do not renumber.</summary>
public enum NotificationDigest : short
{
    /// <summary>One message per event, sent as it happens.</summary>
    Immediate = 0,

    /// <summary>One message a day collecting everything since the last one.</summary>
    Daily = 1,
}

/// <summary>The notification vocabulary and its defaults, in one place.</summary>
public static class NotificationCategories
{
    public static IReadOnlyList<NotificationCategory> All { get; } = [.. Enum.GetValues<NotificationCategory>()];

    /// <summary>
    /// Whether a category is on for a user who has never touched their settings. Also the value
    /// used for a category with no stored row, so a partially-saved set behaves predictably.
    /// </summary>
    public static bool DefaultEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.CavingGroupMembership => true,
        NotificationCategory.PermissionGranted => true,
        NotificationCategory.TripParticipation => true,
        NotificationCategory.JobCompleted => true,
        NotificationCategory.SecurityAlerts => true,
        NotificationCategory.TripPlanning => true,
        NotificationCategory.TripCallout => true,
        // A category added to the enum but not named here stays off rather than surprising
        // everyone with mail they never asked for.
        _ => false,
    };

    /// <summary>
    /// Whether the user may switch a category off.
    /// </summary>
    /// <remarks>
    /// Two categories may not be, and each is here for its own reason rather than by family
    /// resemblance. <see cref="NotificationCategory.SecurityAlerts"/>: an attacker holding a live
    /// session could otherwise silence the warning that the account is being taken over.
    /// <see cref="NotificationCategory.TripCallout"/>: the message exists to be heard when nobody
    /// is answering, and a muted overdue alarm is indistinguishable from a party that came back.
    /// Every other category is the user's own choice, and a third exception is a decision somebody
    /// has to take rather than something a new category may quietly help itself to.
    /// </remarks>
    public static bool IsUserConfigurable(NotificationCategory category) =>
        category is not (NotificationCategory.SecurityAlerts or NotificationCategory.TripCallout);

    /// <summary>
    /// Whether a category ignores the digest setting and the master switch, and is therefore sent
    /// the moment it is raised.
    /// </summary>
    /// <remarks>
    /// The same two, for the same two reasons, and deliberately the same list: a category nobody
    /// may switch off but which a daily summary may hold until morning has been switched off in
    /// all but name. For the callout that is the whole failure — an alarm raised at ten at night
    /// and delivered with the next digest arrives after the night somebody spent underground.
    /// </remarks>
    public static bool IsAlwaysImmediate(NotificationCategory category) =>
        category is NotificationCategory.SecurityAlerts or NotificationCategory.TripCallout;
}

/// <summary>A user's choice for one notification category. Unique per (UserId, Category).</summary>
public class UserNotificationPreference : ITimestamped
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public NotificationCategory Category { get; set; }

    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
