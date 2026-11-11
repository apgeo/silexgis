// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Storage;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Storage;

/// <summary>One ceiling that climbed past a threshold since the last pass, and the level it reached.</summary>
public sealed record StorageCrossing(StorageLimitKind Kind, StorageLevel From, StorageLevel To);

/// <summary>
/// Tells administrators when storage fills up: each installation-wide ceiling (the file store's
/// cap, the point clouds' cap, the free space of the two disks) is measured, compared with the
/// level last recorded for it, and a climb past a threshold is told once — to every administrator,
/// through the ordinary notification channel, so their own preferences decide whether it also
/// arrives by mail.
/// </summary>
/// <remarks>
/// <para>
/// A fall back is recorded without a message. Remembering it is what makes the rule "once per
/// crossing" rather than "once ever": space freed and filled again is a second crossing and is told
/// again, while a store sitting at 80 % for a month is told once.
/// </para>
/// <para>
/// The recorded level moves only by an update that names the level it moves from, inside the same
/// transaction that queues the messages. Two passes racing over the same crossing therefore cannot
/// both tell it: the second finds the row already moved and does nothing, and a pass that fails
/// after recording leaves no recorded level without its message.
/// </para>
/// </remarks>
public sealed class StorageAlerts(SilexGisDbContext db, StorageFiguresService figures, TimeProvider clock)
{
    /// <summary>The administrators' page the messages link to: what fills the store, and what could be deleted.</summary>
    public const string ReportPage = "/admin/storage";

    /// <summary>Measures storage now and tells administrators about every threshold crossed upwards since the last pass.</summary>
    public async Task<IReadOnlyList<StorageCrossing>> CheckAsync(CancellationToken ct = default) =>
        await CheckAsync(await figures.ForAsync(null, ct: ct), ct);

    /// <summary>The same pass over figures already read.</summary>
    public async Task<IReadOnlyList<StorageCrossing>> CheckAsync(StorageFigures now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(now);
        var at = clock.GetUtcNow();
        var recorded = await db.StorageAlertStates.AsNoTracking()
            .ToDictionaryAsync(s => s.LimitKind, s => s.Level, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var crossings = new List<StorageCrossing>();
        foreach (var (kind, level) in now.Levels)
        {
            var known = recorded.TryGetValue(kind, out var last);
            if (!known)
            {
                // No row is the same as a row at normal: only a level above it is worth a row.
                if (level == StorageLevel.Normal)
                {
                    continue;
                }

                // A racing pass inserting the same ceiling fails this save on the key, and its
                // transaction — with any message it queued — goes with it.
                db.StorageAlertStates.Add(new StorageAlertState { LimitKind = kind, Level = level, ChangedAt = at });
            }
            else
            {
                if (level == last)
                {
                    continue;
                }

                var moved = await db.StorageAlertStates
                    .Where(s => s.LimitKind == kind && s.Level == last)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Level, level).SetProperty(s => s.ChangedAt, at), ct);
                if (moved == 0)
                {
                    // Another pass moved it first and told whatever there was to tell.
                    continue;
                }
            }

            var from = known ? last : StorageLevel.Normal;
            if (StorageRules.IsReportedCrossing(from, level))
            {
                crossings.Add(new StorageCrossing(kind, from, level));
            }
        }

        if (crossings.Count > 0)
        {
            // The full administrators, held directly or through a caving group, whose accounts can
            // sign in: the people who can raise a cap, free space or add a disk, and the only ones
            // the report page the messages link to opens for. A locked account would only collect
            // messages nobody reads.
            var administrators = await FullAdministrators.LiveMemberIdsAsync(db, ct);
            foreach (var crossing in crossings)
            {
                var (template, placeholders) = MessageOf(crossing.Kind, now);
                foreach (var administrator in administrators)
                {
                    NotificationQueue.Enqueue(db, administrator, NotificationCategory.StorageAlert, template, placeholders);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return crossings;
    }

    private (string Template, IReadOnlyDictionary<string, string> Placeholders) MessageOf(StorageLimitKind kind, StorageFigures now) =>
        kind switch
        {
            StorageLimitKind.Installation => (MessageTemplateCatalog.NotifyStorageStoreCap,
                CapPlaceholders(now.StoreUsedBytes, now.StoreCapBytes ?? 0)),
            StorageLimitKind.Scans => (MessageTemplateCatalog.NotifyStorageScansCap,
                CapPlaceholders(now.ScanUsedBytes, now.ScanCapBytes ?? 0)),
            StorageLimitKind.StoreDisk => (MessageTemplateCatalog.NotifyStorageDiskLow,
                DiskPlaceholders(figures.StoreRoot, now.StoreFreeBytes ?? 0, now.MinFreeBytes)),
            StorageLimitKind.WorkerDisk => (MessageTemplateCatalog.NotifyStorageDiskLow,
                DiskPlaceholders(figures.WorkerRoot, now.WorkerFreeBytes ?? 0, now.MinFreeBytes)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an installation-wide ceiling."),
        };

    private static Dictionary<string, string> CapPlaceholders(long used, long cap) => new()
    {
        ["used"] = StorageRules.Describe(used),
        ["cap"] = StorageRules.Describe(cap),
        ["percent"] = StorageRules.PercentOf(used, cap).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["url"] = ReportPage,
    };

    private static Dictionary<string, string> DiskPlaceholders(string directory, long free, long minFree) => new()
    {
        ["directory"] = directory,
        ["free"] = StorageRules.Describe(free),
        ["minFree"] = StorageRules.Describe(minFree),
        ["url"] = ReportPage,
    };
}
