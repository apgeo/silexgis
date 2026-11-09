// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Storage;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Storage;

/// <summary>What one confirmed deletion of a group gave back.</summary>
/// <param name="Items">How many things were deleted: files, directories, transfers or survey versions.</param>
/// <param name="FreedBytes">What the store (or, for work directories, the worker's volume) holds less afterwards.</param>
public sealed record StorageReclaimResult(string Group, int Items, long FreedBytes);

/// <summary>
/// A deletion stopped because something it was about to remove turned out to be an uploaded
/// original or still to be in use. Nothing was removed.
/// </summary>
public sealed class StorageReclaimRefusedException(string message) : Exception(message);

/// <summary>
/// Writes the daily storage report — what fills the file store, how fast it grows, what could be
/// deleted — and deletes one of those groups when an administrator confirms it.
/// </summary>
/// <remarks>
/// <para>
/// Every figure is an aggregate over the file table and the rows that name files, never a walk
/// of the store; the processing worker's job directories are the one exception, because no row
/// records them.
/// </para>
/// <para>
/// A deletion is recomputed when it is confirmed, never taken from the report it was offered in:
/// the report may be a day old, and something that has become a cave's current scan since, or
/// started running again, is not what the administrator agreed to delete.
/// </para>
/// </remarks>
public sealed class StorageReportService(
    SilexGisDbContext db,
    StorageFiguresService figures,
    ScanWorkerHandoff handoff,
    SurveyUploadArtefactStore artefacts,
    StoredContentRemover remover,
    IFileStore fileStore,
    ThumbnailService thumbnails,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<StorageReportService> logger)
{
    /// <summary>How many caves, accounts and files the report lists.</summary>
    public const int TopCount = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The newest report, or null when none has been written yet.</summary>
    public async Task<StorageReport?> LatestAsync(CancellationToken ct = default)
    {
        var json = await db.StorageReportSnapshots.AsNoTracking()
            .OrderByDescending(s => s.TakenAt)
            .Select(s => s.Report)
            .FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<StorageReport>(json, Json);
    }

    /// <summary>Measures storage now, keeps the report as the newest snapshot, and drops the ones past retention.</summary>
    public async Task<StorageReport> WriteAsync(CancellationToken ct = default)
    {
        var report = await MeasureAsync(ct);
        db.StorageReportSnapshots.Add(new StorageReportSnapshot
        {
            TakenAt = report.TakenAt,
            StoreUsedBytes = report.StoreUsedBytes,
            ScanUsedBytes = report.ScanUsedBytes,
            StoreFreeBytes = report.StoreFreeBytes,
            Report = JsonSerializer.Serialize(report, Json),
        });
        await db.SaveChangesAsync(ct);

        // The month's growth is measured against a report of a month ago, so at least that many
        // are kept whatever the setting says.
        var keepDays = Math.Max(31, options.Value.ReportRetentionDays);
        var cutoff = report.TakenAt.AddDays(-keepDays);
        await db.StorageReportSnapshots.Where(s => s.TakenAt < cutoff).ExecuteDeleteAsync(ct);
        return report;
    }

    /// <summary>Measures storage now, without keeping anything.</summary>
    public async Task<StorageReport> MeasureAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var at = await figures.ForAsync(null, ct: ct);

        var byFileKind = (await db.StoredFiles.AsNoTracking()
                .GroupBy(f => f.Kind)
                .Select(g => new { g.Key, Bytes = g.Sum(f => f.SizeBytes), Files = g.Count() })
                .ToListAsync(ct))
            .OrderByDescending(r => r.Bytes)
            .Select(r => new StorageKindTotal(Spelled(r.Key.ToString()), r.Bytes, r.Files))
            .ToList();

        var scanByKind = (await db.ScanArtifacts.AsNoTracking()
                .GroupBy(a => new { a.Kind, Original = a.StepId == ScanSteps.UploadStepId })
                .Select(g => new { g.Key.Kind, g.Key.Original, Bytes = g.Sum(a => a.SizeBytes), Files = g.Count() })
                .ToListAsync(ct))
            // Originals of every kind (an uploaded cloud, a raw recording) share one row, so the
            // label is the key: rows that spell the same are summed rather than listed twice.
            .GroupBy(r => r.Original ? "original" : Spelled(r.Kind.ToString()))
            .Select(g => new StorageKindTotal(g.Key, g.Sum(r => r.Bytes), g.Sum(r => r.Files)))
            .OrderByDescending(r => r.Bytes)
            .ToList();

        var workDirectories = WorkDirectories();
        var reclaimable = new List<StorageReclaimable>();
        foreach (var group in StorageReclaimGroups.All)
        {
            var (items, bytes) = await CandidatesAsync(group, ct);
            reclaimable.Add(new StorageReclaimable(group, items, bytes));
        }

        return new StorageReport(
            now,
            at.StoreUsedBytes,
            at.StoreCapBytes,
            at.ScanUsedBytes,
            at.ScanCapBytes,
            at.StoreFreeBytes,
            at.WorkerFreeBytes,
            at.MinFreeBytes,
            at.Level,
            byFileKind,
            scanByKind,
            workDirectories.Sum(d => d.Bytes),
            await ByCaveAsync(ct),
            await ByAccountAsync(ct),
            await LargestAsync(ct),
            await GrowthAsync(now, at, ct),
            reclaimable);
    }

    /// <summary>
    /// Deletes everything in <paramref name="group"/> as it stands now, and records who did it.
    /// </summary>
    /// <exception cref="ArgumentException">The group is not one that may be deleted.</exception>
    /// <exception cref="StorageReclaimRefusedException">Something in it is an original or still in use; nothing was deleted.</exception>
    public async Task<StorageReclaimResult> ReclaimAsync(string group, Guid byUserId, CancellationToken ct = default)
    {
        if (!StorageReclaimGroups.IsKnown(group))
        {
            throw new ArgumentException($"'{group}' is not a group that may be deleted.", nameof(group));
        }

        var result = group switch
        {
            StorageReclaimGroups.SupersededScanProducts => await ReclaimSupersededScanProductsAsync(ct),
            StorageReclaimGroups.FailedRunWork => ReclaimFailedRunWork(await FailedRunWorkAsync(ct)),
            StorageReclaimGroups.OlderSurveyVersions => await ReclaimOlderSurveyVersionsAsync(ct),
            _ => await ReclaimExpiredUploadsAsync(ct),
        };

        db.AuditEntries.Add(new AuditEntry
        {
            UserId = byUserId,
            Action = AuditActions.StorageReclaimed,
            EntityType = "StorageReclaimGroup",
            EntityId = group,
            Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["items"] = result.Items,
                ["freedBytes"] = result.FreedBytes,
            }),
        });
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>How many things <paramref name="group"/> holds now, and what deleting them would give back.</summary>
    public async Task<(int Items, long Bytes)> CandidatesAsync(string group, CancellationToken ct = default)
    {
        switch (group)
        {
            case StorageReclaimGroups.SupersededScanProducts:
            {
                var rows = await SupersededScanProducts().Select(a => a.SizeBytes).ToListAsync(ct);
                return (rows.Count, rows.Sum());
            }
            case StorageReclaimGroups.FailedRunWork:
            {
                var dirs = await FailedRunWorkAsync(ct);
                return (dirs.Count, dirs.Sum(d => d.Bytes));
            }
            case StorageReclaimGroups.OlderSurveyVersions:
            {
                var held = await OlderSurveyVersionsAsync(ct);
                return (held.Count, held.Sum(h => h.Bytes));
            }
            case StorageReclaimGroups.ExpiredUploads:
            {
                var now = clock.GetUtcNow();
                var rows = await db.UploadSessions.AsNoTracking()
                    .Where(s => s.ExpiresAt <= now)
                    .Select(s => s.ReceivedBytes)
                    .ToListAsync(ct);
                return (rows.Count, rows.Sum());
            }
            default:
                throw new ArgumentException($"'{group}' is not a group that may be deleted.", nameof(group));
        }
    }

    // ---- superseded scan products --------------------------------------------------------

    /// <summary>
    /// The thinned copies and tilesets of point-cloud models superseded by a newer version: a
    /// later cloud of the same survey has been tiled since. Only a run's products: an artefact the
    /// upload step recorded is the original, and a file some model reads as its source is never
    /// among them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Newer" is the order the clouds arrived in, never the current mark: the mark stays on the
    /// first cloud of a cave until somebody moves it, so a cloud without it is as often the newest
    /// as the oldest, and its products are what its own page shows.
    /// </para>
    /// <para>
    /// Three more things keep a model's products whatever came after it: being the cave's current
    /// scan; being the scan the cave is shown by on the map and the globe, which with no current
    /// scan tiled and placed is the newest one that is; and having its upload queued or running
    /// again, which takes up these very products.
    /// </para>
    /// </remarks>
    private IQueryable<ScanArtifact> SupersededScanProducts()
    {
        var cloudFormats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud).ToArray();
        var clouds = db.SurveyModels.Where(m => cloudFormats.Contains(m.Format));
        return db.ScanArtifacts.Where(a =>
            (a.Kind == ScanArtifactKind.PointCloudLazDisplay || a.Kind == ScanArtifactKind.PointCloudTiles)
            && a.StepId != ScanSteps.UploadStepId
            && clouds.Any(m => m.Id == a.SurveyModelId
                && !m.IsCurrent
                // Superseded: a later cloud of the same survey, tiled.
                && m.CaveSurveyId != null
                && clouds.Any(o => o.CaveSurveyId == m.CaveSurveyId && o.Id != m.Id
                    && o.ConvertedFileId != null && o.CreatedAt > m.CreatedAt)
                // Not what the cave is shown by: not tiled and placed itself, or beaten by a
                // tiled, placed cloud that is current or newer.
                && (m.ConvertedFileId == null || m.Anchor == null || m.AnchorHeightM == null
                    || clouds.Any(o => o.CaveFeatureId == m.CaveFeatureId && o.Id != m.Id
                        && o.ConvertedFileId != null && o.Anchor != null && o.AnchorHeightM != null
                        && (o.IsCurrent || o.CreatedAt > m.CreatedAt))))
            && !db.SurveyUploads.Any(u => u.Id == a.SurveyUploadId
                && (u.Status == SurveyUploadStatus.Receiving || u.Status == SurveyUploadStatus.Queued || u.Status == SurveyUploadStatus.Running))
            && !db.SurveyModels.Any(m => m.FileId == a.FileId)
            && !db.ScanArtifacts.Any(o => o.FileId == a.FileId && o.Id != a.Id)
            && !db.Attachments.Any(x => x.FileId == a.FileId));
    }

    private async Task<StorageReclaimResult> ReclaimSupersededScanProductsAsync(CancellationToken ct)
    {
        List<StoredFile> files;
        long freed;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Lock what the group reaches, then compute it again under the lock: a rerun queued or
            // a current scan chosen in the meantime is seen, and one coming after waits for this.
            var reached = await SupersededScanProducts()
                .Select(a => new { a.SurveyUploadId, CaveId = db.SurveyModels.Where(m => m.Id == a.SurveyModelId).Select(m => m.CaveFeatureId).FirstOrDefault() })
                .ToListAsync(ct);
            await StorageReportSql.LockScanRowsAsync(
                db,
                reached.Select(r => r.SurveyUploadId).Distinct().ToList(),
                reached.Select(r => r.CaveId).Where(id => id != Guid.Empty).Distinct().ToList(),
                ct);

            var rows = await SupersededScanProducts().ToListAsync(ct);
            var fileIds = rows.Select(a => a.FileId).Distinct().ToList();
            await RefuseOriginalsAsync(fileIds, ct);

            foreach (var model in await db.SurveyModels.Where(m => m.ConvertedFileId != null && fileIds.Contains(m.ConvertedFileId.Value)).ToListAsync(ct))
            {
                // The model keeps its original and can be run again; until then it has nothing to draw.
                model.ConvertedFileId = null;
            }

            db.ScanArtifacts.RemoveRange(rows);
            await db.SaveChangesAsync(ct);

            files = await db.StoredFiles.Where(f => fileIds.Contains(f.Id)).ToListAsync(ct);
            freed = files.Sum(f => f.SizeBytes);
            db.StoredFiles.RemoveRange(files);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e)
            {
                // A row this query does not know about still names one of the files: nothing goes.
                logger.LogWarning(e, "A superseded scan product is still referenced; nothing was deleted");
                throw new StorageReclaimRefusedException("A file in this group is still in use; nothing was deleted.");
            }

            await transaction.CommitAsync(ct);
        }

        // Bytes after the rows, never before: a row without its bytes is a fault, bytes without a row are litter.
        await remover.DropAsync(files);
        return new StorageReclaimResult(StorageReclaimGroups.SupersededScanProducts, files.Count, freed);
    }

    /// <summary>
    /// Stops a deletion that reaches an uploaded original: the file a point cloud's upload step
    /// archived, or the file any model reads as its source. Checked against the rows themselves at
    /// the moment of deletion, whatever the group's own query was meant to exclude, so a mistake
    /// in a group's definition refuses instead of deleting somebody's recording.
    /// </summary>
    private async Task RefuseOriginalsAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        if (fileIds.Count == 0)
        {
            return;
        }

        var original = await db.ScanArtifacts.AnyAsync(a => fileIds.Contains(a.FileId) && a.StepId == ScanSteps.UploadStepId, ct)
            || await db.SurveyModels.AnyAsync(m => fileIds.Contains(m.FileId), ct);
        if (original)
        {
            throw new StorageReclaimRefusedException("This group reaches an uploaded original, which is never deleted from here; nothing was deleted.");
        }
    }

    // ---- failed-run work directories -----------------------------------------------------

    private sealed record WorkDirectory(string Path, Guid? UploadId, long Bytes);

    /// <summary>
    /// The processing worker's job directories, each named for the upload whose run it served. A
    /// directory is the only trace of its run on the worker's volume and no row records it, so
    /// these are the one figure in the report taken from the disk.
    /// </summary>
    private List<WorkDirectory> WorkDirectories()
    {
        var root = Path.Combine(handoff.Root, ScanWorkerHandoff.JobsDirectory);
        var found = new List<WorkDirectory>();
        try
        {
            if (!Directory.Exists(root))
            {
                return found;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                Guid? upload = name.Length >= 32 && Guid.TryParseExact(name[..32], "N", out var id) ? id : null;
                found.Add(new WorkDirectory(directory, upload, SizeOf(directory)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The processing worker's job directories could not be read");
        }

        return found;
    }

    private static long SizeOf(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The job directories whose run is over: the upload failed, was abandoned, finished (a
    /// finished run removes its directory, so one left is litter) or no longer exists. A directory
    /// whose upload is queued or running is kept — a run interrupted by a restart takes its
    /// directory up again — and one not named for an upload was not made by this server.
    /// </summary>
    private async Task<List<WorkDirectory>> FailedRunWorkAsync(CancellationToken ct)
    {
        var directories = WorkDirectories().Where(d => d.UploadId is not null).ToList();
        if (directories.Count == 0)
        {
            return directories;
        }

        var ids = directories.Select(d => d.UploadId!.Value).Distinct().ToList();
        var live = await db.SurveyUploads.AsNoTracking()
            .Where(u => ids.Contains(u.Id)
                && (u.Status == SurveyUploadStatus.Receiving || u.Status == SurveyUploadStatus.Queued || u.Status == SurveyUploadStatus.Running))
            .Select(u => u.Id)
            .ToListAsync(ct);
        return directories.Where(d => !live.Contains(d.UploadId!.Value)).ToList();
    }

    private StorageReclaimResult ReclaimFailedRunWork(List<WorkDirectory> directories)
    {
        var items = 0;
        long freed = 0;
        foreach (var directory in directories)
        {
            try
            {
                Directory.Delete(directory.Path, recursive: true);
                items++;
                freed += directory.Bytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "A processing work directory could not be deleted");
            }
        }

        return new StorageReclaimResult(StorageReclaimGroups.FailedRunWork, items, freed);
    }

    // ---- older survey versions -----------------------------------------------------------

    private sealed record HeldVersion(Guid UploadId, long Bytes);

    /// <summary>
    /// The versions of each survey older than its newest one that produced something, with what
    /// their outputs hold — the same choice the scheduled prune makes when an installation keeps
    /// one version's outputs. Only what the prune would really delete is counted: an output whose
    /// model is the cave's current one, or has a trip's live tracking armed on it, is kept by the
    /// prune, so it is left out here too, and a version with nothing else is not offered at all.
    /// </summary>
    private async Task<List<HeldVersion>> OlderSurveyVersionsAsync(CancellationToken ct)
    {
        var surveys = await db.SurveyUploads.AsNoTracking()
            .GroupBy(u => u.CaveSurveyId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var prunable = new List<Guid>();
        foreach (var surveyId in surveys)
        {
            var versions = await db.SurveyUploads.AsNoTracking()
                .Where(u => u.CaveSurveyId == surveyId)
                .Select(u => new
                {
                    u.Id,
                    u.VersionAt,
                    Finished = u.Status == SurveyUploadStatus.Succeeded || u.Status == SurveyUploadStatus.Failed
                        || u.Status == SurveyUploadStatus.Abandoned,
                    HasOutputs = db.SurveyUploadFiles.Any(f => f.SurveyUploadId == u.Id
                        && f.Role == SurveyUploadFileRole.Output && f.Status == SurveyUploadFileStatus.Stored),
                })
                .ToListAsync(ct);
            prunable.AddRange(SurveyUploadArtefacts.Prunable(
                versions.Select(v => (v.Id, v.VersionAt, v.Finished, v.HasOutputs)), keep: 1));
        }

        if (prunable.Count == 0)
        {
            return [];
        }

        var held = await (
            from file in db.SurveyUploadFiles.AsNoTracking()
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentId equals (Guid?)version.DocumentId
            join stored in db.StoredFiles.AsNoTracking() on version.Id equals stored.DocumentVersionId
            where prunable.Contains(file.SurveyUploadId)
                && (file.Role == SurveyUploadFileRole.Output || file.Role == SurveyUploadFileRole.Log)
                && file.Status == SurveyUploadFileStatus.Stored
                && (file.SurveyModelId == null
                    || !db.SurveyModels.Any(m => m.Id == file.SurveyModelId
                        && (m.IsCurrent || db.TripTrackings.Any(t => t.SurveyModelId == m.Id && t.State == TripTrackingState.Armed))))
            group stored.SizeBytes by file.SurveyUploadId into perVersion
            select new { UploadId = perVersion.Key, Bytes = perVersion.Sum() })
            .ToListAsync(ct);
        return held.Select(h => new HeldVersion(h.UploadId, h.Bytes)).ToList();
    }

    private async Task<StorageReclaimResult> ReclaimOlderSurveyVersionsAsync(CancellationToken ct)
    {
        var versions = await OlderSurveyVersionsAsync(ct);
        var before = await db.StoredFiles.SumAsync(f => f.SizeBytes, ct);
        var items = 0;
        foreach (var version in versions)
        {
            // The prune keeps what a version was compiled from, and any model somebody has made
            // the cave's current one since: the version can be compiled again from what is left.
            if (await artefacts.PruneAsync(version.UploadId, ct) > 0)
            {
                items++;
            }
        }

        var after = await db.StoredFiles.SumAsync(f => f.SizeBytes, ct);
        return new StorageReclaimResult(StorageReclaimGroups.OlderSurveyVersions, items, Math.Max(0, before - after));
    }

    // ---- expired transfers ---------------------------------------------------------------

    /// <summary>
    /// Deletes the transfers past their retention, as the scheduled sweep does: each row is
    /// claimed by a delete of its own before its bytes are touched, so bytes an upload has just
    /// adopted as a document's file are never taken from under it — that upload removes the row
    /// in its own transaction, and the claim then finds nothing.
    /// </summary>
    private async Task<StorageReclaimResult> ReclaimExpiredUploadsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = await db.UploadSessions.AsNoTracking()
            .Where(s => s.ExpiresAt <= now)
            .Select(s => new { s.Id, s.StoragePath, s.ReceivedBytes })
            .ToListAsync(ct);

        var items = 0;
        long freed = 0;
        foreach (var session in expired)
        {
            var claimed = await db.UploadSessions.Where(s => s.Id == session.Id && s.ExpiresAt <= now).ExecuteDeleteAsync(ct);
            if (claimed == 0)
            {
                continue;
            }

            items++;
            freed += session.ReceivedBytes;
            try
            {
                await fileStore.DeleteAsync(session.StoragePath, ct);
                thumbnails.Purge(session.Id);
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "Could not delete the partial content of upload session {SessionId}", session.Id);
            }
        }

        return new StorageReclaimResult(StorageReclaimGroups.ExpiredUploads, items, freed);
    }

    // ---- breakdowns ----------------------------------------------------------------------

    /// <summary>
    /// Each file paired with the cave it belongs to: attached to the cave, or archived by one of
    /// the cave's survey uploads. A file belonging to a cave both ways is one pair, not two.
    /// It projects ids only, so no entity is ever loaded through it and tracking does not arise.
    /// </summary>
    private IQueryable<FileCave> FileCaves()
    {
        var attached = db.Attachments
            .Where(a => a.FeatureId != null && db.Caves.Any(c => c.Id == a.FeatureId))
            .Select(a => new FileCave { FileId = a.FileId, CaveId = a.FeatureId!.Value });
        var surveyed =
            from upload in db.SurveyUploads
            join file in db.SurveyUploadFiles on upload.Id equals file.SurveyUploadId
            join version in db.DocumentVersions on file.DocumentId equals (Guid?)version.DocumentId
            join stored in db.StoredFiles on version.Id equals stored.DocumentVersionId
            select new FileCave { FileId = stored.Id, CaveId = upload.CaveFeatureId };
        return attached.Union(surveyed);
    }

    private sealed class FileCave
    {
        public Guid FileId { get; init; }
        public Guid CaveId { get; init; }
    }

    private async Task<List<StorageHolder>> ByCaveAsync(CancellationToken ct)
    {
        var rows = await (
                from pair in FileCaves()
                join file in db.StoredFiles.AsNoTracking() on pair.FileId equals file.Id
                group file.SizeBytes by pair.CaveId into perCave
                select new { CaveId = perCave.Key, Bytes = perCave.Sum(), Files = perCave.Count() })
            .OrderByDescending(r => r.Bytes)
            .Take(TopCount)
            .ToListAsync(ct);
        var ids = rows.Select(r => r.CaveId).ToList();
        var names = await db.Features.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        return rows.Select(r => new StorageHolder(r.CaveId, names.GetValueOrDefault(r.CaveId), r.Bytes, r.Files)).ToList();
    }

    private async Task<List<StorageHolder>> ByAccountAsync(CancellationToken ct)
    {
        var rows = await (
                from file in db.StoredFiles.AsNoTracking()
                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                join document in db.Documents.AsNoTracking() on version.DocumentId equals document.Id
                group file.SizeBytes by document.OwnerUserId into perAccount
                select new { UserId = perAccount.Key, Bytes = perAccount.Sum(), Files = perAccount.Count() })
            .OrderByDescending(r => r.Bytes)
            .Take(TopCount)
            .ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.UserId).ToList(), ct);
        return rows.Select(r => new StorageHolder(r.UserId, names.GetValueOrDefault(r.UserId), r.Bytes, r.Files)).ToList();
    }

    private async Task<List<StorageLargeItem>> LargestAsync(CancellationToken ct)
    {
        var rows = await (
                from file in db.StoredFiles.AsNoTracking()
                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                join document in db.Documents.AsNoTracking() on version.DocumentId equals document.Id
                orderby file.SizeBytes descending
                select new { file.Id, file.OriginalName, file.Kind, file.SizeBytes, file.CreatedAt, DocumentId = document.Id, document.OwnerUserId })
            .Take(TopCount)
            .ToListAsync(ct);

        var fileIds = rows.Select(r => r.Id).ToList();
        var caveOf = (await FileCaves().Where(p => fileIds.Contains(p.FileId)).ToListAsync(ct))
            .GroupBy(p => p.FileId)
            .ToDictionary(g => g.Key, g => g.Min(p => p.CaveId));
        var caveIds = caveOf.Values.Distinct().ToList();
        var caveNames = await db.Features.AsNoTracking().Where(f => caveIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var owners = await NamesAsync(rows.Select(r => r.OwnerUserId).Distinct().ToList(), ct);

        return rows.Select(r =>
        {
            Guid? cave = caveOf.TryGetValue(r.Id, out var c) ? c : null;
            return new StorageLargeItem(
                r.Id,
                r.OriginalName,
                Spelled(r.Kind.ToString()),
                r.SizeBytes,
                r.DocumentId,
                r.OwnerUserId,
                owners.GetValueOrDefault(r.OwnerUserId),
                cave,
                cave is { } id ? caveNames.GetValueOrDefault(id) : null,
                r.CreatedAt);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(List<Guid> userIds, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName, ct);

    /// <summary>
    /// Growth against the reports of about a week and about a month ago. "About" is a window, not
    /// the nearest report whatever its age: a report from half a year ago is not last month's,
    /// and an installation that missed some days still gets a figure from the day nearest.
    /// </summary>
    private async Task<StorageGrowth> GrowthAsync(DateTimeOffset now, StorageFigures at, CancellationToken ct)
    {
        var week = await EarlierAsync(now, 7, 14, ct);
        var month = await EarlierAsync(now, 30, 60, ct);
        var weekGrowth = StorageGrowthRules.Since(at.StoreUsedBytes, week?.StoreUsedBytes);
        var monthGrowth = StorageGrowthRules.Since(at.StoreUsedBytes, month?.StoreUsedBytes);
        var pace = StorageGrowthRules.PerDay(monthGrowth, month is null ? 0 : (now - month.TakenAt).TotalDays)
            ?? StorageGrowthRules.PerDay(weekGrowth, week is null ? 0 : (now - week.TakenAt).TotalDays);
        long? room = at.StoreCapBytes is { } cap ? cap - at.StoreUsedBytes : at.StoreFreeBytes;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // The point clouds' cap is a ceiling of its own, and one run can take gigabytes of it, so
        // it gets its own pace and its own date, from the same reports.
        var scanPace = StorageGrowthRules.PerDay(
                StorageGrowthRules.Since(at.ScanUsedBytes, month?.ScanUsedBytes), month is null ? 0 : (now - month.TakenAt).TotalDays)
            ?? StorageGrowthRules.PerDay(
                StorageGrowthRules.Since(at.ScanUsedBytes, week?.ScanUsedBytes), week is null ? 0 : (now - week.TakenAt).TotalDays);
        var scansFullOn = at.ScanCapBytes is { } scanCap
            ? StorageGrowthRules.FullOn(scanCap - at.ScanUsedBytes, scanPace, today)
            : null;

        return new StorageGrowth(weekGrowth, monthGrowth, pace, StorageGrowthRules.FullOn(room, pace, today), scanPace, scansFullOn);
    }

    private sealed record Earlier(DateTimeOffset TakenAt, long StoreUsedBytes, long ScanUsedBytes);

    private async Task<Earlier?> EarlierAsync(DateTimeOffset now, int days, int oldestDays, CancellationToken ct)
    {
        // A report written a little after the day's hour still counts as that day's.
        var newest = now.AddDays(-days).AddHours(6);
        var oldest = now.AddDays(-oldestDays);
        return await db.StorageReportSnapshots.AsNoTracking()
            .Where(s => s.TakenAt <= newest && s.TakenAt >= oldest)
            .OrderByDescending(s => s.TakenAt)
            .Select(s => new Earlier(s.TakenAt, s.StoreUsedBytes, s.ScanUsedBytes))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>An enum member's name as answers spell it: <c>PointCloudTiles</c> reads <c>point-cloud-tiles</c>.</summary>
    public static string Spelled(string name)
    {
        var spelled = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                spelled.Append('-');
            }

            spelled.Append(char.ToLowerInvariant(name[i]));
        }

        return spelled.ToString();
    }
}
