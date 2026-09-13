// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// Turns "the party is at about −120 m" into the stations that could mean, using nothing but
/// station altitudes. Pure by design: callers load the model's stations and every rule about
/// what a depth means lives here, testable without a database or a viewer.
///
/// Depth is metres below a reference altitude, displayed positive-down; a signed input means
/// the same place (−120 ≡ 120 down — the convention field notes actually use). The reference
/// is a named station's altitude when the trip configured one, otherwise the highest
/// entrance-flagged station, otherwise the model's highest station at all — a survey with no
/// entrance flags still gets a stable, explainable datum.
/// </summary>
public static class TrackingDepthResolver
{
    /// <summary>One station, reduced to what depth resolution needs.</summary>
    public readonly record struct Station(string Name, string? SurveyName, double Z, bool IsEntrance);

    /// <summary>A station that could be "at that depth", with how far off it is.</summary>
    public readonly record struct Candidate(string Name, string? SurveyName, double DepthM, double DeltaM);

    /// <summary>
    /// The altitude depths are measured from, or null when the model has no stations (or the
    /// named reference does not exist — the caller distinguishes by having checked the name).
    /// </summary>
    public static double? ReferenceZ(IReadOnlyCollection<Station> stations, string? referenceStationName)
    {
        if (referenceStationName is not null)
        {
            foreach (var s in stations)
            {
                if (string.Equals(s.Name, referenceStationName, StringComparison.Ordinal)) return s.Z;
            }
            return null;
        }

        double? best = null;
        foreach (var s in stations)
        {
            if (s.IsEntrance && (best is null || s.Z > best)) best = s.Z;
        }
        if (best is not null) return best;

        foreach (var s in stations)
        {
            if (best is null || s.Z > best) best = s.Z;
        }
        return best;
    }

    /// <summary>
    /// The stations closest to the asked depth, best first. Ties break by name so the answer
    /// is stable between calls. An empty filter means the whole model; a filter entry matches
    /// a station whose full name or survey name starts with it.
    /// </summary>
    public static IReadOnlyList<Candidate> Resolve(
        IReadOnlyCollection<Station> stations,
        double referenceZ,
        double askedDepthM,
        IReadOnlyCollection<string> filterPrefixes,
        int take = 5)
    {
        var target = Math.Abs(askedDepthM);

        return stations
            .Where(s => filterPrefixes.Count == 0 || filterPrefixes.Any(p =>
                s.Name.StartsWith(p, StringComparison.Ordinal)
                || (s.SurveyName?.StartsWith(p, StringComparison.Ordinal) ?? false)))
            .Select(s => new Candidate(s.Name, s.SurveyName, referenceZ - s.Z, Math.Abs(referenceZ - s.Z - target)))
            .OrderBy(c => c.DeltaM)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }
}
