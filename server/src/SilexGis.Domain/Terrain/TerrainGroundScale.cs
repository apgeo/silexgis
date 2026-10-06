// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>A run of whole rows of a raster, computed together.</summary>
/// <param name="FirstRow">The first row of the run, counted from the northern edge.</param>
/// <param name="RowCount">How many rows it holds.</param>
public readonly record struct TerrainRowBand(int FirstRow, int RowCount);

/// <summary>
/// How large the cells of a raster held in degrees really are on the ground, and what that does to
/// a picture computed from one.
/// </summary>
/// <remarks>
/// <para>
/// A prepared elevation raster is a grid of degrees. Its cells are as tall as they are wide in
/// degrees and are not on the ground: a degree of longitude shrinks with the cosine of the
/// latitude, so at 46° north a cell thirty-one metres tall is under twenty-two wide. Anything
/// that divides a height by a distance across the ground, or works out a direction from the
/// differences between neighbouring cells, has to be told so — otherwise an east-facing hillside
/// that stands at 55° is reported at 45°, and a slope facing north-east is reported as facing
/// nearer north than it does.
/// </para>
/// <para>
/// The answer here is the two distances themselves, taken from the ellipsoid the rasters are
/// placed on, rather than a reprojection of the elevation into metres. A reprojection resamples
/// the heights twice — into the metric grid and back — which softens exactly the relief these
/// pictures exist to show, and a metric grid has a north of its own that turns away from true
/// north towards its edges, so a facing read off it is wrong by that turn. On the grid of
/// degrees the columns are meridians and the rows are parallels everywhere, and the only thing
/// that changes from one row to the next is how wide a cell is.
/// </para>
/// </remarks>
public static class TerrainGroundScale
{
    // WGS 84, the ellipsoid every prepared raster is placed on.
    private const double SemiMajorAxisMetres = 6_378_137d;
    private const double Flattening = 1d / 298.257223563d;
    private const double EccentricitySquared = Flattening * (2d - Flattening);

    /// <summary>The latitude past which a cell is treated as no narrower.</summary>
    /// <remarks>
    /// At the pole a degree of longitude has no width at all and every division by it is a
    /// division by zero, which arrives downstream as a raster of infinities rather than as an
    /// error. Held just short of it, a raster that touches the pole is drawn slightly wrong along
    /// its last rows instead of not at all.
    /// </remarks>
    private const double SteepestLatitude = 89.9d;

    /// <summary>The tallest run of rows computed with one east–west scale, in degrees.</summary>
    /// <remarks>
    /// The library computing a picture takes one scale for a whole raster, and the scale that is
    /// right for the middle row is wrong for every other: over a raster one degree tall at 46°
    /// north it is out by nearly one per cent at the top and bottom rows, a quarter of a degree of
    /// steepness; over five degrees it is five per cent and a degree and a half. A tenth of a
    /// degree keeps the scale within a quarter of a per cent of true — under a tenth of a degree
    /// of steepness — at any latitude up to 70°, and still under a third of a degree at 85°.
    /// </remarks>
    public const double TallestBandDegrees = 0.1d;

    /// <summary>The most cells one run of rows is allowed to hold.</summary>
    /// <remarks>
    /// Each run is held in memory while it is computed, so this bounds what a picture costs in
    /// memory whatever the size of the raster it is drawn from: thirty-two megabytes of heights
    /// and as much again for the result.
    /// </remarks>
    public const long MostCellsPerBand = 8_000_000L;

    /// <summary>How many metres one degree of latitude spans at this latitude.</summary>
    public static double MetresPerDegreeOfLatitude(double latitude)
    {
        var sine = Math.Sin(Radians(Held(latitude)));
        var meridional = SemiMajorAxisMetres * (1d - EccentricitySquared)
            / Math.Pow(1d - (EccentricitySquared * sine * sine), 1.5d);
        return meridional * Math.PI / 180d;
    }

    /// <summary>How many metres one degree of longitude spans at this latitude.</summary>
    public static double MetresPerDegreeOfLongitude(double latitude)
    {
        var held = Radians(Held(latitude));
        var sine = Math.Sin(held);
        var primeVertical = SemiMajorAxisMetres / Math.Sqrt(1d - (EccentricitySquared * sine * sine));
        return primeVertical * Math.Cos(held) * Math.PI / 180d;
    }

    /// <summary>
    /// How many times taller than wide a cell is on the ground, at this latitude.
    /// </summary>
    /// <param name="latitude">The latitude of the row the cell is in.</param>
    /// <param name="cellWidthDegrees">The cell's width, in degrees of longitude.</param>
    /// <param name="cellHeightDegrees">The cell's height, in degrees of latitude.</param>
    public static double CellStretch(double latitude, double cellWidthDegrees, double cellHeightDegrees) =>
        (cellHeightDegrees * MetresPerDegreeOfLatitude(latitude))
        / (cellWidthDegrees * MetresPerDegreeOfLongitude(latitude));

    /// <summary>
    /// The direction ground really faces, given the direction worked out as though its cells
    /// were square.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A facing is the direction of the downhill step, and it is worked out from how much the
    /// height changes from one column to the next and from one row to the next. Read as a
    /// direction straight off those two numbers, the answer is right only when a column is as
    /// far from its neighbour as a row is. Where a cell is <paramref name="cellStretch"/> times
    /// taller than wide, the same change across a column happens over a shorter distance and is
    /// that many times steeper than it was counted, so the east–west part of the direction is
    /// multiplied by the stretch and the direction turned to match.
    /// </para>
    /// <para>
    /// Exact, and needing nothing but the latitude of the row: the correction is a property of
    /// the cell's shape and not of the ground. North, south, east and west are left exactly
    /// where they were, which is why the error this removes was invisible on any slope facing
    /// one of them.
    /// </para>
    /// </remarks>
    /// <param name="facingOnSquareCells">Degrees clockwise from north, as computed.</param>
    /// <param name="cellStretch">See <see cref="CellStretch"/>.</param>
    /// <returns>Degrees clockwise from north, from 0 up to but not including 360.</returns>
    public static double FacingOnTheGround(double facingOnSquareCells, double cellStretch)
    {
        var counted = Radians(facingOnSquareCells);
        var facing = Math.Atan2(cellStretch * Math.Sin(counted), Math.Cos(counted)) * 180d / Math.PI;

        if (facing < 0d)
        {
            facing += 360d;
        }

        return facing >= 360d ? 0d : facing;
    }

    /// <summary>
    /// The runs of rows a raster is computed in, north to south, covering every row once.
    /// </summary>
    /// <param name="width">The raster's width in cells.</param>
    /// <param name="height">The raster's height in cells.</param>
    /// <param name="cellHeightDegrees">The height of one cell, in degrees of latitude.</param>
    public static IReadOnlyList<TerrainRowBand> RowBands(int width, int height, double cellHeightDegrees)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (!double.IsFinite(cellHeightDegrees) || cellHeightDegrees <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(cellHeightDegrees));
        }

        // Never fewer than one row: a raster whose cells are themselves taller than the limit is
        // computed a row at a time, each with exactly its own scale.
        var byLatitude = Math.Floor(TallestBandDegrees / cellHeightDegrees);
        var byMemory = MostCellsPerBand / width;
        var rows = (int)Math.Clamp(Math.Min(byLatitude, byMemory), 1d, height);

        var bands = new List<TerrainRowBand>((height + rows - 1) / rows);
        for (var first = 0; first < height; first += rows)
        {
            bands.Add(new TerrainRowBand(first, Math.Min(rows, height - first)));
        }

        return bands;
    }

    private static double Held(double latitude) =>
        Math.Clamp(latitude, -SteepestLatitude, SteepestLatitude);

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}
