// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.TestData;

/// <summary>What a generated cave's survey is grown to: its shape, its size, its grain and its ground.</summary>
/// <param name="TargetLengthM">The survey's length to grow towards; the grown one is within a shot of it.</param>
/// <param name="TargetDepthM">For a shaft series, the depth to reach; otherwise a ceiling on how deep it may go.</param>
/// <param name="FractureAzimuthsDeg">The massif's fracture directions, which passages follow.</param>
/// <param name="OtherEntrances">Where the cave's other entrances are, in metres about the main one.</param>
/// <param name="SurfaceAbove">The ground's height above the main entrance at a point (metres east, north), for keeping the passages under it.</param>
/// <param name="FirstSurvey">The day the first survey was drawn; later ones follow it.</param>
public sealed record CaveNetworkRequest(
    CaveArchetype Archetype,
    double TargetLengthM,
    double TargetDepthM,
    IReadOnlyList<double> FractureAzimuthsDeg,
    IReadOnlyList<(double X, double Y, double Z)> OtherEntrances,
    Func<double, double, double> SurfaceAbove,
    DateOnly FirstSurvey,
    DateOnly LastDay);

/// <summary>
/// Grows a cave's survey from its main entrance: a segment grammar that takes each shot's
/// direction from the massif's fractures with noise, its slope and size from the cave's
/// archetype, branches, closes loops where passages meet, and keeps every station under the
/// ground but the entrances. Pure and deterministic — the same request and stream give the same
/// survey.
/// </summary>
public static class CaveNetworkGenerator
{
    /// <summary>The thickness of rock kept above every passage but the entrances.</summary>
    public const double CoverM = 6;

    private const int MaxStations = 6000;

    private static readonly IReadOnlyDictionary<string, int> SolutionCaveWeights = new Dictionary<string, int>
    {
        [nameof(CaveArchetype.PhreaticTubes)] = 30,
        [nameof(CaveArchetype.VadoseCanyon)] = 35,
        [nameof(CaveArchetype.FossilChambers)] = 25,
        [nameof(CaveArchetype.Maze)] = 10,
    };

    public static CaveArchetype ArchetypeFor(string caveType, string rock, TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return caveType switch
        {
            "pit" => CaveArchetype.ShaftSeries,
            "spring_cave" => CaveArchetype.SpringCave,
            "rock_shelter" => CaveArchetype.RockShelter,
            "mine" => CaveArchetype.Mine,
            _ when rock is "gypsum" or "salt" => CaveArchetype.Maze,
            _ => Enum.Parse<CaveArchetype>(rng.Weighted(SolutionCaveWeights)),
        };
    }

    private sealed class Growth(CaveNetworkRequest request, TestDataRng rng)
    {
        public List<CaveNetworkStation> Stations { get; } = [];

        public List<CaveNetworkLeg> Legs { get; } = [];

        public int Survey { get; set; }

        public double Length { get; set; }

        public CaveNetworkRequest Request { get; } = request;

        public TestDataRng Rng { get; } = rng;

        public int Add(double x, double y, double z, (double L, double R, double U, double D) size, bool entrance = false)
        {
            if (!entrance)
            {
                // Under the ground, with rock above it: a passage that would come out of the hill is
                // pushed down instead.
                var ceiling = Request.SurfaceAbove(x, y) - CoverM - size.U;
                z = Math.Min(z, ceiling);
            }

            Stations.Add(new CaveNetworkStation(string.Empty, x, y, z, size.L, size.R, size.U, size.D, entrance));
            return Stations.Count - 1;
        }

        public void Join(int from, int to)
        {
            Legs.Add(new CaveNetworkLeg(from, to, Survey));
            var a = Stations[from];
            var b = Stations[to];
            Length += Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));
        }
    }

    private readonly record struct Tip(int Station, double AzimuthDeg, int Age);

    public static CaveNetwork Grow(CaveNetworkRequest request, TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rng);
        var g = new Growth(request, rng);
        var shape = Shape.Of(request.Archetype);
        var fractures = request.FractureAzimuthsDeg.Count > 0 ? request.FractureAzimuthsDeg : [0.0, 90.0];

        var start = g.Add(0, 0, 0, shape.Size(rng), entrance: true);
        var tips = new List<Tip> { new(start, rng.Pick(fractures) + rng.Between(-25.0, 25.0), 0) };
        var pitchLeft = 0.0;
        var depthLeft = request.TargetDepthM;

        while (g.Length < request.TargetLengthM && g.Stations.Count < MaxStations && tips.Count > 0)
        {
            // Mostly the newest tip, as a survey pushes on at the front; sometimes an older one,
            // as a side passage is surveyed on a later trip.
            var at = rng.Chance(shape.SideBranchShare) ? rng.Next(tips.Count) : tips.Count - 1;
            var tip = tips[at];
            var from = g.Stations[tip.Station];

            var azimuth = Grain(tip.AzimuthDeg, fractures, shape, rng);
            var shot = rng.Between(shape.ShotMinM, shape.ShotMaxM);
            double inclination;
            if (request.Archetype == CaveArchetype.ShaftSeries && depthLeft > 2 && (pitchLeft > 0 || rng.Chance(0.45)))
            {
                // A pitch: a run of near-vertical shots until it lands.
                if (pitchLeft <= 0)
                {
                    pitchLeft = Math.Min(depthLeft, rng.LogNormal(18, 0.8, 4, 120));
                }

                inclination = -rng.Between(80.0, 90.0);
                shot = Math.Min(rng.Between(5.0, 25.0), pitchLeft + 0.5);
                pitchLeft -= shot;
                depthLeft -= shot;
            }
            else
            {
                inclination = shape.Inclination(rng, -from.Z, request.TargetDepthM);
            }

            var rad = azimuth * Math.PI / 180;
            var dip = inclination * Math.PI / 180;
            var x = from.X + (shot * Math.Cos(dip) * Math.Sin(rad));
            var y = from.Y + (shot * Math.Cos(dip) * Math.Cos(rad));
            var z = from.Z + (shot * Math.Sin(dip));
            var station = g.Add(x, y, z, shape.Size(rng));
            g.Join(tip.Station, station);
            tips[at] = new Tip(station, azimuth, tip.Age + 1);

            // A passage meets another: the survey closes a loop onto a station already drawn nearby.
            if (rng.Chance(shape.LoopShare) && Nearest(g.Stations, station, 4, 14) is { } meets)
            {
                g.Join(station, meets);
            }

            if (rng.Chance(shape.BranchShare))
            {
                var turn = rng.Chance(0.5) ? 1 : -1;
                tips.Add(new Tip(station, azimuth + (turn * rng.Between(55.0, 100.0)), 0));
            }

            // A passage too tight to follow, a sump, a choke: the tip is left as a lead.
            if (tips.Count > 1 && rng.Chance(shape.EndShare))
            {
                tips.RemoveAt(at);
            }

            if (g.Legs.Count % shape.LegsPerSurvey == 0)
            {
                g.Survey++;
            }
        }

        // Every other way in joins the cave by a short passage to the station nearest it.
        foreach (var (ex, ey, ez) in request.OtherEntrances)
        {
            var entrance = g.Add(ex, ey, ez, shape.Size(rng), entrance: true);
            var target = NearestAny(g.Stations, entrance);
            var steps = Math.Max(1, (int)Math.Ceiling(Distance(g.Stations[entrance], g.Stations[target]) / 10));
            var previous = entrance;
            for (var s = 1; s < steps; s++)
            {
                var t = (double)s / steps;
                var a = g.Stations[entrance];
                var b = g.Stations[target];
                var mid = g.Add(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t), shape.Size(rng));
                g.Join(previous, mid);
                previous = mid;
            }

            g.Join(previous, target);
        }

        return Finish(g, tips.Select(t => t.Station).ToHashSet(), request, rng);
    }

    /// <summary>Names the stations by the survey that reached them, marks the leads, and dates the surveys.</summary>
    private static CaveNetwork Finish(Growth g, HashSet<int> leads, CaveNetworkRequest request, TestDataRng rng)
    {
        var surveyCount = g.Survey + 1;
        var surveys = new List<CaveNetworkSurvey>(surveyCount);
        var span = Math.Max(0, request.LastDay.DayNumber - request.FirstSurvey.DayNumber);
        for (var s = 0; s < surveyCount; s++)
        {
            // Spread over the years between the first survey and today, earlier ones closer together.
            var share = surveyCount == 1 ? 0 : Math.Pow((double)s / (surveyCount - 1), 1.4);
            var day = DateOnly.FromDayNumber(request.FirstSurvey.DayNumber + (int)(share * span));
            surveys.Add(new CaveNetworkSurvey(Prefix(s), day));
        }

        // A station belongs to the survey of the first leg that reached it; the main entrance to the first.
        var owner = new int[g.Stations.Count];
        Array.Fill(owner, -1);
        owner[0] = 0;
        foreach (var leg in g.Legs)
        {
            if (owner[leg.To] < 0)
            {
                owner[leg.To] = leg.Survey;
            }

            if (owner[leg.From] < 0)
            {
                owner[leg.From] = leg.Survey;
            }
        }

        var counters = new int[surveyCount];
        var stations = g.Stations.Select((s, i) =>
        {
            var survey = Math.Max(0, owner[i]);
            var name = surveys[survey].Prefix + (counters[survey]++).ToString(CultureInfo.InvariantCulture);
            return s with { Name = name, Continuation = leads.Contains(i) && i != 0 && !s.Entrance };
        }).ToList();

        // A wall shot at a few stations, as a surveyor takes them where the passage is worth drawing.
        var legs = g.Legs.ToList();
        var wallShots = new List<CaveNetworkStation>();
        var named = stations.Count;
        for (var i = 1; i < named; i += Math.Max(3, named / 40))
        {
            var s = stations[i];
            var angle = rng.Between(0, 2 * Math.PI);
            wallShots.Add(new CaveNetworkStation(
                string.Empty, s.X + (s.Left * Math.Cos(angle)), s.Y + (s.Left * Math.Sin(angle)), s.Z, 0, 0, 0, 0));
            legs.Add(new CaveNetworkLeg(i, stations.Count + wallShots.Count - 1, legs.First(l => l.To == i || l.From == i).Survey, Splay: true));
        }

        stations.AddRange(wallShots);
        return new CaveNetwork(request.Archetype, stations, legs, surveys);
    }

    private static string Prefix(int survey) =>
        survey < 26 ? ((char)('A' + survey)).ToString() : "S" + survey.ToString(CultureInfo.InvariantCulture) + "_";

    /// <summary>The next shot's direction: the last one with noise, pulled towards the nearest fracture direction.</summary>
    private static double Grain(double azimuth, IReadOnlyList<double> fractures, Shape shape, TestDataRng rng)
    {
        if (shape.RightAngles)
        {
            // A mine or a maze keeps to its grid; it turns only by a quarter.
            var grid = fractures[0];
            var snapped = grid + (90 * Math.Round((azimuth - grid) / 90));
            return snapped + rng.Between(-3.0, 3.0);
        }

        var next = azimuth + (rng.Normal() * shape.WanderDeg);
        var nearest = fractures.Select(f => f + (180 * Math.Round((next - f) / 180))).MinBy(f => Math.Abs(f - next));
        return next + ((nearest - next) * shape.GrainPull);
    }

    private static int? Nearest(List<CaveNetworkStation> stations, int from, double min, double max)
    {
        var a = stations[from];
        int? best = null;
        var bestDistance = max;
        // Only stations drawn a while ago: the previous few are the passage being followed.
        for (var i = 0; i < stations.Count - 6; i++)
        {
            var d = Distance(a, stations[i]);
            if (d >= min && d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    private static int NearestAny(List<CaveNetworkStation> stations, int from)
    {
        var a = stations[from];
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < stations.Count; i++)
        {
            if (i == from || stations[i].Entrance)
            {
                continue;
            }

            var d = Distance(a, stations[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    private static double Distance(CaveNetworkStation a, CaveNetworkStation b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>How each archetype grows.</summary>
    private sealed record Shape(
        double ShotMinM,
        double ShotMaxM,
        double WanderDeg,
        double GrainPull,
        double BranchShare,
        double LoopShare,
        double EndShare,
        double SideBranchShare,
        int LegsPerSurvey,
        bool RightAngles,
        (double Min, double Max) Width,
        (double Min, double Max) Height,
        Func<TestDataRng, double, double, double> Inclination)
    {
        public (double L, double R, double U, double D) Size(TestDataRng rng)
        {
            var width = rng.Between(Width.Min, Width.Max);
            var height = rng.Between(Height.Min, Height.Max);
            var left = width * rng.Between(0.3, 0.7);
            var down = height * rng.Between(0.1, 0.4);
            return (Math.Round(left, 1), Math.Round(width - left, 1), Math.Round(height - down, 1), Math.Round(down, 1));
        }

        public static Shape Of(CaveArchetype archetype) => archetype switch
        {
            CaveArchetype.PhreaticTubes => new(3, 12, 18, 0.35, 0.08, 0.05, 0.02, 0.15, 60, false, (1, 4), (1, 3.5),
                (rng, _, _) => rng.Between(-6.0, 6.0)),
            CaveArchetype.VadoseCanyon => new(3, 12, 25, 0.3, 0.06, 0.02, 0.02, 0.12, 50, false, (0.6, 2.5), (3, 12),
                (rng, depth, target) => depth < target ? rng.Between(-18.0, 2.0) : rng.Between(-3.0, 3.0)),
            CaveArchetype.ShaftSeries => new(2, 8, 30, 0.2, 0.05, 0.01, 0.03, 0.1, 30, false, (1, 4), (2, 6),
                (rng, _, _) => rng.Between(-15.0, 5.0)),
            CaveArchetype.FossilChambers => new(5, 15, 20, 0.25, 0.1, 0.04, 0.02, 0.2, 60, false, (4, 30), (3, 18),
                (rng, _, _) => rng.Between(-10.0, 8.0)),
            CaveArchetype.Maze => new(3, 9, 4, 0.8, 0.25, 0.25, 0.03, 0.35, 80, true, (0.8, 2.5), (1.5, 5),
                (rng, _, _) => rng.Between(-3.0, 3.0)),
            CaveArchetype.SpringCave => new(3, 10, 22, 0.3, 0.03, 0.01, 0.02, 0.1, 50, false, (1, 4), (1.5, 4),
                (rng, _, _) => rng.Between(-4.0, 10.0)),
            CaveArchetype.RockShelter => new(1, 4, 40, 0.1, 0.1, 0.0, 0.05, 0.3, 100, false, (3, 12), (1.5, 5),
                (rng, _, _) => rng.Between(-5.0, 5.0)),
            CaveArchetype.Mine => new(5, 20, 0, 1, 0.12, 0.04, 0.03, 0.3, 40, true, (1.5, 3), (1.8, 2.4),
                (rng, _, _) => rng.Between(-1.0, 1.0)),
            _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
        };
    }
}
