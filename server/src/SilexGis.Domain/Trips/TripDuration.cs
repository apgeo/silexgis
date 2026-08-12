// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// How long somebody was underground, worked out from the two wall-clock times and the days the
/// trip spans. It is derived on every read and never stored: the times and the dates are the
/// record, and a total kept beside them disagrees with them the first time either is corrected.
/// </summary>
/// <remarks>
/// The clock times carry no day, so the days have to be added back. An exit earlier than the
/// entry means "the next morning" only on a trip recorded as lasting a single day — on a trip
/// that says which day it ended, the span is already known and guessing a midnight on top of it
/// would count one night twice. A thirty-hour push entered as a two-day trip is therefore thirty
/// hours, not six.
///
/// Seconds are dropped rather than rounded: a time is entered and shown to the minute, and a row
/// that happens to carry seconds must not make one surface disagree with another by a minute.
/// </remarks>
public static class TripDuration
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Minutes underground, or null when the values cannot say: either clock time missing, or a
    /// range whose end precedes its start. Null is not zero — zero would claim a trip took no
    /// time, and a sum that absorbed it would quietly understate every total it belongs to.
    /// </summary>
    public static int? UndergroundMinutes(
        DateOnly tripDate, DateOnly? tripDateEnd, TimeOnly? entry, TimeOnly? exit)
    {
        if (entry is null || exit is null)
        {
            return null;
        }

        // Whole days by day number, not by subtracting instants: a day spanning a clock change is
        // 23 or 25 hours long, and the calendar difference is the one that is meant here.
        var days = tripDateEnd is { } end ? end.DayNumber - tripDate.DayNumber : 0;
        if (days < 0)
        {
            // Nothing at the database level forbids an end before the start, so a row that has
            // one — imported, or written before the rule existed — says nothing rather than a
            // negative that a sum would silently take off somebody else's hours.
            return null;
        }

        var minutes = (days * MinutesPerDay) + MinutesOfDay(exit.Value) - MinutesOfDay(entry.Value);
        if (days == 0 && minutes < 0)
        {
            minutes += MinutesPerDay;
        }

        return minutes < 0 ? null : minutes;
    }

    private static int MinutesOfDay(TimeOnly time) => (time.Hour * 60) + time.Minute;
}
