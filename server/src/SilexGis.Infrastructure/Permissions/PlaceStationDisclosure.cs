// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.PlaceStations;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Batch evaluation of the whole-link rule for places phones mapped to survey stations. The decision
/// lives in Domain (<see cref="ResLinkRules.PlaceStationShown"/>); this resolves its facts for a set
/// of candidate links at once — which places the caller may read and see exactly, and which surveys
/// it is shown — and answers the mappings to hide.
/// </summary>
/// <remarks>
/// <para>
/// Every route that emits links folds the answer into its query before it orders, pages or counts,
/// as it does the pins on phones' maps.
/// </para>
/// <para>
/// A survey model is shown when its cave is readable and its exact position open to the caller —
/// the rule every survey-model surface reads a model by. A survey file is shown by the document's
/// own rules. A mapping missing either end is hidden.
/// </para>
/// </remarks>
public sealed class PlaceStationDisclosure(SilexGisDbContext db, IAccessService access, FeatureProtection protection)
{
    /// <summary>Of the candidate links, the ids of the place-to-station mappings this caller is not shown.</summary>
    public async Task<HashSet<Guid>> HiddenLinkIdsAsync(
        AccessContext ctx, IQueryable<ResLink> candidates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(candidates);
        var mappings = await PlaceStationQueries.PlaceStations(db, candidates)
            .AsNoTracking()
            .Select(l => new
            {
                l.Id,
                Place = db.ResLinkMembers
                    .Where(m => m.ResLinkId == l.Id && m.IsMain && m.FeatureId != null)
                    .Select(m => m.FeatureId)
                    .FirstOrDefault(),
                Model = db.ResLinkMembers
                    .Where(m => m.ResLinkId == l.Id && !m.IsMain && m.EntityType == AttachedEntityType.SurveyModel)
                    .Select(m => m.EntityId)
                    .FirstOrDefault(),
                Document = db.ResLinkMembers
                    .Where(m => m.ResLinkId == l.Id && !m.IsMain && m.EntityType == AttachedEntityType.Document)
                    .Select(m => m.EntityId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        if (mappings.Count == 0)
        {
            return [];
        }

        var modelIds = mappings.Where(m => m.Model is not null).Select(m => m.Model!.Value).Distinct().ToList();
        var modelCaves = modelIds.Count == 0
            ? []
            : await db.SurveyModels.AsNoTracking()
                .Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.CaveFeatureId, ct);
        var documents = await PlacePointDisclosure.ReadableDocumentIdsAsync(
            db, access, ctx, [.. mappings.Where(m => m.Document is not null).Select(m => m.Document!.Value).Distinct()], ct);

        var featureIds = mappings.Where(m => m.Place is not null).Select(m => m.Place!.Value)
            .Concat(modelCaves.Values).Distinct().ToList();
        var readable = featureIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => featureIds.Contains(f.Id))
                .Select(f => f.Id)
                .ToHashSetAsync(ct);
        var exact = readable.Count == 0 ? [] : await protection.ExactViewIdsAsync(ctx, readable, ct);

        bool SurveyShown(Guid? model, Guid? document) =>
            model is { } m
                ? modelCaves.TryGetValue(m, out var cave) && readable.Contains(cave) && exact.Contains(cave)
                : document is { } d && documents.Contains(d);

        return
        [
            .. mappings
                .Where(m => !ResLinkRules.PlaceStationShown(
                    m.Place is { } place && readable.Contains(place),
                    m.Place is { } exactPlace && exact.Contains(exactPlace),
                    SurveyShown(m.Model, m.Document)))
                .Select(m => m.Id),
        ];
    }

    /// <summary>Whether this caller is shown one link, which may or may not be a mapping.</summary>
    public async Task<bool> IsHiddenAsync(AccessContext ctx, Guid linkId, CancellationToken ct = default) =>
        (await HiddenLinkIdsAsync(ctx, db.ResLinks.Where(l => l.Id == linkId), ct)).Count > 0;
}
