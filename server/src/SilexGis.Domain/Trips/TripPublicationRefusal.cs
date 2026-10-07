// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// Why one read of a published trip was refused, for whoever runs the installation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A vocabulary about a request, not about a link, and never part of an answer.</b> Every one of
/// these is answered to the caller as the single 404 an invented token gets — that is the property
/// the published surface is built on, and nothing here loosens it. The reason exists so that an
/// operator asked "why does the page show nothing" can read it in the installation's own log
/// instead of reconstructing it from five tables.
/// </para>
/// <para>
/// It is deliberately not <see cref="PublishedLinkStatus"/>. That one describes what a link does
/// for its holder and so has no word for a token that names no link at all, and it folds together
/// — as "lapsed" — causes an operator needs told apart: the link's own end, a watch that is over,
/// a history that has aged out, an archive that is switched off.
/// </para>
/// </remarks>
public enum PublishedReadRefusal : short
{
    /// <summary>
    /// The token names no link of this installation: mistyped, cut short, of another installation,
    /// or of a trip or a watch that no longer exists.
    /// </summary>
    UnknownLink = 0,

    /// <summary>Somebody took the link back.</summary>
    Revoked = 1,

    /// <summary>
    /// The link's own end has passed while its trip is not yet history: the watch is still running,
    /// or another link of the same trip is still following it.
    /// </summary>
    Expired = 2,

    /// <summary>The watch is switched off — never started, or stood down — so there is nobody to follow.</summary>
    WatchOff = 3,

    /// <summary>
    /// The watch has closed and the short window in which the followed page says that everybody is
    /// out has passed. Said only of the followed page: the same link may well open the archive.
    /// </summary>
    ClosedPastGrace = 4,

    /// <summary>The trip is over and older than the archive's retention.</summary>
    PastRetention = 5,

    /// <summary>
    /// The link would open something were it not for its cave: the watch has lost the cave it was
    /// anchored to, or that cave is now position-protected.
    /// </summary>
    CaveWithheld = 6,

    /// <summary>
    /// The link would open the archive, and this installation has the archive switched off.
    /// </summary>
    ArchiveOff = 7,

    /// <summary>
    /// The link is good and the past trip it asked for is not one it may read: a trip of another
    /// cave, one that does not exist, one never published or wholly withdrawn, one still being
    /// followed, or one older than the retention.
    /// </summary>
    TripNotInArchive = 8,

    /// <summary>
    /// The link's own trip ended longer ago than this installation lets a lapsed link go on
    /// listing who is in the cave now. Said only of that list: the same link still opens its
    /// cave's past trips.
    /// </summary>
    PastSiblingWindow = 9,
}

/// <summary>
/// Why a link's windows are shut, read off the same facts the windows themselves were decided by.
/// </summary>
/// <remarks>
/// <para>
/// <b>These explain a decision; they never make one.</b> Each asks the rule that owns the decision
/// first and answers null when that rule says open, so a route that consulted only this could not
/// come to refuse a read the window serves, nor to serve one it refuses. What is added here is the
/// order in which the causes are named when more than one holds, which is the order the rules
/// themselves test them in: a revocation, then the link's own end, then the watch.
/// </para>
/// <para>
/// Pure functions of values the routes have already read. Naming a reason therefore costs no
/// query — which matters, because a token that was once real must cost the database exactly what
/// an invented one costs.
/// </para>
/// </remarks>
public static class TripPublicationRefusal
{
    /// <summary>
    /// Why this link does not follow its party right now, or null when it does.
    /// </summary>
    public static PublishedReadRefusal? OfLiveWindow(
        DateTimeOffset now,
        DateTimeOffset? revokedAt,
        DateTimeOffset expiresAt,
        TripTrackingState state,
        DateTimeOffset? closedAt,
        TimeSpan graceAfterClose)
    {
        if (TripPublicationWindow.IsOpen(now, revokedAt, expiresAt, state, closedAt, graceAfterClose))
        {
            return null;
        }

        if (revokedAt is not null) return PublishedReadRefusal.Revoked;
        if (now >= expiresAt) return PublishedReadRefusal.Expired;

        // Not revoked and not run out, so it is the watch: closed with its grace over, or not
        // running at all.
        return state == TripTrackingState.Closed
            ? PublishedReadRefusal.ClosedPastGrace
            : PublishedReadRefusal.WatchOff;
    }

    /// <summary>
    /// Why this link opens neither its party nor its cave's history, or null when it opens either.
    /// </summary>
    /// <param name="latestUnrevokedExpiry">
    /// The latest expiry among the unrevoked links of the same trip, as the archive's rule reads it.
    /// </param>
    /// <remarks>
    /// A closed watch past its grace is not a reason here, and neither is a watch switched off:
    /// those shut the followed page and are exactly what turns a trip into history. So an unrevoked
    /// link with both windows shut is one of two things. Its trip is not history yet — still being
    /// followed, by a running watch or through a later link — and this link has simply run out. Or
    /// its trip is history that has aged past the retention.
    /// </remarks>
    public static PublishedReadRefusal? OfBothWindows(
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
        var windows = TripPublicationStatus.WindowsOf(
            now, revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry,
            tripDate, tripDateEnd, graceAfterClose, retention);
        if (!windows.Neither) return null;

        if (revokedAt is not null) return PublishedReadRefusal.Revoked;

        var stillFollowed = state == TripTrackingState.Armed
            || TripPublicationWindow.IsFollowableByAnyLink(
                now, latestUnrevokedExpiry, state, closedAt, graceAfterClose);
        if (stillFollowed) return PublishedReadRefusal.Expired;

        return TripPastTrackWindow.WithinRetention(now, tripDate, tripDateEnd, retention)
            // Inside the retention with both windows shut: the archive's rule found no unrevoked
            // link of the trip at all. An unrevoked link is itself one, so no stored row reads this
            // way; said of the link's own end because that is the only fact left to name.
            ? PublishedReadRefusal.Expired
            : PublishedReadRefusal.PastRetention;
    }
}
