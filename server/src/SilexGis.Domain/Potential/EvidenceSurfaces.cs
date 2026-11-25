// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;

namespace SilexGis.Domain.Potential;

/// <summary>What was read to make one evidence layer of a run.</summary>
/// <param name="FeatureIds">
/// Every recorded feature whose position went into the layer, protected or not, each once and in
/// id order. All of them rather than only the protected ones, because protection can be switched
/// on after a run: whoever is later shown the layer has to be someone who may place every one of
/// these as things stand then, and that can only be asked if the whole list was kept.
/// </param>
/// <param name="ProtectedFeatures">
/// How many of them were location-protected when the layer was made — positions the runner was
/// allowed to place exactly and most readers are not.
/// </param>
public sealed record EvidenceProvenance(IReadOnlyList<Guid> FeatureIds, int ProtectedFeatures)
{
    /// <summary>The provenance of a layer made from the elevation model alone.</summary>
    public static EvidenceProvenance None { get; } = new([], 0);

    /// <summary>Whether a protected position shaped the layer.</summary>
    public bool UsedProtectedPositions => ProtectedFeatures > 0;
}

/// <summary>The terrain build, and the computed picture of it, that one terrain layer was read from.</summary>
/// <param name="BuildId">The elevation build that was active when the run read the ground.</param>
/// <param name="Layer">The evidence layer's code.</param>
/// <param name="DerivativeLayerId">The computed picture read, or null for elevation itself.</param>
/// <param name="Settings">That picture's settings as stored (JSON text), or null for elevation itself.</param>
public sealed record TerrainInput(Guid BuildId, string Layer, Guid? DerivativeLayerId, string? Settings);

/// <summary>One evidence layer on a run's grid, or why it could not be made.</summary>
/// <param name="Layer">Which evidence this is.</param>
/// <param name="Values">
/// One value per cell, row by row from the north-west, or null when the layer is unavailable. A
/// cell that holds no reading is not-a-number. A categorical layer holds the position of the
/// cell's class in the layer's own list of classes.
/// </param>
/// <param name="UnavailableReason">A stable code from <see cref="EvidenceUnavailable"/>, or null.</param>
/// <param name="Provenance">The recorded features that went into the layer.</param>
/// <param name="Terrain">What a terrain layer was read from; null for a layer made from features.</param>
public sealed record BuiltEvidence(
    EvidenceLayer Layer,
    float[]? Values,
    string? UnavailableReason,
    EvidenceProvenance Provenance,
    TerrainInput? Terrain = null)
{
    public bool Available => Values is not null;
}

/// <summary>Why an evidence layer could not be made for a run.</summary>
public static class EvidenceUnavailable
{
    /// <summary>No elevation has been built, so there is no ground to read.</summary>
    public const string NoTerrain = "no_terrain";

    /// <summary>The terrain surface this layer reads has not been computed for the current elevation.</summary>
    public const string NoLayer = "no_layer";

    /// <summary>Steepness has been computed only as a percentage, and a model scores it in degrees.</summary>
    public const string SlopeUnit = "slope_unit";

    /// <summary>More recorded features lie in reach than one run reads.</summary>
    public const string TooManyFeatures = "too_many_features";
}

/// <summary>
/// The arithmetic that turns recorded positions into a surface on a run's grid: how far each cell
/// is from the nearest of them, how densely they sit around it, which outline it falls in.
/// </summary>
/// <remarks>
/// Each of these places its inputs exactly — a distance surface has a zero where the thing is and a
/// density surface has its peak there. They take whatever geometries they are given, so the caller
/// decides whose positions those are; nothing here can soften a position it was handed.
/// </remarks>
public static class EvidenceSurfaces
{
    /// <summary>
    /// How far beyond a grid's edge a distance surface looks, and the value it stops counting at.
    /// A feature just outside the area is as near to the cells along that edge as one inside, so
    /// the search has to reach past the edge; and since nothing farther than this from the grid is
    /// read, no cell can honestly claim to be farther than this from anything.
    /// </summary>
    public const double DistanceReachMeters = 5_000d;

    /// <summary>The narrowest a density surface's kernel is drawn, whatever the cell size.</summary>
    private const double MinDensityBandwidthMeters = 500d;

    /// <summary>
    /// Beyond this many bandwidths a Gaussian adds less than a ten-thousandth of its peak.
    /// </summary>
    private const double KernelRadiusInBandwidths = 3d;

    /// <summary>
    /// The standard deviation of the kernel an entrance-density layer is drawn with at a cell size:
    /// two cells, and never less than half a kilometre.
    /// </summary>
    /// <remarks>
    /// The floor is what makes the layer a density rather than a second distance-to-entrance: at a
    /// ten-metre cell a two-cell kernel is a dot on each entrance, and a model weighing it would
    /// only be saying "caves are where the caves are".
    /// </remarks>
    public static double DensityBandwidthMeters(double cellMeters) =>
        Math.Max(Geo.DensityGrid.DefaultBandwidthMeters(cellMeters), MinDensityBandwidthMeters);

    /// <summary>How far beyond a grid's edge an entrance still adds to the density inside it.</summary>
    public static double DensityReachMeters(double cellMeters) =>
        DensityBandwidthMeters(cellMeters) * KernelRadiusInBandwidths;

    /// <summary>
    /// The ground distance in metres from each cell's centre to the nearest of the sources, never
    /// more than <see cref="DistanceReachMeters"/>. A cell inside a source outline is at nought.
    /// </summary>
    /// <param name="sources">Points, lines and outlines in degrees on the world datum.</param>
    public static float[] DistanceMeters(EvidenceGrid grid, IReadOnlyList<Geometry> sources)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(sources);

        var values = new float[grid.CellCount];
        Array.Fill(values, (float)DistanceReachMeters);

        var factory = new GeometryFactory();
        var planar = new List<Geometry>(sources.Count);
        var outlines = new List<Geometry>();
        foreach (var source in sources)
        {
            if (source is null || source.IsEmpty)
            {
                continue;
            }

            var projected = ToLocalMeters(grid, source);
            planar.Add(projected);
            if (projected.Dimension == Dimension.Surface)
            {
                outlines.Add(projected);
            }
        }

        if (planar.Count == 0)
        {
            return values;
        }

        var nearest = new IndexedFacetDistance(factory.BuildGeometry(planar));
        var locators = outlines.ConvertAll(o => (o.EnvelopeInternal, Locator: new IndexedPointInAreaLocator(o)));
        var xPerColumn = grid.CellWidthDegrees * grid.MetersPerDegreeLongitude;
        var yPerRow = grid.CellHeightDegrees * EvidenceGrid.MetersPerDegree;

        for (var row = 0; row < grid.Rows; row++)
        {
            // The plane's origin is the grid's south-west corner, north upwards.
            var y = (grid.Rows - row - 0.5d) * yPerRow;
            for (var column = 0; column < grid.Columns; column++)
            {
                var centre = new Coordinate((column + 0.5d) * xPerColumn, y);
                var inside = false;
                foreach (var (envelope, locator) in locators)
                {
                    if (envelope.Contains(centre) && locator.Locate(centre) != Location.Exterior)
                    {
                        inside = true;
                        break;
                    }
                }

                var distance = inside ? 0d : nearest.Distance(factory.CreatePoint(centre));
                values[grid.IndexOf(column, row)] = (float)Math.Min(distance, DistanceReachMeters);
            }
        }

        return values;
    }

    /// <summary>
    /// A Gaussian kernel density of points, in points per square kilometre at each cell's centre.
    /// </summary>
    /// <param name="points">Positions in degrees on the world datum (x longitude, y latitude).</param>
    /// <param name="bandwidthMeters">The kernel's standard deviation on the ground.</param>
    public static float[] KernelDensityPerKm2(
        EvidenceGrid grid, IReadOnlyList<Coordinate> points, double bandwidthMeters)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(points);
        if (!(bandwidthMeters > 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(bandwidthMeters), "A kernel needs a width.");
        }

        var sums = new double[grid.CellCount];
        var xPerColumn = grid.CellWidthDegrees * grid.MetersPerDegreeLongitude;
        var yPerRow = grid.CellHeightDegrees * EvidenceGrid.MetersPerDegree;
        var reach = bandwidthMeters * KernelRadiusInBandwidths;
        var twoBandwidthSquared = 2d * bandwidthMeters * bandwidthMeters;

        foreach (var point in points)
        {
            // In metres from the grid's north-west corner, south downwards like the rows.
            var x = (point.X - grid.West) * grid.MetersPerDegreeLongitude;
            var y = (grid.North - point.Y) * EvidenceGrid.MetersPerDegree;

            var firstColumn = Math.Max(0, (int)Math.Floor((x - reach) / xPerColumn));
            var lastColumn = Math.Min(grid.Columns - 1, (int)Math.Floor((x + reach) / xPerColumn));
            var firstRow = Math.Max(0, (int)Math.Floor((y - reach) / yPerRow));
            var lastRow = Math.Min(grid.Rows - 1, (int)Math.Floor((y + reach) / yPerRow));

            for (var row = firstRow; row <= lastRow; row++)
            {
                var dy = ((row + 0.5d) * yPerRow) - y;
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var dx = ((column + 0.5d) * xPerColumn) - x;
                    var d2 = (dx * dx) + (dy * dy);
                    if (d2 <= reach * reach)
                    {
                        sums[grid.IndexOf(column, row)] += Math.Exp(-d2 / twoBandwidthSquared);
                    }
                }
            }
        }

        // Two-pi-h-squared is the Gaussian's own normalisation, which makes the sum an intensity
        // per square metre; the million reports it per square kilometre.
        var peak = 1_000_000d / (2d * Math.PI * bandwidthMeters * bandwidthMeters);
        var values = new float[sums.Length];
        for (var i = 0; i < sums.Length; i++)
        {
            values[i] = (float)(sums[i] * peak);
        }

        return values;
    }

    /// <summary>
    /// Which outline each cell's centre falls in: the class of the last outline in the list that
    /// holds it, and not-a-number where none does.
    /// </summary>
    /// <param name="outlines">
    /// Outlines in degrees with the class each stands for, in the order they are to be laid down —
    /// a later one covers an earlier one where they overlap, so the caller lists the larger first
    /// to let a unit mapped in detail show through the regional one it sits in.
    /// </param>
    public static float[] ClassesInside(EvidenceGrid grid, IReadOnlyList<(Geometry Outline, int ClassIndex)> outlines)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(outlines);

        var values = new float[grid.CellCount];
        Array.Fill(values, float.NaN);
        foreach (var (outline, classIndex) in outlines)
        {
            Paint(grid, outline, values, classIndex);
        }

        return values;
    }

    /// <summary>Which cells have their centre inside an outline given in degrees.</summary>
    public static bool[] Inside(EvidenceGrid grid, Geometry outline)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var painted = new float[grid.CellCount];
        Paint(grid, outline, painted, 1f);
        return Array.ConvertAll(painted, v => v == 1f);
    }

    private static void Paint(EvidenceGrid grid, Geometry? outline, float[] values, float value)
    {
        if (outline is null || outline.IsEmpty || outline.Dimension != Dimension.Surface)
        {
            return;
        }

        var box = outline.EnvelopeInternal;
        var firstColumn = Math.Max(0, (int)Math.Floor((box.MinX - grid.West) / grid.CellWidthDegrees));
        var lastColumn = Math.Min(grid.Columns - 1, (int)Math.Floor((box.MaxX - grid.West) / grid.CellWidthDegrees));
        var firstRow = Math.Max(0, (int)Math.Floor((grid.North - box.MaxY) / grid.CellHeightDegrees));
        var lastRow = Math.Min(grid.Rows - 1, (int)Math.Floor((grid.North - box.MinY) / grid.CellHeightDegrees));
        if (firstColumn > lastColumn || firstRow > lastRow)
        {
            return;
        }

        var locator = new IndexedPointInAreaLocator(outline);
        for (var row = firstRow; row <= lastRow; row++)
        {
            var latitude = grid.LatitudeOf(row);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (locator.Locate(new Coordinate(grid.LongitudeOf(column), latitude)) != Location.Exterior)
                {
                    values[grid.IndexOf(column, row)] = value;
                }
            }
        }
    }

    /// <summary>
    /// A copy of a geometry in metres on a flat plane whose origin is the grid's south-west corner.
    /// </summary>
    /// <remarks>
    /// One scale for the whole grid, east–west at its middle latitude. Over an area tens of
    /// kilometres across that is within a small fraction of a percent of the distance on the
    /// spheroid, which is finer than a cell and far finer than the judgment a model's ramp encodes.
    /// </remarks>
    private static Geometry ToLocalMeters(EvidenceGrid grid, Geometry geometry)
    {
        var copy = geometry.Copy();
        copy.Apply(new LocalMeters(grid.West, grid.South, grid.MetersPerDegreeLongitude));
        copy.GeometryChanged();
        return copy;
    }

    private sealed class LocalMeters(double west, double south, double metersPerDegreeLongitude)
        : ICoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            seq.SetX(i, (seq.GetX(i) - west) * metersPerDegreeLongitude);
            seq.SetY(i, (seq.GetY(i) - south) * EvidenceGrid.MetersPerDegree);
        }
    }
}
