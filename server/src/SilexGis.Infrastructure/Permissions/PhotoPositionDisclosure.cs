// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// Batch evaluation of the photo-position rule. The decision itself lives in Domain
/// (<see cref="PhotoPositionProtection.IsDisclosable"/>); this class resolves the fact the
/// rule cannot fetch for itself — which features each photo inherits protection from — and
/// asks <see cref="FeatureProtection"/> the exact-view question once for the whole batch.
/// </summary>
/// <remarks>
/// Five reads serve any number of photos: the attachments, the locating links of whatever
/// features those name, the member trips of whatever camps they name, the caves of every trip
/// that turned up either way, and the exact-view pass over the lot. Everything downstream of
/// that is in memory, so authorising a page of photos costs what authorising one does.
/// </remarks>
public sealed class PhotoPositionDisclosure(SilexGisDbContext db, FeatureProtection protection)
{
    /// <summary>
    /// Of the given files, those whose own recorded position may be disclosed to the caller.
    /// </summary>
    /// <remarks>
    /// The question is answered from what each file hangs on, without asking whether the file
    /// actually holds a position — so a text report attached to a guarded cave comes back
    /// absent, meaning only "a position here would have to be withheld". Callers pair this
    /// with the file's own position: no position, nothing to withhold, whatever this says.
    /// Doing it the other way round would cost a read of every file to answer a question most
    /// of them are not asking.
    /// </remarks>
    public async Task<HashSet<Guid>> DisclosableIdsAsync(
        AccessContext? ctx, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        var ids = fileIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var chains = await ProtectionChainsAsync(ids, ct);
        var exact = await protection.ExactViewIdsAsync(
            ctx, [.. chains.Values.SelectMany(c => c).Distinct()], ct);

        return
        [
            .. ids.Where(id => PhotoPositionProtection.IsDisclosable(
                chains.TryGetValue(id, out var chain) ? chain : [], exact)),
        ];
    }

    /// <summary>
    /// Every feature each of the given files inherits position protection from. Files that
    /// inherit from nothing are absent from the result rather than present with an empty
    /// chain — the two mean the same thing to the rule, and leaving them out keeps the
    /// dictionary the size of the answer instead of the size of the question.
    /// </summary>
    public async Task<Dictionary<Guid, List<Guid>>> ProtectionChainsAsync(
        IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        var ids = fileIds.Distinct().ToList();
        var chains = new Dictionary<Guid, List<Guid>>();
        if (ids.Count == 0)
        {
            return chains;
        }

        var links = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.FileId))
            .Select(a => new { a.FileId, a.FeatureId, a.EntityType, a.EntityId })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return chains;
        }

        var attachedFeatureIds = links.Where(l => l.FeatureId != null)
            .Select(l => l.FeatureId!.Value).Distinct().ToList();
        var campIds = links.Where(l => l.EntityType == AttachedEntityType.Expedition)
            .Select(l => l.EntityId!.Value).Distinct().ToList();

        // A camp gathers a named set of trips, and a photograph filed under the camp rather than
        // under one of them is placed by exactly the same journeys — filing it one level up is a
        // matter of where the uploader put it, not of where the camera was. So the camp's member
        // trips join the chain of anything attached to the camp, and the places those trips name
        // protect it just as they would if it hung on the trip itself. Fail-closed: every member
        // trip counts, because nothing about the photograph says which of them it came from.
        var campTrips = campIds.Count == 0
            ? []
            : await db.ExpeditionTrips.AsNoTracking()
                .Where(x => campIds.Contains(x.ExpeditionId))
                .Select(x => new { x.ExpeditionId, x.TripLogId })
                .ToListAsync(ct);
        var campToTrips = campTrips.GroupBy(x => x.ExpeditionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TripLogId).ToList());

        var tripIds = links.Where(l => l.EntityType == AttachedEntityType.TripLog)
            .Select(l => l.EntityId!.Value)
            .Concat(campTrips.Select(x => x.TripLogId))
            .Distinct().ToList();

        // A link whose kind places its endpoints discloses the other end's position by
        // proximity, in either direction, so both ends join the chain — fail-closed.
        var locatingLinks = attachedFeatureIds.Count == 0
            ? []
            : await db.FeatureLinks.AsNoTracking()
                .Where(l => (attachedFeatureIds.Contains(l.FromId) || attachedFeatureIds.Contains(l.ToId))
                    && db.LinkKinds.Any(k => k.Id == l.LinkKindId && k.Locating))
                .Select(l => new { l.FromId, l.ToId })
                .ToListAsync(ct);
        var partners = new Dictionary<Guid, List<Guid>>();
        foreach (var link in locatingLinks)
        {
            AddPartner(link.FromId, link.ToId);
            AddPartner(link.ToId, link.FromId);
        }

        // A trip names where it went, so a photo on the trip is as placed as one on those
        // places themselves. Every role counts: which of them the trip carried out there says
        // nothing about whether the photograph is placed by it, and a chain narrowed to one
        // role would hand over the bytes for every place named under any other — silently,
        // since nothing about a photograph says which trip role put it where it is. Every kind
        // of feature counts too, for the same reason: a guarded shaft places a picture exactly
        // as a guarded cave does.
        var tripFeatures = await TripRoleLinks.PairsForAsync(db, tripIds, kind: null, ct);
        var tripToCaves = tripFeatures.GroupBy(x => x.TripId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FeatureId).ToList());

        foreach (var link in links)
        {
            if (link.FeatureId is { } featureId)
            {
                Chain(link.FileId).Add(featureId);
                if (partners.TryGetValue(featureId, out var reached))
                {
                    Chain(link.FileId).AddRange(reached);
                }
            }
            else if (link.EntityType == AttachedEntityType.TripLog
                && tripToCaves.TryGetValue(link.EntityId!.Value, out var caveIds))
            {
                Chain(link.FileId).AddRange(caveIds);
            }
            else if (link.EntityType == AttachedEntityType.Expedition
                && campToTrips.TryGetValue(link.EntityId!.Value, out var memberTripIds))
            {
                foreach (var tripId in memberTripIds)
                {
                    if (tripToCaves.TryGetValue(tripId, out var campCaveIds))
                    {
                        Chain(link.FileId).AddRange(campCaveIds);
                    }
                }
            }
        }

        return chains;

        List<Guid> Chain(Guid fileId)
        {
            if (!chains.TryGetValue(fileId, out var chain))
            {
                chains[fileId] = chain = [];
            }

            return chain;
        }

        void AddPartner(Guid featureId, Guid partnerId)
        {
            if (!partners.TryGetValue(featureId, out var list))
            {
                partners[featureId] = list = [];
            }

            list.Add(partnerId);
        }
    }
}
