// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Import;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>One vector row moving through import/export, format-agnostic.</summary>
public sealed record VectorFeature(Geometry Geom, IReadOnlyDictionary<string, object?> Properties);

public sealed record VectorDataset(IReadOnlyList<VectorFeature> Features, int? SourceSrid);

/// <summary>Export targets. Distinct from <see cref="GeofileFormat"/>: exports include CSV.</summary>
public enum ExportFormat
{
    GeoJson,
    Gpx,
    Kml,
    Csv,
    ShapefileZip,
}

/// <summary>
/// Vector format engine seam. The GDAL binding stays behind this interface so it can be
/// swapped (or moved out of process) without touching feature slices.
/// </summary>
public interface IVectorIO
{
    /// <summary>
    /// Reads all vector rows from a file, reprojected to EPSG:4326.
    /// Throws <see cref="VectorIOException"/> with a user-presentable message on bad input.
    /// </summary>
    /// <param name="sourceOptions">
    /// How to read a format that does not describe itself — which columns of a delimited file
    /// hold the coordinates. Ignored by every other format, which say so themselves.
    /// </param>
    VectorDataset Read(string absolutePath, GeofileFormat format, GeofileSourceOptions? sourceOptions = null);

    /// <summary>
    /// The timestamped positions a recorded track holds, oldest first, for placing photographs
    /// by when they were taken.
    /// </summary>
    /// <remarks>
    /// A separate read, and it has to be. The ordinary read deliberately takes a track as one
    /// aggregate line — which is what draws and what imports as a feature — and a line has no
    /// times on its vertices. The times only exist on the per-point layer that read skips, so
    /// answering "where was the photographer at 11:42" means going back to the file.
    /// <para>
    /// Empty for a format that records no times, which is most of them: only a track log has a
    /// clock behind it, and a shapefile of waypoints does not become one by being asked.
    /// </para>
    /// </remarks>
    IReadOnlyList<TrackFix> ReadTrackFixes(string absolutePath, GeofileFormat format);

    /// <summary>Serializes features to the given format; returns the file bytes.</summary>
    byte[] Write(ExportFormat format, string layerName, IReadOnlyList<VectorFeature> features);
}

public sealed class VectorIOException(string message, Exception? inner = null) : Exception(message, inner);
