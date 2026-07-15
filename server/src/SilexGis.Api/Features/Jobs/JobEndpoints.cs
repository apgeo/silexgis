// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Jobs;

public sealed record ProcessingJobDto(
    long Id,
    string Kind,
    ProcessingJobStatus Status,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public static class JobEndpoints
{
    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{id:long}", GetAsync)
            .WithTags("Jobs")
            .WithSummary("Processing job status (requester or admin).");
        api.MapPost("/jobs/photo-geo-backfill", EnqueuePhotoGeoBackfillAsync)
            .WithTags("Jobs")
            .WithSummary("Enqueues a one-off backfill of EXIF GPS points onto existing photos (admin).");
        return api;
    }

    private static async Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> EnqueuePhotoGeoBackfillAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.IsAdmin)
        {
            return ApiProblems.Forbidden("jobs.requires_admin");
        }

        var job = new ProcessingJob
        {
            Kind = ProcessingJobKinds.PhotoGeoBackfill,
            RequestedBy = user.UserId,
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ProcessingJobDto(
            job.Id, job.Kind, job.Status, job.Error, job.CreatedAt, job.StartedAt, job.CompletedAt));
    }

    private static async Task<Results<Ok<ProcessingJobDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        long id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var job = await db.ProcessingJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        // Jobs of other users are not disclosed.
        if (job is null || (!user.IsAdmin && job.RequestedBy != user.UserId))
        {
            return ApiProblems.NotFound("job.not_found");
        }

        return TypedResults.Ok(new ProcessingJobDto(
            job.Id, job.Kind, job.Status, job.Error, job.CreatedAt, job.StartedAt, job.CompletedAt));
    }
}
