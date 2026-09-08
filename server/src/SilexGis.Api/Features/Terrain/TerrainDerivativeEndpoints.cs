// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Terrain;

/// <summary>One file of a computed picture of the ground, and where to fetch it.</summary>
/// <param name="Url">
/// A signed address for the raster itself. A tile reader in a browser fetches ranges of the file
/// and can put no credential on those requests, so the permission is decided when this address is
/// built and travels inside it. It expires, which is why a listing is re-read periodically rather
/// than held.
/// </param>
public sealed record TerrainDerivativeRasterDto(
    long Id,
    double West,
    double South,
    double East,
    double North,
    int Width,
    int Height,
    double PixelSizeDegrees,
    long SizeBytes,
    string Url);

/// <summary>
/// One computed picture of the ground: what it is, what it was drawn from, and whether the ground
/// it was drawn from is still the ground this installation serves.
/// </summary>
/// <param name="Stale">
/// True when the elevation this was computed from is no longer the active build. Served rather
/// than worked out in the browser, because it is two facts joined and a caller holding only one of
/// them has no way to know it is missing the other. A stale picture is still shown — withdrawing
/// it would leave a reader with nothing over ground that has probably not changed — but it must
/// never be shown as current.
/// </param>
public sealed record TerrainDerivativeLayerDto(
    Guid Id,
    Guid TerrainBuildId,
    TerrainDerivative Derivative,
    string Name,
    string Settings,
    TerrainDerivativeStatus Status,
    string? ErrorCode,
    string? Message,
    int Version,
    long SizeBytes,
    bool Stale,
    DateTimeOffset? ComputedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TerrainDerivativeRasterDto> Rasters);

/// <summary>Asks for one picture of the ground to be computed from one elevation build.</summary>
public sealed record TerrainDerivativeCreateRequest(
    Guid TerrainBuildId,
    TerrainDerivative Derivative,
    string Name,
    TerrainHillshadeLighting? Lighting,
    double? AzimuthDegrees,
    double? AltitudeDegrees,
    double? ZFactor,
    TerrainSurfaceFit? SurfaceFit,
    TerrainSlopeUnit? SlopeUnit,
    TerrainRuggednessFit? RuggednessFit,
    bool? ComputeEdges,
    IReadOnlyList<TerrainColourStop>? ColourRamp)
{
    /// <summary>The request as the settings record everything downstream reads.</summary>
    public TerrainDerivativeSettings ToSettings() => new()
    {
        Derivative = Derivative,
        Lighting = Lighting ?? TerrainHillshadeLighting.Single,
        AzimuthDegrees = AzimuthDegrees ?? 315d,
        AltitudeDegrees = AltitudeDegrees ?? 45d,
        ZFactor = ZFactor ?? 1d,
        SurfaceFit = SurfaceFit ?? TerrainSurfaceFit.Horn,
        SlopeUnit = SlopeUnit ?? TerrainSlopeUnit.Degrees,
        RuggednessFit = RuggednessFit ?? TerrainRuggednessFit.Riley,
        ComputeEdges = ComputeEdges ?? true,
        ColourRamp = ColourRamp ?? [],
    };
}

public sealed class TerrainDerivativeCreateRequestValidator
    : AbstractValidator<TerrainDerivativeCreateRequest>
{
    public TerrainDerivativeCreateRequestValidator()
    {
        RuleFor(x => x.TerrainBuildId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        // The settings are checked by the one rule that decides what can be computed, rather than
        // by a second copy of it written in validator form. A second copy is how a request the
        // computation refuses gets accepted, queued and then failed by a background job, which the
        // person who asked reads as a broken installation rather than as a bad request.
        RuleFor(x => x).Custom((request, context) =>
        {
            var problem = TerrainDerivativeRules.Problem(request.ToSettings());
            if (problem is not null)
            {
                context.AddFailure(nameof(TerrainDerivativeCreateRequest.Derivative), problem);
            }
        });
    }
}

/// <summary>
/// The computed pictures of the ground: asking for one, listing them with whether they are still
/// current, and handing out the rasters themselves.
/// </summary>
/// <remarks>
/// Gated on the terrain right throughout, and on nothing else. These are pictures of the surface,
/// computed from public elevation, and they hold no cave position — so they carry no owner, no
/// audience and no visibility, and there is deliberately nothing here that annotates them with
/// where caves are. Doing that would put cave positions behind a permission that exists to govern
/// an installation asset.
/// </remarks>
public static class TerrainDerivativeEndpoints
{
    public const string NotFoundCode = "terrain_derivative.not_found";
    public const string ForbiddenCode = "access.forbidden";

    /// <summary>The build asked for does not exist, or has nothing to draw the ground from.</summary>
    public const string BuildNotFoundCode = "terrain_derivative.build_not_found";

    /// <summary>The picture is not finished, so there is nothing to fetch yet.</summary>
    public const string NotReadyCode = "terrain_derivative.not_ready";

    public static RouteGroupBuilder MapTerrainDerivativeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/terrain/derivatives", ListAsync)
            .WithTags("Terrain")
            .WithSummary("Computed pictures of the ground, each saying whether the elevation beneath it has since been replaced; requires Read on the Terrain domain.");
        api.MapPost("/terrain/derivatives", CreateAsync)
            .WithValidation<TerrainDerivativeCreateRequest>()
            .WithTags("Terrain")
            .WithSummary("Asks for a picture of the ground to be computed from an elevation build; requires Execute on the Terrain domain.");
        api.MapDelete("/terrain/derivatives/{id:guid}", DeleteAsync)
            .WithTags("Terrain")
            .WithSummary("Removes a computed picture and the rasters it left on disk; requires Delete on the Terrain domain.");
        api.MapGet("/terrain/derivatives/{id:guid}/rasters/{rasterId:long}/content", ContentAsync)
            .AllowAnonymous() // opened by the signed address handed out with the listing, not by a header
            .WithTags("Terrain")
            .WithSummary("One computed raster, for a tile reader; opened by the signed address the listing hands out.");
        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<TerrainDerivativeLayerDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TerrainDerivativeCatalogue catalogue,
        ITerrainRasterTokenService tokens,
        Guid? buildId,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var views = buildId is { } id
            ? await catalogue.ForBuildAsync(id, ct)
            : await catalogue.AllAsync(ct);

        var ids = views.Select(v => v.Id).ToList();
        var rasters = await db.TerrainDerivativeRasters.AsNoTracking()
            .Where(r => ids.Contains(r.TerrainDerivativeLayerId))
            .OrderBy(r => r.TerrainDerivativeLayerId).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var byLayer = rasters.GroupBy(r => r.TerrainDerivativeLayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dtos = views
            .Select(view => ToDto(
                view,
                byLayer.TryGetValue(view.Id, out var own) ? own : [],
                tokens))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<TerrainDerivativeLayerDto>>(dtos);
    }

    /// <summary>
    /// Records the request and queues the run that answers it.
    /// </summary>
    /// <remarks>
    /// A request for a picture already asked for over the same build answers with the row that
    /// exists rather than a second one. Two pictures computed from the same elevation with the same
    /// settings are the same file twice, and terrain sits on a volume that is neither swept nor
    /// backed up.
    /// </remarks>
    private static async Task<Results<Ok<TerrainDerivativeLayerDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TerrainDerivativeCreateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TerrainDerivativeCatalogue catalogue,
        ITerrainRasterTokenService tokens,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var build = await db.TerrainBuilds.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.TerrainBuildId, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(BuildNotFoundCode);
        }

        var settings = TerrainDerivativeRegistry.Normalise(request.ToSettings());
        var hash = TerrainDerivativeRegistry.Fingerprint(settings);

        var existing = await db.TerrainDerivativeLayers
            .FirstOrDefaultAsync(
                l => l.TerrainBuildId == request.TerrainBuildId
                    && l.Derivative == request.Derivative
                    && l.SettingsHash == hash,
                ct);

        if (existing is null)
        {
            existing = new TerrainDerivativeLayer
            {
                TerrainBuildId = request.TerrainBuildId,
                Derivative = settings.Derivative,
                Settings = TerrainDerivativeRegistry.Describe(settings),
                SettingsHash = hash,
                Name = request.Name,
            };
            db.TerrainDerivativeLayers.Add(existing);
        }
        else if (existing.Status == TerrainDerivativeStatus.Failed)
        {
            // Asking again for a picture whose last run failed is asking for it to be tried again.
            existing.Status = TerrainDerivativeStatus.Queued;
            existing.ErrorCode = null;
            existing.Message = null;
        }
        else
        {
            var stored = await catalogue.FindAsync(existing.Id, ct);
            return TypedResults.Ok(ToDto(stored!, await RastersOfAsync(db, existing.Id, ct), tokens));
        }

        // The row and the run it needs commit together: a row with no job is a picture that never
        // gets drawn, and a job naming a row that was never written fails for a reason nobody asked
        // about.
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.TerrainDerivative,
            Payload = JsonSerializer.Serialize(
                new TerrainDerivativePayload(existing.Id), JsonSerializerOptions.Web),
            RequestedBy = ctx.UserId,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsSameRequestRace(e))
        {
            // Two administrators asking for the same picture of the same ground in the same
            // instant both look, both find nothing, and both try to write. The index refuses the
            // second, and the honest answer to it is the row that won — the same answer it would
            // have had a moment later. The refused write took its queued run down with it, so the
            // picture is asked for once and drawn once.
            db.ChangeTracker.Clear();
            var raced = await db.TerrainDerivativeLayers.AsNoTracking().FirstAsync(
                l => l.TerrainBuildId == request.TerrainBuildId
                    && l.Derivative == request.Derivative
                    && l.SettingsHash == hash,
                ct);

            var won = await catalogue.FindAsync(raced.Id, ct);
            return TypedResults.Ok(ToDto(won!, await RastersOfAsync(db, raced.Id, ct), tokens));
        }

        var view = await catalogue.FindAsync(existing.Id, ct);
        return TypedResults.Ok(ToDto(view!, await RastersOfAsync(db, existing.Id, ct), tokens));
    }

    /// <summary>The index that holds one request for one picture of one build.</summary>
    private const string RequestUniqueIndex = "ux_terrain_derivative_layers_request";

    /// <summary>
    /// Whether a refused write is a second request for exactly the same picture arriving at the
    /// same moment, rather than a fault worth reporting as one.
    /// </summary>
    private static bool IsSameRequestRace(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RequestUniqueIndex,
        };

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TerrainWorkspace workspace,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Delete, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var row = await db.TerrainDerivativeLayers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var buildId = row.TerrainBuildId;
        db.TerrainDerivativeLayers.Remove(row);
        await db.SaveChangesAsync(ct);

        // After the commit, and uncancelled: rows are what the application answers from, and files
        // left behind by a sweep that did not run are litter rather than a wrong answer. The other
        // order — files first — can leave a row promising rasters that are gone.
        workspace.RemoveDerivative(buildId, id);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Hands over one computed raster to whatever is drawing it.
    /// </summary>
    /// <remarks>
    /// Reached without a credential, because a tile reader cannot present one; the signed address
    /// is the permission, and it was signed only for a caller who had the terrain right at the time
    /// the listing was read. Nothing here re-decides anything, and a token that does not verify is
    /// answered exactly as a raster that does not exist is — whether these bytes exist is part of
    /// what a refusal keeps back.
    /// </remarks>
    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ContentAsync(
        Guid id,
        long rasterId,
        string? token,
        SilexGisDbContext db,
        ITerrainRasterTokenService tokens,
        CancellationToken ct)
    {
        if (!tokens.Validate(token, id, rasterId))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var raster = await db.TerrainDerivativeRasters.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == rasterId && r.TerrainDerivativeLayerId == id, ct);
        if (raster is null || !File.Exists(raster.Path))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        return TypedResults.PhysicalFile(
            raster.Path,
            contentType: "image/tiff",
            enableRangeProcessing: true);
    }

    private static async Task<List<TerrainDerivativeRaster>> RastersOfAsync(
        SilexGisDbContext db, Guid layerId, CancellationToken ct) =>
        await db.TerrainDerivativeRasters.AsNoTracking()
            .Where(r => r.TerrainDerivativeLayerId == layerId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

    private static TerrainDerivativeLayerDto ToDto(
        TerrainDerivativeLayerView view,
        IReadOnlyList<TerrainDerivativeRaster> rasters,
        ITerrainRasterTokenService tokens) =>
        new(
            view.Id,
            view.TerrainBuildId,
            view.Derivative,
            view.Name,
            view.Settings,
            view.Status,
            view.ErrorCode,
            view.Message,
            view.Version,
            view.SizeBytes,
            view.Stale,
            view.ComputedAt,
            view.CreatedAt,
            [.. rasters.Select(raster => ToDto(view.Id, raster, tokens))]);

    private static TerrainDerivativeRasterDto ToDto(
        Guid layerId, TerrainDerivativeRaster raster, ITerrainRasterTokenService tokens)
    {
        var box = raster.Footprint.EnvelopeInternal;
        return new TerrainDerivativeRasterDto(
            raster.Id,
            box.MinX,
            box.MinY,
            box.MaxX,
            box.MaxY,
            raster.Width,
            raster.Height,
            raster.PixelSizeDegrees,
            raster.SizeBytes,
            $"/api/v1/terrain/derivatives/{layerId}/rasters/{raster.Id}/content"
                + $"?token={Uri.EscapeDataString(tokens.CreateToken(layerId, raster.Id))}");
    }
}
