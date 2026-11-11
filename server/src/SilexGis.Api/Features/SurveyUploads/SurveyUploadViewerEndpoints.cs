// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>What a page of one upload's results shows as figures.</summary>
/// <param name="Compiler">
/// What the compiler's log says about the run — sizes, counts, loop closures and the stations and
/// positions it prints with them — or null when the run wrote no log, or what it wrote is not a log
/// a compiler printed.
/// </param>
/// <param name="Placed">
/// Whether the run placed the survey in the world — whether the line work route has a line to give.
/// </param>
/// <param name="Analyses">
/// The upload's own survey measured: its shape, its network, which way it runs and its depth along
/// the passage — computed over the legs this upload's own model was read into, never over the
/// survey the cave is currently drawn from. Null when the run's model was not read into legs: no
/// model, a reading not finished, or a survey that could not be placed.
/// </param>
public sealed record SurveyUploadFiguresDto(SurveyLogFiguresDto? Compiler, bool Placed, SurveyUploadAnalysesDto? Analyses);

/// <summary>
/// The figures a compiler's log reports, as the log states them: each range with the stations at
/// its ends and their absolute height, northing or easting; each loop with the stations it runs
/// through; the coordinate system and declinations the compiler worked with.
/// </summary>
/// <param name="Outcome">How the run ended, as its log says.</param>
/// <param name="CompilerVersion">The compiler's version, as it announced itself.</param>
/// <param name="CompilationSeconds">How long the run took, as the compiler said.</param>
/// <param name="ErrorCount">Errors the compiler reported.</param>
/// <param name="WarningCount">Warnings the compiler reported.</param>
/// <param name="OutputCoordinateSystem">The coordinate system the output was written in, as the log names it.</param>
/// <param name="MeridianConvergenceDeg">The meridian convergence applied, in degrees.</param>
/// <param name="Declinations">The magnetic declinations used, each with its date.</param>
/// <param name="HasNoControlPoints">True when the survey has no fixed point and the compiler invented one, so its heights and grid positions are relative.</param>
/// <param name="StationCount">Survey stations.</param>
/// <param name="ShotCount">Shots joining them.</param>
/// <param name="ConnectedComponents">Pieces of the survey not joined to each other.</param>
/// <param name="TotalLengthM">Total length of the shots, in metres.</param>
/// <param name="TotalLengthAdjustedM">The same after the loops were adjusted.</param>
/// <param name="PlanLengthM">Total length seen from above.</param>
/// <param name="VerticalLengthM">Total vertical length of the shots.</param>
/// <param name="VerticalRange">Height between the highest and the lowest station, with both stations and their heights.</param>
/// <param name="NorthSouthRange">Reach north to south, with the stations at its ends and their northings.</param>
/// <param name="EastWestRange">Reach east to west, with the stations at its ends and their eastings.</param>
/// <param name="LoopCount">Loops in the survey.</param>
/// <param name="AverageLoopErrorPercent">The average relative loop error.</param>
/// <param name="Loops">Each loop's closure, in the compiler's order.</param>
public sealed record SurveyLogFiguresDto(
    SurveyCompilationOutcome Outcome,
    string? CompilerVersion,
    int? CompilationSeconds,
    int ErrorCount,
    int WarningCount,
    string? OutputCoordinateSystem,
    double? MeridianConvergenceDeg,
    IReadOnlyList<SurveyLogDeclinationDto> Declinations,
    bool HasNoControlPoints,
    int? StationCount,
    int? ShotCount,
    int? ConnectedComponents,
    double? TotalLengthM,
    double? TotalLengthAdjustedM,
    double? PlanLengthM,
    double? VerticalLengthM,
    SurveyLogExtentDto? VerticalRange,
    SurveyLogExtentDto? NorthSouthRange,
    SurveyLogExtentDto? EastWestRange,
    int? LoopCount,
    double? AverageLoopErrorPercent,
    IReadOnlyList<SurveyLogLoopDto> Loops);

/// <summary>One extent of the survey and the stations at its ends.</summary>
/// <param name="LengthM">The extent, in metres.</param>
/// <param name="FromStation">The station at one end.</param>
/// <param name="FromValueM">Its height, northing or easting, in the compiler's coordinate system.</param>
/// <param name="ToStation">The station at the other end.</param>
/// <param name="ToValueM">Its height, northing or easting.</param>
public sealed record SurveyLogExtentDto(double LengthM, string FromStation, double FromValueM, string ToStation, double ToValueM);

/// <summary>How one loop closes.</summary>
/// <param name="RelativeErrorPercent">The misclosure as a share of the loop's length.</param>
/// <param name="AbsoluteErrorM">The misclosure, in metres.</param>
/// <param name="LengthM">The loop's length.</param>
/// <param name="StationCount">Stations it runs through.</param>
/// <param name="ErrorXM">The misclosure's east component, in metres.</param>
/// <param name="ErrorYM">Its north component.</param>
/// <param name="ErrorZM">Its vertical component.</param>
/// <param name="Stations">The stations it runs through, as the compiler lists them.</param>
public sealed record SurveyLogLoopDto(
    double RelativeErrorPercent,
    double AbsoluteErrorM,
    double LengthM,
    int StationCount,
    double ErrorXM,
    double ErrorYM,
    double ErrorZM,
    string Stations);

/// <summary>A magnetic declination the compiler used.</summary>
/// <param name="Date">The date it was taken for, as the log writes it.</param>
/// <param name="Degrees">The declination, in degrees.</param>
public sealed record SurveyLogDeclinationDto(string Date, double Degrees);

/// <summary>
/// The upload's own survey measured, as the cave page measures the cave's current survey — the same
/// arithmetic over the legs of this upload's own model.
/// </summary>
/// <param name="SurveyModelId">The model the figures were measured over: this upload's compiled model, else its Survex file.</param>
/// <param name="HasAltitudes">Whether the legs carry heights; without them every vertical figure is refused.</param>
/// <param name="Statistics">Lengths, extents and shape ratios, and the sinuosity of the longest paths.</param>
/// <param name="Orientation">Which way and how steeply the passage runs.</param>
/// <param name="DepthProfile">Depth below the highest surveyed point against distance along the passage.</param>
/// <param name="Topology">The shape of the passage network, as measured when the model was read; null when it holds nothing that joins up.</param>
public sealed record SurveyUploadAnalysesDto(
    Guid SurveyModelId,
    bool HasAltitudes,
    SurveyUploadStatisticsDto Statistics,
    SurveyUploadOrientationDto Orientation,
    SurveyUploadDepthProfileDto DepthProfile,
    SurveyUploadTopologyDto? Topology);

/// <summary>The upload's survey's lengths, extents and shape ratios.</summary>
/// <param name="Basis">Which body of line work answered: always the survey file's own legs here.</param>
/// <param name="HasAltitudes">Whether the legs carry heights.</param>
/// <param name="Indices">The figures.</param>
/// <param name="Paths">The longest paths' sinuosity, longest first.</param>
public sealed record SurveyUploadStatisticsDto(
    SurveySegmentBasis Basis,
    bool HasAltitudes,
    CaveIndexSummary Indices,
    IReadOnlyList<PathSinuosity> Paths);

/// <summary>Which way and how steeply the upload's survey runs.</summary>
/// <param name="Basis">Which body of line work answered.</param>
/// <param name="HasAltitudes">Whether the legs carry heights; without them there is no dip.</param>
/// <param name="SegmentCount">Legs that move in plan and so have a bearing.</param>
/// <param name="TotalLengthM">Their total length.</param>
/// <param name="ByCount">The mean axis counting legs.</param>
/// <param name="ByLength">The mean axis weighting legs by length.</param>
/// <param name="Bins">The rose diagram's sectors.</param>
/// <param name="Dip">How steeply the passage runs; null without heights.</param>
public sealed record SurveyUploadOrientationDto(
    SurveySegmentBasis Basis,
    bool HasAltitudes,
    int SegmentCount,
    double TotalLengthM,
    OrientationMeasure ByCount,
    OrientationMeasure ByLength,
    IReadOnlyList<OrientationBin> Bins,
    DipSummary? Dip);

/// <summary>Depth against distance along the upload's survey.</summary>
/// <param name="Basis">Which body of line work answered.</param>
/// <param name="HasAltitudes">Whether the legs carry heights; without them there is no profile.</param>
/// <param name="PassageLengthM">The passage walked.</param>
/// <param name="HighestM">The highest surveyed point.</param>
/// <param name="LowestM">The lowest.</param>
/// <param name="DeepestM">The depth between them.</param>
/// <param name="Samples">The profile, in passage order.</param>
public sealed record SurveyUploadDepthProfileDto(
    SurveySegmentBasis Basis,
    bool HasAltitudes,
    double PassageLengthM,
    double? HighestM,
    double? LowestM,
    double? DeepestM,
    IReadOnlyList<SurveyUploadDepthSampleDto> Samples);

/// <summary>One point of the depth profile.</summary>
/// <param name="DistanceAlongM">Distance along the passage.</param>
/// <param name="AltitudeM">Height at that point.</param>
/// <param name="DepthM">Depth below the highest surveyed point.</param>
/// <param name="PathIndex">The path it lies on, where the line work has paths.</param>
/// <param name="SegmentIndex">The leg it lies on.</param>
public sealed record SurveyUploadDepthSampleDto(double DistanceAlongM, double AltitudeM, double DepthM, int? PathIndex, int SegmentIndex);

/// <summary>
/// The shape of the upload's survey network, as stored when its model was read. The members mean
/// what the cave page's network figures mean; they are measured over this upload's model.
/// </summary>
public sealed record SurveyUploadTopologyDto(
    int? DroppedShotCount,
    int? MergedStationCount,
    int NodeCount,
    int EdgeCount,
    int ComponentCount,
    int ReducedNodeCount,
    int ReducedEdgeCount,
    int ReducedComponentCount,
    int CyclomaticNumber,
    int ExtremityCount,
    int JunctionCount,
    double? Alpha,
    double? Beta,
    double? Gamma,
    double? MeanDegree,
    double? DegreeStandardDeviation,
    double? DegreeCoefficientOfVariation,
    double? CorrelationOfVertexDegree,
    int BranchCount,
    int LoopingBranchCount,
    double? MeanBranchLengthM,
    double? BranchLengthCoefficientOfVariation,
    double? MinBranchLengthM,
    double? MaxBranchLengthM,
    double? LengthEntropy,
    double? OrientationEntropy,
    double? MeanTortuosity,
    double? AverageShortestPathLength,
    double? CentralPointDominance,
    double? AverageClusteringCoefficient,
    DateTime ComputedAt);

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

    /// <summary>How many paths the sinuosity breakdown carries, longest first, as on the cave page.</summary>
    private const int AnalysisPathRowLimit = 20;

    /// <summary>The formats an upload's line work is offered in, by the name the query uses.</summary>
    private static readonly Dictionary<string, (ExportFormat Format, string ContentType, string Extension)> GeodataFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["geojson"] = (ExportFormat.GeoJson, "application/geo+json", "geojson"),
            ["kml"] = (ExportFormat.Kml, "application/vnd.google-earth.kml+xml", "kml"),
            ["gpx"] = (ExportFormat.Gpx, "application/gpx+xml", "gpx"),
        };

    /// <summary>
    /// The routes a page of one upload's results reads beyond the status: the compiler's figures
    /// with the upload's own survey measured, the survey's line work in the world's coordinates,
    /// and — for a public link's reader who is signed in — the way to the same upload in the
    /// application.
    ///
    /// <para>
    /// None of them is opened by an access key: they serve the web's pages, not the device.
    /// </para>
    ///
    /// <para>
    /// <b>A public link shows what the upload's own files contain, and nothing else.</b> The link
    /// was made by somebody entitled to share the cave, and it already hands out the compiled model
    /// and the compiler's log, which place every station and print the survey's extremes in the
    /// world's coordinates. So the same link is given the same things in the forms a page shows
    /// them: the whole figures, the survey measured, and the line work as a map and as KML, GeoJSON
    /// or GPX. Every one of those is derived from this upload's files alone — the line is the one
    /// its own model was read into, placed by the coordinate system its own files declare — and
    /// never from the cave's record: the cave's stored entrance, its other surveys and every other
    /// cave stay behind the ordinary location protection on every route. The link's open decision
    /// runs first on every read, so a withdrawn link, a cave protected since an ordinary link was
    /// made and a switched-off installation close all of these at once.
    /// </para>
    ///
    /// <para>
    /// <para>
    /// <b>Signed in, the same rule: whoever may open the upload's sent files gets what derives from
    /// them.</b> The figures, the line work and whether the run placed it follow exactly the terms the
    /// upload and its sent files answer on — Read on the cave and its exact position — and nothing
    /// more is asked of them: in particular not the line's own protection as a feature, because the
    /// line is the same information the sent files and the compiled model already carry. What is
    /// stored about the cave itself — its entrance, its other surveys, its centrelines as features —
    /// is untouched by this and stays behind the ordinary protection on its own routes.
    /// </para>
    /// </para>
    /// </summary>
    private static void MapViewerEndpoints(RouteGroupBuilder api)
    {
        var uploads = api.MapGroup("/survey-uploads").WithTags("SurveyUploads");

        uploads.MapGet("/{id:guid}/figures", FiguresAsync)
            .WithName("surveyUploadFigures")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("What the compiler's log of an upload's run says about the survey, and the upload's own survey measured: shape, network, orientation and depth profile. On the upload's own terms.");

        uploads.MapGet("/{id:guid}/geodata", GeodataAsync)
            .WithName("surveyUploadGeodata")
            .Produces(StatusCodes.Status200OK, contentType: "application/geo+json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The upload's survey line work in WGS 84, as GeoJSON, KML or GPX (`format`, default geojson), on the terms the upload's sent files answer on, and no other. 404 survey_upload.not_georeferenced when the run placed no line in the world.");

        uploads.MapGet("/by-public-link/{token}", InAppAsync)
            .WithName("surveyUploadByPublicLink")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("For a signed-in reader of a public link: the same upload's page and its cave's in the application, when the link opens and the reader may see the upload on its own terms. Otherwise the link's ordinary 404.");

        // The figures and the line work of a public link's page, each decided by the decision its
        // page is, on every read.
        api.MapGet("/public/survey-uploads/{token}/figures", PublicFiguresAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUploadFigures")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("What the compiler's log of the upload behind a public link says about the survey, whether the run placed it, and the upload's own survey measured. Opens and refuses exactly as the page does.");

        api.MapGet("/public/survey-uploads/{token}/geodata", PublicGeodataAsync)
            .WithTags("SurveyUploads")
            .WithName("publicSurveyUploadGeodata")
            .AllowAnonymous()
            .RequireRateLimiting(PublicSurveyUploadRateLimits.PolicyName)
            .Produces(StatusCodes.Status200OK, contentType: "application/geo+json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The line work of the upload behind a public link, in WGS 84, as GeoJSON, KML or GPX (`format`, default geojson): the line the upload's own model was read into. Opens and refuses exactly as the page does; 404 survey_upload.not_georeferenced when the run placed no line in the world.");
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

        var placed = await UploadLineWorkAsync(db, found.Upload.Id, ct) is not null;
        return TypedResults.Ok(new SurveyUploadFiguresDto(
            await FiguresOfAsync(db, fileStore, reader, found.Upload.Id, ct),
            placed,
            await AnalysesOfAsync(db, found.Upload.Id, ct)));
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

        return TypedResults.Ok(new SurveyUploadFiguresDto(
            await FiguresOfAsync(db, fileStore, reader, found.Upload.Id, ct),
            await UploadLineWorkAsync(db, found.Upload.Id, ct) is not null,
            await AnalysesOfAsync(db, found.Upload.Id, ct)));
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

        if (GeodataFormatOf(format) is not { } target)
        {
            return UnsupportedFormat(format);
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await UploadLineWorkAsync(db, found.Upload.Id, ct) is not { } line)
        {
            return ApiProblems.NotFound(NotGeoreferencedCode);
        }

        var (file, failed) = await LineWorkFileAsync(db, vectorIO, found.Upload, found.Cave, line, target, ct);
        return file is not null ? file : failed!;
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> PublicGeodataAsync(
        string token,
        string? format,
        SilexGisDbContext db,
        SurveyUploadPublicLinkService links,
        IAccessContextAccessor accessAccessor,
        IVectorIO vectorIO,
        CancellationToken ct)
    {
        // The link decides first, so an invented or withdrawn token answers its one 404 whatever
        // format it was asked in, and a format is judged only for a link that opens.
        var (opened, refusal) = await OpenPublicLinkAsync(token, links, accessAccessor, ct);
        if (opened is not { } found)
        {
            return refusal!;
        }

        if (GeodataFormatOf(format) is not { } target)
        {
            return UnsupportedFormat(format);
        }

        // The line the upload's own model was read into, with no further check of the line as a
        // feature: it is what the files the link hands out already place — as on the signed-in route.
        if (await UploadLineWorkAsync(db, found.Upload.Id, ct) is not { } line)
        {
            return ApiProblems.NotFound(NotGeoreferencedCode);
        }

        var (file, failed) = await LineWorkFileAsync(db, vectorIO, found.Upload, found.Cave, line, target, ct);
        return file is not null ? file : failed!;
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

    private static (ExportFormat Format, string ContentType, string Extension)? GeodataFormatOf(string? format) =>
        GeodataFormats.TryGetValue(string.IsNullOrWhiteSpace(format) ? "geojson" : format.Trim(), out var target) ? target : null;

    private static ProblemHttpResult UnsupportedFormat(string? format) =>
        ApiProblems.BadRequest(
            GeodataFormatCode,
            $"Unsupported format '{format?.Trim()}'. Supported: {string.Join(", ", GeodataFormats.Keys)}.");

    /// <summary>
    /// The line work as a file to save, named after the survey and stamped with the upload's
    /// version — or the problem to answer when the writer refuses the line.
    /// </summary>
    private static async Task<(FileContentHttpResult? File, ProblemHttpResult? Failed)> LineWorkFileAsync(
        SilexGisDbContext db,
        IVectorIO vectorIO,
        SurveyUpload upload,
        Feature cave,
        UploadLineWork line,
        (ExportFormat Format, string ContentType, string Extension) target,
        CancellationToken ct)
    {
        var title = await db.CaveSurveys.AsNoTracking()
            .Where(s => s.Id == upload.CaveSurveyId)
            .Select(s => s.Title)
            .FirstOrDefaultAsync(ct) ?? upload.SurveyTitle;
        var properties = new Dictionary<string, object?>
        {
            ["name"] = title,
            ["cave"] = cave.Name,
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
            return (null, ApiProblems.BadRequest("export.failed", e.Message));
        }

        var stamp = upload.VersionAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return (TypedResults.File(bytes, target.ContentType, $"{SafeFileName(title)}-{stamp}.{target.Extension}"), null);
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
                f.OutputCoordinateSystem,
                f.MeridianConvergenceDeg,
                [.. f.Declinations.Select(d => new SurveyLogDeclinationDto(d.Date, d.Degrees))],
                f.HasNoControlPoints,
                f.StationCount,
                f.ShotCount,
                f.ConnectedComponents,
                f.TotalLengthM,
                f.TotalLengthAdjustedM,
                f.PlanLengthM,
                f.VerticalLengthM,
                ExtentDto(f.VerticalRange),
                ExtentDto(f.NorthSouthRange),
                ExtentDto(f.EastWestRange),
                f.LoopCount,
                f.AverageLoopErrorPercent,
                [.. f.Loops.Select(l => new SurveyLogLoopDto(
                    l.RelativeErrorPercent, l.AbsoluteErrorM, l.LengthM, l.StationCount, l.ErrorXM, l.ErrorYM, l.ErrorZM, l.Stations))])
            : null;
    }

    private static SurveyLogExtentDto? ExtentDto(SurveyLogExtent? e) =>
        e is null ? null : new SurveyLogExtentDto(e.LengthM, e.FromStation, e.FromValueM, e.ToStation, e.ToValueM);

    /// <summary>
    /// The upload's own survey measured, or null when none of its models was read into legs. The
    /// same arithmetic the cave page runs over the cave's current survey, run here over the model
    /// this upload's files produced — its compiled 3D file first, else its Survex file, the order
    /// its line work is chosen in — so a page about this upload never describes a different one.
    /// Computed on every ask, as the cave page's are; nothing is stored beyond the network figures
    /// the reading already kept.
    /// </summary>
    private static async Task<SurveyUploadAnalysesDto?> AnalysesOfAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct)
    {
        foreach (var modelId in await UploadModelIdsAsync(db, uploadId, ct))
        {
            var set = await SurveySegmentSource.ForModelAsync(db, modelId, ct);
            if (set.Segments.Count == 0)
            {
                continue;
            }

            var passage = SurveySegments.AsPassage(set.Segments);
            var indices = CaveSurveyIndices.Compute(passage);
            var paths = CaveSurveyIndices.PathSinuosities(passage)
                .OrderByDescending(p => p.LengthM)
                .Take(AnalysisPathRowLimit)
                .ToList();
            var (orientation, dip) = SurveySegments.Orientation(set);
            var profile = CaveDepthProfile.Build(passage);
            var topology = await SurveyTopologySql.ForModelAsync(db, modelId, ct);

            return new SurveyUploadAnalysesDto(
                modelId,
                set.HasZ,
                new SurveyUploadStatisticsDto(set.Basis, indices.HasAltitudes, indices, paths),
                new SurveyUploadOrientationDto(
                    set.Basis,
                    set.HasZ,
                    orientation.SampleCount,
                    orientation.TotalLengthM,
                    orientation.ByCount,
                    orientation.ByLength,
                    orientation.Bins,
                    dip),
                new SurveyUploadDepthProfileDto(
                    set.Basis,
                    set.HasZ,
                    profile.PassageLengthM,
                    profile.HighestM,
                    profile.LowestM,
                    profile.DeepestM,
                    [.. profile.Samples.Select(p => new SurveyUploadDepthSampleDto(
                        p.DistanceAlongM, p.AltitudeM, p.DepthM, p.PathIndex, p.SegmentIndex))]),
                topology is null ? null : TopologyDto(topology));
        }

        return null;
    }

    private static SurveyUploadTopologyDto TopologyDto(SurveyTopologyRow t) => new(
        t.DroppedShotCount,
        t.MergedStationCount,
        t.NodeCount,
        t.EdgeCount,
        t.ComponentCount,
        t.ReducedNodeCount,
        t.ReducedEdgeCount,
        t.ReducedComponentCount,
        t.CyclomaticNumber,
        t.ExtremityCount,
        t.JunctionCount,
        t.Alpha,
        t.Beta,
        t.Gamma,
        t.MeanDegree,
        t.DegreeStandardDeviation,
        t.DegreeCoefficientOfVariation,
        t.CorrelationOfVertexDegree,
        t.BranchCount,
        t.LoopingBranchCount,
        t.MeanBranchLengthM,
        t.BranchLengthCoefficientOfVariation,
        t.MinBranchLengthM,
        t.MaxBranchLengthM,
        t.LengthEntropy,
        t.OrientationEntropy,
        t.MeanTortuosity,
        t.AverageShortestPathLength,
        t.CentralPointDominance,
        t.AverageClusteringCoefficient,
        t.ComputedAt);

    /// <summary>The survey models the upload's run produced, its compiled 3D file's first.</summary>
    private static async Task<IReadOnlyList<Guid>> UploadModelIdsAsync(SilexGisDbContext db, Guid uploadId, CancellationToken ct)
    {
        var models = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == uploadId
                && f.Role == SurveyUploadFileRole.Output
                && f.Status == SurveyUploadFileStatus.Stored
                && f.SurveyModelId != null)
            .Select(f => new { f.Output, Model = f.SurveyModelId!.Value })
            .ToListAsync(ct);
        return [.. models.OrderBy(m => m.Output == "lox" ? 0 : 1).Select(m => m.Model).Distinct()];
    }

    private sealed record UploadLineWork(Geometry Geometry, decimal? LengthM);

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
        return chosen is null ? null : new UploadLineWork(chosen.Skeleton ?? chosen.Geom!, chosen.LengthM);
    }

    /// <summary>A survey title as a file name: letters, digits, dashes and underscores, never empty.</summary>
    private static string SafeFileName(string title)
    {
        var kept = new string([.. title.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')]).Trim('_');
        return kept.Length == 0 ? "survey" : kept.Length > 80 ? kept[..80] : kept;
    }
}
