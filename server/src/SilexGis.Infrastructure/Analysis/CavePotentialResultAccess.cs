// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// The one place that decides which cave-potential results an account may read.
/// </summary>
/// <remarks>
/// <para>
/// A result is a map, and a map weighed from recorded positions shows where those positions are:
/// the ground nearest a sinkhole scores highest, the ground densest in entrances stands out. So a
/// result is read under the access of the reader, asked afresh at every reading, and never under
/// anything remembered from when it was made. An account may read one when all of these hold:
/// </para>
/// <list type="number">
/// <item>it may read the terrain;</item>
/// <item>it may read and place exactly the area the result was run over, whose outline the map draws;</item>
/// <item>the run is finished — an unfinished or failed one is shown only to the account it was
/// asked by and to full administrators, since nobody else has any use for it;</item>
/// <item>it may, today, read and place exactly <b>every</b> recorded feature the run was computed
/// from. The account the run was made for is asked the same question as everybody else, so a
/// member who has since lost sight of a protected cave loses the maps made from it too.</item>
/// </list>
/// <para>
/// A reader who passes could have made the same map from what they can already place, so reading
/// it tells them nothing about where anything is. A result made from the ground alone names no
/// feature, and rule four is then empty.
/// </para>
/// <para>
/// A feature that has since been deleted can be placed by nobody but a full administrator, so a
/// result made from one is read by nobody else: the stricter answer, kept until the result is
/// deleted and run again.
/// </para>
/// </remarks>
public sealed class CavePotentialResultAccess(SilexGisDbContext db, FeatureProtection protection)
{
    /// <summary>Whether this account may read cave-potential results at all.</summary>
    public static bool MayReadAny(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed;

    /// <summary>Of these results, the ones the account may read, in the order given.</summary>
    public async Task<List<CavePotentialResult>> ReadableAsync(
        AccessContext ctx, IReadOnlyList<CavePotentialResult> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || !MayReadAny(ctx))
        {
            return [];
        }

        var areas = new Dictionary<Guid, bool>();
        var readable = new List<CavePotentialResult>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Status != CavePotentialResultStatus.Ready
                && !ctx.IsFullAdmin && row.RequestedByUserId != ctx.UserId)
            {
                continue;
            }

            if (!areas.TryGetValue(row.AreaFeatureId, out var mayPlaceArea))
            {
                mayPlaceArea = await protection.MayScopeByAsync(ctx, row.AreaFeatureId, ct);
                areas[row.AreaFeatureId] = mayPlaceArea;
            }

            if (mayPlaceArea && await PlacesEveryAsync(ctx, row.EvidenceFeatureIds, ct))
            {
                readable.Add(row);
            }
        }

        return readable;
    }

    /// <summary>The result of this id when the account may read it, or null.</summary>
    /// <remarks>
    /// Null for a result that does not exist and for one the account may not read alike, so a
    /// caller answering "not found" for null says the same thing in both cases.
    /// </remarks>
    public async Task<CavePotentialResult?> ReadAsync(AccessContext ctx, Guid id, CancellationToken ct = default)
    {
        var row = await db.CavePotentialResults.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : (await ReadableAsync(ctx, [row], ct)).FirstOrDefault();
    }

    private async Task<bool> PlacesEveryAsync(AccessContext ctx, Guid[] featureIds, CancellationToken ct)
    {
        if (featureIds.Length == 0)
        {
            return true;
        }

        // Read first, then place: an id that names no readable row — withheld, deleted or gone —
        // is one the account cannot place, and the walk below is never asked about it.
        var ids = featureIds.ToList();
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (readable.Count != featureIds.Length)
        {
            return false;
        }

        return (await protection.ExactViewIdsAsync(ctx, readable, ct)).Count == featureIds.Length;
    }
}
