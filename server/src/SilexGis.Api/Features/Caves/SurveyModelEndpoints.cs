// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using NetTopologySuite.Geometries;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

public sealed record SurveyModelDto(
    Guid Id,
    Guid CaveId,
    string Name,
    SurveyModelFormat Format,
    Guid FileId,
    string? Description,
    DateOnly? SurveyedAt,
    /// <summary>
    /// Whether this is the model the cave is represented by, among the cave's models of the same
    /// kind — the line plot the map and the statistics read, or the wall mesh the scene draws.
    /// At most one per cave and kind. The first model of a kind takes it and keeps it until
    /// another is chosen through the route that sets it.
    /// </summary>
    bool IsCurrent,
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
    /// Signed URL of the drawable mesh, once there is one: an uploaded wall mesh converted, or the
    /// walls a line plot's own reading built from the wall surfaces and passage dimensions in the
    /// file. Null for a line plot that carries neither — nothing is drawn around a leg nobody
    /// measured the walls of — and the embedded viewer reads a line plot from
    /// <see cref="ModelUrl"/> either way.
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
    /// How many bytes the drawable mesh is, so a viewer can see how big it is before it arrives;
    /// null until a conversion has produced one. The triangle count above says how much there is
    /// to draw, this says how much there is to fetch, and on a metered connection the second is
    /// the one that costs.
    /// </summary>
    long? MeshSizeBytes,
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
    /// <summary>
    /// How many of the file's station records were its "there is no station here" placeholder — the
    /// far end of a shot at the passage wall — and so became no row. Null until a line plot has been
    /// read, and zero for a file that fired no wall shots.
    ///
    /// <para>
    /// Published for the reason the two counts above are, and more pressingly, because it is much
    /// the largest of the three. Of fourteen measured files that use the placeholder at all, ten
    /// carry more than ten of them for every station somebody named, and one carries a hundred and
    /// eighty-five. A station count read without this beside it therefore looks like a reading that
    /// mislaid most of the cave: the two numbers together account for the file, and either alone
    /// does not. Nothing was lost — every one of those legs is still stored as a leg — but a
    /// shortfall nobody can account for is indistinguishable from one that should worry them.
    /// </para>
    /// </summary>
    int? AnonymousStationCount,
    /// <summary>
    /// Another reading of the stored file is queued or running, and the model goes on answering
    /// from the reading it holds until that one replaces it. Only ever true of a model that is
    /// ready: one that holds nothing says it is waiting through <see cref="Status"/> instead.
    /// </summary>
    bool ReadingAgain,
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
/// <param name="ViewerName">
/// What the drawing calls the same station. One of the two formats names its stations under the
/// file's root survey and the drawing leaves that part out, so the two spellings differ there and
/// are equal everywhere else. This is the spelling a report and a declared place keep, which is
/// why a chooser offers this one and not <paramref name="Name"/>: the name somebody picks is the
/// name they will then see on the model and in the log.
/// </param>
/// <param name="Flags">
/// What the file says about the station, one name per flag it set. A list rather than a single
/// value because these combine — an entrance station is above ground and underground at once.
/// </param>
public sealed record SurveyStationDto(
    string Name,
    string ViewerName,
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
            .WithSummary("Stations read out of the survey, optionally only those whose name begins with `q`; withheld without the exact-location permission.");
        api.MapGet("/survey-models/{id:guid}/shots", ShotsAsync)
            .WithTags("SurveyModels")
            .WithSummary("Legs read out of the survey; withheld without the exact-location permission.");
        api.MapPut("/survey-models/{id:guid}", UpdateAsync)
            .WithValidation<SurveyModelUpdateRequest>()
            .WithTags("SurveyModels")
            .WithSummary("Metadata update (Write on the cave).");
        api.MapPut("/survey-models/{id:guid}/current", MakeCurrentAsync)
            .WithTags("SurveyModels")
            .WithSummary("Makes this the model its cave is represented by, among the cave's models of the same kind (Write on the cave).");
        api.MapPost("/survey-models/{id:guid}/reading", ReadAgainAsync)
            .WithTags("SurveyModels")
            .WithSummary("Queues another reading of the model's stored file (Write on the cave). Refused while a reading is queued or running.");
        api.MapDelete("/survey-models/{id:guid}", DeleteAsync)
            .WithTags("SurveyModels")
            .WithSummary("Deletes the survey model (Write on the cave); the stored file is kept. Refused while a trip's live tracking is armed on it.");

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
        var sizes = await MeshSizesAsync(db, models, ct);
        var outstanding = await SurveyModelReading.OutstandingAsync(db, ct);
        return TypedResults.Ok(models.Select(m => m.ToDto(tokens, sizes, outstanding)).ToList());
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
        SurveyModelReading.QueueFirst(db, model, ctx.UserId);

        // The first model of a kind is the one the cave is represented by; a later one takes over
        // only when somebody says so. Arriving is not choosing: a second line plot may be a
        // corrected re-export or a survey of one side passage, and only its uploader knows which.
        await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (model.IsCurrent)
        {
            // Two first uploads of one kind for one cave, at once: the index let one of them have
            // the mark, and this one is stored without it rather than refused.
            model.IsCurrent = false;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Created($"/api/v1/survey-models/{model.Id}", model.ToDto(tokens, null, null));
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
        return TypedResults.Ok(model.ToDto(tokens, await MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }

    /// <summary>
    /// The most stations one search answers with, whatever page size was asked for.
    /// </summary>
    /// <remarks>
    /// A search exists to feed a chooser, which shows a short list under the field and asks again
    /// on the next keystroke. A surveyed cave holds tens of thousands of stations and a one-letter
    /// beginning can match most of them, so the list's own ceiling would send hundreds of rows per
    /// keystroke that nobody reads. The total is still reported, so a surface can say that there
    /// are more and that typing further narrows them.
    /// </remarks>
    internal const int MaxStationSearchPageSize = 50;

    /// <summary>The longest beginning a search is asked with: the longest name a station can have.</summary>
    private const int MaxStationSearchLength = 400;

    private static async Task<Ok<PagedResult<SurveyStationDto>>> StationsAsync(
        Guid id,
        int? page,
        int? pageSize,
        string? q,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        // The gate first and the search after it, never the other way round: a withheld survey
        // answers one empty page whatever was asked, so no beginning somebody tries can be told
        // apart from any other and the names cannot be felt out a letter at a time.
        var (paging, model) = await ReadableAsync(id, page, pageSize, db, access, protection, accessAccessor, ct);
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (search is not null)
        {
            paging = (paging.Page, Math.Min(paging.PageSize, MaxStationSearchPageSize));
        }

        if (model is null)
        {
            return TypedResults.Ok(EmptyPage<SurveyStationDto>(paging));
        }

        var stations = db.SurveyStations.AsNoTracking().Where(s => s.SurveyModelId == model.Id);
        if (search is not null)
        {
            if (search.Length > MaxStationSearchLength)
            {
                // Longer than any name can be, so it begins none of them. Answered rather than
                // refused: this is a field being typed into, not a form being submitted.
                return TypedResults.Ok(EmptyPage<SurveyStationDto>(paging));
            }

            // Somebody types the name they read off the drawing, and the rows may hold it under
            // the file's root survey. The spelling rule already says which stored names a given
            // name can mean, best reading first; a beginning is read the same way, so the search
            // finds a station under the name the drawing shows and under the name the rows hold.
            var readings = SurveyStationNames.StoredCandidates(model.Format, model.RootSurveyName, search);
            var asDrawn = BeginningWith(readings[0]);
            var asStored = BeginningWith(readings[^1]);

            // Case and accents are folded, as the application's other name searches fold them: a
            // name is typed from memory or from a relayed message, and surveys are not consistent
            // about capitals among themselves.
            stations = stations.Where(s =>
                EF.Functions.ILike(EF.Functions.Unaccent(s.Name), EF.Functions.Unaccent(asDrawn), LikeEscape)
                || EF.Functions.ILike(EF.Functions.Unaccent(s.Name), EF.Functions.Unaccent(asStored), LikeEscape));
        }

        return TypedResults.Ok(await stations
            .OrderBy(s => s.Name)
            .ToPagedAsync(paging.Page, paging.PageSize, s => ToDto(s, model), ct));
    }

    private const string LikeEscape = "\\";

    /// <summary>
    /// The pattern matching every name that begins with <paramref name="typed"/>, read literally.
    /// </summary>
    /// <remarks>
    /// Escaped because the characters a pattern treats as wildcards are ordinary in station names —
    /// an underscore separates the parts of a great many of them — and an unescaped one would turn
    /// "a_1" into a search for "ab1" as well.
    /// </remarks>
    private static string BeginningWith(string typed) =>
        typed.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
        + "%";

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
        return TypedResults.Ok(model.ToDto(tokens, await MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }

    /// <summary>
    /// Gives this model the cave's current mark for its kind, taking it from whichever model of
    /// that kind held it. Idempotent: marking the holder again changes nothing and still answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same gates as every other write to a model: the cave must be visible with its exact
    /// location open, and writable. No version precondition is asked for, because this is not an
    /// edit of the row's content that another edit could be lost under — it is a choice between
    /// rows, and the last choice made is the one that was meant.
    /// </para>
    /// <para>
    /// Only a model whose reading or conversion has finished can be chosen. For a line plot the
    /// choice also makes the centerline read out of it the cave's shape, so that the map and the
    /// figures move together; a model still being read has no centerline to hand the map, and
    /// choosing it would leave the map drawing one survey and the figures waiting on another.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> MakeCurrentAsync(
        Guid id,
        SilexGisDbContext db,
        FeatureWriteService writes,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

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

        if (model.Status != SurveyModelStatus.Ready)
        {
            return ApiProblems.Conflict(
                "survey_model.not_ready",
                "This model is still being read, or could not be. Only a model that has finished can be made the current one.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!model.IsCurrent)
            {
                await SurveyModelCurrency.MakeCurrentAsync(db, model, ct);
            }

            // The cave's shape follows the choice: the centerline read out of this model becomes
            // the default, whatever was the default before. Looked at on every call, not only when
            // the mark moved, so that choosing the holder again repairs a shape that had drifted.
            var own = await db.Centerlines
                .Where(c => c.SurveyModelId == model.Id)
                .OrderBy(c => c.Source == CenterlineSource.Extracted ? 0 : 1).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.IsDefault })
                .ToListAsync(ct);
            if (own.Count > 0 && !own.Any(c => c.IsDefault))
            {
                await writes.SetDefaultCenterlineAsync(own[0].Id, ct);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Ok(model.ToDto(tokens, await MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }

    /// <summary>
    /// Queues another reading of the file this model was uploaded as, and answers the model as it
    /// now stands: being read again if it held a reading, and otherwise waiting, with whatever the
    /// last reading complained of cleared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stored file is read once, when it arrives, and what that reading produced is what every
    /// later screen uses. So when the reader itself improves, or a reading failed for a reason
    /// that has since gone, the only way to a better answer used to be uploading the same file a
    /// second time — which makes a second model, with a second set of rows, and leaves every trip
    /// that was followed on the first one pointing at the old reading. This asks the question again
    /// of the model that is already there.
    /// </para>
    /// <para>
    /// The same gates as choosing the current model, and for the same reason: it is a write to the
    /// cave's surveys, so the cave must be visible with its exact location open, and writable. A
    /// caller who may not place the cave is told the model does not exist, and no answer of this
    /// route names a station, a survey or a position.
    /// </para>
    /// <para>
    /// A model that could not be read may be read again — that is the retry a failed reading never
    /// had — and goes back to waiting, since it holds nothing. A model that has been read stays
    /// ready and is answered as being read again: it goes on drawing, measuring and placing from
    /// the reading it holds until the new one replaces it, and keeps that reading if the new one
    /// fails. A model whose reading is queued or running is refused instead of queued a second
    /// time: the second job would read the same bytes into the same rows, and the person asking
    /// would learn nothing from being told it had been accepted.
    /// </para>
    /// <para>
    /// A watch running on the model does not stand in the way, and loses nothing while the reading
    /// waits. What it can lose is narrow and is the price of the correction: a reading by a newer
    /// reader may store a station under a different name than the earlier one did — a station its
    /// file gives no name is the case, stored under the label the viewer shows it by where it used
    /// to be stored under the file's number for it. A position already recorded at such a station
    /// keeps the name it was recorded under and no longer finds a row, so it stays on the record
    /// and is no longer drawn. Every station the file names keeps its name.
    /// </para>
    /// <para>
    /// A wall mesh is converted again by the same request, and its earlier conversion is replaced.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReadAgainAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

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

        // Asked of the database and not of the row loaded above: two requests arriving together
        // both loaded a finished model, and only one of them may queue the reading.
        if (await SurveyModelReading.QueueAgainAsync(db, model, ctx.UserId, ct)
            == SurveyReadingRequest.AlreadyInProgress)
        {
            return ApiProblems.Conflict(
                "survey_model.reading_in_progress",
                "This model is already being read. Wait for that reading to finish before asking for another.");
        }

        return TypedResults.Ok(model.ToDto(tokens, await MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
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

        // A party is being followed against this model right now: refused, and this is the one
        // place the refusal can be made.
        //
        // Deleting it succeeds in every technical sense — nothing the database knows about is
        // damaged, and the position rows keep their own cave anchor and their own record of the
        // survey they were measured in. What breaks is the live surface: the watch stays Armed,
        // goes on taking entries, exits and notes, and can place nobody, because the model the
        // coordinator presses stations on is gone. That is a rescue surface quietly losing the
        // ability to say where anyone is, discovered at the moment somebody tries to record a
        // position. Weighed against it, the cost of refusing is one deliberate act by the person
        // who was already about to take a deliberate act — close the watch, or point it at the
        // replacement survey — and a re-survey landing mid-trip is not blocked by any of this:
        // uploading a model never touches an existing one, so the new survey is already there.
        //
        // Armed only. A watch that is off or closed is following nobody, and a model of a cave
        // somebody once tracked a trip in must not become undeletable for ever.
        //
        // And the refusal has to carry a way out of itself, or it is a trap. "Close that watch"
        // is only an instruction to somebody who can find the watch. A watch stays armed
        // until a person ends it — a party that came out and nobody told the application is the
        // ordinary steady state, not an exception — so an abandoned one would otherwise make this
        // survey undeletable for good, by a refusal whose instruction the only person holding it
        // cannot carry out.
        //
        // So the trips are named, filtered by what this caller may read. Whoever may write a
        // cave's surveys is still not thereby entitled to learn which trips exist, which is why
        // the naming goes through the same visibility the trips' own routes use rather than
        // through the tracking rows: a caller who may not read the trip learns only that one
        // exists and is told who can end it. A full administrator reads every trip, so there is
        // always somebody for whom this refusal names its own remedy — which is what keeps a
        // survey deletable.
        var armedTripIds = await db.TripTrackings.AsNoTracking()
            .Where(t => t.SurveyModelId == model.Id && t.State == TripTrackingState.Armed)
            .Select(t => t.TripLogId)
            .ToListAsync(ct);
        if (armedTripIds.Count > 0)
        {
            // No context, no names — unreachable, because writing this cave's surveys was already
            // required above, but said as a branch rather than as a null-forgiving operator: the
            // one thing that must not happen here is a list assembled for a caller nobody read.
            var named = ctx is null
                ? []
                : await db.TripLogs.AsNoTracking()
                    .VisibleTo(ctx, AccessDomain.TripLogs)
                    .Where(t => armedTripIds.Contains(t.Id))
                    .OrderBy(t => t.TripDate).ThenBy(t => t.Id)
                    .Select(t => t.Title)
                    .Take(MaxNamedArmedTrips)
                    .ToListAsync(ct);
            return ApiProblems.Conflict("survey_model.tracking_armed",
                named.Count > 0
                    ? $"A trip's live tracking is armed on this survey model ({string.Join(", ", named)}). Close that watch, or point it at another model, before deleting this one."
                    : "A trip's live tracking is armed on this survey model, and it is a trip this account cannot read. Ask its team, or an administrator, to close that watch or point it at another model.",
                // The names as a member of their own, because the sentence above is written for a
                // person and gets translated: a client recovering them out of that prose would
                // break the moment the prose is reworded.
                "armedTrips", string.Join(", ", named));
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
        // The mark moves on to the newest model of the kind that is left, so the cave stays
        // represented by something named rather than by a fallback rule. Promotion is a separate
        // statement after the removal is saved, for the per-statement index check, and the two
        // share a transaction so a failure between them leaves the cave with its old model.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.SurveyModels.Remove(model);
            await db.SaveChangesAsync(ct);
            await SurveyModelCurrency.PromoteSuccessorAsync(db, model.CaveFeatureId, model.Format, ct);
            await transaction.CommitAsync(ct);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// How many armed watches a delete refusal names before it stops listing them.
    /// </summary>
    /// <remarks>
    /// A refusal is read at a glance and acted on; past a few names it stops being a route to a
    /// remedy and becomes a wall of text with the remedy somewhere in it. One is the ordinary
    /// answer, because one party is in one cave at a time.
    /// </remarks>
    private const int MaxNamedArmedTrips = 5;

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

    /// <summary>
    /// The byte length of each model's converted mesh, keyed by the converted file's id, for the
    /// models that have one. One query for a whole list rather than one per row.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, long>> MeshSizesAsync(
        SilexGisDbContext db, IReadOnlyList<SurveyModel> models, CancellationToken ct)
    {
        var converted = models.Where(m => m.ConvertedFileId is not null).Select(m => m.ConvertedFileId!.Value).ToList();
        if (converted.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        return await db.StoredFiles.AsNoTracking()
            .Where(f => converted.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.SizeBytes, ct);
    }

    private static SurveyModelDto ToDto(
        this SurveyModel m,
        IFileAccessTokenService tokens,
        IReadOnlyDictionary<Guid, long>? meshSizes,
        IReadOnlySet<Guid>? readingOutstanding) => new(
        m.Id,
        m.CaveFeatureId,
        m.Name,
        m.Format,
        m.FileId,
        m.Description,
        m.SurveyedAt,
        m.IsCurrent,
        FileUrl(tokens, m.FileId),
        m.Status,
        m.ProcessingError,
        m.ConvertedFileId is { } converted ? FileUrl(tokens, converted) : null,
        m.Anchor?.X,
        m.Anchor?.Y,
        m.AnchorHeightM,
        m.TriangleCount,
        m.ConvertedFileId is { } sized && meshSizes is not null && meshSizes.TryGetValue(sized, out var bytes) ? bytes : null,
        m.SourcePrecisionLost,
        m.DroppedShotCount,
        m.MergedStationCount,
        m.AnonymousStationCount,
        m.Status == SurveyModelStatus.Ready && readingOutstanding is not null && readingOutstanding.Contains(m.Id),
        m.CreatedAt,
        m.UpdatedAt);

    private static SurveyStationDto ToDto(SurveyStation s, SurveyModel model) => new(
        s.Name,
        SurveyStationNames.ViewerName(model.Format, model.RootSurveyName, s.Name),
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
