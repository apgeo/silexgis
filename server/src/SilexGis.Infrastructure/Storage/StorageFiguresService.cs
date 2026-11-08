// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Storage;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Infrastructure.Storage;

/// <summary>
/// How full storage is right now, as one value: the installation's total and cap, the scans'
/// total and cap, the asking account's quota, and the free space of the two volumes bytes land on.
/// </summary>
/// <param name="AccountUsedBytes">What the account holds; zero when it has no quota (it is only counted against one).</param>
/// <param name="AccountCapBytes">The account's quota, or null for none.</param>
/// <param name="StoreUsedBytes">What the whole file store holds, with the bytes reserved by transfers still arriving.</param>
/// <param name="StoreCapBytes">The installation's cap, or null for none.</param>
/// <param name="ScanUsedBytes">What point clouds take: originals and every product, every version, with the bytes reserved by scan uploads still arriving.</param>
/// <param name="ScanCapBytes">The scans' own cap, or null for none.</param>
/// <param name="StoreFreeBytes">Free space on the file store's volume, or null when it cannot be read.</param>
/// <param name="WorkerFreeBytes">Free space on the processing worker's volume, or null when it cannot be read.</param>
/// <param name="MinFreeBytes">The free space the installation wants kept on each volume.</param>
/// <param name="Thresholds">The percentages warned at.</param>
public sealed record StorageFigures(
    long AccountUsedBytes,
    long? AccountCapBytes,
    long StoreUsedBytes,
    long? StoreCapBytes,
    long ScanUsedBytes,
    long? ScanCapBytes,
    long? StoreFreeBytes,
    long? WorkerFreeBytes,
    long MinFreeBytes,
    StorageThresholds Thresholds)
{
    /// <summary>
    /// The ceilings a request is checked under, in the order a person can act on them: their own
    /// quota first, then the installation's cap, then — for a point cloud — the scans' cap, then
    /// the volumes. Only limits that exist are listed.
    /// </summary>
    /// <param name="scan">Whether the bytes are a point cloud's (its upload, or a product of its run).</param>
    /// <param name="onWorkerDisk">Whether the bytes are written on the worker's volume as well — a processing step's inputs and outputs are.</param>
    public IReadOnlyList<StorageLimit> Limits(bool scan, bool onWorkerDisk = false)
    {
        var limits = new List<StorageLimit>();
        if (AccountCapBytes is { } account)
        {
            limits.Add(new StorageLimit(StorageLimitKind.Account, account, AccountUsedBytes));
        }

        if (StoreCapBytes is { } store)
        {
            limits.Add(new StorageLimit(StorageLimitKind.Installation, store, StoreUsedBytes));
        }

        if (scan && ScanCapBytes is { } scans)
        {
            limits.Add(new StorageLimit(StorageLimitKind.Scans, scans, ScanUsedBytes));
        }

        if (StoreFreeBytes is { } storeFree)
        {
            limits.Add(StorageLimit.Disk(StorageLimitKind.StoreDisk, storeFree));
        }

        if (onWorkerDisk && WorkerFreeBytes is { } workerFree)
        {
            limits.Add(StorageLimit.Disk(StorageLimitKind.WorkerDisk, workerFree));
        }

        return limits;
    }

    /// <summary>
    /// How full each installation-wide ceiling is: its cap, the scans' cap and the two volumes.
    /// The account's quota is not among them — it is one person's, and they are told at upload.
    /// </summary>
    public IReadOnlyDictionary<StorageLimitKind, StorageLevel> Levels => new Dictionary<StorageLimitKind, StorageLevel>
    {
        [StorageLimitKind.Installation] = StorageRules.LevelOf(StoreUsedBytes, StoreCapBytes, Thresholds),
        [StorageLimitKind.Scans] = StorageRules.LevelOf(ScanUsedBytes, ScanCapBytes, Thresholds),
        [StorageLimitKind.StoreDisk] = StorageRules.DiskLevelOf(StoreFreeBytes, MinFreeBytes),
        [StorageLimitKind.WorkerDisk] = StorageRules.DiskLevelOf(WorkerFreeBytes, MinFreeBytes),
    };

    /// <summary>How full the installation is: the worst of its cap, the scans' cap and the two volumes.</summary>
    public StorageLevel Level => StorageRules.Worst(Levels.Values);
}

/// <summary>
/// Reads <see cref="StorageFigures"/>. Every figure is an aggregate over the file table and the
/// rows that reserve room, never a walk of the disk and never a maintained counter, for the same
/// reason the upload quota is: a counter is wrong the first time something deletes a file by a
/// route that forgot it, and a number that refuses uploads has to be right.
/// </summary>
public sealed class StorageFiguresService(
    SilexGisDbContext db,
    UploadAllowanceService allowances,
    IOptions<FilesOptions> files,
    IOptions<ScanOptions> scans,
    IOptions<StorageOptions> storage,
    ScanWorkerHandoff handoff,
    TimeProvider clock)
{
    private readonly string storeRoot = Path.GetFullPath(files.Value.Root, AppContext.BaseDirectory);

    /// <summary>The file store's directory, whose volume <see cref="StorageFigures.StoreFreeBytes"/> is read from.</summary>
    public string StoreRoot => storeRoot;

    /// <summary>The processing worker's hand-off directory, whose volume <see cref="StorageFigures.WorkerFreeBytes"/> is read from.</summary>
    public string WorkerRoot => handoff.Root;

    /// <summary>
    /// The figures for <paramref name="userId"/> (null for none — the installation's alone).
    /// </summary>
    /// <param name="excludingUploadId">
    /// A survey upload whose own reservation is left out: when one of its files arrives, the room
    /// it reserved at opening is what that file is about to take, and counting it as well would
    /// refuse the upload for its own reservation.
    /// </param>
    public async Task<StorageFigures> ForAsync(Guid? userId, Guid? excludingUploadId = null, CancellationToken ct = default)
    {
        long accountUsed = 0;
        long? accountCap = null;
        if (userId is { } user)
        {
            var allowance = await allowances.ForAsync(user, ct);
            accountCap = allowance.UserQuotaBytes;
            accountUsed = allowance.UserUsedBytes
                + (accountCap is null ? 0 : await ReservedAsync(u => u.RequestedBy == user, excludingUploadId, ct));
        }

        var stored = await db.StoredFiles.AsNoTracking().SumAsync(f => f.SizeBytes, ct);
        var staged = await db.UploadSessions.AsNoTracking().Where(s => s.StagedForSync).SumAsync(s => s.DeclaredSizeBytes, ct);
        var reserved = await ReservedAsync(_ => true, excludingUploadId, ct);
        var scanStored = await ScanStoredAsync(ct);
        var scanReserved = await ReservedAsync(u => u.Source == SurveyOrigin.PointCloud, excludingUploadId, ct);

        var settings = files.Value;
        return new StorageFigures(
            accountUsed,
            accountCap,
            stored + staged + reserved,
            settings.MaxTotalStoreBytes > 0 ? settings.MaxTotalStoreBytes : null,
            scanStored + scanReserved,
            scans.Value.TotalCap,
            FreeBytes(storeRoot),
            handoff.Enabled ? FreeBytes(handoff.Root) : null,
            Math.Max(0, storage.Value.MinFreeBytes),
            storage.Value.Thresholds);
    }

    /// <summary>
    /// What the file store holds for point clouds: every file of every document a point-cloud
    /// upload archived its original into. The products of a run are files of that same document,
    /// so this one sum covers originals, thinned copies, tilesets and reports, every version of
    /// each — and counts an original from the moment it is archived, before the run has recorded
    /// it as an artefact.
    /// </summary>
    private Task<long> ScanStoredAsync(CancellationToken ct) =>
        db.StoredFiles.AsNoTracking()
            .Where(f => db.DocumentVersions.Any(v =>
                v.Id == f.DocumentVersionId
                && db.SurveyUploadFiles.Any(sf =>
                    sf.DocumentId == v.DocumentId
                    && db.SurveyUploads.Any(u => u.Id == sf.SurveyUploadId && u.Source == SurveyOrigin.PointCloud))))
            .SumAsync(f => f.SizeBytes, ct);

    /// <summary>
    /// The bytes survey uploads have declared and not yet sent, while the upload is still open.
    /// Counted from the moment the upload opens — the same reservation the opening is judged
    /// against — so two point clouds opened side by side cannot each fit into the room only one
    /// of them has. An upload past its expiry reserves nothing: it can no longer receive files.
    /// </summary>
    private Task<long> ReservedAsync(
        System.Linq.Expressions.Expression<Func<SurveyUpload, bool>> which, Guid? excludingUploadId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var uploads = db.SurveyUploads.AsNoTracking()
            .Where(u => u.Status == SurveyUploadStatus.Receiving && u.ExpiresAt > now)
            .Where(which);
        if (excludingUploadId is { } excluded)
        {
            uploads = uploads.Where(u => u.Id != excluded);
        }

        return db.SurveyUploadFiles.AsNoTracking()
            .Where(f => f.Status == SurveyUploadFileStatus.Declared && uploads.Any(u => u.Id == f.SurveyUploadId))
            .SumAsync(f => f.DeclaredSize, ct);
    }

    /// <summary>
    /// The free space of the volume holding <paramref name="directory"/>, or null when it cannot
    /// be read. The directory is made first so that a fresh installation, whose store has never
    /// been written, still answers for the volume it will be on.
    /// </summary>
    public static long? FreeBytes(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return new DriveInfo(directory).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
