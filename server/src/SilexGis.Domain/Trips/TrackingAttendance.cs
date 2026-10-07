// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// One line of a tracking log as far as time underground is concerned: what kind of report it was
/// and the instant it speaks of. Nothing about where — a stay underground is counted from the two
/// reports that say somebody went in and came out, and a place is neither.
/// </summary>
public readonly record struct TrackingPassage(TripPositionEventKind Kind, DateTimeOffset At);

/// <summary>One stay underground: the report that somebody went in and the next that they came out.</summary>
public readonly record struct TrackingStay(DateTimeOffset Entered, DateTimeOffset Exited)
{
    /// <summary>
    /// Whole minutes between the two, each instant cut to its minute first.
    /// </summary>
    /// <remarks>
    /// Cut rather than rounded, and cut before subtracting: the times a person types on a roster
    /// are kept and compared to the minute with their seconds dropped, so a stay read from a log
    /// has to come to the same number that roster would give for the same two moments. Subtracting
    /// first and cutting afterwards differs from it by a minute whenever the exit's seconds are
    /// fewer than the entry's.
    /// </remarks>
    public int Minutes =>
        (int)((Exited.UtcTicks / TimeSpan.TicksPerMinute) - (Entered.UtcTicks / TimeSpan.TicksPerMinute));
}

/// <summary>
/// How long one person was underground on one trip, read from that trip's tracking log.
/// </summary>
/// <remarks>
/// <para>
/// A stay is an entry and the first exit after it. The reports are instants, so nothing here has
/// to guess a midnight: a stay that began on Saturday evening and ended on Sunday morning is the
/// difference between two moments, whatever the trip's own dates say.
/// </para>
/// <para>
/// <b>An entry nobody closed adds nothing.</b> Not zero, and not "until now": a person whose exit
/// was never written down was underground for a time the log does not know, and any number put in
/// its place would be added to a total as though it were known. A watch still running and a log
/// somebody stopped keeping look the same from here, and neither is a duration. For the same
/// reason an exit with no entry before it adds nothing, and a place reported with no entry does
/// not stand in for one — the moment somebody was first heard of inside is not the moment they
/// went in.
/// </para>
/// <para>
/// <b>Reports that say the same thing twice are one statement.</b> Two reports about one person at
/// one instant are legal in a log (two coordinators writing down the same radio call), and so is
/// an entry repeated an hour later by somebody who had not seen the first. While a person is in, a
/// further entry changes nothing — they have been in since the first; once they are out, a further
/// exit changes nothing. So a doubled report can neither count a stay twice nor shorten it.
/// </para>
/// <para>
/// <b>Order.</b> Reports are read by the instant they speak of, and reports that share an instant
/// are read in the order they were handed in. A caller passes them in the log's own order — by
/// instant, then by when each was written — so an exit and an entry sharing an instant resolve the
/// way the log itself lists them, and the same log always comes to the same figure.
/// </para>
/// </remarks>
public static class TrackingAttendance
{
    /// <summary>
    /// The completed stays in one person's reports on one trip, earliest first. Reports of any
    /// kind other than an entry or an exit are passed over.
    /// </summary>
    public static IReadOnlyList<TrackingStay> StaysOf(IEnumerable<TrackingPassage>? reports)
    {
        if (reports is null) return [];

        var stays = new List<TrackingStay>();
        DateTimeOffset? enteredAt = null;

        // OrderBy is a stable sort: reports sharing an instant keep the order they came in.
        foreach (var report in reports.OrderBy(r => r.At.UtcTicks))
        {
            switch (report.Kind)
            {
                case TripPositionEventKind.Entered:
                    enteredAt ??= report.At;
                    break;
                case TripPositionEventKind.Exited when enteredAt is { } entered:
                    stays.Add(new TrackingStay(entered, report.At));
                    enteredAt = null;
                    break;
                default:
                    break;
            }
        }

        return stays;
    }

    /// <summary>
    /// Minutes underground across the completed stays, or null when there is not one. Null is not
    /// zero: zero would say the person was timed and spent no time inside, and a count of the
    /// people a total covers would then include somebody the log says nothing usable about.
    /// </summary>
    public static int? UndergroundMinutes(IEnumerable<TrackingPassage>? reports)
    {
        var stays = StaysOf(reports);
        return stays.Count == 0 ? null : stays.Sum(s => s.Minutes);
    }
}
