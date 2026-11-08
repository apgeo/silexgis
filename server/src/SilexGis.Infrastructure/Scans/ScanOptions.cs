// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// How point clouds (scans) are received and processed. A cloud is uploaded through the
/// survey-upload exchange like a survey archive, but is orders of magnitude larger, so it has a
/// cap of its own; and it is tiled by a worker of its own, reached through a directory both
/// sides share, which these settings switch on and bound.
/// </summary>
public sealed class ScanOptions
{
    public const string SectionName = "Scans";

    /// <summary>
    /// The largest point cloud file an upload may send, in bytes. 4 GiB by default — a day's
    /// scanning in LAZ. The reverse proxy in front of the application has to let a body of this
    /// size through as well.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The cap, never below one byte.</summary>
    public long UploadCap => Math.Max(1, MaxUploadBytes);

    /// <summary>
    /// The most everything kept for point clouds may take together, in bytes: the uploaded
    /// originals (which are always kept) and every product made from them. Zero, the default,
    /// sets no cap of its own, leaving the installation's file store cap to answer. A scan run
    /// can take 5–10 GB, which is why scans can be capped apart from the documents beside them.
    /// </summary>
    public long MaxTotalBytes { get; set; }

    /// <summary>The scans' total cap, or null when none is set.</summary>
    public long? TotalCap => MaxTotalBytes > 0 ? MaxTotalBytes : null;

    /// <summary>
    /// Whether this installation runs a point-cloud worker. Off, a run ends after reading the
    /// header with a note saying the cloud was not tiled; on, the run leaves each worker step in
    /// the hand-off directory and waits for its answer.
    /// </summary>
    public bool ProcessingEnabled { get; set; }

    /// <summary>
    /// The directory the application and the worker meet in: the worker's capabilities file at
    /// its root, one directory per job under <c>jobs/</c>. Relative paths are resolved against
    /// the application's own directory.
    /// </summary>
    public string HandoffRoot { get; set; } = "data/scans";

    /// <summary>
    /// How long a job may wait in the hand-off directory before something takes it, in seconds.
    /// Past this, the worker is judged absent and the run fails saying so, rather than waiting
    /// for an answer nothing will write.
    /// </summary>
    public int PickupSeconds { get; set; } = 300;

    /// <summary>
    /// The most one worker step may run, in seconds, after which the job is called off and the
    /// run fails. Four hours by default: tiling was measured at about a minute per 40 million
    /// points, and a cloud that takes longer than this is one to thin first.
    /// </summary>
    public int JobTimeoutSeconds { get; set; } = 14_400;

    /// <summary>Above this many points, the run suggests a thinned display copy.</summary>
    public long DisplayWarnPoints { get; set; } = 50_000_000;

    /// <summary>Above this many points, the run strongly advises a thinned display copy.</summary>
    public long DisplayAdvisePoints { get; set; } = 200_000_000;

    /// <summary>Above this many estimated bytes of tiles, the run suggests a thinned display copy. 2 GiB by default.</summary>
    public long WarnTileBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>The pickup wait, held between one second and an hour.</summary>
    public TimeSpan PickupWait => TimeSpan.FromSeconds(Math.Clamp(PickupSeconds, 1, 3600));

    /// <summary>The time limit of one job, held between five seconds and seven days.</summary>
    public TimeSpan JobTimeout => TimeSpan.FromSeconds(Math.Clamp(JobTimeoutSeconds, 5, 7 * 24 * 3600));
}
