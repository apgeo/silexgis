// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Blender;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>How a render-node call ends, as the route answers it.</summary>
public enum RenderNodeStatus
{
    /// <summary>200, with <see cref="RenderNodeAnswer.Json"/> when there is a body.</summary>
    Ok,

    /// <summary>204: no job.</summary>
    NoContent,

    /// <summary>400: the message is not one the protocol allows, or a file is not the one described.</summary>
    BadRequest,

    /// <summary>401: no token, an unknown one or a withdrawn one; the worker stops.</summary>
    Unauthorized,

    /// <summary>404: no render job of that id.</summary>
    NotFound,

    /// <summary>409: the job is not this node's attempt any more.</summary>
    Conflict,

    /// <summary>413: a file larger than the server takes, or one too many.</summary>
    TooLarge,
}

/// <summary>A render-node call's answer: a status, a JSON body, or a problem's code and words.</summary>
public sealed record RenderNodeAnswer(RenderNodeStatus Status, string? Json = null, string? Code = null, string? Detail = null)
{
    public static RenderNodeAnswer Ok(string? json = null) => new(RenderNodeStatus.Ok, json);

    public static RenderNodeAnswer Problem(RenderNodeStatus status, string code, string detail) => new(status, null, code, detail);
}

/// <summary>The stored bundle of a job, opened for the node that holds it.</summary>
public sealed record RenderNodeBundleFile(Stream Content, long Size, string FileName);

/// <summary>
/// The render nodes' side of the job queue: a node claims a <see cref="ProcessingJobKinds.BlenderRender"/>
/// job, downloads its bundle, keeps its lease with heartbeats, uploads the film and completes or
/// fails it. The node is a machine outside this process; nothing here starts anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>A call counts only for the node's current attempt.</b> The claim counts an attempt and
/// leases the job to the node; every later call names that attempt, and is answered 409 when the
/// job is no longer running on it for this node — its lease ran out and the sweep took it back,
/// it was claimed again, or it ended. Every write that ends or extends an attempt is made with the
/// job's row locked for that attempt (<see cref="JobsSql.HoldIfStillOursAsync"/>), so a sweep or
/// another claim cannot slip between the check and the write.
/// </para>
/// <para>
/// <b>Every call may be repeated.</b> A heartbeat renews again; a file sent again replaces the one
/// held; a complete or a fail for an attempt this node already ended is answered 200 and changes
/// nothing.
/// </para>
/// </remarks>
public sealed class RenderNodeQueue(
    SilexGisDbContext db,
    IFileStore fileStore,
    IOptions<RenderNodeOptions> options,
    TimeProvider clock,
    ILogger<RenderNodeQueue> logger)
{
    /// <summary>The code of a message that is not one the protocol allows.</summary>
    public const string InvalidMessageCode = "render_node.invalid_message";

    /// <summary>The code of a claim in another version of the protocol.</summary>
    public const string ProtocolCode = "render_node.protocol_unsupported";

    /// <summary>The code of a call for a job this node does not hold on this attempt.</summary>
    public const string NotHeldCode = "render_node.not_held";

    /// <summary>The code of a call naming no render job.</summary>
    public const string NoJobCode = "render_node.job_not_found";

    /// <summary>The code of an uploaded file whose bytes are not the ones described.</summary>
    public const string ChecksumCode = "render_node.checksum_mismatch";

    /// <summary>The code of a file name or a file this server does not take.</summary>
    public const string FileRefusedCode = "render_node.file_refused";

    /// <summary>The code of a file larger than the server takes.</summary>
    public const string TooLargeCode = "render_node.file_too_large";

    /// <summary>The code of a render that would hold more than the server keeps.</summary>
    public const string StorageFullCode = "render_node.storage_full";

    /// <summary>The code of a completion naming files that are not the ones held.</summary>
    public const string FilesMismatchCode = "render_node.files_mismatch";

    /// <summary>What a cancelled render's error starts with, as the protocol's reason does.</summary>
    public const string CancelledPrefix = RenderNodeFailure.Cancelled + ": ";

    /// <summary>The error of a render its requester stopped.</summary>
    public const string CancelledError = CancelledPrefix + "stopped by the person who asked for it";

    /// <summary>The error of a render whose node stopped answering on its last allowed attempt.</summary>
    public const string InterruptedError =
        "interrupted: the render node stopped answering on the last allowed attempt, so the render is not tried again";

    /// <summary>What the error of a render that ran past the time this server allows starts with.</summary>
    public const string TimeLimitPrefix = "timeLimit: ";

    /// <summary>What the error of a render no node took in time starts with.</summary>
    public const string NotTakenPrefix = "notTaken: ";

    /// <summary>What a record of a bundle is filed under, as the bundle's own record is.</summary>
    public const string BundleAuditEntityType = "FlybyBundle";

    /// <summary>What the record of a node's download of a bundle says it was.</summary>
    public const string NodeDownloadFormat = "render-node-download";

    /// <summary>
    /// The words a render's error may start with, before <c>": "</c>: the protocol's failure
    /// reasons and this server's own. The panel says each in the reader's language and shows the
    /// rest as the node's own words.
    /// </summary>
    public static readonly IReadOnlySet<string> ErrorReasons = new HashSet<string>(
        [.. RenderNodeFailure.All, "interrupted", TimeLimitPrefix[..^2], NotTakenPrefix[..^2]],
        StringComparer.Ordinal);

    /// <summary>The extensions a result file may have, by the role the protocol gives it.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleOfExtension = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".mp4"] = "movie",
        [".webm"] = "movie",
        [".mkv"] = "movie",
        [".png"] = "still",
        [".srt"] = "subtitles",
        [".vtt"] = "subtitles",
        [".log"] = "log",
        [".json"] = "status",
    };

    private RenderNodeOptions Options => options.Value;

    private TimeSpan Lease => TimeSpan.FromSeconds(Math.Max(3, Options.LeaseSeconds));

    // ---- the node's token -------------------------------------------------------------------

    /// <summary>
    /// The node a bearer value is the token of; null for none, an unknown one and a withdrawn one,
    /// which the routes refuse alike.
    /// </summary>
    public async Task<RenderNode?> AuthenticateAsync(string? bearer, CancellationToken ct)
    {
        if (!RenderNodeTokens.IsPlausible(bearer))
        {
            return null;
        }

        var hash = RenderNodeTokens.Hash(bearer!);
        return await db.RenderNodes.FirstOrDefaultAsync(n => n.TokenHash == hash && n.RevokedAt == null, ct);
    }

    // ---- claim -------------------------------------------------------------------------------

    /// <summary>
    /// The node's claim: a job leased to it, or none. Records the node's check-in first, whatever
    /// the answer, so the flyby panel knows a node is there.
    /// </summary>
    public async Task<RenderNodeAnswer> ClaimAsync(RenderNode node, string body, CancellationToken ct)
    {
        var (claim, problems) = RenderNodeMessages.Read<RenderNodeClaim>(RenderNodeMessages.Claim, body);
        if (claim is null)
        {
            // A claim of another version is answered 400 by name, so a worker newer than this
            // server says why in its log rather than looping on a shape it cannot fix.
            return RenderNodeVersionOf(body) is { } version && version != RenderNodeProtocol.Version
                ? RenderNodeAnswer.Problem(
                    RenderNodeStatus.BadRequest, ProtocolCode,
                    $"This server speaks version {RenderNodeProtocol.Version} of the render node protocol, not {version}.")
                : Invalid(problems);
        }

        await CheckInAsync(node, claim, ct);

        var preferred = claim.KeptJobs
            .Select(id => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (long?)null)
            .OfType<long>()
            .ToList();

        // A long poll, as the node allows and no longer than this server holds one: a render asked
        // for while the node waits starts within a second or two instead of at its next claim.
        var hold = TimeSpan.FromSeconds(Math.Clamp(claim.WaitSeconds, 0, Math.Max(0, Options.ClaimHoldSeconds)));
        var deadline = clock.GetUtcNow() + hold;
        var containers = claim.Containers.Distinct(StringComparer.Ordinal).ToList();
        while (true)
        {
            var answer = await TryClaimAsync(node, preferred, containers, claim.FreeDiskBytes, ct);
            if (answer.Status != RenderNodeStatus.NoContent || clock.GetUtcNow() >= deadline)
            {
                await RecordSkipsAsync(node, preferred, containers, claim.FreeDiskBytes, ct);
                return answer;
            }

            // The connection goes back to the pool while the claim waits: a waiting node must not
            // hold one of the database's connections for the whole of its long poll.
            if (db.Database.CurrentTransaction is null)
            {
                await db.Database.GetDbConnection().CloseAsync();
            }

            await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        }
    }

    /// <summary>
    /// Records on the node's row which queued renders its claim left for other nodes, and why, so
    /// the administrator's page can say why a render waits while a node calls.
    /// </summary>
    private async Task RecordSkipsAsync(
        RenderNode node, List<long> preferred, List<string> containers, long? freeDiskBytes, CancellationToken ct)
    {
        var (forContainer, forDisk, mostNeeded) = await RenderNodeSql.SkippedAsync(db, preferred, containers, freeDiskBytes, ct);
        (node.SkipReason, node.SkippedRenders, node.SkipNeedsBytes) = forContainer > 0
            ? ("container", forContainer, (long?)null)
            : forDisk > 0
                ? ("disk", forDisk, mostNeeded)
                : ((string?)null, (int?)null, (long?)null);
        if (node.SkipReason is not null)
        {
            logger.LogInformation(
                "Render node {Node} left {Count} queued render(s) to other nodes ({Reason})", node.Name, node.SkippedRenders, node.SkipReason);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<RenderNodeAnswer> TryClaimAsync(
        RenderNode node, List<long> preferred, List<string> containers, long? freeDiskBytes, CancellationToken ct)
    {
        // A job claimed and found unusable (its render stopped meanwhile, or its bundle gone) is
        // ended and the next one taken, a few at most: the node then simply claims again.
        for (var tries = 0; tries < 5; tries++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var jobId = await RenderNodeSql.ClaimNextAsync(db, Lease, preferred, containers, freeDiskBytes, ct);
            if (jobId is not { } id)
            {
                await transaction.CommitAsync(ct);
                return new RenderNodeAnswer(RenderNodeStatus.NoContent);
            }

            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == id, ct);
            var render = await db.FlybyRenders.SingleOrDefaultAsync(r => r.ProcessingJobId == id, ct);
            if (render is null || render.BundlePath is null || render.CancelRequestedAt is not null)
            {
                var error = render?.CancelRequestedAt is not null
                    ? CancelledError
                    : RenderNodeFailure.BadJob + ": the render this job was queued for, or its bundle, is no longer stored";
                End(job, error);
                if (render is not null)
                {
                    Finish(render, job, notify: render.CancelRequestedAt is null);
                }

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                await DeleteStoredAsync(render, keepResults: false, ct);
                continue;
            }

            render.NodeId = node.Id;
            render.Phase = "claimed";
            render.Message = null;
            render.SecondsLeft = null;
            render.SecondsPerFrame = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation(
                "Render node {Node} claimed job {JobId} (attempt {Attempt})", node.Name, job.Id, job.Attempts);
            return RenderNodeAnswer.Ok(RenderNodeMessages.Write(JobFor(job, render)));
        }

        return new RenderNodeAnswer(RenderNodeStatus.NoContent);
    }

    private RenderNodeJob JobFor(ProcessingJob job, FlybyRender render) => new()
    {
        Protocol = RenderNodeProtocol.Version,
        JobId = job.Id.ToString(CultureInfo.InvariantCulture),
        Attempt = job.Attempts,
        LeaseSeconds = (int)Lease.TotalSeconds,
        HeartbeatSeconds = Options.HeartbeatSeconds > 0 ? Options.HeartbeatSeconds : null,

        // The bundle's own base name, not the reader's words: a label for the node's log and
        // folders, made of letters, digits and hyphens that every file system takes.
        Name = FlybyBlenderBundle.Slug(render.Name),
        Bundle = new RenderNodeBundle
        {
            // Relative to the server URL the node was given (a folder): wherever this application
            // is served from, behind whatever path a proxy puts it under, the node finds it there.
            Url = $"jobs/{job.Id.ToString(CultureInfo.InvariantCulture)}/bundle",
            Size = render.BundleSize,
            Sha256 = render.BundleSha256,
        },
        Plan = render.Plan,
        Container = render.Container,
        Limits = new RenderNodeLimits
        {
            MaxHours = Options.MaxHours > 0 ? Options.MaxHours : null,
            MaxResultBytes = Options.MaxResultBytes > 0 ? Options.MaxResultBytes : null,
        },
    };

    private async Task CheckInAsync(RenderNode node, RenderNodeClaim claim, CancellationToken ct)
    {
        node.LastSeenAt = clock.GetUtcNow();
        node.Worker = Cut(claim.Worker, 100);
        node.Device = Cut(claim.Device, 200);
        node.BlenderVersions = Cut(string.Join(", ", claim.BlenderVersions), 500);
        node.FreeDiskBytes = claim.FreeDiskBytes;
        await db.SaveChangesAsync(ct);
    }

    private static int? RenderNodeVersionOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("protocol", out var protocol)
                && protocol.TryGetInt32(out var version)
                    ? version
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- the bundle --------------------------------------------------------------------------

    /// <summary>
    /// The stored bundle of a job the node holds, opened, or why not. Each download is recorded:
    /// the bundle is the one copy of a render's caves that leaves this installation, and the record
    /// says which node took which bundle (by its SHA-256, which the bundle's own record of when it
    /// was made also names) on which attempt.
    /// </summary>
    /// <remarks>
    /// A bundle whose stored file has gone (a disk restored without it) ends the render as a bad
    /// job and is answered 404: the node cannot render it on any attempt, and a 500 would have it
    /// try again until its attempts ran out.
    /// </remarks>
    public async Task<(RenderNodeAnswer Answer, RenderNodeBundleFile? Bundle)> BundleAsync(
        RenderNode node, string jobId, CancellationToken ct)
    {
        var (job, render) = await FindAsync(jobId, ct);
        if (job is null || render is null)
        {
            return (NotFound(), null);
        }

        if (job.Status != ProcessingJobStatus.Running || render.NodeId != node.Id || render.BundlePath is null)
        {
            return (NotHeld(), null);
        }

        Stream content;
        try
        {
            content = await fileStore.OpenReadAsync(render.BundlePath, ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            await EndMissingBundleAsync(node, job.Id, job.Attempts, ct);
            return (NotFound(), null);
        }

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = null,
            Action = AuditActions.Exported,
            EntityType = BundleAuditEntityType,
            EntityId = render.Id.ToString(),
            Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["format"] = NodeDownloadFormat,
                ["renderId"] = render.Id.ToString(),
                ["jobId"] = job.Id,
                ["attempt"] = job.Attempts,
                ["nodeId"] = node.Id.ToString(),
                ["nodeName"] = node.Name,
                ["requestedBy"] = render.RequestedBy.ToString(),
                ["sha256"] = render.BundleSha256,
                ["size"] = render.BundleSize,
            }),
        });
        await db.SaveChangesAsync(ct);
        return (RenderNodeAnswer.Ok(), new RenderNodeBundleFile(content, render.BundleSize, $"{job.Id}.zip"));
    }

    private async Task EndMissingBundleAsync(RenderNode node, long jobId, int attempt, CancellationToken ct)
    {
        FlybyRender? render;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await JobsSql.HoldIfStillOursAsync(db, jobId, attempt, ct))
            {
                return;
            }

            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId, ct);
            render = await db.FlybyRenders.SingleOrDefaultAsync(r => r.ProcessingJobId == jobId, ct);
            End(job, RenderNodeFailure.BadJob + ": the bundle of this render is no longer stored");
            if (render is not null)
            {
                Finish(render, job, notify: true);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await DeleteStoredAsync(render, keepResults: false, ct);
        logger.LogWarning("The stored bundle of render job {JobId} is missing; the render is ended (node {Node})", jobId, node.Name);
    }

    // ---- heartbeat ---------------------------------------------------------------------------

    /// <summary>
    /// How far the node got: renews the lease and records the progress, or tells the node to stop
    /// when the render's requester asked for that — and ends the job then and there, so the
    /// render reads stopped at once and the node's own fail that follows changes nothing.
    /// </summary>
    public async Task<RenderNodeAnswer> HeartbeatAsync(RenderNode node, string jobId, string body, CancellationToken ct)
    {
        var (beat, problems) = RenderNodeMessages.Read<RenderNodeHeartbeat>(RenderNodeMessages.Heartbeat, body);
        if (beat is null)
        {
            return Invalid(problems);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (job, render) = await FindAsync(jobId, ct);
        if (job is null || render is null)
        {
            return NotFound();
        }

        if (!Holds(node, job, render, beat.Attempt) || !await JobsSql.HoldIfStillOursAsync(db, job.Id, beat.Attempt, ct))
        {
            return NotHeld();
        }

        node.LastSeenAt = clock.GetUtcNow();
        if (render.CancelRequestedAt is not null)
        {
            End(job, CancelledError);
            Finish(render, job, notify: false);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await DeleteStoredAsync(render, keepResults: false, ct);
            logger.LogInformation("Render job {JobId} was stopped by its requester; node {Node} told so", job.Id, node.Name);
            return RenderNodeAnswer.Ok(RenderNodeMessages.Write(new RenderNodeHeartbeatReply { Action = RenderNodeProtocol.Cancel }));
        }

        render.Phase = Cut(beat.Phase, 50);
        render.Message = Cut(beat.Message, 2000);
        render.SecondsLeft = beat.SecondsLeft;
        render.SecondsPerFrame = beat.SecondsPerFrame ?? render.SecondsPerFrame;
        await db.SaveChangesAsync(ct);

        await JobsSql.RenewLeaseAsync(db, job.Id, beat.Attempt, Lease, ct);
        if (beat.Frame is { } frame && beat.FrameCount is { } frames)
        {
            await JobsSql.ReportProgressAsync(db, job.Id, Math.Min(frame, frames), frames, ct);
        }

        await transaction.CommitAsync(ct);
        return RenderNodeAnswer.Ok(RenderNodeMessages.Write(new RenderNodeHeartbeatReply { Action = RenderNodeProtocol.Continue }));
    }

    // ---- files -------------------------------------------------------------------------------

    /// <summary>
    /// One result file: checked against the SHA-256 the node sent, stored, and kept beside the
    /// render until the completion names it. A second upload of the same name replaces the first.
    /// </summary>
    public async Task<RenderNodeAnswer> PutFileAsync(
        RenderNode node,
        string jobId,
        string name,
        int? attempt,
        string? sha256,
        long? contentLength,
        Stream body,
        CancellationToken ct)
    {
        if (!RenderNodeProtocol.IsFileName(name) || !RoleOfExtension.ContainsKey(Path.GetExtension(name).ToLowerInvariant()))
        {
            return RenderNodeAnswer.Problem(
                RenderNodeStatus.BadRequest, FileRefusedCode,
                "A result file is named with letters, digits, '.', '_' and '-', and is a film (.mp4, .webm, .mkv), "
                + "a picture (.png), subtitles (.srt, .vtt), a log (.log) or a status record (.json).");
        }

        if (attempt is not > 0 || !RenderNodeProtocol.IsSha256(sha256))
        {
            return Invalid([$"the attempt (?attempt=N) and the {RenderNodeProtocol.Sha256Header} header (64 lower-case hex digits) are required"]);
        }

        var (job, render) = await FindAsync(jobId, ct);
        if (job is null || render is null)
        {
            return NotFound();
        }

        if (!Holds(node, job, render, attempt.Value))
        {
            return NotHeld();
        }

        if (contentLength is { } declared && declared > Options.MaxResultBytes)
        {
            return TooLarge(declared);
        }

        var held = await db.FlybyRenderFiles.Where(f => f.RenderId == render.Id).Select(f => new { f.Name, f.Size }).ToListAsync(ct);
        if (held.Count >= Options.MaxFilesPerRender && held.All(f => f.Name != name))
        {
            return RenderNodeAnswer.Problem(
                RenderNodeStatus.TooLarge, TooLargeCode,
                $"One render keeps at most {Options.MaxFilesPerRender} files.");
        }

        // The room left is the cap less what is stored, the file this one replaces given back: a
        // body is held to it as it streams, so one sent without a length (or with a wrong one) is
        // held to the cap too, not only to the largest file. It is checked again, with every other
        // render's storage held, before the file is kept.
        var replacing = held.SingleOrDefault(f => f.Name == name)?.Size ?? 0;
        var room = Options.StorageCapBytes - await StoredBytesAsync(ct) + replacing;
        if ((contentLength ?? 0) > room || room <= 0)
        {
            return StorageFull();
        }

        string path;
        string actual;
        long length;
        var ceiling = Math.Min(Options.MaxResultBytes, room);
        await using (var counted = new HashingReadStream(body, ceiling))
        {
            try
            {
                path = await fileStore.SaveAsync(counted, Path.GetExtension(name).ToLowerInvariant(), ct);
            }
            catch (ResultTooLargeException)
            {
                return ceiling < Options.MaxResultBytes ? StorageFull() : TooLarge(Options.MaxResultBytes + 1);
            }

            actual = counted.Sha256Hex();
            length = counted.Length;
        }

        if (contentLength is { } promised && promised != length)
        {
            await fileStore.DeleteAsync(path, ct);
            return Invalid([$"the body held {length} bytes, not the {promised} its length said"]);
        }

        if (!string.Equals(actual, sha256, StringComparison.Ordinal))
        {
            await fileStore.DeleteAsync(path, ct);
            return RenderNodeAnswer.Problem(
                RenderNodeStatus.BadRequest, ChecksumCode,
                $"The file's SHA-256 is {actual}, not the {sha256} the {RenderNodeProtocol.Sha256Header} header says.");
        }

        string? replaced = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await JobsSql.HoldIfStillOursAsync(db, job.Id, attempt.Value, ct))
            {
                await transaction.RollbackAsync(ct);
                await fileStore.DeleteAsync(path, ct);
                return NotHeld();
            }

            var row = await db.FlybyRenderFiles.SingleOrDefaultAsync(f => f.RenderId == render.Id && f.Name == name, ct);

            // Two uploads streamed side by side each saw the room before the other was kept.
            await RenderNodeSql.TakeStorageLockAsync(db, ct);
            if (await StoredBytesAsync(ct) - (row?.Size ?? 0) + length > Options.StorageCapBytes)
            {
                await transaction.RollbackAsync(ct);
                await fileStore.DeleteAsync(path, CancellationToken.None);
                return StorageFull();
            }

            if (row is null)
            {
                db.FlybyRenderFiles.Add(new FlybyRenderFile
                {
                    RenderId = render.Id,
                    Name = name,
                    Attempt = attempt.Value,
                    Size = length,
                    Sha256 = actual,
                    StoragePath = path,
                    UploadedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                replaced = row.StoragePath;
                row.Attempt = attempt.Value;
                row.Size = length;
                row.Sha256 = actual;
                row.StoragePath = path;
                row.Role = null;
                row.UploadedAt = clock.GetUtcNow();
            }

            node.LastSeenAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        if (replaced is not null)
        {
            await fileStore.DeleteAsync(replaced, ct);
        }

        return RenderNodeAnswer.Ok();
    }

    private static RenderNodeAnswer StorageFull() => RenderNodeAnswer.Problem(
        RenderNodeStatus.TooLarge, StorageFullCode,
        "The space this server keeps for renders is full; the file is not taken.");

    private RenderNodeAnswer TooLarge(long size) => RenderNodeAnswer.Problem(
        RenderNodeStatus.TooLarge, TooLargeCode,
        $"A result file may be at most {Options.MaxResultBytes} bytes; this one is {size}.");

    // ---- complete ----------------------------------------------------------------------------

    /// <summary>
    /// The render succeeded: every file the node names is the one held, by name, size and SHA-256;
    /// those are kept as the result, whatever else was uploaded is deleted, and the requester is told.
    /// </summary>
    public async Task<RenderNodeAnswer> CompleteAsync(RenderNode node, string jobId, string body, CancellationToken ct)
    {
        var (complete, problems) = RenderNodeMessages.Read<RenderNodeComplete>(RenderNodeMessages.Complete, body);
        if (complete is null)
        {
            return Invalid(problems);
        }

        IReadOnlyList<string> deleted;
        FlybyRender render;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var (job, found) = await FindAsync(jobId, ct);
            if (job is null || found is null)
            {
                return NotFound();
            }

            render = found;
            if (EndedBy(node, job, render, complete.Attempt) && job.Status == ProcessingJobStatus.Succeeded)
            {
                return RenderNodeAnswer.Ok();
            }

            if (!Holds(node, job, render, complete.Attempt) || !await JobsSql.HoldIfStillOursAsync(db, job.Id, complete.Attempt, ct))
            {
                return NotHeld();
            }

            var held = await db.FlybyRenderFiles.Where(f => f.RenderId == render.Id).ToListAsync(ct);
            var mismatched = complete.Files
                .Where(listed => held.SingleOrDefault(f => f.Name == listed.Name) is not { } file
                    || file.Attempt != complete.Attempt || file.Size != listed.Size
                    || !string.Equals(file.Sha256, listed.Sha256, StringComparison.Ordinal))
                .Select(listed => listed.Name)
                .ToList();
            if (mismatched.Count > 0)
            {
                return RenderNodeAnswer.Problem(
                    RenderNodeStatus.BadRequest, FilesMismatchCode,
                    "These files are not the ones this server holds for the attempt (name, size and SHA-256): "
                    + string.Join(", ", mismatched.Take(20)));
            }

            if (!complete.Files.Any(f => f.Role is "movie" or "still"))
            {
                return RenderNodeAnswer.Problem(
                    RenderNodeStatus.BadRequest, FilesMismatchCode,
                    "A completed render names at least one film or picture.");
            }

            var kept = complete.Files.ToDictionary(f => f.Name, f => f.Role, StringComparer.Ordinal);
            foreach (var file in held)
            {
                if (kept.TryGetValue(file.Name, out var role))
                {
                    file.Role = role;
                }
                else
                {
                    db.FlybyRenderFiles.Remove(file);
                }
            }

            deleted = [.. held.Where(f => !kept.ContainsKey(f.Name)).Select(f => f.StoragePath)];

            var now = clock.GetUtcNow();
            job.Status = ProcessingJobStatus.Succeeded;
            job.CompletedAt = now;
            job.LeaseExpiresAt = null;
            job.Error = null;
            if (complete.FrameCount is { } frames)
            {
                job.ProgressDone = frames;
                job.ProgressTotal = frames;
            }

            render.Device = Cut(complete.Device, 200);
            render.RenderSeconds = complete.Seconds;
            render.SecondsPerFrame = complete.SecondsPerFrame ?? render.SecondsPerFrame;
            render.FrameCount = complete.FrameCount;
            render.Notices = Cut(string.Join("\n", complete.Notices), 20_000);
            render.Phase = "done";
            render.Message = null;
            render.SecondsLeft = null;
            node.LastSeenAt = now;
            Finish(render, job, notify: true);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        foreach (var path in deleted)
        {
            await fileStore.DeleteAsync(path, ct);
        }

        await DeleteStoredAsync(render, keepResults: true, ct);
        logger.LogInformation("Render job of {Render} completed on node {Node}", render.Id, node.Name);
        return RenderNodeAnswer.Ok();
    }

    // ---- fail --------------------------------------------------------------------------------

    /// <summary>
    /// The render failed on the node. A failure the node calls passing, of a reason the protocol
    /// counts as passing, puts the job back in the queue while it has tries left; any other ends
    /// it, with the reason and the node's words as its error.
    /// </summary>
    public async Task<RenderNodeAnswer> FailAsync(RenderNode node, string jobId, string body, CancellationToken ct)
    {
        var (fail, problems) = RenderNodeMessages.Read<RenderNodeFail>(RenderNodeMessages.Fail, body);
        if (fail is null)
        {
            return Invalid(problems);
        }

        FlybyRender render;
        bool ended;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var (job, found) = await FindAsync(jobId, ct);
            if (job is null || found is null)
            {
                return NotFound();
            }

            render = found;
            if (EndedBy(node, job, render, fail.Attempt))
            {
                return RenderNodeAnswer.Ok();
            }

            if (!Holds(node, job, render, fail.Attempt) || !await JobsSql.HoldIfStillOursAsync(db, job.Id, fail.Attempt, ct))
            {
                return NotHeld();
            }

            var cancelled = render.CancelRequestedAt is not null || fail.Reason == RenderNodeFailure.Cancelled;
            var error = cancelled && render.CancelRequestedAt is not null
                ? CancelledError
                : Cut($"{fail.Reason}: {fail.Message}", 4000)!;
            var retry = !cancelled && fail.Retryable && RenderNodeFailure.IsRetryable(fail.Reason)
                && job.Attempts < Options.MaxAttempts;
            node.LastSeenAt = clock.GetUtcNow();
            render.Phase = null;
            render.Message = null;
            render.SecondsLeft = null;
            if (retry)
            {
                await db.SaveChangesAsync(ct);
                await JobsSql.PutBackAsync(db, job.Id, fail.Attempt, JobRetry.DelayAfter(job.Attempts), error, ct);
                ended = false;
            }
            else
            {
                End(job, error);
                Finish(render, job, notify: !cancelled);
                await db.SaveChangesAsync(ct);
                ended = true;
            }

            await transaction.CommitAsync(ct);
        }

        if (ended)
        {
            await DeleteStoredAsync(render, keepResults: false, ct);
        }

        logger.LogInformation(
            "Render job of {Render} failed on node {Node} ({Reason}); {Outcome}",
            render.Id, node.Name, fail.Reason, ended ? "ended" : "queued again");
        return RenderNodeAnswer.Ok();
    }

    // ---- what the routes share ---------------------------------------------------------------

    private async Task<(ProcessingJob? Job, FlybyRender? Render)> FindAsync(string jobId, CancellationToken ct)
    {
        if (!RenderNodeProtocol.IsJobId(jobId)
            || !long.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return (null, null);
        }

        var job = await db.ProcessingJobs.SingleOrDefaultAsync(
            j => j.Id == id && ProcessingJobKinds.NodeLane.Contains(j.Kind), ct);
        if (job is null)
        {
            return (null, null);
        }

        return (job, await db.FlybyRenders.SingleOrDefaultAsync(r => r.ProcessingJobId == id, ct));
    }

    /// <summary>Whether the job is running on <paramref name="attempt"/> for this node.</summary>
    private static bool Holds(RenderNode node, ProcessingJob job, FlybyRender render, int attempt) =>
        job.Status == ProcessingJobStatus.Running && job.Attempts == attempt && render.NodeId == node.Id;

    /// <summary>Whether this node's <paramref name="attempt"/> already ended (put back, succeeded or failed).</summary>
    private static bool EndedBy(RenderNode node, ProcessingJob job, FlybyRender render, int attempt) =>
        job.Status != ProcessingJobStatus.Running && job.Attempts == attempt && render.NodeId == node.Id;

    private void End(ProcessingJob job, string error)
    {
        job.Status = ProcessingJobStatus.Failed;
        job.Error = error;
        job.CompletedAt = clock.GetUtcNow();
        job.LeaseExpiresAt = null;
    }

    /// <summary>
    /// What follows a render's end, in the transaction that ends it: when its film goes, that its
    /// bundle is gone, and the requester's notice.
    /// </summary>
    internal void Finish(FlybyRender render, ProcessingJob job, bool notify)
    {
        var now = clock.GetUtcNow();
        render.CompletedAt = now;
        render.ExpiresAt = now.AddDays(Math.Max(1, Options.RetentionDays));
        if (notify)
        {
            ProcessingJobWorkerBase.NotifyRequester(db, job);
        }
    }

    /// <summary>
    /// Deletes what an ended render no longer needs, after the transaction that ended it: the
    /// bundle always, and the uploaded files too unless they are its result.
    /// </summary>
    internal async Task DeleteStoredAsync(FlybyRender? render, bool keepResults, CancellationToken ct)
    {
        if (render is null)
        {
            return;
        }

        var paths = new List<string>();
        if (render.BundlePath is { } bundle)
        {
            paths.Add(bundle);
            await db.FlybyRenders.Where(r => r.Id == render.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.BundlePath, (string?)null), ct);
            render.BundlePath = null;
        }

        if (!keepResults)
        {
            var files = await db.FlybyRenderFiles.Where(f => f.RenderId == render.Id).ToListAsync(ct);
            paths.AddRange(files.Select(f => f.StoragePath));
            await db.FlybyRenderFiles.Where(f => f.RenderId == render.Id).ExecuteDeleteAsync(ct);
        }

        foreach (var path in paths)
        {
            await fileStore.DeleteAsync(path, ct);
        }
    }

    /// <summary>The bytes every render's stored bundle and files take together.</summary>
    internal async Task<long> StoredBytesAsync(CancellationToken ct) =>
        (await db.FlybyRenderFiles.SumAsync(f => (long?)f.Size, ct) ?? 0)
        + (await db.FlybyRenders.Where(r => r.BundlePath != null).SumAsync(r => (long?)r.BundleSize, ct) ?? 0);

    private static RenderNodeAnswer Invalid(IReadOnlyList<string> problems) => RenderNodeAnswer.Problem(
        RenderNodeStatus.BadRequest, InvalidMessageCode, string.Join("; ", problems));

    private static RenderNodeAnswer NotFound() => RenderNodeAnswer.Problem(
        RenderNodeStatus.NotFound, NoJobCode, "There is no render job of that id.");

    private static RenderNodeAnswer NotHeld() => RenderNodeAnswer.Problem(
        RenderNodeStatus.Conflict, NotHeldCode,
        "The job is not this node's on that attempt any more: its lease ran out, it was claimed again, or it ended.");

    private static string? Cut(string? text, int length) =>
        text is null ? null : text.Length <= length ? text : text[..length];
}

/// <summary>A result file went past the largest the server takes while it was being read.</summary>
internal sealed class ResultTooLargeException() : IOException("The result file is larger than the server takes.");

/// <summary>
/// Reads a request body through to the file store, counting and hashing what passes and refusing
/// to go past a ceiling — so a body with no length, or a wrong one, cannot fill the disk.
/// </summary>
internal sealed class HashingReadStream(Stream inner, long ceiling) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => read;

    public override long Position
    {
        get => read;
        set => throw new NotSupportedException();
    }

    public string Sha256Hex() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public override int Read(byte[] buffer, int offset, int count) => Count(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        return Count(buffer.Span[..n]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Count(ReadOnlySpan<byte> bytes)
    {
        read += bytes.Length;
        if (read > ceiling)
        {
            throw new ResultTooLargeException();
        }

        hash.AppendData(bytes);
        return bytes.Length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
