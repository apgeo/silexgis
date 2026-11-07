// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
