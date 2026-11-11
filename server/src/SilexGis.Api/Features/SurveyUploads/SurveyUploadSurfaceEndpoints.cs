// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>Asks for a surface to be made of a processed scan and saved as a cave's wall mesh.</summary>
/// <param name="CaveId">The cave the surface is saved for; the scan's own cave when null.</param>
/// <param name="ProfileId">The surface step's processing profile; the step's own defaults when null.</param>
public sealed record SurveyUploadSurfaceRequest(Guid? CaveId = null, Guid? ProfileId = null);

public sealed class SurveyUploadSurfaceRequestValidator : AbstractValidator<SurveyUploadSurfaceRequest>
{
    public SurveyUploadSurfaceRequestValidator()
    {
        RuleFor(x => x.CaveId).NotEqual(Guid.Empty).When(x => x.CaveId is not null);
        RuleFor(x => x.ProfileId).NotEqual(Guid.Empty).When(x => x.ProfileId is not null);
    }
}

/// <summary>
/// "Make a surface": the worker reconstructs the walls of a processed scan as a mesh, and the mesh
/// is saved as a wall mesh of the cave the person chose — the scan's own by default — standing
/// where the scan stands.
/// </summary>
/// <remarks>
/// <para>
/// The run is the scan's own run asked to go on one step further: every earlier step's product is
/// reused, and only the surface is made. So it is refused while the scan's processing is queued or
/// running, like a rerun.
/// </para>
/// <para>
/// The surface takes the scan's placement, which is the cave's position by another name, and it
/// writes a model of the chosen cave. So both caves are asked what an upload of a wall mesh asks:
/// the caller must be able to place each (its exact location open to them) and write each. A cave
/// the caller may not place is answered exactly as a missing one, before the write check, so the
/// answer never tells a guarded cave apart from an absent one.
/// </para>
/// </remarks>
public static partial class SurveyUploadEndpoints
{
    /// <summary>The upload is not a scan, or has no tiles yet: there is nothing to make a surface of.</summary>
    public const string SurfaceNeedsTilesCode = "survey_upload.surface_needs_tiles";

    /// <summary>The scan is not placed, so a surface of it would have nowhere to stand.</summary>
    public const string SurfaceNeedsPlacementCode = "survey_upload.surface_needs_placement";

    /// <summary>The profile named is not one for the surface step.</summary>
    public const string SurfaceProfileStepCode = "survey_upload.surface_profile_step";

    private static void MapSurfaceEndpoints(RouteGroupBuilder uploads)
    {
        uploads.MapPost("/{id:guid}/surface", SurfaceAsync)
            .WithName("makeSurveyUploadSurface")
            .AddEndpointFilter<ContractFilter>()
            .WithValidation<SurveyUploadSurfaceRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Makes a surface of a processed, placed scan with a processing profile, and saves it as a wall mesh of the chosen cave (the scan's own by default).");
    }

    private static async Task<Results<Accepted<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> SurfaceAsync(
        Guid id,
        SurveyUploadSurfaceRequest? request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        IOptions<SurveyUploadOptions> options,
        ITypedPropertiesValidator schemas,
        ProcessingProfileResolver profiles,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null || !await SurveyModelAccess.LocationOpenAsync(protection, ctx, found.Cave.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        switch (upload.Status)
        {
            case SurveyUploadStatus.Queued or SurveyUploadStatus.Running:
                return ApiProblems.Conflict(RunInProgressCode, "This upload's processing is already queued or running.");
            case SurveyUploadStatus.Receiving or SurveyUploadStatus.Abandoned:
                return ApiProblems.Conflict(NotCompletedCode, "Only an upload whose files all arrived can be made into a surface.");
            default:
                break;
        }

        var scan = ScanOrigins.IsScan(upload.Source)
            ? await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.SurveyUploadId == upload.Id && m.PointCloud != null, ct)
            : null;
        var facts = scan is null ? null : PointCloudFacts.FromJson(scan.PointCloud);
        if (scan is null || facts is null || ScanPlacement.TilesZeroPoint(facts) is null)
        {
            return ApiProblems.Conflict(SurfaceNeedsTilesCode, "A surface is made of a scan that has been processed and tiled.");
        }

        if (ScanAutoPlacement.Read(scan) is null)
        {
            return ApiProblems.Conflict(SurfaceNeedsPlacementCode, "Place the scan first: its surface is saved standing where the scan stands.");
        }

        // The cave the walls are saved for: asked what an upload of a wall mesh to it would be asked.
        var caveId = request?.CaveId ?? found.Cave.Id;
        var cave = caveId == found.Cave.Id ? found.Cave : await CaveAsync(db, caveId, ct);
        if (cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct)
            || !await SurveyModelAccess.LocationOpenAsync(protection, ctx, cave.Id, ct))
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var current = ScanRecipes.FromOptionsJson(upload.Options)
            ?? (upload.Source == SurveyOrigin.Mandeye
                ? PointCloudRecipes.ForRecording(schemas, null, null, null, out _)!
                : PointCloudRecipes.Default(schemas));
        var recipe = PointCloudRecipes.WithSurface(current, new ScanSurfaceRequest(cave.Id, ctx.UserId));
        var (resolution, profileProblem) = await profiles.ApplyAsync(
            ctx, recipe, request?.ProfileId is { } chosen ? [chosen] : null, ct);
        if (profileProblem is not null)
        {
            return ApiProblems.BadRequest(profileProblem.Code, profileProblem.Detail);
        }

        // A profile of another step the scan's recipe runs would be laid over that step instead.
        if (request?.ProfileId is not null && !resolution!.Chosen.ContainsKey(MeshSurfaceStep.StepId))
        {
            return ApiProblems.BadRequest(SurfaceProfileStepCode, "The profile named is not a profile of the surface step.");
        }

        upload.Options = ScanRecipes.ToOptionsJson(resolution!.Recipe);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await SurveyUploadWrites.ClearRunAsync(db, upload.Id, ct))
            {
                return ApiProblems.Conflict(RunInProgressCode, "This upload's processing is already queued or running.");
            }

            upload.Status = SurveyUploadStatus.Queued;
            Queue(db, upload);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await profiles.RememberAsync(ctx.UserId, resolution.Chosen, ct);
        await db.Entry(upload).ReloadAsync(ct);

        return Accepted(await ToDtoAsync(db, tokens, links, options.Value, upload, found.Cave, writable: true, ct));
    }
}
