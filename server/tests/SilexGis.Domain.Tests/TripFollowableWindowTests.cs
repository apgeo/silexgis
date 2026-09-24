// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Whether a trip is being followed right now by whoever holds any of its links — the question a
/// list of one cave's current activity asks, as opposed to "may the caller in front of me read
/// this one trip".
/// </summary>
/// <remarks>
/// <para>
/// Each closure is stated beside the opening it differs from by one thing, for the reason the
/// neighbouring window's tests give: a rule that answered false to everything would satisfy every
/// closure on its own and would quietly take the feature away.
/// </para>
/// <para>
/// The last test is the one that earns its place. The design claims this rule and
/// <see cref="TripPastTrackWindow.IsReadableAsPast"/> <em>partition</em> a published trip's life —
/// followable, then past, never both and never neither while the retention allows. That claim is
/// what lets a page read two lists and trust they neither overlap nor leave a gap, and it is a
/// property of the pair that no test of either rule alone can see.
/// </para>
/// </remarks>
public class TripFollowableWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);
    private static readonly DateOnly TripDate = new(2026, 9, 18);

    /// <summary>Far enough ahead that the expiry is never what decides a case, unless it is.</summary>
    private static readonly DateTimeOffset Later = Now.AddDays(30);

    private static bool Followable(
        TripTrackingState state, DateTimeOffset? closedAt, DateTimeOffset? latestUnrevokedExpiry,
        DateTimeOffset? now = null) =>
        TripPublicationWindow.IsFollowableByAnyLink(
            now ?? Now, latestUnrevokedExpiry, state, closedAt, Grace);

    [Fact]
    public void An_armed_watch_with_a_link_nobody_withdrew_is_followable_and_without_one_is_not()
    {
        // The pair that says what "published" means here: the watch is identical in both, and the
        // only difference is whether a link to it still stands. Withdrawing every link is how a
        // club unpublishes a trip, and it has to take the trip off this list.
        Followable(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: Later)
            .ShouldBeTrue();
        Followable(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: null)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_watch_that_was_never_armed_is_not_followable_however_well_published()
    {
        // Off is not a state a reader may follow: there is no party. Asserted beside the armed case
        // above with the same expiry, so this cannot pass by the expiry having closed.
        Followable(TripTrackingState.Off, closedAt: null, latestUnrevokedExpiry: Later)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_closed_watch_is_followable_inside_its_grace_and_not_after_it()
    {
        var closed = Now.AddHours(-1);
        Followable(TripTrackingState.Closed, closed, latestUnrevokedExpiry: Later).ShouldBeTrue();

        var longClosed = Now - Grace - TimeSpan.FromHours(1);
        Followable(TripTrackingState.Closed, longClosed, latestUnrevokedExpiry: Later)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_closed_watch_with_no_recorded_instant_is_treated_as_closed_long_ago()
    {
        // Fail closed for an incomplete row, matching the live window's own reading of it: a watch
        // recorded as closed with nothing saying when is not a watch that closed this second.
        Followable(TripTrackingState.Closed, closedAt: null, latestUnrevokedExpiry: Later)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_lapsed_expiry_closes_it_even_while_the_watch_is_armed()
    {
        // The backstop, and the case that matters most: a watch nobody ever closed is the ordinary
        // outcome of a trip that ended well, and without this the party would still be shown
        // underground years later.
        Followable(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: Now.AddSeconds(-1))
            .ShouldBeFalse();
        Followable(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: Now.AddSeconds(1))
            .ShouldBeTrue();
    }

    [Fact]
    public void One_revoked_link_does_not_unpublish_a_trip_that_has_another()
    {
        // The whole reason this rule is not IsOpen. IsOpen refuses the caller whose own link was
        // revoked; that is a fact about the caller, not about the trip. A trip with two links, one
        // withdrawn, is still being followed through the other — and the value this rule is given
        // is "the latest expiry among the links nobody revoked", so the withdrawn one simply is not
        // in it.
        //
        // Stated as the difference it makes: the same trip, read through the rule that has a
        // caller in it, is refused for the holder of the revoked link and open for the other.
        TripPublicationWindow
            .IsOpen(Now, revokedAt: Now.AddDays(-1), Later, TripTrackingState.Armed, null, Grace)
            .ShouldBeFalse();
        Followable(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: Later)
            .ShouldBeTrue();
    }

    [Fact]
    public void Followable_and_readable_as_past_partition_a_published_trips_life()
    {
        // The property the two-list design rests on, asked across the whole life of one trip rather
        // than at a chosen instant: at no moment is a published trip both followable and readable
        // as past, and at no moment inside the retention is it neither.
        //
        // Walked hour by hour from before the watch is armed to well past the grace window, because
        // the handover is a boundary and a test that sampled two instants either side of where it
        // believes the boundary to be would agree with a rule whose boundary had moved.
        var closed = new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);
        var expiry = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(365);

        var neitherChecked = 0;
        var followableHours = 0;
        var pastHours = 0;
        for (var hour = 0; hour < 24 * 40; hour++)
        {
            var now = closed.AddDays(-1).AddHours(hour);

            // The watch is armed until it closes, and closed thereafter — the one row changing
            // under both rules, so the pair is asked about the same trip throughout.
            var armed = now < closed;
            var state = armed ? TripTrackingState.Armed : TripTrackingState.Closed;
            var closedAt = armed ? (DateTimeOffset?)null : closed;

            var followable = TripPublicationWindow.IsFollowableByAnyLink(
                now, expiry, state, closedAt, Grace);
            var past = TripPastTrackWindow.IsReadableAsPast(
                now, state, closedAt, expiry, TripDate, tripDateEnd: null, Grace, retention);

            (followable && past).ShouldBeFalse($"both at {now:O}");

            // And never neither, for the whole of this walk. Note which window that depends on:
            // what bounds the archive is the retention, not the link's expiry. A lapsed expiry ends
            // the live window and leaves the trip readable as past — so a trip whose links have all
            // gone quiet is still history, which is the behaviour a club's article needs and the
            // reason the two rules are not bounded by the same thing.
            (followable || past).ShouldBeTrue($"neither at {now:O}");
            neitherChecked++;

            if (followable) followableHours++;
            if (past) pastHours++;
        }

        // Guards the walk itself, which is the half of a property test that is easy to leave out.
        // "Nothing is ever both" is satisfied by a rule answering false to everything, and
        // "nothing is ever neither" is never asked at all if the expiry sits before the walk
        // begins. So: the walk has to have seen this trip followable, have seen it past, and have
        // asked the neither question — otherwise the assertions above passed vacuously.
        followableHours.ShouldBeGreaterThan(0);
        pastHours.ShouldBeGreaterThan(0);
        neitherChecked.ShouldBeGreaterThan(0);

        // And it has to have crossed the handover rather than stopped at it: every hour of the walk
        // is exactly one of the two, so the counts partition it.
        (followableHours + pastHours).ShouldBe(24 * 40);
        neitherChecked.ShouldBe(24 * 40);

        // The expiry lapses partway through the walk and changes nothing about the archive — stated
        // out loud because it is the asymmetry a later reader is most likely to "tidy up" by giving
        // the past window an expiry it deliberately does not have.
        expiry.ShouldBeLessThan(closed.AddDays(-1).AddHours(24 * 40));
        TripPastTrackWindow.IsReadableAsPast(
                expiry.AddHours(1), TripTrackingState.Closed, closed, expiry,
                TripDate, tripDateEnd: null, Grace, retention)
            .ShouldBeTrue();
    }
}
