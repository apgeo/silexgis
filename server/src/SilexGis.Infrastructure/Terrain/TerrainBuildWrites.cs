// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
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
    private const int PyramidVersionLength = 64;

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
    /// Records what a checked pyramid turned out to be: what it takes up, and the version every
    /// address into it carries.
    /// </summary>
    /// <remarks>
    /// Written only once the pyramid has passed every check, because both values are read as
    /// statements that it did. The version in particular is the string a viewer's cache is keyed on,
    /// so a build carrying one is a build something may be asked to draw.
    /// </remarks>
    public static Task RecordPyramidAsync(
        SilexGisDbContext db, Guid buildId, long sizeBytes, string version, CancellationToken ct)
    {
        var bounded = Head(version, PyramidVersionLength);
        var now = DateTimeOffset.UtcNow;

        return db.TerrainBuilds
            .Where(b => b.Id == buildId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.SizeBytes, Math.Max(sizeBytes, 0))
                .SetProperty(b => b.PyramidVersion, bounded)
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

    /// <summary>
    /// Draws a build that has just finished, unless somebody chose what is drawn now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one write here that is not a direct update statement, deliberately: moving which build
    /// the scene draws is one of the acts the row's audit trail exists to record, and it is the
    /// same act the button performs. It therefore goes through the tracked path and takes the same
    /// advisory lock, so a build finishing and an operator pressing the button at the same instant
    /// queue behind one another instead of racing for a unique index that permits one winner.
    /// </para>
    /// <para>
    /// The mark is let go and taken in two saves rather than one. Within a single save the order
    /// of the two updates is the change tracker's to choose, and the order where the new holder is
    /// written first is the order the unique index refuses. This mirrors what the endpoint does,
    /// for the same reason.
    /// </para>
    /// <para>
    /// Returns whether the scene changed, which is what the caller has to log: an unremarkable
    /// "did not take over" is the ordinary outcome on an installation whose operator picks terrain
    /// by hand, and is not a failure of anything.
    /// </para>
    /// </remarks>
    public static async Task<bool> DrawIfNothingWasChosenAsync(
        SilexGisDbContext db, Guid buildId, bool hasPublishedPyramid, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TerrainBuildSql.TakeActivationLockAsync(db, ct);

        var build = await db.TerrainBuilds.FirstOrDefaultAsync(b => b.Id == buildId, ct);
        if (build is null || build.Status != TerrainBuildStatus.Succeeded)
        {
            return false;
        }

        // Both halves of "drawable" are asked here rather than trusted from the run: the version is
        // written by the check that read the pyramid back and found it whole, and the pyramid on
        // disk is what a browser will actually ask for.
        var drawable = hasPublishedPyramid && !string.IsNullOrWhiteSpace(build.PyramidVersion);

        var drawn = await db.TerrainBuilds.FirstOrDefaultAsync(b => b.IsActive, ct);
        if (!TerrainActivationRules.MayDrawAutomatically(
                drawable, build.IsActive, drawn?.ActivationWasAutomatic))
        {
            return false;
        }

        if (drawn is not null)
        {
            drawn.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        build.IsActive = true;
        build.ActivationWasAutomatic = true;
        build.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
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
