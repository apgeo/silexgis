// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record DataExportDto(
    Guid Id,
    AccountExportStatus Status,
    long? SizeBytes,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// A copy of the caller's own account data, built in the background and downloaded once ready.
/// </summary>
/// <remarks>
/// <para>
/// The subject is always taken from the bearer token, never from the request, and every read
/// filters on it — so another user's export id answers 404 rather than 403, and there is no code
/// path in which a caller-supplied value chooses whose data is packaged. The queued job carries
/// only the export row's id for the same reason: there is no user field in it to tamper with.
/// </para>
/// <para>
/// The archive is streamed from here, behind the bearer token, rather than being handed out as a
/// file with a delivery token in the URL — a token-in-URL is checked without re-testing who may
/// read the file, which is the wrong rule for one person's personal data.
/// </para>
/// </remarks>
public static class MeDataExportEndpoints
{
    public static RouteGroupBuilder MapMeDataExportEndpoints(this RouteGroupBuilder api)
    {
        var exports = api.MapGroup("/me/data-export").WithTags("Me");

        exports.MapPost("/", RequestAsync)
            .WithSummary("Asks for a copy of the caller's account data; built in the background.");
        exports.MapGet("/", LatestAsync)
            .WithSummary("The caller's most recent account-data export and its status.");
        exports.MapGet("/{id:guid}/download", DownloadAsync)
            .WithSummary("Downloads a ready account-data export belonging to the caller.");

        return api;
    }

    private static async Task<Results<Accepted<DataExportDto>, UnauthorizedHttpResult, ProblemHttpResult>> RequestAsync(
        IUserContextAccessor userAccessor, SilexGisDbContext db, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var pending = await db.AccountDataExports.AnyAsync(
            e => e.UserId == user.UserId
                && (e.Status == AccountExportStatus.Queued || e.Status == AccountExportStatus.Running),
            ct);
        if (pending)
        {
            return ApiProblems.Conflict("me.export_in_progress", "An export is already being prepared.");
        }

        var export = new AccountDataExport { UserId = user.UserId };
        db.AccountDataExports.Add(export);
        // Row and job in one save: a queued job whose export row never committed would fail forever.
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.AccountDataExport,
            Payload = JsonSerializer.Serialize(new AccountDataExportPayload(export.Id), JsonSerializerOptions.Web),
            RequestedBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Accepted($"/api/v1/me/data-export", ToDto(export));
    }

    private static async Task<Results<Ok<DataExportDto>, UnauthorizedHttpResult, ProblemHttpResult>> LatestAsync(
        IUserContextAccessor userAccessor, SilexGisDbContext db, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var export = await db.AccountDataExports.AsNoTracking()
            .Where(e => e.UserId == user.UserId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return export is null
            ? ApiProblems.NotFound("me.export_not_found")
            : TypedResults.Ok(ToDto(export));
    }

    private static async Task<Results<FileStreamHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid id,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        IFileStore fileStore,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var export = await db.AccountDataExports.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.UserId, ct);
        if (export is null || export.StoragePath is null || export.Status != AccountExportStatus.Ready)
        {
            return ApiProblems.NotFound("me.export_not_found");
        }

        if (export.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return ApiProblems.NotFound("me.export_expired");
        }

        Stream content;
        try
        {
            content = await fileStore.OpenReadAsync(export.StoragePath, ct);
        }
        catch (FileNotFoundException)
        {
            // The blob was swept or lost; the row alone is not a download.
            return ApiProblems.NotFound("me.export_not_found");
        }

        return TypedResults.Stream(
            content,
            "application/zip",
            $"silexgis-account-data-{export.CreatedAt:yyyy-MM-dd}.zip");
    }

    private static DataExportDto ToDto(AccountDataExport export) => new(
        export.Id,
        export.Status,
        export.SizeBytes,
        export.Error,
        export.CreatedAt,
        export.CompletedAt,
        export.ExpiresAt);
}
