// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>The public page of an upload's results, as anybody holding its link reads it.</summary>
/// <remarks>
/// Self-contained and shared with nothing: it is not the signed-in status answer with members
/// blanked, because that one carries the cave's id, the party, the uploaded files and the log, and
/// keeping the two shapes apart by construction is what keeps those off this page.
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
/// <param name="Artefacts">What the run produced, to download — never its log.</param>
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
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>The survey a public page is about.</summary>
public sealed record PublicSurveyUploadSurveyDto(string Title);

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

    /// <summary>
    /// Making and withdrawing an upload's public link by hand — the web's status page — and the
    /// public page itself. The first two are not opened by a personal access key: a link is handed
    /// to the world by a person at the web, and taken back there.
    /// </summary>
    private static void MapPublicLinkEndpoints(RouteGroupBuilder api)
    {
        var web = api.MapGroup("/survey-uploads").WithTags("SurveyUploads");
        web.MapPost("/{id:guid}/public-link", MintPublicLinkAsync)
            .WithName("mintSurveyUploadPublicLink")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Makes a new public link to the upload's results, withdrawing the one it had (Write and Share on the cave; never for a protected cave).");
        web.MapDelete("/{id:guid}/public-link", RevokePublicLinkAsync)
            .WithName("revokeSurveyUploadPublicLink")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Withdraws the upload's public link; it then answers as an address that never existed (Write on the cave).");

        // The token is the credential, so this sits on the anonymous allow-list. What it may show
        // is re-decided on every read: a protected cave, a withdrawn link and an installation that
        // has turned links off all answer as an invented token does.
        api.MapGet("/public/survey-uploads/{token}", PublicAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUpload")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The public page of an upload's results: its survey title, the cave's name, the run's progress and the files it produced.");
    }

    private static async Task<Results<Created<SurveyUploadPublicLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> MintPublicLinkAsync(
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

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (found.Upload.Status is SurveyUploadStatus.Receiving or SurveyUploadStatus.Abandoned)
        {
            return ApiProblems.Conflict(NotCompletedCode, "An upload gets a public link once it is complete.");
        }

        switch (await links.RefusalForHandAsync(access, ctx, found.Cave, ct))
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
                    "The cave's position is protected, so its survey results are not published.");
        }

        var (token, link) = await links.ReplaceAsync(found.Upload, ctx.UserId, ct);
        return TypedResults.Created(
            $"/api/v1/survey-uploads/{found.Upload.Id}/public-link",
            new SurveyUploadPublicLinkDto(SurveyUploadLinks.PublicPage(token), link.CreatedAt));
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
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (await links.ResolveAsync(token, ct) is not { } found)
        {
            return ApiProblems.NotFound(PublicLinkNotFoundCode);
        }

        var (upload, cave) = found;
        var title = await db.CaveSurveys.AsNoTracking()
            .Where(s => s.Id == upload.CaveSurveyId)
            .Select(s => s.Title)
            .FirstOrDefaultAsync(ct) ?? upload.SurveyTitle;

        // What the run produced, minus its log: the compiler's words over the caver's files name
        // stations and quote fixed coordinates.
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
            upload.CreatedAt,
            upload.FinishedAt));
    }
}
