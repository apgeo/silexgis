// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Queues another reading of every line-plot survey the installation holds.
/// <para>
/// An upload queues its own reading, and what that reading stored is what every later screen
/// uses. So when the reader changes — a station it names differently, a figure it now records, a
/// kind of file it can now place — the surveys already stored go on carrying the old answer, and
/// nothing would ever revisit them: a model is read when it arrives and at no other time. This is
/// what an installation runs once after such an upgrade, instead of opening every cave and asking
/// for its surveys one at a time.
/// </para>
/// <para>
/// Every line plot is taken, not only the ones an older reader read, because a model does not
/// record which version of the reader produced its rows. A reading of the same file by the same
/// reader is the same answer, so the cost of taking one that needed nothing is the time it takes
/// and no more. A survey that could not be read is taken too: a better reader is the one thing
/// that might read it. A wall mesh is left alone — it holds no stations or legs, so no change to
/// how surveys are read changes anything about it.
/// </para>
/// <para>
/// Nothing is taken away while the queue is worked through. A survey that has been read goes on
/// answering from the reading it holds — for its cave's figures, and for a watch following a party
/// on it — until its turn comes and the new rows replace the old in one transaction.
/// </para>
/// <para>
/// Safe to run as often as anyone likes. A model whose reading is already queued or running is
/// passed over, so starting the sweep twice while the first is still being worked through queues
/// nothing twice.
/// </para>
/// </summary>
public sealed class SurveyReadingBackfillHandler(SilexGisDbContext db) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.SurveyReadingBackfill;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        // Which formats are line plots is the domain's answer, the same one the cave's current
        // model is chosen by, so that a new format is classified once. Every status is listed:
        // whether a model has a reading waiting or running is for the claim below to say, on the
        // row and the queue as they stand at that moment.
        var linePlots = SurveyModelKinds.FormatsOf(SurveyModelKind.LinePlot);
        var candidates = await db.SurveyModels.AsNoTracking()
            .Where(m => linePlots.Contains(m.Format))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);

        foreach (var model in candidates)
        {
            // Nobody is named as having asked, as at a sweep of any other kind: the queue mails
            // its requester on every outcome, and a sweep over an installation's surveys is not a
            // request for one message per survey. A model that somebody asked to have read in the
            // moment since the list was taken is simply not claimed.
            await SurveyModelReading.QueueAgainAsync(db, model, requestedBy: null, ct);
        }
    }
}
