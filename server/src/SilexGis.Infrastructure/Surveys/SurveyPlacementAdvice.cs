// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// A declaration that would place a survey file at its cave, offered because the one it was
/// uploaded with does not.
/// </summary>
/// <param name="SourceEpsg">
/// The projected system the file's numbers are in, or null when the advice is to read them as
/// plain metres about a point.
/// </param>
/// <param name="OffByKm">
/// How far from its cave the file landed as declared, or null when it could not be placed at all.
/// </param>
/// <param name="SuggestedOffByKm">
/// How far from its cave the file lands as advised, or null when that cannot be known before the
/// file is read again — a local file lands wherever its origin is said to be.
/// </param>
public sealed record SurveyPlacementAdvice(int? SourceEpsg, double? OffByKm, double? SuggestedOffByKm)
{
    /// <summary>Read the file as plain metres about a point.</summary>
    public bool UseLocalMetres => SourceEpsg is null;
}

/// <summary>
/// Notices a survey file declared in the wrong coordinates, and finds the declaration that places
/// it at its cave.
///
/// <para>
/// The two mistakes this catches are the ones a person uploading a file can make by choosing the
/// wrong option, and both are silent without it. A file on a projected grid declared as metres
/// from a point is placed hundreds or thousands of kilometres away; a file declared in the grid
/// next door — a neighbouring UTM zone, which differs from the right one by nothing a person would
/// see in the numbers — lands hundreds of kilometres to one side. In both cases every stage
/// succeeds and the cave is drawn somewhere nobody looks.
/// </para>
///
/// <para>
/// What makes the mistake detectable is that the cave already has a position. A survey that
/// lands more than <see cref="FarKm"/> from it is wrong about something, and the coordinate
/// systems a cave's survey is plausibly written in are few enough to try one by one. Only a
/// candidate that lands within the same distance is offered, so the advice is never a guess that
/// merely lands somewhere less wrong.
/// </para>
/// </summary>
public static class SurveyPlacementCheck
{
    /// <summary>
    /// How far from its cave a survey may land before its declaration is doubted. A large system's
    /// passages reach several kilometres from its entrance and the middle of its footprint can sit
    /// a few kilometres from the entrance too; a wrong grid moves a survey by hundreds.
    /// </summary>
    public const double FarKm = 20;

    /// <summary>
    /// How far the middle of a file declared in local metres may be from its zero point. The
    /// largest cave systems reach a few tens of kilometres from their fixed station; a projected
    /// grid's numbers are hundreds of thousands to millions. A hundred kilometres sits well clear
    /// of both.
    /// </summary>
    public const double MaxLocalReachM = 100_000;

    /// <summary>
    /// National and continental grids tried besides the UTM zones around the cave, in the order
    /// they are preferred when two place a file equally well: the grids European cave surveys are
    /// commonly written in. Any other can still be declared by hand; it is only not guessed.
    /// </summary>
    private static readonly int[] NationalGrids =
    [
        31700, // Romania, Stereo 70
        3844,  // Romania, Stereo 70 (ETRS89 realisation)
        23700, // Hungary, EOV
        5514,  // Czechia and Slovakia, S-JTSK Krovak East North
        2180,  // Poland, CS92
        3794,  // Slovenia, D96/TM
        3765,  // Croatia, HTRS96/TM
        31287, // Austria, Lambert
        31254, // Austria, GK West
        31255, // Austria, GK Central
        31256, // Austria, GK East
        2056,  // Switzerland, LV95
        21781, // Switzerland, LV03
        2154,  // France, Lambert-93
        27700, // Great Britain, National Grid
        2100,  // Greece, GGRS87
        3035,  // Europe, LAEA
    ];

    /// <summary>
    /// Whether a file's middle sits too far from zero to be metres about one point of a cave.
    /// </summary>
    public static bool LooksProjected((double X, double Y) footprintCentre) =>
        Math.Sqrt((footprintCentre.X * footprintCentre.X) + (footprintCentre.Y * footprintCentre.Y)) > MaxLocalReachM;

    /// <summary>
    /// What to offer instead of a local declaration, for a file whose numbers are a grid's.
    /// Null when no grid tried places it near its cave, or the cave has no position to compare.
    /// </summary>
    public static SurveyPlacementAdvice? ForLocalFileOnAGrid(
        ICoordinateProjector projector, (double X, double Y) footprintCentre, (double Longitude, double Latitude)? cave)
    {
        if (cave is not { } near)
        {
            return null;
        }

        return NearestGrid(projector, footprintCentre, near, except: null) is { } best
            ? new SurveyPlacementAdvice(best.Epsg, OffByKm: null, best.Km)
            : null;
    }

    /// <summary>
    /// What to offer instead of a projected declaration that placed the file at
    /// <paramref name="placed"/>, or could not place it (null). Null when the declaration is fine,
    /// when nothing better was found, or when the cave has no position to compare.
    /// </summary>
    public static SurveyPlacementAdvice? ForProjectedFile(
        ICoordinateProjector projector,
        int declaredEpsg,
        (double X, double Y) footprintCentre,
        (double Longitude, double Latitude)? placed,
        (double Longitude, double Latitude)? cave)
    {
        if (cave is not { } near)
        {
            return null;
        }

        double? offBy = placed is { } at ? DistanceKm(at, near) : null;
        if (offBy is <= FarKm)
        {
            return null;
        }

        // Numbers this small are metres about a point, whatever grid they were declared in: read
        // on any grid they land near that grid's own origin, nowhere near a cave.
        if (!LooksProjected(footprintCentre))
        {
            return new SurveyPlacementAdvice(SourceEpsg: null, offBy, SuggestedOffByKm: null);
        }

        return NearestGrid(projector, footprintCentre, near, except: declaredEpsg) is { } best
            ? new SurveyPlacementAdvice(best.Epsg, offBy, best.Km)
            : null;
    }

    /// <summary>
    /// The grid, of those tried, that places the file's middle nearest its cave, provided that is
    /// within <see cref="FarKm"/>.
    /// </summary>
    private static (int Epsg, double Km)? NearestGrid(
        ICoordinateProjector projector,
        (double X, double Y) footprintCentre,
        (double Longitude, double Latitude) cave,
        int? except)
    {
        (int Epsg, double Km)? best = null;
        foreach (var epsg in Candidates(cave))
        {
            if (epsg == except || projector.ToWgs84(epsg, footprintCentre.X, footprintCentre.Y) is not { } at
                || Math.Abs(at.Latitude) > 90 || Math.Abs(at.Longitude) > 180)
            {
                continue;
            }

            var km = DistanceKm((at.Longitude, at.Latitude), cave);
            // Strictly nearer: on a tie the earlier candidate, which is the more common grid, wins.
            if (km <= FarKm && (best is null || km < best.Value.Km))
            {
                best = (epsg, km);
            }
        }

        return best;
    }

    /// <summary>
    /// The coordinate systems tried: the WGS 84 UTM zone the cave is in and its two neighbours —
    /// a survey near a zone boundary is as often written in the next zone as in its own — then the
    /// national grids.
    /// </summary>
    private static IEnumerable<int> Candidates((double Longitude, double Latitude) cave)
    {
        var zone = (int)Math.Floor((cave.Longitude + 180) / 6) + 1;
        var hemisphere = cave.Latitude >= 0 ? 32600 : 32700;
        foreach (var offset in new[] { 0, -1, 1 })
        {
            // Zones wrap round the antimeridian: zone 0 is zone 60, zone 61 is zone 1.
            var z = ((zone + offset - 1 + 60) % 60) + 1;
            yield return hemisphere + z;
        }

        foreach (var epsg in NationalGrids)
        {
            yield return epsg;
        }
    }

    /// <summary>Great-circle distance between two positions, in kilometres.</summary>
    public static double DistanceKm((double Longitude, double Latitude) a, (double Longitude, double Latitude) b)
    {
        const double earthRadiusKm = 6371.0088;
        var lat1 = a.Latitude * Math.PI / 180;
        var lat2 = b.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (b.Longitude - a.Longitude) * Math.PI / 180;
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
