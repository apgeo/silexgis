// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// When a published trip stops being published.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this answers.</b> A follow link used to end in exactly one way: somebody
/// revoking it. Everything else about it was permanent — no expiry on the row, nothing consulting
/// the watch, no pass over the table — so a trip published in March was still serving its party's
/// last reported positions in November. And the one remedy on offer did not actually cover the
/// disclosure, because the workflow this application hands out puts the token into a club's own
/// article: that HTML is indexable and archivable, so the address outlives the article's author
/// remembering it exists, and revoking closes the live page without touching a copy in an archive.
/// Something has to end a publication without anybody remembering to.
/// </para>
/// <para>
/// <b>Three things govern it, and the earliest of them wins.</b> Each answers a different way the
/// link outlives its purpose, which is why it is a combination rather than one rule:
/// </para>
/// <list type="number">
/// <item>
/// <b>Revocation</b>, which is somebody deciding now. Unchanged, immediate, and still the only
/// thing that ends a publication early.
/// </item>
/// <item>
/// <b>The watch's own end.</b> The page exists so that people can follow a party underground; when
/// the watch is closed there is no party to follow, and when it was never armed there never was
/// one. This is the rule that ends nearly every real publication, and it costs nothing to
/// administer because closing the watch is an act the coordinator already performs.
/// <b>With a grace window</b>, because the instant the watch closes is the instant the families
/// reading the page most want to see it — "everybody out" is the last thing it has to say, and a
/// page that answered nothing at that moment would fail exactly the people it was written for.
/// </item>
/// <item>
/// <b>An expiry fixed when the link is minted</b>, which is the backstop and the reason this is
/// not left to the watch alone. A watch that is never closed is not a hypothetical — it is the
/// ordinary outcome of a trip that ended well and a coordinator who moved on — and under the first
/// two rules alone such a link would still be live years later. The expiry needs nobody to do
/// anything.
/// </item>
/// </list>
/// <para>
/// <b>What the trip's own dates do, and what they deliberately do not.</b> They set where the
/// expiry is counted from, not whether the page answers. A link minted on the first morning of a
/// three-week expedition must not lapse in the middle of it, so the countdown starts at the end of
/// the trip's last planned day rather than at the moment of minting. But the dates are never a gate
/// on their own: a party is overdue precisely when it has departed from its plan, and that is the
/// hour the page matters most. A rule that closed the page when the trip was "supposed" to be over
/// would go dark at the start of a callout.
/// </para>
/// <para>
/// <b>None of this is ever a distinguishable answer.</b> A lapsed link, a revoked one, one whose
/// watch was closed last month and one that never existed all answer the same 404 the surface
/// already gives an invented token. An "expired" refusal would tell a stranger holding a guess
/// that their guess was once real, which is a fact about which links exist.
/// </para>
/// <para>
/// Pure, and asked on every read rather than written onto the row: the same stance the cave's
/// protection refusal takes on the same surface. A verdict stored at mint would go on being true
/// after the watch it was about had closed.
/// </para>
/// </remarks>
public static class TripPublicationWindow
{
    /// <summary>
    /// When a link minted now should lapse, given the trip it is for.
    /// </summary>
    /// <param name="lifetime">
    /// How long a publication outlives the trip it is about. One configured window, applied from
    /// the end of the trip rather than from the mint, so that the same setting means the same thing
    /// for an afternoon in a local cave and for a three-week expedition.
    /// </param>
    /// <remarks>
    /// The trip's dates carry no time zone — they are the days a club wrote down — so the end of
    /// the last one is read as midnight UTC after it. That is approximate by at most a day either
    /// way, which is immaterial against a window measured in weeks and is the only reading that
    /// does not invent a time zone the trip never recorded.
    /// </remarks>
    public static DateTimeOffset ExpiresAtFor(
        DateTimeOffset now, DateOnly tripDate, DateOnly? tripDateEnd, TimeSpan lifetime)
    {
        var afterTheTrip = EndOfTrip(tripDate, tripDateEnd);

        // Whichever is later, so a link minted after the trip still gets its whole window and one
        // minted before the trip gets a window measured from the end of it.
        var expiresAt = (afterTheTrip > now ? afterTheTrip : now) + lifetime;

        // Truncated to whole microseconds, because the instant is stored in a timestamptz column
        // whose resolution is the microsecond. Minting and reading back must say the same end, so
        // the sub-microsecond ticks the clock happens to carry are dropped here, at the one place
        // the instant is decided, rather than rounded away differently on each surface.
        return expiresAt.AddTicks(-(expiresAt.Ticks % 10));
    }

    /// <summary>
    /// The instant a trip is over: midnight UTC after its last planned day.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracted rather than spelled twice because two different windows now count from it — the
    /// expiry a link is minted with, and how long the trip stays readable as a past track — and
    /// two readings of "when was the trip over" that drifted by a day would be invisible in both.
    /// </para>
    /// <para>
    /// The dates carry no time zone; they are the days a club wrote down, so the end of the last
    /// one is read as midnight UTC after it. Approximate by at most a day either way, which is
    /// immaterial against windows measured in weeks and is the only reading that does not invent a
    /// time zone the trip never recorded. A recorded end earlier than the start is nonsense
    /// somebody typed and is ignored rather than obeyed — reading it literally would shorten a
    /// window on the strength of a typing mistake.
    /// </para>
    /// </remarks>
    public static DateTimeOffset EndOfTrip(DateOnly tripDate, DateOnly? tripDateEnd)
    {
        var lastDay = tripDateEnd is { } end && end > tripDate ? end : tripDate;
        return new DateTimeOffset(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    /// <summary>
    /// Whether a link opens the page at this instant.
    /// </summary>
    /// <param name="state">
    /// The watch. <see cref="TripTrackingState.Armed"/> is the live case;
    /// <see cref="TripTrackingState.Closed"/> keeps answering for <paramref name="graceAfterClose"/>
    /// so the page can say the party is out; <see cref="TripTrackingState.Off"/> answers nothing at
    /// all, because a watch that was never armed has nothing to follow and one that was stood down
    /// was stood down deliberately.
    /// </param>
    /// <param name="closedAt">
    /// When the watch closed. A watch recorded as closed with no instant against it is treated as
    /// closed long ago rather than as closed just now — the fail-closed reading, and the only safe
    /// one for a row whose history is incomplete.
    /// </param>
    public static bool IsOpen(
        DateTimeOffset now,
        DateTimeOffset? revokedAt,
        DateTimeOffset expiresAt,
        TripTrackingState state,
        DateTimeOffset? closedAt,
        TimeSpan graceAfterClose)
    {
        if (revokedAt is not null) return false;
        if (now >= expiresAt) return false;

        return state switch
        {
            TripTrackingState.Armed => true,
            TripTrackingState.Closed => closedAt is { } closed && now < closed + graceAfterClose,
            _ => false,
        };
    }
}
