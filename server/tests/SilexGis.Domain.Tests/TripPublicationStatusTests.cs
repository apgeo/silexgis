// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The word an administrator is shown for a published link, and the two windows the anonymous
/// routes serve by — one composition, read two ways.
/// </summary>
/// <remarks>
/// <para>
/// The named cases say what each word means, each beside the neighbour it differs from by one
/// thing. The two walks at the end are the ones that earn their place: they hold the status to the
/// window rules it is built from over every combination of inputs, so that a status which drifted
/// from what the routes answer — the one failure this type exists to prevent — fails here rather
/// than on an administrator's screen.
/// </para>
/// </remarks>
public class TripPublicationStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);
    private static readonly DateOnly TripDate = new(2026, 9, 18);

    /// <summary>Far enough ahead that the expiry is never what decides a case, unless it is.</summary>
    private static readonly DateTimeOffset Later = Now.AddDays(30);

    private static PublishedLinkStatus Status(
        TripTrackingState state,
        DateTimeOffset? closedAt = null,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? expiresAt = null,
        bool tripStillPublished = true,
        bool archiveEnabled = true,
        TimeSpan? retention = null,
        bool cavePublishable = true)
    {
        var expiry = expiresAt ?? Later;
        return TripPublicationStatus.OfLink(
            Now, revokedAt, expiry, state, closedAt,
            latestUnrevokedExpiry: tripStillPublished && revokedAt is null ? expiry : null,
            TripDate, tripDateEnd: null, Grace, archiveEnabled, retention, cavePublishable);
    }

    [Fact]
    public void A_running_watch_is_followable_and_one_that_just_closed_is_in_grace()
    {
        Status(TripTrackingState.Armed).ShouldBe(PublishedLinkStatus.Followable);
        Status(TripTrackingState.Closed, closedAt: Now.AddHours(-1)).ShouldBe(PublishedLinkStatus.InGrace);
    }

    [Fact]
    public void Past_the_grace_window_a_closed_trip_is_in_the_archive()
    {
        // The same closed watch either side of the boundary, so the word changes for one reason.
        Status(TripTrackingState.Closed, closedAt: Now - Grace + TimeSpan.FromSeconds(1))
            .ShouldBe(PublishedLinkStatus.InGrace);
        Status(TripTrackingState.Closed, closedAt: Now - Grace)
            .ShouldBe(PublishedLinkStatus.InArchive);
    }

    [Fact]
    public void A_link_that_ran_out_keeps_a_finished_trip_in_the_archive_and_a_running_one_nowhere()
    {
        // An expired link nobody took back is exactly how a finished trip sits in the archive.
        Status(TripTrackingState.Closed, closedAt: Now.AddDays(-30), expiresAt: Now.AddDays(-1))
            .ShouldBe(PublishedLinkStatus.InArchive);

        // And the fail-closed corner: the watch still runs, the link has run out, and the party is
        // neither followable on a dead link nor history.
        Status(TripTrackingState.Armed, expiresAt: Now.AddDays(-1))
            .ShouldBe(PublishedLinkStatus.Lapsed);
    }

    [Fact]
    public void A_watch_that_was_stood_down_opens_nothing_live_and_is_history_like_any_other()
    {
        // Off is never followable, and it is not armed either, so the archive has it.
        Status(TripTrackingState.Off).ShouldBe(PublishedLinkStatus.InArchive);
        Status(TripTrackingState.Off, archiveEnabled: false).ShouldBe(PublishedLinkStatus.Lapsed);
    }

    [Fact]
    public void With_the_archive_off_or_its_retention_over_a_finished_trip_has_lapsed()
    {
        var closedAt = Now.AddDays(-30);
        Status(TripTrackingState.Closed, closedAt).ShouldBe(PublishedLinkStatus.InArchive);
        Status(TripTrackingState.Closed, closedAt, archiveEnabled: false).ShouldBe(PublishedLinkStatus.Lapsed);
        Status(TripTrackingState.Closed, closedAt, retention: TimeSpan.FromHours(1))
            .ShouldBe(PublishedLinkStatus.Lapsed);

        // Switching the archive off must not touch a party that is being followed.
        Status(TripTrackingState.Armed, archiveEnabled: false).ShouldBe(PublishedLinkStatus.Followable);
    }

    [Fact]
    public void A_cave_that_may_not_be_published_withholds_a_link_that_would_otherwise_open()
    {
        Status(TripTrackingState.Armed, cavePublishable: false).ShouldBe(PublishedLinkStatus.Withheld);
        Status(TripTrackingState.Closed, closedAt: Now.AddDays(-30), cavePublishable: false)
            .ShouldBe(PublishedLinkStatus.Withheld);

        // A link that opens nothing anyway is lapsed, not withheld: giving the cave back would
        // bring nothing with it, and the word must not suggest that it would.
        Status(TripTrackingState.Armed, expiresAt: Now.AddDays(-1), cavePublishable: false)
            .ShouldBe(PublishedLinkStatus.Lapsed);
    }

    [Fact]
    public void A_revoked_link_is_revoked_whatever_else_is_true_of_it()
    {
        Status(TripTrackingState.Armed, revokedAt: Now.AddMinutes(-1)).ShouldBe(PublishedLinkStatus.Revoked);
        Status(TripTrackingState.Armed, revokedAt: Now.AddMinutes(-1), cavePublishable: false)
            .ShouldBe(PublishedLinkStatus.Revoked);

        // Including when a second link of the same trip still stands: the trip is published, this
        // link is not.
        TripPublicationStatus.OfLink(
                Now, revokedAt: Now.AddMinutes(-1), Later, TripTrackingState.Closed, Now.AddDays(-30),
                latestUnrevokedExpiry: Later, TripDate, null, Grace, archiveEnabled: true, retention: null,
                cavePublishable: true)
            .ShouldBe(PublishedLinkStatus.Revoked);
        TripPublicationStatus.WindowsOf(
                Now, revokedAt: Now.AddMinutes(-1), Later, TripTrackingState.Closed, Now.AddDays(-30),
                latestUnrevokedExpiry: Later, TripDate, null, Grace, retention: null)
            .Neither.ShouldBeTrue();
    }

    /// <summary>Every combination of the inputs that can change the answer.</summary>
    private static IEnumerable<(
        DateTimeOffset? RevokedAt, DateTimeOffset ExpiresAt, TripTrackingState State, DateTimeOffset? ClosedAt,
        DateTimeOffset? LatestUnrevokedExpiry, TimeSpan? Retention)> Inputs()
    {
        DateTimeOffset?[] revocations = [null, Now.AddHours(-3)];
        DateTimeOffset[] expiries = [Now.AddDays(-1), Now, Now.AddDays(30)];
        TripTrackingState[] states = [TripTrackingState.Off, TripTrackingState.Armed, TripTrackingState.Closed];
        DateTimeOffset?[] closings = [null, Now.AddHours(-1), Now - Grace, Now.AddDays(-30)];
        TimeSpan?[] retentions = [null, TimeSpan.FromHours(1), TimeSpan.FromDays(365)];

        foreach (var revokedAt in revocations)
        foreach (var expiresAt in expiries)
        foreach (var state in states)
        foreach (var closedAt in closings)
        foreach (var retention in retentions)
        {
            // What the trip's other links leave standing: nothing, this link's own expiry, or a
            // later one belonging to a sibling link. A revoked link cannot be its trip's latest
            // unrevoked one, so that pairing is not generated.
            DateTimeOffset?[] latest = revokedAt is null
                ? [expiresAt, Now.AddDays(60)]
                : [null, Now.AddDays(-2), Now.AddDays(60)];
            foreach (var latestUnrevokedExpiry in latest)
            {
                yield return (revokedAt, expiresAt, state, closedAt, latestUnrevokedExpiry, retention);
            }
        }
    }

    [Fact]
    public void The_two_windows_are_exactly_the_gate_the_past_routes_are_documented_to_answer_by()
    {
        var walked = 0;
        var open = 0;
        foreach (var i in Inputs())
        {
            var windows = TripPublicationStatus.WindowsOf(
                Now, i.RevokedAt, i.ExpiresAt, i.State, i.ClosedAt, i.LatestUnrevokedExpiry,
                TripDate, null, Grace, i.Retention);
            var gate = TripPastTrackWindow.OpensThePast(
                Now, i.RevokedAt, i.ExpiresAt, i.State, i.ClosedAt, i.LatestUnrevokedExpiry,
                TripDate, null, Grace, i.Retention);

            (!windows.Neither).ShouldBe(gate, $"{i}");
            windows.Live.ShouldBe(
                TripPublicationWindow.IsOpen(Now, i.RevokedAt, i.ExpiresAt, i.State, i.ClosedAt, Grace), $"{i}");
            (windows.Live && windows.Past).ShouldBeFalse($"both windows at {i}");

            walked++;
            if (!windows.Neither) open++;
        }

        // The walk has to have met both answers, or it proved nothing about either.
        walked.ShouldBeGreaterThan(200);
        open.ShouldBeGreaterThan(20);
        (walked - open).ShouldBeGreaterThan(20);
    }

    [Fact]
    public void The_archive_switch_is_over_the_past_window_only()
    {
        // All four pairs, though the window rules never produce the last: the reading must not
        // depend on that.
        new PublishedLinkWindows(Live: true, Past: false).OpensAnything(archiveEnabled: false).ShouldBeTrue();
        new PublishedLinkWindows(Live: true, Past: false).OpensAnything(archiveEnabled: true).ShouldBeTrue();
        new PublishedLinkWindows(Live: false, Past: true).OpensAnything(archiveEnabled: true).ShouldBeTrue();
        new PublishedLinkWindows(Live: false, Past: true).OpensAnything(archiveEnabled: false).ShouldBeFalse();
        new PublishedLinkWindows(Live: false, Past: false).OpensAnything(archiveEnabled: true).ShouldBeFalse();
        new PublishedLinkWindows(Live: true, Past: true).OpensAnything(archiveEnabled: false).ShouldBeTrue();
    }

    [Fact]
    public void The_followed_list_opens_for_a_live_link_always_and_for_a_lapsed_one_inside_its_period()
    {
        var live = new PublishedLinkWindows(Live: true, Past: false);
        var lapsed = new PublishedLinkWindows(Live: false, Past: true);

        // A link still following its own party: neither lever is about it.
        foreach (var archive in new[] { true, false })
        {
            foreach (var within in new[] { true, false })
            {
                live.OpensTheFollowedList(archive, within).ShouldBeTrue();
            }
        }

        // A link whose trip is over: the archive on, and still inside the period.
        lapsed.OpensTheFollowedList(archiveEnabled: true, withinSiblingWindow: true).ShouldBeTrue();
        lapsed.OpensTheFollowedList(archiveEnabled: true, withinSiblingWindow: false).ShouldBeFalse();
        lapsed.OpensTheFollowedList(archiveEnabled: false, withinSiblingWindow: true).ShouldBeFalse();

        // And a link that opens nothing opens this neither, whatever is passed.
        default(PublishedLinkWindows).OpensTheFollowedList(archiveEnabled: true, withinSiblingWindow: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void The_followed_list_never_opens_for_a_link_the_administrators_list_would_call_shut()
    {
        // Every combination there is. The list of published links describes a link by
        // OpensAnything; a followed list that opened where that says "nothing" would be served by
        // a link the administrator is told has lapsed.
        var bits = new[] { false, true };
        var opened = 0;
        foreach (var isLive in bits)
        {
            foreach (var isPast in bits)
            {
                foreach (var archive in bits)
                {
                    foreach (var within in bits)
                    {
                        var windows = new PublishedLinkWindows(isLive, isPast);
                        if (!windows.OpensTheFollowedList(archive, within)) continue;
                        opened++;
                        windows.OpensAnything(archive).ShouldBeTrue();
                    }
                }
            }
        }

        opened.ShouldBeGreaterThan(0, "a rule that opened nothing would satisfy the line above for free");
    }

    [Fact]
    public void Every_status_is_the_one_the_window_rules_imply_and_every_status_is_reached()
    {
        var seen = new HashSet<PublishedLinkStatus>();
        foreach (var i in Inputs())
        foreach (var archiveEnabled in new[] { true, false })
        foreach (var cavePublishable in new[] { true, false })
        {
            var status = TripPublicationStatus.OfLink(
                Now, i.RevokedAt, i.ExpiresAt, i.State, i.ClosedAt, i.LatestUnrevokedExpiry,
                TripDate, null, Grace, archiveEnabled, i.Retention, cavePublishable);
            seen.Add(status);

            // The rules asked directly, with nothing of the type under test between them and the
            // expectation.
            var live = TripPublicationWindow.IsOpen(
                Now, i.RevokedAt, i.ExpiresAt, i.State, i.ClosedAt, Grace);
            var past = i.RevokedAt is null && archiveEnabled && TripPastTrackWindow.IsReadableAsPast(
                Now, i.State, i.ClosedAt, i.LatestUnrevokedExpiry, TripDate, null, Grace, i.Retention);

            var expected =
                i.RevokedAt is not null ? PublishedLinkStatus.Revoked
                : !live && !past ? PublishedLinkStatus.Lapsed
                : !cavePublishable ? PublishedLinkStatus.Withheld
                : !live ? PublishedLinkStatus.InArchive
                : i.State == TripTrackingState.Armed ? PublishedLinkStatus.Followable
                : PublishedLinkStatus.InGrace;

            status.ShouldBe(expected, $"{i} archive={archiveEnabled} cave={cavePublishable}");
        }

        seen.ShouldBe(Enum.GetValues<PublishedLinkStatus>(), ignoreOrder: true);
    }
}
