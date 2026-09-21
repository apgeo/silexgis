// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// When a published trip becomes readable as a past track, and when the link that was handed out
/// stops opening the past at all.
/// </summary>
/// <remarks>
/// <para>
/// Every case that says something is closed is stated beside the case that says it is open. A rule
/// that returned false for everything would satisfy each closure on its own and would take the
/// feature away without a single test going red; the pairs are the point — the same trip, one
/// thing different.
/// </para>
/// <para>
/// The other half of the point is the pair of windows. Several tests below assert the live rule
/// and this one about the same row, because the whole design is that the second window opens
/// exactly where the first one closes: an assertion about only one of them could not tell that
/// apart from the two being the same window.
/// </para>
/// </remarks>
public class TripPastTrackWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);

    /// <summary>The trip these are all about: one day, a fortnight before "now".</summary>
    private static readonly DateOnly TripDate = new(2026, 9, 6);

    private static bool Past(
        TripTrackingState state, DateTimeOffset? closedAt, DateTimeOffset? latestUnrevokedExpiry,
        TimeSpan? retention = null, DateTimeOffset? now = null, DateOnly? tripDate = null) =>
        TripPastTrackWindow.IsReadableAsPast(
            now ?? Now, state, closedAt, latestUnrevokedExpiry,
            tripDate ?? TripDate, tripDateEnd: null, Grace, retention);

    [Fact]
    public void A_closed_watch_past_its_grace_is_past_and_the_same_watch_inside_grace_is_not()
    {
        // The handover, asserted from both sides: the grace window after a watch closes belongs
        // entirely to the live page — "everybody is out" is the last thing it has to say — and only
        // once it has said it does the trip become history. The two windows must not overlap.
        var justClosed = Now.AddHours(-1);
        var longClosed = Now.AddDays(-5);
        var expiry = Now.AddDays(5);

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Closed, justClosed, Grace)
            .ShouldBeTrue();
        Past(TripTrackingState.Closed, justClosed, expiry).ShouldBeFalse();

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Closed, longClosed, Grace)
            .ShouldBeFalse();
        Past(TripTrackingState.Closed, longClosed, expiry).ShouldBeTrue();
    }

    [Fact]
    public void A_party_still_underground_on_a_lapsed_link_is_not_past_however_dead_the_link_is()
    {
        // The clause that is not implied by any other one and must never be dropped. The live
        // window closes for a link that has run out even while the watch is still armed — so
        // without the armed test a party still underground would fall straight into an anonymous
        // archive, readable by anybody holding a token to any other trip of the same cave. That is
        // the very disclosure the live window exists to prevent, reached through a side door.
        var lapsed = Now.AddDays(-1);

        TripPublicationWindow.IsOpen(Now, null, lapsed, TripTrackingState.Armed, null, Grace)
            .ShouldBeFalse();
        Past(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: lapsed).ShouldBeFalse();

        // And the same trip once the watch is closed and the grace is spent: now it is history.
        Past(TripTrackingState.Closed, closedAt: Now.AddDays(-3), latestUnrevokedExpiry: lapsed)
            .ShouldBeTrue();
    }

    [Fact]
    public void An_armed_watch_on_a_live_link_is_followed_rather_than_archived()
    {
        // The ordinary live case, stated so that "not past" cannot be passing because the row was
        // wrong in some other way: the very same row is open on the live rule.
        var expiry = Now.AddDays(5);

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Armed, null, Grace)
            .ShouldBeTrue();
        Past(TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: expiry).ShouldBeFalse();
    }

    [Fact]
    public void A_trip_nobody_published_is_never_past_and_publishing_it_makes_it_past()
    {
        // "Published" is read off the link rows that already exist — there is no archive flag and
        // no second act — so a null latest expiry is the whole of "never published, or every link
        // of it withdrawn".
        Past(TripTrackingState.Closed, Now.AddDays(-5), latestUnrevokedExpiry: null).ShouldBeFalse();
        Past(TripTrackingState.Closed, Now.AddDays(-5), latestUnrevokedExpiry: Now.AddDays(-1))
            .ShouldBeTrue();
    }

    [Fact]
    public void Revoking_every_link_of_a_trip_takes_it_back_out_of_the_archive()
    {
        // The only withdrawal path there is, now that there is no per-trip archive act: revocation
        // already means "end this publication, now", and it is read as meaning exactly that. The
        // pair is one trip with a link left and the same trip with none.
        var closed = Now.AddDays(-5);

        Past(TripTrackingState.Closed, closed, latestUnrevokedExpiry: Now.AddDays(3)).ShouldBeTrue();
        Past(TripTrackingState.Closed, closed, latestUnrevokedExpiry: null).ShouldBeFalse();
    }

    [Fact]
    public void Retention_ends_the_archive_and_no_retention_never_does()
    {
        // Counted from the end of the trip rather than from a mint or from the watch closing, so
        // one configured number means the same thing for an afternoon and for an expedition.
        var closed = new DateTimeOffset(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);
        var expiry = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

        // The trip ended on 6 September, so its archive with a week's retention runs to the 14th.
        Past(TripTrackingState.Closed, closed, expiry, retention: TimeSpan.FromDays(7),
            now: new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
        Past(TripTrackingState.Closed, closed, expiry, retention: TimeSpan.FromDays(7),
            now: new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)).ShouldBeFalse();

        // And with no limit configured, the same instant that fell outside the window above is
        // still inside the archive — which is the default, and the whole point of one.
        Past(TripTrackingState.Closed, closed, expiry, retention: null,
            now: new DateTimeOffset(2036, 9, 15, 12, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void Retention_counts_from_the_end_of_a_multi_day_trip_not_from_its_first_day()
    {
        // The same reading of "when was the trip over" the live expiry uses, which is why it is one
        // function: a three-week expedition whose archive expired a week after it set off would be
        // gone before it ended.
        var lastDay = new DateOnly(2026, 9, 21);
        var week = TimeSpan.FromDays(7);

        TripPastTrackWindow.WithinRetention(
            new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 1), lastDay, week).ShouldBeTrue();
        TripPastTrackWindow.WithinRetention(
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 1), lastDay, week).ShouldBeFalse();
    }

    [Fact]
    public void The_retention_origin_is_the_same_instant_the_live_expiry_counts_from()
    {
        // Stated as an equality rather than trusted, because the two windows reading "the end of
        // the trip" a day apart would be invisible in both.
        var start = new DateOnly(2026, 9, 1);
        var end = new DateOnly(2026, 9, 21);
        var lifetime = TimeSpan.FromDays(14);
        var mint = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        var endOfTrip = TripPublicationWindow.EndOfTrip(start, end);
        endOfTrip.ShouldBe(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
        TripPublicationWindow.ExpiresAtFor(mint, start, end, lifetime).ShouldBe(endOfTrip + lifetime);

        // The boundary, both sides of it: the instant retention runs out is already outside.
        TripPastTrackWindow.WithinRetention(endOfTrip + lifetime, start, end, lifetime).ShouldBeFalse();
        TripPastTrackWindow.WithinRetention(
            endOfTrip + lifetime - TimeSpan.FromSeconds(1), start, end, lifetime).ShouldBeTrue();
    }

    // ---- the token's two windows -------------------------------------------------------------

    private static bool Opens(
        DateTimeOffset? revokedAt, DateTimeOffset expiresAt, TripTrackingState state,
        DateTimeOffset? closedAt, DateTimeOffset? latestUnrevokedExpiry,
        TimeSpan? retention = null, DateTimeOffset? now = null) =>
        TripPastTrackWindow.OpensThePast(
            now ?? Now, revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry,
            TripDate, tripDateEnd: null, Grace, retention);

    [Fact]
    public void A_link_whose_party_is_still_out_there_opens_the_past_because_the_picker_hangs_off_a_live_page()
    {
        // Window one. A visitor standing on tonight's page picks a past trip from it, so the token
        // has to open the archive while its own trip is still live.
        var expiry = Now.AddDays(5);

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Armed, null, Grace)
            .ShouldBeTrue();
        Opens(null, expiry, TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: expiry)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_link_whose_live_window_has_closed_goes_on_opening_the_past()
    {
        // Window two, and the correction this whole class exists for. Gating the archive on the
        // live window would make a club's article show its history only while some party happened
        // to be underground — which is almost never — so the second window outlives the first.
        var closedLongAgo = Now.AddDays(-5);
        var expiry = Now.AddDays(3);

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Closed, closedLongAgo, Grace)
            .ShouldBeFalse();
        Opens(null, expiry, TripTrackingState.Closed, closedLongAgo, latestUnrevokedExpiry: expiry)
            .ShouldBeTrue();

        // And a link that has lapsed as well — the live page is long gone and the archive is not.
        var lapsed = Now.AddDays(-1);
        TripPublicationWindow.IsOpen(Now, null, lapsed, TripTrackingState.Closed, closedLongAgo, Grace)
            .ShouldBeFalse();
        Opens(null, lapsed, TripTrackingState.Closed, closedLongAgo, latestUnrevokedExpiry: lapsed)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_revoked_link_opens_neither_window_even_when_the_trip_has_another_link()
    {
        // Revocation is somebody deciding now, and it has to end this link whatever else is true of
        // the trip. The past half of the rule is asked about the trip rather than about this link,
        // so without the explicit test a withdrawn link would keep answering on the strength of a
        // sibling nobody revoked — which is the pair asserted here.
        var stillLive = Now.AddDays(5);

        Opens(revokedAt: null, stillLive, TripTrackingState.Closed, Now.AddDays(-5),
            latestUnrevokedExpiry: stillLive).ShouldBeTrue();
        Opens(revokedAt: Now.AddHours(-1), stillLive, TripTrackingState.Closed, Now.AddDays(-5),
            latestUnrevokedExpiry: stillLive).ShouldBeFalse();
    }

    [Fact]
    public void A_lapsed_link_on_a_watch_that_is_still_armed_opens_nothing_at_all()
    {
        // The fail-closed corner, and the one place both windows refuse the same row: the live half
        // refuses it because the link has run out, and the past half because the watch is armed. A
        // party underground is neither followable on a dead link nor history.
        var lapsed = Now.AddDays(-1);

        Opens(null, lapsed, TripTrackingState.Armed, closedAt: null, latestUnrevokedExpiry: lapsed)
            .ShouldBeFalse();

        // Close the watch and let the grace run out, and the same link opens the archive — which
        // says the refusal above was about the party being underground and not about the link.
        Opens(null, lapsed, TripTrackingState.Closed, closedAt: Now.AddDays(-3),
            latestUnrevokedExpiry: lapsed).ShouldBeTrue();
    }

    [Fact]
    public void A_link_whose_archive_has_aged_out_opens_nothing_and_answers_as_an_unknown_one()
    {
        // The end of the second window. Both windows shut, which is the state a caller must not be
        // able to tell apart from a token that never existed — and the pair says this is retention
        // doing it and not some other clause.
        var closed = Now.AddDays(-5);
        var expiry = Now.AddDays(-1);

        Opens(null, expiry, TripTrackingState.Closed, closed, latestUnrevokedExpiry: expiry,
            retention: TimeSpan.FromDays(365)).ShouldBeTrue();
        Opens(null, expiry, TripTrackingState.Closed, closed, latestUnrevokedExpiry: expiry,
            retention: TimeSpan.FromDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_trip_that_was_never_published_opens_nothing_even_if_a_link_row_says_otherwise()
    {
        // Belt and braces on the past half: a null latest unrevoked expiry cannot be talked into a
        // 200 by a live link, because a live link is exactly what makes the trip not past.
        Opens(null, Now.AddDays(-1), TripTrackingState.Closed, Now.AddDays(-5),
            latestUnrevokedExpiry: null).ShouldBeFalse();
    }

    [Fact]
    public void A_watch_that_was_never_armed_opens_nothing_live_and_is_archived_only_once_published()
    {
        // A watch that is off has nothing to follow, which the live rule already says. It can still
        // be history — but only on the strength of a link somebody actually minted, which is the
        // pair below.
        var expiry = Now.AddDays(5);

        TripPublicationWindow.IsOpen(Now, null, expiry, TripTrackingState.Off, null, Grace)
            .ShouldBeFalse();
        Opens(null, expiry, TripTrackingState.Off, closedAt: null, latestUnrevokedExpiry: null)
            .ShouldBeFalse();
        Opens(null, expiry, TripTrackingState.Off, closedAt: null, latestUnrevokedExpiry: expiry)
            .ShouldBeTrue();
    }
}
