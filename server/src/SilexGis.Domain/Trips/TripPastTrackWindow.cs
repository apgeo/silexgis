// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// When a trip that was published stops being readable as a past track, and when the link that
/// was handed out stops opening the past at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two windows, not one, and the difference between them is the whole of this file.</b>
/// <see cref="TripPublicationWindow"/> answers <em>may this link follow a party right now</em>;
/// this one answers <em>may this link read trips of the same cave that are already over</em>.
/// They are deliberately separate lifetimes with separate settings, because they protect
/// different things and end at different times.
/// </para>
/// <list type="number">
/// <item>
/// <b>The live window is the short one and it is not widened by anything here.</b> It exists
/// because a token sitting in a club's indexed, archived article must stop serving the positions
/// of a party who are underground <em>now</em>. Nothing in this file grants a capability, extends
/// an expiry, or resurrects a revoked link for the live page.
/// </item>
/// <item>
/// <b>The past window outlives it, on purpose.</b> The archive is the club's own published
/// history, asked for so that it can be read over time. Gating it on the live window instead
/// would mean an article's past-track picker worked only during the few hours a year some party
/// of that cave happened to be underground, which is to say almost never — and a feature that is
/// reachable almost never is not the feature that was asked for.
/// </item>
/// </list>
/// <para>
/// <b>What the split must not cost, and does not.</b> A link outside <em>both</em> windows answers
/// exactly what an invented token answers: the same 404, the same code, the same body. So a
/// stranger who finds a token in an old article still cannot tell a once-real one from one they
/// made up — which is the property the live window was protecting, kept intact. What is dropped is
/// only the coupling that made the archive vanish whenever no party was underground.
/// </para>
/// <para>
/// Pure and asked on every read, never written onto a row: a verdict stored when a link was minted
/// would go on being true after the watch it was about had closed, after retention had run out,
/// and after somebody revoked the publication.
/// </para>
/// </remarks>
public static class TripPastTrackWindow
{
    /// <summary>
    /// Whether a trip of the token's cave may be listed and played back as a past track.
    /// </summary>
    /// <param name="state">
    /// The watch. <see cref="TripTrackingState.Armed"/> is never past, whatever else is true.
    /// </param>
    /// <param name="latestUnrevokedExpiry">
    /// The latest expiry among this trip's links that nobody has revoked, or <c>null</c> when the
    /// trip was never published or every link of it has been withdrawn.
    /// </param>
    /// <param name="retention">
    /// How long a past track stays readable, counted from the end of the trip's last day, or
    /// <c>null</c> for no limit.
    /// </param>
    /// <remarks>
    /// <para>
    /// Four clauses, all required, and each one earns its place:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>It was published and not withdrawn</b> — there is at least one link of it that nobody
    /// revoked, which is what <paramref name="latestUnrevokedExpiry"/> being non-null says. There
    /// is no separate archive flag and no second act: publication is the act, read off the rows it
    /// already writes. It follows that <b>revoking every link of a trip takes it out of the
    /// archive</b> — not a new power, just revocation read as meaning what it says, and the only
    /// withdrawal path there is.
    /// </item>
    /// <item>
    /// <b>The watch is not armed.</b> <em>This is not implied by the clause below and must never be
    /// dropped.</em> The live window closes for a link that has lapsed even while the watch is
    /// still running — so a party still underground on a fortnight-old link would fall straight
    /// into the archive, and their positions would be readable by anybody holding a token to any
    /// other trip of the same cave. That is precisely the disclosure the live window exists to
    /// prevent, reached through a side door.
    /// </item>
    /// <item>
    /// <b>The live window is over</b>, asked through <see cref="TripPublicationWindow.IsOpen"/>
    /// rather than restated, so the archive begins exactly where the live page ends. No trip is
    /// ever both followable and past-readable, the handover is clean, and the grace window after a
    /// watch closes belongs entirely to the live page — "everybody is out" is the last thing it
    /// has to say, and only after it has said it does the trip become history.
    /// </item>
    /// <item>
    /// <b>It is inside retention</b>, counted from the end of the trip rather than from whenever a
    /// link happened to be minted, through the same
    /// <see cref="TripPublicationWindow.EndOfTrip"/> the expiry counts from.
    /// </item>
    /// </list>
    /// <para>
    /// <b>The trip's dates are not a gate</b>, for the reason the live window already argues at
    /// length: a party is overdue precisely when it has departed from its plan, so a rule that
    /// called a trip over because its planned last day had passed would put a live, overdue party
    /// into an anonymous archive at the very hour a callout began. Dates are used for one thing
    /// only — the origin of the retention countdown.
    /// </para>
    /// <para>
    /// <b>A closed state is not a gate either.</b> A watch nobody closes is the ordinary outcome of
    /// a trip that went fine, so an archive that required <see cref="TripTrackingState.Closed"/>
    /// would be near-empty on a real installation and would fail by showing nothing rather than by
    /// complaining.
    /// </para>
    /// </remarks>
    public static bool IsReadableAsPast(
        DateTimeOffset now,
        TripTrackingState state,
        DateTimeOffset? closedAt,
        DateTimeOffset? latestUnrevokedExpiry,
        DateOnly tripDate,
        DateOnly? tripDateEnd,
        TimeSpan graceAfterClose,
        TimeSpan? retention)
    {
        // Never published, or every link of it withdrawn.
        if (latestUnrevokedExpiry is not { } expiry) return false;

        // Somebody is being followed right now. Not implied by the live window below.
        if (state == TripTrackingState.Armed) return false;

        // Still live: the page has it, the archive does not.
        if (TripPublicationWindow.IsOpen(now, revokedAt: null, expiry, state, closedAt, graceAfterClose))
        {
            return false;
        }

        return WithinRetention(now, tripDate, tripDateEnd, retention);
    }

    /// <summary>
    /// Whether one link opens the past-track routes at this instant — the token gate, and the
    /// only place the two windows are put side by side.
    /// </summary>
    /// <param name="revokedAt">When this link was revoked, or null. Revocation ends both windows.</param>
    /// <param name="expiresAt">This link's own expiry, for the live half of the question.</param>
    /// <param name="latestUnrevokedExpiry">
    /// The latest expiry among unrevoked links of the <em>same</em> trip, for the past half — the
    /// same reading the list takes of every other trip, so a trip cannot be past by one measure
    /// and live by another.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Open while either window is open, and the two are asked separately on purpose.</b> While
    /// the link's own trip is still being followed the visitor is standing on a live page and
    /// picking history from it; once that trip is itself over, the same link goes on opening the
    /// archive for as long as the archive lasts. One address, two lifetimes, and the second one
    /// starts where the first ends.
    /// </para>
    /// <para>
    /// <b>Outside both, this says no, and no is the answer an invented token gets.</b> Whoever
    /// calls this must answer a refusal here exactly as it answers an unknown token — same status,
    /// same code, no detail — because a distinguishable refusal would tell a stranger holding a
    /// guess that their guess was once real.
    /// </para>
    /// <para>
    /// <b>A revoked link opens neither.</b> Stated here as well as inside the live rule, because
    /// the past half is asked about the trip rather than about this link, and a trip with a second,
    /// unrevoked link would otherwise keep answering a link somebody deliberately withdrew.
    /// </para>
    /// <para>
    /// <b>A still-armed watch on a lapsed link opens nothing</b> — the live half refuses it because
    /// the link has run out, and the past half refuses it because the watch is armed. That is the
    /// fail-closed corner and it is the right answer: a party underground is neither followable on
    /// a dead link nor history.
    /// </para>
    /// </remarks>
    public static bool OpensThePast(
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
        if (revokedAt is not null) return false;

        // Window one: this link still follows its own party. The picker hangs off a live page.
        if (TripPublicationWindow.IsOpen(now, revokedAt, expiresAt, state, closedAt, graceAfterClose))
        {
            return true;
        }

        // Window two: this link's own trip has become history, and the history is still readable.
        return IsReadableAsPast(
            now, state, closedAt, latestUnrevokedExpiry, tripDate, tripDateEnd, graceAfterClose, retention);
    }

    /// <summary>
    /// Whether a trip that is over is still inside the archive's own lifetime.
    /// </summary>
    /// <remarks>
    /// Counted from the end of the trip rather than from a mint or from a watch closing, so one
    /// configured number means the same thing for an afternoon in a local cave and for a
    /// three-week expedition — and read through the same
    /// <see cref="TripPublicationWindow.EndOfTrip"/> the live expiry counts from, so the two
    /// windows cannot disagree about when the trip was over.
    /// </remarks>
    public static bool WithinRetention(
        DateTimeOffset now, DateOnly tripDate, DateOnly? tripDateEnd, TimeSpan? retention) =>
        retention is not { } window
        || now < TripPublicationWindow.EndOfTrip(tripDate, tripDateEnd) + window;
}
