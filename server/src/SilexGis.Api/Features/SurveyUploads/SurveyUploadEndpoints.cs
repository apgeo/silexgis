// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>
/// Surveys sent from a field device: open an upload, send its files one by one, say it is
/// complete, and watch its processing run.
/// </summary>
/// <remarks>
/// <para>
/// Every file is archived the moment it arrives — as a survey source of the cave, or as a
/// document attached to it — inside the upload's upload batch, before anything reads it. A
/// processing run reads copies, so nothing it does can change what was archived, and a device
/// that loses its signal halfway has lost nothing it already sent.
/// </para>
/// <para>
/// Every route here answers on the cave's survey terms: Read on the cave <b>and</b> its exact
/// location open to the caller, because a survey archive carries fixed stations, a Therion source
/// carries fix lines, and the processing run's messages and log quote both. A caller who fails
/// either half is told the upload does not exist, never that it is not for them. Changing an
/// upload — opening one, sending a file, completing, abandoning, running again — needs Write on
/// the cave as well.
/// </para>
/// <para>
/// These are the routes a personal access key made for survey uploads opens, so a device is
/// given a key rather than the account's password.
/// </para>
/// </remarks>
public static partial class SurveyUploadEndpoints
{
    /// <summary>The version of this exchange the server serves.</summary>
    public const int ContractVersion = 1;

    /// <summary>The header a device names the contract version it speaks in.</summary>
    public const string ContractHeader = "X-SilexGis-Contract";

    /// <summary>How often a device is told to ask how its upload is getting on.</summary>
    public const int PollIntervalSeconds = 3;

    /// <summary>The survey tools accepted, by the names an upload's <c>source</c> uses.</summary>
    public static readonly IReadOnlyList<string> Sources = ["topodroid"];

    public const string ContractUnsupportedCode = "survey_upload.contract_unsupported";
    public const string SourceUnsupportedCode = "survey_upload.source_unsupported";
    public const string CaveRequiredCode = "survey_upload.cave_required";
    public const string CaveAmbiguousCode = "survey_upload.cave_ambiguous";
    public const string CaveNotFoundCode = "cave.not_found";
    public const string CaveCreationDisabledCode = "survey_upload.cave_creation_disabled";
    public const string CaveTypeUnknownCode = "survey_upload.cave_type_unknown";
    public const string SurveyNotFoundCode = "survey_upload.survey_not_found";
    public const string OutputUnknownCode = "survey_upload.output_unknown";
    public const string BusyCode = "survey_upload.busy";
    public const string NotFoundCode = "survey_upload.not_found";
    public const string NotReceivingCode = "survey_upload.not_receiving";
    public const string ExpiredCode = "survey_upload.expired";
    public const string FilesMissingCode = "survey_upload.files_missing";
    public const string NotCompletedCode = "survey_upload.not_completed";
    public const string RunInProgressCode = "survey_upload.run_in_progress";

    /// <summary>The page size a listing uses when it names none, and the most it may ask for.</summary>
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapSurveyUploadEndpoints(this RouteGroupBuilder api)
    {
        // Metadata is fixed when the route is mapped, so the per-file cap that keeps the web server
        // from cutting a large survey archive off before the handler runs is read here.
        var fileCap = ((IEndpointRouteBuilder)api).ServiceProvider
            .GetRequiredService<IOptions<SurveyUploadOptions>>().Value.FileCap;

        var uploads = api.MapGroup("/survey-uploads")
            .WithTags("SurveyUploads")
            .AcceptsAccessKey(AccessKeyPurpose.SurveyUpload);

        // Read before anything else, so it never refuses a contract version — it is where a
        // device learns which one the server speaks.
        uploads.MapGet("/capabilities", CapabilitiesAsync)
            .WithName("surveyUploadCapabilities")
            .WithSummary("What this server takes from a survey tool: sizes, file kinds, the compiler, cave creation, outputs.");

        uploads.MapGet("/", ListAsync)
            .WithName("listSurveyUploads")
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Uploads of caves whose survey the caller may see with the exact location, newest first.");
        uploads.MapPost("/", OpenAsync)
            .WithName("openSurveyUpload")
            .AddEndpointFilter<ContractFilter>()
            .WithValidation<SurveyUploadOpenRequest>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status426UpgradeRequired)
            .WithSummary("Opens an upload of one survey: where it goes (a cave, a survey, a new cave, or one made from the title) and every file that will be sent.");
        uploads.MapGet("/{id:guid}", GetAsync)
            .WithName("getSurveyUpload")
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("An upload and how its processing is getting on.");
        uploads.MapPut("/{id:guid}/files/{name}", PutFileAsync)
            .WithName("putSurveyUploadFile")
            .AddEndpointFilter<ContractFilter>()
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(fileCap))
            .Accepts<Stream>("application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .WithSummary("Sends one declared file as the raw request body; archives it at once. A repeat for a file already archived answers what the first did.");
        uploads.MapPost("/{id:guid}/complete", CompleteAsync)
            .WithName("completeSurveyUpload")
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .WithSummary("Says every file has been sent, and queues the processing run.");
        uploads.MapPost("/{id:guid}/rerun", RerunAsync)
            .WithName("rerunSurveyUpload")
            .AddEndpointFilter<ContractFilter>()
            .WithValidation<SurveyUploadRerunRequest>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Runs a finished upload's processing again on its archived files, with its options or new ones.");
        uploads.MapDelete("/{id:guid}", AbandonAsync)
            .WithName("abandonSurveyUpload")
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Abandons an upload still receiving files. The files already sent stay archived.");

        MapPublicLinkEndpoints(api);
        return api;
    }

    private static async Task<Ok<SurveyUploadCapabilitiesDto>> CapabilitiesAsync(
        SilexGisDbContext db, IOptions<SurveyUploadOptions> options, ISurveyCompiler compiler, CancellationToken ct)
    {
        var settings = options.Value;
        return TypedResults.Ok(new SurveyUploadCapabilitiesDto(
            ContractVersion,
            Sources,
            settings.FileCap,
            settings.FileCountCap,
            SurveyUploadFiles.AcceptedExtensions,
            // Without a compiler, uploads are archived and their archive read, and nothing is
            // compiled; a device says so before the caver starts.
            compiler.IsAvailable
                ? new SurveyCompilerCapabilitiesDto(true, compiler.Version, SurveyOutputs.Catalogue)
                : new SurveyCompilerCapabilitiesDto(false, null, []),
            new SurveyCaveCreationCapabilitiesDto(
                settings.CaveCreation,
                settings.CaveCreation ? await DefaultCaveTypeIdAsync(db, settings, ct) : null,
                [Visibility.Private, Visibility.CavingGroup, Visibility.Authenticated, Visibility.Public]),
            new SurveyOutputCapabilitiesDto(SurveyOutputs.Catalogue, settings.DefaultOutputList()),
            PollIntervalSeconds));
    }

    private static async Task<Results<Ok<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var writable = await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct);
        return TypedResults.Ok(await ToDtoAsync(db, tokens, links, found.Upload, found.Cave, writable, ct));
    }

    private static async Task<Results<Accepted<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> CompleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        var now = clock.GetUtcNow();

        // A repeat of a completion that already went through — the answer to the first was lost
        // on the way back — is answered with where the upload now stands, not refused.
        if (upload.Status is SurveyUploadStatus.Queued or SurveyUploadStatus.Running
            or SurveyUploadStatus.Succeeded or SurveyUploadStatus.Failed)
        {
            return Accepted(await ToDtoAsync(db, tokens, links, upload, found.Cave, writable: true, ct));
        }

        if (upload.Status != SurveyUploadStatus.Receiving)
        {
            return ApiProblems.Conflict(NotReceivingCode, "This upload was abandoned; open a new one.");
        }

        if (now >= upload.ExpiresAt)
        {
            return Expired();
        }

        var missing = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == upload.Id && f.Status != SurveyUploadFileStatus.Stored)
            .OrderBy(f => f.Name)
            .Select(f => f.Name)
            .ToListAsync(ct);
        if (missing.Count > 0)
        {
            return ApiProblems.BadRequest(
                FilesMissingCode,
                $"These declared files have not arrived: {string.Join(", ", missing)}.",
                "files",
                missing);
        }

        upload.Status = SurveyUploadStatus.Queued;
        upload.Stage = null;
        upload.Progress = 0;
        upload.Message = null;
        Queue(db, upload);
        await CloseBatchAsync(db, upload.UploadBatchId, now, ct);

        // The public link is made with the completion, in the same save, by whoever completes it:
        // the cave's protection is known by now, a cave the upload made included.
        await links.MintAtCompletionAsync(access, ctx, upload, found.Cave, ct);
        await db.SaveChangesAsync(ct);

        return Accepted(await ToDtoAsync(db, tokens, links, upload, found.Cave, writable: true, ct));
    }

    private static async Task<Results<Accepted<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> RerunAsync(
        Guid id,
        SurveyUploadRerunRequest? request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        IOptions<SurveyUploadOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        switch (upload.Status)
        {
            case SurveyUploadStatus.Queued or SurveyUploadStatus.Running:
                return ApiProblems.Conflict(RunInProgressCode, "This upload's processing is already queued or running.");
            case SurveyUploadStatus.Receiving or SurveyUploadStatus.Abandoned:
                return ApiProblems.Conflict(NotCompletedCode, "Only an upload whose files all arrived can be run again.");
            default:
                break;
        }

        if (request?.Pipeline is { } pipeline)
        {
            // The outputs the uploader always asks for stay asked for, whoever runs it again.
            var asked = await RunOptionsAsync(db, options.Value, upload.RequestedBy, pipeline, ct);
            if (asked.Problem is { } problem)
            {
                return problem;
            }

            upload.Options = JsonSerializer.Serialize(asked.Options, JsonSerializerOptions.Web);
        }

        // The last run's results are cleared by a direct update, so the audited save below records
        // only the human act — queued again, and with which options — and none of what was cleared.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await SurveyUploadWrites.ClearRunAsync(db, upload.Id, ct))
            {
                return ApiProblems.Conflict(RunInProgressCode, "This upload's processing is already queued or running.");
            }

            upload.Status = SurveyUploadStatus.Queued;
            Queue(db, upload);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await db.Entry(upload).ReloadAsync(ct);

        return Accepted(await ToDtoAsync(db, tokens, links, upload, found.Cave, writable: true, ct));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> AbandonAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        if (upload.Status == SurveyUploadStatus.Abandoned)
        {
            return TypedResults.NoContent();
        }

        if (upload.Status != SurveyUploadStatus.Receiving)
        {
            return ApiProblems.Conflict(
                NotReceivingCode, "Every file of this upload arrived; it is undone from the web, not abandoned.");
        }

        // Only the session ends. What arrived stays archived: it is the caver's survey, and
        // taking it away is an act of its own on the cave page.
        var now = clock.GetUtcNow();
        upload.Status = SurveyUploadStatus.Abandoned;
        upload.FinishedAt = now;
        await CloseBatchAsync(db, upload.UploadBatchId, now, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SurveyUploadPageDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IFileAccessTokenService tokens,
        Guid? caveId,
        Guid? caveSurveyId,
        string? source,
        bool? mine,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.SurveyUploads.AsNoTracking();
        if (caveId is { } cave)
        {
            query = query.Where(u => u.CaveFeatureId == cave);
        }

        if (caveSurveyId is { } survey)
        {
            query = query.Where(u => u.CaveSurveyId == survey);
        }

        if (!string.IsNullOrEmpty(source))
        {
            if (!RouteEnums.TryParse<SurveyOrigin>(source, out var origin))
            {
                return RouteEnums.Invalid<SurveyOrigin>("source");
            }

            query = query.Where(u => u.Source == origin);
        }

        if (mine == true)
        {
            query = query.Where(u => u.RequestedBy == ctx.UserId);
        }

        // The caves these uploads hang on, reduced to those whose survey the caller may see:
        // readable, and placed exactly. Everything after is counted and paged within that set
        // only, so neither a row nor the total says anything about a cave kept from the caller.
        var caveIds = await query.Select(u => u.CaveFeatureId).Distinct().ToListAsync(ct);
        var readable = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id) && f.Kind == FeatureKind.Cave && f.DeletedAt == null)
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);
        var exact = await protection.ExactViewIdsAsync(ctx, [.. readable.Select(r => r.Id)], ct);
        var caveNames = readable.Where(r => exact.Contains(r.Id)).ToDictionary(r => r.Id, r => r.Name ?? "");
        var visibleCaves = caveNames.Keys.ToList();

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var number = Math.Max(1, page ?? 1);
        var visible = query.Where(u => visibleCaves.Contains(u.CaveFeatureId));
        var total = await visible.CountAsync(ct);
        var rows = await visible
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var uploadIds = rows.Select(r => r.Id).ToList();
        var surveyIds = rows.Select(r => r.CaveSurveyId).Distinct().ToList();
        var titles = await db.CaveSurveys.AsNoTracking()
            .Where(s => surveyIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Title, ct);
        var files = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => uploadIds.Contains(f.SurveyUploadId)
                && f.Status == SurveyUploadFileStatus.Stored
                && f.DocumentId != null
                && (f.Role == SurveyUploadFileRole.Archive || f.Role == SurveyUploadFileRole.TherionSource))
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
        var revisions = await CurrentRevisionsAsync(db, [.. files.Select(f => f.DocumentId!.Value)], ct);
        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, rows.Select(r => r.RequestedBy), ct);

        SurveyUploadListFileDto? Listed(SurveyUploadFile f) =>
            revisions.TryGetValue(f.DocumentId!.Value, out var r)
                ? new SurveyUploadListFileDto(f.Name, r.SizeBytes, ContentUrl(tokens, r.FileId))
                : null;

        var items = rows.Select(u =>
        {
            var own = files.Where(f => f.SurveyUploadId == u.Id).ToList();
            return new SurveyUploadListItemDto(
                u.Id,
                u.CaveFeatureId,
                caveNames[u.CaveFeatureId],
                new SurveyUploadCaveSurveyDto(u.CaveSurveyId, titles.GetValueOrDefault(u.CaveSurveyId, u.SurveyTitle)),
                u.VersionAt,
                u.Source,
                u.SurveyTitle,
                u.SurveyName,
                u.SurveyDate,
                new SurveyUploadUploaderDto(u.RequestedBy, labels.GetValueOrDefault(u.RequestedBy, "")),
                u.Status,
                u.CreatedAt,
                own.Where(f => f.Role == SurveyUploadFileRole.Archive).Select(Listed).FirstOrDefault(x => x is not null),
                [.. own.Where(f => f.Role == SurveyUploadFileRole.TherionSource).Select(Listed).OfType<SurveyUploadListFileDto>()],
                SurveyUploadLinks.StatusPage(u.CaveFeatureId, u.CaveSurveyId, u.Id));
        }).ToList();

        return TypedResults.Ok(new SurveyUploadPageDto(items, total, number, size));
    }

    // ---- shared by the handlers of this slice ----

    private sealed record VisibleUpload(SurveyUpload Upload, Feature Cave);

    /// <summary>
    /// The upload, tracked, with its cave — or null when either is missing or the caller may not
    /// see the cave's survey. One answer for all of those, so a refusal never says which.
    /// </summary>
    private static async Task<VisibleUpload?> VisibleUploadAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct)
    {
        var upload = await db.SurveyUploads.FirstOrDefaultAsync(u => u.Id == id, ct);
        var cave = upload is null ? null : await CaveAsync(db, upload.CaveFeatureId, ct);
        return upload is not null && cave is not null
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct)
            ? new VisibleUpload(upload, cave)
            : null;
    }

    private static Task<Feature?> CaveAsync(SilexGisDbContext db, Guid id, CancellationToken ct) =>
        db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave && f.DeletedAt == null, ct);

    /// <summary>
    /// The processing job for an upload, added to the same save as the status change that asks
    /// for it. No requester is named: the generic "your job finished" notice would arrive with
    /// nothing to say about a survey, and the notice an upload deserves links to its own page.
    /// </summary>
    private static void Queue(SilexGisDbContext db, SurveyUpload upload) =>
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.SurveyUploadPipeline,
            Payload = JsonSerializer.Serialize(new SurveyUploadPipelinePayload(upload.Id), JsonSerializerOptions.Web),
        });

    private static async Task CloseBatchAsync(SilexGisDbContext db, Guid batchId, DateTimeOffset now, CancellationToken ct)
    {
        var batch = await db.UploadBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is { Status: UploadBatchStatus.Open })
        {
            batch.Status = UploadBatchStatus.Completed;
            batch.CompletedAt = now;
        }
    }

    private static Accepted<SurveyUploadDto> Accepted(SurveyUploadDto dto) =>
        TypedResults.Accepted($"/api/v1/survey-uploads/{dto.Id}", dto);

    private static ProblemHttpResult Expired() =>
        TypedResults.Problem(
            detail: "This upload waited too long for its files and was closed; open a new one.",
            statusCode: StatusCodes.Status410Gone,
            extensions: new Dictionary<string, object?> { ["code"] = ExpiredCode });

    /// <summary>The cave type a cave made from an upload gets when it names none.</summary>
    private static async Task<long?> DefaultCaveTypeIdAsync(
        SilexGisDbContext db, SurveyUploadOptions settings, CancellationToken ct)
    {
        if (settings.DefaultCaveTypeId is { } configured
            && await db.CaveTypes.AnyAsync(t => t.Id == configured, ct))
        {
            return configured;
        }

        var ordinary = await db.CaveTypes.AsNoTracking()
            .Where(t => t.Code == "cave")
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);
        return ordinary ?? await db.CaveTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);
    }

    private sealed record AskedRun(SurveyUploadRunOptions? Options, ProblemHttpResult? Problem);

    /// <summary>
    /// What a run is asked to do: the request's outputs (or the installation's defaults) with the
    /// account's and the installation's always-produced outputs added.
    /// </summary>
    private static async Task<AskedRun> RunOptionsAsync(
        SilexGisDbContext db,
        SurveyUploadOptions settings,
        Guid userId,
        SurveyUploadPipelineRequest? pipeline,
        CancellationToken ct)
    {
        var requested = pipeline?.Outputs;
        if (requested is not null && SurveyOutputs.Unknown(requested) is { Count: > 0 } unknown)
        {
            return new AskedRun(null, ApiProblems.BadRequest(
                OutputUnknownCode,
                $"Not in the output catalogue: {string.Join(", ", unknown)}.",
                "outputs",
                unknown));
        }

        var account = await db.SurveyUploadPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.AlwaysOutputs)
            .FirstOrDefaultAsync(ct) ?? [];
        var outputs = SurveyOutputs.Effective(
            requested?.Select(o => o.Trim().ToLowerInvariant()).ToList(),
            settings.DefaultOutputList(),
            account,
            settings.AlwaysOutputList());

        return new AskedRun(
            new SurveyUploadRunOptions(
                pipeline?.Compile ?? true,
                outputs,
                pipeline?.MapScale ?? SurveyUploadRunOptions.DefaultMapScale),
            null);
    }

    private static SurveyUploadRunOptions ReadOptions(SurveyUpload upload)
    {
        try
        {
            return JsonSerializer.Deserialize<SurveyUploadRunOptions>(upload.Options, JsonSerializerOptions.Web)
                ?? new SurveyUploadRunOptions(true, [], SurveyUploadRunOptions.DefaultMapScale);
        }
        catch (JsonException)
        {
            return new SurveyUploadRunOptions(true, [], SurveyUploadRunOptions.DefaultMapScale);
        }
    }

    private static async Task<SurveyUploadDto> ToDtoAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        SurveyUpload upload,
        Feature cave,
        bool writable,
        CancellationToken ct)
    {
        var title = await db.CaveSurveys.AsNoTracking()
            .Where(s => s.Id == upload.CaveSurveyId)
            .Select(s => s.Title)
            .FirstOrDefaultAsync(ct) ?? upload.SurveyTitle;
        var files = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == upload.Id)
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Name)
            .ToListAsync(ct);
        var revisions = await CurrentRevisionsAsync(
            db, [.. files.Where(f => f.DocumentId is not null).Select(f => f.DocumentId!.Value)], ct);
        var options = ReadOptions(upload);

        // What the run produced is listed apart from what the device sent and what the run was
        // compiled from, in the order the run stored it.
        var produced = files.Where(f => f.Role is SurveyUploadFileRole.Output or SurveyUploadFileRole.Log).ToList();
        var modelIds = produced.Where(f => f.SurveyModelId is not null).Select(f => f.SurveyModelId!.Value).ToList();
        var models = modelIds.Count == 0
            ? []
            : await db.SurveyModels.AsNoTracking()
                .Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Status, ct);
        var artefacts = new List<SurveyUploadArtefactDto>();
        foreach (var f in produced)
        {
            var kind = f.Role == SurveyUploadFileRole.Log ? SurveyUploadArtefacts.LogKind : f.Output ?? "";
            var error = f.RefusalCode is { } refusal ? new SurveyUploadErrorDto(refusal, SurveyPipelineNotes.ForArtefact(refusal)) : null;
            if (f.Status == SurveyUploadFileStatus.Refused && error is not null)
            {
                // Asked for and not produced: listed, with no file, so a screen can say which.
                artefacts.Add(new SurveyUploadArtefactDto(kind, f.Name, null, null, null, null, null, null, null, error));
                continue;
            }

            if (f.Status != SurveyUploadFileStatus.Stored || f.DocumentId is not { } d || !revisions.TryGetValue(d, out var revision)
                || !SurveyUploadArtefacts.Listable(f.Output, f.SurveyModelId))
            {
                continue;
            }

            SurveyModelStatus? modelStatus = f.SurveyModelId is { } m && models.TryGetValue(m, out var status) ? status : null;
            artefacts.Add(new SurveyUploadArtefactDto(
                kind,
                f.Name,
                revision.SizeBytes,
                ContentUrl(tokens, revision.FileId),
                f.SurveyModelId,
                f.SurveyModelId is null && f.SurveySourceId is null ? f.DocumentId : null,
                f.SurveySourceId,
                null,
                modelStatus,
                error));
        }

        var (publicLink, publicLinkRefusal) = await links.StateAsync(upload, writable, ct);

        return new SurveyUploadDto(
            upload.Id,
            upload.Status,
            upload.Stage,
            upload.Progress,
            upload.Message,
            upload.ErrorCode is { } code ? new SurveyUploadErrorDto(code, upload.ErrorMessage) : null,
            new SurveyUploadCaveDto(cave.Id, cave.Name ?? "", $"/caves/{cave.Id}"),
            new SurveyUploadCaveSurveyDto(upload.CaveSurveyId, title),
            upload.VersionAt,
            upload.CreatedCave,
            new SurveyUploadSurveyDto(upload.SurveyTitle, upload.SurveyName, upload.SurveyDate, upload.Team),
            [.. files.Where(f => f.Role is not (SurveyUploadFileRole.Output or SurveyUploadFileRole.Log)).Select(f => new SurveyUploadFileDto(
                f.Name,
                f.Role,
                f.Status,
                f.DeclaredSize,
                f.ReceivedSize,
                f.Sha256,
                f.RefusalCode,
                f.SurveySourceId,
                f.DocumentId,
                f.DocumentId is { } doc && revisions.TryGetValue(doc, out var r) ? ContentUrl(tokens, r.FileId) : null))],
            artefacts,
            upload.CompilerVersion is { } compiler ? new SurveyUploadCompilerDto(compiler) : null,
            options.Outputs,
            options.Compile,
            // The compiler's own words over the caver's files: station names and fixed
            // coordinates can appear in them, and they are the cave's writers' to read.
            writable ? upload.LogTail : null,
            SurveyUploadLinks.StatusPage(cave.Id, upload.CaveSurveyId, upload.Id),
            publicLink,
            publicLinkRefusal,
            upload.CreatedAt,
            upload.ExpiresAt,
            upload.StartedAt,
            upload.FinishedAt);
    }

    private sealed record Revision(Guid FileId, long SizeBytes);

    /// <summary>The uploaded file of each document's current revision — not a rendition made from it.</summary>
    private static async Task<Dictionary<Guid, Revision>> CurrentRevisionsAsync(
        SilexGisDbContext db, List<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from version in db.DocumentVersions.AsNoTracking()
            join stored in db.StoredFiles.AsNoTracking() on version.Id equals stored.DocumentVersionId
            where documentIds.Contains(version.DocumentId) && version.IsCurrent && stored.ConvertedFromFileId == null
            select new { version.DocumentId, stored.Id, stored.SizeBytes })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => new Revision(g.First().Id, g.First().SizeBytes));
    }

    private static string ContentUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";

    /// <summary>
    /// Refuses a request that names a contract version this server does not serve. A request that
    /// names none is served: the web's own pages use these routes too, and speak the current one.
    /// </summary>
    private sealed class ContractFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var sent = context.HttpContext.Request.Headers[ContractHeader].ToString().Trim();
            if (sent.Length == 0 || sent == ContractVersion.ToString(CultureInfo.InvariantCulture))
            {
                return next(context);
            }

            return ValueTask.FromResult<object?>(TypedResults.Problem(
                detail: $"This server speaks version {ContractVersion} of the survey upload exchange.",
                statusCode: StatusCodes.Status426UpgradeRequired,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ContractUnsupportedCode,
                    ["contractVersion"] = ContractVersion,
                }));
        }
    }
}
