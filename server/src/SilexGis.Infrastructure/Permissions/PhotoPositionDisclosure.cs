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
/// Four reads serve any number of photos: the attachments, the locating links of whatever
/// features those name, the caves of whatever trips they name, and the exact-view pass over
/// everything that turned up. Everything downstream of that is in memory, so authorising a
/// page of photos costs what authorising one does.
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
        var tripIds = links.Where(l => l.EntityType == AttachedEntityType.TripLog)
            .Select(l => l.EntityId!.Value).Distinct().ToList();

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

        // A trip names the caves it visited, so a photo on the trip is as placed as one on
        // the cave itself.
        var tripCaves = tripIds.Count == 0
            ? []
            : await db.TripLogCaves.AsNoTracking()
                .Where(x => tripIds.Contains(x.TripLogId))
                .Select(x => new { x.TripLogId, x.CaveId })
                .ToListAsync(ct);
        var tripToCaves = tripCaves.GroupBy(x => x.TripLogId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CaveId).ToList());

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
