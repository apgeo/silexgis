// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Terrain;

/// <summary>One place along a drawn line the ground is read at.</summary>
/// <param name="DistanceAlongM">Great-circle distance from the start of the line, metres.</param>
public readonly record struct TerrainLineStation(double DistanceAlongM, double Longitude, double Latitude);

/// <summary>What a drawn line's ground profile adds up to.</summary>
/// <param name="ReadCount">How many stations got a height.</param>
/// <param name="LowestM">Lowest height read, or null with none.</param>
/// <param name="HighestM">Highest height read, or null with none.</param>
/// <param name="AscentM">Sum of the rises between neighbouring stations that both got a height.</param>
/// <param name="DescentM">Sum of the falls between neighbouring stations that both got a height.</param>
public sealed record TerrainLineSummary(
    int ReadCount, double? LowestM, double? HighestM, double AscentM, double DescentM);

/// <summary>
/// Where to read the ground along a line somebody drew on the map.
///
/// <para>
/// Knows nothing about caves, features or what lies under the line — that is the property that lets
/// the terrain right alone be enough to ask. The stations are a function of the vertices and of
/// nothing else.
/// </para>
/// </summary>
public static class TerrainLineProfile
{
    /// <summary>Most stations one profile asks the shared elevation reader about.</summary>
    public const int MaxStations = 400;

    /// <summary>Closest two stations are placed, metres; finer than any elevation model prepared here.</summary>
    public const double MinStepM = 5.0;

    /// <summary>Most vertices a drawn line may have.</summary>
    public const int MaxVertices = 200;

    /// <summary>
    /// Evenly spaced stations along the line, the first on its start and the last on its end.
    /// Consecutive duplicate vertices add nothing. Empty with a length of zero for a line with no
    /// extent.
    /// </summary>
    public static (IReadOnlyList<TerrainLineStation> Stations, double LengthM) Stations(
        IReadOnlyList<DemSamplePoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        var lengths = new double[Math.Max(0, vertices.Count - 1)];
        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = Geodesy.DistanceMeters(
                vertices[i].Longitude, vertices[i].Latitude,
                vertices[i + 1].Longitude, vertices[i + 1].Latitude);
        }

        var total = lengths.Sum();
        if (total <= 0)
        {
            return ([], 0);
        }

        var count = Math.Clamp((int)Math.Floor(total / MinStepM) + 1, 2, MaxStations);
        var step = total / (count - 1);

        var stations = new List<TerrainLineStation>(count);
        var piece = 0;
        var consumed = 0d;
        for (var i = 0; i < count; i++)
        {
            var distance = i == count - 1 ? total : i * step;
            while (piece < lengths.Length - 1 && distance >= consumed + lengths[piece])
            {
                consumed += lengths[piece];
                piece++;
            }

            var fraction = lengths[piece] > 0 ? Math.Clamp((distance - consumed) / lengths[piece], 0, 1) : 0;
            var from = vertices[piece];
            var to = vertices[piece + 1];
            stations.Add(new TerrainLineStation(
                distance,
                from.Longitude + ((to.Longitude - from.Longitude) * fraction),
                from.Latitude + ((to.Latitude - from.Latitude) * fraction)));
        }

        return (stations, total);
    }

    /// <summary>
    /// Totals over the heights read. A station with no height breaks the chain: nothing is
    /// interpolated across it, so ascent and descent are only ever sums of differences between two
    /// real readings.
    /// </summary>
    public static TerrainLineSummary Summarise(IReadOnlyList<double?> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);

        var read = heights.Where(h => h is not null).Select(h => h!.Value).ToList();
        double ascent = 0, descent = 0;
        for (var i = 1; i < heights.Count; i++)
        {
            if (heights[i - 1] is { } before && heights[i] is { } after)
            {
                var delta = after - before;
                if (delta > 0)
                {
                    ascent += delta;
                }
                else
                {
                    descent -= delta;
                }
            }
        }

        return new TerrainLineSummary(
            read.Count,
            read.Count > 0 ? read.Min() : null,
            read.Count > 0 ? read.Max() : null,
            ascent,
            descent);
    }
}
