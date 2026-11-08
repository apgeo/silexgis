// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Storage;

/// <summary>
/// What fills the file store at one moment, written once a day so an administrator can see what
/// takes the room, how fast it grows and what could be given back. Plain data: it is stored as
/// one document per snapshot and answered as it was stored.
/// </summary>
/// <param name="TakenAt">When it was measured.</param>
/// <param name="StoreUsedBytes">What the whole file store holds, with the room reserved by transfers still arriving.</param>
/// <param name="StoreCapBytes">The installation's cap, or null for none.</param>
/// <param name="ScanUsedBytes">What point clouds take, originals and products.</param>
/// <param name="ScanCapBytes">The point clouds' own cap, or null for none.</param>
/// <param name="StoreFreeBytes">Free space on the file store's volume, or null when it could not be read.</param>
/// <param name="WorkerFreeBytes">Free space on the processing worker's volume, or null when there is none or it could not be read.</param>
/// <param name="MinFreeBytes">The free space the installation wants kept on each volume.</param>
/// <param name="Level">How full the installation is, the worst of its ceilings.</param>
/// <param name="ByFileKind">The whole store by the kind of file.</param>
/// <param name="ScanByKind">Point clouds' files by what they are: raw originals apart from what processing made of them.</param>
/// <param name="WorkDirectoryBytes">What the processing worker's job directories hold on its own volume (not in the file store).</param>
/// <param name="ByCave">The caves whose files take the most, largest first.</param>
/// <param name="ByAccount">The accounts whose documents take the most, largest first.</param>
/// <param name="Largest">The largest single files, largest first.</param>
/// <param name="Growth">How the store grew, and when at that rate it is full.</param>
/// <param name="Reclaimable">What could be deleted, group by group, with what each would give back.</param>
public sealed record StorageReport(
    DateTimeOffset TakenAt,
    long StoreUsedBytes,
    long? StoreCapBytes,
    long ScanUsedBytes,
    long? ScanCapBytes,
    long? StoreFreeBytes,
    long? WorkerFreeBytes,
    long MinFreeBytes,
    StorageLevel Level,
    IReadOnlyList<StorageKindTotal> ByFileKind,
    IReadOnlyList<StorageKindTotal> ScanByKind,
    long WorkDirectoryBytes,
    IReadOnlyList<StorageHolder> ByCave,
    IReadOnlyList<StorageHolder> ByAccount,
    IReadOnlyList<StorageLargeItem> Largest,
    StorageGrowth Growth,
    IReadOnlyList<StorageReclaimable> Reclaimable);

/// <summary>One kind of file and what its files take.</summary>
/// <param name="Kind">The kind, as answers spell it (lower case, hyphenated).</param>
public sealed record StorageKindTotal(string Kind, long Bytes, int Files);

/// <summary>One cave or account and what its files take.</summary>
/// <param name="Id">The cave's feature id, or the account's id.</param>
/// <param name="Name">Its name as it reads now, or null when it has none.</param>
public sealed record StorageHolder(Guid Id, string? Name, long Bytes, int Files);

/// <summary>One large file, with whose it is and how old.</summary>
/// <param name="CaveId">A cave the file belongs to, when it belongs to one.</param>
public sealed record StorageLargeItem(
    Guid FileId,
    string Name,
    string Kind,
    long Bytes,
    Guid DocumentId,
    Guid OwnerUserId,
    string? OwnerName,
    Guid? CaveId,
    string? CaveName,
    DateTimeOffset CreatedAt);

/// <summary>A group of things that could be deleted, and what deleting them would give back.</summary>
/// <param name="Group">One of <see cref="StorageReclaimGroups"/>.</param>
public sealed record StorageReclaimable(string Group, int Items, long Bytes);

/// <summary>
/// How the store grew over the last week and month, and the day it is full at that pace.
/// </summary>
/// <param name="SevenDayBytes">Growth since the snapshot of about a week ago, or null when there is none that old.</param>
/// <param name="ThirtyDayBytes">Growth since the snapshot of about a month ago, or null when there is none that old.</param>
/// <param name="BytesPerDay">The pace the projection uses: the month's when known, else the week's.</param>
/// <param name="FullOn">The day the installation's cap — or, without one, the store's volume — is reached at that pace; null when it is not growing or nothing bounds it.</param>
/// <param name="ScanBytesPerDay">The point clouds' own pace, measured the same way over what they hold.</param>
/// <param name="ScansFullOn">The day the point clouds' cap is reached at their pace; null without that cap, or when they are not growing.</param>
public sealed record StorageGrowth(
    long? SevenDayBytes,
    long? ThirtyDayBytes,
    long? BytesPerDay,
    DateOnly? FullOn,
    long? ScanBytesPerDay = null,
    DateOnly? ScansFullOn = null)
{
    /// <summary>No history to compare against.</summary>
    public static StorageGrowth None { get; } = new(null, null, null, null);
}

/// <summary>
/// The groups an administrator may delete from the storage report, one confirmed action each.
/// </summary>
/// <remarks>
/// Every group holds only what can be made again or was never finished. Uploaded originals —
/// the raw recordings and every file somebody sent — are in none of them and have no group of
/// their own: they are the record, and nothing here offers them, whatever an administrator asks.
/// </remarks>
public static class StorageReclaimGroups
{
    /// <summary>Thinned copies and tilesets of point clouds no longer their cave's current one; remade by running the scan again.</summary>
    public const string SupersededScanProducts = "superseded-scan-products";

    /// <summary>Processing work directories left behind by runs that failed, were abandoned or no longer exist.</summary>
    public const string FailedRunWork = "failed-run-work";

    /// <summary>What compiling older versions of a survey produced — models, drawings, logs — kept from the newest version onward; remade by compiling that version again.</summary>
    public const string OlderSurveyVersions = "older-survey-versions";

    /// <summary>Transfers past their retention that nobody completed, and the partial bytes they hold.</summary>
    public const string ExpiredUploads = "expired-uploads";

    /// <summary>Every group, in the order a report lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [SupersededScanProducts, FailedRunWork, OlderSurveyVersions, ExpiredUploads];

    /// <summary>Whether <paramref name="group"/> names a group that may be deleted.</summary>
    public static bool IsKnown(string? group) => group is not null && All.Contains(group, StringComparer.Ordinal);
}

/// <summary>Growth arithmetic of the storage report.</summary>
public static class StorageGrowthRules
{
    /// <summary>
    /// How much the store grew since an earlier measurement, or null when there is none.
    /// </summary>
    public static long? Since(long usedNow, long? usedThen) => usedThen is { } then ? usedNow - then : null;

    /// <summary>
    /// The pace per day between two measurements <paramref name="days"/> apart, or null when the
    /// growth is unknown or the span is not positive.
    /// </summary>
    public static long? PerDay(long? growth, double days) =>
        growth is { } g && days > 0 ? (long)Math.Round(g / days) : null;

    /// <summary>
    /// The day <paramref name="room"/> bytes run out at <paramref name="bytesPerDay"/>, counted
    /// from <paramref name="today"/>. Null when nothing bounds the store, when it is not growing,
    /// or when the day is beyond any calendar worth showing. A store already past its ceiling is
    /// full today.
    /// </summary>
    public static DateOnly? FullOn(long? room, long? bytesPerDay, DateOnly today)
    {
        if (room is not { } left || bytesPerDay is not { } pace || pace <= 0)
        {
            return null;
        }

        if (left <= 0)
        {
            return today;
        }

        var days = Math.Ceiling(left / (double)pace);
        return days > 36_500 ? null : today.AddDays((int)days);
    }
}
