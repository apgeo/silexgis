// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

public enum ProcessingJobStatus : short
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// DB-backed processing queue row. The in-process worker polls this table; future
/// external workers (e.g. Python geoprocessing services) can consume the same queue,
/// which is why payloads are self-contained JSON rather than .NET types.
/// </summary>
public class ProcessingJob
{
    public long Id { get; set; }

    /// <summary>Handler discriminator, e.g. "geofile-import".</summary>
    public required string Kind { get; set; }

    /// <summary>Handler input as a JSON object (jsonb).</summary>
    public string Payload { get; set; } = "{}";

    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Queued;

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public Guid? RequestedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Well-known job kinds (extensible — string column by design).</summary>
public static class ProcessingJobKinds
{
    public const string GeofileImport = "geofile-import";
    public const string RasterCog = "raster-cog";

    /// <summary>Backfill EXIF GPS points onto existing image files that predate geotag capture.</summary>
    public const string PhotoGeoBackfill = "photo-geo-backfill";
}
