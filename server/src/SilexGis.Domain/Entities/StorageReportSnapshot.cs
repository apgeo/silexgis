// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One day's storage report, kept so the next one can say how fast the store grows.
/// </summary>
/// <remarks>
/// The few figures growth is computed from are columns; everything else — the breakdowns, the
/// largest files, what could be deleted — is one JSON document, read back whole and never
/// queried into, so its shape can grow without a migration each time.
/// </remarks>
public class StorageReportSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>When it was measured.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>What the file store held, with the room reserved by transfers still arriving.</summary>
    public long StoreUsedBytes { get; set; }

    /// <summary>What point clouds held.</summary>
    public long ScanUsedBytes { get; set; }

    /// <summary>Free space on the file store's volume, when it could be read.</summary>
    public long? StoreFreeBytes { get; set; }

    /// <summary>The whole report, as JSON.</summary>
    public required string Report { get; set; }
}
