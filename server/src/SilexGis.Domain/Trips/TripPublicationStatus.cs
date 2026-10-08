// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// What one published link does for whoever holds it, at one instant, in one word.
/// </summary>
/// <remarks>
/// Declared in the order a reader would rank them — the links doing the most first, the ones doing
/// nothing last — so that sorting by this is sorting by how much attention a link deserves.
/// </remarks>
public enum PublishedLinkStatus : short
{
    /// <summary>The watch is running and the link follows the party underground now.</summary>
    Followable = 0,

    /// <summary>
    /// The watch has closed and the link still opens the page for the short window in which it says
    /// that everybody is out.
    /// </summary>
    InGrace = 1,

    /// <summary>
    /// The trip is over and the link opens it, and the other finished trips of its cave, as history.
    /// </summary>
    InArchive = 2,

    /// <summary>
    /// The link would open something were it not for its cave: the watch has lost the cave it was
    /// anchored to, or that cave is now position-protected. Every route answers such a link exactly
    /// as it answers an invented one, for as long as that holds and no longer.
    /// </summary>
    Withheld = 3,

    /// <summary>
    /// Nobody took the link back and it opens nothing: it ran out on a watch that is still running,
    /// its watch was stood down, the archive is switched off, or the archive's retention has passed.
    /// </summary>
    Lapsed = 4,

    /// <summary>Somebody took the link back. It opens nothing and never will again.</summary>
    Revoked = 5,
}

/// <summary>
/// Which of a link's two windows is open at one instant.
/// </summary>
/// <param name="Live">
/// The link still follows its own party: the watch is armed, or closed and inside its grace window.
/// </param>
/// <param name="Past">
/// The link's own trip has become history that is still inside the archive's retention. Never true
/// together with <paramref name="Live"/>.
/// </param>
public readonly record struct PublishedLinkWindows(bool Live, bool Past)
{
    /// <summary>True when neither window is open — the answer an invented token gets.</summary>
    public bool Neither => !Live && !Past;

    /// <summary>
    /// Whether the link opens anything on an installation that does, or does not, serve past trips.
    /// </summary>
    /// <param name="archiveEnabled">Whether this installation serves past trips at all.</param>
    /// <remarks>
    /// Following a party is never switched off; an open past window is worth something only while
    /// the archive is on. This is the one reading of the two windows under that switch: the
    /// administrator's list tells a link in the archive from one that opens nothing by it, and the
    /// route that lists the parties underground now starts from it in
    /// <see cref="OpensTheFollowedList"/>. Written in each of those places, a change to the
    /// route's gate would leave the list describing a link by a rule the route no longer applies.
    /// </remarks>
    public bool OpensAnything(bool archiveEnabled) => Live || (archiveEnabled && Past);

    /// <summary>
    /// Whether the link opens the list of parties being followed in its cave right now.
    /// </summary>
    /// <param name="archiveEnabled">Whether this installation serves past trips at all.</param>
    /// <param name="withinSiblingWindow">
    /// Whether the link's own trip ended recently enough for a lapsed link to go on listing others
    /// — <see cref="TripPublicationWindow.WithinSiblingWindow"/>, true wherever no period is set.
    /// </param>
    /// <remarks>
    /// <para>
    /// A link still following its own party opens the list outright, whatever the two arguments
    /// say: neither the archive's switch nor the period after a lapse is about a party that is
    /// underground. A link whose trip is over opens it while it opens the archive and is still
    /// inside that period.
    /// </para>
    /// <para>
    /// <b>Never wider than <see cref="OpensAnything"/></b>, and that is what keeps the
    /// administrator's list honest: every link this opens the followed list for is one that list
    /// calls open. The converse does not hold once a period is set — a link past it still opens its
    /// cave's past trips and is described as in the archive, which it is; what it has lost is the
    /// view of who is underground now, and the installation's log says so under a reason of its
    /// own.
    /// </para>
    /// </remarks>
    public bool OpensTheFollowedList(bool archiveEnabled, bool withinSiblingWindow) =>
        Live || (archiveEnabled && Past && withinSiblingWindow);
}

/// <summary>
/// The one place a published link's two windows are put side by side and read as a verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside the two window rules rather than inside either.</b>
/// <see cref="TripPublicationWindow"/> says when a link follows a party and
/// <see cref="TripPastTrackWindow"/> says when a trip is readable history. Two different readers
/// need both answers about the same link at the same instant: the anonymous routes, which refuse or
/// serve by them, and the administrator's list of everything published, which has to say what each
/// link is doing. Composed separately in each place they would be two descriptions of one thing —
/// and the list would sooner or later call a link "followable" that the page it describes answers
/// with a 404. So the composition lives here, both call it, and neither window rule is restated:
/// this asks them.
/// </para>
/// <para>
/// <b>The windows are handed back apart, not folded into one yes or no,</b> because the routes that
/// share them do not apply the same installation switch to both. Following a party is the core of
/// the surface; reading a club's history is something an installation may switch off, and switching
/// it off must not stop anybody following tonight's party. Each route therefore decides what an
/// open past window is worth to it; <see cref="OfLink"/> is the reading for a caller that wants the
/// outcome and not the parts.
/// </para>
/// <para>
/// Pure, and asked again on every read. A status written onto a row would go on being true after
/// the watch had closed, the retention had run out or the cave had been protected.
/// </para>
/// </remarks>
public static class TripPublicationStatus
{
    /// <summary>
    /// The two windows of one link.
    /// </summary>
    /// <param name="revokedAt">When this link was revoked, or null. Revocation shuts both windows.</param>
    /// <param name="expiresAt">This link's own expiry, which governs the live window.</param>
    /// <param name="latestUnrevokedExpiry">
    /// The latest expiry among the unrevoked links of the same trip, which is how "has this trip
    /// been published at all" is read for the past window — of this trip exactly as of every other.
    /// </param>
    /// <remarks>
    /// A revoked link is asked first and answered without consulting either rule, because only one
    /// of the two takes a revocation as an argument: the archive's rule is about the trip's
    /// remaining links, and a trip with a second, unrevoked link would otherwise keep answering a
    /// link somebody deliberately withdrew.
    /// </remarks>
    public static PublishedLinkWindows WindowsOf(
        DateTimeOffset now,
        DateTimeOffset? revokedAt,
        DateTimeOffset expiresAt,
        TripTrackingState state,
        DateTimeOffset? closedAt,
        DateTimeOffset? latestUnrevokedExpiry,
        DateOnly tripDate,
        DateOnly? tripDateEnd,
        TimeSpan graceAfterClose,
        TimeSpan? retention)
    {
        if (revokedAt is not null) return default;

        var live = TripPublicationWindow.IsOpen(now, revokedAt, expiresAt, state, closedAt, graceAfterClose);
        var past = TripPastTrackWindow.IsReadableAsPast(
            now, state, closedAt, latestUnrevokedExpiry, tripDate, tripDateEnd, graceAfterClose, retention);

        return new PublishedLinkWindows(live, past);
    }

    /// <summary>
    /// What one link does for its holder at this instant.
    /// </summary>
    /// <param name="archiveEnabled">
    /// Whether this installation serves past trips at all. With the archive off, a link whose live
    /// window has shut opens nothing, whatever the past window says.
    /// </param>
    /// <param name="cavePublishable">
    /// Whether the watch is anchored to a cave that may be published right now — present, a cave,
    /// and under no position protection. The caller decides it, because deciding it takes the
    /// database; it is passed in so that this stays a pure function of what it is given.
    /// </param>
    /// <remarks>
    /// <para>
    /// Asked in the order the routes ask: the revocation, then the windows, and the cave last. So
    /// <see cref="PublishedLinkStatus.Withheld"/> is said only of a link that would otherwise open
    /// something — a link that has lapsed on a protected cave is lapsed, and unprotecting the cave
    /// would bring nothing back.
    /// </para>
    /// <para>
    /// <see cref="PublishedLinkStatus.Followable"/> and <see cref="PublishedLinkStatus.InGrace"/>
    /// are the two halves of the live window, told apart by the watch: the rule that opens it has
    /// exactly those two cases.
    /// </para>
    /// </remarks>
    public static PublishedLinkStatus OfLink(
        DateTimeOffset now,
        DateTimeOffset? revokedAt,
        DateTimeOffset expiresAt,
        TripTrackingState state,
        DateTimeOffset? closedAt,
        DateTimeOffset? latestUnrevokedExpiry,
        DateOnly tripDate,
        DateOnly? tripDateEnd,
        TimeSpan graceAfterClose,
        bool archiveEnabled,
        TimeSpan? retention,
        bool cavePublishable)
    {
        if (revokedAt is not null) return PublishedLinkStatus.Revoked;

        var windows = WindowsOf(
            now, revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry,
            tripDate, tripDateEnd, graceAfterClose, retention);

        if (!windows.OpensAnything(archiveEnabled)) return PublishedLinkStatus.Lapsed;
        if (!cavePublishable) return PublishedLinkStatus.Withheld;

        if (!windows.Live) return PublishedLinkStatus.InArchive;

        return state == TripTrackingState.Armed
            ? PublishedLinkStatus.Followable
            : PublishedLinkStatus.InGrace;
    }
}
