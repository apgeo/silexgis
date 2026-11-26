// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Queues another reading of every Survex survey whose stored legs carry no survey date.
/// <para>
/// A Survex <c>.3d</c> file may say on which day each leg was surveyed, and a reading keeps that
/// day on the leg. Surveys read before the day was kept hold their legs without it, and nothing
/// would ever revisit them: a model is read when it arrives and at no other time. This is the
/// sweep an installation runs once after that upgrade. It is narrower than the sweep over every
/// line plot on purpose: a Therion <c>.lox</c> file has no dates to gain, and a Survex survey one
/// of whose legs already has a date was read by a reader that keeps them.
/// </para>
/// <para>
/// A survey is taken when it has been read — it holds legs — and not one of them has a date. A
/// file that gives none of its legs a date is read again every time the sweep runs and gains
/// nothing: the stored rows cannot tell a file without dates from a reading that did not keep
/// them, and reading the same file again gives the same answer. A survey that could not be read
/// is left to the sweep over every line plot, which is the one that retries those.
/// </para>
/// <para>
/// What a re-reading does, and that nothing is out of use while it waits and runs, is the
/// reading's own: see <see cref="SurveyModelReading"/>. A model whose reading is already queued or
/// running is passed over, so starting the sweep twice queues nothing twice.
/// </para>
/// </summary>
public sealed class SurveyLegDatesBackfillHandler(SilexGisDbContext db) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.SurveyLegDatesBackfill;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var candidates = await db.SurveyModels.AsNoTracking()
            .Where(m => m.Format == SurveyModelFormat.Survex3d
                && m.Status == SurveyModelStatus.Ready
                && db.SurveyShots.Any(s => s.SurveyModelId == m.Id)
                && !db.SurveyShots.Any(s => s.SurveyModelId == m.Id && s.SurveyedOn != null))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);

        foreach (var model in candidates)
        {
            // Nobody is named as having asked: a sweep is not a request for one message a survey.
            await SurveyModelReading.QueueAgainAsync(db, model, requestedBy: null, ct);
        }
    }
}
