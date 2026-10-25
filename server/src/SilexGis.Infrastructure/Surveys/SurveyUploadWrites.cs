// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Persistence.Configurations;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>How a processing run writes to its upload's status row.</summary>
/// <remarks>
/// <para>
/// Every write is a direct update statement rather than a tracked save. The row is audited: the
/// acts worth a trail are the human ones — opening an upload, completing it, running it again —
/// and a run nudging its percentage would bury them under entries recording that a number went up.
/// A direct update is also the only way the run cannot write back fields a person changed while it
/// worked.
/// </para>
/// <para>
/// The interceptor that keeps the updated timestamp never sees these writes, so every one of them
/// sets it: a device that sees the progress move while the timestamp stands still decides the run
/// is stuck. Every text is cut to its column before it is sent, because a write that records a
/// failure must not itself be able to fail — a row left reading "running" for ever with nothing
/// saying why is the worst answer a watching phone can get.
/// </para>
/// </remarks>
public static class SurveyUploadWrites
{
    /// <summary>The width of the survey party column.</summary>
    private const int TeamLength = 1000;

    /// <summary>
    /// Marks the upload as picked up, keeping the first start time across reruns. Answers false —
    /// and writes nothing — when the upload is no longer waiting for a run: a job left over from a
    /// state the upload has since left has nothing to do.
    /// </summary>
    public static async Task<bool> StartAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var (start, _) = SurveyPipelineProgress.Band(SurveyUploadStage.Archive);
        return await db.SurveyUploads
            .Where(u => u.Id == uploadId
                && (u.Status == SurveyUploadStatus.Queued || u.Status == SurveyUploadStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, SurveyUploadStatus.Running)
                .SetProperty(u => u.Stage, SurveyUploadStage.Archive)
                .SetProperty(u => u.Progress, start)
                .SetProperty(u => u.Message, "Checking the archived files.")
                .SetProperty(u => u.ErrorCode, (string?)null)
                .SetProperty(u => u.ErrorMessage, (string?)null)
                .SetProperty(u => u.StartedAt, u => u.StartedAt ?? now)
                .SetProperty(u => u.FinishedAt, (DateTimeOffset?)null)
                .SetProperty(u => u.UpdatedAt, now), ct) > 0;
    }

    /// <summary>Where the run has got to, and what it is doing.</summary>
    public static Task ProgressAsync(
        SilexGisDbContext db, Guid uploadId, SurveyUploadStage stage, int progress, string? message, CancellationToken ct)
    {
        // Computed out here rather than in the update expression, which is translated to SQL: a
        // clamp the database performed would be a clamp nothing had applied to the value sent.
        var bounded = Math.Clamp(progress, 0, 100);
        var words = Head(message, SurveyUploadConfiguration.MessageLength);
        var now = DateTimeOffset.UtcNow;

        return db.SurveyUploads
            .Where(u => u.Id == uploadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Stage, stage)
                .SetProperty(u => u.Progress, bounded)
                .SetProperty(u => u.Message, words)
                .SetProperty(u => u.UpdatedAt, now), ct);
    }

    /// <summary>
    /// What the survey's own archive says about it — its date, its party, its declination — kept on
    /// the row for the record. Values the archive did not give are left as they were.
    /// </summary>
    public static Task RecordArchiveFactsAsync(
        SilexGisDbContext db, Guid uploadId, TopoDroidSurveyFacts facts, CancellationToken ct)
    {
        var date = facts.Date;
        var team = Head(facts.Team, TeamLength);
        var declination = facts.Declination;
        var now = DateTimeOffset.UtcNow;

        return db.SurveyUploads
            .Where(u => u.Id == uploadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.SurveyDate, u => date ?? u.SurveyDate)
                .SetProperty(u => u.Team, u => team ?? u.Team)
                .SetProperty(u => u.Declination, u => declination ?? u.Declination)
                .SetProperty(u => u.UpdatedAt, now), ct);
    }

    /// <summary>The run finished; the stage it reached is the stage it stops at.</summary>
    public static Task SucceedAsync(
        SilexGisDbContext db, Guid uploadId, SurveyUploadStage stage, string? message, CancellationToken ct)
    {
        var words = Head(message, SurveyUploadConfiguration.MessageLength);
        var now = DateTimeOffset.UtcNow;

        return db.SurveyUploads
            .Where(u => u.Id == uploadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, SurveyUploadStatus.Succeeded)
                .SetProperty(u => u.Stage, stage)
                .SetProperty(u => u.Progress, 100)
                .SetProperty(u => u.Message, words)
                .SetProperty(u => u.ErrorCode, (string?)null)
                .SetProperty(u => u.ErrorMessage, (string?)null)
                .SetProperty(u => u.FinishedAt, now)
                .SetProperty(u => u.UpdatedAt, now), ct);
    }

    /// <summary>
    /// The run stopped. The reason is a stable code and a sentence for the person who sent the
    /// survey; the progress stays where the run had got to, so the bar shows how far it came.
    /// </summary>
    public static Task FailAsync(
        SilexGisDbContext db, Guid uploadId, SurveyUploadStage stage, string code, string? message, CancellationToken ct)
    {
        var bounded = Head(code, SurveyUploadConfiguration.ErrorCodeLength);
        var words = Head(message, SurveyUploadConfiguration.ErrorMessageLength);
        var now = DateTimeOffset.UtcNow;

        return db.SurveyUploads
            .Where(u => u.Id == uploadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, SurveyUploadStatus.Failed)
                .SetProperty(u => u.Stage, stage)
                .SetProperty(u => u.Message, (string?)null)
                .SetProperty(u => u.ErrorCode, bounded)
                .SetProperty(u => u.ErrorMessage, words)
                .SetProperty(u => u.FinishedAt, now)
                .SetProperty(u => u.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Puts the last run's results away before the upload is queued again. Answers false — and
    /// writes nothing — when a run is already queued or running.
    /// </summary>
    /// <remarks>
    /// Direct rather than tracked for one more reason than the run's own writes: a tracked save
    /// records each cleared field with the value it held, and that is how a compiler's log tail —
    /// told only to the cave's writers, because station names and fixed coordinates appear in it —
    /// would be copied into the cave's history for every reader of the timeline.
    /// </remarks>
    public static async Task<bool> ClearRunAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.SurveyUploads
            .Where(u => u.Id == uploadId
                && u.Status != SurveyUploadStatus.Queued && u.Status != SurveyUploadStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Stage, (SurveyUploadStage?)null)
                .SetProperty(u => u.Progress, 0)
                .SetProperty(u => u.Message, (string?)null)
                .SetProperty(u => u.ErrorCode, (string?)null)
                .SetProperty(u => u.ErrorMessage, (string?)null)
                .SetProperty(u => u.LogTail, (string?)null)
                .SetProperty(u => u.FinishedAt, (DateTimeOffset?)null)
                .SetProperty(u => u.UpdatedAt, now), ct) > 0;
    }

    private static string? Head(string? text, int length) =>
        text is null || text.Length <= length ? text : text[..length];
}
