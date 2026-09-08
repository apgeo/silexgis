// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.SurveyCompilation"/> jobs.</summary>
public sealed record SurveyCompilationPayload(Guid SurveyCompilationId);

/// <summary>
/// Reads an archived compilation log into the quality figures it reports.
///
/// <para>
/// Off the request thread for the same reason every other reading of an uploaded file is: the
/// upload is capped at a hundred megabytes and the whole of it is scanned, which is fine for a
/// worker and not for somebody waiting on an upload to come back.
/// </para>
///
/// <para>
/// A reading replaces the reading before it rather than joining it. The loop rows are deleted
/// before the new ones are written, in the same transaction: appended rows would double every loop
/// count with nothing failing, and the row's own provenance columns would then describe one run
/// while the table under it held two.
/// </para>
/// </summary>
public sealed class SurveyCompilationHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    SurveyCompilationLogReader reader) : IProcessingJobHandler
{
    /// <summary>
    /// Widths the columns hold. Everything measured here came out of a file somebody uploaded, so a
    /// value that will not fit is shortened rather than allowed to fail the save — losing the whole
    /// reading of a log because its version string was long would be the wrong trade.
    /// </summary>
    private const int MaxVersionLength = 200;

    private const int MaxReleaseDateLength = 100;

    private const int MaxStageLength = 200;

    private const int MaxErrorLength = 1000;

    /// <summary>
    /// What an uploader is told when the failure was not about their file. Held in one place
    /// because it is both stored on the record and carried by the exception that leaves here.
    /// </summary>
    private const string UnreadableReason = "The compilation log could not be read.";

    public string Kind => ProcessingJobKinds.SurveyCompilation;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SurveyCompilationPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty survey-compilation payload.");

        var record = await db.SurveyCompilations
            .FirstOrDefaultAsync(c => c.Id == payload.SurveyCompilationId, ct)
            ?? throw new InvalidOperationException(
                $"Survey compilation {payload.SurveyCompilationId} no longer exists.");

        var upload = await db.StoredFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == record.LogFileId, ct)
            ?? throw new InvalidOperationException($"Stored file {record.LogFileId} no longer exists.");

        try
        {
            var text = await TextAsync(upload, ct);
            var report = reader.Read(upload.OriginalName, text);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Whatever an earlier reading of this log left behind goes first. Kept in the same
            // transaction as the rows that replace it so that a reading is never half of one run
            // and half of another.
            await db.SurveyCompilationLoops
                .Where(l => l.SurveyCompilationId == record.Id)
                .ExecuteDeleteAsync(ct);

            db.SurveyCompilationLoops.AddRange(report.LoopErrors.Select(e => new SurveyCompilationLoop
            {
                SurveyCompilationId = record.Id,
                Ordinal = e.Ordinal,
                RelativeErrorPercent = e.RelativeErrorPercent,
                AbsoluteErrorM = e.AbsoluteErrorM,
                TotalLengthM = e.TotalLengthM,
                StationCount = e.StationCount,
                ErrorXM = e.ErrorXM,
                ErrorYM = e.ErrorYM,
                ErrorZM = e.ErrorZM,
                Stations = e.Stations,
            }));

            record.Status = SurveyCompilationStatus.Read;
            record.ReadError = null;
            record.ReadAt = DateTimeOffset.UtcNow;
            record.Outcome = report.Outcome;
            record.CompilerVersion = Clipped(report.CompilerVersion, MaxVersionLength);
            record.CompilerReleaseDate = Clipped(report.CompilerReleaseDate, MaxReleaseDateLength);
            record.IncompleteStage = Clipped(report.IncompleteStage, MaxStageLength);
            record.CompilationSeconds = report.CompilationSeconds;
            record.ErrorCount = report.ErrorCount;
            record.WarningCount = report.WarningCount;
            record.LoopCount = report.LoopCount;
            record.AverageLoopErrorPercent = report.AverageLoopErrorPercent;
            record.TotalLengthM = report.TotalLengthM;
            record.TotalLengthAdjustedM = report.TotalLengthAdjustedM;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e)
        {
            // A file that is not a compilation log is nearly always something the uploader can do
            // something about, so that reason is kept. Anything else is ours and is not described
            // to them.
            var reason = e is SurveySourceException ? e.Message : UnreadableReason;
            await RecordFailureAsync(record.Id, reason);

            // Rethrown carrying the same reason the record carries, never the original message:
            // the failure travels on to the job's requester in a notification, so an internal
            // message here — a storage path, a database constraint — would be handed to the
            // uploader by the very layer that just took care not to store it. The original is
            // kept as the inner exception, so the log still has all of it.
            if (e is SurveySourceException)
            {
                throw;
            }

            throw new InvalidOperationException(reason, e);
        }
    }

    /// <summary>
    /// The archived bytes as text. Read whole because the parser works over the entire log, and the
    /// upload limit is what bounds how much that costs. Decoded as UTF-8 with the byte-order mark
    /// honoured when there is one: the parser matches on ASCII keywords, so a log written in some
    /// other single-byte encoding still reads, and only its station names carry the substitutions.
    /// </summary>
    private async Task<string> TextAsync(StoredFile upload, CancellationToken ct)
    {
        await using var input = await fileStore.OpenReadAsync(upload.StoragePath, ct);
        using var text = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await text.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Records the log as unreadable, on a change tracker cleared of the reading that failed.
    ///
    /// <para>
    /// Written the way the other survey readers write theirs, and for the same reason: a save that
    /// fails leaves its entities pending, so writing the failure through the same tracker would
    /// re-send the identical failing statements and throw again. The record would then never be
    /// marked at all — it would sit as still being read for ever, be picked up on every retry, and
    /// be polled by whoever uploaded it for just as long.
    /// </para>
    /// </summary>
    private async Task RecordFailureAsync(Guid compilationId, string reason)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }

        var record = await db.SurveyCompilations
            .FirstOrDefaultAsync(c => c.Id == compilationId, CancellationToken.None);
        if (record is null)
        {
            return;
        }

        record.Status = SurveyCompilationStatus.Unreadable;
        record.ReadError = Clipped(reason, MaxErrorLength);

        // The figures of the reading before this one are not left standing under a row that now
        // says it could not read anything: they described a run this record no longer claims.
        // Every column the reading writes is cleared, not only the ones that name an outcome —
        // a row saying the log could not be read must assert nothing at all about the run, and a
        // surviving loop count or average would be read as though the compiler had said it.
        record.Outcome = null;
        record.IncompleteStage = null;
        record.ReadAt = null;
        record.CompilerVersion = null;
        record.CompilerReleaseDate = null;
        record.CompilationSeconds = null;
        record.ErrorCount = null;
        record.WarningCount = null;
        record.LoopCount = null;
        record.AverageLoopErrorPercent = null;
        record.TotalLengthM = null;
        record.TotalLengthAdjustedM = null;
        await db.SurveyCompilationLoops
            .Where(l => l.SurveyCompilationId == compilationId)
            .ExecuteDeleteAsync(CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string? Clipped(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
