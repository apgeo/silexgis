// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The single place that decides which rows a device may not be given. Every read and every
/// write in this slice asks here; a second implementation would be a second answer to the same
/// question, and the two would drift apart on the day one of them was corrected.
/// </summary>
/// <remarks>
/// <para>
/// This class does not decide whether a caller may see a position — that rule lives in one place
/// already, and is asked rather than restated. What is decided here is what this channel does
/// with the answer, and the answer is <em>absence</em>. Elsewhere in the API a caller who may not
/// place a protected cave is shown a grid-snapped point flagged approximate; a device is not,
/// because a snapped coordinate delivered to a map is looked at once while a snapped coordinate
/// delivered here is written to a phone in cleartext, kept for as long as the app is installed,
/// and re-shared to people this server never authenticated. And these rows come in numbers: a
/// scatter of protected places snapped into the same few grid squares outlines the cave whose
/// position the protection exists to hide.
/// </para>
/// <para>
/// The decision is per row and never per cave. A place inside a cave can be its own protection
/// root, independently of the cave containing it, so a filter that asked only about the cave
/// would hand an independently protected inner point to somebody entitled to the cave and not to
/// the point — which is the whole of what withholding was chosen to prevent.
/// </para>
/// <para>
/// Callers exclude the answer <em>before</em> the page is cut, never after. A row dropped
/// afterwards shortens the page it was on, and a page that shortens tells a device how many rows
/// it was not allowed to have.
/// </para>
/// </remarks>
public static class SyncWithhold
{
    /// <summary>
    /// Of rows already in hand, the ones to withhold. Note that an identifier with no feature
    /// row behind it is reported as withheld: this fails closed, so a caller holding identifiers
    /// from somewhere other than the database must establish existence separately rather than
    /// reading absence from this answer.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> WithheldIdsAsync(
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var exact = await protection.ExactViewIdsAsync(ctx, candidateIds, ct);
        return candidateIds.Where(id => !exact.Contains(id)).ToHashSet();
    }

    /// <summary>
    /// Of rows not yet read, the ones to withhold — for a caller that will fold the answer back
    /// into its query before ordering, paging or counting.
    /// </summary>
    /// <remarks>
    /// Only rows under a protection root can be withheld, so only those are asked about. Most
    /// selections contain none at all, and then this costs one narrow read of identifiers and
    /// nothing else. The narrowing is a cheap column and never the verdict: it says a row
    /// <em>could</em> be withheld, and only the rule above says whether it is.
    /// </remarks>
    public static async Task<IReadOnlySet<Guid>> WithheldIdsAsync(
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<IQueryable<Feature>> streams,
        CancellationToken ct)
    {
        var candidates = new List<Guid>();
        foreach (var stream in streams)
        {
            candidates.AddRange(
                await stream.Where(f => f.IsProtectedEffective).Select(f => f.Id).ToListAsync(ct));
        }

        return await WithheldIdsAsync(protection, ctx, [.. candidates.Distinct()], ct);
    }
}
