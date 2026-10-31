// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>The terrain measures a feature can be set against its surroundings by.</summary>
public enum TerrainMeasure
{
    /// <summary>Steepness of the ground, degrees.</summary>
    Slope = 0,

    /// <summary>The compass direction the ground faces, eight sectors.</summary>
    Aspect = 1,

    /// <summary>Height relative to the neighbouring cells: hollow, level or rise.</summary>
    Position = 2,
}

/// <summary>
/// How readings of a computed picture of the ground fall into classes, and how a set of features
/// compares with the background it sits in.
///
/// <para>
/// The class edges are constants in the same spirit as the other morphometric thresholds: they are
/// what a reader needs to compare two numbers, not a claim about caves. Slope uses the usual
/// geomorphological bands; position uses a one-metre band around level, which is below the noise of
/// the elevation models prepared here and so reads as "level" for anything the model cannot resolve.
/// </para>
/// </summary>
public static class TerrainClassStatistics
{
    /// <summary>Upper edges of the slope classes, degrees; the last class is open.</summary>
    public static readonly IReadOnlyList<double> SlopeEdgesDegrees = [5, 15, 30, 45];

    /// <summary>Edges of the position classes, metres; below the first is hollow, above the second a rise.</summary>
    public static readonly IReadOnlyList<double> PositionEdgesM = [-1, 1];

    /// <summary>
    /// Ground flatter than this faces nowhere worth naming. The aspect picture writes a bearing of
    /// zero for flat ground, so without this every plateau would count as north-facing.
    /// </summary>
    public const double FlatSlopeDegrees = 2.0;

    /// <summary>The class codes of a measure, in order.</summary>
    public static IReadOnlyList<string> Codes(TerrainMeasure measure) => measure switch
    {
        TerrainMeasure.Slope => ["lt5", "5to15", "15to30", "30to45", "gt45"],
        TerrainMeasure.Aspect => ["n", "ne", "e", "se", "s", "sw", "w", "nw"],
        TerrainMeasure.Position => ["hollow", "level", "rise"],
        _ => throw new ArgumentOutOfRangeException(nameof(measure)),
    };

    /// <summary>Which class a reading falls in, as an index into <see cref="Codes"/>.</summary>
    public static int ClassOf(TerrainMeasure measure, double value)
    {
        switch (measure)
        {
            case TerrainMeasure.Aspect:
                var bearing = ((value % 360) + 360) % 360;
                return (int)Math.Floor(((bearing + 22.5) % 360) / 45);
            case TerrainMeasure.Slope:
                return Edge(SlopeEdgesDegrees, value);
            case TerrainMeasure.Position:
                // Both edges belong to the level class.
                return value < PositionEdgesM[0] ? 0 : value > PositionEdgesM[1] ? 2 : 1;
            default:
                throw new ArgumentOutOfRangeException(nameof(measure));
        }
    }

    private static int Edge(IReadOnlyList<double> edges, double value)
    {
        var index = 0;
        while (index < edges.Count && value >= edges[index])
        {
            index++;
        }

        return index;
    }

    /// <summary>Counts of readings per class.</summary>
    public static int[] Histogram(TerrainMeasure measure, IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var counts = new int[Codes(measure).Count];
        foreach (var value in values.Where(double.IsFinite))
        {
            counts[ClassOf(measure, value)]++;
        }

        return counts;
    }

    /// <summary>
    /// A class's share among the features divided by its share of the background: above one the
    /// features sit there more than the ground does, below one less. Null where the ratio would say
    /// nothing — no features read, or the class is absent from the background.
    /// </summary>
    public static double? Ratio(int featureCount, int featureTotal, int backgroundCount, int backgroundTotal)
    {
        if (featureTotal <= 0 || backgroundTotal <= 0 || backgroundCount <= 0)
        {
            return null;
        }

        return ((double)featureCount / featureTotal) / ((double)backgroundCount / backgroundTotal);
    }

    /// <summary>
    /// Evenly spread points inside a rectangle, row by row, <paramref name="perSide"/> along each
    /// axis, each at the middle of its cell. The caller keeps those that fall inside its outline.
    /// </summary>
    public static IReadOnlyList<DemSamplePoint> GridInside(
        double west, double south, double east, double north, int perSide)
    {
        if (perSide < 1 || !(east > west) || !(north > south))
        {
            return [];
        }

        var points = new List<DemSamplePoint>(perSide * perSide);
        for (var row = 0; row < perSide; row++)
        {
            for (var column = 0; column < perSide; column++)
            {
                points.Add(new DemSamplePoint(
                    west + ((column + 0.5) / perSide * (east - west)),
                    south + ((row + 0.5) / perSide * (north - south))));
            }
        }

        return points;
    }
}
