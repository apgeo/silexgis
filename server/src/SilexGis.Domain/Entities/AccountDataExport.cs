// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Lifecycle of one account-data export. Stored as smallint — do not renumber.</summary>
public enum AccountExportStatus : short
{
    Queued = 0,
    Running = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>
/// A user's request for a copy of their own account data, and the archive it produced.
/// </summary>
/// <remarks>
/// <para>
/// The processing queue deliberately carries no result column, so the requesting entity holds the
/// outcome — the same arrangement uploaded geodata uses.
/// </para>
/// <para>
/// The archive is kept as a plain stored blob referenced by <see cref="StoragePath"/> rather than
/// as a file record. File content is served against a short-lived capability token in the URL,
/// which is checked without re-testing who may read the file; an archive of one person's personal
/// data must only ever be downloadable by that person's own authenticated request, so it stays out
/// of that path and out of file listings entirely.
/// </para>
/// </remarks>
public class AccountDataExport : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public AccountExportStatus Status { get; set; } = AccountExportStatus.Queued;

    /// <summary>Store-relative path of the archive, once built.</summary>
    public string? StoragePath { get; set; }

    public long? SizeBytes { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// When the archive stops being downloadable and may be deleted. An export is unencrypted
    /// personal data sitting in the file store, so it does not live indefinitely.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
