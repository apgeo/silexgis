// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Files;

public sealed class AccessHistoryOptions
{
    public const string SectionName = "AccessHistory";

    /// <summary>
    /// How long a record of somebody reading a file is kept. Zero or negative disables
    /// recording entirely — an installation that would rather not hold this at all should
    /// be able to say so with one setting, because the safest way to keep personal data is
    /// not to collect it.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Within this window, one person reading one file again does not add a row. A media
    /// player scrubbing a recording issues dozens of range requests, a document left open
    /// re-fetches its bytes as its delivery link is renewed, and a reload is not a second
    /// reading — without a window the table would measure the client's behaviour instead
    /// of anybody's.
    /// </summary>
    public TimeSpan CollapseWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often to queue a pass that deletes rows past the retention window.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether there is a sweep schedule at all — the interval alone decides, and
    /// <see cref="Retention"/> deliberately has no say. A retention of zero is the case that
    /// most needs sweeping rather than the case that cancels it: everything held is already
    /// past a window of zero length, and an installation that turned recording off did so to
    /// stop holding this, not to freeze what it had.
    /// </summary>
    public bool SweepIsScheduled => SweepInterval > TimeSpan.Zero;
}

/// <summary>
/// Writes the record of somebody having been handed a stored file's original bytes.
/// </summary>
/// <remarks>
/// Recording is best-effort on purpose: a delivery that already passed every check must
/// not fail because the history could not be written. Two deliveries racing inside the
/// collapse window can both find no recent row and both insert one — a duplicate in a log
/// is cheaper than the lock that would prevent it, and this is a log rather than a ledger.
/// </remarks>
public sealed class FileAccessRecorder(
    SilexGisDbContext db,
    IOptions<AccessHistoryOptions> options,
    ILogger<FileAccessRecorder> logger)
{
    public async Task RecordAsync(Guid fileId, Guid? userId, CancellationToken ct = default)
    {
        var settings = options.Value;

        // A delivery whose caller cannot be named is not evidence about a person, and a
        // pile of anonymous rows would make the history look more complete than it is.
        if (userId is not { } reader || settings.Retention <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var since = DateTimeOffset.UtcNow - settings.CollapseWindow;
            var alreadyRecorded = await db.FileAccessEvents.AsNoTracking()
                .AnyAsync(e => e.UserId == reader && e.FileId == fileId && e.At >= since, ct);
            if (alreadyRecorded)
            {
                return;
            }

            var documentId = await db.StoredFiles.AsNoTracking()
                .Where(f => f.Id == fileId)
                .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
                .FirstOrDefaultAsync(ct);
            if (documentId == Guid.Empty)
            {
                return; // the file lost its revision; there is nothing to file the read under
            }

            db.FileAccessEvents.Add(new FileAccessEvent
            {
                UserId = reader,
                FileId = fileId,
                DocumentId = documentId,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Could not record the reading of file {FileId}", fileId);
        }
    }
}
