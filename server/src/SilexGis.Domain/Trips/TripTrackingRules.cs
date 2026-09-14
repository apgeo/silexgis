// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// The tracking lifecycle's legality table and the slice's shared limits — one home, so the
/// API and its tests cannot drift apart on what a tracking state may become.
/// </summary>
public static class TripTrackingRules
{
    public const int MaxTitleLength = 200;
    public const int MaxNoteLength = 2000;
    public const int MaxStationNameLength = 400;
    public const int MaxDepthFilterEntries = 200;
    public const decimal MaxDepthAbsM = 5000m;
    public const int MaxCaversPerWrite = 100;

    /// <summary>How long a published participant's display label may be — a caption, not a bio.</summary>
    public const int MaxLabelLength = 200;

    /// <summary>
    /// Longest publication token this application will even hash. One it mints is 43 characters;
    /// the bound keeps an unbounded string out of the lookup, and a token over it is answered
    /// exactly as an unknown one is.
    /// </summary>
    public const int MaxShareTokenLength = 100;

    /// <summary>How far ahead of the server clock a report may claim to be — clock skew, not planning.</summary>
    public static readonly TimeSpan RecordedAtSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Off arms, Armed closes, Closed re-arms (a party that turns out to still be underground),
    /// and any state restates itself. Nothing returns to Off: history exists, and "we never
    /// tracked this trip" would be a lie the moment one event row is on the timeline.
    /// </summary>
    public static bool MayTransition(TripTrackingState from, TripTrackingState to) => (from, to) switch
    {
        _ when from == to => true,
        (TripTrackingState.Off, TripTrackingState.Armed) => true,
        (TripTrackingState.Armed, TripTrackingState.Closed) => true,
        (TripTrackingState.Closed, TripTrackingState.Armed) => true,
        _ => false,
    };
}
