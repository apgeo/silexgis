// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>One output the compiler wrote, by its catalogue name and where in the project it is.</summary>
public sealed record SurveyProducedOutput(string Output, string RelativePath);

/// <summary>What a finished compile left in its project directory, for storing.</summary>
/// <param name="ProjectDirectory">The project the compiler ran in.</param>
/// <param name="Produced">The outputs the compiler's log says it wrote in full.</param>
/// <param name="Missing">The outputs that were asked for and that the compiler did not write.</param>
/// <param name="LogText">The compiler's log, or null when it wrote none.</param>
/// <param name="SourceEpsg">
/// The coordinate system the models were written in, or null when the survey is fixed to no
/// position or the log names one no EPSG code is known for.
/// </param>
/// <param name="CoordinateSystemUnknown">
/// Whether the log names an output coordinate system that no EPSG code is known for — as against
/// naming none, which is a survey fixed to no position.
/// </param>
public sealed record SurveyCompiledProject(
    string ProjectDirectory,
    IReadOnlyList<SurveyProducedOutput> Produced,
    IReadOnlyList<SurveyProducedOutput> Missing,
    string? LogText,
    int? SourceEpsg,
    bool CoordinateSystemUnknown);

/// <summary>What the store stage came to.</summary>
/// <param name="OutputsStored">How many of the outputs the compiler wrote are now stored.</param>
/// <param name="LinePlotRead">Whether the stored Therion model was queued to be read.</param>
/// <param name="LinePlotUnplaced">Whether a Therion model was stored and left unread because it has no position.</param>
/// <param name="LinePlotCrsUnknown">
/// Whether a Therion model was stored and left unread because its coordinate system is one no EPSG
/// code is known for.
/// </param>
public sealed record SurveyStoredArtefacts(int OutputsStored, bool LinePlotRead, bool LinePlotUnplaced, bool LinePlotCrsUnknown);

/// <summary>
/// Stores what a compiled survey upload produced, each output as what it is: the Therion model as
/// a survey model of the cave, read into stations and legs; the Survex model as a survey model for
/// download and the viewer; drawings, tables and tracks as documents attached to the cave; an
/// output asked for and not written as a row saying so; the compiler's log as an archived source
/// with its compilation record; and the configuration that was compiled as an archived source
/// beside the upload's own.
/// </summary>
/// <remarks>
/// <para>
/// What a version was compiled from is stored before it is compiled (<see cref="StoreInputsAsync"/>),
/// and the compiler's log whenever it wrote one (<see cref="StoreLogAsync"/>), so a version whose
/// compile failed keeps the configuration to reproduce it in Therion and the log that says why.
/// </para>
/// <para>
/// Every stored thing names the survey and the upload it came from and joins the upload's batch,
/// so undoing the upload reaches all of it, and every name carries the upload's version stamp.
/// None of it takes the cave's current-model mark: a phone sending a survey does not decide which
/// model the cave is drawn from — that is somebody's deliberate act on the web.
/// </para>
/// <para>
/// Each file is stored on its own, so one that cannot be stored costs only itself. A run of the
/// same upload again finds its earlier files by name — the name is the upload's, stamp and all —
/// and stores what it produced this time as a new revision of each, rather than a second copy
/// beside it: a model then reads its new file, a log its new figures.
/// </para>
/// </remarks>
public sealed class SurveyUploadArtefactStore(
    SilexGisDbContext db,
    IFileStore fileStore,
    DocumentWriteService documents,
    FeatureWriteService features,
    IAppSettingsService settings,
    ILogger<SurveyUploadArtefactStore> logger)
{
    /// <summary>The tag every document a compiled upload produced carries.</summary>
    public const string OutputTagName = "survey-output";

    private const int ModelNameLength = 200;

    private const string UnplacedNote =
        "Not read: the survey is fixed to no position, so its coordinates are local and cannot be placed. "
        + "Fix a station in the survey and upload it again.";

    private const string CrsUnknownNote =
        "Not read: the survey's coordinate system is one this server cannot convert, so its model cannot be placed. "
        + "Give the survey a coordinate system with an EPSG code and upload it again.";

    /// <summary>
    /// What a version is compiled from beside the device's own files — the generated configuration
    /// and the wrapper survey around the uploaded one — kept as sources of the version before it is
    /// compiled, so the user can download them and carry on in Therion whatever the compile does.
    /// </summary>
    public async Task StoreInputsAsync(
        SurveyUpload upload, Feature cave, string projectDirectory, string configFile, string? wrapperFile, CancellationToken ct)
    {
        var surveyName = SurveyNameOf(upload);
        var version = upload.VersionAt;
        await TryAsync("the generated configuration", () => StoreSourceAsync(
            upload, cave, Path.Combine(projectDirectory, configFile),
            SurveyUploadArtefacts.ConfigFileName(surveyName, version), SurveySourceKind.TherionConfig,
            SurveyUploadFileRole.GeneratedConfig, null, ct), ct);
        if (wrapperFile is not null)
        {
            await TryAsync("the generated wrapper survey", () => StoreSourceAsync(
                upload, cave, Path.Combine(projectDirectory, wrapperFile),
                SurveyUploadArtefacts.WrapperFileName(surveyName, version), SurveySourceKind.TherionSource,
                SurveyUploadFileRole.GeneratedConfig, null, ct), ct);
        }
    }

    /// <summary>
    /// The compiler's log of a version, as an archived source with its compilation record and the
    /// reading of it — after a compile that failed as much as after one that worked, because the
    /// failed one is where the caver needs to read it. <paramref name="compiledModelId"/> is the
    /// Therion model the same compile produced, when it produced one.
    /// </summary>
    public async Task StoreLogAsync(
        SurveyUpload upload, Feature cave, string projectDirectory, string logText, Guid? compiledModelId, CancellationToken ct)
    {
        var logPath = Path.Combine(projectDirectory, SandboxedTherionRunner.LogFileName);
        if (!File.Exists(logPath))
        {
            // A compiler that answers over the network hands the log back as text.
            await File.WriteAllTextAsync(logPath, logText, ct);
        }

        await TryAsync("the compiler's log", () => StoreSourceAsync(
            upload, cave, logPath, SurveyUploadArtefacts.LogFileName(SurveyNameOf(upload), upload.VersionAt),
            SurveySourceKind.TherionLog, SurveyUploadFileRole.Log, compiledModelId, ct), ct);
    }

    /// <summary>
    /// What a compile produced, and its log. The inputs were stored before the compile, by
    /// <see cref="StoreInputsAsync"/>.
    /// </summary>
    public async Task<SurveyStoredArtefacts> StoreAsync(
        SurveyUpload upload, Feature cave, SurveyCompiledProject project, CancellationToken ct)
    {
        var surveyName = SurveyNameOf(upload);
        var title = string.IsNullOrWhiteSpace(upload.SurveyTitle) ? surveyName : upload.SurveyTitle;
        var version = upload.VersionAt;

        // The Survex model before the Therion one: a reader falling back to a cave's newest
        // finished model then finds the Therion model of the same upload, which is the one read.
        var outputs = project.Produced
            .Select(p => (Produced: p, Kind: SurveyUploadArtefacts.KindOf(p.Output)))
            .Where(p => p.Kind is not null)
            .OrderBy(p => p.Kind!.Storage switch
            {
                SurveyArtefactStorage.Survex => 0,
                SurveyArtefactStorage.LinePlot => 1,
                _ => 2,
            })
            .ToList();

        var tagId = outputs.Any(o => o.Kind!.Storage == SurveyArtefactStorage.Document)
            ? await OutputTagAsync(ct)
            : 0;
        var audience = outputs.Any(o => o.Kind!.Storage == SurveyArtefactStorage.Document)
            ? await AudienceAsync(upload, cave, ct)
            : null;

        var stored = 0;
        SurveyModel? linePlot = null;
        foreach (var (produced, kind) in outputs)
        {
            var path = Path.Combine([project.ProjectDirectory, .. produced.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
            var name = SurveyUploadArtefacts.FileName(surveyName, version, kind!);
            var done = await TryAsync(produced.Output, async () =>
            {
                switch (kind!.Storage)
                {
                    case SurveyArtefactStorage.LinePlot:
                    case SurveyArtefactStorage.Survex:
                        var model = await StoreModelAsync(
                            upload, cave, title, path, name, kind, project.SourceEpsg, project.CoordinateSystemUnknown, ct);
                        if (kind.Storage == SurveyArtefactStorage.LinePlot)
                        {
                            linePlot = model;
                        }

                        break;
                    default:
                        if (audience is null)
                        {
                            // The uploader may not create documents at all; the drawings are
                            // left out rather than filed under somebody else's right.
                            throw new SurveyPipelineException(SurveyPipelineFailures.StoreFailed, "No document right.");
                        }

                        await StoreDocumentAsync(upload, cave, title, path, name, kind, audience, tagId, ct);
                        break;
                }
            }, ct);
            stored += done ? 1 : 0;
        }

        // Asked for and not written: recorded per output, so a screen can say which, and the run
        // still succeeds on what was written.
        foreach (var missing in project.Missing)
        {
            if (SurveyUploadArtefacts.KindOf(missing.Output) is { } kind)
            {
                var name = SurveyUploadArtefacts.FileName(surveyName, version, kind);
                await TryAsync(missing.Output, () => RecordMissingAsync(upload.Id, name, kind.Output, ct), ct);
            }
        }

        if (project.LogText is not null)
        {
            await StoreLogAsync(upload, cave, project.ProjectDirectory, project.LogText, linePlot?.Id, ct);
        }

        if (outputs.Count > 0 && stored == 0)
        {
            throw new SurveyPipelineException(
                SurveyPipelineFailures.StoreFailed,
                "Therion compiled the survey, but none of what it produced could be stored. The files are archived; the server's log has the rest.");
        }

        return new SurveyStoredArtefacts(
            stored,
            linePlot is { Status: SurveyModelStatus.Pending },
            linePlot is { Status: SurveyModelStatus.Failed } && !project.CoordinateSystemUnknown,
            linePlot is { Status: SurveyModelStatus.Failed } && project.CoordinateSystemUnknown);
    }

    /// <summary>
    /// Records that an output asked for was not written by this run. An earlier run's stored file
    /// under the same name is left as it is: what this run did not write, it does not take away.
    /// </summary>
    private async Task RecordMissingAsync(Guid uploadId, string name, string output, CancellationToken ct)
    {
        var row = await RowAsync(uploadId, name, ct);
        if (row is { Status: SurveyUploadFileStatus.Stored })
        {
            return;
        }

        if (row is null)
        {
            row = new SurveyUploadFile { SurveyUploadId = uploadId, Name = name };
            db.SurveyUploadFiles.Add(row);
        }

        row.Role = SurveyUploadFileRole.Output;
        row.Output = output;
        row.Status = SurveyUploadFileStatus.Refused;
        row.RefusalCode = SurveyPipelineFailures.OutputMissing;
        await db.SaveChangesAsync(ct);
    }

    private static string SurveyNameOf(SurveyUpload upload) =>
        string.IsNullOrWhiteSpace(upload.SurveyName) ? upload.SurveyTitle : upload.SurveyName;

    /// <summary>
    /// Removes what one upload's compile produced — its models, its drawings and other outputs,
    /// and its log with the compilation record read from it — and keeps what it was compiled from:
    /// the device's files and the generated configuration stay, so the version can be compiled
    /// again. Answers how many outputs were removed.
    /// </summary>
    /// <remarks>
    /// A model somebody has since made the cave's current one, or that a trip's live tracking is
    /// armed on, is somebody's deliberate choice and stays; so does a document that has been
    /// attached or filed somewhere else since. Everything removed goes through the change
    /// tracker, so the cave's timeline records it.
    /// </remarks>
    public async Task<int> PruneAsync(Guid uploadId, CancellationToken ct)
    {
        var rows = await db.SurveyUploadFiles
            .Where(f => f.SurveyUploadId == uploadId
                && (f.Role == SurveyUploadFileRole.Output || f.Role == SurveyUploadFileRole.Log))
            .ToListAsync(ct);
        var removed = 0;
        foreach (var row in rows)
        {
            var blobs = new List<StoredFile>();
            if (row.SurveyModelId is { } modelId)
            {
                var model = await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == modelId, ct);
                if (model is not null)
                {
                    if (model.IsCurrent || await db.TripTrackings.AnyAsync(
                        t => t.SurveyModelId == model.Id && t.State == TripTrackingState.Armed, ct))
                    {
                        continue;
                    }

                    // The centreline a reading of this model drew goes with it, as when a model is
                    // deleted on the web; resource links naming it have no key to go by.
                    var extracted = await db.Centerlines
                        .Where(c => c.SurveyModelId == model.Id && c.Source == CenterlineSource.Extracted)
                        .Select(c => c.Id)
                        .ToListAsync(ct);
                    foreach (var centerlineId in extracted)
                    {
                        await features.DeleteCenterlineAsync(centerlineId, ct);
                    }

                    db.ResLinkMembers.RemoveRange(await db.ResLinkMembers
                        .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId == model.Id)
                        .ToListAsync(ct));
                    db.SurveyModels.Remove(model);
                    await db.SaveChangesAsync(ct);
                }
            }
            else if (row.SurveySourceId is { } sourceId
                && await db.SurveySources.FirstOrDefaultAsync(s => s.Id == sourceId, ct) is { } source)
            {
                // The compilation record read from the log goes with it.
                db.SurveySources.Remove(source);
                await db.SaveChangesAsync(ct);
            }

            if (row.DocumentId is { } documentId)
            {
                var fileIds = await (
                    from version in db.DocumentVersions.AsNoTracking()
                    join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                    where version.DocumentId == documentId
                    select new { file.Id, version.IsCurrent })
                    .ToListAsync(ct);
                var ids = fileIds.Select(f => f.Id).ToList();

                // The run's own attachment and tag, removed through the tracker so the timeline has
                // them; anything else pointing at the files keeps the document where it is.
                db.Attachments.RemoveRange(await db.Attachments
                    .Where(a => ids.Contains(a.FileId)
                        && db.SurveyUploads.Any(u => u.Id == uploadId && u.CaveFeatureId == a.FeatureId)
                        && a.Role == AttachmentRole.Other)
                    .ToListAsync(ct));
                db.Taggings.RemoveRange(await db.Taggings
                    .Where(t => t.EntityType == AttachedEntityType.StoredFile && t.EntityId != null && ids.Contains(t.EntityId.Value)
                        && db.Tags.Any(g => g.Id == t.TagId && g.Slug == OutputTagName))
                    .ToListAsync(ct));
                await db.SaveChangesAsync(ct);

                if (fileIds.FirstOrDefault(f => f.IsCurrent) is { } current
                    && !await db.Attachments.AnyAsync(a => ids.Contains(a.FileId), ct)
                    && !await db.Taggings.AnyAsync(
                        t => t.EntityType == AttachedEntityType.StoredFile && t.EntityId != null && ids.Contains(t.EntityId.Value), ct))
                {
                    blobs.AddRange(await documents.DeleteDocumentOfFileAsync(current.Id, ct));
                    await db.SaveChangesAsync(ct);
                }
            }

            db.SurveyUploadFiles.Remove(row);
            await db.SaveChangesAsync(ct);
            removed++;

            foreach (var blob in blobs)
            {
                await fileStore.DeleteAsync(blob.StoragePath, ct);
            }
        }

        return removed;
    }

    /// <summary>Runs one file's store; a failure is logged, undone and counted, never the run's.</summary>
    private async Task<bool> TryAsync(string what, Func<Task> store, CancellationToken ct)
    {
        try
        {
            await store();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // The file names here are the server's own, never anything out of the survey.
            logger.LogWarning(e, "A survey upload's output could not be stored: {What}", what);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<SurveyModel> StoreModelAsync(
        SurveyUpload upload,
        Feature cave,
        string title,
        string path,
        string name,
        SurveyArtefactKind kind,
        int? sourceEpsg,
        bool coordinateSystemUnknown,
        CancellationToken ct)
    {
        var format = kind.Storage == SurveyArtefactStorage.LinePlot ? SurveyModelFormat.Lox : SurveyModelFormat.Survex3d;
        // The one model reading a coordinate system matters to says so on its row too, where the
        // artefact list carries it as a code.
        var note = format == SurveyModelFormat.Lox && coordinateSystemUnknown ? SurveyPipelineFailures.CrsUnknown : null;
        var row = await RowAsync(upload.Id, name, ct);
        var content = await SaveAsync(path, name, kind.MediaType, FileKind.Survey, ct);
        SurveyModel? model = row?.SurveyModelId is { } modelId
            ? await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == modelId, ct)
            : null;

        if (model is not null && row!.DocumentId is { } documentId)
        {
            if (string.Equals(row.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await fileStore.DeleteAsync(content.StoragePath, ct);
                return model;
            }


            var revision = await documents.AddVersionAsync(await CurrentVersionIdAsync(documentId, ct), content, upload.RequestedBy, ct);
            model.FileId = revision.File.Id;
            model.SourceEpsg = format == SurveyModelFormat.Lox ? sourceEpsg : null;
            await ReadAsync(model, format, sourceEpsg, coordinateSystemUnknown, againIfReading: true, ct);
            Restamp(row, content, revision.Version.DocumentId);
            row.RefusalCode = note;
            await db.SaveChangesAsync(ct);
            return model;
        }

        var stored = documents.Create(content, name, upload.RequestedBy, upload.RequestedBy);
        db.Documents.Local.First(d => d.Id == stored.Version.DocumentId).UploadBatchId = upload.UploadBatchId;
        model = new SurveyModel
        {
            CaveFeatureId = cave.Id,
            Name = Cut(SurveyUploadArtefacts.DocumentTitle(title, upload.VersionAt, kind.Label), ModelNameLength),
            FileId = stored.File.Id,
            Format = format,
            SurveyedAt = upload.SurveyDate,
            CaveSurveyId = upload.CaveSurveyId,
            SurveyUploadId = upload.Id,
            IsCurrent = false,
        };
        db.SurveyModels.Add(model);
        if (await IsShapeOfItsOwnCaveAsync(upload, cave, ct))
        {
            // The Therion model is the line plot of record and takes the mark from the Survex one
            // of the same run whichever was stored first; the Survex one takes it only when nothing
            // holds it, so it is the cave's model only when the run produced no Therion model.
            if (format == SurveyModelFormat.Lox)
            {
                await SurveyModelCurrency.MakeCurrentAsync(db, model, ct);
            }
            else
            {
                await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);
            }
        }

        await ReadAsync(model, format, sourceEpsg, coordinateSystemUnknown, againIfReading: false, ct);
        Track(row, upload.Id, name, kind.Output, SurveyUploadFileRole.Output, content, stored.Version.DocumentId, null, model.Id)
            .RefusalCode = note;
        await db.SaveChangesAsync(ct);
        return model;
    }

    /// <summary>
    /// Whether a model this upload stores becomes its cave's current one. Otherwise an upload's
    /// model never does: which survey a cave is drawn and measured from is somebody's choice on the
    /// web. The one exception, which the installation may switch off, is a cave the upload itself
    /// made — nobody has chosen anything for it yet, and a cave with a survey but no shape is drawn
    /// as a bare point until someone thinks to choose. Only while the cave has no model and no
    /// centreline from anywhere else: the mark is never taken from something a person put there.
    /// </summary>
    private async Task<bool> IsShapeOfItsOwnCaveAsync(SurveyUpload upload, Feature cave, CancellationToken ct) =>
        upload.CreatedCave
        && (await settings.GetSurveyUploadsAsync(ct)).FirstModelOfCreatedCave
        && !await db.SurveyModels.AnyAsync(m => m.CaveFeatureId == cave.Id && m.SurveyUploadId != upload.Id, ct)
        && !await db.Centerlines.AnyAsync(c => c.CaveFeatureId == cave.Id, ct);

    /// <summary>
    /// What happens to a stored model next. The Therion model is read into stations and legs when
    /// it has a position to be placed at; the Survex model is kept for download and the viewer
    /// only — one reading per upload, and the Therion model is the line plot of record.
    /// </summary>
    private async Task ReadAsync(
        SurveyModel model, SurveyModelFormat format, int? sourceEpsg, bool coordinateSystemUnknown, bool againIfReading, CancellationToken ct)
    {
        if (format != SurveyModelFormat.Lox)
        {
            model.Status = SurveyModelStatus.Ready;
            model.ProcessingError = null;
            return;
        }

        if (sourceEpsg is null)
        {
            model.Status = SurveyModelStatus.Failed;
            model.ProcessingError = coordinateSystemUnknown ? CrsUnknownNote : UnplacedNote;
            return;
        }

        model.SourceEpsg = sourceEpsg;
        if (againIfReading && (await SurveyModelReading.OutstandingAsync(db, ct)).Contains(model.Id))
        {
            // A reading already waiting reads the model's file as it is when it runs: the new one.
            model.Status = SurveyModelStatus.Pending;
            return;
        }

        // No requester: the upload's own status page and its notice say how the run went.
        SurveyModelReading.QueueFirst(db, model, requestedBy: null);
    }

    private async Task StoreDocumentAsync(
        SurveyUpload upload,
        Feature cave,
        string title,
        string path,
        string name,
        SurveyArtefactKind kind,
        SurveyCaveAudience audience,
        long tagId,
        CancellationToken ct)
    {
        var row = await RowAsync(upload.Id, name, ct);
        var content = await SaveAsync(path, name, kind.MediaType, FileFormats.KindOf(kind.MediaType), ct);
        if (row?.DocumentId is { } documentId && await db.Documents.AnyAsync(d => d.Id == documentId, ct))
        {
            if (string.Equals(row.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await fileStore.DeleteAsync(content.StoragePath, ct);
                return;
            }

            // The attachment and the tag move onto the new revision with it.
            var revision = await documents.AddVersionAsync(await CurrentVersionIdAsync(documentId, ct), content, upload.RequestedBy, ct);
            Restamp(row, content, revision.Version.DocumentId);
            await db.SaveChangesAsync(ct);
            return;
        }

        var stored = documents.Create(
            content,
            SurveyUploadArtefacts.DocumentTitle(title, upload.VersionAt, kind.Label),
            upload.RequestedBy,
            upload.RequestedBy,
            upload.SurveyDate,
            audience.CavingGroupId);
        var document = db.Documents.Local.First(d => d.Id == stored.Version.DocumentId);
        document.Visibility = audience.Visibility;
        document.UploadBatchId = upload.UploadBatchId;
        db.Attachments.Add(new Attachment
        {
            FileId = stored.File.Id,
            FeatureId = cave.Id,
            Role = AttachmentRole.Other,
            AddedBy = upload.RequestedBy,
        });
        db.Taggings.Add(new Tagging
        {
            TagId = tagId,
            EntityType = AttachedEntityType.StoredFile,
            EntityId = stored.File.Id,
            AddedBy = upload.RequestedBy,
        });
        Track(row, upload.Id, name, kind.Output, SurveyUploadFileRole.Output, content, stored.Version.DocumentId, null, null);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// An archived source of the upload: the compiler's log (with its compilation record and the
    /// reading of it), or the configuration that was compiled.
    /// </summary>
    private async Task StoreSourceAsync(
        SurveyUpload upload,
        Feature cave,
        string path,
        string name,
        SurveySourceKind sourceKind,
        SurveyUploadFileRole role,
        Guid? compiledModelId,
        CancellationToken ct)
    {
        var row = await RowAsync(upload.Id, name, ct);
        var content = await SaveAsync(path, name, SurveySourceFormats.MediaTypeFor(sourceKind), FileKind.Survey, ct);
        if (row?.SurveySourceId is { } sourceId
            && await db.SurveySources.FirstOrDefaultAsync(s => s.Id == sourceId, ct) is { } source)
        {
            if (string.Equals(row.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await fileStore.DeleteAsync(content.StoragePath, ct);
                await LinkCompilationAsync(source.Id, compiledModelId, ct);
                return;
            }

            var revision = await documents.AddVersionAsync(await CurrentVersionIdAsync(source.DocumentId, ct), content, upload.RequestedBy, ct);
            if (sourceKind == SurveySourceKind.TherionLog)
            {
                // The record follows the new revision and is read again from it.
                await SurveyCompilationRequeue.ForNewRevisionAsync(
                    db, source.DocumentId, revision.File.Id, revision.Version.VersionNumber, requestedBy: null, ct);
            }

            Restamp(row, content, source.DocumentId);
            await db.SaveChangesAsync(ct);
            await LinkCompilationAsync(source.Id, compiledModelId, ct);
            return;
        }

        var stored = documents.Create(content, Path.GetFileNameWithoutExtension(name), upload.RequestedBy, upload.RequestedBy);
        db.Documents.Local.First(d => d.Id == stored.Version.DocumentId).UploadBatchId = upload.UploadBatchId;
        var created = new SurveySource
        {
            CaveFeatureId = cave.Id,
            DocumentId = stored.Version.DocumentId,
            Kind = sourceKind,
            Name = name,
            OriginalFileName = name,
            CaveSurveyId = upload.CaveSurveyId,
            SurveyUploadId = upload.Id,
        };
        db.SurveySources.Add(created);

        if (sourceKind == SurveySourceKind.TherionLog)
        {
            // Exactly what archiving a log on the web does, in the same save, plus the model the
            // same run produced: the loops this log reports are that model's.
            var compilation = new SurveyCompilation
            {
                CaveFeatureId = cave.Id,
                SurveySourceId = created.Id,
                LogFileId = stored.File.Id,
                LogVersionNumber = stored.Version.VersionNumber,
                SurveyModelId = compiledModelId,
            };
            db.SurveyCompilations.Add(compilation);
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyCompilation,
                Payload = JsonSerializer.Serialize(new SurveyCompilationPayload(compilation.Id), JsonSerializerOptions.Web),
            });
        }

        Track(row, upload.Id, name, null, role, content, stored.Version.DocumentId, created.Id, null);
        await db.SaveChangesAsync(ct);
    }

    private Task LinkCompilationAsync(Guid sourceId, Guid? modelId, CancellationToken ct) =>
        db.SurveyCompilations
            .Where(c => c.SurveySourceId == sourceId && c.SurveyModelId != modelId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SurveyModelId, modelId), ct);

    private Task<SurveyUploadFile?> RowAsync(Guid uploadId, string name, CancellationToken ct) =>
        db.SurveyUploadFiles.FirstOrDefaultAsync(f => f.SurveyUploadId == uploadId && f.Name == name, ct);

    private async Task<Guid> CurrentVersionIdAsync(Guid documentId, CancellationToken ct) =>
        await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId && v.IsCurrent)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException("A stored output's document has no current revision.");

    private SurveyUploadFile Track(
        SurveyUploadFile? row,
        Guid uploadId,
        string name,
        string? output,
        SurveyUploadFileRole role,
        StoredContent content,
        Guid documentId,
        Guid? sourceId,
        Guid? modelId)
    {
        if (row is null)
        {
            row = new SurveyUploadFile { SurveyUploadId = uploadId, Name = name };
            db.SurveyUploadFiles.Add(row);
        }

        row.Role = role;
        row.Output = output;
        row.Status = SurveyUploadFileStatus.Stored;
        // A row an earlier run recorded as not written is now written.
        row.RefusalCode = null;
        row.DeclaredSize = content.SizeBytes;
        row.SurveySourceId = sourceId;
        row.SurveyModelId = modelId;
        Restamp(row, content, documentId);
        return row;
    }

    private static void Restamp(SurveyUploadFile row, StoredContent content, Guid documentId)
    {
        row.ReceivedSize = content.SizeBytes;
        row.Sha256 = content.Sha256;
        row.DocumentId = documentId;
    }

    /// <summary>
    /// Puts one file of the project into the store. A directory — Therion writes a shapefile as
    /// one — goes in as a single archive of the files in it.
    /// </summary>
    private async Task<StoredContent> SaveAsync(string path, string name, string mediaType, FileKind kind, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            var zipped = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".stored.zip";
            File.Delete(zipped);
            ZipFile.CreateFromDirectory(path, zipped, CompressionLevel.Optimal, includeBaseDirectory: false);
            path = zipped;
        }

        string sha256;
        long size;
        await using (var read = File.OpenRead(path))
        {
            size = read.Length;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
        }

        string storagePath;
        await using (var read = File.OpenRead(path))
        {
            storagePath = await fileStore.SaveAsync(read, Path.GetExtension(name), ct);
        }

        return new StoredContent(storagePath, name, mediaType, size, sha256, kind);
    }

    /// <summary>The tag the run's documents carry, made the first time anything needs it.</summary>
    private async Task<long> OutputTagAsync(CancellationToken ct)
    {
        var slug = Tag.Slugify(OutputTagName);
        if (await db.Tags.AsNoTracking().Where(t => t.Slug == slug).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct) is { } id)
        {
            return id;
        }

        var tag = new Tag { Name = OutputTagName, Slug = slug };
        db.Tags.Add(tag);
        try
        {
            await db.SaveChangesAsync(ct);
            return tag.Id;
        }
        catch (DbUpdateException)
        {
            // Another run made it first; tags are installation-wide, so theirs is ours.
            db.ChangeTracker.Clear();
            return await db.Tags.AsNoTracking().Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync(ct);
        }
    }

    /// <summary>
    /// Who the run's documents are open to, by the same rule the device's own exports were filed
    /// under when they arrived — or null when the uploader may not create documents at all.
    /// </summary>
    private async Task<SurveyCaveAudience?> AudienceAsync(SurveyUpload upload, Feature cave, CancellationToken ct)
    {
        var ctx = await AccessContextResolver.ResolveAsync(db, upload.RequestedBy, ct);
        var audience = SurveyDocumentAudience.Resolve(
            ctx, cave.IsProtectedEffective, cave.Visibility, cave.CavingGroupId, upload.DocumentsVisibility);
        return audience.RefusalCode switch
        {
            null => audience,
            CreateRules.ForbiddenCode => null,
            // A club audience asked for where the cave has no club the account may file under:
            // the open route refused that for the device's exports, and the run files its own
            // privately rather than failing over it.
            _ => new SurveyCaveAudience(Visibility.Private, null),
        };
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
