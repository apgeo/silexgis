// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

public sealed record SurveyModelDto(
    Guid Id,
    Guid CaveId,
    string Name,
    SurveyModelFormat Format,
    Guid FileId,
    string? Description,
    DateOnly? SurveyedAt,
    /// <summary>Signed survey-file URL — fetch and hand to the 3D viewer as-is.</summary>
    string ModelUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SurveyModelUpdateRequest(
    string Name,
    string? Description,
    DateOnly? SurveyedAt);

public sealed class SurveyModelUpdateRequestValidator : AbstractValidator<SurveyModelUpdateRequest>
{
    public SurveyModelUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

/// <summary>
/// 3D survey models of a cave (.lox / .3d). They inherit the cave's access control, and
/// because the files carry absolute georeferenced coordinates they are location data:
/// for a location-protected cave every read path here withholds the records entirely
/// from callers without exact-location access — same stance as cave-linked rasters,
/// stricter than the link redaction applied to features that merely reference a cave.
/// </summary>
public static class SurveyModelEndpoints
{
    /// <summary>Whole-system survey exports stay well under this; matches the general file cap.</summary>
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    public static RouteGroupBuilder MapSurveyModelEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/survey-models", ListAsync)
            .WithTags("SurveyModels")
            .WithSummary("Survey models of a cave; withheld without the exact-location permission.");
        api.MapPost("/caves/{caveId:guid}/survey-models", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadBytes))
            .WithTags("SurveyModels")
            .WithSummary("Uploads a .lox/.3d survey model (Write on the cave).");
        api.MapGet("/survey-models/{id:guid}", GetAsync)
            .WithTags("SurveyModels")
            .WithSummary("Single survey model with a fresh file delivery URL.");
        api.MapPut("/survey-models/{id:guid}", UpdateAsync)
            .WithValidation<SurveyModelUpdateRequest>()
            .WithTags("SurveyModels")
            .WithSummary("Metadata update (Write on the cave).");
        api.MapDelete("/survey-models/{id:guid}", DeleteAsync)
            .WithTags("SurveyModels")
            .WithSummary("Deletes the survey model (Write on the cave); the stored file is kept.");

        return api;
    }

    private static async Task<Results<Ok<List<SurveyModelDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await CaveFeatureAsync(db, caveId, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // The cave stays readable, but its 3D models ARE its location.
        if (await WithheldAsync(protection, ctx, caveId, ct))
        {
            return TypedResults.Ok(new List<SurveyModelDto>());
        }

        var models = await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == caveId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(models.Select(m => m.ToDto(tokens)).ToList());
    }

    private static async Task<Results<Created<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        Guid caveId,
        IFormFile file,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cave = await CaveFeatureAsync(db, caveId, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".lox" or ".3d"))
        {
            return ApiProblems.BadRequest(
                "survey_model.format_unsupported", "Upload a Therion .lox or Survex .3d file.");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("survey_model.size_invalid", "The file is empty or exceeds 100 MB.");
        }

        string storagePath;
        await using (var content = file.OpenReadStream())
        {
            storagePath = await fileStore.SaveAsync(content, extension, ct);
        }

        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        var stored = new StoredFile
        {
            StoragePath = storagePath,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = "application/octet-stream",
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedBy = ctx.UserId,
            Kind = FileKind.Survey,
        };
        stored.VersionGroupId = stored.Id; // head of its own version chain

        var model = new SurveyModel
        {
            CaveFeatureId = caveId,
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            FileId = stored.Id,
            Format = extension == ".lox" ? SurveyModelFormat.Lox : SurveyModelFormat.Survex3d,
        };

        db.StoredFiles.Add(stored);
        db.SurveyModels.Add(model);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/survey-models/{model.Id}", model.ToDto(tokens));
    }

    private static async Task<Results<Ok<SurveyModelDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var (model, cave) = await FindWithCaveAsync(db, id, ct);
        if (model is null || cave is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
            || await WithheldAsync(protection, ctx, cave.Id, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        await Concurrency.EmitETagAsync(http, db, VersionedTable.SurveyModels, model.Id, ct);
        return TypedResults.Ok(model.ToDto(tokens));
    }

    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SurveyModelUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        var cave = model is null ? null : await CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
            || await WithheldAsync(protection, ctx, cave.Id, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct) is { } stale)
        {
            return stale;
        }

        model.Name = request.Name;
        model.Description = request.Description;
        model.SurveyedAt = request.SurveyedAt;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(model.ToDto(tokens));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        var cave = model is null ? null : await CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
            || await WithheldAsync(protection, ctx, cave.Id, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct) is { } stale)
        {
            return stale;
        }

        // The stored file is immutable and may be referenced elsewhere; only the model row goes.
        db.SurveyModels.Remove(model);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<(SurveyModel? Model, Feature? Cave)> FindWithCaveAsync(
        SilexGisDbContext db, Guid id, CancellationToken ct)
    {
        var model = await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return model is null ? (null, null) : (model, await CaveFeatureAsync(db, model.CaveFeatureId, ct));
    }

    private static Task<Feature?> CaveFeatureAsync(SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct) =>
        db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveFeatureId && f.Kind == FeatureKind.Cave, ct);

    /// <summary>
    /// True when the cave's exact location is closed to this caller — which closes its
    /// survey models entirely, files and metadata alike.
    /// </summary>
    private static async Task<bool> WithheldAsync(
        FeatureProtection protection, AccessContext? ctx, Guid caveFeatureId, CancellationToken ct) =>
        !(await protection.ExactViewIdsAsync(ctx, [caveFeatureId], ct)).Contains(caveFeatureId);

    private static SurveyModelDto ToDto(this SurveyModel m, IFileAccessTokenService tokens) => new(
        m.Id,
        m.CaveFeatureId,
        m.Name,
        m.Format,
        m.FileId,
        m.Description,
        m.SurveyedAt,
        $"/api/v1/files/{m.FileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(m.FileId))}",
        m.CreatedAt,
        m.UpdatedAt);
}
