// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Rendering;

/// <summary>
/// How render jobs are handed to render nodes, and how much of their results is kept. Section
/// <c>RenderNodes</c>; every value can be set as <c>SILEXGIS__RenderNodes__…</c>.
/// </summary>
public sealed class RenderNodeOptions
{
    public const string SectionName = "RenderNodes";

    /// <summary>How long a job stays its node's without a heartbeat, in seconds (at least 3).</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>How often the node is asked to send one; a third of the lease, so one may be missed.</summary>
    public int HeartbeatSeconds { get; set; } = 40;

    /// <summary>
    /// Tries a render gets in all, the first included — a lease that ran out counts as one. More
    /// than an in-process job's three: a node that is stopped and started again goes on from the
    /// frames it kept, so another try costs little, and a home machine is stopped more often than
    /// a server process.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The longest one render may run on the node, in hours; the node stops it after that.</summary>
    public double MaxHours { get; set; } = 24;

    /// <summary>
    /// The largest file a node may upload, in bytes. Below the bundled proxy's request limit
    /// (1 GiB by default, <c>SILEXGIS_MAX_BODY_SIZE</c>): a film is sent in one request.
    /// </summary>
    public long MaxResultBytes { get; set; } = 1_000_000_000;

    /// <summary>How many files one render may upload (a plan that writes frames writes many).</summary>
    public int MaxFilesPerRender { get; set; } = 2_000;

    /// <summary>
    /// The most the stored bundles and results of every render may take together, in bytes. A new
    /// render, or an upload, that would go past it is refused.
    /// </summary>
    public long StorageCapBytes { get; set; } = 50_000_000_000;

    /// <summary>How many days a finished render's film is kept before it is deleted.</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>The longest a claim is held open waiting for a job, in seconds (the node may ask for less).</summary>
    public int ClaimHoldSeconds { get; set; } = 20;

    /// <summary>
    /// How recently a node must have called for rendering on the server to be offered, in seconds.
    /// A waiting node claims every half minute or so.
    /// </summary>
    public int OnlineSeconds { get; set; } = 180;

    /// <summary>How many renders one account may have queued or running at once.</summary>
    public int MaxActivePerAccount { get; set; } = 1;
}
