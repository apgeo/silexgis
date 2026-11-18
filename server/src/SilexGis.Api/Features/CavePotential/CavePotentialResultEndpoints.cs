// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Potential;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.CavePotential;

/// <summary>One layer's part in a run.</summary>
/// <param name="Weight">The layer's share of the model, out of one.</param>
/// <param name="Features">How many recorded features the layer was made from; nought for a terrain layer.</param>
/// <param name="ProtectedFeatures">How many of those were location-protected.</param>
/// <param name="Cells">How many cells inside the area the layer had a value for.</param>
public sealed record CavePotentialResultLayerDto(
    string Layer, double Weight, int Features, int ProtectedFeatures, int Cells);

/// <summary>The computed picture of the ground one terrain layer of a run was read from.</summary>
/// <param name="Layer">The evidence layer's code.</param>
/// <param name="DerivativeLayerId">The picture read, or null for elevation itself.</param>
/// <param name="Settings">That picture's settings as stored (JSON text), or null for elevation itself.</param>
public sealed record CavePotentialTerrainInputDto(string Layer, Guid? DerivativeLayerId, string? Settings);

/// <summary>What the known entrances say about one class of one layer.</summary>
/// <param name="Class">
/// The class's position in the layer's table of the model as run, or for a layer that is classes
/// already, in that layer's own list of classes.
/// </param>
/// <param name="Cells">How many cells of the ground weighed fall in the class.</param>
/// <param name="Occurrences">How many of those hold an entrance the model learnt from.</param>
/// <param name="FrequencyRatio">The class's share of the occupied cells over its share of the ground; null for a class no cell falls in.</param>
/// <param name="PositiveWeight">W+, a natural logarithm; null for a class no cell falls in.</param>
/// <param name="NegativeWeight">W−; null for a class no cell falls in or none falls outside of.</param>
/// <param name="Contrast">W+ − W−.</param>
/// <param name="ContrastDeviation">The standard deviation of the contrast.</param>
/// <param name="StudentisedContrast">The contrast over its standard deviation.</param>
/// <param name="Corrected">Whether a half was added to each count because one of them was nought.</param>
public sealed record CavePotentialClassWeightsDto(
    int Class,
    int Cells,
    int Occurrences,
    double? FrequencyRatio,
    double? PositiveWeight,
    double? NegativeWeight,
    double? Contrast,
    double? ContrastDeviation,
    double? StudentisedContrast,
    bool Corrected);

/// <summary>One layer's table of weights, a row for every class in the layer's order.</summary>
public sealed record CavePotentialWeightTableDto(string Layer, IReadOnlyList<CavePotentialClassWeightsDto> Classes);

/// <summary>One point of a rate curve: a share of the ground, most favourable first, and the share of occupied cells on it.</summary>
public sealed record CavePotentialRatePointDto(double AreaShare, double OccurrenceShare);

/// <summary>How well the map ranks a set of occupied cells.</summary>
/// <param name="Points">From (0, 0) to (1, 1), at most a hundred and one points.</param>
/// <param name="AreaUnderCurve">A half is no better than chance.</param>
/// <param name="Cells">How many cells the map has a value for.</param>
/// <param name="Occurrences">How many occupied cells the curve was drawn against.</param>
public sealed record CavePotentialRateCurveDto(
    IReadOnlyList<CavePotentialRatePointDto> Points, double AreaUnderCurve, int Cells, int Occurrences);

/// <summary>
/// What a model that learns from known entrances learnt on this run. Counts, tables per class
/// and curves: never an entrance, a position or a cell.
/// </summary>
/// <param name="HeldOutShare">The share of occupied cells kept back to test the map.</param>
/// <param name="Seed">What the draw of the kept-back cells started from.</param>
/// <param name="TrainingEntrances">How many entrances the weights were counted from.</param>
/// <param name="HeldOutEntrances">How many entrances were kept back.</param>
/// <param name="TrainingCells">How many cells the training entrances occupy.</param>
/// <param name="HeldOutCells">How many cells the kept-back entrances occupy.</param>
/// <param name="ProtectedEntrances">How many of the entrances used were location-protected.</param>
/// <param name="Cells">How many cells of ground were weighed.</param>
/// <param name="PriorProbability">The share of that ground a training entrance occupies.</param>
/// <param name="ValueMinimum">The least value the method gave a cell, before the map was stretched onto nought to one.</param>
/// <param name="ValueMaximum">The greatest; a drawn value <c>v</c> stands for minimum + v × (maximum − minimum).</param>
/// <param name="ExpectedOccurrences">
/// For weights of evidence, the occupied cells the posterior probabilities add up to, to set
/// beside <paramref name="TrainingCells"/>; null for a frequency-ratio model.
/// </param>
/// <param name="Tables">The weight tables, in the model's order of layers.</param>
/// <param name="SuccessRate">The map against the cells it learnt from.</param>
/// <param name="PredictionRate">The map against the cells kept back.</param>
public sealed record CavePotentialTrainingDto(
    double HeldOutShare,
    int Seed,
    int TrainingEntrances,
    int HeldOutEntrances,
    int TrainingCells,
    int HeldOutCells,
    int ProtectedEntrances,
    int Cells,
    double PriorProbability,
    double ValueMinimum,
    double ValueMaximum,
    double? ExpectedOccurrences,
    IReadOnlyList<CavePotentialWeightTableDto> Tables,
    CavePotentialRateCurveDto SuccessRate,
    CavePotentialRateCurveDto PredictionRate);

/// <summary>What a finished run found.</summary>
/// <param name="AreaCells">How many cells lie inside the area.</param>
/// <param name="ScoredCells">How many of them every layer had a value for, and so were scored.</param>
/// <param name="Training">
/// What the run learnt from known entrances and how well its map finds them; null for a model
/// weighed by judgment. Shown to every reader of the result, who by the rule that admits them may
/// place every entrance it was counted from. The bounds of the classes are the model's and come
/// with its definition.
/// </param>
public sealed record CavePotentialResultSummaryDto(
    IReadOnlyList<CavePotentialResultLayerDto> Layers,
    int AreaCells,
    int ScoredCells,
    double? Minimum,
    double? Maximum,
    double? Mean,
    CavePotentialTrainingDto? Training);

/// <summary>One run of a cave-potential model, as one reader sees it.</summary>
/// <param name="ModelId">The model it was run from, or null once that model is deleted.</param>
/// <param name="ModelName">
/// The model's name, or null for a reader who may not read the model: a run does not widen the
/// audience its author chose for the recipe.
/// </param>
/// <param name="Definition">
/// The model as it was run, whatever has been done to the model since. Null for a reader who may
/// not read the model; the asker and a full administrator always have it.
/// </param>
/// <param name="Weighting">
/// The weights that definition comes to; null whenever the definition is, and for a model that
/// learnt from known entrances, whose weights are the tables in the summary.
/// </param>
/// <param name="TerrainBuildId">The elevation build the run read the ground from, or null when it read none.</param>
/// <param name="TerrainBuildActive">
/// Whether that build is still the active one, so a map made from ground that has since been
/// replaced can be told; null when the run read no terrain.
/// </param>
/// <param name="TerrainInputs">The computed pictures of the ground the run read, with their settings.</param>
/// <param name="UsesPositions">
/// Whether the model weighs any layer made from recorded positions, or learnt its weights from
/// known entrances. Such a map shows where those positions are and is listed only for readers who
/// may place every one of them.
/// </param>
/// <param name="EvidenceFeatures">
/// How many recorded features the run was computed from, the entrances a model learnt from or was
/// tested against among them.
/// </param>
/// <param name="ProtectedFeatures">How many of those were location-protected.</param>
/// <param name="Mine">Whether the reader asked for this run.</param>
/// <param name="RequestedByName">Who asked for it, as the reader may be shown that member.</param>
/// <param name="RanAsFullAdministrator">Whether the account it was computed for was a full administrator then.</param>
/// <param name="MayDelete">Whether the server would accept this reader's deleting it.</param>
/// <param name="Url">
/// Where the map is read from, for a few minutes: a cloud-optimised raster of favourability from
/// nought to one, with <see cref="CavePotentialResultEndpoints.NoDataValue"/> where there is no
/// answer. Null until the run is ready.
/// </param>
public sealed record CavePotentialResultDto(
    Guid Id,
    Guid? ModelId,
    string? ModelName,
    CavePotentialMethod Method,
    CavePotentialModelDefinition? Definition,
    CavePotentialWeightingDto? Weighting,
    Guid? TerrainBuildId,
    bool? TerrainBuildActive,
    IReadOnlyList<CavePotentialTerrainInputDto> TerrainInputs,
    Guid AreaFeatureId,
    string? AreaName,
    double CellMeters,
    CavePotentialResultStatus Status,
    string? ErrorCode,
    string? Message,
    bool UsesPositions,
    int EvidenceFeatures,
    int ProtectedFeatures,
    CavePotentialResultSummaryDto? Summary,
    double? West,
    double? South,
    double? East,
    double? North,
    int? Columns,
    int? Rows,
    long SizeBytes,
    double NoData,
    bool Mine,
    string? RequestedByName,
    bool RanAsFullAdministrator,
    bool MayDelete,
    string? Url,
    DateTimeOffset? ComputedAt,
    DateTimeOffset CreatedAt);

/// <summary>A run asked for: which model, over which area, at what cell size.</summary>
/// <param name="CellMeters">The size of a cell on the ground, in metres.</param>
public sealed record CavePotentialRunRequest(Guid ModelId, Guid AreaFeatureId, double CellMeters);

public sealed class CavePotentialRunRequestValidator : AbstractValidator<CavePotentialRunRequest>
{
    public CavePotentialRunRequestValidator()
    {
        RuleFor(x => x.ModelId).NotEmpty();
        RuleFor(x => x.AreaFeatureId).NotEmpty();
        RuleFor(x => x.CellMeters)
            .Must(m => double.IsFinite(m)
                && m >= EvidenceGridRules.MinCellMeters && m <= EvidenceGridRules.MaxCellMeters)
            .WithMessage(FormattableString.Invariant(
                $"A cell is between {EvidenceGridRules.MinCellMeters} and {EvidenceGridRules.MaxCellMeters} metres."));
    }
}

/// <summary>
/// Runs of cave-potential models: asking for one, and reading and deleting what they produced.
/// </summary>
/// <remarks>
/// <para>
/// <b>Signed-in and internal.</b> Nothing here is exported, shared by link or shown to anybody
/// without an account. The one route that answers without a session is the map's own bytes, opened
/// by a token that lasts minutes and is minted only for a reader who has just passed the reading
/// rule.
/// </para>
/// <para>
/// <b>Who may read a result</b> is decided in one place, <see cref="CavePotentialResultAccess"/>:
/// the terrain right, the area it was run over, and — for a map weighed from recorded positions —
/// every feature it was computed from, each of which the reader must be able to place exactly
/// today. A result the reader may not read is answered exactly as one that does not exist, by
/// every route here, so nothing says that a map was made over ground the reader cannot see.
/// </para>
/// <para>
/// <b>Asking for a run</b> takes the right to read the terrain, a model the caller may read, and an
/// area the caller may place. The run itself then reads as the caller, with the caller's rights as
/// they are when it starts.
/// </para>
/// </remarks>
public static class CavePotentialResultEndpoints
{
    public const string NotFoundCode = "cave_potential_result.not_found";
    public const string ForbiddenCode = "access.forbidden";
    public const string NoTerrainCode = "cave_potential_result.no_terrain";
    public const string ModelInvalidCode = "cave_potential_result.model_invalid";
    public const string RoutePrefix = "cave_potential_result";

    /// <summary>What a cell with no answer holds in the map's raster.</summary>
    public const double NoDataValue = CavePotentialRasterWriter.NoData;

    public static RouteGroupBuilder MapCavePotentialResultEndpoints(this RouteGroupBuilder api)
    {
        var results = api.MapGroup("/cave-potential/results").WithTags("CavePotential");

        results.MapGet("/", ListAsync)
            .WithSummary("The cave-potential results the caller may read, newest first, optionally "
                + "those over one area.");
        results.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One cave-potential result, with the model as it was run.");
        results.MapPost("/", RunAsync).WithValidation<CavePotentialRunRequest>()
            .WithSummary("Queues a run of a model over an area at a cell size. Needs the right to "
                + "read the terrain; the run reads as the caller.");
        results.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a result and its map (whoever asked for it, or a full administrator).");
        results.MapGet("/{id:guid}/content", ContentAsync)
            .AllowAnonymous()
            .WithSummary("The favourability map as a cloud-optimised raster. Token-validated; "
                + "supports Range.");

        return api;
    }

    private static async Task<Results<Ok<List<CavePotentialResultDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        Guid? areaFeatureId,
        SilexGisDbContext db,
        CavePotentialResultAccess resultAccess,
        ICavePotentialRasterTokenService tokens,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CavePotentialResultAccess.MayReadAny(ctx))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var rows = await db.CavePotentialResults.AsNoTracking()
            .Where(r => areaFeatureId == null || r.AreaFeatureId == areaFeatureId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(ct);
        var readable = await resultAccess.ReadableAsync(ctx, rows, ct);
        return TypedResults.Ok(await ToDtosAsync(readable, ctx, user, db, tokens, ct));
    }

    private static async Task<Results<Ok<CavePotentialResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        CavePotentialResultAccess resultAccess,
        ICavePotentialRasterTokenService tokens,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CavePotentialResultAccess.MayReadAny(ctx))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var row = await resultAccess.ReadAsync(ctx, id, ct);
        return row is null
            ? ApiProblems.NotFound(NotFoundCode)
            : TypedResults.Ok((await ToDtosAsync([row], ctx, user, db, tokens, ct))[0]);
    }

    private static async Task<Results<Accepted<CavePotentialResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> RunAsync(
        CavePotentialRunRequest request,
        SilexGisDbContext db,
        FeatureProtection protection,
        ICavePotentialRasterTokenService tokens,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CavePotentialResultAccess.MayReadAny(ctx))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var model = await db.CavePotentialModels.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.MapViews)
            .FirstOrDefaultAsync(m => m.Id == request.ModelId, ct);
        if (model is null)
        {
            return ApiProblems.NotFound(CavePotentialModelEndpoints.NotFoundCode);
        }

        var definition = CavePotentialModelDefinition.Deserialize(model.Definition);
        if (definition is null || CavePotentialModelRules.Problems(model.Method, definition).Count > 0)
        {
            return ApiProblems.BadRequest(
                ModelInvalidCode, "That model is not one that can be run as it stands; open it and save it again.");
        }

        if (definition.Layers.Any(l => EvidenceLayers.Find(l.Layer)?.Source == EvidenceSource.Terrain)
            && !await db.TerrainBuilds.AsNoTracking().AnyAsync(b => b.IsActive, ct))
        {
            // The same "no terrain here" the probes and area statistics answer, as a coded refusal
            // because this route has no body to carry a flag in: nothing is queued to fail later.
            return ApiProblems.Conflict(
                NoTerrainCode, "This installation has no elevation built, and the model weighs the ground.");
        }

        var (outline, refusal) = await StudyAreaGate.OutlineAsync(
            protection, ctx, request.AreaFeatureId, RoutePrefix, "weigh the evidence over", ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var box = outline!.EnvelopeInternal;
        if (EvidenceGridRules.Problem(box.MinX, box.MinY, box.MaxX, box.MaxY, request.CellMeters) is { } problem)
        {
            return ApiProblems.BadRequest(
                $"{RoutePrefix}.{problem}",
                problem == EvidenceGridRules.TooManyCellsCode
                    ? FormattableString.Invariant(
                        $"That area at that cell size is more than {EvidenceGridRules.MaxCells} cells; choose a larger cell.")
                    : "That area cannot be covered by a grid at that cell size.");
        }

        var row = new CavePotentialResult
        {
            ModelId = model.Id,
            ModelName = model.Name,
            Method = model.Method,
            // The server's own serialisation of the definition it has just read and checked.
            Definition = CavePotentialModelDefinition.Serialize(definition),
            AreaFeatureId = request.AreaFeatureId,
            CellMeters = request.CellMeters,
            RequestedByUserId = ctx.UserId,
            RanAsFullAdministrator = ctx.IsFullAdmin,
            UsesPositions = CavePotentialModelRules.UsesPositions(model.Method, definition),
            Status = CavePotentialResultStatus.Queued,
        };
        db.CavePotentialResults.Add(row);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.CavePotentialRun,
            Payload = JsonSerializer.Serialize(new CavePotentialRunPayload(row.Id), JsonSerializerOptions.Web),
            RequestedBy = ctx.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Accepted(
            $"/api/v1/cave-potential/results/{row.Id}", (await ToDtosAsync([row], ctx, user, db, tokens, ct))[0]);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        CavePotentialResultAccess resultAccess,
        TerrainWorkspace workspace,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.CavePotentialResults.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayDelete(ctx, row))
        {
            // Somebody who may read it is told they may not delete it; anybody else is told there
            // is nothing here, exactly as when there is not.
            return (await resultAccess.ReadableAsync(ctx, [row], ct)).Count > 0
                ? ApiProblems.Forbidden(ForbiddenCode)
                : ApiProblems.NotFound(NotFoundCode);
        }

        db.CavePotentialResults.Remove(row);
        await db.SaveChangesAsync(ct);

        // After the commit: a file removed for a row that then stayed would be a ready result
        // with no map behind it.
        workspace.RemoveCavePotentialResult(id);
        return TypedResults.NoContent();
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ContentAsync(
        Guid id,
        string? token,
        SilexGisDbContext db,
        ICavePotentialRasterTokenService tokens,
        CancellationToken ct)
    {
        // One answer for a bad token, an unknown result and one with no map: nothing here says
        // which ids exist.
        if (!tokens.Validate(token, id))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var path = await db.CavePotentialResults.AsNoTracking()
            .Where(r => r.Id == id && r.Status == CavePotentialResultStatus.Ready)
            .Select(r => r.Path)
            .FirstOrDefaultAsync(ct);
        return path is null || !File.Exists(path)
            ? ApiProblems.NotFound(NotFoundCode)
            : TypedResults.PhysicalFile(path, "image/tiff", enableRangeProcessing: true);
    }

    /// <summary>
    /// Whoever asked for a run, or a full administrator. Its author may always take their own run
    /// away, even one they can no longer read: removing a map shows nobody anything.
    /// </summary>
    private static bool MayDelete(AccessContext ctx, CavePotentialResult row) =>
        ctx.IsFullAdmin || row.RequestedByUserId == ctx.UserId;

    /// <summary>
    /// Each row as this reader sees it. Only ever given rows the reader has been found able to
    /// read, which is what makes naming the area and minting an address for the map safe here.
    /// </summary>
    private static async Task<List<CavePotentialResultDto>> ToDtosAsync(
        IReadOnlyList<CavePotentialResult> rows,
        AccessContext ctx,
        UserContext user,
        SilexGisDbContext db,
        ICavePotentialRasterTokenService tokens,
        CancellationToken ct)
    {
        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, rows.Select(r => r.RequestedByUserId), ct);
        var areaIds = rows.Select(r => r.AreaFeatureId).Distinct().ToList();
        var areaNames = await db.Features.AsNoTracking()
            .Where(f => areaIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        // A run reads as its map; the recipe it was made from is still its model's to give. Only
        // the asker, a full administrator and whoever the model is still shared with are shown it.
        var modelIds = rows.Where(r => r.ModelId is not null).Select(r => r.ModelId!.Value).Distinct().ToList();
        var readableModels = ctx.IsFullAdmin || modelIds.Count == 0
            ? modelIds
            : await db.CavePotentialModels.AsNoTracking()
                .VisibleTo(ctx, AccessDomain.MapViews)
                .Where(m => modelIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync(ct);
        var activeBuildId = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var dtos = new List<CavePotentialResultDto>(rows.Count);
        foreach (var row in rows)
        {
            var mayReadRecipe = ctx.IsFullAdmin
                || row.RequestedByUserId == ctx.UserId
                || (row.ModelId is { } modelId && readableModels.Contains(modelId));
            var definition = mayReadRecipe ? CavePotentialModelDefinition.Deserialize(row.Definition) : null;
            var valid = definition is not null
                && CavePotentialModelRules.Problems(row.Method, definition).Count == 0;
            var summary = row.Status == CavePotentialResultStatus.Ready
                ? CavePotentialRunSummary.Deserialize(row.Summary)
                : null;
            dtos.Add(new CavePotentialResultDto(
                row.Id,
                row.ModelId,
                mayReadRecipe ? row.ModelName : null,
                row.Method,
                definition,
                valid ? CavePotentialModelEndpoints.ToDto(CavePotentialModelRules.Weighting(row.Method, definition!)) : null,
                row.TerrainBuildId,
                row.TerrainBuildId is { } built ? built == activeBuildId : null,
                [.. (summary?.TerrainInputs ?? []).Select(i => new CavePotentialTerrainInputDto(
                    i.Layer, i.DerivativeLayerId, i.Settings))],
                row.AreaFeatureId,
                areaNames.GetValueOrDefault(row.AreaFeatureId),
                row.CellMeters,
                row.Status,
                row.ErrorCode,
                row.Message,
                row.UsesPositions,
                row.EvidenceFeatureIds.Length,
                row.ProtectedFeatureCount,
                summary is null
                    ? null
                    : new CavePotentialResultSummaryDto(
                        [.. summary.Layers.Select(l => new CavePotentialResultLayerDto(
                            l.Layer, l.Weight, l.Features, l.ProtectedFeatures, l.Cells))],
                        summary.AreaCells,
                        summary.ScoredCells,
                        summary.Minimum,
                        summary.Maximum,
                        summary.Mean,
                        summary.Training is { } training ? ToDto(training) : null),
                row.West,
                row.South,
                row.East,
                row.North,
                row.Columns,
                row.Rows,
                row.SizeBytes,
                NoDataValue,
                row.RequestedByUserId == ctx.UserId,
                labels.GetValueOrDefault(row.RequestedByUserId),
                row.RanAsFullAdministrator,
                MayDelete(ctx, row),
                row.Status == CavePotentialResultStatus.Ready
                    ? $"/api/v1/cave-potential/results/{row.Id}/content?token={Uri.EscapeDataString(tokens.CreateToken(row.Id))}"
                    : null,
                row.ComputedAt,
                row.CreatedAt));
        }

        return dtos;
    }

    private static CavePotentialTrainingDto ToDto(CavePotentialTrainingSummary training) => new(
        training.HeldOutShare,
        training.Seed,
        training.TrainingEntrances,
        training.HeldOutEntrances,
        training.TrainingCells,
        training.HeldOutCells,
        training.ProtectedEntrances,
        training.Cells,
        training.PriorProbability,
        training.ValueMinimum,
        training.ValueMaximum,
        training.ExpectedOccurrences,
        [
            .. training.Layers.Select(layer => new CavePotentialWeightTableDto(
                layer.Layer,
                [
                    .. layer.Classes.Select(row => new CavePotentialClassWeightsDto(
                        row.Class,
                        row.Cells,
                        row.Occurrences,
                        row.FrequencyRatio,
                        row.PositiveWeight,
                        row.NegativeWeight,
                        row.Contrast,
                        row.ContrastDeviation,
                        row.StudentisedContrast,
                        row.Corrected)),
                ])),
        ],
        ToDto(training.SuccessRate),
        ToDto(training.PredictionRate));

    private static CavePotentialRateCurveDto ToDto(RateCurve curve) => new(
        [.. curve.Points.Select(p => new CavePotentialRatePointDto(p.AreaShare, p.OccurrenceShare))],
        curve.AreaUnderCurve,
        curve.Cells,
        curve.Occurrences);
}
