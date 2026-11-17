// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>
/// The cells one run of a cave-potential model is computed over: a north-up lattice in degrees on
/// the world datum, whose cells are close to square on the ground at the area's own latitude.
/// </summary>
/// <remarks>
/// Every evidence layer of a run is produced on this one lattice, cell for cell, so that weighing
/// them is arithmetic between cells of the same index and nothing is resampled a second time.
/// Rows run from north to south and cells are stored row by row, the order a raster is written in.
/// </remarks>
/// <param name="West">The western edge of the first column, in degrees.</param>
/// <param name="North">The northern edge of the first row, in degrees.</param>
/// <param name="CellWidthDegrees">A cell's width in degrees of longitude.</param>
/// <param name="CellHeightDegrees">A cell's height in degrees of latitude.</param>
/// <param name="CellMeters">The cell size asked for, in metres on the ground.</param>
public sealed record EvidenceGrid(
    double West,
    double North,
    double CellWidthDegrees,
    double CellHeightDegrees,
    int Columns,
    int Rows,
    double CellMeters)
{
    /// <summary>
    /// Metres in a degree of latitude, on the same sphere the protection lattice and the density
    /// grid are cut on, so a cell of this grid and a cell of those are the same size on the ground.
    /// </summary>
    public const double MetersPerDegree = 111_320d;

    public double East => West + (Columns * CellWidthDegrees);

    public double South => North - (Rows * CellHeightDegrees);

    public int CellCount => Columns * Rows;

    /// <summary>The latitude metres east–west are measured at: the middle of the grid.</summary>
    public double MiddleLatitude => North - (Rows * CellHeightDegrees / 2d);

    /// <summary>Metres in a degree of longitude at the grid's middle latitude.</summary>
    public double MetersPerDegreeLongitude =>
        MetersPerDegree * Math.Max(Math.Cos(MiddleLatitude * Math.PI / 180d), 1e-6d);

    /// <summary>The longitude of the centre of a column's cells.</summary>
    public double LongitudeOf(int column) => West + ((column + 0.5d) * CellWidthDegrees);

    /// <summary>The latitude of the centre of a row's cells.</summary>
    public double LatitudeOf(int row) => North - ((row + 0.5d) * CellHeightDegrees);

    /// <summary>Where a cell sits in a row-by-row array.</summary>
    public int IndexOf(int column, int row) => (row * Columns) + column;

    /// <summary>
    /// The grid that covers a box at a cell size, or null when <see cref="EvidenceGridRules.Problem"/>
    /// would refuse it.
    /// </summary>
    /// <remarks>
    /// The grid starts at the box's north-west corner and runs whole cells past its south-east one,
    /// so no part of the box is left without a cell.
    /// </remarks>
    public static EvidenceGrid? Over(double west, double south, double east, double north, double cellMeters)
    {
        if (EvidenceGridRules.Problem(west, south, east, north, cellMeters) is not null)
        {
            return null;
        }

        var (columns, rows, width, height) = EvidenceGridRules.Shape(west, south, east, north, cellMeters);
        return new EvidenceGrid(west, north, width, height, (int)columns, (int)rows, cellMeters);
    }
}

/// <summary>What a run's grid may be.</summary>
public static class EvidenceGridRules
{
    /// <summary>
    /// The finest cell a run may ask for. Below it the grid is finer than any elevation the
    /// application prepares, and every terrain layer would be the same value repeated.
    /// </summary>
    public const double MinCellMeters = 5d;

    /// <summary>The coarsest cell a run may ask for: beyond it an area is a handful of cells.</summary>
    public const double MaxCellMeters = 1_000d;

    /// <summary>
    /// The most cells a run may hold. Every evidence layer is one number per cell held in memory
    /// while the run weighs them, so this bounds what a single run can take from the server.
    /// </summary>
    public const long MaxCells = 1_000_000;

    public const string CellSizeCode = "cell_size";
    public const string ExtentCode = "extent";
    public const string TooManyCellsCode = "too_many_cells";

    /// <summary>Why a grid over this box at this cell size is refused, as a stable code, or null.</summary>
    public static string? Problem(double west, double south, double east, double north, double cellMeters)
    {
        if (!double.IsFinite(cellMeters) || cellMeters < MinCellMeters || cellMeters > MaxCellMeters)
        {
            return CellSizeCode;
        }

        if (!double.IsFinite(west) || !double.IsFinite(south) || !double.IsFinite(east) || !double.IsFinite(north)
            || west < -180d || east > 180d || south < -90d || north > 90d
            || !(east > west) || !(north > south))
        {
            return ExtentCode;
        }

        var (columns, rows, _, _) = Shape(west, south, east, north, cellMeters);
        return columns * rows > MaxCells ? TooManyCellsCode : null;
    }

    internal static (long Columns, long Rows, double WidthDegrees, double HeightDegrees) Shape(
        double west, double south, double east, double north, double cellMeters)
    {
        var height = cellMeters / EvidenceGrid.MetersPerDegree;
        var cosine = Math.Max(Math.Cos((south + north) / 2d * Math.PI / 180d), 1e-6d);
        var width = height / cosine;

        // The small allowance keeps a box that is a whole number of cells from gaining one more to
        // a rounding error in the division.
        var columns = Math.Max(1L, (long)Math.Ceiling(((east - west) / width) - 1e-9d));
        var rows = Math.Max(1L, (long)Math.Ceiling(((north - south) / height) - 1e-9d));
        return (columns, rows, width, height);
    }
}
