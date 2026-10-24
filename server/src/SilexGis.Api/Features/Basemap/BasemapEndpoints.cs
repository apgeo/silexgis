// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Basemap;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Basemap;

/// <summary>A box of the world in degrees.</summary>
public sealed record BasemapRegionDto(double West, double South, double East, double North);

/// <summary>A region offered by name: <c>romania</c>, or <c>apuseni</c> for a karst region.</summary>
public sealed record BasemapPresetDto(string Key, BasemapRegionDto Region);

/// <summary>The extract being served now.</summary>
/// <param name="SourceId">The name its tiles are served under — the part of their address that changes with every extract.</param>
/// <param name="Region">The box it was cut to; null for a file put in the directory by hand.</param>
/// <param name="MaxZoom">The last level it holds; null for a file put in the directory by hand.</param>
public sealed record BasemapFileDto(
    string SourceId, string Build, DateTimeOffset ExtractedAt, long SizeBytes, BasemapRegionDto? Region, int? MaxZoom);

/// <summary>Where a download stands.</summary>
public enum BasemapDownloadState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>One download of a region, the latest one or the one just asked for.</summary>
/// <param name="Percent">How far it has got, 0 to 100; 100 only once the extract is served.</param>
/// <param name="Build">The build it is cut from; null while "the latest" has not been looked up yet.</param>
/// <param name="ErrorCode">Why it failed, as a stable code; null unless it failed.</param>
/// <param name="Error">The same in words, for the administrator.</param>
public sealed record BasemapDownloadDto(
    long Id,
    BasemapDownloadState State,
    int Percent,
    BasemapRegionDto Region,
    int MaxZoom,
    string? Build,
    string? ErrorCode,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Everything the administration page shows about the installation's own basemap.</summary>
/// <param name="SelfHosted">Whether the installation serves a basemap of its own at all — its configuration says.</param>
/// <param name="Current">The extract served now, or null while there is none.</param>
/// <param name="Download">The latest download, or null when none was ever asked for.</param>
/// <param name="Entries">The catalogue's entries drawn from the installation's own basemap, offered once there is an extract.</param>
/// <param name="MaxZoom">The deepest level a download may ask for.</param>
/// <param name="MaxBytes">The largest extract the installation accepts.</param>
/// <param name="Source">The address regions are cut from, with <c>{build}</c> where the date goes.</param>
public sealed record BasemapStatusDto(
    bool SelfHosted,
    BasemapFileDto? Current,
    BasemapDownloadDto? Download,
    IReadOnlyList<string> Entries,
    IReadOnlyList<BasemapPresetDto> Presets,
    int MaxZoom,
    long MaxBytes,
    string Source);

/// <summary>A planet build the source publishes.</summary>
public sealed record BasemapBuildDto(string Build, long SizeBytes, DateTimeOffset? Uploaded, string? Version);

/// <summary>What the size of an extract would be, and whether it would be accepted.</summary>
/// <param name="RefusalCode">Why a download of it would be refused — too large, or no room on the disk — or null.</param>
public sealed record BasemapEstimateDto(string Build, long Bytes, long? FreeBytes, string? RefusalCode);

/// <summary>A region to download or to weigh: a box, its last level, and a build — none for the latest.</summary>
public sealed record BasemapRegionRequest(BasemapRegionDto? Region, int? MaxZoom, string? Build);

public sealed class BasemapRegionRequestValidator : AbstractValidator<BasemapRegionRequest>
{
    public BasemapRegionRequestValidator()
    {
        RuleFor(x => x.Region).NotNull();
        RuleFor(x => x.Region!)
            .Must(r => SelfHostedBasemap.RegionProblem(Domain(r)) is null)
            .WithErrorCode(SelfHostedBasemap.Codes.RegionInvalid)
            .WithMessage(x => SelfHostedBasemap.RegionProblem(Domain(x.Region!)) ?? string.Empty)
            .When(x => x.Region is not null);
        RuleFor(x => x.MaxZoom)
            .NotNull()
            .Must(z => z is { } zoom && SelfHostedBasemap.IsZoom(zoom))
            .WithErrorCode(SelfHostedBasemap.Codes.ZoomInvalid)
            .WithMessage($"The last level is from 0 to {SelfHostedBasemap.MaxZoom}.");
        RuleFor(x => x.Build)
            .Must(SelfHostedBasemap.IsBuild)
            .WithErrorCode(SelfHostedBasemap.Codes.BuildInvalid)
            .WithMessage("A build is written as its date, yyyyMMdd.")
            .When(x => x.Build is not null);
    }

    internal static BasemapRegion Domain(BasemapRegionDto region) =>
        new(region.West, region.South, region.East, region.North);
}

/// <summary>
/// The installation's own basemap, as an administrator manages it: what is served, the builds a
/// region can be cut from, what a region would weigh, and the download that cuts one.
/// </summary>
/// <remarks>
/// <para>
/// Governed like the installation's other settings — Read to look, Write to download or call a
/// download off — because this is the installation's choice of what every map may draw under it.
/// Each download asked for, and each called off, is written to the audit trail with who did it.
/// </para>
/// <para>
/// Nothing here takes an address. A region is cut from the source the installation's configuration
/// names, at a build chosen from the dates it publishes; the box and the level are numbers.
/// </para>
/// </remarks>
public static class BasemapEndpoints
{
    private const string AuditType = "Basemap";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapBasemapEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/basemap").WithTags("Admin");

        admin.MapGet("/", StatusAsync)
            .WithSummary("The installation's own basemap: whether it is served, the extract served now and the latest download.");
        admin.MapGet("/builds", BuildsAsync)
            .WithSummary("The planet builds the configured source publishes, newest first.");
        admin.MapPost("/estimate", EstimateAsync)
            .WithValidation<BasemapRegionRequest>()
            .WithSummary("What an extract of a region would weigh, and whether it would be accepted. Downloads nothing.");
        admin.MapPost("/downloads", DownloadAsync)
            .WithValidation<BasemapRegionRequest>()
            .WithSummary("Downloads a region as the installation's basemap, in the background; one at a time.");
        admin.MapPost("/downloads/{id:long}/cancel", CancelAsync)
            .WithSummary("Calls a download off. The extract being served stays as it was.");

        return api;
    }

    private static async Task<Results<Ok<BasemapStatusDto>, UnauthorizedHttpResult, ProblemHttpResult>> StatusAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        BasemapStore store,
        IOptions<BasemapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (Refusal(ctx, AccessAction.Read) is { } refusal)
        {
            return refusal;
        }

        var latest = await Downloads(db).OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);
        var entries = (await db.MapLayers.AsNoTracking().Where(l => l.Enabled).OrderBy(l => l.SortOrder)
                .Select(l => new { l.Name, l.UrlTemplate }).ToListAsync(ct))
            .Where(l => SelfHostedBasemap.IsSelfHosted(l.UrlTemplate))
            .Select(l => l.Name)
            .ToList();
        var current = store.Current();
        return TypedResults.Ok(new BasemapStatusDto(
            store.SelfHosted,
            current is null ? null : ToDto(current),
            latest is null ? null : ToDto(latest),
            entries,
            [.. SelfHostedBasemap.Presets.Select(p => new BasemapPresetDto(p.Key, ToDto(p.Value)))],
            SelfHostedBasemap.MaxZoom,
            options.Value.MaxBytes,
            options.Value.SourceUrlTemplate));
    }

    private static async Task<Results<Ok<List<BasemapBuildDto>>, UnauthorizedHttpResult, ProblemHttpResult>> BuildsAsync(
        IAccessContextAccessor accessAccessor, IBasemapBuilds builds, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (Refusal(ctx, AccessAction.Read) is { } refusal)
        {
            return refusal;
        }

        try
        {
            return TypedResults.Ok((await builds.ListAsync(ct))
                .Select(b => new BasemapBuildDto(b.Build, b.SizeBytes, b.Uploaded, b.Version))
                .ToList());
        }
        catch (BasemapException e)
        {
            return ApiProblems.ServiceUnavailable(e.Code, "The list of builds could not be read from the source.");
        }
    }

    private static async Task<Results<Ok<BasemapEstimateDto>, UnauthorizedHttpResult, ProblemHttpResult>> EstimateAsync(
        BasemapRegionRequest request,
        IAccessContextAccessor accessAccessor,
        BasemapStore store,
        IBasemapExtractor extractor,
        IBasemapBuilds builds,
        IOptions<BasemapOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (Refusal(ctx, AccessAction.Write) is { } refusal)
        {
            return refusal;
        }

        if (!store.SelfHosted)
        {
            return SelfHostingOff();
        }

        try
        {
            var build = request.Build ?? SelfHostedBasemap.Latest((await builds.ListAsync(ct)).Select(b => b.Build));
            if (build is null)
            {
                return ApiProblems.ServiceUnavailable(SelfHostedBasemap.Codes.BuildNotFound, "The source lists no build.");
            }

            var source = options.Value.SourceUrlTemplate.Replace("{build}", build, StringComparison.Ordinal);
            var bytes = await extractor.EstimateAsync(
                source, BasemapRegionRequestValidator.Domain(request.Region!), request.MaxZoom!.Value, ct);
            var free = store.FreeBytes();
            var refusalCode = bytes > options.Value.MaxBytes
                ? SelfHostedBasemap.Codes.TooLarge
                : free is { } room && room < bytes
                    ? SelfHostedBasemap.Codes.DiskFull
                    : null;
            return TypedResults.Ok(new BasemapEstimateDto(build, bytes, free, refusalCode));
        }
        catch (BasemapException e)
        {
            return ApiProblems.ServiceUnavailable(e.Code, "The size could not be worked out from the source.");
        }
    }

    private static async Task<Results<Accepted<BasemapDownloadDto>, UnauthorizedHttpResult, ProblemHttpResult>> DownloadAsync(
        BasemapRegionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        BasemapStore store,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (Refusal(ctx, AccessAction.Write) is { } refusal)
        {
            return refusal;
        }

        if (!store.SelfHosted)
        {
            return SelfHostingOff();
        }

        if (await Downloads(db).AnyAsync(j => j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running, ct))
        {
            return ApiProblems.Conflict(
                SelfHostedBasemap.Codes.DownloadRunning,
                "A download is already queued or running; one at a time.");
        }

        var region = request.Region!;
        var payload = new BasemapExtractPayload(region.West, region.South, region.East, region.North, request.MaxZoom!.Value, request.Build);
        var job = new ProcessingJob
        {
            Kind = ProcessingJobKinds.BasemapExtract,
            Payload = JsonSerializer.Serialize(payload, Json),
            RequestedBy = ctx.UserId,
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = ctx.UserId,
            Action = AuditActions.Created,
            EntityType = AuditType,
            EntityId = job.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Changes = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, object?>>
            {
                ["download"] = new() { ["old"] = null, ["new"] = payload },
            }, Json),
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Accepted($"/api/v1/admin/basemap/downloads/{job.Id}", ToDto(job));
    }

    private static async Task<Results<Ok<BasemapDownloadDto>, UnauthorizedHttpResult, ProblemHttpResult>> CancelAsync(
        long id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        BasemapCancellation cancellation,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (Refusal(ctx, AccessAction.Write) is { } refusal)
        {
            return refusal;
        }

        var job = await Downloads(db).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return ApiProblems.NotFound(SelfHostedBasemap.Codes.NotFound);
        }

        var before = StateOf(job);
        if (job.Status == ProcessingJobStatus.Queued)
        {
            // Not taken yet: it is marked ended, which the worker's claim never picks up.
            var ended = await db.ProcessingJobs
                .Where(j => j.Id == id && j.Status == ProcessingJobStatus.Queued)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, ProcessingJobStatus.Failed)
                        .SetProperty(j => j.Error, $"{SelfHostedBasemap.Codes.Cancelled}: the download was called off before it started")
                        .SetProperty(j => j.CompletedAt, clock.GetUtcNow()),
                    ct);
            if (ended == 0)
            {
                // Taken in the meantime: call it off where it runs.
                cancellation.Cancel(id);
            }
        }
        else if (job.Status == ProcessingJobStatus.Running)
        {
            cancellation.Cancel(id);
        }

        await db.Entry(job).ReloadAsync(ct);
        if (before is BasemapDownloadState.Queued or BasemapDownloadState.Running)
        {
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = ctx.UserId,
                Action = AuditActions.Updated,
                EntityType = AuditType,
                EntityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Changes = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, object?>>
                {
                    ["state"] = new() { ["old"] = Name(before), ["new"] = Name(BasemapDownloadState.Cancelled) },
                }, Json),
            });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToDto(job));
    }

    private static IQueryable<ProcessingJob> Downloads(SilexGisDbContext db) =>
        db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.BasemapExtract);

    private static ProblemHttpResult? Refusal(AccessContext ctx, AccessAction action) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Settings, action, null).Allowed
            ? null
            : ApiProblems.Forbidden("access.forbidden");

    private static ProblemHttpResult SelfHostingOff() =>
        ApiProblems.Conflict(
            SelfHostedBasemap.Codes.SelfHostingOff,
            "This installation does not serve a basemap of its own (SILEXGIS__Basemap__SelfHosted is off).");

    private static BasemapDownloadState StateOf(ProcessingJob job) => job.Status switch
    {
        ProcessingJobStatus.Queued => BasemapDownloadState.Queued,
        ProcessingJobStatus.Running => BasemapDownloadState.Running,
        ProcessingJobStatus.Succeeded => BasemapDownloadState.Succeeded,
        _ when ErrorCode(job) == SelfHostedBasemap.Codes.Cancelled => BasemapDownloadState.Cancelled,
        _ => BasemapDownloadState.Failed,
    };

    /// <summary>The stable code a failed download recorded in front of its words, when it recorded one.</summary>
    private static string? ErrorCode(ProcessingJob job)
    {
        if (job.Error is not { } error || !error.StartsWith("basemap.", StringComparison.Ordinal))
        {
            return job.Error is null ? null : SelfHostedBasemap.Codes.ExtractFailed;
        }

        var colon = error.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? error[..colon] : error;
    }

    private static string Name(BasemapDownloadState state) => JsonNamingPolicy.CamelCase.ConvertName(state.ToString());

    private static BasemapDownloadDto ToDto(ProcessingJob job)
    {
        var payload = JsonSerializer.Deserialize<BasemapExtractPayload>(job.Payload, Json)
            ?? new BasemapExtractPayload(0, 0, 0, 0, 0, null);
        var state = StateOf(job);
        var percent = state == BasemapDownloadState.Succeeded
            ? 100
            : job.ProgressTotal is > 0 && job.ProgressDone is { } done
                ? (int)Math.Clamp(100L * done / job.ProgressTotal.Value, 0, 99)
                : 0;
        var code = state is BasemapDownloadState.Failed or BasemapDownloadState.Cancelled ? ErrorCode(job) : null;
        return new BasemapDownloadDto(
            job.Id,
            state,
            percent,
            new BasemapRegionDto(payload.West, payload.South, payload.East, payload.North),
            payload.MaxZoom,
            payload.Build,
            code,
            code is null ? null : job.Error,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt);
    }

    private static BasemapFileDto ToDto(BasemapFile file) => new(
        file.SourceId,
        file.Build,
        file.ExtractedAt,
        file.SizeBytes,
        file.Record is null ? null : ToDto(file.Record.Region),
        file.Record?.MaxZoom);

    private static BasemapRegionDto ToDto(BasemapRegion region) => new(region.West, region.South, region.East, region.North);
}
