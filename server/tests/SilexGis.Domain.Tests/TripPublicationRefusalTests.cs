// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The reason an operator's log gives for a refused read, held to the rules that did the refusing.
/// </summary>
/// <remarks>
/// The named cases say what each word means, each beside the neighbour it differs from by one
/// thing. The walk at the end is the one that earns its place: over every combination of inputs, a
/// reason is given exactly when the window rule itself refuses — so the explanation cannot come to
/// describe a refusal that did not happen, nor stay silent about one that did.
/// </remarks>
public class TripPublicationRefusalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);
    private static readonly DateOnly TripDate = new(2026, 9, 18);
    private static readonly DateTimeOffset Later = Now.AddDays(30);

    private static PublishedReadRefusal? Live(
        TripTrackingState state,
        DateTimeOffset? closedAt = null,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? expiresAt = null) =>
        TripPublicationRefusal.OfLiveWindow(Now, revokedAt, expiresAt ?? Later, state, closedAt, Grace);

    private static PublishedReadRefusal? Both(
        TripTrackingState state,
        DateTimeOffset? closedAt = null,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? siblingExpiry = null,
        TimeSpan? retention = null)
    {
        var expiry = expiresAt ?? Later;
        DateTimeOffset? latest = revokedAt is null
            ? (siblingExpiry is { } sibling && sibling > expiry ? sibling : expiry)
            : siblingExpiry;
        return TripPublicationRefusal.OfBothWindows(
            Now, revokedAt, expiry, state, closedAt, latest, TripDate, null, Grace, retention);
    }

    [Fact]
    public void A_link_that_follows_its_party_is_given_no_reason()
    {
        Live(TripTrackingState.Armed).ShouldBeNull();
        Live(TripTrackingState.Closed, closedAt: Now.AddHours(-1)).ShouldBeNull();
        Both(TripTrackingState.Armed).ShouldBeNull();
    }

    [Fact]
    public void The_followed_page_names_the_watch_once_the_link_itself_is_sound()
    {
        // The same closed watch either side of the end of its grace, so the word appears for one
        // reason.
        Live(TripTrackingState.Closed, closedAt: Now - Grace + TimeSpan.FromSeconds(1)).ShouldBeNull();
        Live(TripTrackingState.Closed, closedAt: Now - Grace).ShouldBe(PublishedReadRefusal.ClosedPastGrace);

        Live(TripTrackingState.Off).ShouldBe(PublishedReadRefusal.WatchOff);
    }

    [Fact]
    public void A_revocation_is_named_before_the_links_end_and_the_links_end_before_the_watch()
    {
        // All three hold at once on the first line; each line takes away the cause named before.
        Live(TripTrackingState.Closed, closedAt: Now.AddDays(-30), revokedAt: Now.AddHours(-1), expiresAt: Now.AddDays(-1))
            .ShouldBe(PublishedReadRefusal.Revoked);
        Live(TripTrackingState.Closed, closedAt: Now.AddDays(-30), expiresAt: Now.AddDays(-1))
            .ShouldBe(PublishedReadRefusal.Expired);
        Live(TripTrackingState.Closed, closedAt: Now.AddDays(-30))
            .ShouldBe(PublishedReadRefusal.ClosedPastGrace);

        // A link is over at its end, not after it.
        Live(TripTrackingState.Armed, expiresAt: Now).ShouldBe(PublishedReadRefusal.Expired);
    }

    [Fact]
    public void A_trip_that_is_history_is_refused_only_by_its_age()
    {
        // Closed long ago: the followed page is over, and that is exactly what opens the archive.
        Both(TripTrackingState.Closed, closedAt: Now.AddDays(-30)).ShouldBeNull();
        // The link's own end does not shut the archive either.
        Both(TripTrackingState.Closed, closedAt: Now.AddDays(-30), expiresAt: Now.AddDays(-1)).ShouldBeNull();

        Both(TripTrackingState.Closed, closedAt: Now.AddDays(-30), retention: TimeSpan.FromHours(1))
            .ShouldBe(PublishedReadRefusal.PastRetention);
        Both(TripTrackingState.Closed, closedAt: Now.AddDays(-30), retention: TimeSpan.FromDays(365))
            .ShouldBeNull();
    }

    [Fact]
    public void A_link_that_ran_out_while_its_trip_is_still_followed_has_run_out_whatever_the_retention()
    {
        // Still armed: no history to be too old, so the retention is not what refused it.
        Both(TripTrackingState.Armed, expiresAt: Now.AddDays(-1), retention: TimeSpan.FromHours(1))
            .ShouldBe(PublishedReadRefusal.Expired);
        Both(TripTrackingState.Armed, expiresAt: Now.AddDays(-1)).ShouldBe(PublishedReadRefusal.Expired);

        // Closed and in its grace, followed through a later link of the same trip: this one ran out.
        Both(TripTrackingState.Closed, closedAt: Now.AddHours(-1), expiresAt: Now.AddDays(-1),
                siblingExpiry: Now.AddDays(60))
            .ShouldBe(PublishedReadRefusal.Expired);
        // With no later link the same trip is already history, and readable.
        Both(TripTrackingState.Closed, closedAt: Now.AddHours(-1), expiresAt: Now.AddDays(-1)).ShouldBeNull();
    }

    [Fact]
    public void A_revoked_link_is_revoked_on_both_windows_while_a_sibling_keeps_the_trip_published()
    {
        Both(TripTrackingState.Closed, closedAt: Now.AddDays(-30), revokedAt: Now.AddHours(-1),
                siblingExpiry: Now.AddDays(60))
            .ShouldBe(PublishedReadRefusal.Revoked);
        Both(TripTrackingState.Armed, revokedAt: Now.AddHours(-1)).ShouldBe(PublishedReadRefusal.Revoked);
    }

    [Fact]
    public void A_reason_is_given_exactly_when_the_window_rules_refuse_and_every_window_reason_is_reached()
    {
        DateTimeOffset?[] revocations = [null, Now.AddHours(-3)];
        DateTimeOffset[] expiries = [Now.AddDays(-1), Now, Now.AddDays(30)];
        TripTrackingState[] states = [TripTrackingState.Off, TripTrackingState.Armed, TripTrackingState.Closed];
        DateTimeOffset?[] closings = [null, Now.AddHours(-1), Now - Grace, Now.AddDays(-30)];
        TimeSpan?[] retentions = [null, TimeSpan.FromHours(1), TimeSpan.FromDays(365)];

        var seenLive = new HashSet<PublishedReadRefusal>();
        var seenBoth = new HashSet<PublishedReadRefusal>();
        var walked = 0;

        foreach (var revokedAt in revocations)
        foreach (var expiresAt in expiries)
        foreach (var state in states)
        foreach (var closedAt in closings)
        foreach (var retention in retentions)
        {
            // What the trip's other links leave standing. A revoked link cannot be its trip's
            // latest unrevoked one, and an unrevoked one always is at least that.
            DateTimeOffset?[] latest = revokedAt is null
                ? [expiresAt, Now.AddDays(60)]
                : [null, Now.AddDays(-2), Now.AddDays(60)];
            foreach (var latestUnrevokedExpiry in latest)
            {
                var at = $"{revokedAt:u} {expiresAt:u} {state} {closedAt:u} {latestUnrevokedExpiry:u} {retention}";
                walked++;

                var live = TripPublicationRefusal.OfLiveWindow(Now, revokedAt, expiresAt, state, closedAt, Grace);
                (live is null).ShouldBe(
                    TripPublicationWindow.IsOpen(Now, revokedAt, expiresAt, state, closedAt, Grace), at);
                if (live is { } whyLive) seenLive.Add(whyLive);

                var both = TripPublicationRefusal.OfBothWindows(
                    Now, revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry,
                    TripDate, null, Grace, retention);
                (both is null).ShouldBe(
                    TripPastTrackWindow.OpensThePast(
                        Now, revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry,
                        TripDate, null, Grace, retention),
                    at);
                if (both is { } whyBoth) seenBoth.Add(whyBoth);

                // A revocation is the reason whenever there is one, on either question.
                if (revokedAt is not null)
                {
                    live.ShouldBe(PublishedReadRefusal.Revoked, at);
                    both.ShouldBe(PublishedReadRefusal.Revoked, at);
                }

                // History that is refused for its age is refused with a retention set and passed —
                // never while somebody is still being followed.
                if (both == PublishedReadRefusal.PastRetention)
                {
                    retention.ShouldNotBeNull(at);
                    state.ShouldNotBe(TripTrackingState.Armed, at);
                    TripPastTrackWindow.WithinRetention(Now, TripDate, null, retention).ShouldBeFalse(at);
                }
            }
        }

        walked.ShouldBeGreaterThan(200);
        seenLive.ShouldBe(
            [
                PublishedReadRefusal.Revoked, PublishedReadRefusal.Expired,
                PublishedReadRefusal.WatchOff, PublishedReadRefusal.ClosedPastGrace,
            ],
            ignoreOrder: true);
        // The followed page's two words about the watch are never said of both windows: a watch
        // that is over is what makes a trip history, not what shuts its archive.
        seenBoth.ShouldBe(
            [PublishedReadRefusal.Revoked, PublishedReadRefusal.Expired, PublishedReadRefusal.PastRetention],
            ignoreOrder: true);
    }
}
