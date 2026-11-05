// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>The public page of an upload's results, as anybody holding its link reads it — or, for a link made for the organization, any signed-in account.</summary>
/// <remarks>
/// Self-contained and shared with nothing: it is not the signed-in status answer with members
/// blanked, because that one carries the cave's id, the party and the uploaded files, and keeping
/// the two shapes apart by construction is what keeps those off this page. The compiler's log is
/// on it: its words can name stations and repeat the survey's fixed point, but whoever holds the
/// link already has the compiled model, which places every station, and the log is where a failed
/// compile says why.
/// </remarks>
/// <param name="Survey">The survey, by its title.</param>
/// <param name="Cave">The cave, by its name only.</param>
/// <param name="Version">The upload's version of its survey: its UTC moment, to the second.</param>
/// <param name="Status">Where the upload stands.</param>
/// <param name="Stage">The step the run is in, or reached last.</param>
/// <param name="Stages">Every step of a run, with how far it has got.</param>
/// <param name="Progress">0–100.</param>
/// <param name="Message">What the run is doing now, in words.</param>
/// <param name="Error">Why the run failed, when it did.</param>
/// <param name="Artefacts">What the run produced, to download. The log is not among them: it has <paramref name="Log"/>.</param>
/// <param name="LogTail">The last lines the compiler wrote, as the signed-in status page shows them; null before a compile.</param>
/// <param name="Log">The compiler's whole log, to download, once a compile has written one.</param>
/// <param name="CreatedAt">When the upload was opened.</param>
/// <param name="FinishedAt">When the last run finished.</param>
public sealed record PublicSurveyUploadDto(
    PublicSurveyUploadSurveyDto Survey,
    PublicSurveyUploadCaveDto Cave,
    DateTimeOffset Version,
    SurveyUploadStatus Status,
    SurveyUploadStage? Stage,
    IReadOnlyList<PublicSurveyUploadStageDto> Stages,
    int Progress,
    string? Message,
    PublicSurveyUploadErrorDto? Error,
    IReadOnlyList<PublicSurveyUploadArtefactDto> Artefacts,
    string? LogTail,
    PublicSurveyUploadLogDto? Log,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>The survey a public page is about.</summary>
public sealed record PublicSurveyUploadSurveyDto(string Title);

/// <summary>The compiler's log of the run, downloaded through the page's own link.</summary>
/// <param name="Name">The log's file name, which carries the version stamp.</param>
/// <param name="SizeBytes">The log's size.</param>
/// <param name="Url">
/// Where to download it: a route of the same link, which decides on every read exactly as the page
/// does — a link withdrawn or closed since answers there as it does here.
/// </param>
public sealed record PublicSurveyUploadLogDto(string Name, long SizeBytes, string Url);

/// <summary>The cave a public page is about, by name and nothing else.</summary>
public sealed record PublicSurveyUploadCaveDto(string Name);

/// <summary>One step of a run and how far it has got.</summary>
public sealed record PublicSurveyUploadStageDto(SurveyUploadStage Stage, SurveyUploadStageState State);

/// <summary>Why a run failed: a stable code and a sentence.</summary>
public sealed record PublicSurveyUploadErrorDto(string Code, string? Message);

/// <summary>One file a run produced, with a short-lived address to download it from.</summary>
/// <param name="Kind">The output's catalogue name: <c>lox</c>, <c>3d</c>, <c>plan-pdf</c>, ….</param>
/// <param name="Name">The file's name, which carries the version stamp.</param>
/// <param name="SizeBytes">The file's size.</param>
/// <param name="ContentUrl">A signed, short-lived download address; read the page again for a fresh one.</param>
public sealed record PublicSurveyUploadArtefactDto(string Kind, string Name, long SizeBytes, string ContentUrl);

/// <summary>
/// The bound on how often one address may read public survey-upload pages. Cost control, not a
/// confidentiality control: the token cannot be guessed, and what keeps the page safe is what it
/// declines to carry. A window of its own, because a page watching a run reads again every few
/// seconds while it runs, which is a different shape from the other public surfaces'.
/// </summary>
internal static class PublicSurveyUploadRateLimits
{
    public const string PolicyName = "public-survey-upload";

    public const string ConfigurationKey = "SurveyUploads:PublicRateLimitPerMinute";

    public const int DefaultPerMinute = 120;
}

public static partial class SurveyUploadEndpoints
{
    public const string PublicLinkNotFoundCode = "survey_upload.public_link_not_found";
    public const string PublicLinksDisabledCode = "survey_upload.public_links_disabled";
    public const string PublicLinkForbiddenCode = "survey_upload.public_link_forbidden";
    public const string PublicLinkProtectedCode = "survey_upload.public_link_refused_protected";
    public const string PublicLinkSignInRequiredCode = "survey_upload.public_link_sign_in_required";
    public const string PublicLogNotFoundCode = "survey_upload.public_log_not_found";

    /// <summary>
    /// Making and withdrawing an upload's public link by hand, and the public page itself. The first
    /// two are opened by a personal access key made for survey uploads as well as by a signed-in
    /// account: the device that sent the survey offers to withdraw its link or make a new one on its
    /// result screen. The key carries its account's rights and no more, so it can do here exactly
    /// what that account could at the web.
    /// </summary>
    private static void MapPublicLinkEndpoints(RouteGroupBuilder api)
    {
        var links = api.MapGroup("/survey-uploads").WithTags("SurveyUploads").AcceptsAccessKey(AccessKeyPurpose.SurveyUpload);
        links.MapPost("/{id:guid}/public-link", MintPublicLinkAsync)
            .WithName("mintSurveyUploadPublicLink")
            .WithValidation<SurveyUploadPublicLinkMintRequest>()
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Makes a new public link to the upload's results, withdrawing the one it had (Write and Share on the cave; a protected cave only with allowProtected).");
        links.MapDelete("/{id:guid}/public-link", RevokePublicLinkAsync)
            .WithName("revokeSurveyUploadPublicLink")
            .AddEndpointFilter<ContractFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Withdraws the upload's public link; it then answers as an address that never existed (Write on the cave).");

        // The token is the credential, so this sits on the anonymous allow-list. What it may show
        // is re-decided on every read: a cave protected without the link having been made knowing
        // it, a withdrawn link and an installation that has turned links off all answer as an
        // invented token does. A link made for the organization asks an anonymous caller to sign
        // in — only once everything else has said it opens, so the different answer says nothing
        // about a token that would not.
        api.MapGet("/public/survey-uploads/{token}", PublicAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUpload")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The public page of an upload's results: its survey title, the cave's name, the run's progress, the files it produced and the compiler's log. 401 survey_upload.public_link_sign_in_required for a link made for the organization, read without signing in.");

        // The page's log in another form, so it is opened by the very decision the page is, on
        // every read: a delivery address minted when the page was read would outlive a withdrawal.
        api.MapGet("/public/survey-uploads/{token}/log", PublicLogAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUploadLog")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The compiler's whole log of the upload behind a public link, as a text file to save. Opens and refuses exactly as the page does; 404 survey_upload.public_log_not_found when the link opens and the upload has no log.");
    }

    /// <summary>
    /// What the log is answered as. Plain text whatever Therion wrote it in, so a browser shows or
    /// saves it as text rather than offering it to whatever program claims the compiler's own type.
    /// </summary>
    private const string PublicLogContentType = "text/plain; charset=utf-8";

    private static async Task<Results<Created<SurveyUploadPublicLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> MintPublicLinkAsync(
        Guid id,
        SurveyUploadPublicLinkMintRequest? request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
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

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (found.Upload.Status is SurveyUploadStatus.Receiving or SurveyUploadStatus.Abandoned)
        {
            return ApiProblems.Conflict(NotCompletedCode, "An upload gets a public link once it is complete.");
        }

        var allowProtected = request?.AllowProtected ?? false;
        var (refusal, caveProtected) = await links.RefusalForHandAsync(access, ctx, found.Cave, allowProtected, ct);
        switch (refusal)
        {
            case SurveyUploadPublicLinks.InstallationOff:
                return ApiProblems.Conflict(PublicLinksDisabledCode, "This installation makes no public links.");
            case SurveyUploadPublicLinks.NoShareRight:
                return TypedResults.Problem(
                    detail: "Making a public link takes the right to share the cave.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["code"] = PublicLinkForbiddenCode });
            case SurveyUploadPublicLinks.ProtectedCave:
                return ApiProblems.Conflict(
                    PublicLinkProtectedCode,
                    "The cave's position is protected: its survey results show where its passages are, and a link is made only when asked for with allowProtected.");
        }

        var audience = request?.Audience ?? await links.AudienceForHandAsync(found.Upload, ctx.UserId, ct);
        var (token, link) = await links.ReplaceAsync(found.Upload, ctx.UserId, audience, caveProtected && allowProtected, ct);
        return TypedResults.Created(
            $"/api/v1/survey-uploads/{found.Upload.Id}/public-link",
            new SurveyUploadPublicLinkDto(SurveyUploadLinks.PublicPage(token), link.CreatedAt, link.Audience, link.ProtectedAcknowledged));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokePublicLinkAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
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

        // Taking a link back needs only Write: a link can always be withdrawn by those who may
        // change the upload, whatever became of the right that made it.
        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        await links.RevokeAsync(found.Upload, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PublicSurveyUploadDto>, ProblemHttpResult>> PublicAsync(
        string token,
        SilexGisDbContext db,
        SurveyUploadPublicLinkService links,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var (opened, refusal) = await OpenPublicLinkAsync(token, links, accessAccessor, ct);
        if (opened is not { } found)
        {
            return refusal!;
        }

        var (upload, cave) = found;
        var title = await db.CaveSurveys.AsNoTracking()
            .Where(s => s.Id == upload.CaveSurveyId)
            .Select(s => s.Title)
            .FirstOrDefaultAsync(ct) ?? upload.SurveyTitle;

        // What the run produced. The log is listed apart, with an address of this link's own.
        var outputs = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == upload.Id
                && f.Role == SurveyUploadFileRole.Output
                && f.Status == SurveyUploadFileStatus.Stored
                && f.DocumentId != null)
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Name)
            .ToListAsync(ct);
        var revisions = await CurrentRevisionsAsync(db, [.. outputs.Select(f => f.DocumentId!.Value)], ct);
        var artefacts = outputs
            .Where(f => revisions.ContainsKey(f.DocumentId!.Value) && SurveyUploadArtefacts.Listable(f.Output, f.SurveyModelId))
            .Select(f =>
            {
                var revision = revisions[f.DocumentId!.Value];
                return new PublicSurveyUploadArtefactDto(f.Output ?? "", f.Name, revision.SizeBytes, ContentUrl(tokens, revision.FileId));
            })
            .ToList();

        var log = await PublicLogFileAsync(db, upload.Id, ct);
        var compile = ReadOptions(upload).Compile;
        return TypedResults.Ok(new PublicSurveyUploadDto(
            new PublicSurveyUploadSurveyDto(title),
            new PublicSurveyUploadCaveDto(cave.Name ?? ""),
            upload.VersionAt,
            upload.Status,
            upload.Stage,
            [.. SurveyUploadPublicLinks.StageStates(upload.Status, upload.Stage, compile)
                .Select(s => new PublicSurveyUploadStageDto(s.Stage, s.State))],
            upload.Progress,
            upload.Message,
            upload.ErrorCode is { } code ? new PublicSurveyUploadErrorDto(code, upload.ErrorMessage) : null,
            artefacts,
            upload.LogTail,
            log is null ? null : new PublicSurveyUploadLogDto(log.Name, log.SizeBytes, PublicLogUrl(token)),
            upload.CreatedAt,
            upload.FinishedAt));
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> PublicLogAsync(
        string token,
        SilexGisDbContext db,
        SurveyUploadPublicLinkService links,
        IAccessContextAccessor accessAccessor,
        IFileStore fileStore,
        CancellationToken ct)
    {
        var (opened, refusal) = await OpenPublicLinkAsync(token, links, accessAccessor, ct);
        if (opened is not { } found)
        {
            return refusal!;
        }

        if (await PublicLogFileAsync(db, found.Upload.Id, ct) is not { } log)
        {
            return ApiProblems.NotFound(PublicLogNotFoundCode);
        }

        return TypedResults.PhysicalFile(
            fileStore.GetAbsolutePath(log.StoragePath),
            contentType: PublicLogContentType,
            fileDownloadName: log.Name,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// The upload and cave a public link opens for this caller now, or the answer to give instead —
    /// the one decision the page and its log share. Every refusal but one is the 404 an invented
    /// token gets. Any signed-in account of this installation opens a link made for the
    /// organization; nothing is asked of its rights on the cave, because holding the address is
    /// what the link grants. A personal access key is never a signed-in caller here: its principal
    /// is taken away on a route that needs no account.
    /// </summary>
    private static async Task<((SurveyUpload Upload, Feature Cave)? Opened, ProblemHttpResult? Refusal)> OpenPublicLinkAsync(
        string token, SurveyUploadPublicLinkService links, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        if (await links.ResolveAsync(token, ct) is not { } found)
        {
            return (null, ApiProblems.NotFound(PublicLinkNotFoundCode));
        }

        var (upload, cave, audience) = found;
        if (audience != SurveyUploadLinkAudience.Public
            && !SurveyUploadPublicLinks.Admits(audience, signedIn: await accessAccessor.GetAsync(ct) is not null))
        {
            return (null, TypedResults.Problem(
                detail: "This link opens for the installation's signed-in accounts only; sign in to read it.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = PublicLinkSignInRequiredCode }));
        }

        return ((upload, cave), null);
    }

    private sealed record PublicLogFile(string Name, long SizeBytes, string StoragePath);

    /// <summary>The current revision of the upload's stored log — the uploaded file, not a rendition of it — or null.</summary>
    private static Task<PublicLogFile?> PublicLogFileAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct) =>
        (from file in db.SurveyUploadFiles.AsNoTracking()
         join version in db.DocumentVersions.AsNoTracking() on file.DocumentId equals version.DocumentId
         join stored in db.StoredFiles.AsNoTracking() on version.Id equals stored.DocumentVersionId
         where file.SurveyUploadId == uploadId
             && file.Role == SurveyUploadFileRole.Log
             && file.Status == SurveyUploadFileStatus.Stored
             && version.IsCurrent
             && stored.ConvertedFromFileId == null
         orderby file.CreatedAt
         select new PublicLogFile(file.Name, stored.SizeBytes, stored.StoragePath))
        .FirstOrDefaultAsync(ct);

    private static string PublicLogUrl(string token) => $"/api/v1/public/survey-uploads/{Uri.EscapeDataString(token)}/log";
}
