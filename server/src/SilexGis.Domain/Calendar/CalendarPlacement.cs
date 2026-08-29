// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Calendar;

/// <summary>
/// Where a dated row's lifecycle state puts it on a calendar, if anywhere.
/// </summary>
/// <remarks>
/// <para>
/// This answers a display question and nothing else. It never decides who may read a row: that is
/// settled by the row's visibility and its access entries alone, and a second rule answering it
/// would be free to disagree with the first. A row a caller may read stays readable on the list
/// and page it was always readable on whatever this says about a calendar.
/// </para>
/// <para>
/// The distinction between being on the calendar and being drawn on the grid is the reason this
/// is five answers rather than a boolean: a row whose date has been put back still exists and is
/// still worth listing, but the date it carries is not one anybody is going on, so drawing it in
/// that day's cell would state something untrue.
/// </para>
/// </remarks>
public enum CalendarPlacement
{
    /// <summary>Not on a calendar at all, whatever its date says.</summary>
    Off = 0,

    /// <summary>A date somebody is going on. The forward half of the record.</summary>
    Ahead = 1,

    /// <summary>A date that happened. The past half of the record.</summary>
    Behind = 2,

    /// <summary>
    /// On the calendar, on its date, and marked as called off — shown rather than hidden, so the
    /// calendar cannot quietly disagree with the row's own page, and offered as something a
    /// reader may narrow away when they want only what is going ahead.
    /// </summary>
    CalledOff = 3,

    /// <summary>
    /// On the record with a postponed mark, and deliberately <b>not</b> in a day cell: the date
    /// the row still carries is not one anybody is going on any more, so a grid drawing it there
    /// would repeat the mistake of quoting a date that has been abandoned. It returns to an
    /// ordinary placement when a new date is settled.
    /// </summary>
    PutBack = 4,
}
