// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Storage;

namespace SilexGis.Infrastructure.Storage;

/// <summary>
/// When an installation's storage is reported as filling up. The caps themselves stay where they
/// were configured — the file store's installation and per-account caps, the scans' own total —
/// and these say how near a cap, or how little free disk, is worth an administrator's attention.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>The percentage of a cap at which administrators get a notice. 75 by default.</summary>
    public int NoticePercent { get; set; } = 75;

    /// <summary>The percentage of a cap at which administrators get a warning. 90 by default.</summary>
    public int WarningPercent { get; set; } = 90;

    /// <summary>
    /// The free space wanted on the file store's volume and on the processing worker's, in bytes.
    /// Under it administrators are warned, under twice it they get a notice. 20 GiB by default:
    /// about twice the largest run expected, since one recorded scan can take 5–10 GB across its
    /// original, its intermediate products and its tiles. Zero switches the free-space warnings off;
    /// a request that cannot be written at all is refused whatever this says.
    /// </summary>
    public long MinFreeBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>
    /// How often storage is measured and administrators told about any threshold crossed since
    /// the last pass. Hourly by default: a crossing is told within the hour, and a pass is a few
    /// sums over the file table and two free-space reads. Zero or less switches the schedule off.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often the storage report is written: what fills the store, how fast it grows and what
    /// could be deleted. Daily by default, which is what the week's and the month's growth are
    /// measured against. Zero or less switches the schedule off; an administrator can still
    /// measure from the storage page.
    /// </summary>
    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How many daily reports are kept, in days. A year by default; the month's growth needs at
    /// least 31. Older reports are removed when a new one is written.
    /// </summary>
    public int ReportRetentionDays { get; set; } = 365;

    /// <summary>The thresholds, made sane.</summary>
    public StorageThresholds Thresholds => new StorageThresholds(NoticePercent, WarningPercent).Normalized();
}
