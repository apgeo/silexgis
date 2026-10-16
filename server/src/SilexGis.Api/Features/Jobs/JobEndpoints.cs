// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Jobs;

/// <summary>
/// One job as the screen waiting on it sees it.
/// </summary>
/// <param name="Error">
/// Why it failed — or, on a job queued again after a passing failure, why the last attempt did not
/// finish, which a screen shows beside "trying again".
/// </param>
/// <param name="Attempts">How many times it has been taken, the one under way included.</param>
/// <param name="NotBefore">When a job put back after a passing failure will be taken again.</param>
/// <param name="ProgressDone">How many units of its work a running job has done, where its kind says.</param>
/// <param name="ProgressTotal">How many it has in all.</param>
public sealed record ProcessingJobDto(
    long Id,
    string Kind,
    ProcessingJobStatus Status,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Attempts,
    DateTimeOffset? NotBefore,
    int? ProgressDone,
    int? ProgressTotal)
{
    public static ProcessingJobDto From(ProcessingJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new(
            job.Id, job.Kind, job.Status, job.Error, job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.Attempts, job.NotBefore, job.ProgressDone, job.ProgressTotal);
    }
}

public static class JobEndpoints
{
    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{id:long}", GetAsync)
            .WithTags("Jobs")
            .WithSummary("Processing job status (the requester, or Read on the Jobs domain).");
        api.MapPost("/jobs/photo-geo-backfill", EnqueuePhotoGeoBackfillAsync)
            .WithTags("Jobs")
            .WithSummary("Enqueues a one-off backfill of EXIF GPS points onto existing photos; requires Execute on the Jobs domain.");
        api.MapPost("/jobs/text-extraction-backfill", EnqueueTextExtractionBackfillAsync)
            .WithTags("Jobs")
            .WithSummary("Enqueues a sweep that reads the text of stored files nothing has read, or that a newer reader should read again; requires Execute on the Jobs domain.");
        api.MapPost("/jobs/document-conversion-backfill", EnqueueDocumentConversionBackfillAsync)
            .WithTags("Jobs")
            .WithSummary("Enqueues a sweep that makes a readable copy of every office document that has none yet; requires Execute on the Jobs domain.");
        api.MapPost("/jobs/survey-reading-backfill", EnqueueSurveyReadingBackfillAsync)
            .WithTags("Jobs")
            .WithSummary("Enqueues a sweep that reads every line-plot survey again, so that surveys read by an earlier version get what the present reader produces; requires Execute on the Jobs domain.");
        return api;
    }

    private static Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueuePhotoGeoBackfillAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
        => EnqueueAsync(ProcessingJobKinds.PhotoGeoBackfill, db, accessAccessor, ct);

    private static Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueueTextExtractionBackfillAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
        => EnqueueAsync(ProcessingJobKinds.TextExtractionBackfill, db, accessAccessor, ct);

    private static Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueueDocumentConversionBackfillAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
        => EnqueueAsync(ProcessingJobKinds.DocumentConversionBackfill, db, accessAccessor, ct);

    private static Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueueSurveyReadingBackfillAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
        => EnqueueAsync(ProcessingJobKinds.SurveyReadingBackfill, db, accessAccessor, ct);

    /// <summary>
    /// Queues a maintenance sweep. Each takes no input of its own — the work it does is a
    /// property of the installation's own rows — so the only thing that varies is which one.
    /// </summary>
    private static async Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueueAsync(
        string kind,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Jobs, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var job = new ProcessingJob
        {
            Kind = kind,
            RequestedBy = ctx.UserId,
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ProcessingJobDto.From(job));
    }

    private static async Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        long id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var job = await db.ProcessingJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        // Jobs of other users are not disclosed: a caller without Read over the domain gets the
        // same answer for someone else's job as for one that never existed.
        if (job is null
            || (!AccessEvaluator.Decide(ctx, AccessDomain.Jobs, AccessAction.Read, null).Allowed
                && job.RequestedBy != ctx.UserId))
        {
            return ApiProblems.NotFound("job.not_found");
        }

        return TypedResults.Ok(ProcessingJobDto.From(job));
    }
}
