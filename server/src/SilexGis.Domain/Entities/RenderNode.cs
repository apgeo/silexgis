// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A machine somebody runs a render worker on, with the one token it calls the render-node routes
/// with. The machine is the operator's: this application starts nothing on it and never reaches
/// it — the worker calls in, asks for a render job, downloads the job's bundle, says how far it
/// got and sends the film back.
/// </summary>
/// <remarks>
/// <para>
/// The token is stored the way every other secret this application hands out is: the plaintext
/// exists once, when an administrator issues it, and only its SHA-256 is kept. It opens the
/// render-node routes and nothing else — it is not an account, acts as nobody, and reads nothing
/// but the bundle of a job its node holds.
/// </para>
/// <para>
/// A node is withdrawn by stamping <see cref="RevokedAt"/>; its next call is refused, and a job it
/// was rendering goes back to the queue when its lease runs out.
/// </para>
/// </remarks>
public class RenderNode : IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The administrator's name for the machine, unique among the nodes not withdrawn.</summary>
    public required string Name { get; set; }

    /// <summary>The first characters of the token, kept in clear so the nodes can be told apart.</summary>
    public required string TokenPrefix { get; set; }

    /// <summary>SHA-256 of the whole token text. The token itself is never stored.</summary>
    public required byte[] TokenHash { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the node was withdrawn, or null while its token opens the routes.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// The last call the node made, to the second: a claim or a heartbeat. What the flyby panel
    /// reads to decide whether rendering on the server is offered at all.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>The worker's version, as its last claim said.</summary>
    public string? Worker { get; set; }

    /// <summary>The device its last render used, as its last claim said ("GPU (OPTIX)", "CPU").</summary>
    public string? Device { get; set; }

    /// <summary>The Blender versions it renders with, newest first, as its last claim said.</summary>
    public string? BlenderVersions { get; set; }

    /// <summary>Free bytes on its scratch disk, as its last claim said.</summary>
    public long? FreeDiskBytes { get; set; }

    /// <summary>
    /// Why its last claim left queued renders to other nodes: <c>container</c> (a film in a
    /// container it does not write) or <c>disk</c> (more scratch space than it said it has); null
    /// when its last claim left none. The renders it cannot take stay queued for another node.
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>How many queued renders its last claim left for <see cref="SkipReason"/>.</summary>
    public int? SkippedRenders { get; set; }

    /// <summary>For <c>disk</c>: the most scratch space one of those renders is estimated to need, in bytes.</summary>
    public long? SkipNeedsBytes { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// A flyby someone asked to be rendered on a render node: the stored bundle, the job that renders
/// it, the node that holds the job, what the node last said, and the film that came back.
/// </summary>
/// <remarks>
/// One row for each request, and one <see cref="ProcessingJob"/> of kind
/// <see cref="ProcessingJobKinds.BlenderRender"/> beside it. The job's row says where the work
/// stands (queued, running with a lease, ended) as every job's does; this row holds what only a
/// render has.
/// </remarks>
public class FlybyRender
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long? ProcessingJobId { get; set; }

    /// <summary>Who asked: the only account that sees the render and downloads its film.</summary>
    public Guid RequestedBy { get; set; }

    /// <summary>The flyby's name, for the panel and for the node's log.</summary>
    public required string Name { get; set; }

    /// <summary><c>full</c> or <c>draft</c>: which of the bundle's plans the node plays.</summary>
    public required string Plan { get; set; }

    /// <summary><c>mp4</c>, <c>webm</c> or <c>mkv</c>.</summary>
    public required string Container { get; set; }

    /// <summary>Where the bundle is stored; null once the job ended and the bundle was deleted.</summary>
    public string? BundlePath { get; set; }

    public long BundleSize { get; set; }

    /// <summary>The bundle's SHA-256, 64 lower-case hex digits.</summary>
    public required string BundleSha256 { get; set; }

    /// <summary>
    /// The scratch space the render is estimated to need on its node, in bytes: the bundle
    /// unpacked, every frame as a picture (a film is rendered in two stages, frames first) and the
    /// film, with a margin. A node that says it has less free is not given the render.
    /// </summary>
    public long ScratchBytes { get; set; }

    /// <summary>The node that claimed the job last; kept after the attempt ended, so a repeated
    /// complete or fail from it is recognised.</summary>
    public Guid? NodeId { get; set; }

    /// <summary>When the requester asked for the render to stop; the node hears it at its next heartbeat.</summary>
    public DateTimeOffset? CancelRequestedAt { get; set; }

    /// <summary>The node's last heartbeat phase: downloading, rendering, uploading…</summary>
    public string? Phase { get; set; }

    /// <summary>The node's last heartbeat message.</summary>
    public string? Message { get; set; }

    public double? SecondsLeft { get; set; }

    public double? SecondsPerFrame { get; set; }

    /// <summary>The device the film was rendered on, as the node's completion said.</summary>
    public string? Device { get; set; }

    /// <summary>How long the render took on the node, in seconds.</summary>
    public double? RenderSeconds { get; set; }

    public int? FrameCount { get; set; }

    /// <summary>What the node noticed about the film, one line each, joined by new lines.</summary>
    public string? Notices { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When the film and this row are deleted; set when the job ends.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public List<FlybyRenderFile> Files { get; set; } = [];
}

/// <summary>
/// One file a node uploaded for a render: the film, a still, subtitles, Blender's log, the job's
/// status record. Uploaded before the node says the job is complete; the completion names the
/// files that make the result, and those are kept.
/// </summary>
public class FlybyRenderFile
{
    public Guid RenderId { get; set; }

    /// <summary>The file's name, as the node gave it: a safe file name of the protocol's.</summary>
    public required string Name { get; set; }

    /// <summary>The attempt that uploaded it.</summary>
    public int Attempt { get; set; }

    /// <summary><c>movie</c>, <c>still</c>, <c>subtitles</c>, <c>log</c> or <c>status</c>; null
    /// until the completion names the file.</summary>
    public string? Role { get; set; }

    public long Size { get; set; }

    public required string Sha256 { get; set; }

    public required string StoragePath { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
