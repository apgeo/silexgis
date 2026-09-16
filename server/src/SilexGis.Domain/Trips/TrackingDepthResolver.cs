// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

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
///
/// <para>
/// <b>A station is carried under both of its names, and everything here that compares a name with
/// something a person wrote compares it with both.</b> For one of the two line-plot formats the
/// survey rows and the viewer that draws them spell one station differently
/// (<see cref="SurveyStationNames"/> holds that rule and the reason for it), and the two things
/// this resolver is told — which station is the depth datum, which parts of the survey to look in —
/// are typed by somebody who read a name off whichever of the two was in front of them. Refusing
/// the spelling they happened to have is a filter that quietly matches nothing and a datum that
/// quietly matches none, neither of which says why. What comes back carries both names too, so the
/// caller stores and displays the one its surface speaks without re-deriving it.
/// </para>
/// </summary>
public static class TrackingDepthResolver
{
    /// <summary>One station, reduced to what depth resolution needs, under both of its names.</summary>
    /// <param name="Name">As the survey rows hold it — the vocabulary the survey name below shares.</param>
    /// <param name="ViewerName">As the viewer that draws the model addresses it; often the same string.</param>
    public readonly record struct Station(
        string Name, string ViewerName, string? SurveyName, double Z, bool IsEntrance)
    {
        /// <summary>
        /// The station one survey row describes, both names worked out from the model it belongs
        /// to. Made here rather than at each caller so that every list of stations fed to this
        /// resolver gets its second name from the one rule that knows how.
        /// </summary>
        public static Station Of(
            SurveyModelFormat format,
            string? rootSurveyName,
            string name,
            string? surveyName,
            double z,
            bool isEntrance) =>
            new(name, SurveyStationNames.ViewerName(format, rootSurveyName, name), surveyName, z, isEntrance);
    }

    /// <summary>A station that could be "at that depth", with how far off it is.</summary>
    public readonly record struct Candidate(
        string Name, string ViewerName, string? SurveyName, double DepthM, double DeltaM);

    /// <summary>
    /// The altitude depths are measured from, or null when the model has no stations (or the
    /// named reference does not exist — the caller distinguishes by having checked the name).
    ///
    /// <para>
    /// The datum is matched under either of the station's names. It is stored in the viewer's
    /// spelling, because that is what the administrator who set it was reading; a name in the
    /// rows' own spelling names the same station and is taken as naming it.
    /// </para>
    /// </summary>
    public static double? ReferenceZ(IReadOnlyCollection<Station> stations, string? referenceStationName)
    {
        if (referenceStationName is not null)
        {
            foreach (var s in stations)
            {
                if (string.Equals(s.Name, referenceStationName, StringComparison.Ordinal)
                    || string.Equals(s.ViewerName, referenceStationName, StringComparison.Ordinal))
                {
                    return s.Z;
                }
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
    /// a station whose full name — in either spelling — or whose survey name starts with it.
    ///
    /// <para>
    /// Matching either spelling only ever widens what a filter keeps, and that is the safe
    /// direction: the filter exists to stop a depth landing in the wrong part of a cave, and it is
    /// written by somebody reading station names off a list beside it. A filter that matched only
    /// the spelling they did not use would keep nothing at all, and every depth report would be
    /// refused with nothing on screen explaining why.
    /// </para>
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
                || s.ViewerName.StartsWith(p, StringComparison.Ordinal)
                || (s.SurveyName?.StartsWith(p, StringComparison.Ordinal) ?? false)))
            .Select(s => new Candidate(
                s.Name, s.ViewerName, s.SurveyName, referenceZ - s.Z, Math.Abs(referenceZ - s.Z - target)))
            .OrderBy(c => c.DeltaM)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }
}
