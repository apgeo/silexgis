// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Surveys;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace SilexGis.Infrastructure.Flybys;

/// <summary>
/// In what order a cave appears where a bundle reveals it: a number for every place along its
/// survey, which the renderer compares with a threshold that rises while the clip runs. By
/// distance, the distance along the passages, in true metres, from its entrances; by date, the
/// day each leg was surveyed, as a year with its fraction.
///
/// <para>
/// <b>The rule is the survey library's.</b> The cave's stored legs are handed to it as a graph of
/// named stations and it measures them: from the stations the survey flags as entrances; a part of
/// the cave joined to no entrance from the first station of its first leg. By date it reads each
/// leg's day, and a leg with none takes the earliest there is. It also gives a wall its numbers —
/// every vertex takes the number of the nearest place on the centreline — so what is worked out
/// here is only which leg meets which, and, by date, what a cave falls back on.
/// </para>
///
/// <para>
/// <b>By date, a cave none of whose legs has a day</b> appears whole on the one date its survey
/// was given, where it was given one; with neither it has no numbers and is there from the start.
/// The caves of one scene share one clock of years, so a leg with no day is put at the earliest
/// day of the whole scene (<see cref="From"/>): it is there from the start of the reveal.
/// </para>
/// </summary>
public sealed class FlybyBundleRevealValues
{
    private readonly IReadOnlyList<FlybyBundleLeg> legs;
    private readonly IReadOnlyList<(double From, double To)> values;
    private readonly IReadOnlyList<bool> undated;
    private readonly Dictionary<string, double> atStation = new(StringComparer.Ordinal);
    private readonly Dictionary<FlybyBundleLeg, (double From, double To)>? ofLeg;

    private FlybyBundleRevealValues(
        IReadOnlyList<FlybyBundleLeg> legs,
        IReadOnlyList<(double From, double To)> values,
        IReadOnlyList<bool> undated,
        double min,
        double max,
        bool wholeOnTheSurveyDate,
        bool byLeg)
    {
        this.legs = legs;
        this.values = values;
        this.undated = undated;
        Min = min;
        Max = max;
        WholeOnTheSurveyDate = wholeOnTheSurveyDate;
        if (byLeg)
        {
            // By date a station is not one number: legs surveyed on two days meet at it.
            ofLeg = [];
            for (var i = 0; i < legs.Count; i++)
            {
                ofLeg.TryAdd(legs[i], values[i]);
            }
        }

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

    /// <summary>By date: how many of the legs measured have no day of their own.</summary>
    public int LegsWithoutDate => WholeOnTheSurveyDate ? 0 : undated.Count(u => u);

    /// <summary>By date: the cave has no leg with a day, and appears whole on its survey's date.</summary>
    public bool WholeOnTheSurveyDate { get; }

    /// <summary>
    /// The numbers of a cave, or null for one with nothing to reveal it by: no leg to measure
    /// along, or, by date, no day on any leg and no date for its survey.
    /// </summary>
    public static FlybyBundleRevealValues? Of(FlybyBundleCave cave, FlybyBundleRevealBy by = FlybyBundleRevealBy.Distance)
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
        var graph = new List<RevealGraphLeg>();
        foreach (var leg in cave.SurveyLegs)
        {
            var (start, end) = (StartOf(leg), EndOf(leg));
            var from = Place(start, leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM);
            var to = Place(end, leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM);

            // The library measures along the legs it would draw: one shorter than a millimetre is none.
            if ((to - from).Length < 1e-3)
            {
                continue;
            }

            kept.Add(leg);
            graph.Add(new RevealGraphLeg(start, end, from, to, leg.SurveyedOn));
        }

        if (graph.Count == 0)
        {
            return null;
        }

        var undated = kept.Select(leg => leg.SurveyedOn is null).ToList();
        var source = by == FlybyBundleRevealBy.Date ? RevealSource.Date : RevealSource.Distance;
        var field = RevealField.FromLegs(graph, new RevealOptions { Source = source }, cave.EntranceStations);
        if (field is not null && field.Legs.Count == kept.Count)
        {
            return new FlybyBundleRevealValues(
                kept, [.. field.Legs.Select(leg => (leg.FromValue, leg.ToValue))], undated, field.Min, field.Max, wholeOnTheSurveyDate: false, byLeg: by == FlybyBundleRevealBy.Date);
        }

        // By date with no day on any leg: the whole cave on its survey's one date, where it has one.
        if (by == FlybyBundleRevealBy.Date && field is null && cave.SurveyDate is { } day)
        {
            var year = RevealField.DecimalYear(day);
            return new FlybyBundleRevealValues(
                kept, [.. kept.Select(_ => (year, year))], [.. kept.Select(_ => false)], year, year, wholeOnTheSurveyDate: true, byLeg: true);
        }

        return null;
    }

    /// <summary>
    /// What a reveal by date of <paramref name="caves"/> has to go on: how many legs of caves that
    /// have dated legs have none of their own (there from the start), how many caves appear whole
    /// on their survey's date, and how many have no date at all (there from the start). Caves with
    /// no survey to reveal are not counted: they are there from the start by any reveal.
    /// </summary>
    public static FlybyBundleRevealDates DatesOf(IReadOnlyList<FlybyBundleCave> caves)
    {
        ArgumentNullException.ThrowIfNull(caves);
        int legs = 0, whole = 0, none = 0;
        foreach (var cave in caves.Where(cave => cave.SurveyLegs.Count > 0))
        {
            switch (Of(cave, FlybyBundleRevealBy.Date))
            {
                case null:
                    none++;
                    break;
                case { WholeOnTheSurveyDate: true }:
                    whole++;
                    break;
                case var values:
                    legs += values.LegsWithoutDate;
                    break;
            }
        }

        return new FlybyBundleRevealDates(legs, whole, none);
    }

    /// <summary>
    /// By date, these numbers on the clock of a whole scene whose earliest day is
    /// <paramref name="earliest"/>: a leg with no day of its own is there from the start of the
    /// reveal rather than from the start of its own cave. Unchanged where every leg has a day.
    /// </summary>
    public FlybyBundleRevealValues From(double earliest)
    {
        if (LegsWithoutDate == 0)
        {
            return this;
        }

        var moved = values.Select((value, i) => undated[i] ? (earliest, earliest) : value).ToList();
        return new FlybyBundleRevealValues(
            legs, moved, undated, Math.Min(Min, earliest), Math.Max(Max, earliest), wholeOnTheSurveyDate: false, byLeg: true);
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

    /// <summary>
    /// The numbers at the two ends of a leg the cave's lines are drawn of: by distance, those of
    /// the stations it joins; by date, the leg's own, where it is one of the legs measured.
    /// </summary>
    public (double From, double To) Of(FlybyBundleLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (ofLeg is not null && ofLeg.TryGetValue(leg, out var own))
        {
            return own;
        }

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

/// <summary>What a reveal by date of a scene's caves has to go on; see <see cref="FlybyBundleRevealValues.DatesOf"/>.</summary>
/// <param name="LegsWithoutDate">Legs with no day of their own, of caves some of whose legs have one.</param>
/// <param name="CavesOnTheSurveyDate">Caves with no dated leg that appear whole on their survey's date.</param>
/// <param name="CavesWithoutDate">Caves with no date at all.</param>
public sealed record FlybyBundleRevealDates(int LegsWithoutDate, int CavesOnTheSurveyDate, int CavesWithoutDate)
{
    /// <summary>Nothing to say: every leg of every cave has a day.</summary>
    public static FlybyBundleRevealDates None { get; } = new(0, 0, 0);
}
