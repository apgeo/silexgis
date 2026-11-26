// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Surveys;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace SilexGis.Infrastructure.Flybys;

/// <summary>
/// In what order a cave appears where a bundle reveals it: a number for every place along its
/// survey — the distance along the passages, in true metres, from its entrances — which the
/// renderer compares with a threshold that rises while the clip runs.
///
/// <para>
/// <b>The rule is the survey library's.</b> The cave's stored legs are handed to it as a model of
/// its own kind and it measures them: from the stations the survey flags as entrances; a part of
/// the cave joined to no entrance from the first station of its first leg. It also gives a wall
/// its numbers — every vertex takes the number of the nearest place on the centreline — so what
/// is worked out here is only which leg meets which.
/// </para>
///
/// <para>
/// <b>There is no reveal by date, because no leg here has a date.</b> A survey file may say on
/// which day each leg was measured; what is kept of a survey is where its legs run, and one date
/// for the whole model. A cave's history cannot be told from that.
/// </para>
/// </summary>
public sealed class FlybyBundleRevealValues
{
    private readonly IReadOnlyList<FlybyBundleLeg> legs;
    private readonly IReadOnlyList<(double From, double To)> values;
    private readonly Dictionary<string, double> atStation = new(StringComparer.Ordinal);

    private FlybyBundleRevealValues(IReadOnlyList<FlybyBundleLeg> legs, IReadOnlyList<(double From, double To)> values, double min, double max)
    {
        this.legs = legs;
        this.values = values;
        Min = min;
        Max = max;
        for (var i = 0; i < legs.Count; i++)
        {
            atStation.TryAdd(StartOf(legs[i]), values[i].From);
            atStation.TryAdd(EndOf(legs[i]), values[i].To);
        }
    }

    /// <summary>The lowest number: nothing of the cave is there while the threshold is at it.</summary>
    public double Min { get; }

    /// <summary>The highest number: all of the cave is there once the threshold is past it.</summary>
    public double Max { get; }

    /// <summary>
    /// The numbers of a cave, or null for one with no leg to measure along.
    /// </summary>
    public static FlybyBundleRevealValues? Of(FlybyBundleCave cave)
    {
        ArgumentNullException.ThrowIfNull(cave);
        if (cave.SurveyLegs.Count == 0)
        {
            return null;
        }

        // Metres about the first leg's start. A station is one place: the first place a name is
        // met at stands for every leg that names it, so two legs meet where the survey says they
        // do and not where the last digit of two sums agrees.
        var first = cave.SurveyLegs[0];
        var about = new LocalTangentFrame(first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        var places = new Dictionary<string, CaveVector3>(StringComparer.Ordinal);
        CaveVector3 Place(string station, double longitude, double latitude, double altitudeM)
        {
            if (!places.TryGetValue(station, out var place))
            {
                var (east, north, up) = about.ToLocal(longitude, latitude, altitudeM);
                places[station] = place = new CaveVector3(Math.Round(east, 3), Math.Round(north, 3), Math.Round(up, 3));
            }

            return place;
        }

        var kept = new List<FlybyBundleLeg>();
        var shots = new List<CaveShot>();
        foreach (var leg in cave.SurveyLegs)
        {
            var from = Place(StartOf(leg), leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM);
            var to = Place(EndOf(leg), leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM);

            // The library measures along the legs it would draw: one shorter than a millimetre is none.
            if ((to - from).Length < 1e-3)
            {
                continue;
            }

            kept.Add(leg);
            shots.Add(new CaveShot { FromPosition = from, ToPosition = to });
        }

        if (shots.Count == 0)
        {
            return null;
        }

        var entrances = cave.EntranceStations.ToHashSet(StringComparer.Ordinal);
        uint id = 0;
        var model = new CaveModel
        {
            Shots = shots,
            Stations =
            [
                .. places.Select(place => new CaveStation
                {
                    Id = id++,
                    Name = place.Key,
                    Position = place.Value,
                    Flags = entrances.Contains(place.Key) ? CaveStationFlags.Entrance : 0,
                }),
            ],
        };
        var field = RevealField.Build(model, new RevealOptions { Source = RevealSource.Distance });
        if (field is null || field.Legs.Count != kept.Count)
        {
            return null;
        }

        return new FlybyBundleRevealValues(kept, [.. field.Legs.Select(leg => (leg.FromValue, leg.ToValue))], field.Min, field.Max);
    }

    /// <summary>
    /// The number of each vertex of a mesh of this cave that was not built from these legs — its
    /// walls: that of the nearest place on the centreline.
    /// </summary>
    /// <param name="mesh">Metres east, north and up of its own anchor.</param>
    public IReadOnlyList<double> On(FlybyBundleMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var about = new LocalTangentFrame(mesh.AnchorLongitude, mesh.AnchorLatitude, mesh.AnchorAltitudeM);
        CaveVector3 Local(double longitude, double latitude, double altitudeM)
        {
            var (east, north, up) = about.ToLocal(longitude, latitude, altitudeM);
            return new CaveVector3(east, north, up);
        }

        var field = new RevealField
        {
            Source = RevealSource.Distance,
            Legs =
            [
                .. legs.Select((leg, i) => new RevealLeg(
                    Local(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM),
                    Local(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM),
                    values[i].From,
                    values[i].To)),
            ],
            Min = Min,
            Max = Max,
        };
        var vertices = new CaveVector3[mesh.Mesh.VertexCount];
        for (var v = 0; v < vertices.Length; v++)
        {
            vertices[v] = new CaveVector3(mesh.Mesh.Positions[v * 3], mesh.Mesh.Positions[(v * 3) + 1], mesh.Mesh.Positions[(v * 3) + 2]);
        }

        return field.OnWalls(new CaveMesh { Vertices = vertices, Triangles = [] }).Reveal!;
    }

    /// <summary>The numbers at the two ends of a leg the cave's lines are drawn of.</summary>
    public (double From, double To) Of(FlybyBundleLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        var from = atStation.TryGetValue(StartOf(leg), out var a) ? a : (double?)null;
        var to = atStation.TryGetValue(EndOf(leg), out var b) ? b : (double?)null;

        // A leg the measuring left out — one of no length — is there with whatever it touches.
        return (from ?? to ?? Min, to ?? from ?? Min);
    }

    private static string StartOf(FlybyBundleLeg leg) => leg.FromStation ?? Spot(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM);

    private static string EndOf(FlybyBundleLeg leg) => leg.ToStation ?? Spot(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM);

    /// <summary>
    /// A place that has no name, as a name: to a hundred-millionth of a degree and a millimetre,
    /// which is finer than a survey is and coarser than the last digit of a sum.
    /// </summary>
    private static string Spot(double longitude, double latitude, double altitudeM) =>
        FormattableString.Invariant($"@{longitude:F8},{latitude:F8},{altitudeM:F3}");
}
