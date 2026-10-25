// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using Therion.Build;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Processes one survey upload from a field device, stage by stage, writing where it has got to on
/// the upload's own status row for the device and the web to watch.
/// </summary>
/// <remarks>
/// <para>
/// Every file was archived when it arrived, before this runs; the run only reads, and reads the
/// survey archive through a read-only stream of the store, so nothing it does — and nothing a
/// crafted archive makes it do — can change what was archived. The archive stage checks that every
/// file the upload archived is still in the store; the unpack stage reads the archive's manifest
/// and the survey row and fixed stations of its dump, and keeps the survey's date, party and
/// declination on the row. A cave the upload itself made is placed at the survey's fixed station,
/// as its main entrance, through the path every entrance is written by.
/// </para>
/// <para>
/// A run that is not compiled ends there, successfully, with a note saying why — compiling was not
/// asked for, or the installation has no compiler — because an archived and readable survey is a
/// survey kept, which is what an upload is for.
/// </para>
/// <para>
/// A compiled run goes on in a directory of its own under the compiler's work directory, deleted
/// when the run ends however it ends. The scaffold stage writes the compile copies of the uploaded
/// survey and sketches there (checked to read nothing but each other) with the configuration
/// around them, and keeps the generated configuration and wrapper with the version before anything
/// is compiled; the compile stage hands the project to the installation's compiler and judges what
/// came back by the compiler's log rather than by the files lying about — an output counts when
/// the log says it was written in full — and keeps the whole log when the compile fails. The store
/// stage keeps each output as what it is, named for
/// the upload's version (see <see cref="SurveyUploadArtefactStore"/>), and the read stage is the
/// model's reading, queued with it: the run says it succeeded without waiting for the reading,
/// whose progress the model's own status reports.
/// </para>
/// <para>
/// Every stage can be run again from the start: a host stopped mid-run leaves the row running and
/// the job is handed back after the restart, and a rerun of a finished upload starts it over. A
/// failure is written on the row — a stable code and a sentence for the caver — before the job
/// itself is recorded as failed. No requester is named on the job, so the queue's generic "your job
/// finished" notice is not sent; the uploader gets a notice of the upload's own instead, linking to
/// its status page, when the run succeeds or fails.
/// </para>
/// </remarks>
public sealed class SurveyUploadPipelineHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    ISurveyCompiler compiler,
    IOptions<SurveyUploadOptions> options,
    IOptions<SurveyCompilerOptions> compilerOptions,
    IOptions<FilesOptions> filesOptions,
    SurveyUploadArtefactStore artefacts,
    FeatureWriteService features,
    ILogger<SurveyUploadPipelineHandler> logger) : IProcessingJobHandler
{
    /// <summary>How often a running compile's progress is written to the status row.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    public string Kind => ProcessingJobKinds.SurveyUploadPipeline;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The survey upload job names no upload.");

        var upload = await db.SurveyUploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == payload.SurveyUploadId, ct);
        if (upload is null || !await SurveyUploadWrites.StartAsync(db, upload.Id, ct))
        {
            // Deleted, or no longer waiting for a run: a job left over from a state the upload has
            // since left has nothing to do.
            logger.LogInformation("Survey upload {UploadId} is not waiting for a run; nothing to do", payload.SurveyUploadId);
            return;
        }

        var stage = SurveyUploadStage.Archive;
        string? runDirectory = null;
        try
        {
            var archived = await ArchivedFilesAsync(upload.Id, ct);
            var archivePath = archived.Single(f => f.Role == SurveyUploadFileRole.Archive).StoragePath;

            stage = SurveyUploadStage.Unpack;
            await SurveyUploadWrites.ProgressAsync(
                db, upload.Id, stage, SurveyPipelineProgress.Band(stage).Start, "Reading the survey archive.", ct);

            TopoDroidArchiveReading reading;
            await using (var archive = await OpenAsync(archivePath, ct))
            {
                reading = await TopoDroidArchiveReader.ReadAsync(archive, options.Value.UnpackedCap, ct);
            }

            await SurveyUploadWrites.RecordArchiveFactsAsync(db, upload.Id, reading.Survey, ct);

            // The later stages date what they write by the survey, so the copy they read says what
            // the row now says. It is not tracked: nothing here reaches the cave's timeline.
            upload.SurveyDate = reading.Survey.Date ?? upload.SurveyDate;

            // A cave this upload made is placed at the survey's fixed station whether or not the
            // survey is compiled: the position is the archive's, not the compiler's.
            await SurveyUploadEntrance.WriteFromFixAsync(db, features, upload, reading.Survey, ct);

            var asked = RunOptions(upload);
            if (SurveyPipelineNotes.WithoutCompiling(asked.Compile, compiler.IsAvailable) is { } note)
            {
                await SurveyUploadWrites.SucceedAsync(db, upload.Id, stage, note, ct);
                await NotifyAsync(upload, succeeded: true);
                return;
            }

            stage = SurveyUploadStage.Scaffold;
            await SurveyUploadWrites.ProgressAsync(
                db, upload.Id, stage, SurveyPipelineProgress.Band(stage).Start, "Building the Therion project.", ct);
            runDirectory = NewRunDirectory(upload.Id);
            var project = Path.Combine(runDirectory, "project");
            Directory.CreateDirectory(project);
            var scaffold = await ScaffoldAsync(upload, asked, archived, reading.Survey, project, ct);

            // What is compiled is kept before it is compiled, so a version whose compile fails can
            // still be downloaded and carried on with in Therion.
            var cave = await db.Features.AsNoTracking().FirstAsync(f => f.Id == upload.CaveFeatureId, ct);
            await artefacts.StoreInputsAsync(upload, cave, project, scaffold.ConfigFileName, WrapperOf(scaffold), ct);

            stage = SurveyUploadStage.Compile;
            var (compiled, log, logText) = await CompileAsync(upload, cave, project, scaffold, ct);

            stage = SurveyUploadStage.Store;
            await SurveyUploadWrites.ProgressAsync(
                db, upload.Id, stage, SurveyPipelineProgress.Band(stage).Start, "Storing what Therion produced.", ct);
            var stored = await artefacts.StoreAsync(upload, cave, Compiled(project, scaffold, compiled, log, logText), ct);

            // The reading of the model was queued with it; the run does not wait for it, and the
            // model's own status says when it is done.
            stage = SurveyUploadStage.Read;
            await SurveyUploadWrites.SucceedAsync(
                db,
                upload.Id,
                stage,
                SurveyPipelineNotes.Compiled(stored.OutputsStored, scaffold.ExpectedOutputs.Count, scaffold.SketchesDrawn.Count > 0)
                    + (stored.LinePlotUnplaced ? SurveyPipelineNotes.ModelUnplaced : "")
                    + (stored.LinePlotCrsUnknown ? SurveyPipelineNotes.ModelCrsUnknown : ""),
                ct);
            await NotifyAsync(upload, succeeded: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping, which is not this upload's verdict: the row stays running and
            // the job is handed back at the next start.
            throw;
        }
        catch (SurveyPipelineException e)
        {
            logger.LogInformation("Survey upload {UploadId} stopped at {Stage}: {Code}", upload.Id, stage, e.Code);
            await SurveyUploadWrites.FailAsync(db, upload.Id, stage, e.Code, e.Message, CancellationToken.None);
            await NotifyAsync(upload, succeeded: false);
            throw new InvalidOperationException($"Survey upload {upload.Id} stopped at {stage}: {e.Code}.");
        }
        catch (Exception e)
        {
            // A defect rather than something about the survey. The whole of it goes to the server's
            // log, which is the operator's; the row says only that it happened, because the inside
            // of the server is not the uploader's to read.
            logger.LogError(e, "Survey upload {UploadId} failed unexpectedly at {Stage}", upload.Id, stage);
            await SurveyUploadWrites.FailAsync(
                db,
                upload.Id,
                stage,
                SurveyPipelineFailures.Unexpected,
                "Something went wrong that this run did not expect. The files are archived; the server's log has the rest.",
                CancellationToken.None);
            await NotifyAsync(upload, succeeded: false);
            throw new InvalidOperationException($"Survey upload {upload.Id} failed unexpectedly at {stage}.", e);
        }
        finally
        {
            if (runDirectory is not null)
            {
                DeleteRunDirectory(runDirectory);
            }
        }
    }

    /// <summary>
    /// Tells the uploader the run ended, and how, with a link to the page it is watched on. The
    /// job names no requester, so this is the only notice an upload sends. Its words are the
    /// survey's title and the cave's name and nothing else; the run's own messages are left on the
    /// page, because the compiler's words over the caver's files can name stations and positions.
    /// Addressed to the cave, so whether the reader may still open it is decided when they read
    /// it. A notice that cannot be queued is logged and never turns a finished run into a failed
    /// one.
    /// </summary>
    private async Task NotifyAsync(SurveyUpload upload, bool succeeded)
    {
        try
        {
            // Whatever a failed stage left half-added must not ride along with the notice's save.
            db.ChangeTracker.Clear();
            var caveName = await db.Features.AsNoTracking()
                .Where(f => f.Id == upload.CaveFeatureId)
                .Select(f => f.Name)
                .FirstOrDefaultAsync(CancellationToken.None);
            var title = await db.CaveSurveys.AsNoTracking()
                .Where(s => s.Id == upload.CaveSurveyId)
                .Select(s => s.Title)
                .FirstOrDefaultAsync(CancellationToken.None) ?? upload.SurveyTitle;

            NotificationQueue.Enqueue(
                db,
                upload.RequestedBy,
                NotificationCategory.JobCompleted,
                succeeded ? MessageTemplateCatalog.NotifySurveyUploadSucceeded : MessageTemplateCatalog.NotifySurveyUploadFailed,
                new Dictionary<string, string>
                {
                    ["surveyTitle"] = title,
                    ["caveName"] = caveName ?? string.Empty,
                    ["url"] = SurveyUploadLinks.StatusPage(upload.CaveFeatureId, upload.CaveSurveyId, upload.Id),
                },
                NotificationTargetKind.Feature,
                upload.CaveFeatureId);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not queue the notice for survey upload {UploadId}", upload.Id);
        }
    }

    /// <summary>
    /// The scaffold stage: the uploaded survey and its sketches, as the compiler is to read them,
    /// with the configuration that names the outputs asked for.
    /// </summary>
    private async Task<SurveyScaffold> ScaffoldAsync(
        SurveyUpload upload,
        SurveyUploadRunOptions asked,
        IReadOnlyList<ArchivedFile> archived,
        TopoDroidSurveyFacts facts,
        string project,
        CancellationToken ct)
    {
        var surveys = archived.Where(f => f.Role == SurveyUploadFileRole.TherionSource).ToList();
        if (surveys.Count == 0)
        {
            throw new SurveyPipelineException(
                SurveyPipelineFailures.SourceMissing,
                "Compiling was asked for, but no Therion survey file (.th) came with the upload.");
        }

        if (surveys.Count > 1)
        {
            throw new SurveyPipelineException(
                SurveyPipelineFailures.SourceInvalid,
                "The upload holds more than one Therion survey file (.th); one survey is compiled at a time.");
        }

        var sketches = new List<UploadedTherionFile>();
        foreach (var sketch in archived.Where(f => f.Role == SurveyUploadFileRole.TherionSketch))
        {
            sketches.Add(new UploadedTherionFile(sketch.Name, await ReadTextAsync(sketch.StoragePath, ct)));
        }

        var input = new SurveyScaffoldInput(
            new UploadedTherionFile(surveys[0].Name, await ReadTextAsync(surveys[0].StoragePath, ct)),
            sketches,
            asked.Outputs.Count > 0 ? asked.Outputs : options.Value.DefaultOutputList(),
            asked.MapScale,
            upload.SurveyTitle,
            facts.FixedPoints.FirstOrDefault());

        return SurveyUploadScaffold.Write(project, input);
    }

    /// <summary>The generated survey around the uploaded one, relative to the project; null when none was written.</summary>
    private static string? WrapperOf(SurveyScaffold scaffold) =>
        scaffold.GeneratedFiles.FirstOrDefault(f =>
            !string.Equals(f, scaffold.ConfigFileName, StringComparison.Ordinal)
            && f.EndsWith(".th", StringComparison.OrdinalIgnoreCase));

    /// <summary>What the compile left for the store stage, each output under its catalogue name.</summary>
    private static SurveyCompiledProject Compiled(
        string project, SurveyScaffold scaffold, OutputReconciliation compiled, TherionLogSummary? log, string logText)
    {
        var produced = compiled.Produced.ToHashSet(StringComparer.Ordinal);
        var epsg = TherionCoordinateSystems.EpsgOf(log?.OutputCoordinateSystem);
        return new SurveyCompiledProject(
            project,
            [.. scaffold.ExpectedOutputs.Where(e => produced.Contains(e.RelativePath)).Select(e => new SurveyProducedOutput(e.Name, e.RelativePath))],
            [.. scaffold.ExpectedOutputs.Where(e => !produced.Contains(e.RelativePath)).Select(e => new SurveyProducedOutput(e.Name, e.RelativePath))],
            string.IsNullOrEmpty(logText) ? null : logText,
            epsg,
            // A log that names an output coordinate system is a survey with a position; when no
            // EPSG code is known for it, that is a different fact from a survey fixed nowhere.
            CoordinateSystemUnknown: epsg is null && !string.IsNullOrWhiteSpace(log?.OutputCoordinateSystem));
    }

    /// <summary>
    /// The compile stage: the project through the installation's compiler, judged by its log.
    /// Answers the outputs the log says were written in full, and what the log says.
    /// </summary>
    private async Task<(OutputReconciliation Reconciled, TherionLogSummary? Log, string LogText)> CompileAsync(
        SurveyUpload upload, Feature cave, string project, SurveyScaffold scaffold, CancellationToken ct)
    {
        var uploadId = upload.Id;
        await SurveyUploadWrites.ProgressAsync(
            db, uploadId, SurveyUploadStage.Compile, SurveyPipelineProgress.Band(SurveyUploadStage.Compile).Start,
            "Compiling with Therion.", ct);

        var timeout = compilerOptions.Value.Timeout;
        var latest = new LatestProgress();
        SurveyCompileResult result;
        try
        {
            var running = compiler.CompileAsync(new SurveyCompileRequest(project, scaffold.ConfigFileName, timeout), latest, ct);

            // The compiler reports from the threads reading its output; the database is written
            // from here only, every few seconds, with the latest of what it said.
            SurveyCompileProgress? written = null;
            while (!running.IsCompleted)
            {
                await Task.WhenAny(running, Task.Delay(ProgressInterval, ct));
                if (latest.Value is { } now && now != written)
                {
                    written = now;
                    await SurveyUploadWrites.ProgressAsync(
                        db, uploadId, SurveyUploadStage.Compile, SurveyPipelineProgress.Overall(SurveyUploadStage.Compile, now.Percent),
                        $"Compiling with Therion: {now.Stage}.", ct);
                }
            }

            result = await running;
        }
        catch (TimeoutException)
        {
            // Whatever log Therion had written by then is the only account of how far it got.
            var partial = Path.Combine(project, SandboxedTherionRunner.LogFileName);
            if (File.Exists(partial))
            {
                await artefacts.StoreLogAsync(upload, cave, project, await File.ReadAllTextAsync(partial, ct), null, ct);
            }

            throw new SurveyPipelineException(
                SurveyPipelineFailures.CompileTimeout,
                $"Therion did not finish within {timeout.TotalMinutes:0.#} minutes and was stopped.");
        }
        catch (SurveyCompilerUnavailableException e)
        {
            // The installation's compiler service is down or says it cannot compile: a fact about
            // the server, which the operator's log gets in full and the uploader as a sentence
            // saying a rerun will do once it is back.
            logger.LogWarning("Survey upload {UploadId} was not compiled: {Reason}", uploadId, e.Message);
            throw new SurveyPipelineException(
                SurveyPipelineFailures.CompilerUnavailable,
                "The survey compiler is not available at the moment; run the upload again later.");
        }

        await SurveyUploadWrites.RecordCompileAsync(db, uploadId, compiler.Version, result.OutputTail, ct);

        var log = TherionLogParser.LooksLikeTherionLog(SandboxedTherionRunner.LogFileName, result.LogText)
            ? TherionLogParser.Parse(result.LogText)
            : null;
        var reconciled = OutputReconciler.Reconcile(
            scaffold.ExpectedOutputs.Select(o => o.RelativePath), log?.OutputFiles ?? [], project);

        var verdict = SurveyCompileOutcome.Judge(result.ExitCode, log is null || log.Outcome == TherionLogOutcome.Failed, reconciled.Produced.Count);
        if (verdict != SurveyCompileVerdict.Succeeded && !string.IsNullOrEmpty(result.LogText))
        {
            // The whole log of a failed compile is kept with the version — the tail on the status
            // row is the last few lines, and the error is usually further up.
            await artefacts.StoreLogAsync(upload, cave, project, result.LogText, null, ct);
        }

        switch (verdict)
        {
            case SurveyCompileVerdict.CompilerFailed:
                // Therion's own words stay in the log tail, which only those who may change the
                // cave read: an error line can quote the survey line it stopped at, fix and all.
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.CompileFailed,
                    log is null
                        ? "Therion stopped without writing its log."
                        : $"Therion could not compile the survey ({log.Errors.Count()} errors). The log tail has what it said.");
            case SurveyCompileVerdict.NothingProduced:
                throw new SurveyPipelineException(
                    SurveyPipelineFailures.OutputMissing,
                    "Therion compiled the survey but wrote none of the outputs asked for.");
        }

        return (reconciled, log, result.LogText);
    }

    private string NewRunDirectory(Guid uploadId)
    {
        var configured = compilerOptions.Value.WorkDir;
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetFullPath(filesOptions.Value.Root, AppContext.BaseDirectory), "survey-work")
            : Path.GetFullPath(configured, AppContext.BaseDirectory);
        var run = Path.Combine(root, $"{uploadId:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(run);
        return run;
    }

    private void DeleteRunDirectory(string runDirectory)
    {
        try
        {
            Directory.Delete(runDirectory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "A survey compile directory could not be deleted: {Directory}", runDirectory);
        }
    }

    private async Task<string> ReadTextAsync(string storagePath, CancellationToken ct)
    {
        await using var stream = await OpenAsync(storagePath, ct);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>An archived file of the upload and where its bytes are in the store.</summary>
    private sealed record ArchivedFile(string Name, SurveyUploadFileRole Role, string StoragePath);

    /// <summary>
    /// The archive stage: every declared file archived, its archived bytes still in the store.
    /// Answers each file with where its bytes are.
    /// </summary>
    private async Task<IReadOnlyList<ArchivedFile>> ArchivedFilesAsync(Guid uploadId, CancellationToken ct)
    {
        var files = await db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.SurveyUploadId == uploadId)
            .Where(f => f.Role == SurveyUploadFileRole.Archive || f.Role == SurveyUploadFileRole.TherionSource
                || f.Role == SurveyUploadFileRole.TherionSketch || f.Role == SurveyUploadFileRole.Other)
            .ToListAsync(ct);

        if (files.Any(f => f.Status != SurveyUploadFileStatus.Stored || f.DocumentId is null || f.Sha256 is null))
        {
            throw Missing();
        }

        var documentIds = files.Select(f => f.DocumentId!.Value).ToList();
        var stored = await (
            from version in db.DocumentVersions.AsNoTracking()
            join blob in db.StoredFiles.AsNoTracking() on version.Id equals blob.DocumentVersionId
            where documentIds.Contains(version.DocumentId) && blob.ConvertedFromFileId == null
            select new { version.DocumentId, blob.Sha256, blob.StoragePath })
            .ToListAsync(ct);

        var archived = new List<ArchivedFile>();
        foreach (var file in files)
        {
            // The bytes this upload received, by their digest, rather than whatever is the
            // document's current revision now: somebody may have added a revision on the web since.
            var path = stored
                .FirstOrDefault(s => s.DocumentId == file.DocumentId && string.Equals(s.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                ?.StoragePath ?? throw Missing();

            await using (await OpenAsync(path, ct))
            {
            }

            archived.Add(new ArchivedFile(file.Name, file.Role, path));
        }

        return archived.Any(f => f.Role == SurveyUploadFileRole.Archive) ? archived : throw Missing();
    }

    private async Task<Stream> OpenAsync(string storagePath, CancellationToken ct)
    {
        try
        {
            return await fileStore.OpenReadAsync(storagePath, ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing();
        }
    }

    private static SurveyPipelineException Missing() =>
        new(SurveyPipelineFailures.FilesMissing, "A file this upload archived can no longer be found on the server.");

    /// <summary>What the run was asked to do, as the upload recorded it; compiling is assumed asked when unreadable.</summary>
    private static SurveyUploadRunOptions RunOptions(SurveyUpload upload)
    {
        try
        {
            return JsonSerializer.Deserialize<SurveyUploadRunOptions>(upload.Options, JsonSerializerOptions.Web)
                ?? new SurveyUploadRunOptions(true, [], SurveyUploadRunOptions.DefaultMapScale);
        }
        catch (JsonException)
        {
            return new SurveyUploadRunOptions(true, [], SurveyUploadRunOptions.DefaultMapScale);
        }
    }

    /// <summary>The latest progress a compiler reported, read and written from different threads.</summary>
    private sealed class LatestProgress : IProgress<SurveyCompileProgress>
    {
        private SurveyCompileProgress? value;

        public SurveyCompileProgress? Value => Volatile.Read(ref value);

        public void Report(SurveyCompileProgress progress) => Volatile.Write(ref value, progress);
    }
}
