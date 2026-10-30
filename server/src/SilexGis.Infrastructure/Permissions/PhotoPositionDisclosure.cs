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
/// A fixed handful of reads serve any number of photos: the attachments, the locating links of
/// whatever features those name, the member trips of whatever camps they name, the caves of
/// every trip that turned up either way, the guarded features when any photo is filed under an
/// object that names no place, one round per level of files filed under files, and the
/// exact-view pass over the lot. Everything downstream of that is in
/// memory, so authorising a page of photos costs what authorising one does.
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
        new(await DisclosableIdsAsync(ctx, fileIds, ct), await settings.GetProtectionAsync(ct));

    /// <summary>
    /// The same answer for the person holding a file they have just sent. Nothing can hang on a
    /// file created this instant, so its position places nothing yet; the switch still applies,
    /// because it is about what any delivery carries rather than about who asks.
    /// </summary>
    public async Task<PhotoReach> ForUploaderAsync(Guid fileId, CancellationToken ct = default) =>
        new(new HashSet<Guid> { fileId }, await settings.GetProtectionAsync(ct));

    /// <summary>
    /// What places a photograph filed under a non-feature object of the given kind — the one
    /// table this rule keeps of the attachment vocabulary. A kind it does not name is placed by
    /// every guarded feature, so a kind appended later is withheld rather than disclosed until
    /// somebody decides otherwise.
    /// </summary>
    public static PhotoChainSource ChainSourceOf(AttachedEntityType type) =>
        TryChainSourceOf(type, out var source) ? source : PhotoChainSource.EveryGuardedFeature;

    /// <summary>
    /// The explicit answer for a kind, or false for one nobody has placed yet. Kept apart from
    /// <see cref="ChainSourceOf"/> so a structural test can insist every kind is named here
    /// while a kind that slips through still fails closed.
    /// </summary>
    /// <remarks>
    /// Kinds with no position of their own and no cave they name — a club, a calendar event, a
    /// geodata file, a scanned sheet, a saved map view — say nothing about where the camera
    /// stood, so they are placed by every guarded feature. So are the kinds no attachment
    /// surface accepts today (a document, a survey model, a roster caver, a cabinet, an album,
    /// a checklist, the reserved comment): should one start accepting them, the photographs
    /// filed there are withheld until somebody gives the kind an arm of its own.
    /// </remarks>
    public static bool TryChainSourceOf(AttachedEntityType type, out PhotoChainSource source)
    {
        source = type switch
        {
            AttachedEntityType.TripLog => PhotoChainSource.Trip,
            AttachedEntityType.Expedition => PhotoChainSource.Camp,
            AttachedEntityType.TripRecording => PhotoChainSource.Recording,
            AttachedEntityType.TripRecordingPoint => PhotoChainSource.Scan,
            AttachedEntityType.StoredFile => PhotoChainSource.ParentFile,
            AttachedEntityType.CavingGroup or AttachedEntityType.Event or AttachedEntityType.Geofile
                or AttachedEntityType.GeoreferencedMap or AttachedEntityType.MapView
                or AttachedEntityType.Document or AttachedEntityType.SurveyModel
                or AttachedEntityType.Caver or AttachedEntityType.Cabinet or AttachedEntityType.Album
                or AttachedEntityType.Checklist or AttachedEntityType.Comment
                => PhotoChainSource.EveryGuardedFeature,
            _ => (PhotoChainSource)(-1),
        };
        return Enum.IsDefined(source);
    }

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

        // A file can be filed under another file, and that one under a third: a photograph
        // attached to a survey report is placed by whatever places the report. So the files
        // asked about are widened to every file they are filed under, in whole rounds, before
        // anything is resolved; each file enters once, which is also what lets two files filed
        // under each other settle instead of looping.
        var closure = new HashSet<Guid>(ids);
        var links = new List<ChainLink>();
        var frontier = ids;
        while (frontier.Count > 0)
        {
            var round = await db.Attachments.AsNoTracking()
                .Where(a => frontier.Contains(a.FileId))
                .Select(a => new ChainLink(a.FileId, a.FeatureId, a.EntityType, a.EntityId))
                .ToListAsync(ct);
            links.AddRange(round);
            frontier = [.. round
                .Where(l => l.EntityType == AttachedEntityType.StoredFile && closure.Add(l.EntityId!.Value))
                .Select(l => l.EntityId!.Value)];
        }

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

        // A recording is made in one cave, and a scan names a place in it: a photo on either is
        // placed by those features, so they join its chain. A removed recording or scan counts
        // too, for the reason a removed member trip does — the camera was where it was, and a
        // deletion somebody can take back must not hand over the position meanwhile. Fail-closed:
        // a scan that names no place is still placed by its recording's cave.
        var recordingIds = links.Where(l => l.EntityType == AttachedEntityType.TripRecording)
            .Select(l => l.EntityId!.Value).Distinct().ToList();
        var scanIds = links.Where(l => l.EntityType == AttachedEntityType.TripRecordingPoint)
            .Select(l => l.EntityId!.Value).Distinct().ToList();
        var scans = scanIds.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                .Where(p => scanIds.Contains(p.Id))
                .Select(p => new { p.Id, p.RecordingId, p.PlaceFeatureId })
                .ToListAsync(ct);
        var allRecordingIds = recordingIds.Concat(scans.Select(p => p.RecordingId)).Distinct().ToList();
        var recordingCaves = allRecordingIds.Count == 0
            ? []
            : await db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
                .Where(r => allRecordingIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.CaveFeatureId, ct);
        var scanFeatures = scans.ToDictionary(
            p => p.Id,
            p => new[] { recordingCaves.GetValueOrDefault(p.RecordingId), p.PlaceFeatureId ?? Guid.Empty }
                .Where(id => id != Guid.Empty).ToList());

        // A caving group has no position and names no place: nothing about a photograph filed
        // under one says which cave the camera stood at, and a club's members photograph caves
        // the club does not own as readily as ones it does. So the chain is every guarded
        // feature in the installation — the position reaches only a caller who may place every
        // one of them, and for anyone else the picture comes without it. Fail-closed, for the
        // reason every member trip of a camp counts: anything narrower (the club's own caves,
        // the caves its trips name) would hand over the position of a guarded cave whenever the
        // photograph happened to be taken somewhere the narrowing did not reach. Deleted rows
        // count, as everywhere in this rule, because a deletion can be taken back. Read once,
        // and only when a photo in the batch is filed this way.
        //
        // Every other object a photograph can be filed under that has no arm of its own — a
        // calendar event, a geodata file, a scanned sheet, a saved map view, and any kind added
        // later — is answered the same way, for the same reason: it says nothing about where the
        // camera stood, and a chain left empty would read as "nothing guards this position".
        var guardedFeatureIds = !links.Any(l => l.EntityType is { } type
                && ChainSourceOf(type) == PhotoChainSource.EveryGuardedFeature)
            ? []
            : await db.Features.AsNoTracking().IgnoreQueryFilters()
                .Where(f => f.LocationProtected || f.IsProtectedEffective)
                .Select(f => f.Id)
                .ToListAsync(ct);

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

        // What each file is placed by through its own links; a file filed under another file is
        // noted here and given that file's chain afterwards.
        var own = new Dictionary<Guid, List<Guid>>();
        var parents = new Dictionary<Guid, List<Guid>>();
        foreach (var link in links)
        {
            if (link.FeatureId is { } featureId)
            {
                Own(link.FileId).Add(featureId);
                if (partners.TryGetValue(featureId, out var reached))
                {
                    Own(link.FileId).AddRange(reached);
                }

                continue;
            }

            var entityId = link.EntityId!.Value;
            switch (ChainSourceOf(link.EntityType!.Value))
            {
                case PhotoChainSource.Trip:
                    if (tripToCaves.TryGetValue(entityId, out var caveIds))
                    {
                        Own(link.FileId).AddRange(caveIds);
                    }

                    break;
                case PhotoChainSource.Recording:
                    if (recordingCaves.TryGetValue(entityId, out var recordingCave))
                    {
                        Own(link.FileId).Add(recordingCave);
                    }

                    break;
                case PhotoChainSource.Scan:
                    if (scanFeatures.TryGetValue(entityId, out var scanChain))
                    {
                        Own(link.FileId).AddRange(scanChain);
                    }

                    break;
                case PhotoChainSource.Camp:
                    if (campToTrips.TryGetValue(entityId, out var memberTripIds))
                    {
                        foreach (var tripId in memberTripIds)
                        {
                            if (tripToCaves.TryGetValue(tripId, out var campCaveIds))
                            {
                                Own(link.FileId).AddRange(campCaveIds);
                            }
                        }
                    }

                    break;
                case PhotoChainSource.ParentFile:
                    if (!parents.TryGetValue(link.FileId, out var list))
                    {
                        parents[link.FileId] = list = [];
                    }

                    list.Add(entityId);
                    break;
                default:
                    Own(link.FileId).AddRange(guardedFeatureIds);
                    break;
            }
        }

        // Each file asked about carries its own chain and that of every file above it.
        foreach (var id in ids)
        {
            var seen = new HashSet<Guid> { id };
            var pending = new Stack<Guid>([id]);
            var chain = new List<Guid>();
            while (pending.TryPop(out var current))
            {
                if (own.TryGetValue(current, out var direct))
                {
                    chain.AddRange(direct);
                }

                foreach (var parent in parents.GetValueOrDefault(current) ?? [])
                {
                    if (seen.Add(parent))
                    {
                        pending.Push(parent);
                    }
                }
            }

            if (chain.Count > 0)
            {
                chains[id] = chain;
            }
        }

        return chains;

        List<Guid> Own(Guid fileId)
        {
            if (!own.TryGetValue(fileId, out var chain))
            {
                own[fileId] = chain = [];
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

/// <summary>What places a photograph filed under a non-feature object of a given kind.</summary>
public enum PhotoChainSource
{
    /// <summary>Every place the trip names, whatever its role.</summary>
    Trip,

    /// <summary>Every place any member trip of the camp names, deleted members included.</summary>
    Camp,

    /// <summary>The cave the recording was made in.</summary>
    Recording,

    /// <summary>The scan's place and its recording's cave.</summary>
    Scan,

    /// <summary>Whatever places the file it is filed under, all the way up.</summary>
    ParentFile,

    /// <summary>Every guarded feature of the installation: the object says nothing of a place.</summary>
    EveryGuardedFeature,
}

internal sealed record ChainLink(Guid FileId, Guid? FeatureId, AttachedEntityType? EntityType, Guid? EntityId);

/// <summary>
/// A resolved answer about a batch of files: whose recorded position this caller may be given,
/// and what the installation lets travel inside photo files. The one place a delivery's reach is
/// worked out from those, so every listing, detail and map layer that mints a delivery address
/// answers alike.
/// </summary>
public sealed class PhotoReach(IReadOnlySet<Guid> disclosable, ProtectionSettings protection)
{
    /// <summary>The installation's location switch, as it stood when this answer was resolved.</summary>
    public bool KeepLocationInFiles { get; } = protection.KeepLocationInPhotoFiles;

    /// <summary>
    /// Whether a reader who may be told the location still gets a file no location-free copy can
    /// be made of while the location switch is off, as it stood when this answer was resolved.
    /// </summary>
    public bool ServeUnstrippableToLocationReaders { get; } = protection.ServeUnstrippableOriginalsToLocationReaders;

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
            locationRemovable: LocationFreeCopyService.CanRemoveLocation(kind, mimeType),
            ServeUnstrippableToLocationReaders);
}
