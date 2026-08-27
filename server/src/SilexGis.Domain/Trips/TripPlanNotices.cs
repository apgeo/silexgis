// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// When a trip being planned is worth telling the people it concerns about.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a third question asked of the lifecycle vocabulary, which answers two and no
/// others: may this activity move from one state to another, and does entering a state tell the
/// people named on it that it exists. This is a different question with a different answer — it is
/// asked of a trip that is already known to the people it concerns, about a change to it — so it
/// lives with the code that means it rather than widening a switch everything shares.
/// </para>
/// <para>
/// The two notices it governs are sent deliberately by the paths that cause them, and neither is a
/// state-entry notice: a trip is not called off by being edited.
/// </para>
/// </remarks>
public static class TripPlanNotices
{
    /// <summary>
    /// Whether editing a trip in this state, or calling it off from this state, tells the people
    /// it concerns.
    /// </summary>
    /// <remarks>
    /// A draft has been shown to nobody — telling them it changed would be telling them it exists,
    /// which is exactly what a draft does not do. A trip that has already happened is a write-up,
    /// and a correction to a write-up is not news anybody needs by mail; the announcement is the
    /// event that tells them about that. A trip already called off cannot be called off again, and
    /// changes to it are housekeeping. What is left is the span in which people are expecting to
    /// go somewhere on a date: that is when a change to the plan matters to them.
    /// A state added to the vocabulary and not named here stays silent, rather than mailing
    /// everybody about something nobody decided to tell them.
    /// </remarks>
    public static bool AnnouncesChanges(ActivityState state) => state switch
    {
        ActivityState.Draft => false,
        ActivityState.Proposed => true,
        ActivityState.Planned => true,
        ActivityState.Confirmed => true,
        ActivityState.Delayed => true,
        ActivityState.Done => false,
        ActivityState.Published => false,
        ActivityState.Cancelled => false,
        _ => false,
    };

    /// <summary>
    /// Whether the people on a trip in this state should be reminded, in the run-up, that it is
    /// coming up on the date the trip carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not the same question as <see cref="AnnouncesChanges"/>, and the difference is
    /// the whole reason this exists. That one asks whether a change is worth mailing about; this
    /// one asks whether the date on the trip is still a date somebody is going on. They part
    /// company on exactly one state: a trip that has been <b>put back</b> is worth telling people
    /// about — that is news — but the date it still carries is not one anybody is going on any
    /// more, so a reminder quoting it would be the reminder saying something untrue. A new date
    /// has to be settled before the run-up means anything again.
    /// </para>
    /// <para>
    /// A state added to the vocabulary and not named here sends no reminder, which is the same
    /// safe default the announcement rule takes: silence is recoverable, a wrong date is not.
    /// </para>
    /// </remarks>
    public static bool RemindsOfDate(ActivityState state) => state switch
    {
        ActivityState.Proposed => true,
        ActivityState.Planned => true,
        ActivityState.Confirmed => true,
        _ => false,
    };
}
