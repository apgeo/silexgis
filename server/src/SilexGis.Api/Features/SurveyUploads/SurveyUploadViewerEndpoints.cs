// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>What a page of one upload's results shows as figures.</summary>
/// <param name="Compiler">
/// What the compiler's log says about the run — sizes, counts and loop closures, never a position —
/// or null when the run wrote no log, or what it wrote is not a log a compiler printed.
/// </param>
/// <param name="Placed">
/// On the signed-in route, whether the run placed the survey in the world where this reader may be
/// shown it — whether the line work route has a line to give them. Null through a public link, which
/// is offered no line work and is told nothing about one.
/// </param>
public sealed record SurveyUploadFiguresDto(SurveyLogFiguresDto? Compiler, bool? Placed);

/// <summary>
/// The figures a compiler's log reports. Every range is its length alone: the log also says at
/// which stations and at what absolute height, northing and easting each extreme lies, and that is
/// left out, as are the stations each loop runs through.
/// </summary>
/// <param name="Outcome">How the run ended, as its log says.</param>
/// <param name="CompilerVersion">The compiler's version, as it announced itself.</param>
/// <param name="CompilationSeconds">How long the run took, as the compiler said.</param>
/// <param name="ErrorCount">Errors the compiler reported.</param>
/// <param name="WarningCount">Warnings the compiler reported.</param>
/// <param name="StationCount">Survey stations.</param>
/// <param name="ShotCount">Shots joining them.</param>
/// <param name="ConnectedComponents">Pieces of the survey not joined to each other.</param>
/// <param name="TotalLengthM">Total length of the shots, in metres.</param>
/// <param name="TotalLengthAdjustedM">The same after the loops were adjusted.</param>
/// <param name="PlanLengthM">Total length seen from above.</param>
/// <param name="VerticalLengthM">Total vertical length of the shots.</param>
/// <param name="VerticalRangeM">Height between the highest and the lowest station.</param>
/// <param name="NorthSouthRangeM">Reach north to south.</param>
/// <param name="EastWestRangeM">Reach east to west.</param>
/// <param name="LoopCount">Loops in the survey.</param>
/// <param name="AverageLoopErrorPercent">The average relative loop error.</param>
/// <param name="Loops">Each loop's closure, in the compiler's order.</param>
public sealed record SurveyLogFiguresDto(
    SurveyCompilationOutcome Outcome,
    string? CompilerVersion,
    int? CompilationSeconds,
    int ErrorCount,
    int WarningCount,
    int? StationCount,
    int? ShotCount,
    int? ConnectedComponents,
    double? TotalLengthM,
    double? TotalLengthAdjustedM,
    double? PlanLengthM,
    double? VerticalLengthM,
    double? VerticalRangeM,
    double? NorthSouthRangeM,
    double? EastWestRangeM,
    int? LoopCount,
    double? AverageLoopErrorPercent,
    IReadOnlyList<SurveyLogLoopDto> Loops);

/// <summary>How one loop closes.</summary>
/// <param name="RelativeErrorPercent">The misclosure as a share of the loop's length.</param>
/// <param name="AbsoluteErrorM">The misclosure, in metres.</param>
/// <param name="LengthM">The loop's length.</param>
/// <param name="StationCount">Stations it runs through.</param>
public sealed record SurveyLogLoopDto(double RelativeErrorPercent, double AbsoluteErrorM, double LengthM, int StationCount);

/// <summary>Where a signed-in reader of a public link finds the same upload in the application.</summary>
/// <param name="StatusPageUrl">The upload's own page in the application.</param>
/// <param name="CaveUrl">The cave's page.</param>
public sealed record SurveyUploadInAppDto(string StatusPageUrl, string CaveUrl);

public static partial class SurveyUploadEndpoints
{
    public const string NotGeoreferencedCode = "survey_upload.not_georeferenced";
    public const string GeodataFormatCode = "survey_upload.geodata_format_unsupported";

    /// <summary>
    /// The most of a log the figures are read from. A compiler's log is tens of kilobytes; one far
    /// past this is not a log anybody wants read on a page load, and the page simply has no figures.
    /// </summary>
    private const long MaxFiguresLogBytes = 16L * 1024 * 1024;

    /// <summary>The formats an upload's line work is offered in, by the name the query uses.</summary>
    private static readonly Dictionary<string, (ExportFormat Format, string ContentType, string Extension)> GeodataFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["geojson"] = (ExportFormat.GeoJson, "application/geo+json", "geojson"),
            ["kml"] = (ExportFormat.Kml, "application/vnd.google-earth.kml+xml", "kml"),
            ["gpx"] = (ExportFormat.Gpx, "application/gpx+xml", "gpx"),
        };

    /// <summary>
    /// The routes a page of one upload's results reads beyond the status: the compiler's figures,
    /// the survey's line work in the world's coordinates, and — for a public link's reader who is
    /// signed in — the way to the same upload in the application.
    ///
    /// <para>
    /// None of them is opened by an access key: they serve the web's pages, not the device.
    /// </para>
    ///
    /// <para>
    /// <b>A public link is given figures and no line work.</b> The figures are sizes, which the
    /// link's holder already has in the log and the model it hands out. The line work is a set of
    /// positions in the world's coordinates, and the public page has always been promised to carry no
    /// position of the cave's own: so it is served on the signed-in route alone, on the survey
    /// terms every upload route answers on — Read on the cave and its exact position — and the
    /// public page offers it to nobody. That is the strictest rule already in force, kept until
    /// somebody decides otherwise; opening it is one more route over <see cref="UploadLineWorkAsync"/>
    /// after the link has opened.
    /// </para>
    /// </summary>
    private static void MapViewerEndpoints(RouteGroupBuilder api)
    {
        var uploads = api.MapGroup("/survey-uploads").WithTags("SurveyUploads");

        uploads.MapGet("/{id:guid}/figures", FiguresAsync)
            .WithName("surveyUploadFigures")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("What the compiler's log of an upload's run says about the survey: lengths, ranges, counts and loop closures, never a position. On the upload's own terms.");

        uploads.MapGet("/{id:guid}/geodata", GeodataAsync)
            .WithName("surveyUploadGeodata")
            .Produces(StatusCodes.Status200OK, contentType: "application/geo+json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The upload's survey line work in WGS 84, as GeoJSON, KML or GPX (`format`, default geojson), on the upload's own terms and the line's own exact-position check. 404 survey_upload.not_georeferenced when the run placed no line in the world.");

        uploads.MapGet("/by-public-link/{token}", InAppAsync)
            .WithName("surveyUploadByPublicLink")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("For a signed-in reader of a public link: the same upload's page and its cave's in the application, when the link opens and the reader may see the upload on its own terms. Otherwise the link's ordinary 404.");

        // The figures of a public link's page, decided by the decision its page is, on every read.
        api.MapGet("/public/survey-uploads/{token}/figures", PublicFiguresAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUploadFigures")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("What the compiler's log of the upload behind a public link says about the survey: sizes, counts and loop closures, never a position. Opens and refuses exactly as the page does.");
    }

    private static async Task<Results<Ok<SurveyUploadFiguresDto>, UnauthorizedHttpResult, ProblemHttpResult>> FiguresAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileStore fileStore,
        SurveyCompilationLogReader reader,
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

        var placed = await ShownLineWorkAsync(db, protection, ctx, found.Upload.Id, ct) is not null;
        return TypedResults.Ok(new SurveyUploadFiguresDto(await FiguresOfAsync(db, fileStore, reader, found.Upload.Id, ct), placed));
    }

    private static async Task<Results<Ok<SurveyUploadFiguresDto>, ProblemHttpResult>> PublicFiguresAsync(
        string token,
        SilexGisDbContext db,
        SurveyUploadPublicLinkService links,
        IAccessContextAccessor accessAccessor,
        IFileStore fileStore,
        SurveyCompilationLogReader reader,
        CancellationToken ct)
    {
        var (opened, refusal) = await OpenPublicLinkAsync(token, links, accessAccessor, ct);
        if (opened is not { } found)
        {
            return refusal!;
        }

        return TypedResults.Ok(new SurveyUploadFiguresDto(await FiguresOfAsync(db, fileStore, reader, found.Upload.Id, ct), null));
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> GeodataAsync(
        Guid id,
        string? format,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IVectorIO vectorIO,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var name = string.IsNullOrWhiteSpace(format) ? "geojson" : format.Trim();
        if (!GeodataFormats.TryGetValue(name, out var target))
        {
            return ApiProblems.BadRequest(
                GeodataFormatCode,
                $"Unsupported format '{name}'. Supported: {string.Join(", ", GeodataFormats.Keys)}.");
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await ShownLineWorkAsync(db, protection, ctx, found.Upload.Id, ct) is not { } line)
        {
            return ApiProblems.NotFound(NotGeoreferencedCode);
        }

        var title = await db.CaveSurveys.AsNoTracking()
            .Where(s => s.Id == found.Upload.CaveSurveyId)
            .Select(s => s.Title)
            .FirstOrDefaultAsync(ct) ?? found.Upload.SurveyTitle;
        var properties = new Dictionary<string, object?>
        {
            ["name"] = title,
            ["cave"] = found.Cave.Name,
            ["kind"] = "centerline",
            ["length_m"] = line.LengthM is { } length ? (double)length : null,
        };

        byte[] bytes;
        try
        {
            bytes = vectorIO.Write(target.Format, "centerline", [new VectorFeature(line.Geometry, properties)]);
        }
        catch (VectorIOException e)
        {
            return ApiProblems.BadRequest("export.failed", e.Message);
        }

        var stamp = found.Upload.VersionAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.File(bytes, target.ContentType, $"{SafeFileName(title)}-{stamp}.{target.Extension}");
    }

    private static async Task<Results<Ok<SurveyUploadInAppDto>, UnauthorizedHttpResult, ProblemHttpResult>> InAppAsync(
        string token,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        SurveyUploadPublicLinkService links,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The link has to open, and the reader has to be one the upload answers on its own: holding
        // the address grants the page, never the upload's place in the application, which names
        // the cave by its id and opens everything the cave's own page holds.
        var (opened, _) = await OpenPublicLinkAsync(token, links, accessAccessor, ct);
        if (opened is not { } link
            || await VisibleUploadAsync(db, access, protection, ctx, link.Upload.Id, ct) is not { } found)
        {
            return ApiProblems.NotFound(PublicLinkNotFoundCode);
        }

        return TypedResults.Ok(new SurveyUploadInAppDto(
            SurveyUploadLinks.StatusPage(found.Cave.Id, found.Upload.CaveSurveyId, found.Upload.Id),
            $"/caves/{found.Cave.Id}"));
    }

    /// <summary>The run's figures, read from its stored log on every ask; none when there is no log.</summary>
    private static async Task<SurveyLogFiguresDto?> FiguresOfAsync(
        SilexGisDbContext db, IFileStore fileStore, SurveyCompilationLogReader reader, Guid uploadId, CancellationToken ct)
    {
        if (await PublicLogFileAsync(db, uploadId, ct) is not { } log || log.SizeBytes > MaxFiguresLogBytes)
        {
            return null;
        }

        string text;
        await using (var input = await fileStore.OpenReadAsync(log.StoragePath, ct))
        using (var stream = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = await stream.ReadToEndAsync(ct);
        }

        return reader.ReadFigures(log.Name, text) is { } f
            ? new SurveyLogFiguresDto(
                f.Outcome,
                f.CompilerVersion,
                f.CompilationSeconds,
                f.ErrorCount,
                f.WarningCount,
                f.StationCount,
                f.ShotCount,
                f.ConnectedComponents,
                f.TotalLengthM,
                f.TotalLengthAdjustedM,
                f.PlanLengthM,
                f.VerticalLengthM,
                f.VerticalRangeM,
                f.NorthSouthRangeM,
                f.EastWestRangeM,
                f.LoopCount,
                f.AverageLoopErrorPercent,
                [.. f.Loops.Select(l => new SurveyLogLoopDto(l.RelativeErrorPercent, l.AbsoluteErrorM, l.LengthM, l.StationCount))])
            : null;
    }

    private sealed record UploadLineWork(Guid FeatureId, Geometry Geometry, decimal? LengthM);

    /// <summary>
    /// The run's line work as this reader may be shown it, or null. The upload's terms already ask
    /// for the cave's exact position; the line is a feature of its own and asks again, so a line
    /// that sits under a protection the cave does not share is still withheld — whole, because a
    /// line cannot be blurred into a safe version of itself — and withheld exactly as a run that
    /// placed no line is answered, so nothing says whether there was one.
    /// </summary>
    private static async Task<UploadLineWork?> ShownLineWorkAsync(
        SilexGisDbContext db, FeatureProtection protection, AccessContext ctx, Guid uploadId, CancellationToken ct)
    {
        var line = await UploadLineWorkAsync(db, uploadId, ct);
        return line is not null && (await protection.ExactViewIdsAsync(ctx, [line.FeatureId], ct)).Count > 0 ? line : null;
    }

    /// <summary>
    /// The line the run's own model was read into, placed in the world, or null when it placed none:
    /// a survey with neither a fixed point nor a coordinate system of its own cannot be placed, and
    /// its reading writes no line. The model of the compiled 3D file is preferred over the Survex
    /// file's, which describe the same survey. The display skeleton is served where there is one —
    /// the traverse without its splay shots — because a file read into a map or a GPS wants the
    /// passage, not a fan of wall shots at every station.
    /// </summary>
    private static async Task<UploadLineWork?> UploadLineWorkAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct)
    {
        var models = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == uploadId
                && f.Role == SurveyUploadFileRole.Output
                && f.Status == SurveyUploadFileStatus.Stored
                && f.SurveyModelId != null)
            .Select(f => new { f.Output, Model = f.SurveyModelId!.Value })
            .ToListAsync(ct);
        if (models.Count == 0)
        {
            return null;
        }

        var ids = models.Select(m => m.Model).ToList();
        var lines = await (
            from centerline in db.Centerlines.AsNoTracking()
            join feature in db.Features.AsNoTracking() on centerline.Id equals feature.Id
            where centerline.SurveyModelId != null && ids.Contains(centerline.SurveyModelId.Value)
                && centerline.Source == CenterlineSource.Extracted
                && feature.DeletedAt == null && feature.Geom != null
            select new { centerline.Id, Model = centerline.SurveyModelId!.Value, centerline.Skeleton, feature.Geom, centerline.LengthM })
            .ToListAsync(ct);

        var chosen = models
            .OrderBy(m => m.Output == "lox" ? 0 : 1)
            .Select(m => lines.FirstOrDefault(l => l.Model == m.Model))
            .FirstOrDefault(l => l is not null);
        return chosen is null ? null : new UploadLineWork(chosen.Id, chosen.Skeleton ?? chosen.Geom!, chosen.LengthM);
    }

    /// <summary>A survey title as a file name: letters, digits, dashes and underscores, never empty.</summary>
    private static string SafeFileName(string title)
    {
        var kept = new string([.. title.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')]).Trim('_');
        return kept.Length == 0 ? "survey" : kept.Length > 80 ? kept[..80] : kept;
    }
}
