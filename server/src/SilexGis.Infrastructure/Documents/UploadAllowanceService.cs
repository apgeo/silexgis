// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// How much room somebody has, and what this installation accepts. One place, because the
/// answer is given twice — as advice before a transfer starts, and as the rule when the bytes
/// arrive — and the two must never disagree.
/// </summary>
public sealed class UploadAllowanceService(SilexGisDbContext db, IOptions<FilesOptions> options)
{
    /// <summary>
    /// What <paramref name="userId"/> may upload right now.
    /// </summary>
    /// <remarks>
    /// The two usage figures are aggregates rather than maintained counters, and that is a
    /// deliberate trade. A counter would be one read instead of a sum, and would be wrong the
    /// first time anything deleted a file by a route that forgot to decrement it — which,
    /// for a number that refuses uploads, means somebody locked out of their own archive with
    /// no way to see why. The sum is over an indexed join that a club-sized store answers in
    /// milliseconds, and it is asked once per upload rather than per byte.
    /// </remarks>
    public async Task<UploadAllowance> ForAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = options.Value;

        // Zero means "no limit" in configuration, so it is turned into the absent value the
        // rule understands here rather than being carried as a magic number any further.
        var installationQuota = settings.MaxTotalStoreBytes > 0 ? settings.MaxTotalStoreBytes : (long?)null;
        var defaultUserQuota = settings.DefaultUserQuotaBytes > 0 ? settings.DefaultUserQuotaBytes : (long?)null;

        // An account's own figure overrides the installation's, and zero on the account is a
        // real value — "may upload nothing" — which is why the column is nullable and this
        // reads the null rather than the number.
        var overrideQuota = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.StorageQuotaBytes)
            .FirstOrDefaultAsync(ct);

        var userQuota = overrideQuota ?? defaultUserQuota;

        // Asked only when there is a limit to measure against. On an installation with no
        // quotas at all — the default — this costs nothing, which is what keeps the common
        // case free.
        var userUsed = userQuota is null ? 0 : await UsedByAsync(userId, ct);
        var storeUsed = installationQuota is null ? 0 : await UsedInTotalAsync(ct);

        return new UploadAllowance(
            settings.MaxUploadBytes,
            userQuota,
            userUsed,
            installationQuota,
            storeUsed,
            [.. settings.AcceptedExtensions],
            [.. settings.RefusedExtensions]);
    }

    /// <summary>
    /// How many bytes of stored content this person owns.
    /// </summary>
    /// <remarks>
    /// Counted through the documents they own rather than the versions they uploaded. Those
    /// are different sets and the difference is the point: content handed over to somebody
    /// else is that person's to account for, and a caver who uploads a club survey and then
    /// transfers it should not still be paying for it. Every file under the document counts,
    /// including the copies the server itself derived, because those occupy the disk too.
    /// </remarks>
    private Task<long> UsedByAsync(Guid userId, CancellationToken ct) =>
        db.StoredFiles.AsNoTracking()
            .Where(f => db.DocumentVersions.Any(v =>
                v.Id == f.DocumentVersionId
                && db.Documents.Any(d => d.Id == v.DocumentId && d.OwnerUserId == userId)))
            .SumAsync(f => f.SizeBytes, ct);

    private Task<long> UsedInTotalAsync(CancellationToken ct) =>
        db.StoredFiles.AsNoTracking().SumAsync(f => f.SizeBytes, ct);
}
