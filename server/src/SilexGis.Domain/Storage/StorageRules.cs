// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Storage;

/// <summary>
/// How full a limit is, in the steps an administrator is told about. Stored as a number on the
/// row that remembers the last level reported, so values are appended, never renumbered.
/// </summary>
public enum StorageLevel : short
{
    /// <summary>Below the notice threshold, or no limit at all.</summary>
    Normal = 0,

    /// <summary>At or above the notice threshold (three quarters by default).</summary>
    Notice = 1,

    /// <summary>At or above the warning threshold (nine tenths by default).</summary>
    Warning = 2,

    /// <summary>Nothing more fits: the limit is reached or passed.</summary>
    Full = 3,
}

/// <summary>
/// Which ceiling a piece of storage is measured against. The first three are configured caps
/// counted over the file table; the last two are the free space of the volumes the bytes land on.
/// </summary>
public enum StorageLimitKind : short
{
    /// <summary>The uploading account's own quota.</summary>
    Account = 0,

    /// <summary>The whole installation's file store cap.</summary>
    Installation = 1,

    /// <summary>The cap on everything kept for point clouds (scans): originals and their products.</summary>
    Scans = 2,

    /// <summary>The free space of the volume holding the file store.</summary>
    StoreDisk = 3,

    /// <summary>The free space of the volume the processing worker works in.</summary>
    WorkerDisk = 4,
}

/// <summary>
/// One ceiling and how much of it is used. A volume's free space is expressed in the same shape —
/// its cap is what can still be written, and nothing of it is "used" —
/// so one fit rule answers for configured caps and real disks alike.
/// </summary>
public sealed record StorageLimit(StorageLimitKind Kind, long CapBytes, long UsedBytes)
{
    /// <summary>What still fits under this ceiling. Never negative: a cap lowered below what is held leaves no room rather than a debt.</summary>
    public long RoomBytes => Math.Max(0, CapBytes - UsedBytes);

    /// <summary>
    /// A volume's free space as a limit: the bytes that can still be written to it. Only a
    /// request that cannot be written at all is refused on this ceiling; running low is what the
    /// warnings are for (see <see cref="StorageRules.DiskLevelOf"/>), so a reserve kept for
    /// warning does not become space nobody may use.
    /// </summary>
    public static StorageLimit Disk(StorageLimitKind kind, long availableBytes) =>
        new(kind, Math.Max(0, availableBytes), 0);
}

/// <summary>Why something does not fit: under which ceiling, how much it needs, how much room there is, and so how much is missing.</summary>
public sealed record StorageShortfall(StorageLimitKind Kind, long NeededBytes, long RoomBytes)
{
    /// <summary>How much has to be freed (or the cap raised by) before this fits.</summary>
    public long MissingBytes => Math.Max(0, NeededBytes - RoomBytes);
}

/// <summary>The threshold percentages an installation warns at.</summary>
/// <param name="NoticePercent">The first, gentle, threshold; 75 by default.</param>
/// <param name="WarningPercent">The second, urgent, threshold; 90 by default.</param>
public sealed record StorageThresholds(int NoticePercent, int WarningPercent)
{
    /// <summary>The installation defaults: a notice at three quarters, a warning at nine tenths.</summary>
    public static StorageThresholds Default { get; } = new(75, 90);

    /// <summary>
    /// The thresholds as configured, made sane: each held between 1 and 100, and the warning never
    /// below the notice — an operator who sets them the wrong way round gets one level that
    /// means both rather than a warning that fires before its notice.
    /// </summary>
    public StorageThresholds Normalized()
    {
        var notice = Math.Clamp(NoticePercent, 1, 100);
        var warning = Math.Clamp(WarningPercent, 1, 100);
        return new StorageThresholds(notice, Math.Max(notice, warning));
    }
}

/// <summary>The arithmetic of storage: whether something fits, and how full a limit is.</summary>
public static class StorageRules
{
    /// <summary>
    /// The worst shortfall of adding <paramref name="addingBytes"/> under every one of
    /// <paramref name="limits"/>, or null when it fits under all of them.
    /// </summary>
    /// <remarks>
    /// Every ceiling is checked and the one missing most is answered, because that is the figure
    /// the person has to act on: freeing the smaller amount would still leave the request refused
    /// by the other ceiling. On a tie the first named wins, so callers list the ceilings a person
    /// can do something about (their own quota) before the ones only an administrator can.
    /// Nothing to add always fits — an empty request is refused, if at all, for being empty.
    /// </remarks>
    public static StorageShortfall? Shortfall(IEnumerable<StorageLimit> limits, long addingBytes)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (addingBytes <= 0)
        {
            return null;
        }

        StorageShortfall? worst = null;
        foreach (var limit in limits)
        {
            var room = limit.RoomBytes;
            if (addingBytes <= room)
            {
                continue;
            }

            var shortfall = new StorageShortfall(limit.Kind, addingBytes, room);
            if (worst is null || shortfall.MissingBytes > worst.MissingBytes)
            {
                worst = shortfall;
            }
        }

        return worst;
    }

    /// <summary>
    /// How full <paramref name="usedBytes"/> makes a cap of <paramref name="capBytes"/>, against
    /// <paramref name="thresholds"/>. No cap (null, or zero or less) is never past any threshold.
    /// </summary>
    /// <remarks>
    /// Compared in whole bytes rather than through a rounded percentage, so a store one byte short
    /// of its notice threshold is not reported as having crossed it.
    /// </remarks>
    public static StorageLevel LevelOf(long usedBytes, long? capBytes, StorageThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (capBytes is not { } cap || cap <= 0)
        {
            return StorageLevel.Normal;
        }

        var sane = thresholds.Normalized();
        var used = (decimal)Math.Max(0, usedBytes);
        if (used >= cap)
        {
            return StorageLevel.Full;
        }

        if (used * 100 >= (decimal)cap * sane.WarningPercent)
        {
            return StorageLevel.Warning;
        }

        return used * 100 >= (decimal)cap * sane.NoticePercent ? StorageLevel.Notice : StorageLevel.Normal;
    }

    /// <summary>
    /// How low a volume's free space is against the free space the installation wants kept
    /// (<paramref name="minFreeBytes"/>): nothing left is <see cref="StorageLevel.Full"/>; under
    /// the minimum a warning; under twice the minimum a notice. Unknown free space, or no minimum
    /// configured, is normal — a volume that cannot be read is not reported as full.
    /// </summary>
    /// <remarks>
    /// The minimum is sized as about twice the largest run expected, so falling under it means the
    /// next two runs may not fit, and twice it is the first moment worth an administrator's
    /// attention. Expressed against that minimum rather than as a percentage of the volume because a
    /// large disk at 90 % can still hold a year of scans and a small one at 50 % may not hold the next.
    /// </remarks>
    public static StorageLevel DiskLevelOf(long? availableBytes, long minFreeBytes)
    {
        if (availableBytes is not { } free)
        {
            return StorageLevel.Normal;
        }

        if (free <= 0)
        {
            return StorageLevel.Full;
        }

        if (minFreeBytes <= 0)
        {
            return StorageLevel.Normal;
        }

        if (free < minFreeBytes)
        {
            return StorageLevel.Warning;
        }

        return free < 2 * minFreeBytes ? StorageLevel.Notice : StorageLevel.Normal;
    }

    /// <summary>
    /// Whether moving from <paramref name="last"/> (the level last reported or fallen back to) to
    /// <paramref name="now"/> is a crossing administrators are told about: only a climb is. Staying
    /// at a level says nothing new, and falling back is recorded without a message, so that
    /// climbing past the same threshold again later is a fresh crossing and is told again.
    /// </summary>
    /// <remarks>
    /// A climb past two thresholds at once (normal straight to warning) is one crossing and one
    /// message, at the level reached: the notice it skipped would arrive in the same breath and say
    /// less.
    /// </remarks>
    public static bool IsReportedCrossing(StorageLevel last, StorageLevel now) => now > last;

    /// <summary>
    /// A size as a person reads it in a message: decimal units (a gigabyte is a thousand
    /// megabytes, as disks are sold), one decimal place above a kilobyte, written the same in every
    /// language so a figure in a mail matches the one the storage page shows.
    /// </summary>
    public static string Describe(long bytes)
    {
        string[] units = ["kB", "MB", "GB", "TB", "PB"];
        if (bytes < 1000)
        {
            return bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";
        }

        var value = (double)bytes;
        var unit = -1;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// The whole percentage <paramref name="usedBytes"/> is of <paramref name="capBytes"/>, rounded
    /// down so a store at 89.9 % is not told it is at 90 %; zero when there is no cap.
    /// </summary>
    public static int PercentOf(long usedBytes, long capBytes) =>
        capBytes <= 0 ? 0 : (int)Math.Min(int.MaxValue, (decimal)Math.Max(0, usedBytes) * 100 / capBytes);

    /// <summary>The highest of several levels.</summary>
    public static StorageLevel Worst(IEnumerable<StorageLevel> levels) =>
        levels.DefaultIfEmpty(StorageLevel.Normal).Max();
}
