// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// How long the bytes a held line would adopt are kept: for as long as the line waits, and for the
/// installation's retention after it is decided without them being adopted.
/// </summary>
/// <remarks>
/// <para>
/// A phone sends a photo's bytes ahead of the row that names them, as a staged upload with an
/// expiry of its own; the hourly staged-upload sweep deletes whatever no upload adopted by then.
/// A held row adopts nothing until a reviewer accepts it, which can be weeks later, so its staged
/// upload is parked — its expiry pushed beyond any date the sweep reaches — and the same sweep is
/// what deletes it once the line is decided and the retention has passed. One sweep, one expiry
/// column, and the adoption check that already refuses an expired staged upload: nothing else had
/// to learn about review.
/// </para>
/// <para>
/// Accepting a line adopts its bytes, which removes the staged upload; whatever a decided line
/// left behind — turned down, replaced by a later upload, failed, or accepted without the bytes
/// being needed — is released. Bytes a line still waiting names are never released, whichever
/// line released them: a later upload that supersedes a held photo may name the same bytes.
/// </para>
/// </remarks>
public static class HeldUploadBytes
{
    /// <summary>The expiry of a staged upload a waiting line holds: beyond anything the sweep reaches.</summary>
    public static readonly DateTimeOffset Parked = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Parks the staged uploads of <paramref name="uploaderId"/> that held lines now name.</summary>
    public static Task<int> ParkAsync(
        SilexGisDbContext db, Guid uploaderId, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return db.UploadSessions
            .Where(s => sessionIds.Contains(s.Id) && s.UserId == uploaderId && s.StagedForSync)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ExpiresAt, Parked), ct);
    }

    /// <summary>
    /// Gives the staged uploads the decided lines <paramref name="itemIds"/> held back to the sweep,
    /// to go <paramref name="retentionDays"/> after <paramref name="now"/> — unless a line still
    /// waiting names them.
    /// </summary>
    public static async Task<int> ReleaseAsync(
        SilexGisDbContext db, IReadOnlyCollection<long> itemIds, int retentionDays, DateTimeOffset now, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return 0;
        }

        var released = await db.ImportBatchItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)
                && i.HeldUploadSessionId != null
                && i.ReviewDecision != null
                && i.ReviewDecision != ImportReviewDecision.Pending)
            .Select(i => i.HeldUploadSessionId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (released.Count == 0)
        {
            return 0;
        }

        var stillWaiting = db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ReviewDecision == ImportReviewDecision.Pending && i.HeldUploadSessionId != null)
            .Select(i => i.HeldUploadSessionId!.Value);
        var until = now.AddDays(retentionDays);

        // Only ever brought forward: a staged upload whose own expiry is sooner keeps it.
        return await db.UploadSessions
            .Where(s => released.Contains(s.Id) && !stillWaiting.Contains(s.Id) && s.ExpiresAt > until)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ExpiresAt, until), ct);
    }
}
