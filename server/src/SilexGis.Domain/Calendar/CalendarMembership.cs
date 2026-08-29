// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Calendar;

/// <summary>
/// The one place that decides whether a dated row's lifecycle state puts it on a calendar, and
/// on which half.
/// </summary>
/// <remarks>
/// <para>
/// It is a <b>display</b> rule and it must stay one. Who may read a trip, a camp or an event is
/// answered by that row's visibility and its access entries, once, and this is never consulted
/// there — a row kept off a calendar is still read on its own page and still listed by every
/// listing that showed it before. The two questions are kept apart because a rule that answered
/// both would eventually answer them differently, and the disagreement would be a disclosure
/// rather than a bug somebody notices. So this is applied to a query only after the reader's
/// ordinary visibility has already narrowed it, never inside that narrowing and never before it.
/// </para>
/// <para>
/// A state the vocabulary gains later and that nobody names here is <see cref="CalendarPlacement.Off"/>
/// — off the calendar until somebody decides what it means. That is the same safe default the
/// notice rules take: a row missing from a grid is noticed and asked about, a row drawn on a day
/// nobody agreed to is acted on.
/// </para>
/// </remarks>
public static class CalendarMembership
{
    /// <summary>
    /// Where a row in this lifecycle state belongs on a calendar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The forward half is not re-listed here. The states that mean the row's date is still a
    /// date somebody is going on are exactly the states that earn a run-up reminder, so this asks
    /// that rule rather than writing a second list beside it: two lists of the same states are
    /// two answers waiting to drift apart, and the surface that drifts is the one nobody is
    /// looking at.
    /// </para>
    /// <para>
    /// The rest are named one by one, and each name is a decision. A record that happened belongs
    /// on the calendar as much as one that has not — a calendar people read is a record of the
    /// year, not a to-do list. A row that was called off is shown and marked, because the reader
    /// most needing to notice it is the person who was going on it. A row put back is listed and
    /// marked but kept out of a day cell, because its date has been abandoned rather than kept.
    /// A draft has been shown to nobody at all, so it appears nowhere by way of a calendar.
    /// </para>
    /// </remarks>
    public static CalendarPlacement PlacementOf(ActivityState state) => state switch
    {
        _ when TripPlanNotices.RemindsOfDate(state) => CalendarPlacement.Ahead,
        ActivityState.Done => CalendarPlacement.Behind,
        ActivityState.Published => CalendarPlacement.Behind,
        ActivityState.Cancelled => CalendarPlacement.CalledOff,
        ActivityState.Delayed => CalendarPlacement.PutBack,
        _ => CalendarPlacement.Off,
    };

    /// <summary>
    /// Whether a row in this state reaches a calendar at all — in a day cell or only in the
    /// record beside it.
    /// </summary>
    public static bool ShowsOnCalendar(ActivityState state) =>
        PlacementOf(state) != CalendarPlacement.Off;

    /// <summary>
    /// The states that reach a calendar, taken from the whole vocabulary rather than written out,
    /// so that a state added later is excluded by the safe default above with no list to update.
    /// Materialised once because a query narrows on it.
    /// </summary>
    public static IReadOnlyList<ActivityState> ShownStates { get; } =
        [.. ActivityStates.All.Where(ShowsOnCalendar)];
}
