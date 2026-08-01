// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Builds the target context a feature create is decided against. Shared by the slices
/// that create features so the shape of that context — and the readability rule on the
/// prospective parent — has one home.
/// </summary>
public static class CreateContext
{
    /// <summary>
    /// Target facts for creating a feature under an optional parent. Returns null when a
    /// parent was named but the caller cannot read it: a body reference must never
    /// confirm the existence of rows the caller cannot see, so the slice answers
    /// "no such parent" rather than "forbidden".
    /// </summary>
    public static async Task<AccessTargetFacts?> ParentCreateFactsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Guid? parentId,
        Guid? cavingGroupId,
        FeatureKind kind,
        CancellationToken ct,
        long? featureTypeId = null)
    {
        if (parentId is null)
        {
            return AccessTargetFacts.ForCreate(null, [], cavingGroupId, kind, featureTypeId);
        }

        var parent = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Id == parentId)
            .Select(f => new { f.OwnerUserId, f.AncestorIds })
            .FirstOrDefaultAsync(ct);

        return parent is null
            ? null
            : AccessTargetFacts.ForCreate(
                parent.OwnerUserId, parent.AncestorIds, cavingGroupId, kind, featureTypeId);
    }
}
