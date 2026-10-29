// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Batch evaluation of the whole-link rule for places pinned on phones' maps. The decision lives in
/// Domain (<see cref="ResLinkRules.PlacePointShown"/>); this resolves its three facts for a set of
/// candidate links at once — which pictures the caller may read, which places, and which of those
/// places the caller may see exactly — and answers the pins to hide.
/// </summary>
/// <remarks>
/// <para>
/// Every route that emits links folds the answer into its query before it orders, pages or counts:
/// a pin cut after the count would still be counted, and the count is the badge on the panel.
/// </para>
/// <para>
/// The picture is read by the document's own rules, the place by the feature visibility filter and
/// the exact view by <see cref="FeatureProtection"/> — the same three answers the rasters stream
/// reads a pin by, so a browser reader is shown exactly the pins a phone of theirs would be given.
/// A pin missing either end is hidden.
/// </para>
/// </remarks>
public sealed class PlacePointDisclosure(SilexGisDbContext db, IAccessService access, FeatureProtection protection)
{
    /// <summary>Of the candidate links, the ids of the place pins this caller is not shown.</summary>
    public async Task<HashSet<Guid>> HiddenLinkIdsAsync(
        AccessContext ctx, IQueryable<ResLink> candidates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(candidates);
        var pins = await PlacePointQueries.PlacePoints(db, candidates)
            .AsNoTracking()
            .Select(l => new
            {
                l.Id,
                Picture = db.ResLinkMembers
                    .Where(m => m.ResLinkId == l.Id && m.IsMain && m.EntityType == AttachedEntityType.Document)
                    .Select(m => m.EntityId)
                    .FirstOrDefault(),
                Place = db.ResLinkMembers
                    .Where(m => m.ResLinkId == l.Id && m.FeatureId != null)
                    .Select(m => m.FeatureId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        if (pins.Count == 0)
        {
            return [];
        }

        var pictures = await ReadableDocumentIdsAsync(
            ctx, [.. pins.Where(p => p.Picture is not null).Select(p => p.Picture!.Value).Distinct()], ct);
        var placeIds = pins.Where(p => p.Place is not null).Select(p => p.Place!.Value).Distinct().ToList();
        var readablePlaces = placeIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => placeIds.Contains(f.Id))
                .Select(f => f.Id)
                .ToHashSetAsync(ct);
        var exact = readablePlaces.Count == 0 ? [] : await protection.ExactViewIdsAsync(ctx, readablePlaces, ct);

        return
        [
            .. pins
                .Where(p => !ResLinkRules.PlacePointShown(
                    p.Picture is { } picture && pictures.Contains(picture),
                    p.Place is { } place && readablePlaces.Contains(place),
                    p.Place is { } exactPlace && exact.Contains(exactPlace)))
                .Select(p => p.Id),
        ];
    }

    /// <summary>Whether this caller is shown one link, which may or may not be a place pin.</summary>
    public async Task<bool> IsHiddenAsync(AccessContext ctx, Guid linkId, CancellationToken ct = default) =>
        (await HiddenLinkIdsAsync(ctx, db.ResLinks.Where(l => l.Id == linkId), ct)).Count > 0;

    private async Task<HashSet<Guid>> ReadableDocumentIdsAsync(
        AccessContext ctx, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var documents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var served = (await (
                    from version in db.DocumentVersions.AsNoTracking()
                    join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                    where ids.Contains(version.DocumentId) && version.IsCurrent
                    orderby file.CreatedAt, file.Id
                    select new { version.DocumentId, File = file })
                .ToListAsync(ct))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.First().File);
        var readable = new HashSet<Guid>();
        foreach (var document in documents)
        {
            if (await DocumentAccessRules.CanReadAsync(db, access, ctx, document, served.GetValueOrDefault(document.Id), ct))
            {
                readable.Add(document.Id);
            }
        }

        return readable;
    }
}
