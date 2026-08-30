// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// When a trip that arranged an overdue check is still one somebody could be underground on.
/// </summary>
/// <remarks>
/// The check itself is armed and stood down by hand — the state of the trip does not arm it and
/// does not stand it down. What this answers is narrower: whether the arrangement the alarm was
/// set against still describes something that is happening, so that a check nobody remembered to
/// stand down does not raise an alarm about a trip that plainly did not take place.
/// </remarks>
public static class TripCalloutRules
{
    /// <summary>
    /// Whether an armed check on a trip in this state should still be watched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two states say the arrangement no longer holds. A trip that was <b>called off</b> had nobody
    /// go, so nobody can be late back from it. A trip that was <b>put back</b> has moved, and the
    /// hour the check was armed against belongs to a date that is no longer true — raising an alarm
    /// at it would report a party overdue from a trip that has not started.
    /// </para>
    /// <para>
    /// Everything else is watched, including a trip somebody has already written up: a party may
    /// well be out and the record simply written ahead, but the two mistakes are not the same size.
    /// A false alarm is a message somebody stands down in one tap; a swallowed alarm is a party
    /// nobody goes looking for. So a state added to the vocabulary and not named here is watched,
    /// which is the opposite default from the one that decides whether a change is worth mailing
    /// about, and deliberately so.
    /// </para>
    /// </remarks>
    public static bool WatchesForOverdue(ActivityState state) => state switch
    {
        ActivityState.Cancelled => false,
        ActivityState.Delayed => false,
        _ => true,
    };
}
