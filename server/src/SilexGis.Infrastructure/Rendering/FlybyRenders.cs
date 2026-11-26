// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>Where a render stands, as its requester's panel says it.</summary>
public enum FlybyRenderState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>A render and its job, read together.</summary>
public sealed record FlybyRenderView(FlybyRender Render, ProcessingJob? Job)
{
    public FlybyRenderState State => Job?.Status switch
    {
        ProcessingJobStatus.Queued => FlybyRenderState.Queued,
        ProcessingJobStatus.Running => FlybyRenderState.Running,
        ProcessingJobStatus.Succeeded => FlybyRenderState.Succeeded,
        _ when Job?.Error?.StartsWith(RenderNodeQueue.CancelledPrefix, StringComparison.Ordinal) == true
            || (Job is null && Render.CancelRequestedAt is not null) => FlybyRenderState.Cancelled,
        _ => FlybyRenderState.Failed,
    };
}

/// <summary>Why a render was not queued.</summary>
public enum FlybyRenderRefusal
{
    None,

    /// <summary>The account already has as many renders queued or running as it may.</summary>
    AlreadyActive,

    /// <summary>The space kept for renders is full.</summary>
    StorageFull,
}

/// <summary>
/// The requester's and the administrator's side of rendering on a node: queuing a render of a
/// stored bundle, following it, stopping it, its film; issuing and withdrawing node tokens.
/// </summary>
public sealed class FlybyRenders(
    SilexGisDbContext db,
    IFileStore fileStore,
    RenderNodeQueue queue,
    IOptions<RenderNodeOptions> options,
    TimeProvider clock)
{
    /// <summary>The plans a node can play: the bundle's whole one, or its draft.</summary>
    public static readonly IReadOnlyList<string> Plans = ["draft", "full"];

    /// <summary>The containers the player writes a film in.</summary>
    public static readonly IReadOnlyList<string> Containers = ["mp4", "webm", "mkv"];

    private RenderNodeOptions Options => options.Value;

    // ---- nodes -------------------------------------------------------------------------------

    /// <summary>Whether a node called recently enough for a render to be taken soon.</summary>
    public async Task<bool> AnyNodeOnlineAsync(CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddSeconds(-Math.Max(1, Options.OnlineSeconds));
        return await db.RenderNodes.AnyAsync(n => n.RevokedAt == null && n.LastSeenAt != null && n.LastSeenAt >= since, ct);
    }

    public async Task<IReadOnlyList<RenderNode>> NodesAsync(CancellationToken ct) =>
        await db.RenderNodes.AsNoTracking()
            .OrderBy(n => n.RevokedAt != null).ThenBy(n => n.Name)
            .ToListAsync(ct);

    /// <summary>A new node and its token; null when a node not withdrawn already has the name.</summary>
    public async Task<(RenderNode Node, string Token)?> IssueAsync(string name, Guid? by, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await db.RenderNodes.AnyAsync(n => n.Name == trimmed && n.RevokedAt == null, ct))
        {
            return null;
        }

        var (token, hash, prefix) = RenderNodeTokens.Mint();
        var node = new RenderNode
        {
            Name = trimmed,
            TokenHash = hash,
            TokenPrefix = prefix,
            CreatedBy = by,
            CreatedAt = clock.GetUtcNow(),
        };
        db.RenderNodes.Add(node);
        await db.SaveChangesAsync(ct);
        return (node, token);
    }

    /// <summary>Withdraws a node; false when there is none of that id. Withdrawing twice is not an error.</summary>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct)
    {
        var node = await db.RenderNodes.SingleOrDefaultAsync(n => n.Id == id, ct);
        if (node is null)
        {
            return false;
        }

        node.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- renders -----------------------------------------------------------------------------

    /// <summary>Stores a bundle and queues its render for a node.</summary>
    public async Task<(FlybyRenderView? Render, FlybyRenderRefusal Refusal)> EnqueueAsync(
        Guid requester, string name, byte[] bundle, string plan, string container, CancellationToken ct)
    {
        var active = await (
                from r in db.FlybyRenders
                join j in db.ProcessingJobs on r.ProcessingJobId equals j.Id
                where r.RequestedBy == requester
                      && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running)
                select r.Id)
            .CountAsync(ct);
        if (active >= Math.Max(1, Options.MaxActivePerAccount))
        {
            return (null, FlybyRenderRefusal.AlreadyActive);
        }

        if (await queue.StoredBytesAsync(ct) + bundle.LongLength > Options.StorageCapBytes)
        {
            return (null, FlybyRenderRefusal.StorageFull);
        }

        string path;
        using (var content = new MemoryStream(bundle, writable: false))
        {
            path = await fileStore.SaveAsync(content, ".zip", ct);
        }

        var render = new FlybyRender
        {
            RequestedBy = requester,
            Name = name.Length <= 200 ? name : name[..200],
            Plan = plan,
            Container = container,
            BundlePath = path,
            BundleSize = bundle.LongLength,
            BundleSha256 = Convert.ToHexStringLower(SHA256.HashData(bundle)),
            CreatedAt = clock.GetUtcNow(),
        };
        var job = new ProcessingJob
        {
            Kind = ProcessingJobKinds.BlenderRender,
            RequestedBy = requester,
            Payload = JsonSerializer.Serialize(new
            {
                renderId = render.Id,
                bundle = new { id = render.Id, size = render.BundleSize, sha256 = render.BundleSha256 },
                plan,
                container,
                requestedBy = requester,
            }),
        };

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync(ct);
            render.ProcessingJobId = job.Id;
            db.FlybyRenders.Add(render);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await fileStore.DeleteAsync(path, CancellationToken.None);
            throw;
        }

        return (new FlybyRenderView(render, job), FlybyRenderRefusal.None);
    }

    /// <summary>The account's renders still kept, newest first.</summary>
    public async Task<IReadOnlyList<FlybyRenderView>> MineAsync(Guid requester, CancellationToken ct)
    {
        var rows = await (
                from r in db.FlybyRenders.AsNoTracking().Include(r => r.Files)
                where r.RequestedBy == requester
                join j in db.ProcessingJobs.AsNoTracking() on r.ProcessingJobId equals j.Id into jobs
                from j in jobs.DefaultIfEmpty()
                orderby r.CreatedAt descending
                select new { r, j })
            .Take(20)
            .ToListAsync(ct);
        return [.. rows.Select(x => new FlybyRenderView(x.r, x.j))];
    }

    /// <summary>One of the account's renders; null when it has none of that id.</summary>
    public async Task<FlybyRenderView?> OneAsync(Guid requester, Guid id, CancellationToken ct)
    {
        var render = await db.FlybyRenders.AsNoTracking().Include(r => r.Files)
            .SingleOrDefaultAsync(r => r.Id == id && r.RequestedBy == requester, ct);
        if (render is null)
        {
            return null;
        }

        var job = render.ProcessingJobId is { } jobId
            ? await db.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, ct)
            : null;
        return new FlybyRenderView(render, job);
    }

    /// <summary>
    /// Stops one of the account's renders: a queued one at once; a running one at its node's next
    /// heartbeat, or at once when its node has stopped answering. A render that ended already is
    /// left as it is. Null when the account has no render of that id.
    /// </summary>
    public async Task<FlybyRenderView?> CancelAsync(Guid requester, Guid id, CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var render = await db.FlybyRenders.SingleOrDefaultAsync(r => r.Id == id && r.RequestedBy == requester, ct);
            if (render is null)
            {
                return null;
            }

            var job = render.ProcessingJobId is { } jobId
                ? await db.ProcessingJobs.SingleOrDefaultAsync(j => j.Id == jobId, ct)
                : null;
            if (job is { Status: ProcessingJobStatus.Queued or ProcessingJobStatus.Running })
            {
                render.CancelRequestedAt ??= clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        // The queued, and the running whose node is gone, end now; the running with a live node
        // hears it at its next heartbeat.
        await EndCancelledAsync(ct);
        return await OneAsync(requester, id, ct);
    }

    /// <summary>A result file of one of the account's finished renders; null when there is none of that name.</summary>
    public async Task<FlybyRenderFile?> FileAsync(Guid requester, Guid id, string name, CancellationToken ct) =>
        await db.FlybyRenderFiles.AsNoTracking()
            .Where(f => f.RenderId == id && f.Name == name && f.Role != null)
            .Where(f => db.FlybyRenders.Any(r => r.Id == id && r.RequestedBy == requester))
            .SingleOrDefaultAsync(ct);

    // ---- the sweep ---------------------------------------------------------------------------

    /// <summary>
    /// Ends the renders whose requester stopped them and that no node will tell any more, and
    /// deletes what they stored. Answers how many it ended.
    /// </summary>
    public async Task<int> EndCancelledAsync(CancellationToken ct)
    {
        IReadOnlyList<long> ended;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            ended = await RenderNodeSql.EndCancelledAsync(db, RenderNodeQueue.CancelledError, ct);
            await FinishEndedAsync(ended, notify: false, ct);
            await transaction.CommitAsync(ct);
        }

        await DeleteEndedAsync(ended, ct);
        return ended.Count;
    }

    /// <summary>
    /// Takes back the render jobs whose node stopped answering — a lease that ran out — and fails
    /// those that have had every try, telling their requesters. Answers how many it failed.
    /// </summary>
    public async Task<int> ReturnStaleAsync(CancellationToken ct)
    {
        IReadOnlyList<long> failed;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            failed = await Jobs.JobsSql.ReturnStaleAsync(
                db, Jobs.JobLane.RenderNode, Math.Max(1, Options.MaxAttempts), RenderNodeQueue.InterruptedError, ct);
            await FinishEndedAsync(failed, notify: true, ct);
            await transaction.CommitAsync(ct);
        }

        await DeleteEndedAsync(failed, ct);
        return failed.Count;
    }

    /// <summary>Deletes the renders whose keeping time is over, with their films. Answers how many.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = await db.FlybyRenders.Include(r => r.Files)
            .Where(r => r.ExpiresAt != null && r.ExpiresAt < now)
            .Take(100)
            .ToListAsync(ct);
        foreach (var render in expired)
        {
            var paths = render.Files.Select(f => f.StoragePath).ToList();
            if (render.BundlePath is { } bundle)
            {
                paths.Add(bundle);
            }

            db.FlybyRenders.Remove(render);
            await db.SaveChangesAsync(ct);
            foreach (var path in paths)
            {
                await fileStore.DeleteAsync(path, ct);
            }
        }

        return expired.Count;
    }

    private async Task FinishEndedAsync(IReadOnlyList<long> jobIds, bool notify, CancellationToken ct)
    {
        if (jobIds.Count == 0)
        {
            return;
        }

        var jobs = await db.ProcessingJobs.Where(j => jobIds.Contains(j.Id)).ToListAsync(ct);
        var renders = await db.FlybyRenders.Where(r => r.ProcessingJobId != null && jobIds.Contains(r.ProcessingJobId.Value)).ToListAsync(ct);
        foreach (var render in renders)
        {
            var job = jobs.Single(j => j.Id == render.ProcessingJobId);
            render.Phase = null;
            render.Message = null;
            render.SecondsLeft = null;
            queue.Finish(render, job, notify);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DeleteEndedAsync(IReadOnlyList<long> jobIds, CancellationToken ct)
    {
        if (jobIds.Count == 0)
        {
            return;
        }

        var renders = await db.FlybyRenders.Where(r => r.ProcessingJobId != null && jobIds.Contains(r.ProcessingJobId.Value)).ToListAsync(ct);
        foreach (var render in renders)
        {
            await queue.DeleteStoredAsync(render, keepResults: false, ct);
        }
    }
}
