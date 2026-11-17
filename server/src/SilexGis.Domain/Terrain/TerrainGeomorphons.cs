// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// The landform picture (Jasiewicz and Stepinski's geomorphons): its ten classes, the settings it
/// is drawn with, and what those settings may be.
/// </summary>
/// <remarks>
/// <para>
/// Each cell is classed by looking out along the eight compass lines for up to the search distance
/// and noting, along each, whether the ground rises above the flatness angle, falls below it, or
/// stays level; the pattern of eight answers names the landform. It describes the shape of the
/// ground at the scale of the search distance, which is why that distance is a setting rather than
/// a constant: the same hillside is a slope at a hundred metres and a shoulder at a kilometre.
/// </para>
/// <para>
/// Distances are in metres over the ground rather than in cells. The rasters of one build are
/// separate files at their own pixel sizes, so a distance in cells would mean a different landform
/// scale on each of them; the computation turns metres into each raster's own cells.
/// </para>
/// </remarks>
public static class TerrainGeomorphons
{
    /// <summary>
    /// The class names by class number less one: the numbers WhiteboxTools and GRASS both write,
    /// 1 flat through 10 pit, in this order. The order is the stored meaning of every computed
    /// landform raster, so it is never changed.
    /// </summary>
    public static readonly IReadOnlyList<string> Classes =
    [
        "flat", "peak", "ridge", "shoulder", "spur", "slope", "hollow", "footslope", "valley", "pit",
    ];

    /// <summary>The lowest class number.</summary>
    public const int FirstClass = 1;

    /// <summary>The highest class number.</summary>
    public const int LastClass = 10;

    /// <summary>The usual search distance, in metres.</summary>
    /// <remarks>
    /// Half a kilometre: wide enough to see a doline or a dry valley whole at the thirty-metre pixel
    /// of the global elevation models, and narrow enough that a hillside carrying them is not read as
    /// one slope. The program's own default is fifty cells, which means a different distance at every
    /// pixel size.
    /// </remarks>
    public const double DefaultSearchMetres = 500d;

    /// <summary>The usual flatness angle, in degrees — the reference implementation's own default.</summary>
    public const double DefaultFlatnessDegrees = 1d;

    /// <summary>The longest search distance that may be asked for, in metres.</summary>
    /// <remarks>
    /// Every cell looks out this far in eight directions, so the work grows with it; ten kilometres
    /// is already a landscape-scale reading and far past what a cave survey area needs.
    /// </remarks>
    public const double MaxSearchMetres = 10_000d;

    /// <summary>The steepest flatness angle that may be asked for, in degrees.</summary>
    public const double MaxFlatnessDegrees = 45d;

    /// <summary>Why these settings cannot be computed, or null when they can.</summary>
    public static string? Problem(double searchMetres, double skipMetres, double flatnessDegrees)
    {
        if (!double.IsFinite(searchMetres) || searchMetres <= 0d || searchMetres > MaxSearchMetres)
        {
            return $"A landform search distance is above zero and at most {MaxSearchMetres:0} metres.";
        }

        if (!double.IsFinite(skipMetres) || skipMetres < 0d || skipMetres >= searchMetres)
        {
            return "The distance looked past is zero or more, and shorter than the search distance.";
        }

        if (!double.IsFinite(flatnessDegrees) || flatnessDegrees < 0d || flatnessDegrees > MaxFlatnessDegrees)
        {
            return $"The flatness angle is between 0 and {MaxFlatnessDegrees:0} degrees.";
        }

        return null;
    }

    /// <summary>The class code of a stored value, or null for anything that is not a class.</summary>
    public static string? ClassOf(double value)
    {
        if (!double.IsFinite(value) || value != Math.Floor(value) || value < FirstClass || value > LastClass)
        {
            return null;
        }

        return Classes[(int)value - FirstClass];
    }

    /// <summary>How many cells a distance in metres is at a pixel size in metres, at least one when <paramref name="atLeastOne"/>.</summary>
    public static int Cells(double metres, double pixelMetres, bool atLeastOne)
    {
        if (!double.IsFinite(metres) || !double.IsFinite(pixelMetres) || pixelMetres <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelMetres), "A pixel has a size above zero.");
        }

        var cells = (int)Math.Round(metres / pixelMetres, MidpointRounding.AwayFromZero);
        return atLeastOne ? Math.Max(1, cells) : Math.Max(0, cells);
    }
}
