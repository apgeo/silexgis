// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>One recorded position on a track, with the moment it was recorded.</summary>
public sealed record TrackFix(DateTimeOffset Time, double Longitude, double Latitude);

/// <summary>
/// Where a picture was, worked out from when it was taken.
/// </summary>
/// <param name="SecondsFromFix">
/// How far the corrected capture time sits from the nearest recorded fix. Carried rather than
/// discarded because it is the only honest measure of how much this position is worth: a
/// picture matched four seconds from a fix is placed; one matched a hundred seconds from a fix
/// is a guess about somebody walking, and the reviewer should see which they are looking at.
/// </param>
/// <param name="Interpolated">
/// Whether the position sits between two recorded fixes (both close enough to believe) or is
/// simply the nearest one.
/// </param>
public sealed record TrackMatch(
    double Longitude, double Latitude, double SecondsFromFix, bool Interpolated);

/// <summary>
/// A track's recorded fixes, ordered, ready to answer "where was this taken?".
///
/// <para>
/// Built once per track and asked once per picture: a trip's worth of photographs against a
/// day's worth of track points is a few hundred questions over a few thousand fixes, so each
/// one is a binary search rather than a walk.
/// </para>
/// </summary>
public sealed class TrackFixIndex
{
    private readonly TrackFix[] fixes;
    private readonly long[] ticks;

    private TrackFixIndex(TrackFix[] ordered)
    {
        fixes = ordered;
        ticks = [.. ordered.Select(f => f.Time.UtcTicks)];
    }

    public int Count => fixes.Length;

    public DateTimeOffset? FirstTime => fixes.Length == 0 ? null : fixes[0].Time;

    public DateTimeOffset? LastTime => fixes.Length == 0 ? null : fixes[^1].Time;

    /// <summary>
    /// The fixes as an index. Sorted here rather than trusted: a GPX written by merging two
    /// days, or by a unit whose clock stepped, holds times out of order, and every search below
    /// assumes they are not.
    /// </summary>
    public static TrackFixIndex Of(IEnumerable<TrackFix> fixes) =>
        new([.. fixes.OrderBy(f => f.Time)]);

    /// <summary>
    /// Where the camera was when it recorded <paramref name="capturedAt"/>, or null when the
    /// track says nothing about that moment.
    /// </summary>
    /// <param name="clockOffsetSeconds">
    /// Added to the capture time before comparing. Camera clocks drift, and one set to the wrong
    /// zone is out by whole hours while looking entirely plausible — without this the whole
    /// method answers confidently and wrongly.
    /// </param>
    /// <param name="toleranceSeconds">
    /// How far from a recorded fix a picture may still be placed.
    /// </param>
    /// <remarks>
    /// Two fixes on either side are interpolated between only when <em>both</em> are within
    /// tolerance. Where a unit lost the sky for half an hour, the picture taken two minutes into
    /// that gap is bracketed by a fix two minutes back and one twenty-eight minutes forward, and
    /// interpolating across it would place the photographer at a smooth fraction of a walk they
    /// did not take. Snapping to the near end says the true thing instead: this was taken near
    /// there, two minutes after the last time we know where "there" was.
    /// </remarks>
    public TrackMatch? Match(DateTimeOffset capturedAt, int clockOffsetSeconds, int toleranceSeconds)
    {
        if (fixes.Length == 0 || toleranceSeconds < 0)
        {
            return null;
        }

        var corrected = capturedAt.AddSeconds(clockOffsetSeconds).UtcTicks;
        var tolerance = TimeSpan.FromSeconds(toleranceSeconds).Ticks;

        var position = Array.BinarySearch(ticks, corrected);
        if (position >= 0)
        {
            var exact = fixes[position];
            return new TrackMatch(exact.Longitude, exact.Latitude, 0, Interpolated: false);
        }

        var after = ~position;
        var before = after - 1;

        var beforeGap = before >= 0 ? corrected - ticks[before] : long.MaxValue;
        var afterGap = after < ticks.Length ? ticks[after] - corrected : long.MaxValue;

        if (beforeGap <= tolerance && afterGap <= tolerance)
        {
            // Both ends believable: the photographer was somewhere on the line between them,
            // and how far along is exactly the fraction of the elapsed time.
            var span = (double)(beforeGap + afterGap);
            var fraction = span == 0 ? 0 : beforeGap / span;
            var start = fixes[before];
            var end = fixes[after];
            return new TrackMatch(
                start.Longitude + ((end.Longitude - start.Longitude) * fraction),
                start.Latitude + ((end.Latitude - start.Latitude) * fraction),
                TimeSpan.FromTicks(Math.Min(beforeGap, afterGap)).TotalSeconds,
                Interpolated: true);
        }

        var nearestGap = Math.Min(beforeGap, afterGap);
        if (nearestGap > tolerance)
        {
            return null;
        }

        var nearest = beforeGap <= afterGap ? fixes[before] : fixes[after];
        return new TrackMatch(
            nearest.Longitude,
            nearest.Latitude,
            TimeSpan.FromTicks(nearestGap).TotalSeconds,
            Interpolated: false);
    }
}
