// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Placing a picture that carries no fix of its own by matching when it was taken against a
/// recorded track — including the two ways that goes wrong: a camera clock that is out, and a
/// gap in the track where the unit lost the sky.
/// </summary>
public class TrackFixIndexTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A track walking due east, one fix a minute, a tenth of a degree apart.</summary>
    private static TrackFixIndex Walk(int fixes = 10) => TrackFixIndex.Of(
        Enumerable.Range(0, fixes).Select(i => new TrackFix(Start.AddMinutes(i), 25.0 + (i * 0.1), 45.0)));

    [Fact]
    public void A_picture_taken_on_a_recorded_fix_is_placed_on_it()
    {
        var match = Walk().Match(Start.AddMinutes(3), clockOffsetSeconds: 0, toleranceSeconds: 120);

        match.ShouldNotBeNull();
        match.Longitude.ShouldBe(25.3, 1e-9);
        match.SecondsFromFix.ShouldBe(0);
        match.Interpolated.ShouldBeFalse();
    }

    [Fact]
    public void A_picture_between_two_close_fixes_is_placed_between_them()
    {
        var match = Walk().Match(Start.AddSeconds(90), clockOffsetSeconds: 0, toleranceSeconds: 120);

        match.ShouldNotBeNull();
        match.Interpolated.ShouldBeTrue();
        match.Longitude.ShouldBe(25.15, 1e-9); // halfway between minute one and minute two
        match.SecondsFromFix.ShouldBe(30);
    }

    [Fact]
    public void A_camera_clock_an_hour_out_is_corrected_rather_than_refused()
    {
        // The whole reason the offset field exists: a camera left on the previous zone reads an
        // hour early, and every picture from the trip would otherwise fall outside the track.
        var index = Walk();

        index.Match(Start.AddMinutes(3).AddHours(-1), clockOffsetSeconds: 0, toleranceSeconds: 120)
            .ShouldBeNull();
        index.Match(Start.AddMinutes(3).AddHours(-1), clockOffsetSeconds: 3600, toleranceSeconds: 120)
            .ShouldNotBeNull()
            .Longitude.ShouldBe(25.3, 1e-9);
    }

    [Fact]
    public void A_picture_taken_in_a_gap_in_the_track_snaps_to_the_near_end_rather_than_being_interpolated()
    {
        // The unit lost the sky for half an hour. A picture two minutes into that gap is
        // bracketed by a fix two minutes back and one twenty-eight minutes forward;
        // interpolating across it would place the photographer a fourteenth of the way along a
        // walk they did not take.
        var index = TrackFixIndex.Of(
        [
            new TrackFix(Start, 25.0, 45.0),
            new TrackFix(Start.AddMinutes(30), 26.0, 45.0),
        ]);

        var match = index.Match(Start.AddMinutes(2), clockOffsetSeconds: 0, toleranceSeconds: 180);

        match.ShouldNotBeNull();
        match.Interpolated.ShouldBeFalse();
        match.Longitude.ShouldBe(25.0, 1e-9);
        match.SecondsFromFix.ShouldBe(120);
    }

    [Fact]
    public void A_picture_further_from_every_fix_than_the_tolerance_is_not_placed()
    {
        Walk().Match(Start.AddHours(5), clockOffsetSeconds: 0, toleranceSeconds: 120).ShouldBeNull();
        Walk().Match(Start.AddHours(-5), clockOffsetSeconds: 0, toleranceSeconds: 120).ShouldBeNull();
    }

    [Fact]
    public void A_picture_just_before_the_track_starts_is_placed_on_its_first_fix()
    {
        var match = Walk().Match(Start.AddSeconds(-60), clockOffsetSeconds: 0, toleranceSeconds: 120);

        match.ShouldNotBeNull();
        match.Longitude.ShouldBe(25.0, 1e-9);
        match.SecondsFromFix.ShouldBe(60);
    }

    [Fact]
    public void Fixes_recorded_out_of_order_are_still_searched_correctly()
    {
        // A GPX merged from two days, or written by a unit whose clock stepped, holds times out
        // of order — and every search here assumes they are not.
        var index = TrackFixIndex.Of(
        [
            new TrackFix(Start.AddMinutes(2), 25.2, 45.0),
            new TrackFix(Start, 25.0, 45.0),
            new TrackFix(Start.AddMinutes(1), 25.1, 45.0),
        ]);

        index.Match(Start.AddMinutes(1), clockOffsetSeconds: 0, toleranceSeconds: 30)
            .ShouldNotBeNull()
            .Longitude.ShouldBe(25.1, 1e-9);
    }

    [Fact]
    public void A_track_with_no_fixes_places_nothing()
    {
        var index = TrackFixIndex.Of([]);

        index.Count.ShouldBe(0);
        index.FirstTime.ShouldBeNull();
        index.Match(Start, clockOffsetSeconds: 0, toleranceSeconds: 120).ShouldBeNull();
    }
}
