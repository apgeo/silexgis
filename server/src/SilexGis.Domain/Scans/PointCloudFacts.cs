// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Scans;

/// <summary>The coordinate system a point cloud's file stated, kept as it was found.</summary>
/// <param name="HorizontalEpsg">The EPSG code of the projected or geographic system, when the file's keys name one.</param>
/// <param name="VerticalEpsg">The EPSG code of the vertical system, when named.</param>
/// <param name="Wkt">The WKT string, when the file carries one, verbatim.</param>
public sealed record PointCloudCrs(int? HorizontalEpsg, int? VerticalEpsg, string? Wkt);

/// <summary>
/// What the plan and profile pictures of a scan are: the pixel size of each, their size in pixels,
/// and the ends of the colour scale they share. Heights are metres above the cloud's lowest point,
/// so nothing here is a position, whatever frame the cloud is in.
/// </summary>
public sealed record PointCloudPictures
{
    /// <summary>The top view's pixel, in metres: the size asked for, or larger for a cave too big to draw at it.</summary>
    public double PixelM { get; init; }

    /// <summary>"height" or "intensity".</summary>
    public required string ColourBy { get; init; }

    /// <summary>The value the colour scale starts at: metres above the lowest point, or an intensity.</summary>
    public double ValueLow { get; init; }

    /// <summary>The value the colour scale ends at.</summary>
    public double ValueHigh { get; init; }

    /// <summary>The top view's width and height in pixels.</summary>
    public required int[] PlanSize { get; init; }

    /// <summary>The profile's width and height in pixels; null when no profile was drawn (no centreline).</summary>
    public int[]? ProfileSize { get; init; }

    /// <summary>The profile's pixel, in metres.</summary>
    public double? ProfilePixelM { get; init; }

    /// <summary>The length of the centreline the profile is unrolled along, in metres.</summary>
    public double? ProfileLengthM { get; init; }
}

/// <summary>
/// What is known about a point cloud, in two layers: what its header said when it was inspected,
/// and what tiling added. Stored as the model's <c>point_cloud</c> document and shown on the
/// scan's card; nothing queries it.
/// </summary>
/// <remarks>
/// The extent is in the file's own frame — the numbers a scanner or an export wrote, which are
/// earth coordinates only when <see cref="Crs"/> is set. A card may show the extent's size to
/// anyone the cave is visible to; its position is a location, and when the file has a coordinate
/// system it is shown only under the exact-location rule. The facts carry both and leave that
/// decision to the endpoint that answers, which is the one place that knows who is asking.
/// </remarks>
public sealed record PointCloudFacts
{
    /// <summary>How many points the file holds, by its header.</summary>
    public long PointCount { get; init; }

    /// <summary>The LAS point data record format, 0 to 10.</summary>
    public int PointFormat { get; init; }

    /// <summary>The LAS version as written, such as <c>1.4</c>.</summary>
    public required string LasVersion { get; init; }

    /// <summary>Whether the points are LAZ-compressed.</summary>
    public bool Compressed { get; init; }

    /// <summary>Whether each point carries a colour.</summary>
    public bool HasRgb { get; init; }

    /// <summary>
    /// Whether the points carry return intensities that are not all zero. Every LAS format has
    /// the field, so the header cannot say; null until a step that reads the points has answered.
    /// </summary>
    public bool? HasIntensity { get; init; }

    /// <summary>What a stored ordinate is multiplied by, per axis: the cloud's resolution.</summary>
    public required double[] Scale { get; init; }

    /// <summary>What is added to a scaled ordinate, per axis.</summary>
    public required double[] Offset { get; init; }

    /// <summary>The smallest ordinate per axis, in the file's own frame.</summary>
    public required double[] Min { get; init; }

    /// <summary>The largest ordinate per axis, in the file's own frame.</summary>
    public required double[] Max { get; init; }

    /// <summary>The coordinate system the file stated, or null when it stated none.</summary>
    public PointCloudCrs? Crs { get; init; }

    /// <summary>The software that wrote the file, as it named itself.</summary>
    public string? GeneratingSoftware { get; init; }

    /// <summary>The size of the tileset archive in bytes, once tiled.</summary>
    public long? TileBytes { get; init; }

    /// <summary>How many tiles the tileset holds, once tiled.</summary>
    public int? TileCount { get; init; }

    /// <summary>The tiler's version as the worker reported it, once tiled.</summary>
    public string? TilerVersion { get; init; }

    /// <summary>The voxel size in metres the shown copy was thinned to, or null when the full cloud was tiled.</summary>
    public double? DisplayVoxelM { get; init; }

    /// <summary>How many points the tiled copy holds, when it was thinned; null when the full cloud was tiled.</summary>
    public long? DisplayPointCount { get; init; }

    /// <summary>The stride the shown copy was thinned by, when every n-th point was kept; null otherwise.</summary>
    public int? DisplayEveryNth { get; init; }

    /// <summary>
    /// What was subtracted from the file's coordinates before tiling, per axis: the tiles are in
    /// the cloud's own frame around this point. For a georeferenced file it is a position, and is
    /// shown only under the exact-location rule, like the extent's corners.
    /// </summary>
    public double[]? OriginShift { get; init; }

    /// <summary>What a pass over the points said a thinned copy would keep, once estimated.</summary>
    public PointCloudEstimate? Estimate { get; init; }

    /// <summary>The installation's advice on thinning, computed from the estimate when it was made.</summary>
    public PointCloudDisplayAdvice? Advice { get; init; }

    /// <summary>
    /// What the inspection of a raw scanner recording found, for a scan made from one; null for
    /// a point cloud uploaded as a LAS or LAZ file. Kept across the run, so the facts of the
    /// exported cloud that replace the header layer still say what it was made from.
    /// </summary>
    public MandeyeRecordingFacts? Recording { get; init; }

    /// <summary>
    /// The plan and profile pictures the run drew, when it drew them: their pixel sizes and the
    /// colour scale, for a legend. No coordinate is among them.
    /// </summary>
    public PointCloudPictures? Pictures { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The facts a header gives, before any tiling.</summary>
    public static PointCloudFacts FromHeader(LasHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new PointCloudFacts
        {
            PointCount = header.PointCount,
            PointFormat = header.PointFormat,
            LasVersion = header.Version,
            Compressed = header.IsCompressed,
            HasRgb = header.HasRgb,
            Scale = [header.Scale.X, header.Scale.Y, header.Scale.Z],
            Offset = [header.Offset.X, header.Offset.Y, header.Offset.Z],
            Min = [header.Min.X, header.Min.Y, header.Min.Z],
            Max = [header.Max.X, header.Max.Y, header.Max.Z],
            Crs = header.CoordinateSystem is { IsGeoreferenced: true } crs
                ? new PointCloudCrs(crs.HorizontalEpsg, crs.VerticalEpsg, crs.Wkt)
                : null,
            GeneratingSoftware = string.IsNullOrWhiteSpace(header.GeneratingSoftware) ? null : header.GeneratingSoftware,
        };
    }

    /// <summary>
    /// The facts of a raw recording before it is processed: the header layer of its first lidar
    /// chunk — the chunks are written alike, in the sensor's own frame — with the point count of
    /// all of them, and what the inspection found of the recording. The export's header replaces
    /// the header layer once there is one.
    /// </summary>
    public static PointCloudFacts FromRecording(LasHeader firstChunk, MandeyeRecordingFacts recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return FromHeader(firstChunk) with { PointCount = recording.PointCount, Recording = recording };
    }

    /// <summary>
    /// These facts with what an earlier run's processing added carried over — the estimate, the
    /// advice and the tiling — for a rerun that reuses that processing rather than repeating it.
    /// The header's own facts are this record's, read afresh.
    /// </summary>
    public PointCloudFacts WithProcessingOf(PointCloudFacts? earlier) => earlier is null ? this : this with
    {
        HasIntensity = HasIntensity ?? earlier.HasIntensity,
        TileBytes = earlier.TileBytes,
        TileCount = earlier.TileCount,
        TilerVersion = earlier.TilerVersion,
        DisplayVoxelM = earlier.DisplayVoxelM,
        DisplayPointCount = earlier.DisplayPointCount,
        DisplayEveryNth = earlier.DisplayEveryNth,
        OriginShift = earlier.OriginShift,
        Estimate = earlier.Estimate,
        Advice = earlier.Advice,
        Recording = Recording ?? earlier.Recording,
        Pictures = earlier.Pictures,
    };

    /// <summary>
    /// Whether the scan's original is a location: the file stated a coordinate system, or the
    /// raw recording it was made from holds GNSS positions of its track. Either way its original
    /// and the coordinates in these facts are handed out only under the exact-location rule.
    /// </summary>
    public bool IsGeoreferenced => Crs is not null || Recording is { HasGnss: true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The facts a stored document holds, or null for no document or one that does not read.</summary>
    public static PointCloudFacts? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PointCloudFacts>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
