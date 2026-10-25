// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>The upload whose recordings are to be made into trips.</summary>
public sealed record TripRecordingConvertPayload(Guid ImportBatchId);

/// <summary>
/// Makes trip reports out of the phone recordings one upload wrote or changed — the recordings it
/// sent and the recordings whose scans it sent — one recording at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>One recording is one unit of work, in a context of its own.</b> The worker saves its own
/// bookkeeping on the context it hands this handler once the handler returns; a recording whose
/// conversion stopped half-way would otherwise have its staged trip carried out by that save,
/// outside any transaction. So each recording is converted in a scope that is thrown away with
/// whatever it staged, and one recording that cannot be converted costs the others nothing.
/// </para>
/// <para>
/// Not retried after a failure: a conversion that got as far as telling somebody would tell them
/// twice. A re-delivery after a restart is safe for the trips themselves — each recording is
/// claimed, and work an earlier run of the same upload did is recognised — and costs at most a
/// repeated notice for a recording left waiting.
/// </para>
/// </remarks>
public sealed class TripRecordingConvertHandler(
    SilexGisDbContext db,
    IServiceScopeFactory scopes,
    IAppSettingsService settings,
    ILogger<TripRecordingConvertHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.TripRecordingConvert;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TripRecordingConvertPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The job names no upload.");

        var lines = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == payload.ImportBatchId
                && i.UndoneAt == null
                && (i.TripRecordingId != null || i.TripRecordingPointId != null))
            .Select(i => new { i.TripRecordingId, i.TripRecordingPointId })
            .ToListAsync(ct);
        if (lines.Count == 0)
        {
            // An ordinary outcome for queued work: the upload was taken back before this ran.
            return;
        }

        var pointIds = lines.Where(l => l.TripRecordingPointId is not null).Select(l => l.TripRecordingPointId!.Value).ToList();
        var pointsOf = pointIds.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                .Where(p => pointIds.Contains(p.Id))
                .Select(p => new { p.Id, p.RecordingId })
                .ToListAsync(ct);

        var changedPoints = pointsOf
            .GroupBy(p => p.RecordingId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)[.. g.Select(p => p.Id)]);
        var recordingIds = lines.Where(l => l.TripRecordingId is not null).Select(l => l.TripRecordingId!.Value)
            .Concat(changedPoints.Keys)
            .Distinct()
            .Order()
            .ToList();

        var convertWithProblems = (await settings.GetSyncAsync(ct)).ConvertTripsWithProblems;
        var failed = 0;
        foreach (var recordingId in recordingIds)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var converter = scope.ServiceProvider.GetRequiredService<TripRecordingConverter>();
                var result = await converter.ConvertAsync(
                    recordingId,
                    payload.ImportBatchId,
                    changedPoints.GetValueOrDefault(recordingId) ?? [],
                    convertWithProblems,
                    ct);
                logger.LogInformation(
                    "Recording {RecordingId}: {Outcome} ({Positions} positions, {Unsettled} unsettled)",
                    recordingId, result.Outcome, result.Positions, result.Unsettled);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                failed++;
                logger.LogError(e, "Recording {RecordingId} could not be converted", recordingId);
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException(
                $"{failed} of {recordingIds.Count} recordings of upload {payload.ImportBatchId} could not be converted.");
        }
    }
}
