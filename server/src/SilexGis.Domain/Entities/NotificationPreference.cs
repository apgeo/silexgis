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
        // A category added to the enum but not named here stays off rather than surprising
        // everyone with mail they never asked for.
        _ => false,
    };

    /// <summary>
    /// Whether the user may switch a category off. Security alerts may not: an attacker holding a
    /// live session could otherwise silence the warning that the account is being taken over.
    /// </summary>
    public static bool IsUserConfigurable(NotificationCategory category) =>
        category is not NotificationCategory.SecurityAlerts;

    /// <summary>
    /// Whether a category ignores the digest setting and the master switch. Security alerts are
    /// always sent immediately, for the same reason they cannot be switched off.
    /// </summary>
    public static bool IsAlwaysImmediate(NotificationCategory category) =>
        category is NotificationCategory.SecurityAlerts;
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
