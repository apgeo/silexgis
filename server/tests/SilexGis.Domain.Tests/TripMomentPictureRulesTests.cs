// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The two rules a picture on a tracked trip's moment introduces: what the anchor payload says,
/// and how far ahead of the clock a moment may claim to be.
/// </summary>
public class TripMomentPictureRulesTests
{
    [Fact]
    public void A_written_moment_reads_back_as_the_same_instant()
    {
        var at = new DateTimeOffset(2026, 9, 12, 14, 5, 0, TimeSpan.Zero);
        var payload = TripMomentAnchor.Payload(at);

        TripMomentAnchor.Read(payload).ShouldBe(at);
        // And what is written is what the rules accept — the two would otherwise be one refactor
        // apart from disagreeing, and the symptom would be a picture that is stored and never drawn.
        ResLinkRules.AnchorPayloadProblem(AnchorKind.TripMoment, payload).ShouldBeNull();
    }

    /// <summary>
    /// An offset is carried and is not a second meaning: 16:05+02:00 is the same moment as
    /// 14:05Z, and the host-link lookup treats them as one moment on the strength of this.
    /// </summary>
    [Fact]
    public void A_moment_is_the_instant_and_not_the_spelling()
    {
        var utc = new DateTimeOffset(2026, 9, 12, 14, 5, 0, TimeSpan.Zero);
        var local = new DateTimeOffset(2026, 9, 12, 16, 5, 0, TimeSpan.FromHours(2));

        TripMomentAnchor.Payload(utc).ShouldBe(TripMomentAnchor.Payload(local));
        TripMomentAnchor.Read(TripMomentAnchor.Payload(local)).ShouldBe(utc);
    }

    /// <summary>
    /// Every read of an anchor is a question. The payload column is free-form JSON, a row can have
    /// been written by a newer server, and a caller who may not read the target is handed no
    /// payload at all — so each of these has to answer "no moment" rather than throw inside a
    /// derivation.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"at": 1789012345}""")]
    [InlineData("""{"at": "yesterday"}""")]
    [InlineData("""{"t": 42}""")]
    public void An_unreadable_payload_names_no_moment(string? payload) =>
        TripMomentAnchor.Read(payload).ShouldBeNull();

    /// <summary>
    /// The same allowance a report is held to, applied to the same kind of claim. Both directions
    /// asserted: a moment inside the skew is accepted, because a rule that refused everything
    /// would pass the negative and attach nothing.
    /// </summary>
    [Fact]
    public void A_moment_may_not_be_further_ahead_than_the_clock_skew()
    {
        var now = new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero);

        TripTrackingRules.MomentIsInFuture(now.AddHours(-3), now).ShouldBeFalse();
        TripTrackingRules.MomentIsInFuture(now, now).ShouldBeFalse();
        TripTrackingRules.MomentIsInFuture(now + TripTrackingRules.RecordedAtSkew, now).ShouldBeFalse();
        TripTrackingRules.MomentIsInFuture(
            now + TripTrackingRules.RecordedAtSkew + TimeSpan.FromSeconds(1), now).ShouldBeTrue();
        TripTrackingRules.MomentIsInFuture(now.AddHours(3), now).ShouldBeTrue();
    }

    /// <summary>
    /// A moment well outside the stretch of time the watch covers is <b>not</b> a rule — a camera
    /// clock set to the wrong hour is fixed by re-anchoring, not by throwing the picture away. This
    /// states the absence so that adding such a refusal later is a deliberate act rather than a
    /// tightening nobody notices.
    /// </summary>
    [Fact]
    public void A_moment_long_before_the_trip_is_not_refused()
    {
        var now = new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero);
        TripTrackingRules.MomentIsInFuture(now.AddYears(-5), now).ShouldBeFalse();
    }
}
