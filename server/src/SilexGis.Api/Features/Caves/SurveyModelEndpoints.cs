// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using NetTopologySuite.Geometries;
using SilexGis.Infrastructure.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
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
    /// <summary>
    /// Why the work an upload started could not be done — converting a wall mesh into something a
    /// 3D scene can draw, or reading a line plot into its stations and shots. Null otherwise. It is
    /// written for the person who uploaded the file and is usually something they can act on, so it
    /// is shown to them whatever the format is.
    /// </summary>
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
    /// <summary>
    /// How many legs of the traverse could not be attached to a station, and how many stations
    /// stood where another already did and became one node. Null until a line plot has been read.
    ///
    /// <para>
    /// Published because both losses are silent: endpoints are matched to stations by exact
    /// coordinate equality, and a network missing a share of its legs still produces connectivity
    /// numbers that look entirely reasonable. These two counts are what says otherwise.
    /// </para>
    /// </summary>
    int? DroppedShotCount,
    int? MergedStationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One station of a survey, as it was read out of the uploaded file.
/// </summary>
/// <param name="Name">
/// What identifies the station within its survey model — the name the file gives it, qualified by
/// its survey where the format names stations only within their own. Never the number the file
/// wrote it at: one of the two formats assigns those by file order and reassigns them on every
/// re-export.
/// </param>
/// <param name="Flags">
/// What the file says about the station, one name per flag it set. A list rather than a single
/// value because these combine — an entrance station is above ground and underground at once.
/// </param>
public sealed record SurveyStationDto(
    string Name,
    string? SurveyName,
    double Longitude,
    double Latitude,
    double AltitudeM,
    IReadOnlyList<string> Flags,
    bool IsEntrance,
    bool IsFixed);

/// <summary>
/// One leg of a survey: the line between two measured points, with what the file said about it.
/// </summary>
/// <param name="FromStationName">
/// The station the leg starts at, or null where the file resolves none — a splay's far end is
/// routinely a point no station was ever named for.
/// </param>
/// <param name="LengthM">
/// How long the leg is, as the file measured it, in metres.
/// </param>
/// <param name="IsSplay">
/// The leg is a shot at the wall rather than a leg of the traverse, according to the file itself.
/// This is what the file said and not what the shape of the network suggests.
/// </param>
public sealed record SurveyShotDto(
    string? FromStationName,
    string? ToStationName,
    string? SurveyName,
    double FromLongitude,
    double FromLatitude,
    double FromAltitudeM,
    double ToLongitude,
    double ToLatitude,
    double ToAltitudeM,
    double LengthM,
    IReadOnlyList<string> Flags,
    bool IsSplay);

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
/// What an uploaded survey needs alongside the file, where the file does not carry it.
///
/// <para>
/// Read from the multipart form beside the upload rather than as a later edit: a wall mesh cannot
/// be placed at all without it, and a stored model waiting to be told where it is would be a second
/// unfinished state for every reader of a cave to understand.
/// </para>
///
/// <para>
/// The fields are declared parameters of the endpoint rather than read out of the raw form, so the
/// published document names them and a generated client is type-checked against them. Read loosely,
/// a renamed or re-typed field would keep every build and every generated client green while every
/// upload was refused at run time.
/// </para>
/// </summary>
internal sealed record UploadDeclaration(int? SourceEpsg, Point? Origin, double? OriginHeightM)
{
    /// <summary>Deepest and highest a cave entrance can plausibly sit, in metres.</summary>
    private const double LowestHeightM = -500;
    private const double HighestHeightM = 9000;

    /// <summary>
    /// The declaration a wall mesh must carry. Every field is demanded, because eighty bytes of
    /// free text and a list of triangles say nothing at all about where the triangles are.
    /// </summary>
    public static (UploadDeclaration? Declaration, ProblemHttpResult? Problem) ReadRequired(
        int? sourceEpsg, double? originLongitude, double? originLatitude, double? originHeightM)
    {
        var (declaration, problem) =
            ReadOptional(sourceEpsg, originLongitude, originLatitude, originHeightM);
        if (problem is not null)
        {
            return (null, problem);
        }

        if (declaration?.OriginHeightM is null)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.height_invalid",
                "Give the altitude, in metres, that the file's zero level sits at."));
        }

        if (declaration.SourceEpsg is null && declaration.Origin is null)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.origin_invalid",
                "A file in local coordinates needs the position its zero point sits at."));
        }

        return (declaration, null);
    }

    /// <summary>
    /// The declaration a line plot may carry. Nothing is demanded here, because one of the two
    /// line-plot formats has a field naming the coordinate system its numbers are in and an export
    /// that filled it in has nothing left to answer. What is given is still checked and still wins,
    /// because it is the answer a person chose over one a file asserted — and it is the only answer
    /// there is for a survey written in plain metres about a fixed station, which is what the other
    /// format always is and what an export that left the field empty is too.
    /// </summary>
    public static (UploadDeclaration? Declaration, ProblemHttpResult? Problem) ReadOptional(
        int? sourceEpsg, double? originLongitude, double? originLatitude, double? originHeightM)
    {
        if (sourceEpsg is <= 0)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.crs_invalid", "The coordinate system must be an EPSG code."));
        }

        if (originHeightM is { } given
            && (!double.IsFinite(given) || given < LowestHeightM || given > HighestHeightM))
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.height_invalid",
                "Give the altitude, in metres, that the file's zero level sits at."));
        }

        // The file's own coordinates say where a projected survey is; a position given here as well
        // could only contradict them, so it is dropped rather than half-used.
        if (sourceEpsg is { } epsg)
        {
            return (new UploadDeclaration(epsg, null, originHeightM), null);
        }

        if (originLongitude is null && originLatitude is null)
        {
            return (new UploadDeclaration(null, null, originHeightM), null);
        }

        if (originLongitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180
            || originLatitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90)
        {
            return (null, ApiProblems.BadRequest(
                "survey_model.origin_invalid",
                "A file in local coordinates needs the position its zero point sits at."));
        }

        return (new UploadDeclaration(null, new Point(lon, lat) { SRID = 4326 }, originHeightM), null);
    }
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
        api.MapGet("/survey-models/{id:guid}/stations", StationsAsync)
            .WithTags("SurveyModels")
            .WithSummary("Stations read out of the survey; withheld without the exact-location permission.");
        api.MapGet("/survey-models/{id:guid}/shots", ShotsAsync)
            .WithTags("SurveyModels")
            .WithSummary("Legs read out of the survey; withheld without the exact-location permission.");
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
        // Optional on the wire because only a wall mesh is required to answer them; a line plot may
        // and often must. One of the two line-plot formats has a field naming its coordinate system
        // and needs nothing here when the export filled it in — but the other format has no such
        // field at all, so for a survey written in plain metres about a fixed station these are the
        // only answer there is, and without them it cannot be placed anywhere.
        [FromForm] int? sourceEpsg,
        [FromForm] double? originLongitude,
        [FromForm] double? originLatitude,
        [FromForm] double? originHeightM,
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

        // A wall mesh cannot be placed from its own contents, so its declaration comes with it and
        // is checked before a byte is stored: a model kept without one would be a record nothing
        // can draw and nobody can finish. A line plot is asked the same questions and required to
        // answer none of them, because it can carry the answer itself.
        var read = extension == ".stl"
            ? UploadDeclaration.ReadRequired(sourceEpsg, originLongitude, originLatitude, originHeightM)
            : UploadDeclaration.ReadOptional(sourceEpsg, originLongitude, originLatitude, originHeightM);
        if (read.Problem is { } problem)
        {
            return problem;
        }

        var declaration = read.Declaration;

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

        if (declaration is { } source)
        {
            model.SourceEpsg = source.SourceEpsg;
            model.AnchorHeightM = source.OriginHeightM;
            // For a local file this is the position the uploader gave; for a projected one it is
            // left for the reading to derive from the file's own coordinates. Either way the job
            // reads it back off the row, so what was declared is what is used.
            model.Anchor = source.Origin;
        }

        // Every format now has work waiting for it: a mesh has to be turned into the one file the
        // 3D scene draws, and a line plot has to be read into the station and shot rows that carry
        // its own flags. The row and the job are written in one save, so a model can never be
        // stored in a state that says work is coming with nothing queued to do it.
        var kind = model.Format switch
        {
            SurveyModelFormat.Stl => ProcessingJobKinds.SurveyMesh,
            _ => ProcessingJobKinds.SurveyGraph,
        };

        model.Status = SurveyModelStatus.Pending;
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = kind,
            Payload = kind == ProcessingJobKinds.SurveyMesh
                ? JsonSerializer.Serialize(new SurveyMeshPayload(model.Id), JsonSerializerOptions.Web)
                : JsonSerializer.Serialize(new SurveyGraphPayload(model.Id), JsonSerializerOptions.Web),
            RequestedBy = ctx.UserId,
        });

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

    private static async Task<Ok<PagedResult<SurveyStationDto>>> StationsAsync(
        Guid id,
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (paging, model) = await ReadableAsync(id, page, pageSize, db, access, protection, accessAccessor, ct);
        if (model is null)
        {
            return TypedResults.Ok(EmptyPage<SurveyStationDto>(paging));
        }

        return TypedResults.Ok(await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .OrderBy(s => s.Name)
            .ToPagedAsync(paging.Page, paging.PageSize, ToDto, ct));
    }

    private static async Task<Ok<PagedResult<SurveyShotDto>>> ShotsAsync(
        Guid id,
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (paging, model) = await ReadableAsync(id, page, pageSize, db, access, protection, accessAccessor, ct);
        if (model is null)
        {
            return TypedResults.Ok(EmptyPage<SurveyShotDto>(paging));
        }

        return TypedResults.Ok(await db.SurveyShots.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .OrderBy(s => s.Id)
            .ToPagedAsync(paging.Page, paging.PageSize, ToDto, ct));
    }

    /// <summary>
    /// The survey model whose contents may be read, or null when they may not be.
    ///
    /// <para>
    /// Null covers both a model that does not exist and one the caller may not place, and the two
    /// are answered identically on purpose. A station is a cave coordinate as much as the cave's
    /// own point is, so these routes are withheld by the same gate that withholds the model itself
    /// — and that gate answers "no such model", so an empty page for a withheld model and a
    /// populated one for a model that simply has not been read yet would together say which caves
    /// exist and are being kept from you.
    /// </para>
    /// </summary>
    private static async Task<((int Page, int PageSize) Paging, SurveyModel? Model)> ReadableAsync(
        Guid id,
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var paging = Paging.Normalize(page, pageSize);
        var ctx = await accessAccessor.GetAsync(ct);
        var (model, cave) = await FindWithCaveAsync(db, id, ct);
        if (model is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return (paging, null);
        }

        return (paging, model);
    }

    private static PagedResult<T> EmptyPage<T>((int Page, int PageSize) paging) =>
        new([], paging.Page, paging.PageSize, 0);

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
        FeatureWriteService writes,
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

        // The centerline a reading of this file produced goes with it. It is not a centerline
        // anybody drew — it exists only as this file's line work — and the model's own foreign key
        // on it merely blanks itself, so leaving it would strand a machine-made shape nobody can
        // trace back to a survey, still holding the flag that makes it the cave's shape on the map.
        // Re-uploading the corrected file would then produce a second one beside it, not the
        // default, and the map would keep drawing the survey that was thrown away.
        var extracted = await db.Centerlines
            .Where(c => c.SurveyModelId == model.Id && c.Source == CenterlineSource.Extracted)
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var centerlineId in extracted)
        {
            await writes.DeleteCenterlineAsync(centerlineId, ct);
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
        m.DroppedShotCount,
        m.MergedStationCount,
        m.CreatedAt,
        m.UpdatedAt);

    private static SurveyStationDto ToDto(SurveyStation s) => new(
        s.Name,
        s.SurveyName,
        s.Position.X,
        s.Position.Y,
        s.Position.Coordinate.Z,
        FlagNames(s.Flags),
        s.IsEntrance,
        s.IsFixed);

    private static SurveyShotDto ToDto(SurveyShot s) => new(
        s.FromStationName,
        s.ToStationName,
        s.SurveyName,
        s.Geom.Coordinates[0].X,
        s.Geom.Coordinates[0].Y,
        s.Geom.Coordinates[0].Z,
        s.Geom.Coordinates[1].X,
        s.Geom.Coordinates[1].Y,
        s.Geom.Coordinates[1].Z,
        s.LengthM,
        FlagNames(s.Flags),
        s.IsSplay);

    /// <summary>
    /// The flags a bit set has, one name each.
    ///
    /// <para>
    /// Published as names rather than as the enum itself because these are combinable. A generated
    /// client types a single enum as one of its members, and the value a combination serialises to
    /// is not one of them — so the contract would be wrong in a way that type-checks.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> FlagNames<TEnum>(TEnum flags)
        where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>()
            .Where(value => !value.Equals(default(TEnum)) && flags.HasFlag(value))
            .Select(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()))];

    // A survey model is only ever useful as its own bytes — an upload and the mesh converted
    // from it alike — and neither holds a capture point of its own to be careful about, so both
    // are signed for the wider reach. The cave's protection is enforced on the way in, which is
    // where a model that may not be reached at all is refused.
    private static string FileUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";
}
