// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Files;
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
public sealed class PhotoPositionDisclosure(
    SilexGisDbContext db, FeatureProtection protection, IAppSettingsService settings)
{
    /// <summary>
    /// Everything a mint site needs to decide which bytes each of the given files may be
    /// handed out as, and whether its recorded position may be printed: the disclosure answer
    /// for the batch and the installation's switch for location inside photo files.
    /// </summary>
    public async Task<PhotoReach> ReachAsync(
        AccessContext? ctx, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default) =>
        new(await DisclosableIdsAsync(ctx, fileIds, ct), await KeepLocationInFilesAsync(ct));

    /// <summary>
    /// The same answer for the person holding a file they have just sent. Nothing can hang on a
    /// file created this instant, so its position places nothing yet; the switch still applies,
    /// because it is about what any delivery carries rather than about who asks.
    /// </summary>
    public async Task<PhotoReach> ForUploaderAsync(Guid fileId, CancellationToken ct = default) =>
        new(new HashSet<Guid> { fileId }, await KeepLocationInFilesAsync(ct));

    private async Task<bool> KeepLocationInFilesAsync(CancellationToken ct) =>
        (await settings.GetProtectionAsync(ct)).KeepLocationInPhotoFiles;

    /// <summary>
    /// Of the given files, those whose own recorded position may be disclosed to the caller.
    /// </summary>
    /// <remarks>
    /// The question is answered from what each file hangs on, without asking whether the file
    /// actually holds a position — so a text report attached to a guarded cave comes back
    /// absent, meaning only "a position here would have to be withheld". Callers pair this
    /// with whether the file can hold a position at all (its own recorded one, or a capture
    /// location its kind can carry inside its bytes): one that cannot has nothing to withhold,
    /// whatever this says.
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
        //
        // A deleted member trip counts too, which is why this reads past the filter that hides
        // its place in the camp. Deleting a trip is something a person can take back, and the
        // camera was where it was either way: read through the filter, a picture filed under the
        // camp would hand over its position for as long as the one trip that guarded it happened
        // to be deleted, and withhold it again on the day the trip was put back. A picture hung on
        // the trip itself is already held this way, because the places a trip names are read
        // from its links, which nothing hides.
        var campTrips = campIds.Count == 0
            ? []
            : await db.ExpeditionTrips.AsNoTracking().IgnoreQueryFilters()
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

/// <summary>
/// A resolved answer about a batch of files: whose recorded position this caller may be given,
/// and whether the installation lets location data travel inside photo files. The one place a
/// delivery's reach is worked out from those two, so every listing, detail and map layer that
/// mints a delivery address answers alike.
/// </summary>
public sealed class PhotoReach(IReadOnlySet<Guid> disclosable, bool keepLocationInFiles)
{
    /// <summary>The installation's switch, as it stood when this answer was resolved.</summary>
    public bool KeepLocationInFiles { get; } = keepLocationInFiles;

    /// <summary>
    /// Whether the file's recorded position may be shown to this caller — the answer the
    /// position printed beside a photo is gated on. Not the switch's business: the switch is
    /// about what is inside the file, not about the record.
    /// </summary>
    public bool PositionDisclosable(Guid fileId) => disclosable.Contains(fileId);

    /// <summary>Which bytes the file itself may be handed out as.</summary>
    public OriginalReach OriginalOf(StoredFile file) => OriginalOf(file, PositionDisclosable(file.Id));

    /// <summary>
    /// Which bytes the file may be handed out as, with the disclosure answer supplied — for a
    /// superseded revision, which hangs on nothing and so must borrow the answer given for the
    /// current one rather than be asked about directly.
    /// </summary>
    public OriginalReach OriginalOf(StoredFile file, bool positionDisclosable) =>
        OriginalOf(file.Geom is not null, file.Kind, file.MimeType, positionDisclosable);

    /// <summary>
    /// The same answer from the facts it is made of, for a query that projected them rather
    /// than loading the file.
    /// </summary>
    public OriginalReach OriginalOf(bool recordsPosition, FileKind kind, string mimeType, bool positionDisclosable) =>
        PhotoPositionProtection.OriginalReachOf(
            PhotoPositionProtection.MayCarryLocation(kind, recordsPosition),
            positionDisclosable,
            KeepLocationInFiles,
            locationRemovable: LocationFreeCopyService.CanRemoveLocation(kind, mimeType));
}
