// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Where a point cloud's tiles are to stand: the position of their zero point — the cave's
/// entrance, as a rule, with the scan's start on it — the altitude of their zero plane, and the
/// bearing their own north is turned to.
/// </summary>
/// <param name="Longitude">Degrees east, WGS 84.</param>
/// <param name="Latitude">Degrees north, WGS 84.</param>
/// <param name="HeightM">The altitude the tiles' zero plane sits at, in metres.</param>
/// <param name="HeadingDeg">
/// The compass bearing the tiles' +Y axis is to point along, degrees clockwise from true north.
/// Any finite value is read; it is stored in [0, 360).
/// </param>
public sealed record SurveyModelPlacementRequest(
    double Longitude,
    double Latitude,
    double HeightM,
    double HeadingDeg);

public sealed class SurveyModelPlacementRequestValidator : AbstractValidator<SurveyModelPlacementRequest>
{
    public SurveyModelPlacementRequestValidator()
    {
        // Whether the three together are a place on earth a cave is at is the placement's own
        // rule, and this is where it is applied to a request. The per-member rules below say the
        // same thing one field at a time so that the answer names the field that is wrong; they
        // add no bound of their own.
        RuleFor(x => x)
            .Must(r => ScanPlacement.IsOnEarth(r.Longitude, r.Latitude, r.HeightM))
            .OverridePropertyName("placement")
            .WithMessage("The position and height are not a place on earth a cave is at.");
        RuleFor(x => x.Longitude).Must(double.IsFinite).InclusiveBetween(-180, 180);
        RuleFor(x => x.Latitude).Must(double.IsFinite).InclusiveBetween(-90, 90);
        RuleFor(x => x.HeightM).Must(double.IsFinite)
            .InclusiveBetween(ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM);
        RuleFor(x => x.HeadingDeg).Must(double.IsFinite);
    }
}

/// <summary>
/// One pair a scan is to be placed by: a point picked in the scan and the station of the cave's
/// current line plot it stands on.
/// </summary>
/// <param name="ScanX">The picked point, metres from the tiles' zero point along the tiles' +X.</param>
/// <param name="ScanY">The same, along +Y.</param>
/// <param name="ScanZ">The same, along +Z.</param>
/// <param name="Station">
/// The station, by the name the drawing shows or the name the line plot's rows hold; either is
/// read, as the stations route reads them.
/// </param>
public sealed record SurveyModelControlPointRequest(double ScanX, double ScanY, double ScanZ, string Station);

/// <summary>Placing a scan by control points.</summary>
/// <param name="Pairs">At least two pairs; at least three to fit a tilt.</param>
/// <param name="FitTilt">
/// Fit a small tilt as well as the heading. Off by default: a scanner with an inertial unit levels
/// its recording better than a few hand-picked points can.
/// </param>
public sealed record SurveyModelControlPointsRequest(
    IReadOnlyList<SurveyModelControlPointRequest> Pairs,
    bool FitTilt = false);

public sealed class SurveyModelControlPointsRequestValidator : AbstractValidator<SurveyModelControlPointsRequest>
{
    public const int MaxStationLength = 400;

    public SurveyModelControlPointsRequestValidator()
    {
        RuleFor(x => x.Pairs).NotNull();
        RuleFor(x => x.Pairs.Count)
            .InclusiveBetween(ScanControlPoints.MinimumPairs, ScanControlPoints.MaximumPairs)
            .OverridePropertyName("pairs")
            .When(x => x.Pairs is not null);
        RuleFor(x => x.Pairs.Count)
            .GreaterThanOrEqualTo(ScanControlPoints.MinimumTiltPairs)
            .OverridePropertyName("pairs")
            .WithMessage($"A tilt is fitted from at least {ScanControlPoints.MinimumTiltPairs} pairs.")
            .When(x => x.Pairs is not null && x.FitTilt);
        // ChildRules treats a null element as valid, and the handler would then dereference it.
        RuleForEach(x => x.Pairs).NotNull()
            .OverridePropertyName("pairs")
            .WithMessage("A pair must not be empty.")
            .When(x => x.Pairs is not null);
        RuleForEach(x => x.Pairs).ChildRules(pair =>
        {
            pair.RuleFor(p => p.ScanX).Must(double.IsFinite);
            pair.RuleFor(p => p.ScanY).Must(double.IsFinite);
            pair.RuleFor(p => p.ScanZ).Must(double.IsFinite);
            pair.RuleFor(p => p.Station).NotEmpty().MaximumLength(MaxStationLength);
        }).When(x => x.Pairs is not null);
    }
}

/// <summary>One pair a scan was placed by, and how far apart its two ends ended up.</summary>
/// <param name="Station">The station's name as the line plot's rows hold it.</param>
/// <param name="DistanceM">The residual in space, metres; what <paramref name="Flagged"/> is judged on.</param>
/// <param name="Flagged">Over the installation's warning distance: probably paired with the wrong station.</param>
public sealed record SurveyModelControlPointDto(
    double ScanX,
    double ScanY,
    double ScanZ,
    string Station,
    double EastM,
    double NorthM,
    double UpM,
    double HorizontalM,
    double DistanceM,
    bool Flagged);

/// <summary>The control points a scan's placement was fitted to.</summary>
/// <param name="LinePlotId">The line plot whose stations the pairs name.</param>
/// <param name="RmsM">The root mean square of the residuals, metres.</param>
/// <param name="WarnM">The warning distance the pairs were flagged against, metres.</param>
/// <param name="TiltFitted">Whether a small tilt was fitted as well as the heading.</param>
public sealed record SurveyModelControlPointsDto(
    Guid LinePlotId,
    IReadOnlyList<SurveyModelControlPointDto> Pairs,
    double RmsM,
    double WarnM,
    bool TiltFitted)
{
    internal static SurveyModelControlPointsDto? Of(ScanControlRecord? record) =>
        record is null
            ? null
            : new(
                record.LinePlotId,
                record.Pairs.Select(p => new SurveyModelControlPointDto(
                    p.ScanX, p.ScanY, p.ScanZ, p.Station, p.EastM, p.NorthM, p.UpM, p.HorizontalM, p.DistanceM, p.Flagged)).ToList(),
                record.RmsM,
                record.WarnM,
                record.TiltFitted);
}

/// <summary>
/// Placing a scan, and placing it again.
/// <para>
/// A scan's tiles are made once, in the cloud's own frame, and its placement is a row — an
/// anchor, a height and a heading — that a scene composes into the matrix it draws them by. So
/// placing a scan, correcting its placement and taking its placement away are edits of the model
/// and queue nothing; the tiles, their archive and the address they are served at are the same
/// before and after. That is the opposite of a wall mesh, whose placement is written into its
/// vertices by its conversion, which is why these routes refuse every model that is not a point
/// cloud rather than quietly recording a placement nothing would draw by.
/// </para>
/// <para>
/// The gates are the survey model's: the cave must be readable with its exact location open,
/// else the model is not found; and writable, else forbidden. Reading the placement back needs
/// the same exact-location right, through every route that publishes a model — the anchor is the
/// cave's position by another name.
/// </para>
/// </summary>
public static class SurveyModelPlacementEndpoints
{
    public static RouteGroupBuilder MapSurveyModelPlacementEndpoints(this RouteGroupBuilder api)
    {
        api.MapPut("/survey-models/{id:guid}/placement", PlaceAsync)
            .WithValidation<SurveyModelPlacementRequest>()
            .WithTags("SurveyModels")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Places a point cloud, or places it again: its tiles' zero point at a position and altitude, its frame turned to a bearing (Write on the cave, with its exact location). The tiles are not touched. Refused for a line plot or a wall mesh.");
        api.MapPut("/survey-models/{id:guid}/placement/control-points", PlaceByControlPointsAsync)
            .WithValidation<SurveyModelControlPointsRequest>()
            .WithTags("SurveyModels")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Places a point cloud by pairs of a point picked in the scan and a station of the cave's current line plot: the translation and heading (and, when asked, a small tilt) that bring the points closest to their stations, stored with each pair's residual and their RMS; pairs over the installation's warning distance are flagged (Write on the cave, with its exact location). The tiles are not touched.");
        api.MapDelete("/survey-models/{id:guid}/placement", UnplaceAsync)
            .WithTags("SurveyModels")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Takes a point cloud's placement away, leaving it viewable in its own frame and off the map (Write on the cave, with its exact location).");
        return api;
    }

    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> PlaceAsync(
        Guid id,
        SurveyModelPlacementRequest request,
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
        var cave = model is null ? null : await SurveyModelEndpoints.CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (!SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud).Contains(model.Format))
        {
            return ApiProblems.Conflict(
                "survey_model.not_placeable",
                "Only a point cloud is placed this way. A line plot's position is read out of its file, and a wall mesh is placed when it is uploaded.");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        ScanAutoPlacement.Apply(model, ScanPlacement.Manual(request.Longitude, request.Latitude, request.HeightM, request.HeadingDeg));
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(model.ToDto(
            tokens, await SurveyModelEndpoints.MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }

    private const string StationNotFound = "survey_model.control_points_station_not_found";

    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> PlaceByControlPointsAsync(
        Guid id,
        SurveyModelControlPointsRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<ScanOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        var cave = model is null ? null : await SurveyModelEndpoints.CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (!SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud).Contains(model.Format))
        {
            return ApiProblems.Conflict(
                "survey_model.not_placeable",
                "Only a point cloud is placed this way. A line plot's position is read out of its file, and a wall mesh is placed when it is uploaded.");
        }

        // The stations are the cave's own current line plot's: the survey the cave is drawn and
        // measured by, already georeferenced and corrected for declination. The same gate that let
        // the caller this far — the cave readable with its exact location — is the one its
        // stations are read under, so nothing is read here that the stations route would not give.
        var linePlotFormats = SurveyModelKinds.FormatsOf(SurveyModelKind.LinePlot).ToList();
        var linePlot = await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == model.CaveFeatureId && m.IsCurrent && linePlotFormats.Contains(m.Format))
            .Select(m => new { m.Id, m.Format, m.RootSurveyName })
            .FirstOrDefaultAsync(ct);
        if (linePlot is null)
        {
            return ApiProblems.Conflict(
                "survey_model.control_points_no_line_plot",
                "The cave has no current line plot, so there are no stations to place the scan by.");
        }

        var readings = request.Pairs
            .Select(p => SurveyStationNames.StoredCandidates(linePlot.Format, linePlot.RootSurveyName, p.Station.Trim()))
            .ToList();
        var wanted = readings.SelectMany(r => r).Distinct(StringComparer.Ordinal).ToList();
        var found = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == linePlot.Id && wanted.Contains(s.Name))
            .Select(s => new { s.Name, s.Position })
            .ToListAsync(ct);
        var byName = found.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var pairs = new List<ScanControlPair>();
        var stations = new List<string>();
        var missing = new List<string>();
        for (var i = 0; i < request.Pairs.Count; i++)
        {
            var hit = readings[i].Select(name => byName.GetValueOrDefault(name)).FirstOrDefault(s => s is not null);
            if (hit is null)
            {
                missing.Add(request.Pairs[i].Station);
                continue;
            }

            var pair = request.Pairs[i];
            pairs.Add(new ScanControlPair(
                pair.ScanX, pair.ScanY, pair.ScanZ,
                hit.Position.X, hit.Position.Y, double.IsFinite(hit.Position.Z) ? hit.Position.Z : 0));
            stations.Add(hit.Name);
        }

        if (missing.Count > 0)
        {
            return ApiProblems.BadRequest(
                StationNotFound,
                "The cave's current line plot has no station by " + (missing.Count == 1 ? "this name." : "these names."),
                "stations",
                missing);
        }

        var outcome = ScanControlPoints.Fit(pairs, new ScanControlOptions(request.FitTilt, options.Value.ControlPointWarn));
        if (outcome.Fit is not { } fit)
        {
            var first = outcome.Problems[0];
            return ApiProblems.BadRequest(first.Code, string.Join(" ", outcome.Problems.Select(p => p.Message)));
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        ScanAutoPlacement.ApplyFit(model, fit, ScanControlRecord.Of(linePlot.Id, pairs, stations, fit));
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(model.ToDto(
            tokens, await SurveyModelEndpoints.MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }

    private static async Task<Results<Ok<SurveyModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> UnplaceAsync(
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
        var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        var cave = model is null ? null : await SurveyModelEndpoints.CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (!SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud).Contains(model.Format))
        {
            return ApiProblems.Conflict(
                "survey_model.not_placeable",
                "Only a point cloud's placement can be taken away; a line plot's and a wall mesh's positions are part of the model.");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.SurveyModels, model.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        ScanAutoPlacement.Clear(model);
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct);
        return TypedResults.Ok(model.ToDto(
            tokens, await SurveyModelEndpoints.MeshSizesAsync(db, [model], ct), await SurveyModelReading.OutstandingAsync(db, ct)));
    }
}
