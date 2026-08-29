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

    /// <summary>Turn an uploaded cave wall mesh into the form the 3D scene draws.</summary>
    public const string SurveyMesh = "survey-mesh";

    /// <summary>Backfill EXIF GPS points onto existing image files that predate geotag capture.</summary>
    public const string PhotoGeoBackfill = "photo-geo-backfill";

    /// <summary>Build the archive for a user's request for a copy of their own account data.</summary>
    public const string AccountDataExport = "account-data-export";

    /// <summary>Re-derive and check the feature aggregate's maintained state. Scheduled.</summary>
    public const string FeatureIntegrityVerify = "feature-integrity-verify";

    /// <summary>Read the text layer out of a stored file into its page rows.</summary>
    public const string TextExtraction = "text-extraction";

    /// <summary>
    /// Queue a reading for every stored file this installation has not read the text of —
    /// files that predate the readers, and files a newer reader should look at again.
    /// </summary>
    public const string TextExtractionBackfill = "text-extraction-backfill";

    /// <summary>
    /// Delete records of people reading files that are past the retention window. Scheduled.
    /// </summary>
    public const string AccessHistoryPrune = "access-history-prune";

    /// <summary>
    /// Turn an office-suite document into a portable one, stored beside the upload, so a
    /// picture of each of its pages can be drawn. Only ever queued where the optional
    /// conversion service is deployed.
    /// </summary>
    public const string DocumentConversion = "document-conversion";

    /// <summary>
    /// Queue a conversion for every office document that has no portable copy and could have
    /// one. The route back for an installation that has just deployed a converter, and for a
    /// document whose own attempt did not finish.
    /// </summary>
    public const string DocumentConversionBackfill = "document-conversion-backfill";

    /// <summary>
    /// Expand an uploaded archive into filed documents, mirroring its folders as cabinets.
    /// </summary>
    public const string ArchiveExpansion = "archive-expansion";

    /// <summary>
    /// Copy a directory the server itself can reach into the archive. Only ever queued for a
    /// path inside the roots the operator listed.
    /// </summary>
    public const string DirectoryImport = "directory-import";

    /// <summary>
    /// Delete resumable uploads nobody came back to, and the partial bytes they hold.
    /// Scheduled.
    /// </summary>
    public const string UploadSessionSweep = "upload-session-sweep";

    /// <summary>
    /// Delete documents whose restore window has run out, and the bytes they hold. Scheduled.
    /// </summary>
    public const string DocumentPurge = "document-purge";

    /// <summary>
    /// Write the notifications for a notice sent to a caving group whose roster was too large to
    /// write to inside the request that sent it.
    /// </summary>
    public const string CavingGroupAnnouncement = "caving-group-announcement";

    /// <summary>
    /// Look for parties past the hour they said they would be back, tell whoever the trip names,
    /// and remind the people on a trip that is nearly here. Scheduled.
    /// </summary>
    /// <remarks>
    /// Its rows are also the record that the check ran at all: a page showing an armed alarm reads
    /// the last completed pass of this kind, so that a check nobody ran reads as unchecked rather
    /// than as nothing wrong.
    /// </remarks>
    public const string TripCalloutSweep = "trip-callout-sweep";

    /// <summary>
    /// Build the terrain the 3D scene draws over one rectangle: obtain the elevation rasters,
    /// prepare them, bake the pyramid, check it and publish it. One row drives the whole chain
    /// and reports which step it has reached.
    /// </summary>
    public const string TerrainBuild = "terrain-build";

    /// <summary>
    /// The kinds that run on a worker of their own rather than on the general one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The queue hands out one job at a time, oldest first, with no time limit. A terrain build
    /// takes minutes to hours, so a build left in the general queue would stand in front of every
    /// document conversion, text reading, raster conversion and upload sweep behind it for its
    /// whole duration.
    /// </para>
    /// <para>
    /// Two workers therefore divide the same table by this one list: the general worker claims
    /// every kind that is <b>not</b> named here, the terrain worker claims only the kinds that
    /// are. Both predicates are built from this list and from nothing else — a worker claiming by
    /// a list of its own would drift from the other, and a kind that ended up in both lists is
    /// run twice while a kind in neither is never run at all, both of which look exactly like an
    /// idle queue.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> TerrainLane = [TerrainBuild];
}
