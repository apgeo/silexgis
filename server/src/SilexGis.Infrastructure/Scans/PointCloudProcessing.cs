// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Storage;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Storage;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// The worker steps of a point cloud's run, after the header has been read: the thinning
/// estimated, the cloud thinned when the recipe says so, the result tiled, and each product
/// stored as an artefact of the upload. Runs one step at a time through the hand-off directory,
/// reusing an earlier run's artefact wherever the step, its inputs and its parameters are the
/// same as before.
/// </summary>
/// <remarks>
/// <para>
/// An artefact is the unit of reuse. A step's product is matched by the step id, the hash of the
/// parameters it ran with, and the artefacts it read, in that order; a match whose stored file
/// still exists is kept and the worker is not asked. A rerun that chooses another voxel size
/// therefore repeats the thinning and the tiling and nothing else, and a rerun with nothing
/// changed repeats nothing — unless it asks for a step to be done again, in which case that step
/// and every one after it run afresh.
/// </para>
/// <para>
/// A product is stored as a file of the original's document, and the chain from tiles back to
/// the uploaded file is on the artefact rows — each names the step that made it and the
/// artefacts it read. It is deliberately not on the stored files' "converted from" link: that
/// link means "the one rendition of this upload" (a version holds at most one, the file route
/// shows it as the upload's rendition, and deleting the upload cascades to it), none of which is
/// true of an estimate report and a tileset that both derive from the same original. The model's
/// converted file is the tileset archive. When a step's product replaces an earlier one, the new
/// product is stored first and the earlier artefact and its file removed after: a scan keeps one
/// product per step, not a history of every voxel size somebody tried, and a worker that names a
/// product it did not write costs the earlier one nothing.
/// </para>
/// <para>
/// Over the installation's thresholds the run stops after the estimate unless somebody has
/// chosen how much to show, and ends there, succeeded, asking for the choice. The estimate is an
/// artefact like any other, so the rerun that brings the choice reuses it.
/// </para>
/// <para>
/// What the worker reports beyond its files — the estimate's rows, the thinned copy's count, the
/// tiling's size and shift — is kept on the model's facts. A reused artefact keeps the facts the
/// run that made it wrote, which is why they are carried over before anything runs.
/// </para>
/// </remarks>
public sealed class PointCloudProcessing(
    SilexGisDbContext db,
    IFileStore fileStore,
    DocumentWriteService documents,
    ScanWorkerHandoff handoff,
    ICoordinateProjector projector,
    IOptions<ScanOptions> options,
    StorageFiguresService storage,
    ILogger<PointCloudProcessing> logger)
{
    private const string LazMediaType = "application/vnd.laszip";
    private const string LasMediaType = "application/vnd.las";

    // The name of the manifest a raw recording's job reads its files from.
    private const string RecordingManifest = "recording.json";

    /// <summary>Whether this installation says it has a worker; off, the run ends after the header with a note.</summary>
    public bool Enabled => handoff.Enabled;

    /// <summary>What the run came to, for the note the upload ends with.</summary>
    /// <param name="Tiled">Whether the cloud is tiled at the end — by this run or an earlier one.</param>
    /// <param name="TilesReused">Whether the tiles are an earlier run's.</param>
    /// <param name="ChoiceNeeded">Whether the run stopped after the estimate, waiting for a choice of how much to show.</param>
    public sealed record Outcome(PointCloudFacts Facts, bool Tiled, bool TilesReused, bool ChoiceNeeded = false);

    /// <summary>
    /// Runs the recipe's worker steps for a cloud whose header has been read and recorded. Reports
    /// each stage it enters through <paramref name="entering"/>. Answers the facts as they stand
    /// at the end, written on the model.
    /// </summary>
    public async Task<Outcome> RunAsync(
        SurveyUpload upload,
        SurveyModel model,
        PointCloudFacts headerFacts,
        PointCloudFacts? earlierFacts,
        ScanRecipe recipe,
        Action<SurveyUploadStage> entering,
        CancellationToken ct)
    {
        var origin = upload.Source;
        var facts = headerFacts.WithProcessingOf(earlierFacts);

        // What the upload itself made: a point cloud's one file, or every file of a raw recording.
        // The recording's files are put in one fixed order, so the odometry's inputs compare
        // equal from one run to the next while the recording is the same files.
        var uploaded = await db.ScanArtifacts
            .Where(a => a.SurveyUploadId == upload.Id && a.StepId == ScanSteps.UploadStepId)
            .ToListAsync(ct);
        var recording = uploaded.Where(a => a.Kind == ScanArtifactKind.RawRecording).OrderBy(a => a.Id).ToArray();

        // The cloud of record: the uploaded original, or — for a raw recording — what the export
        // made of it, once the export step has run or been reused.
        var cloud = uploaded.FirstOrDefault(a => a.Kind == ScanArtifactKind.PointCloudLaz);
        if (cloud is null && recording.Length == 0)
        {
            throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, "The upload's original is not among its recorded files.");
        }

        // The products are stored as files of the document the model is shown from when the run
        // starts: the original cloud's, or the recording's first lidar chunk's.
        var versionId = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == model.FileId).Select(f => f.DocumentVersionId).FirstAsync(ct);

        // A worker announces itself when it starts; after a restart of the whole stack this run
        // may be taken up before it has, so the announcement is waited for as a job would be.
        var capabilities = await handoff.AwaitCapabilitiesAsync(ct)
            ?? throw new SurveyPipelineException(
                SurveyPipelineFailures.WorkerUnavailable,
                $"Point-cloud processing is switched on, but no worker announced itself in {(int)options.Value.PickupWait.TotalSeconds} seconds. Start the scans worker, then run the upload again.");

        recipe = await ResolveOdometryAsync(upload, recipe, ct);

        // The products of the steps as they stand, by kind, for the steps that read them.
        ScanArtifact? session = null;
        ScanArtifact? trajectory = null;
        ScanArtifact? display = null;
        ScanArtifact? tiles = null;
        var tilesReused = false;
        if (!recipe.Steps.Any(s => s.StepId == PointCloudDownsampleStep.StepId))
        {
            // The whole cloud is tiled: whatever an earlier run thinned is not what is shown.
            facts = facts with { DisplayVoxelM = null, DisplayPointCount = null, DisplayEveryNth = null };
        }

        // The whole run's need is checked once, before the first step that writes more than a
        // report: by then the estimate is known, and the run is refused there rather than after a
        // thinned copy has been stored and the tiling then found not to fit. Each step is checked
        // again before it starts, against the room as it stands then.
        var runChecked = false;
        for (var index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            if (step.StepId == PointCloudInspectStep.StepId)
            {
                continue;
            }

            var definition = ScanSteps.Find(step.StepId)
                ?? throw new SurveyPipelineException(SurveyPipelineFailures.WorkerUnavailable, $"The recipe names a step this server does not know: {step.StepId}.");
            var stage = StageOf(step.StepId);
            entering(stage);

            var inputs = step.StepId switch
            {
                HdmappingOdometryStep.StepId => recording.Length > 0
                    ? recording
                    : throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, "The recipe runs odometry, and the upload holds no recording."),
                HdmappingExportStep.StepId => [Needed(session, "the odometry's session")],
                PointCloudCheckStep.StepId => trajectory is null ? [Needed(cloud, "the exported cloud")] : [Needed(cloud, "the exported cloud"), trajectory],
                Py3dtilesTileStep.StepId => [display ?? Needed(cloud, "the point cloud")],
                _ => new[] { Needed(cloud, "the point cloud") },
            };
            var inputIds = inputs.Select(a => a.Id).ToArray();

            var reused = recipe.MayReuse(step.StepId) ? await ReusableAsync(upload.Id, step, definition, inputIds, ct) : null;
            if (reused is not null)
            {
                logger.LogInformation("Survey upload {UploadId}: {Step} reused from artefact {ArtifactId}", upload.Id, step.StepId, reused[0].Id);
                await SurveyUploadWrites.ProgressAsync(
                    db, upload.Id, stage, SurveyPipelineProgress.Band(stage, origin).End, $"{definition.DisplayName}: kept from an earlier run.", ct);
                Take(reused, kept: true);
                if (step.StepId == HdmappingExportStep.StepId)
                {
                    facts = await ExportedFactsAsync(model, Needed(cloud, "the exported cloud"), facts, earlierFacts, ct);
                }
                else if (step.StepId == PointCloudEstimateStep.StepId && await StopsForChoiceAsync(model, facts, recipe, ct))
                {
                    return new Outcome(facts, Tiled: false, TilesReused: false, ChoiceNeeded: true);
                }

                continue;
            }

            if (capabilities.Find(step.StepId) is null)
            {
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.WorkerUnavailable,
                    $"The point-cloud worker running on this server cannot {definition.DisplayName.ToLowerInvariant()} ({step.StepId}).");
            }

            if (!runChecked && step.StepId != PointCloudEstimateStep.StepId)
            {
                var remaining = recipe.Steps.Skip(index)
                    .Select(s => s.StepId)
                    .Where(id => id != PointCloudInspectStep.StepId && id != PointCloudEstimateStep.StepId)
                    .ToList();
                var runNeed = ScanStorageNeeds.ForRun(
                    remaining, cloud?.SizeBytes ?? recording.Sum(a => a.SizeBytes), display?.SizeBytes, facts.Estimate, ChosenVoxelCm(recipe));
                await RefuseUnlessItFitsAsync(upload, runNeed, "this run", ct);
                runChecked = true;
            }

            var stepNeed = ScanStorageNeeds.For(step.StepId, inputs.Sum(a => a.SizeBytes), facts.Estimate, ChosenVoxelCm(recipe));
            await RefuseUnlessItFitsAsync(upload, stepNeed, definition.DisplayName.ToLowerInvariant(), ct);

            await SurveyUploadWrites.ProgressAsync(
                db, upload.Id, stage, SurveyPipelineProgress.Band(stage, origin).Start, definition.DisplayName + ".", ct);

            var (result, directory) = await RunStepAsync(upload, step, definition, inputs, stage, ct);
            try
            {
                await SurveyUploadWrites.RecordCompileAsync(
                    db, upload.Id, string.IsNullOrEmpty(result.Tool) ? null : $"{result.Tool} {result.ToolVersion}".Trim(),
                    result.LogTail.Count == 0 ? null : string.Join('\n', result.LogTail), ct);
                if (result.Status != ScanJobStatus.Ok)
                {
                    throw new SurveyPipelineException(
                        FailureCode(step.StepId, result.Status),
                        $"The point-cloud worker could not {definition.DisplayName.ToLowerInvariant()}: {Words(result)}");
                }

                var made = await StoreOutputsAsync(upload, model, versionId, step, definition, result, inputs, directory, ct);
                Take(made, kept: false);
                facts = step.StepId switch
                {
                    PointCloudEstimateStep.StepId => WithEstimate(facts, result.Data),
                    PointCloudDownsampleStep.StepId => WithDisplay(facts, result.Data),
                    Py3dtilesTileStep.StepId => WithTiles(facts, result.Data, made.FirstOrDefault(a => a.Kind == ScanArtifactKind.PointCloudTiles), result.ToolVersion),
                    _ => facts,
                };
                if (step.StepId == HdmappingExportStep.StepId)
                {
                    // A new export is a new cloud: nothing an earlier run worked out from the old
                    // one is carried over, and every step after this one reads the new one.
                    facts = await ExportedFactsAsync(model, Needed(cloud, "the exported cloud"), facts, earlier: null, ct);
                }

                // Written after every step, so what a step found stands even when a later one fails.
                model.PointCloud = facts.ToJson();
                model.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                handoff.Forget(directory);
            }

            if (step.StepId == PointCloudEstimateStep.StepId && await StopsForChoiceAsync(model, facts, recipe, ct))
            {
                return new Outcome(facts, Tiled: false, TilesReused: false, ChoiceNeeded: true);
            }
        }

        model.PointCloud = facts.ToJson();
        model.ConvertedFileId = tiles?.FileId;
        if (tiles is not null)
        {
            // The tiles have a frame now, so a file that names its coordinate system can be set
            // down on the earth through it. A placement somebody made by hand is kept — and the
            // hand may have moved while the worker ran: this instance was loaded before the first
            // step, minutes or hours ago, and placing a scan needs no tiles, so the row's
            // placement is read again here rather than trusted from then. Without that, a
            // correction made during a long tiling would be written over by the file's placement
            // and its source flipped back, silently.
            await RefreshPlacementAsync(model, ct);
            ScanAutoPlacement.AfterTiling(projector, model, facts);
        }

        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new Outcome(facts, tiles is not null, tilesReused);

        // Each product, made or kept, becomes what the steps after it read.
        void Take(IEnumerable<ScanArtifact> products, bool kept)
        {
            foreach (var product in products)
            {
                switch (product.Kind)
                {
                    case ScanArtifactKind.HdmSession:
                        session = product;
                        break;
                    case ScanArtifactKind.PointCloudLaz:
                        cloud = product;
                        break;
                    case ScanArtifactKind.Trajectory:
                        trajectory = product;
                        break;
                    case ScanArtifactKind.PointCloudLazDisplay:
                        display = product;
                        break;
                    case ScanArtifactKind.PointCloudTiles:
                        tiles = product;
                        tilesReused = kept;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Brings the tracked model's placement columns up to what the row holds now, so that a
    /// placement written by somebody else while this run's worker steps were under way is what
    /// the decision to place from the file is taken against, and what is kept when it is manual.
    /// Only the placement is refreshed: the facts and the converted file are this run's to write.
    /// </summary>
    private async Task RefreshPlacementAsync(SurveyModel model, CancellationToken ct)
    {
        var row = await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == model.Id)
            .Select(m => new { m.Anchor, m.AnchorHeightM, m.PlacementHeadingDeg, m.PlacementSource, m.SourceEpsg })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return;
        }

        var entry = db.Entry(model);
        Refresh(entry.Property(m => m.Anchor), row.Anchor);
        Refresh(entry.Property(m => m.AnchorHeightM), row.AnchorHeightM);
        Refresh(entry.Property(m => m.PlacementHeadingDeg), row.PlacementHeadingDeg);
        Refresh(entry.Property(m => m.PlacementSource), row.PlacementSource);
        Refresh(entry.Property(m => m.SourceEpsg), row.SourceEpsg);

        // Set as the original value too, so that a column this run then leaves alone is not
        // written back as a change, and a later tiling that places the scan again compares
        // against what the row really held.
        static void Refresh<T>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<SurveyModel, T> property, T value)
        {
            property.CurrentValue = value;
            property.OriginalValue = value;
            property.IsModified = false;
        }
    }

    /// <summary>
    /// Whether the run stops here, after the estimate: the cloud is over the installation's
    /// thresholds and nobody has chosen how much of it to show. The facts are written as they
    /// stand, so the estimate and the advice are on the scan while it waits. A cloud under the
    /// thresholds, or a recipe somebody chose for (the whole cloud included), goes on to tile.
    /// </summary>
    private async Task<bool> StopsForChoiceAsync(SurveyModel model, PointCloudFacts facts, ScanRecipe recipe, CancellationToken ct)
    {
        if (recipe.DisplayChosen || facts.Advice is not { Level: not PointCloudDisplayAdviceLevel.None })
        {
            return false;
        }

        model.PointCloud = facts.ToJson();
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Refuses the run, or one step of it, before anything of it is written, when what it is
    /// estimated to write does not fit: under the uploader's quota, the installation's cap or the
    /// scans' cap, on the file store's disk, or — for its inputs' copies and its outputs together —
    /// on the worker's disk. A run is stopped here rather than left to fail half-way through a
    /// tiling that fills the disk.
    /// </summary>
    /// <param name="what">What needs the room, as the message names it: "this run", or the step's name.</param>
    private async Task RefuseUnlessItFitsAsync(SurveyUpload upload, ScanStepNeed need, string what, CancellationToken ct)
    {
        var room = await storage.ForAsync(upload.RequestedBy, ct: ct);
        var shortfall = StorageRules.Shortfall(room.Limits(scan: true), need.OutputBytes);
        if (room.WorkerFreeBytes is { } workerFree
            && StorageRules.Shortfall([StorageLimit.Disk(StorageLimitKind.WorkerDisk, workerFree)], need.WorkerBytes) is { } onWorker
            && (shortfall is null || onWorker.MissingBytes > shortfall.MissingBytes))
        {
            shortfall = onWorker;
        }

        if (shortfall is not null)
        {
            throw new SurveyPipelineException(
                SurveyPipelineFailures.StorageInsufficient,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Not started: {what} needs about {shortfall.NeededBytes:N0} bytes {Where(shortfall.Kind)}, where {shortfall.RoomBytes:N0} are left — {shortfall.MissingBytes:N0} bytes are missing. Free that much, or raise the cap, then run the upload again."));
        }

        static string Where(StorageLimitKind kind) => kind switch
        {
            StorageLimitKind.Account => "under the uploader's storage quota",
            StorageLimitKind.Installation => "under this installation's storage cap",
            StorageLimitKind.Scans => "under this installation's cap for point clouds",
            StorageLimitKind.StoreDisk => "on the disk that holds the file store",
            _ => "on the disk the point-cloud worker uses",
        };
    }

    /// <summary>The voxel size the recipe thins at before tiling, in centimetres, or null when it does not thin by voxel.</summary>
    private static double? ChosenVoxelCm(ScanRecipe recipe)
    {
        var downsample = recipe.Steps.FirstOrDefault(s => s.StepId == PointCloudDownsampleStep.StepId);
        if (downsample is null || downsample.Parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var method = downsample.Parameters.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : "voxel";
        return method == "voxel" && downsample.Parameters.TryGetProperty("voxelCm", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
    }

    /// <summary>
    /// An earlier run's products of this step with these inputs and parameters — one of every
    /// kind the step makes, each with its stored file still there — or null when any is missing,
    /// in which case the step runs again.
    /// </summary>
    private async Task<List<ScanArtifact>?> ReusableAsync(
        Guid uploadId, ScanRecipeStep step, IScanStep definition, Guid[] inputIds, CancellationToken ct)
    {
        var candidates = await db.ScanArtifacts
            .Where(a => a.SurveyUploadId == uploadId && a.StepId == step.StepId && a.ParamsHash == step.ParamsHash)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        var kept = new List<ScanArtifact>();
        foreach (var kind in definition.Outputs.Distinct())
        {
            ScanArtifact? found = null;
            foreach (var candidate in candidates.Where(c => c.Kind == kind && c.InputArtifactIds.SequenceEqual(inputIds)))
            {
                var path = await db.StoredFiles.AsNoTracking()
                    .Where(f => f.Id == candidate.FileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
                if (path is not null && File.Exists(fileStore.GetAbsolutePath(path)))
                {
                    found = candidate;
                    break;
                }
            }

            if (found is null)
            {
                return null;
            }

            kept.Add(found);
        }

        return kept.Count > 0 ? kept : null;
    }

    /// <summary>
    /// Fills a recording's odometry parameters in, when the recipe still carries an empty set, with
    /// the defaults the HDMapping worker dumped from the program it runs, and writes the recipe
    /// back so the upload records the whole set the step ran with. Unchanged when there is no
    /// odometry step, the set is already resolved, or no worker has dumped its defaults — a worker
    /// that has not cannot run the step either, and the run says so where the step is reached.
    /// </summary>
    private async Task<ScanRecipe> ResolveOdometryAsync(SurveyUpload upload, ScanRecipe recipe, CancellationToken ct)
    {
        if (!PointCloudRecipes.OdometryUnresolved(recipe) || handoff.ReadOdometryDefaults() is not { } defaults)
        {
            return recipe;
        }

        var resolved = PointCloudRecipes.WithOdometryValues(recipe, defaults);
        var stored = ScanRecipes.ToOptionsJson(resolved with { RerunFrom = null });
        await db.SurveyUploads.Where(u => u.Id == upload.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Options, stored), ct);
        return resolved;
    }

    /// <summary>
    /// The facts of an exported cloud: its own header read, the recording's facts kept, and — when
    /// the export is an earlier run's, reused — what that run worked out from it carried over. The
    /// model is shown from the export from here on.
    /// </summary>
    private async Task<PointCloudFacts> ExportedFactsAsync(
        SurveyModel model, ScanArtifact exported, PointCloudFacts facts, PointCloudFacts? earlier, CancellationToken ct)
    {
        var path = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == exported.FileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct)
            ?? throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, "The exported point cloud can no longer be found in the file store.");
        PointCloudFacts header;
        try
        {
            await using var stream = File.OpenRead(fileStore.GetAbsolutePath(path));
            header = PointCloudUploadRun.Inspect(stream);
        }
        catch (SurveyPipelineException e) when (e.Code == SurveyPipelineFailures.PointCloudInvalid)
        {
            throw new SurveyPipelineException(SurveyPipelineFailures.ExportInvalid, "The exported point cloud does not read: " + e.Message);
        }

        var exportedFacts = (header with { Recording = facts.Recording }).WithProcessingOf(earlier);
        model.FileId = exported.FileId;
        model.Format = SurveyModelFormat.Laz;
        return exportedFacts;
    }

    /// <summary>The stage a step's work is reported under.</summary>
    private static SurveyUploadStage StageOf(string stepId) => stepId switch
    {
        HdmappingOdometryStep.StepId => SurveyUploadStage.Odometry,
        HdmappingExportStep.StepId => SurveyUploadStage.Export,
        PointCloudCheckStep.StepId => SurveyUploadStage.Check,
        Py3dtilesTileStep.StepId => SurveyUploadStage.Tile,
        _ => SurveyUploadStage.Downsample,
    };

    /// <summary>The code a worker's answer other than "ok" fails the run with.</summary>
    private static string FailureCode(string stepId, ScanJobStatus status) => status switch
    {
        ScanJobStatus.Interrupted => SurveyPipelineFailures.WorkerInterrupted,
        ScanJobStatus.Failed when stepId == PointCloudCheckStep.StepId => SurveyPipelineFailures.ExportInvalid,
        _ => SurveyPipelineFailures.WorkerFailed,
    };

    private static ScanArtifact Needed(ScanArtifact? artefact, string what) =>
        artefact ?? throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, $"This step needs {what}, and the run has none.");

    private async Task<(ScanJobResult Result, string Directory)> RunStepAsync(
        SurveyUpload upload, ScanRecipeStep step, IScanStep definition, ScanArtifact[] inputs, SurveyUploadStage stage, CancellationToken ct)
    {
        var jobName = $"{upload.Id:N}-{step.StepId.Replace('.', '-')}";
        var directory = await handoff.BeginAsync(jobName, ct);
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        if (step.StepId == HdmappingOdometryStep.StepId)
        {
            await CopyRecordingInAsync(directory, inputs, named, ct);
            inputs = [];
        }

        foreach (var input in inputs)
        {
            var path = await db.StoredFiles.AsNoTracking()
                .Where(f => f.Id == input.FileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct)
                ?? throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, "A file this step needs can no longer be found in the file store.");
            var fileName = InputName(input.Kind) + "." + input.Format;
            await ScanWorkerHandoff.CopyInAsync(directory, fileStore.GetAbsolutePath(path), fileName, ct);
            named[InputName(input.Kind)] = fileName;
        }

        await ScanWorkerHandoff.IssueAsync(directory, step.StepId, step.Parameters, named, options.Value.JobTimeout, ct);

        try
        {
            var result = await handoff.WaitAsync(
                directory,
                (fraction, message, token) => SurveyUploadWrites.ProgressAsync(
                    db, upload.Id, stage, SurveyPipelineProgress.Overall(stage, (int)(fraction * 100), upload.Source),
                    string.IsNullOrWhiteSpace(message) ? definition.DisplayName + "." : message, token),
                ct);
            return (result, directory);
        }
        catch (ScanWorkerException e)
        {
            await SurveyUploadWrites.RecordCompileAsync(db, upload.Id, null, e.LogTail.Count == 0 ? null : string.Join('\n', e.LogTail), CancellationToken.None);
            handoff.Forget(directory);
            throw new SurveyPipelineException(e.Code, e.Message);
        }
        catch (OperationCanceledException)
        {
            // The host is stopping; the job stays where it is for the resumed run to take up.
            throw;
        }
    }

    /// <summary>
    /// Copies a raw recording into the job directory the way the HDMapping worker reads one: every
    /// file under its own name within the recording, and a manifest naming them all, which is the
    /// step's one input. A name the recording rules do not know, or one that comes twice, is not
    /// sent: the worker would refuse it, and the folder it builds for the odometry must be exactly
    /// the recording.
    /// </summary>
    private async Task CopyRecordingInAsync(
        string directory, ScanArtifact[] files, Dictionary<string, string> named, CancellationToken ct)
    {
        var ids = files.Select(a => a.FileId).ToList();
        var stored = await db.StoredFiles.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.StoragePath, f.OriginalName })
            .ToDictionaryAsync(f => f.Id, ct);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!stored.TryGetValue(file.FileId, out var found))
            {
                throw new SurveyPipelineException(SurveyPipelineFailures.FilesMissing, "A file of the recording can no longer be found in the file store.");
            }

            if (MandeyeRecording.Classify(found.OriginalName) is null || !IsPlainName(found.OriginalName) || !seen.Add(found.OriginalName))
            {
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.RecordingInvalid, "The recording cannot be processed: one of its files is no longer named as a file of a MANDEYE recording.");
            }

            await ScanWorkerHandoff.CopyInAsync(directory, fileStore.GetAbsolutePath(found.StoragePath), found.OriginalName, ct);
            names.Add(found.OriginalName);
        }

        await File.WriteAllBytesAsync(
            Path.Combine(directory, RecordingManifest),
            JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["files"] = names }),
            ct);
        named[InputName(ScanArtifactKind.RawRecording)] = RecordingManifest;
    }

    /// <summary>
    /// Puts each product the worker named into the file store, as a file of the original's
    /// document, and records it as an artefact of the step. An earlier artefact of the same step
    /// is removed after the new one is in — the worker's answer is checked (a product it did not
    /// write, a digest that does not match) before anything of the earlier run is touched.
    /// </summary>
    private async Task<List<ScanArtifact>> StoreOutputsAsync(
        SurveyUpload upload,
        SurveyModel model,
        Guid versionId,
        ScanRecipeStep step,
        IScanStep definition,
        ScanJobResult result,
        ScanArtifact[] inputs,
        string directory,
        CancellationToken ct)
    {
        var made = new List<ScanArtifact>();
        var now = DateTimeOffset.UtcNow;

        var earlier = await db.ScanArtifacts.Where(a => a.SurveyUploadId == upload.Id && a.StepId == step.StepId).ToListAsync(ct);

        foreach (var output in result.Outputs)
        {
            var kind = KindOf(output.Kind);
            if (kind is null || !definition.Outputs.Contains(kind.Value) || !IsPlainName(output.File))
            {
                // The worker named a product this step does not make, or a file outside the job: not stored.
                logger.LogWarning("Survey upload {UploadId}: {Step} named an output not taken: {Kind} {File}", upload.Id, step.StepId, output.Kind, output.File);
                continue;
            }

            var path = Path.Combine(directory, output.File);
            if (!File.Exists(path))
            {
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.StoreFailed, $"The point-cloud worker named a product it did not write: {output.File}.");
            }

            string sha256;
            long size;
            await using (var read = File.OpenRead(path))
            {
                size = read.Length;
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
            }

            if (!string.IsNullOrEmpty(output.Sha256) && !string.Equals(output.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.StoreFailed, $"The point-cloud worker's product {output.File} does not match the digest it reported.");
            }

            var extension = Path.GetExtension(output.File);
            string storagePath;
            await using (var read = File.OpenRead(path))
            {
                storagePath = await fileStore.SaveAsync(read, extension, ct);
            }

            var format = extension.TrimStart('.').ToLowerInvariant();
            var stored = documents.AddFile(
                versionId,
                new StoredContent(
                    storagePath,
                    OutputName(upload, step.StepId, kind.Value, extension),
                    MediaTypeOf(format),
                    size,
                    sha256,
                    FileKind.Survey));

            var artefact = new ScanArtifact
            {
                SurveyUploadId = upload.Id,
                SurveyModelId = model.Id,
                Kind = kind.Value,
                Format = format,
                FileId = stored.Id,
                SizeBytes = size,
                Sha256 = sha256,
                StepId = step.StepId,
                Tool = string.IsNullOrEmpty(result.Tool) ? definition.Tool : result.Tool,
                ToolVersion = string.IsNullOrEmpty(result.ToolVersion) ? null : result.ToolVersion,
                ParamsHash = step.ParamsHash,
                InputArtifactIds = inputs.Select(a => a.Id).ToArray(),
                CreatedAt = now,
            };
            db.ScanArtifacts.Add(artefact);
            made.Add(artefact);
        }

        await db.SaveChangesAsync(ct);

        if (earlier.Count > 0)
        {
            var replacedFiles = earlier.Select(a => a.FileId).ToList();
            if (replacedFiles.Contains(model.ConvertedFileId ?? Guid.Empty))
            {
                model.ConvertedFileId = null;
            }

            // A raw recording's model is shown from its export: a new export replaces the old
            // one under it before the old file goes.
            if (replacedFiles.Contains(model.FileId)
                && made.FirstOrDefault(a => a.Kind == ScanArtifactKind.PointCloudLaz) is { } exported)
            {
                model.FileId = exported.FileId;
                model.Format = SurveyModelFormat.Laz;
            }

            db.ScanArtifacts.RemoveRange(earlier);
            await db.SaveChangesAsync(ct);
            var files = await db.StoredFiles.Where(f => replacedFiles.Contains(f.Id)).ToListAsync(ct);
            db.StoredFiles.RemoveRange(files);
            await db.SaveChangesAsync(ct);
            foreach (var file in files)
            {
                try
                {
                    await fileStore.DeleteAsync(file.StoragePath, ct);
                }
                catch (IOException e)
                {
                    logger.LogWarning(e, "The earlier product {Path} of {Step} could not be deleted", file.StoragePath, step.StepId);
                }
            }
        }

        return made;
    }

    private PointCloudFacts WithEstimate(PointCloudFacts facts, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("full", out var full))
        {
            return facts;
        }

        var rows = new List<PointCloudEstimateRow>();
        if (data.TryGetProperty("voxels", out var voxels) && voxels.ValueKind == JsonValueKind.Array)
        {
            rows.AddRange(voxels.EnumerateArray().Select(v => Row(v, Number(v, "voxelCm"))));
        }

        var estimate = new PointCloudEstimate(Row(full, null), rows);
        var hasIntensity = data.TryGetProperty("hasIntensity", out var hi) && hi.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? hi.GetBoolean()
            : facts.HasIntensity;
        return facts with
        {
            Estimate = estimate,
            Advice = PointCloudDisplayAdvice.For(estimate, Settings.DisplayWarnPoints, Settings.DisplayAdvisePoints, Settings.WarnTileBytes),
            HasIntensity = hasIntensity,
        };

        static PointCloudEstimateRow Row(JsonElement row, double? voxelCm) => new(
            voxelCm,
            Long(row, "points"),
            Long(row, "estimatedTileBytes"),
            Long(row, "estimatedSeconds") is { } s ? (int)Math.Min(s, int.MaxValue) : null);
    }

    private static PointCloudFacts WithDisplay(PointCloudFacts facts, JsonElement data) => facts with
    {
        DisplayVoxelM = Number(data, "voxelCm") is { } cm ? cm / 100d : null,
        DisplayEveryNth = Long(data, "everyNth") is { } n ? (int)Math.Min(n, int.MaxValue) : null,
        DisplayPointCount = Long(data, "pointsOut"),
    };

    private static PointCloudFacts WithTiles(PointCloudFacts facts, JsonElement data, ScanArtifact? tiles, string toolVersion) => facts with
    {
        TileBytes = tiles?.SizeBytes ?? Long(data, "archiveBytes"),
        TileCount = Long(data, "tileCount") is { } n ? (int)Math.Min(n, int.MaxValue) : null,
        TilerVersion = string.IsNullOrEmpty(toolVersion) ? null : toolVersion,
        OriginShift = Triple(data, "originShift"),
        HasIntensity = data.TryGetProperty("intensity", out var i) && i.ValueKind is JsonValueKind.True ? true : facts.HasIntensity,
    };

    private ScanOptions Settings => options.Value;

    private static string Words(ScanJobResult result) => result.Status switch
    {
        ScanJobStatus.Refused => "it refused the request" + Reason(result),
        ScanJobStatus.Cancelled => "the job was called off" + Reason(result),
        ScanJobStatus.Interrupted => "the worker was stopped while it ran" + Reason(result),
        ScanJobStatus.Failed => "it failed" + Reason(result),
        _ => "its answer could not be read" + Reason(result),
    };

    private static string Reason(ScanJobResult result) => string.IsNullOrWhiteSpace(result.Reason) ? "." : $": {result.Reason.Trim()}";

    private static string InputName(ScanArtifactKind kind) => kind switch
    {
        ScanArtifactKind.PointCloudLaz => "pointCloudLaz",
        ScanArtifactKind.PointCloudLazDisplay => "pointCloudLazDisplay",
        ScanArtifactKind.PointCloudTiles => "pointCloudTiles",
        ScanArtifactKind.Trajectory => "trajectory",
        ScanArtifactKind.Report => "report",
        ScanArtifactKind.RawRecording => "rawRecording",
        ScanArtifactKind.HdmSession => "hdmSession",
        _ => kind.ToString(),
    };

    private static ScanArtifactKind? KindOf(string name) => name switch
    {
        "pointCloudLaz" => ScanArtifactKind.PointCloudLaz,
        "pointCloudLazDisplay" => ScanArtifactKind.PointCloudLazDisplay,
        "pointCloudTiles" => ScanArtifactKind.PointCloudTiles,
        "trajectory" => ScanArtifactKind.Trajectory,
        "estimate" or "report" => ScanArtifactKind.Report,
        "hdmSession" => ScanArtifactKind.HdmSession,
        _ => null,
    };

    private static string OutputName(SurveyUpload upload, string stepId, ScanArtifactKind kind, string extension)
    {
        var stem = FileFormats.FitFileName(upload.SurveyTitle);
        stem = string.IsNullOrWhiteSpace(stem) ? "scan" : stem;
        return kind switch
        {
            ScanArtifactKind.PointCloudTiles => stem + ".tiles" + extension,
            ScanArtifactKind.PointCloudLazDisplay => stem + ".display" + extension,
            ScanArtifactKind.HdmSession => stem + ".session" + extension,
            ScanArtifactKind.Trajectory => stem + ".trajectory" + extension,
            ScanArtifactKind.Report when stepId == PointCloudCheckStep.StepId => stem + ".check" + extension,
            ScanArtifactKind.Report => stem + ".estimate" + extension,
            _ => stem + extension,
        };
    }

    private static string MediaTypeOf(string format) => format switch
    {
        "zip" => FileFormats.ZipContainerMimeType,
        "laz" => LazMediaType,
        "las" => LasMediaType,
        "json" => "application/json",
        "csv" => "text/csv",
        _ => "application/octet-stream",
    };

    private static bool IsPlainName(string name) =>
        !string.IsNullOrEmpty(name) && name is not ("." or "..") && !name.Contains('/') && !name.Contains('\\') && !name.Contains('\0');

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static long? Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (long)Math.Round(v.GetDouble())
            : null;

    private static double[]? Triple(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
        {
            return null;
        }

        var values = v.EnumerateArray().ToList();
        return values.All(x => x.ValueKind == JsonValueKind.Number) ? values.Select(x => x.GetDouble()).ToArray() : null;
    }
}
