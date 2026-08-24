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

    // 6 is not free. A category for a trip being called off has already taken it on another
    // line of work in flight, and values are part of the schema contract — never renumbered,
    // never reused — so the gap stays a gap rather than being filled in from here.

    /// <summary>
    /// Somebody replied to a comment this user wrote. Kept apart from
    /// <see cref="CommentOnMine"/> deliberately: one category would make a busy document's
    /// traffic and a direct answer to something you said the same setting, and the first thing
    /// anyone does with that is switch it off — losing the answer along with the traffic.
    /// </summary>
    CommentReply = 7,

    /// <summary>
    /// Somebody commented on something this user owns. The quieter half of the pair: it reports
    /// that a conversation is happening on your own upload, which is worth knowing and is not
    /// the same thing as being spoken to.
    /// </summary>
    CommentOnMine = 8,
}

/// <summary>
/// Every way a notification can reach a person, as bit flags.
/// </summary>
/// <remarks>
/// <para>
/// This is the vocabulary of the preference matrix, and it deliberately differs from
/// <see cref="NotificationChannel"/>: in-app belongs here and not there, because a preference is a
/// question about where somebody wants to hear things, whereas a delivery row exists only for a
/// thing that leaves the installation and can fail on the way out. In-app can do neither — the
/// notification row's own existence is its in-app presence.
/// </para>
/// <para>
/// Bit flags rather than a plain enum because which channels a category may <em>ever</em> use is a
/// ceiling expressed as a set (see <see cref="NotificationCategories.Ceiling"/>). Narrowing that
/// ceiling further — an administrator forbidding a channel across the installation, say — then
/// becomes one intersection at one call site rather than a rewrite of everything that reads a
/// preference.
/// </para>
/// <para>
/// Stored as smallint on a preference row, always as exactly one bit. Values are part of the
/// schema contract — do not renumber.
/// </para>
/// </remarks>
[Flags]
public enum NotificationChannelKind : short
{
    /// <summary>No channel at all. A category resolved to this reaches the reader nowhere.</summary>
    None = 0,

    /// <summary>The inbox inside the application. Always present: it has no transport to fail.</summary>
    InApp = 1,

    /// <summary>Electronic mail.</summary>
    Email = 2,

    /// <summary>
    /// Text message. In the vocabulary and in no category's ceiling: the transport exists for
    /// sign-in codes, but nothing here has yet made the case that a notification is worth what a
    /// text message costs. Naming it keeps the ceiling honest — a channel outside a category's
    /// ceiling can never be chosen, and this is the one that proves it.
    /// </summary>
    Sms = 4,
}

/// <summary>
/// What a person has chosen for one category on one channel: nothing, as it happens, or gathered
/// into a daily summary. Stored as smallint — do not renumber.
/// </summary>
/// <remarks>
/// Three states rather than two booleans, so "a daily summary of a category whose mail is switched
/// off" cannot be written down at all. That invariant is the reason for the shape: expressed as an
/// enabled bit plus a digest bit it would be a rule somebody has to remember to enforce on every
/// write, and the first write that forgot would be silent.
/// </remarks>
public enum NotificationChannelChoice : short
{
    /// <summary>Do not reach me here about this.</summary>
    Off = 0,

    /// <summary>One message per event, as it happens.</summary>
    Immediate = 1,

    /// <summary>
    /// Gathered into one message a day. Only meaningful on a channel that can hold something back
    /// and batch it; see <see cref="NotificationChannelKinds.CanDefer"/>.
    /// </summary>
    Daily = 2,
}

/// <summary>The channel vocabulary and what each channel can do, in one place.</summary>
public static class NotificationChannelKinds
{
    /// <summary>
    /// Every channel, one bit each, in the order a settings page reads best: the one that always
    /// works first. <see cref="NotificationChannelKind.None"/> is not a channel and is absent.
    /// </summary>
    public static IReadOnlyList<NotificationChannelKind> All { get; } =
    [
        NotificationChannelKind.InApp,
        NotificationChannelKind.Email,
        NotificationChannelKind.Sms,
    ];

    /// <summary>Every channel at once — the widest a ceiling can be.</summary>
    public static NotificationChannelKind Everything { get; } =
        NotificationChannelKind.InApp | NotificationChannelKind.Email | NotificationChannelKind.Sms;

    /// <summary>
    /// Whether a channel can hold messages back and send them as one. Only email can: an inbox
    /// entry appears when the event happens because it is the event's own record, and a batched
    /// text message would be one long message rather than a summary anybody reads.
    /// </summary>
    public static bool CanDefer(NotificationChannelKind channel) =>
        channel is NotificationChannelKind.Email;

    /// <summary>Whether a set names exactly one channel — what a stored preference row must hold.</summary>
    public static bool IsSingle(NotificationChannelKind channel) =>
        channel is not NotificationChannelKind.None &&
        (channel & (channel - 1)) == 0 &&
        (Everything & channel) == channel;

    /// <summary>The bits set in a channel set, as single channels, in <see cref="All"/> order.</summary>
    public static IEnumerable<NotificationChannelKind> Split(NotificationChannelKind channels) =>
        All.Where(channel => (channels & channel) == channel);

    /// <summary>
    /// The matrix channel a delivery row's channel is a preference about. Loud when it is not one:
    /// a transport that leaves the system and has no cell in the matrix is a wiring error that
    /// would otherwise present itself as messages nobody can switch off.
    /// </summary>
    public static NotificationChannelKind Of(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => NotificationChannelKind.Email,
        _ => throw new ArgumentOutOfRangeException(
            nameof(channel), channel, "This delivery channel has no cell in the preference matrix."),
    };
}

/// <summary>The notification vocabulary and its defaults, in one place.</summary>
public static class NotificationCategories
{
    public static IReadOnlyList<NotificationCategory> All { get; } = [.. Enum.GetValues<NotificationCategory>()];

    /// <summary>
    /// Which channels a category may ever use.
    /// </summary>
    /// <remarks>
    /// A shipped constant, not a setting: it says what the category is <em>for</em>, and a
    /// notification about a caving group is not worth a text message whoever is reading it. A
    /// channel outside this set can never be chosen, however a preference row got written — which
    /// is what makes a row left behind by a narrowed ceiling inert rather than a fault.
    /// </remarks>
    public static NotificationChannelKind Ceiling(NotificationCategory category) => category switch
    {
        NotificationCategory.CavingGroupMembership => InboxAndMail,
        NotificationCategory.PermissionGranted => InboxAndMail,
        NotificationCategory.TripParticipation => InboxAndMail,
        NotificationCategory.JobCompleted => InboxAndMail,
        NotificationCategory.SecurityAlerts => InboxAndMail,
        NotificationCategory.TripPlanning => InboxAndMail,
        NotificationCategory.CommentReply => InboxAndMail,
        NotificationCategory.CommentOnMine => InboxAndMail,
        // A category added to the enum but not named here reaches nobody anywhere, rather than
        // surprising everyone with messages they never asked for.
        _ => NotificationChannelKind.None,
    };

    /// <summary>
    /// What a category is set to on a channel for somebody who has never touched their settings.
    /// Also what a channel with no stored row is worth, so a partially-saved matrix behaves
    /// predictably rather than reading as "everything off".
    /// </summary>
    /// <remarks>
    /// Everything a category may use is on, immediately. The inbox is on for every category by
    /// deliberate choice: it costs the reader nothing until they open it, and a person who has
    /// never opened the settings page should still find their notifications somewhere.
    /// </remarks>
    public static NotificationChannelChoice Default(
        NotificationCategory category, NotificationChannelKind channel) =>
        (Ceiling(category) & channel) == channel && NotificationChannelKinds.IsSingle(channel)
            ? NotificationChannelChoice.Immediate
            : NotificationChannelChoice.Off;

    /// <summary>
    /// Whether the user may switch a category off. Some may not: a security alert warns the owner
    /// that their own account is being taken over, and whoever is doing it may hold a live
    /// session — so a category with a safety argument behind it stays on wherever it can reach.
    /// </summary>
    public static bool IsUserConfigurable(NotificationCategory category) =>
        category is not NotificationCategory.SecurityAlerts;

    /// <summary>
    /// Whether a category refuses to be held back for a summary. The same categories nobody may
    /// switch off, for the same reason: a warning that arrives tomorrow morning is not a warning.
    /// </summary>
    public static bool IsAlwaysImmediate(NotificationCategory category) =>
        category is NotificationCategory.SecurityAlerts;

    private const NotificationChannelKind InboxAndMail =
        NotificationChannelKind.InApp | NotificationChannelKind.Email;
}

/// <summary>
/// A user's choice for one notification category on one channel. Unique per
/// (UserId, Category, Channel), which is the whole of what makes the matrix a matrix.
/// </summary>
public class UserNotificationPreference : ITimestamped
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public NotificationCategory Category { get; set; }

    /// <summary>Exactly one channel — never a set, whatever the flags type allows.</summary>
    public NotificationChannelKind Channel { get; set; }

    public NotificationChannelChoice Choice { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
