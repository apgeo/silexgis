// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// What a picture of contour lines is: how far apart its lines may be, which of them are drawn
/// heavier, and how large the picture is allowed to be.
/// </summary>
/// <remarks>
/// A picture, like the shaded relief beside it, and deliberately not vector data. The lines are
/// drawn into a raster over the footprint of the elevation they were traced from and are served,
/// stored, marked out of date and removed exactly as every other picture of the ground is. Nothing
/// can be measured along one, selected or exported, and no line carries its height: a reader who
/// needs any of that needs a different thing, and this is not a lesser version of it.
/// </remarks>
public static class TerrainContourLines
{
    /// <summary>The spacing a request that names none is given, in metres.</summary>
    /// <remarks>
    /// What a topographic map of mountain country at walking scale uses. Lines of one-arc-second
    /// elevation drawn much closer than this trace the noise of the model rather than the ground.
    /// </remarks>
    public const double DefaultIntervalMetres = 20d;

    /// <summary>The closest lines may be asked to be, in metres.</summary>
    public const double SmallestIntervalMetres = 1d;

    /// <summary>The furthest apart lines may be asked to be, in metres.</summary>
    /// <remarks>
    /// Bounded above because a spacing larger than the relief of the ground produces a finished,
    /// valid picture with no line on it anywhere, which reads as a layer that failed to load.
    /// </remarks>
    public const double LargestIntervalMetres = 1000d;

    /// <summary>Every how many lines one is drawn heavier, counted from sea level.</summary>
    /// <remarks>
    /// The convention of printed maps: at a twenty-metre spacing the heavy lines are the hundreds,
    /// which is what lets a reader count height off a slope without a label on every line.
    /// </remarks>
    public const int IndexEvery = 5;

    /// <summary>How many times finer than the elevation a picture of its contours is drawn.</summary>
    /// <remarks>
    /// A line traced through a grid of heights crosses each cell at a slant, and drawn back onto
    /// that same grid it is a staircase a cell high. Twice as fine halves the step, and is as far
    /// as it is worth going: four times the cells for every further halving, to refine a line
    /// whose position was only ever interpolated between heights a cell apart.
    /// </remarks>
    public const int Fineness = 2;

    /// <summary>The most cells one picture of contours may hold.</summary>
    /// <remarks>
    /// The picture is drawn in memory before it is written, a byte a cell, so this is also what
    /// drawing one costs: sixty-four megabytes whatever the raster it is traced from. It is
    /// enough for a one-degree raster at one arc second drawn twice as fine; a larger raster is
    /// drawn less than twice as fine, and one larger than this already is drawn coarser than it
    /// is, which thins nothing but the lines.
    /// </remarks>
    public const long MostCells = 64_000_000L;

    /// <summary>Why this spacing cannot be drawn, or null when it can.</summary>
    public static string? Problem(double intervalMetres) =>
        double.IsFinite(intervalMetres)
        && intervalMetres >= SmallestIntervalMetres
        && intervalMetres <= LargestIntervalMetres
            ? null
            : $"Contour lines are spaced from {SmallestIntervalMetres} to {LargestIntervalMetres} "
                + "metres of height apart.";

    /// <summary>
    /// How many cells across and down the picture of an elevation raster's contours is.
    /// </summary>
    /// <remarks>
    /// The same proportions as the raster it covers, so that a cell of the picture is the same
    /// shape as a cell of the elevation and the picture sits on exactly the same ground.
    /// </remarks>
    public static (int Width, int Height) PictureSize(int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        var cells = (double)sourceWidth * sourceHeight;
        var finer = Math.Min(Fineness, Math.Sqrt(MostCells / cells));

        // Rounded down, so the limit is a limit; and never to nothing, however thin the raster.
        var across = Math.Max(1L, (long)Math.Floor(sourceWidth * finer));
        var down = Math.Max(1L, (long)Math.Floor(sourceHeight * finer));

        // A raster so thin that one of its sides is a single cell cannot shrink that side any
        // further, so the other side gives up the rest rather than the limit being passed.
        if (across * down > MostCells)
        {
            if (down >= across)
            {
                down = MostCells / across;
            }
            else
            {
                across = MostCells / down;
            }
        }

        return ((int)across, (int)down);
    }
}
