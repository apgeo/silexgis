// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using NetTopologySuite.Geometries;
using SilexGis.Infrastructure.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
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
    SurveyModelStatus Status,
    /// <summary>Why conversion failed, when it did; null otherwise.</summary>
    string? ProcessingError,
    /// <summary>
    /// Signed URL of the drawable mesh, once a conversion has produced one. Null for the line-plot
    /// formats, which the embedded viewer reads from <see cref="ModelUrl"/> directly.
    /// </summary>
    string? MeshUrl,
    /// <summary>
    /// Where the mesh's own zero point sits, which is what a scene positions it by. Null until a
    /// conversion has run. This is the cave's location, and reaches no caller who is not already
    /// entitled to that — the whole record is withheld from the rest.
    /// </summary>
    double? AnchorLongitude,
    double? AnchorLatitude,
    double? AnchorHeightM,
    int? TriangleCount,
    /// <summary>
    /// The uploaded file's coordinates were too large for the precision it stores them in, so the
    /// survey lost detail before it arrived. Re-exporting about a local origin recovers it.
    /// </summary>
    bool SourcePrecisionLost,
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
/// What a wall mesh needs alongside the file, because the file does not carry it.
///
/// <para>
/// Read from the multipart form beside the upload rather than as a later edit: without it the mesh
/// cannot be placed at all, and a stored model waiting to be told where it is would be a second
/// unfinished state for every reader of a cave to understand.
/// </para>
/// </summary>
internal sealed record MeshDeclaration(int? SourceEpsg, Point? Origin, double OriginHeightM)
{
    /// <summary>Deepest and highest a cave entrance can plausibly sit, in metres.</summary>
    private const double LowestHeightM = -500;
    private const double HighestHeightM = 9000;

    public static (MeshDeclaration? Declaration, ProblemHttpResult? Problem) FromForm(IFormCollection form)
    {
        var epsgText = form["sourceEpsg"].ToString();
        int? epsg = null;
        if (!string.IsNullOrWhiteSpace(epsgText))
        {
            if (!int.TryParse(epsgText, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                return (null, ApiProblems.BadRequest(
                    "survey_model.crs_invalid", "The coordinate system must be an EPSG code."));
            }

            epsg = parsed;
        }

        if (!TryNumber(form, "originHeightM", out var height)
            || height < LowestHeightM || height > HighestHeightM)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.height_invalid",
                "Give the altitude, in metres, that the file's zero level sits at."));
        }

        if (epsg is not null)
        {
            // The file's own coordinates say where it is; the position is derived from them, so a
            // second answer here could only contradict the first.
            return (new MeshDeclaration(epsg, null, height), null);
        }

        if (!TryNumber(form, "originLongitude", out var lon) || lon is < -180 or > 180
            || !TryNumber(form, "originLatitude", out var lat) || lat is < -90 or > 90)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.origin_invalid",
                "A file in local coordinates needs the position its zero point sits at."));
        }

        return (new MeshDeclaration(null, new Point(lon, lat) { SRID = 4326 }, height), null);
    }

    private static bool TryNumber(IFormCollection form, string field, out double value) =>
        double.TryParse(form[field].ToString(), CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
}

/// <summary>
/// 3D survey models of a cave (.lox / .3d / .stl). They inherit the cave's access control, and
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
        HttpRequest request,
        SilexGisDbContext db,
        DocumentWriteService documents,
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
        if (extension is not (".lox" or ".3d" or ".stl"))
        {
            return ApiProblems.BadRequest(
                "survey_model.format_unsupported", "Upload a Therion .lox, Survex .3d or .stl file.");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("survey_model.size_invalid", "The file is empty or exceeds 100 MB.");
        }

        // A wall mesh cannot be placed from its own contents, so the declaration comes with it and
        // is checked before a byte is stored: a model kept without one would be a record nothing
        // can draw and nobody can finish.
        MeshDeclaration? declaration = null;
        if (extension == ".stl")
        {
            var read = MeshDeclaration.FromForm(request.Form);
            if (read.Problem is { } problem)
            {
                return problem;
            }

            declaration = read.Declaration;
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

        var name = Path.GetFileNameWithoutExtension(file.FileName);
        var stored = documents.Create(
            new StoredContent(
                storagePath,
                Path.GetFileName(file.FileName),
                "application/octet-stream",
                file.Length,
                sha256,
                FileKind.Survey),
            name,
            ctx.UserId,
            ctx.UserId).File;

        var model = new SurveyModel
        {
            CaveFeatureId = caveId,
            Name = name,
            FileId = stored.Id,
            Format = extension switch
            {
                ".lox" => SurveyModelFormat.Lox,
                ".3d" => SurveyModelFormat.Survex3d,
                _ => SurveyModelFormat.Stl,
            },
        };

        db.SurveyModels.Add(model);

        if (declaration is { } mesh)
        {
            model.Status = SurveyModelStatus.Pending;
            model.SourceEpsg = mesh.SourceEpsg;
            model.AnchorHeightM = mesh.OriginHeightM;
            // For a local file this is the position the uploader gave; for a projected one it is
            // left for the conversion to derive from the file's own coordinates. Either way the
            // conversion reads it back off the row, so what was declared is what is used.
            model.Anchor = mesh.Origin;

            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyMesh,
                Payload = JsonSerializer.Serialize(new SurveyMeshPayload(model.Id), JsonSerializerOptions.Web),
                RequestedBy = ctx.UserId,
            });
        }

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
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
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
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
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
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct) is { } stale)
        {
            return stale;
        }

        // The stored file is immutable and may be referenced elsewhere; only the model
        // row goes — plus the resource-link members that named it, which have no FK to
        // clean themselves up by.
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId == model.Id)
            .ExecuteDeleteAsync(ct);
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
        !await SurveyModelAccess.LocationOpenAsync(protection, ctx, caveFeatureId, ct);

    private static SurveyModelDto ToDto(this SurveyModel m, IFileAccessTokenService tokens) => new(
        m.Id,
        m.CaveFeatureId,
        m.Name,
        m.Format,
        m.FileId,
        m.Description,
        m.SurveyedAt,
        FileUrl(tokens, m.FileId),
        m.Status,
        m.ProcessingError,
        m.ConvertedFileId is { } converted ? FileUrl(tokens, converted) : null,
        m.Anchor?.X,
        m.Anchor?.Y,
        m.AnchorHeightM,
        m.TriangleCount,
        m.SourcePrecisionLost,
        m.CreatedAt,
        m.UpdatedAt);

    // A survey model is only ever useful as its own bytes — an upload and the mesh converted
    // from it alike — and neither holds a capture point of its own to be careful about, so both
    // are signed for the wider reach. The cave's protection is enforced on the way in, which is
    // where a model that may not be reached at all is refused.
    private static string FileUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";
}
