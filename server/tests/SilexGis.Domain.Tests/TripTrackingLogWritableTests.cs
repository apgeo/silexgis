// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which watches may have their log written to — a report recorded, corrected or taken off.
/// </summary>
/// <remarks>
/// The rule exists because recording and deleting used to disagree: reports landed on an armed
/// watch only, while a delete was allowed whatever state the watch was in. A closed trip's log was
/// therefore destroyable and not repairable, which made the documented way to fix a report —
/// putting a right one where a wrong one was — unusable on exactly the trips that get written up
/// from notes afterwards. So each case below is stated beside the one it differs from.
/// </remarks>
public class TripTrackingLogWritableTests
{
    [Fact]
    public void A_closed_watch_is_writable_just_as_an_armed_one_is()
    {
        // The pair that is the whole point: correcting a finished trip's log has to be possible,
        // and asserting the armed case beside it is what distinguishes "both" from "the rule
        // answers true to everything".
        TripTrackingRules.MayWriteLog(TripTrackingState.Armed).ShouldBeTrue();
        TripTrackingRules.MayWriteLog(TripTrackingState.Closed).ShouldBeTrue();
    }

    [Fact]
    public void A_watch_that_was_never_armed_is_not_writable()
    {
        // Off is refused for a reason of its own rather than for being "not started": an off watch
        // names no survey, so a report claiming a station has nothing to resolve it against and
        // nothing to protect the position by. Asserted beside the two that are allowed, so this
        // cannot pass by the rule having become false everywhere.
        TripTrackingRules.MayWriteLog(TripTrackingState.Off).ShouldBeFalse();
        TripTrackingRules.MayWriteLog(TripTrackingState.Armed).ShouldBeTrue();
    }

    [Fact]
    public void Every_state_the_watch_can_hold_is_answered_for()
    {
        // Guards the rule against a state added later and never considered here: a new member of
        // the enum would otherwise inherit whichever branch the switch happens to fall into, and
        // nothing would say so. If this fails, decide what the new state means and say it above.
        Enum.GetValues<TripTrackingState>().Length.ShouldBe(3);
    }

    [Fact]
    public void A_report_is_changed_since_written_only_when_its_second_stamp_has_moved()
    {
        // The two stamps are written together from one clock when a report is first saved, so
        // equal means untouched; the later one moving is the only sign a save changed something.
        var written = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        TripTrackingRules.ChangedSinceWritten(written, written).ShouldBeFalse();
        TripTrackingRules.ChangedSinceWritten(written, written.AddTicks(1)).ShouldBeTrue();
    }
}
