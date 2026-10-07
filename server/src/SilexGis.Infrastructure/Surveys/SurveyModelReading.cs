// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>How a request for another reading of a stored survey model was answered.</summary>
public enum SurveyReadingRequest
{
    /// <summary>A reading was queued.</summary>
    Queued,

    /// <summary>Nothing was queued: a reading of the model is already waiting or running.</summary>
    AlreadyInProgress,
}

/// <summary>
/// Queues the work that turns a stored survey file into what the application draws and measures.
/// This is the only code that decides which job a survey model's format needs.
/// </summary>
/// <remarks>
/// <para>
/// A survey model is read when it arrives, and may be read again later: by somebody who asks for
/// it on one model, or by the sweep that asks for it on every line plot of the installation. All
/// three have to queue the same job with the same payload, because the reading takes everything
/// else it needs off the model's own row — and a second place that mapped formats to jobs would be
/// the place a new format was queued one way on arrival and another way on a re-reading.
/// </para>
/// <para>
/// <b>A model that has been read stays ready while it is read again.</b> Everything that uses a
/// survey — the figures a cave is described by, the drawing a watch follows a party on, the choice
/// of the cave's current model — takes "ready" to mean "holds a reading" and anything else to mean
/// "holds none". Both readings are true of a model that has never been read. Neither would be true
/// of a finished model sent back to waiting: its rows are all still there, and stay there until
/// the next reading replaces them in one transaction. So such a model keeps saying it is ready,
/// and that another reading is on its way is told by the job that is queued for it. Only a model
/// that holds nothing — one whose reading failed — goes back to waiting, because waiting is what
/// is true of it.
/// </para>
/// <para>
/// Reading the same file again is safe because both readings replace what the previous one
/// produced. Station and leg rows are deleted and rewritten in one transaction, so nobody ever
/// reads half a survey. The placement is a fixed point as well: a reading writes back the anchor
/// it used, and that anchor is exactly what the next reading is handed.
/// </para>
/// </remarks>
public static class SurveyModelReading
{
    private static readonly string[] ReadingKinds = [ProcessingJobKinds.SurveyGraph, ProcessingJobKinds.SurveyMesh];

    /// <summary>
    /// The models a reading is queued or running for, as the job queue has it.
    /// </summary>
    /// <remarks>
    /// Asked of the queue and not of the models' own status, because a model that is being read
    /// again goes on saying it is ready. The payloads are read here rather than compared in the
    /// database: the jobs outstanding at any moment are few, and what a payload names is this
    /// type's knowledge and nobody else's.
    /// </remarks>
    public static async Task<IReadOnlySet<Guid>> OutstandingAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var payloads = await db.ProcessingJobs.AsNoTracking()
            .Where(j => ReadingKinds.Contains(j.Kind)
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running))
            .Select(j => j.Payload)
            .ToListAsync(ct);

        // Both kinds carry the model's id under the same name, so one shape reads either.
        return payloads
            .Select(p => JsonSerializer.Deserialize<SurveyGraphPayload>(p, JsonSerializerOptions.Web))
            .Where(p => p is not null)
            .Select(p => p!.SurveyModelId)
            .ToHashSet();
    }

    /// <summary>
    /// Marks a model that has just been added as waiting and queues its first reading. The model
    /// is tracked and not yet saved; the caller's save writes the row and the job together, so a
    /// model is never stored saying work is coming with nothing queued to do it.
    /// </summary>
    public static void QueueFirst(SilexGisDbContext db, SurveyModel model, Guid? requestedBy)
    {
        model.Status = SurveyModelStatus.Pending;
        model.ProcessingError = null;
        db.ProcessingJobs.Add(JobFor(model, requestedBy));
    }

    /// <summary>
    /// Queues another reading of a model that has been read, or that could not be. Changes nothing
    /// when a reading of it is already queued or running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is claimed by a statement on its row rather than by testing the status that was
    /// loaded: two requests arriving together would both have loaded a finished model, and the
    /// second job would read the file a second time for nothing. The claim and the job commit
    /// together, so there is no moment at which a reading has been promised and nothing is queued.
    /// </para>
    /// <para>
    /// A model that failed is claimed by taking it back to waiting, which only one request can do.
    /// A model that is ready stays ready, so there is no status to take: its row is written to
    /// without being changed, which holds the row against a second request until this one has
    /// committed, and the queue is then asked whether a reading is already outstanding. The second
    /// request gets the row only after the first one's job is there to be found.
    /// </para>
    /// </remarks>
    /// <param name="requestedBy">
    /// Who is told how the reading ended; null for a reading nobody asked for by name, because the
    /// queue mails its requester on every outcome.
    /// </param>
    public static async Task<SurveyReadingRequest> QueueAgainAsync(
        SilexGisDbContext db, SurveyModel model, Guid? requestedBy, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var retried = await db.SurveyModels
            .Where(m => m.Id == model.Id && m.Status == SurveyModelStatus.Failed)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, SurveyModelStatus.Pending)
                    .SetProperty(m => m.ProcessingError, (string?)null),
                ct);
        if (retried == 1)
        {
            // The caller's copy says what the row now says, so that what it answers with is the
            // model as it stands. Where the copy is tracked this also writes the two columns again
            // with the values they already hold, which costs one statement and changes nothing.
            model.Status = SurveyModelStatus.Pending;
            model.ProcessingError = null;
        }
        else
        {
            // Ready: the row is held, not changed. A ready model carries no complaint, so writing
            // none onto it is the statement that takes the lock and alters nothing.
            var held = await db.SurveyModels
                .Where(m => m.Id == model.Id && m.Status == SurveyModelStatus.Ready)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessingError, (string?)null), ct);
            if (held == 0 || (await OutstandingAsync(db, ct)).Contains(model.Id))
            {
                // Waiting or running for the first time, or ready with a reading already queued.
                return SurveyReadingRequest.AlreadyInProgress;
            }

            model.Status = SurveyModelStatus.Ready;
            model.ProcessingError = null;
        }

        db.ProcessingJobs.Add(JobFor(model, requestedBy));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return SurveyReadingRequest.Queued;
    }

    /// <summary>
    /// The job a model's format needs: a wall mesh is converted into the file the 3D scene draws,
    /// a line plot is read into station and leg rows. Which of the two a format is, is the
    /// domain's answer and is not restated here.
    /// </summary>
    private static ProcessingJob JobFor(SurveyModel model, Guid? requestedBy) =>
        SurveyModelKinds.Of(model.Format) == SurveyModelKind.LinePlot
            ? new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyGraph,
                Payload = JsonSerializer.Serialize(new SurveyGraphPayload(model.Id), JsonSerializerOptions.Web),
                RequestedBy = requestedBy,
            }
            : new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyMesh,
                Payload = JsonSerializer.Serialize(new SurveyMeshPayload(model.Id), JsonSerializerOptions.Web),
                RequestedBy = requestedBy,
            };
}
