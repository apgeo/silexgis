// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

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
/// declination on the row.
/// </para>
/// <para>
/// A run that is not compiled ends there, successfully, with a note saying why — compiling was not
/// asked for, or the installation has no compiler — because an archived and readable survey is a
/// survey kept, which is what an upload is for.
/// </para>
/// <para>
/// Every stage can be run again from the start: a host stopped mid-run leaves the row running and
/// the job is handed back after the restart, and a rerun of a finished upload starts it over. A
/// failure is written on the row — a stable code and a sentence for the caver — before the job
/// itself is recorded as failed. No requester is named on the job, so the queue's generic "your job
/// finished" notice is not sent; an upload's watcher has its status page.
/// </para>
/// </remarks>
public sealed class SurveyUploadPipelineHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    ISurveyCompiler compiler,
    IOptions<SurveyUploadOptions> options,
    ILogger<SurveyUploadPipelineHandler> logger) : IProcessingJobHandler
{
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
        try
        {
            var archivePath = await ArchivedFilesAsync(upload.Id, ct);

            stage = SurveyUploadStage.Unpack;
            await SurveyUploadWrites.ProgressAsync(
                db, upload.Id, stage, SurveyPipelineProgress.Band(stage).Start, "Reading the survey archive.", ct);

            TopoDroidArchiveReading reading;
            await using (var archive = await OpenAsync(archivePath, ct))
            {
                reading = await TopoDroidArchiveReader.ReadAsync(archive, options.Value.UnpackedCap, ct);
            }

            await SurveyUploadWrites.RecordArchiveFactsAsync(db, upload.Id, reading.Survey, ct);

            var asked = RunOptions(upload);
            await SurveyUploadWrites.SucceedAsync(
                db, upload.Id, stage, SurveyPipelineNotes.WithoutCompiling(asked.Compile, compiler.IsAvailable), ct);
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
            throw new InvalidOperationException($"Survey upload {upload.Id} failed unexpectedly at {stage}.", e);
        }
    }

    /// <summary>
    /// The archive stage: every declared file archived, its archived bytes still in the store.
    /// Answers where the survey archive's bytes are.
    /// </summary>
    private async Task<string> ArchivedFilesAsync(Guid uploadId, CancellationToken ct)
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

        string? archivePath = null;
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

            if (file.Role == SurveyUploadFileRole.Archive)
            {
                archivePath = path;
            }
        }

        return archivePath ?? throw Missing();
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
}
