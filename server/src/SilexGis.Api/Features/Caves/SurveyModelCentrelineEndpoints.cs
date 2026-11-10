// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Features.Caves;

/// <summary>A scan's centreline: the scanner's path simplified into a line through the passages.</summary>
/// <param name="SurveyModelId">The point cloud the line was drawn for.</param>
/// <param name="Frame">
/// <c>tiles</c>: metres in the frame the scan's tiles are written in, so a viewer draws the line
/// over the points exactly as it draws the tiles. <c>world</c>: longitude and latitude in degrees
/// (WGS 84) and height in metres, through the scan's placement.
/// </param>
/// <param name="Coordinates">The vertices in order along the path, each three numbers in the frame named.</param>
/// <param name="LengthM">The line's length along its vertices, in metres.</param>
public sealed record SurveyModelCentrelineDto(
    Guid SurveyModelId,
    string Frame,
    IReadOnlyList<double[]> Coordinates,
    double LengthM);

/// <summary>
/// The centreline a scan's run drew from the scanner's path. Never part of the cave's survey: a
/// product of the scan, drawn beside its points in the scan viewer and on the map.
/// <para>
/// The gate is the survey model's own: Read on the cave with its exact location open, else the
/// model is not found. The line is the cave's shape and, once placed, its position, in whichever
/// frame it is asked for — exactly as the scanner's trajectory it was drawn from is withheld
/// without the exact location, so is this.
/// </para>
/// </summary>
public static class SurveyModelCentrelineEndpoints
{
    public const string TilesFrame = "tiles";
    public const string WorldFrame = "world";

    public static RouteGroupBuilder MapSurveyModelCentrelineEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/survey-models/{id:guid}/centreline", CentrelineAsync)
            .WithTags("SurveyModels")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The centreline a point cloud's run drew from the scanner's path: in the tiles' own frame (`frame=tiles`, the default) for the scan viewer, or as longitude, latitude and height through the scan's placement (`frame=world`) for the map. Withheld without the exact-location permission; refused before the scan is tiled, and in the world frame before it is placed.");
        return api;
    }

    private static async Task<Results<Ok<SurveyModelCentrelineDto>, UnauthorizedHttpResult, ProblemHttpResult>> CentrelineAsync(
        Guid id,
        string? frame,
        SilexGisDbContext db,
        IFileStore fileStore,
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

        var asked = string.IsNullOrEmpty(frame) ? TilesFrame : frame;
        if (asked is not (TilesFrame or WorldFrame))
        {
            return ApiProblems.BadRequest(
                "survey_model.centreline_frame_invalid", $"The frame is '{TilesFrame}' or '{WorldFrame}'.");
        }

        var model = await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        var cave = model is null ? null : await SurveyModelEndpoints.CaveFeatureAsync(db, model.CaveFeatureId, ct);
        if (model is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        var line = await ScanCentrelines.ReadAsync(db, fileStore, model.Id, ct);
        if (line is null || line.Count < 2)
        {
            return ApiProblems.NotFound("survey_model.centreline_not_found");
        }

        // Both frames are reached from the point the tiler wrote the tiles about; before the scan
        // is tiled there is no frame the line could be drawn in beside the points.
        if (PointCloudFacts.FromJson(model.PointCloud) is not { } facts || ScanPlacement.TilesZeroPoint(facts) is not { } zero)
        {
            return ApiProblems.Conflict(
                "survey_model.not_tiled", "The scan has not been tiled yet, so its centreline has no frame to be drawn in.");
        }

        IReadOnlyList<CentrelinePoint> drawn;
        if (asked == WorldFrame)
        {
            if (ScanAutoPlacement.Read(model) is not { } placement)
            {
                return ApiProblems.Conflict(
                    "survey_model.not_placed", "The scan has not been placed, so its centreline has no place on the map.");
            }

            drawn = ScanCentreline.Placed(line, zero, placement);
        }
        else
        {
            drawn = ScanCentreline.InTilesFrame(line, zero);
        }

        return TypedResults.Ok(new SurveyModelCentrelineDto(
            model.Id,
            asked,
            [.. drawn.Select(p => asked == WorldFrame
                ? new[] { Math.Round(p.X, 8), Math.Round(p.Y, 8), Math.Round(p.Z, 3) }
                : new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3) })],
            Math.Round(ScanCentreline.LengthM(line), 3)));
    }
}
