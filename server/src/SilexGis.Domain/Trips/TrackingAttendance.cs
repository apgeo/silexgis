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
/// What a tracking log says about when one person went in and came out, as the two moments a
/// roster has room for: the first report that they went in, and the last that they came out.
/// </summary>
/// <param name="Entered">The first report that the person went in, or null when there is none.</param>
/// <param name="Exited">
/// The last report that the person came out, or null when there is none — or when a later report
/// says they went in again and nothing has closed that.
/// </param>
/// <param name="Stays">
/// How many completed stays lie between the two. More than one means the pair spans time spent on
/// the surface, which a roster's two times cannot leave out.
/// </param>
public readonly record struct TrackingEntryExit(DateTimeOffset? Entered, DateTimeOffset? Exited, int Stays);

/// <summary>Why a roster cannot hold what a tracking log says about one person's times.</summary>
public enum TrackingRosterTimesProblem
{
    /// <summary>No report says the person went in.</summary>
    NoEntry,

    /// <summary>No report says the person came out after they last went in.</summary>
    NoExit,

    /// <summary>The moment they came out is before the moment they went in.</summary>
    ExitBeforeEntry,

    /// <summary>They went in on a day that is not the trip's first: a roster time has no day of its own.</summary>
    EntryOffTripDate,

    /// <summary>They came out on a day the trip's dates do not reach.</summary>
    ExitOffTripDate,

    /// <summary>
    /// The clocks of the zone changed between the two moments, so the two readings are an hour
    /// further apart, or closer, than the time that passed.
    /// </summary>
    ClockChanged,
}

/// <summary>
/// One person's entry and exit as a roster would hold them — two readings of a clock, with no day
/// and no zone — or the reason it cannot.
/// </summary>
/// <param name="Entry">The reading for the moment they went in, wherever there is such a moment — also beside a problem, so that a reviewer sees what was found.</param>
/// <param name="Exit">The reading for the moment they came out, likewise.</param>
/// <param name="Problem">Null when the pair may be written to a roster as it stands.</param>
public readonly record struct TrackingRosterTimes(TimeOnly? Entry, TimeOnly? Exit, TrackingRosterTimesProblem? Problem);

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
    /// The first report that somebody went in and the last that they came out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A roster holds one entry and one exit per person, so somebody who went in twice is given the
    /// outer pair, and <see cref="TrackingEntryExit.Stays"/> says that the pair covers more than
    /// one stay.
    /// </para>
    /// <para>
    /// <b>An exit followed by another entry is not the last word.</b> Somebody who came out at
    /// noon and went back in at one is, as far as the log knows, still inside; offering noon as
    /// the time they came out would write a finished stay over an unfinished one. So the exit is
    /// null there, exactly as it is for an entry nobody ever closed, and for the same reason an
    /// exit that only precedes the first entry is no exit at all.
    /// </para>
    /// <para>Reports are read in the order <see cref="StaysOf"/> reads them.</para>
    /// </remarks>
    public static TrackingEntryExit EntryExitOf(IEnumerable<TrackingPassage>? reports)
    {
        if (reports is null) return default;

        DateTimeOffset? entered = null;
        DateTimeOffset? exited = null;
        var inside = false;
        var stays = 0;

        foreach (var report in reports.OrderBy(r => r.At.UtcTicks))
        {
            switch (report.Kind)
            {
                case TripPositionEventKind.Entered:
                    entered ??= report.At;
                    inside = true;
                    break;
                case TripPositionEventKind.Exited when entered is not null:
                    // Counted as StaysOf counts: an exit closes a stay only while one is open.
                    if (inside) stays++;
                    exited = report.At;
                    inside = false;
                    break;
                default:
                    break;
            }
        }

        return new TrackingEntryExit(entered, inside ? null : exited, stays);
    }

    /// <summary>
    /// What a roster would hold for <paramref name="watch"/>: the two moments read on
    /// <paramref name="zone"/>'s clocks, each to the minute, or the reason the roster cannot say it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A roster's times carry no day. The day of the entry is the trip's first, and the day of the
    /// exit is the trip's last — or, on a trip recorded as a single day, the morning after when the
    /// exit reads earlier than the entry, which is the one night <see cref="TripDuration"/> infers.
    /// Moments that fall on any other day would be read back as a different length of time, so
    /// they are answered as a problem and never as a pair.
    /// </para>
    /// <para>
    /// <b>The pair is offered only when the roster would then count the same minutes as the two
    /// moments are apart.</b> That is checked against the roster's own rule and not re-derived
    /// here, which is what catches the one case the days do not: clocks that changed while the
    /// person was underground. Two true readings an hour further apart than the time that passed
    /// would put a wrong hour into every total the roster feeds, silently, and a person can still
    /// type those two times by hand knowing what they mean.
    /// </para>
    /// <para>
    /// Each moment is read at the offset the zone kept at that moment, so a trip in July and one
    /// in January are each right without anybody stating an offset. Seconds are dropped, as the
    /// roster drops them.
    /// </para>
    /// </remarks>
    public static TrackingRosterTimes RosterTimesOf(
        TrackingEntryExit watch, DateOnly tripDate, DateOnly? tripDateEnd, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var entered = watch.Entered is { } a ? TimeZoneInfo.ConvertTime(a, zone) : (DateTimeOffset?)null;
        var exited = watch.Exited is { } b ? TimeZoneInfo.ConvertTime(b, zone) : (DateTimeOffset?)null;
        TimeOnly? entry = entered is { } e ? new TimeOnly(e.Hour, e.Minute) : null;
        TimeOnly? exit = exited is { } x ? new TimeOnly(x.Hour, x.Minute) : null;

        TrackingRosterTimes Refused(TrackingRosterTimesProblem problem) => new(entry, exit, problem);

        if (entered is not { } wentIn) return Refused(TrackingRosterTimesProblem.NoEntry);
        if (exited is not { } cameOut) return Refused(TrackingRosterTimesProblem.NoExit);
        if (cameOut < wentIn) return Refused(TrackingRosterTimesProblem.ExitBeforeEntry);

        if (DateOnly.FromDateTime(wentIn.DateTime) != tripDate)
        {
            return Refused(TrackingRosterTimesProblem.EntryOffTripDate);
        }

        var lastDay = tripDateEnd ?? tripDate;
        var exitDay = DateOnly.FromDateTime(cameOut.DateTime);
        var theMorningAfter = lastDay == tripDate
            && exitDay.DayNumber == tripDate.DayNumber + 1
            && exit < entry;
        if (exitDay != lastDay && !theMorningAfter)
        {
            return Refused(TrackingRosterTimesProblem.ExitOffTripDate);
        }

        var onTheRoster = TripDuration.UndergroundMinutes(tripDate, tripDateEnd, entry, exit);
        return onTheRoster == new TrackingStay(wentIn, cameOut).Minutes
            ? new TrackingRosterTimes(entry, exit, null)
            : Refused(TrackingRosterTimesProblem.ClockChanged);
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
