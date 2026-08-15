// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// How a worker writes to a build row.
/// </summary>
/// <remarks>
/// <para>
/// Every write here is a direct update statement rather than a tracked save, and that is the whole
/// point of the class. The row is audited: the acts worth a trail entry are the human ones —
/// starting a build, making one the terrain the scene draws, deleting one — and a worker nudging
/// progress every few seconds for the length of a two-hour bake would bury those under thousands
/// of entries recording that a number went up. A direct update is also the only way two writers
/// cannot overwrite each other's untouched fields.
/// </para>
/// <para>
/// The price of stepping outside the tracked path is that the interceptor which normally maintains
/// the updated timestamp never sees these writes, so every one of them sets it. A build whose
/// progress moves while its updated timestamp does not is a build a watching screen decides is
/// stuck.
/// </para>
/// <para>
/// Every value is brought inside the column's bounds before it is written — the percentage against
/// the range the database checks, the three text columns against their lengths. A write that
/// records a failure must not be able to fail: an over-long message thrown back by the database
/// while the reason for stopping is being stored leaves the row reading as running for ever, with
/// nothing anywhere saying why.
/// </para>
/// </remarks>
public static class TerrainBuildWrites
{
    private const int MessageLength = 500;
    private const int ErrorCodeLength = 100;
    private const int LogTailLength = 8000;

    /// <summary>Marks a build as picked up, keeping the first start time if it had one.</summary>
    /// <remarks>
    /// The start time is only stamped once. A build resumed after a restart started when it first
    /// started, not when the machine came back — otherwise "how long has this been going" answers
    /// with the time since the last crash, which is the one thing it must not hide.
    /// </remarks>
    public static Task StartAsync(
        SilexGisDbContext db, Guid buildId, TerrainBuildPhase phase, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, TerrainBuildStatus.Running)
                .SetProperty(b => b.Phase, phase)
                .SetProperty(b => b.StartedAt, b => b.StartedAt ?? now)
                .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>Where the build has got to, and what it is doing.</summary>
    public static Task ProgressAsync(
        SilexGisDbContext db,
        Guid buildId,
        TerrainBuildPhase phase,
        int progress,
        string? message,
        CancellationToken ct)
    {
        // Computed out here rather than inside the update expression: the expression is translated
        // to SQL, and a clamp that turned into arithmetic the database performs would be a clamp
        // nothing had actually applied to the value being sent.
        var bounded = Math.Clamp(progress, 0, 100);
        var words = Head(message, MessageLength);
        var now = DateTimeOffset.UtcNow;

        return db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Phase, phase)
                .SetProperty(b => b.Progress, bounded)
                .SetProperty(b => b.Message, words)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>The run finished. The phase it reached is the phase it stops at.</summary>
    public static Task SucceedAsync(
        SilexGisDbContext db,
        Guid buildId,
        TerrainBuildPhase phase,
        int progress,
        string? message,
        CancellationToken ct)
    {
        var bounded = Math.Clamp(progress, 0, 100);
        var words = Head(message, MessageLength);
        var now = DateTimeOffset.UtcNow;

        return db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, TerrainBuildStatus.Succeeded)
                .SetProperty(b => b.Phase, phase)
                .SetProperty(b => b.Progress, bounded)
                .SetProperty(b => b.Message, words)
                .SetProperty(b => b.ErrorCode, (string?)null)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Adds a line to what this build has said so far, keeping only the end of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build that is running has to be able to show its working. The progress message holds one
    /// sentence and is overwritten every few seconds, so without this the tail of a build in flight
    /// is empty however much its steps have said — and what somebody watching an hours-long run
    /// most wants is the last few things that happened, not the last one.
    /// </para>
    /// <para>
    /// Read, append, write, rather than a concatenation the database performs: one worker runs one
    /// build at a time, so there is no second writer to race, and doing the truncation here means
    /// the value sent is already inside the column's bounds. A write that stores what a tool said
    /// must not be able to fail on the length of it.
    /// </para>
    /// </remarks>
    public static async Task AppendLogAsync(
        SilexGisDbContext db, Guid buildId, string line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var existing = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.Id == buildId)
            .Select(b => b.LogTail)
            .FirstOrDefaultAsync(ct);

        var tail = Tail(
            string.IsNullOrEmpty(existing) ? line : existing + "\n" + line, LogTailLength);
        var now = DateTimeOffset.UtcNow;

        await db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.LogTail, tail)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>
    /// The run stopped. The reason is a short code; whatever a tool said is added to the log tail,
    /// truncated, and never goes anywhere near the queue row.
    /// </summary>
    /// <param name="logTail">
    /// What the thing that failed had to say, <b>added</b> to what this build has already said
    /// rather than written over it. Null leaves the tail exactly as it is — which is what a build
    /// stopped for having already failed needs, since the earlier failure's own words are the whole
    /// reason it is being stopped.
    /// </param>
    public static async Task FailAsync(
        SilexGisDbContext db,
        Guid buildId,
        TerrainBuildPhase phase,
        string errorCode,
        string? message,
        string? logTail,
        CancellationToken ct)
    {
        var code = Head(errorCode, ErrorCodeLength);
        var words = Head(message, MessageLength);
        var now = DateTimeOffset.UtcNow;

        var existing = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.Id == buildId)
            .Select(b => b.LogTail)
            .FirstOrDefaultAsync(ct);

        var tail = logTail is null
            ? Tail(existing, LogTailLength)
            : Tail(
                string.IsNullOrEmpty(existing) ? logTail : existing + "\n" + logTail,
                LogTailLength);

        await db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, TerrainBuildStatus.Failed)
                .SetProperty(b => b.Phase, phase)
                .SetProperty(b => b.ErrorCode, code)
                .SetProperty(b => b.Message, words)
                .SetProperty(b => b.LogTail, tail)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>The start of the text: a reason and a sentence say what they mean at the front.</summary>
    private static string? Head(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// The end of the text rather than its start, because the interesting part of a tool that has
    /// stopped badly is what it said last.
    /// </summary>
    private static string? Tail(string? value, int max) =>
        value is null || value.Length <= max ? value : value[^max..];
}
