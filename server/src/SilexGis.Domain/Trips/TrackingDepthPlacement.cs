// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>How a depth came to name a station, or why it did not.</summary>
public enum TrackingDepthPlacementOutcome
{
    /// <summary>The cave had declared what this depth is, and the declaration named a real station.</summary>
    Declared,

    /// <summary>Nobody had declared it, so the nearest station under the trip's datum answered.</summary>
    Measured,

    /// <summary>The model has no datum to measure depths from, so nothing can be worked out.</summary>
    ReferenceUnknown,

    /// <summary>There is a datum, and no station the trip's filter allows is at that depth.</summary>
    NoStationAtDepth,
}

/// <summary>Which station a depth means, and how that was settled.</summary>
public readonly record struct TrackingDepthPlacement(
    TrackingDepthPlacementOutcome Outcome,
    string? ViewerStationName);

/// <summary>
/// The one place that decides which station a reported depth refers to.
/// </summary>
/// <remarks>
/// <para>
/// Two things can answer, and the order between them is the whole point of this file. A cave can
/// declare that a depth is a named place, and that declaration is what a club actually means: it
/// knows which of several stations within a metre of one another is the place people call by that
/// depth, and geometry does not. Only a depth nobody has declared is answered by measurement —
/// the nearest station to it under the trip's own datum and station filter.
/// </para>
/// <para>
/// It lives in one function because three callers need the same answer: recording a report,
/// correcting one, and importing a sheet of them. A second copy of this ordering is how an
/// imported row lands on a different station from the same depth typed in by hand — and neither
/// would look wrong on its own.
/// </para>
/// </remarks>
public static class TrackingDepthPlacements
{
    /// <summary>
    /// The station a depth refers to: what the cave declared if it declared anything, otherwise
    /// the nearest station measured from the trip's datum.
    /// </summary>
    /// <param name="stations">The model's stations, under both of their names.</param>
    /// <param name="referenceStationName">
    /// The trip's depth datum, in whichever spelling it was stored; null takes the model's entrance.
    /// </param>
    public static TrackingDepthPlacement For(
        IReadOnlyCollection<DeclaredDepthPlaces.Declared> declarations,
        IReadOnlyCollection<TrackingDepthResolver.Station> stations,
        string? referenceStationName,
        IReadOnlyCollection<string> depthFilter,
        decimal depthM)
    {
        ArgumentNullException.ThrowIfNull(stations);

        // Placed as the log will store it, not as it was typed. The stored column keeps one
        // decimal, and a station chosen for 120.04 is not always the station 120.0 would have
        // chosen — so a row that reads "120.0 m" beside another could stand somewhere else. Done
        // here, before either answer below, so that every caller places on the number it keeps.
        var depth = TripTrackingRules.RecordedDepthM(depthM);

        // Checked against the model rather than trusted, because a declaration made against an
        // older survey can name a station the current one does not have, and a station name
        // nothing resolves is a marker that silently never appears. Such a declaration is passed
        // over and the measurement below answers instead — the wrong station is recoverable, an
        // invisible one is not even noticeable.
        if (DeclaredDepthPlaces.For(declarations, depth) is { } declared
            && NamesAStationOf(stations, declared))
        {
            return new TrackingDepthPlacement(
                TrackingDepthPlacementOutcome.Declared, declared.ViewerStationName);
        }

        var referenceZ = TrackingDepthResolver.ReferenceZ(stations, referenceStationName);
        if (referenceZ is null)
        {
            return new TrackingDepthPlacement(TrackingDepthPlacementOutcome.ReferenceUnknown, null);
        }

        var candidates = TrackingDepthResolver.Resolve(
            stations, referenceZ.Value, (double)depth, depthFilter, take: 1);
        if (candidates.Count == 0)
        {
            return new TrackingDepthPlacement(TrackingDepthPlacementOutcome.NoStationAtDepth, null);
        }

        // The winner under the name the viewer knows it by, which the resolver carried alongside
        // the rows' own. A depth-placed position is drawn on the model exactly like a pressed one,
        // and one stamped in the other spelling would never appear.
        return new TrackingDepthPlacement(
            TrackingDepthPlacementOutcome.Measured, candidates[0].ViewerName);
    }

    /// <summary>
    /// Whether the station a declaration names is one the model has, under the viewer's spelling
    /// the declaration is stored in.
    /// </summary>
    /// <remarks>
    /// The same test the placement above applies before honouring a declaration, offered on its own
    /// so that a list of declared places can say which of them a report would actually land on. A
    /// declaration that fails it is passed over silently at the moment of recording — that is the
    /// safe answer there — and the only way the fault gets noticed and fixed is for the surfaces
    /// that offer the place to be told.
    /// </remarks>
    public static bool NamesAStationOf(
        IReadOnlyCollection<TrackingDepthResolver.Station> stations,
        DeclaredDepthPlaces.Declared declaration)
    {
        ArgumentNullException.ThrowIfNull(stations);
        return stations.Any(s => s.ViewerName == declaration.ViewerStationName);
    }
}
