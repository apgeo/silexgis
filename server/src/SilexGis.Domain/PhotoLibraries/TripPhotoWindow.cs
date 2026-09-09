// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// The stretch of time a neighbouring library is asked about, as two instants.
/// </summary>
/// <remarks>
/// <para>
/// Instants rather than dates, because that is what the two products compare against: each holds a
/// moment for every photograph and answers "taken between these" by comparing it. A window
/// expressed as two dates would have to be turned into instants somewhere, and doing it in two
/// client classes is how the two libraries end up disagreeing about which day a trip was.
/// </para>
/// <para>
/// Both ends are always present. There is deliberately no open end anywhere in this type: a
/// half-bounded window put to a club's library is that library wearing somebody's name, and it
/// pages and counts exactly like a real answer, so nothing on the screen it lands on would show
/// the difference.
/// </para>
/// </remarks>
/// <param name="From">The first instant asked about, in UTC.</param>
/// <param name="To">The last, in UTC. After <paramref name="From"/> by construction.</param>
public readonly record struct LibraryPhotoWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// The window of time to ask a photo library about for one trip, worked out from the trip's own
/// dates.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this application stores, which is where the difficulty is.</b> A trip records the
/// calendar day it happened on and, when it ran on past that, the day it ended. Those are
/// <em>dates</em>: no clock time and no time zone. It also records when the party went underground
/// and came back out, but those are wall-clock times carrying no day and no zone either. A
/// photograph, meanwhile, is stamped by a camera and held by the library as an instant. So the two
/// sides are not in the same frame and there is nothing stored anywhere that would convert one
/// into the other exactly.
/// </para>
/// <para>
/// <b>So the window is whole days, widened by a day at each end, and neither of those is
/// arbitrary.</b>
/// </para>
/// <para>
/// Whole days, because the two clock times a trip carries are the wrong times to narrow it with.
/// They are the moments the party went <em>underground</em> and came back out — not the moments the
/// trip began and ended — so a window narrowed to them would drop the photograph of the entrance
/// taken twenty minutes before going in, which is the photograph somebody opening this panel is
/// most likely looking for. They also carry no zone, so turning one into an instant means inventing
/// one, and an invented zone is wrong by however much it was wrong by, silently, at both edges.
/// </para>
/// <para>
/// A day at each end, because a calendar day in one frame and the same calendar day in another can
/// be displaced by up to fourteen hours: zone offsets in use run from twelve hours behind to
/// fourteen ahead. A day of margin covers every one of them with room to spare, and it covers a
/// second uncertainty for free — whether the library compares against the instant it holds or
/// against the wall-clock reading it recorded beside it, which differs by less than a day whichever
/// of the two it is.
/// </para>
/// <para>
/// <b>The margin is the honest direction to be wrong in.</b> Too wide shows a few photographs from
/// the evening before, each carrying its own date where a reader can see it. Too narrow drops the
/// end of a long push and reports a smaller number, and nothing on the screen would say so. The
/// surface that draws this says a day either side is included rather than leaving it to be noticed.
/// </para>
/// </remarks>
public static class TripPhotoWindow
{
    /// <summary>
    /// How far past each end of a trip the window reaches, in days.
    /// </summary>
    /// <remarks>
    /// One day. Named rather than written into the arithmetic twice, because the two ends have to
    /// move together: a margin applied to one end only produces a window that is right for trips in
    /// one half of the world and short at one edge for the other, which is the failure this exists
    /// to prevent, arrived at while looking like the fix for it.
    /// </remarks>
    public const int MarginDays = 1;

    /// <summary>
    /// The longest run of days a <em>trip</em> may cover for a window to be made of it, counting
    /// both ends.
    /// </summary>
    /// <remarks>
    /// A measurement of the trip and not of the window it produces, which is the wider of the two:
    /// the check is made before the margin is added, so a trip at the cap yields a window of this
    /// many days plus one at each end. Written that way round on purpose — the number is here to
    /// say which records are too long to be believed, and a reader looking at a trip's dates should
    /// be able to compare them against it directly.
    ///
    /// <para>
    /// A year and a day, which is longer than any expedition and far shorter than a mistyped year.
    /// The case it exists for is a trip whose end date was entered with the wrong year: that record
    /// is not refused anywhere, it looks ordinary in a list, and the window it produces asks a club's
    /// library for a decade — which comes back as a full page, pages correctly, and would be
    /// presented as the photographs of one weekend. A window that is really the whole library is the
    /// one wrong answer this feature must not give, so a span past this is refused rather than
    /// trimmed to something plausible: trimming would answer a question nobody asked and leave the
    /// wrong date in place, and refusing says which record needs looking at.
    /// </para>
    /// </remarks>
    public const int MaxSpanDays = 366;

    /// <summary>
    /// The window for a trip that started on <paramref name="tripDate"/> and, where it says so,
    /// ran on to <paramref name="tripDateEnd"/> — or null when the trip's dates do not describe a
    /// stretch of time this application will ask a library about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An absent end is not an open end.</b> A trip that names no end day is a trip that ran on
    /// no further than the day it started — which is how everything else in this application already
    /// reads that absence, including the calculation of how long the party was underground — so the
    /// window closes on the start day rather than running to now. The alternative would be a panel
    /// on any unfinished trip showing every photograph taken since, under a heading claiming they
    /// were taken on it.
    /// </para>
    /// <para>
    /// <b>An absent start is a different thing and is refused.</b> A trip must carry a start day, so
    /// there is no such row to serve — but a day that was never filled in arrives as the calendar's
    /// own first day rather than as nothing, and a window around the year one is a question worth
    /// not asking somebody else's server.
    /// </para>
    /// <para>
    /// <b>An end before the start is refused too.</b> Nothing at the database level forbids one, so
    /// a record written before that rule existed, or brought in from elsewhere, can hold one. The
    /// two orders it could be read in describe different trips and there is no way to tell which was
    /// meant, so neither is guessed at.
    /// </para>
    /// </remarks>
    public static LibraryPhotoWindow? For(DateOnly tripDate, DateOnly? tripDateEnd)
    {
        var last = tripDateEnd ?? tripDate;

        if (last < tripDate)
        {
            return null;
        }

        // Counting both ends: a trip that starts and ends on one day spans one day, not none.
        if (last.DayNumber - tripDate.DayNumber + 1 > MaxSpanDays)
        {
            return null;
        }

        // The margin has to fit inside the calendar at both ends, and the check is what makes the
        // arithmetic below total rather than throwing on a date nobody expected to see. It is also
        // where a trip whose start day was never filled in is turned away: an unset day is the
        // calendar's first, which has no day before it to widen into.
        if (tripDate < DateOnly.MinValue.AddDays(MarginDays)
            || last > DateOnly.MaxValue.AddDays(-(MarginDays + 1)))
        {
            return null;
        }

        // Midnight to midnight, in UTC, with the margin outside both. The upper end is the start of
        // the day after the last one the trip covers, so the whole of that last day is inside the
        // window whether or not a product treats its own upper bound as inclusive — a product that
        // stops at the instant is given exactly the window, and one that rounds up to the end of a
        // day is given one more day, and neither loses a photograph.
        var from = new DateTimeOffset(
            tripDate.AddDays(-MarginDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var to = new DateTimeOffset(
            last.AddDays(MarginDays + 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return new LibraryPhotoWindow(from, to);
    }
}
