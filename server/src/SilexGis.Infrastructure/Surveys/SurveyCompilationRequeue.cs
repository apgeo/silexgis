// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Points a stored compilation record at a newer revision of the log it was read from, and queues
/// the reading of it.
///
/// <para>
/// A corrected log is a later revision of the same archived source rather than a second one, so
/// without this the figures on the record would go on describing the superseded run while the
/// archive entry beside them resolved to the corrected one — one run's provenance over another
/// run's numbers, with nothing saying so. The record is therefore reset to the state it had when it
/// was first archived: it names the new revision, it says the reading is still queued, and it
/// asserts nothing about the compilation until the new bytes have actually been read. Keeping the
/// old figures visible in the meantime would be the same conflation in a smaller window.
/// </para>
///
/// <para>
/// Lives here rather than in the route that uploads revisions because the rule belongs to survey
/// material and not to the file machinery, and because the route that archives a log in the first
/// place has to agree with it.
/// </para>
/// </summary>
public static class SurveyCompilationRequeue
{
    /// <summary>
    /// Queues a re-reading of every compilation record whose archived source is this document.
    /// Changes are left pending on <paramref name="db"/> so they commit with the caller's own save.
    /// </summary>
    /// <returns>How many records were repointed.</returns>
    public static async Task<int> ForNewRevisionAsync(
        SilexGisDbContext db,
        Guid documentId,
        Guid logFileId,
        int versionNumber,
        Guid? requestedBy,
        CancellationToken ct)
    {
        var records = await db.SurveyCompilations
            .Where(c => db.SurveySources.Any(s => s.Id == c.SurveySourceId && s.DocumentId == documentId))
            .ToListAsync(ct);
        if (records.Count == 0)
        {
            return 0;
        }

        var ids = records.Select(r => r.Id).ToList();

        // Loaded and removed rather than deleted in place: an immediate delete would commit
        // separately from the reset beside it, so a failed save would leave a record still claiming
        // figures whose loop table had already gone.
        var stale = await db.SurveyCompilationLoops
            .Where(l => ids.Contains(l.SurveyCompilationId))
            .ToListAsync(ct);
        db.SurveyCompilationLoops.RemoveRange(stale);

        foreach (var record in records)
        {
            record.LogFileId = logFileId;
            record.LogVersionNumber = versionNumber;
            record.Status = SurveyCompilationStatus.Pending;
            record.ReadError = null;
            record.ReadAt = null;
            record.Outcome = null;
            record.CompilerVersion = null;
            record.CompilerReleaseDate = null;
            record.IncompleteStage = null;
            record.CompilationSeconds = null;
            record.ErrorCount = null;
            record.WarningCount = null;
            record.LoopCount = null;
            record.AverageLoopErrorPercent = null;
            record.TotalLengthM = null;
            record.TotalLengthAdjustedM = null;

            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyCompilation,
                Payload = JsonSerializer.Serialize(
                    new SurveyCompilationPayload(record.Id), JsonSerializerOptions.Web),
                RequestedBy = requestedBy,
            });
        }

        return records.Count;
    }
}
