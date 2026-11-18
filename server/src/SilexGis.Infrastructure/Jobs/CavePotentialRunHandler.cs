// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Potential;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.CavePotentialRun"/> jobs.</summary>
/// <remarks>
/// The key of the result row and nothing else. What to run, where and for whom is on the row, and
/// what the account may read is looked up when the run starts rather than carried in the queue.
/// </remarks>
public sealed record CavePotentialRunPayload(Guid CavePotentialResultId);

/// <summary>
/// Runs one cave-potential model over one area: makes each evidence layer on the run's grid,
/// weighs them, and writes the favourability map beside the row that asked for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whose reading it is.</b> The run reads as the account that asked for it, with that account's
/// rights resolved from its rows when the run starts. Rights are not copied into the queue: between
/// the asking and the running a grant may have been withdrawn, and a run that honoured the copy
/// would write a map from positions the account may no longer place. An account that has meanwhile
/// lost the terrain or the area gets a failed run and no map.
/// </para>
/// <para>
/// <b>What it records.</b> Every recorded feature that went into the evidence is written on the
/// row in the same save that marks it ready, so there is no moment at which a finished map exists
/// without the list that decides who may read it. For a model that learns from known entrances
/// that list also holds every entrance it learnt from or was tested against; the row's summary
/// keeps only how many there were.
/// </para>
/// <para>
/// Not retried. A run is asked for by somebody watching it and is cheap to ask for again, and its
/// failures are nearly always about what it was given — a surface nobody computed, an area nothing
/// can be scored in — which a second attempt would only repeat.
/// </para>
/// </remarks>
public sealed class CavePotentialRunHandler(
    SilexGisDbContext db,
    TerrainWorkspace workspace,
    CavePotentialEvidenceBuilder evidence,
    CavePotentialRasterWriter writer,
    FeatureProtection protection) : IProcessingJobHandler
{
    /// <summary>The file a run's map is written as, inside the run's own directory.</summary>
    public const string RasterFileName = "favourability.tif";

    public string Kind => ProcessingJobKinds.CavePotentialRun;

    /// <summary>A run given up by the queue stops reading "computing".</summary>
    public async Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
    {
        if (AbandonedJob.PayloadOf<CavePotentialRunPayload>(job) is not { } payload)
        {
            return;
        }

        await db.CavePotentialResults
            .Where(r => r.Id == payload.CavePotentialResultId
                && r.Status == CavePotentialResultStatus.Computing
                && r.ProcessingJobId == job.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CavePotentialResultStatus.Failed)
                .SetProperty(r => r.ErrorCode, CavePotentialRunFailures.Failed)
                .SetProperty(r => r.Message, AbandonedJob.Reason), ct);
    }

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<CavePotentialRunPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty cave-potential payload.");

        var row = await db.CavePotentialResults.FirstOrDefaultAsync(r => r.Id == payload.CavePotentialResultId, ct);
        if (row is null || row.Status == CavePotentialResultStatus.Ready)
        {
            // Asked for and then deleted, or already computed by an earlier delivery of this job.
            return;
        }

        row.Status = CavePotentialResultStatus.Computing;
        row.ProcessingJobId = job.Id;
        row.ErrorCode = null;
        row.Message = null;
        await db.SaveChangesAsync(ct);

        var directory = workspace.CavePotentialResultFor(row.Id);
        try
        {
            var ctx = await AccessContextResolver.ResolveAsync(db, row.RequestedByUserId, ct);
            if (!CavePotentialResultAccess.MayReadAny(ctx))
            {
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.AccessChanged,
                    "The account this was asked by may no longer read the terrain.");
            }

            var outline = await protection.ScopeGeometryAsync(ctx, row.AreaFeatureId, ct);
            if (outline is null)
            {
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.AccessChanged,
                    "The account this was asked by may no longer use that area.");
            }

            var definition = CavePotentialModelDefinition.Deserialize(row.Definition);
            if (definition is null || CavePotentialModelRules.Problems(row.Method, definition).Count > 0)
            {
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.ModelInvalid, "The model as it was run is no longer one that can be run.");
            }

            var box = outline.EnvelopeInternal;
            var grid = StudyArea.IsOutline(outline)
                ? EvidenceGrid.Over(box.MinX, box.MinY, box.MaxX, box.MaxY, row.CellMeters)
                : null;
            if (grid is null)
            {
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.AreaUnusable,
                    "The area is no longer an outline that can be covered at that cell size.");
            }

            var built = await evidence.BuildAsync(ctx, grid, [.. definition.Layers.Select(l => l.Layer)], ct);
            var missing = built.Where(b => !b.Available).ToList();
            if (missing.Count > 0)
            {
                // Layer codes and reason codes only: both are closed lists and say nothing of a place.
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.EvidenceUnavailable,
                    string.Join(", ", missing.Select(b => $"{b.Layer.Code}: {b.UnavailableReason}")));
            }

            var inside = EvidenceSurfaces.Inside(grid, outline);
            float[] values;
            CavePotentialRunSummary summary;
            IEnumerable<Guid> featureIds = built.SelectMany(b => b.Provenance.FeatureIds);
            var protectedFeatures = built.Sum(b => b.Provenance.ProtectedFeatures);
            if (CavePotentialMethods.IsDataDriven(row.Method))
            {
                // Read as the account that asked, like the evidence: only entrances it may see and
                // place exactly, so nothing it could not have drawn itself reaches the weights.
                var entrances = await evidence.TrainingEntrancesAsync(ctx, grid, ct)
                    ?? throw new CavePotentialRunException(
                        CavePotentialRunFailures.TooManyEntrances,
                        "The area holds more known entrances than a run can learn from.");
                var trained = TrainedFavourability.Compute(
                    row.Method, definition, built, inside, [.. entrances.Select(e => e.Entrance)]);

                // Every entrance that taught the model or tested it joins the features the map was
                // made from. That list is what decides who may read the result, so a reader who
                // may not place one of them is not shown the map, its weight tables or its curves.
                featureIds = featureIds.Concat(trained.UsedEntrances.Select(i => entrances[i].Id));
                protectedFeatures += trained.Summary.Training!.ProtectedEntrances;
                values = trained.Values;
                summary = trained.Summary;
            }
            else
            {
                var weighting = CavePotentialModelRules.Weighting(row.Method, definition)!;
                var map = FavourabilityOverlay.Compute(
                    definition.Layers, [.. weighting.Weights.Select(w => w.Weight)], built, inside);
                values = map.Values;
                summary = map.Summary;
            }

            if (summary.ScoredCells == 0)
            {
                throw new CavePotentialRunException(
                    CavePotentialRunFailures.NothingScored,
                    "No part of the area had a value from every layer of the model.");
            }

            ct.ThrowIfCancellationRequested();
            var written = writer.Write(Path.Combine(directory, RasterFileName), grid, values, classes: false);

            row.RanAsFullAdministrator = ctx.IsFullAdmin;
            row.UsesPositions = CavePotentialModelRules.UsesPositions(row.Method, definition);
            row.EvidenceFeatureIds = [.. featureIds.Distinct().Order()];
            row.ProtectedFeatureCount = protectedFeatures;
            var terrainInputs = built.Where(b => b.Terrain is not null).Select(b => b.Terrain!).ToList();
            row.TerrainBuildId = terrainInputs.Count > 0 ? terrainInputs[0].BuildId : null;
            row.Summary = CavePotentialRunSummary.Serialize(
                terrainInputs.Count > 0 ? summary with { TerrainInputs = terrainInputs } : summary);
            row.Path = written.Path;
            row.West = grid.West;
            row.South = grid.South;
            row.East = grid.East;
            row.North = grid.North;
            row.Columns = grid.Columns;
            row.Rows = grid.Rows;
            row.SizeBytes = written.SizeBytes;
            row.ComputedAt = DateTimeOffset.UtcNow;
            row.Status = CavePotentialResultStatus.Ready;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList())
            {
                if (entry.State is EntityState.Modified)
                {
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                }
            }

            row.Status = CavePotentialResultStatus.Failed;
            (row.ErrorCode, row.Message) = e is CavePotentialRunException named
                ? (named.Code, named.Message)
                : (CavePotentialRunFailures.Failed, "The map could not be computed.");

            try
            {
                // Uncancelled on purpose: a run stopped part way must still leave a row that says so.
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The row was deleted while the run computed: there is nothing left to mark.
            }
            finally
            {
                // Always, so a map written after its row was deleted does not stay on the disk.
                workspace.RemoveCavePotentialResult(row.Id);
            }

            throw;
        }
    }
}
