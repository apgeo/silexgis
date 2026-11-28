// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>The shape a generated cave grows in, chosen from its type, its rock and chance.</summary>
public enum CaveArchetype
{
    /// <summary>Near-level round tubes with loops.</summary>
    PhreaticTubes,

    /// <summary>A descending canyon that meanders after a stream.</summary>
    VadoseCanyon,

    /// <summary>Pitches joined by short meanders: depth before length.</summary>
    ShaftSeries,

    /// <summary>Wide galleries opening into chambers.</summary>
    FossilChambers,

    /// <summary>A grid along the massif's two fracture directions, with many loops.</summary>
    Maze,

    /// <summary>A short rising passage that ends in water.</summary>
    SpringCave,

    /// <summary>A few short, wide shots under an overhang.</summary>
    RockShelter,

    /// <summary>Straight adits with right-angle crosscuts.</summary>
    Mine,
}

/// <summary>A station of a generated survey, in metres about the main entrance (x east, y north, z up), with its passage size.</summary>
public sealed record CaveNetworkStation(
    string Name,
    double X,
    double Y,
    double Z,
    double Left,
    double Right,
    double Up,
    double Down,
    bool Entrance = false,
    bool Continuation = false);

/// <summary>A survey leg between two stations, drawn on one of the network's surveys; a splay ends at the wall.</summary>
public sealed record CaveNetworkLeg(int From, int To, int Survey, bool Splay = false);

/// <summary>One survey of the cave: a trip's worth of legs, with the prefix its stations carry and the day it was drawn.</summary>
public sealed record CaveNetworkSurvey(string Prefix, DateOnly Date);

/// <summary>
/// A generated cave's survey: stations, legs and the surveys that drew them, and the measures
/// a register keeps, worked out from them — so a generated cave's length and depth are those of
/// its own survey, not numbers drawn beside it.
/// </summary>
public sealed record CaveNetwork(
    CaveArchetype Archetype,
    IReadOnlyList<CaveNetworkStation> Stations,
    IReadOnlyList<CaveNetworkLeg> Legs,
    IReadOnlyList<CaveNetworkSurvey> Surveys)
{
    /// <summary>The sum of the legs, splays left out.</summary>
    public double LengthM => Legs.Where(l => !l.Splay).Sum(Length);

    public double TopZ => Stations.Max(s => s.Z);

    public double BottomZ => Stations.Min(s => s.Z);

    /// <summary>From the highest station to the lowest.</summary>
    public double DepthM => TopZ - BottomZ;

    /// <summary>How far above the main entrance the cave rises.</summary>
    public double PositiveDepthM => Math.Max(0, TopZ);

    /// <summary>How far below the main entrance the cave goes.</summary>
    public double NegativeDepthM => Math.Max(0, -BottomZ);

    /// <summary>The longest straight distance, in plan, between two stations.</summary>
    public double ExtensionM
    {
        get
        {
            // Over the convex outline's corners would do; the stations are few enough for the plain way.
            var points = Stations.Count > 400 ? [.. Stations.Where((_, i) => i % (Stations.Count / 400 + 1) == 0)] : Stations;
            var best = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                for (var j = i + 1; j < points.Count; j++)
                {
                    best = Math.Max(best, Math.Sqrt(Square(points[i].X - points[j].X) + Square(points[i].Y - points[j].Y)));
                }
            }

            return best;
        }
    }

    /// <summary>The plan area of the passages: each leg's plan length times its width.</summary>
    public double PlanAreaM2 => Legs.Where(l => !l.Splay).Sum(l =>
        PlanLength(l) * ((Stations[l.From].Left + Stations[l.From].Right + Stations[l.To].Left + Stations[l.To].Right) / 2));

    /// <summary>The volume of the passages, as elliptic tubes of the measured size.</summary>
    public double VolumeM3 => Legs.Where(l => !l.Splay).Sum(l =>
    {
        var a = Stations[l.From];
        var b = Stations[l.To];
        var width = (a.Left + a.Right + b.Left + b.Right) / 2;
        var height = (a.Up + a.Down + b.Up + b.Down) / 2;
        return Length(l) * Math.PI / 4 * width * height;
    });

    /// <summary>Stations with more than two legs, per hundred metres of passage.</summary>
    public double JunctionsPerHundredM
    {
        get
        {
            var degree = new int[Stations.Count];
            foreach (var leg in Legs.Where(l => !l.Splay))
            {
                degree[leg.From]++;
                degree[leg.To]++;
            }

            return LengthM <= 0 ? 0 : degree.Count(d => d > 2) * 100 / LengthM;
        }
    }

    public double Length(CaveNetworkLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        var a = Stations[leg.From];
        var b = Stations[leg.To];
        return Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Z - b.Z));
    }

    private double PlanLength(CaveNetworkLeg leg)
    {
        var a = Stations[leg.From];
        var b = Stations[leg.To];
        return Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y));
    }

    private static double Square(double v) => v * v;
}
