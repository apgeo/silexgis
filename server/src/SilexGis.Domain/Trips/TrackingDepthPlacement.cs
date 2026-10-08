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

        // A station the viewer has no label for is not one a report can land on, whatever the
        // declaration says: honouring it would stamp a name the drawing cannot resolve, which is
        // the very fault this test exists to pass over.
        return stations.Any(s => !s.NoViewerLabel && s.ViewerName == declaration.ViewerStationName);
    }

    /// <summary>
    /// The most declared places one published survey is handed, and so also the most of a cave's
    /// labelled declarations a published read looks at.
    /// </summary>
    /// <remarks>
    /// A cave declares a handful — its pitches, its bivouac, its sump — and nothing stops it
    /// declaring thousands. The read that carries these is asked for by anybody holding a link,
    /// once a minute per open page, so what it costs is bounded by a number written down here
    /// rather than by how diligent a club has been. Far above anything a real cave names; past
    /// it, the deepest names are the ones left out.
    /// </remarks>
    public const int MaxPublishedPlaces = 200;

    /// <summary>
    /// Which of a cave's declared places a visitor without an account may be told, for one survey
    /// of that cave: those that have a name, whose station that survey holds, shallowest first,
    /// and no more than <paramref name="max"/> of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only a declaration somebody named.</b> A declaration without a label is an instruction
    /// to the placing above — "this depth means that station" — and says nothing a reader could
    /// read: the station is already in the drawing and the depth is already beside the person.
    /// </para>
    /// <para>
    /// <b>Only one that names a station of the survey being handed over</b>, by the same test a
    /// report passes before a declaration is honoured (<see cref="NamesAStationOf"/>), and for
    /// the same reason turned round: a name published for a station the drawing does not have is
    /// a name that can never appear beside anybody, and a list of them tells a visitor about
    /// places the page in front of them cannot show. The survey meant is the one the answer
    /// serves, which for an old trip is not always the cave's newest.
    /// </para>
    /// <para>
    /// <b>Whether any of this is told at all is not decided here.</b> That belongs to the
    /// installation, and a caller that has been told "no" does not ask. Nor is the cave's
    /// protection: a protected cave has no published answer for these to travel in.
    /// </para>
    /// </remarks>
    /// <param name="declarations">The cave's declarations, in any order.</param>
    /// <param name="stations">
    /// The served survey's stations — all of them, or only those the declarations could mean; the
    /// answer is the same.
    /// </param>
    /// <param name="max">The most to return; <see cref="MaxPublishedPlaces"/> on a published read.</param>
    public static IReadOnlyList<DeclaredDepthPlaces.Declared> PublishedPlaces(
        IReadOnlyCollection<DeclaredDepthPlaces.Declared> declarations,
        IReadOnlyCollection<TrackingDepthResolver.Station> stations,
        int max)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(stations);

        if (max <= 0)
        {
            return [];
        }

        return [.. declarations
            .Where(d => !string.IsNullOrWhiteSpace(d.PlaceLabel) && NamesAStationOf(stations, d))
            // By depth, and by the station under it only so that two answers about one state are
            // the same bytes — a published answer is compared with the one a reader already holds.
            .OrderBy(d => d.DepthM)
            .ThenBy(d => d.ViewerStationName, StringComparer.Ordinal)
            .Take(max)];
    }
}
