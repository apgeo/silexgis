// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.PlaceStations;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Features.ResLinks;

public static partial class ResLinkEndpoints
{
    /// <param name="skipMembershipCut">
    /// True only for the create echo: like the member-write acknowledgments, it answers
    /// the caller with memberships that caller asserted in the same request, so the
    /// disclosure cut every other link projection applies is deliberately not re-asked.
    /// </param>
    internal static async Task<List<ResLinkDto>> ProjectAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLink> links,
        CancellationToken ct,
        bool skipMembershipCut = false,
        bool skipCuration = false)
    {
        if (links.Count == 0)
        {
            return [];
        }

        var linkIds = links.Select(l => l.Id).ToList();
        // A membership naming a deleted trip is kept, so that restoring the trip restores it,
        // and is not shown: the trip answers everywhere as one that does not exist, and a bare
        // row saying "something here is withheld" would be this panel saying that it does.
        var members = await db.ResLinkMembers.AsNoTracking()
            .Where(TripLinkFate.Not(TripLinkFate.OfNoLiveTrip(db)))
            .Where(m => linkIds.Contains(m.ResLinkId))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        // Taken before the disclosure cut below, which removes members from the answer but
        // not from the link: curation asks who may write the link's subject, and a caller
        // who holds that write is not told less about their own rights because the marker
        // sits on a member whose association is withheld from them. It is also the same
        // marker the write paths will read.
        var mainMembers = members.Where(m => m.IsMain).ToList();

        // Displays are resolved before the disclosure cut: which members this caller can
        // read is also an input to it — only a readable sibling can show them coordinates.
        var displays = await ResolveDisplaysAsync(targets, ctx, members, ct);

        // A member naming a feature is an association that can place the feature, so the
        // rule governing document attachments decides whether this caller is told of it
        // at all. A withheld membership leaves the response entirely — no bare row: for
        // an unreadable target the row admits only that something restricted exists, but
        // this row would name the feature, which is the very fact being kept. Each
        // member is judged with its siblings: one of them showing this caller exact
        // coordinates puts the feature's name beside a position, which is the pairing
        // the reveal setting never opens.
        if (!skipMembershipCut)
        {
            var exposing = await ExposingMemberIdsAsync(
                db, protection, photoPositions, access, ctx, members, displays, ct);
            var exposingByLink = members
                .Where(m => exposing.Contains(m.Id))
                .GroupBy(m => m.ResLinkId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());
            var withheld = await associations.WithheldIdsAsync(
                ctx,
                [.. members.Select(m => new AssociationCandidate(
                    m.Id,
                    ResLinkRules.MemberAssociation(
                        m.FeatureId,
                        exposingByLink.TryGetValue(m.ResLinkId, out var siblings)
                            && siblings.Any(s => s != m.Id))))],
                ct);
            if (withheld.Count > 0)
            {
                members = [.. members.Where(m => !withheld.Contains(m.Id))];
            }
        }

        var relationIds = links
            .Where(l => l.RelationTypeId is not null)
            .Select(l => l.RelationTypeId!.Value)
            .Distinct()
            .ToList();
        var relations = relationIds.Count == 0
            ? new Dictionary<long, ResLinkRelationType>()
            : await db.ResLinkRelationTypes.AsNoTracking()
                .Where(r => relationIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var curated = skipCuration
            ? []
            : await CuratedLinkIdsAsync(db, access, targets, ctx, links, mainMembers, ct);

        var memberDtos = await AssembleMembersAsync(db, access, ctx, members, displays, ct);
        var byLink = members
            .GroupBy(m => m.ResLinkId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResLinkMemberDto>)[.. g.Select(m => memberDtos[m.Id])]);

        return [.. links.Select(l => new ResLinkDto(
            l.Id,
            l.ShortCode,
            l.RelationTypeId is { } rid && relations.TryGetValue(rid, out var relation)
                ? ResLinkRelationTypeEndpoints.ToDto(relation)
                : null,
            l.Description,
            l.CreatedBy,
            l.CreatedAt,
            l.UpdatedAt,
            curated.Contains(l.Id),
            byLink.GetValueOrDefault(l.Id) ?? []))];
    }

    /// <summary>
    /// Which of these links the caller may curate — the one home of the rule, answered for
    /// a whole page at once: a link is curated by its creator, by a full administrator, or
    /// by whoever may write the link's main member. The write paths ask it too, for a set
    /// of one, so that there is no second expression of it to drift.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third arm exists because a directed link is an assertion <em>about</em> its main
    /// member — what this trip surveyed, what this document describes — and authorship of
    /// the sentence is a poor proxy for stewardship of the subject. Without it, whoever
    /// typed a link first owns it forever: a second person who may edit the very thing the
    /// link is about cannot correct what was recorded, only add a rival link beside it. The
    /// rule is asked through each target world's own write decision rather than by
    /// comparing ids, so a world that decides writing in an unusual way — a document
    /// through its served file, a shelf through the documents filed at it, a survey model
    /// through its cave — answers here the way it answers everywhere else.
    /// </para>
    /// <para>
    /// It is a widening and never a narrowing: everyone who could edit a link before can
    /// still edit it. A link with <em>no</em> main member is untouched — an undirected link
    /// has no subject to inherit stewardship from, so the creator rule stands alone there,
    /// and a link whose creator account is gone and which names no main member answers to
    /// administrators only.
    /// </para>
    /// <para>
    /// The two arms that need no storage go first, because they answer most of a page for
    /// nothing: an administrator curates everything, and an author curates what they wrote.
    /// Only what is left asks the third arm — write on the link's main member — and it is
    /// asked once per target world over the distinct ids of that world, never once per link.
    /// The main markers are read from the members the projection has already loaded, so
    /// finding them costs nothing at all; the per-link rule would instead have cost a query
    /// for the marker plus a decision walk for every row on the page, on a listing whose
    /// whole cost contract is that it stays flat as the page grows.
    /// </para>
    /// <para>
    /// The answer is a snapshot for display. Writers re-decide under the link's row lock,
    /// because the marker this reads can be moved by a concurrent promotion — so a stale
    /// "yes" here costs a refusal at the write, not a write that should have been refused.
    /// </para>
    /// </remarks>
    private static async Task<HashSet<Guid>> CuratedLinkIdsAsync(
        SilexGisDbContext db,
        IAccessService access,
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        IReadOnlyList<ResLink> links,
        IReadOnlyList<ResLinkMember> mainMembers,
        CancellationToken ct)
    {
        if (ctx.IsFullAdmin)
        {
            return [.. links.Select(l => l.Id)];
        }

        // A place pinned on a phone's map is curated by whoever may change the map's cave — the
        // right a phone needs to move or remove it — and by nobody else: neither its creator nor a
        // writer of the picture. So the button matches the route.
        var placeRelation = await PlacePointQueries.RelationIdAsync(db, ct);
        var pins = links.Where(l => placeRelation is not null && l.RelationTypeId == placeRelation).Select(l => l.Id).ToHashSet();
        var curated = await PlacePointEndpoints.WritableAsync(db, access, ctx, pins, ct);

        // A place's station, likewise, by whoever may change the place.
        var stationRelation = await PlaceStationQueries.RelationIdAsync(db, ct);
        var stations = links.Where(l => stationRelation is not null && l.RelationTypeId == stationRelation).Select(l => l.Id).ToHashSet();
        curated.UnionWith(await PlaceStationEndpoints.WritableAsync(db, access, ctx, stations, ct));
        pins.UnionWith(stations);
        links = [.. links.Where(l => !pins.Contains(l.Id))];
        mainMembers = [.. mainMembers.Where(m => !pins.Contains(m.ResLinkId))];

        curated.UnionWith(
            links.Where(l => l.CreatedBy is { } creator && creator == ctx.UserId).Select(l => l.Id));
        var undecided = mainMembers.Where(m => !curated.Contains(m.ResLinkId)).ToList();
        if (undecided.Count == 0)
        {
            return curated;
        }

        foreach (var world in undecided.GroupBy(m => m.EntityType))
        {
            var ids = world.Select(TargetIdOf).Distinct().ToList();
            var writable = await targets.WritableIdsAsync(ctx, world.Key, ids, ct);
            foreach (var main in world.Where(m => writable.Contains(TargetIdOf(m))))
            {
                curated.Add(main.ResLinkId);
            }
        }

        return curated;

        static Guid TargetIdOf(ResLinkMember member) => member.FeatureId ?? member.EntityId!.Value;
    }

    /// <summary>
    /// Members as one caller sees them: display through each target world's resolver
    /// (null when the target is unreadable — the member still appears, but nothing of
    /// the target travels with it), the anchor presented under the anchor-presentation
    /// rule. Whether a member row may be emitted at all — membership disclosure for
    /// protected-feature targets — is its callers' decision: link projections drop
    /// withheld members before asking here, while the write acknowledgments — the
    /// member-write echoes and the create response — deliberately do not, since they
    /// answer the caller with memberships that caller just asserted.
    /// </summary>
    private static async Task<Dictionary<Guid, ResLinkMemberDto>> ProjectMembersAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        CancellationToken ct)
    {
        var displays = await ResolveDisplaysAsync(targets, ctx, members, ct);
        return await AssembleMembersAsync(db, access, ctx, members, displays, ct);
    }

    /// <summary>Each member's display through its target world's resolver, keyed by
    /// member id; a member whose target is missing or unreadable has no entry.</summary>
    private static async Task<Dictionary<Guid, ResLinkTargetDisplayDto>> ResolveDisplaysAsync(
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        CancellationToken ct)
    {
        var displays = new Dictionary<Guid, ResLinkTargetDisplayDto>();
        foreach (var group in members.GroupBy(m => m.EntityType))
        {
            var ids = group.Select(m => m.FeatureId ?? m.EntityId!.Value).Distinct().ToList();
            var resolved = await targets.Of(group.Key).ResolveAsync(ctx, ids, ct);
            foreach (var member in group)
            {
                if (resolved.TryGetValue(member.FeatureId ?? member.EntityId!.Value, out var display))
                {
                    displays[member.Id] = display;
                }
            }
        }

        return displays;
    }

    /// <summary>
    /// The members that show this caller exact coordinates — the sibling fact the
    /// membership-disclosure mapping asks for. Five ways a member does: it names a
    /// feature with a drawn geometry the caller may both read and place exactly; it
    /// names a trip the caller may read that carries a sketch of its own, which is
    /// served exactly to every reader of the trip; a file of its document carries a
    /// capture point of its own that the photo-position rule discloses to this caller
    /// and that the caller can actually fetch — any currently served file, or a
    /// superseded one for callers the document's own rules let into version history; it
    /// names a survey model the caller may open, which resolves only with exact view on
    /// its cave and routes straight to it; or its anchor reads coordinates out of a
    /// geofile the caller may read. Batched throughout — a link page costs the same
    /// handful of queries however many members it has.
    /// </summary>
    private static async Task<HashSet<Guid>> ExposingMemberIdsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto> displays,
        CancellationToken ct)
    {
        var exposing = new HashSet<Guid>();

        // Feature members: readable (exact view alone does not put coordinates on a page
        // the caller cannot even list the feature on), positioned, and exactly placeable.
        var featureMembers = members
            .Where(m => m.FeatureId is not null && displays.ContainsKey(m.Id))
            .ToList();
        var featureIds = featureMembers.Select(m => m.FeatureId!.Value).Distinct().ToList();
        if (featureIds.Count > 0)
        {
            var positioned = await db.Features.AsNoTracking()
                .Where(f => featureIds.Contains(f.Id) && f.Geom != null)
                .Select(f => f.Id)
                .ToHashSetAsync(ct);
            if (positioned.Count > 0)
            {
                var exact = await protection.ExactViewIdsAsync(ctx, positioned, ct);
                exposing.UnionWith(featureMembers
                    .Where(m => exact.Contains(m.FeatureId!.Value))
                    .Select(m => m.Id));
            }
        }

        // Trip members carrying a position of their own. A trip's geometry is served exactly
        // to everyone who may read the trip — it is never snapped or omitted the way a
        // protected feature's is — so a readable positioned trip standing in a link puts
        // coordinates in front of the caller just as a placeable feature member does. A
        // resolved display is the proof the caller may read the trip; only the geometry is
        // left to ask about, and it is asked in one batched query.
        //
        // Either shape counts, and asking only about the sketch would be a hole rather than a
        // simplification: a trip that states only where its party meets is positioned, and a
        // link standing it in front of a caller discloses that position exactly as a sketch
        // would. Every column on this row that can carry coordinates belongs in this test.
        var tripMembers = members
            .Where(m => m.EntityType == AttachedEntityType.TripLog && displays.ContainsKey(m.Id))
            .ToList();
        var tripIds = tripMembers.Select(m => m.EntityId!.Value).Distinct().ToList();
        if (tripIds.Count > 0)
        {
            var positionedTrips = await db.TripLogs.AsNoTracking()
                .Where(t => tripIds.Contains(t.Id) && (t.Geom != null || t.MeetingGeom != null))
                .Select(t => t.Id)
                .ToHashSetAsync(ct);
            exposing.UnionWith(tripMembers
                .Where(m => positionedTrips.Contains(m.EntityId!.Value))
                .Select(m => m.Id));
        }

        // Geofile members whose anchor addresses coordinates of a file the caller may read.
        exposing.UnionWith(members
            .Where(m => m.EntityType == AttachedEntityType.Geofile
                && ResLinkRules.AnchorAddressesCoordinates(m.AnchorKind)
                && displays.ContainsKey(m.Id))
            .Select(m => m.Id));

        // Survey-model members the caller may open. A model resolves only for callers
        // with exact view on its cave — its files are absolute georeferenced coordinates
        // and its display routes to that cave — so a resolved display IS the proof that
        // this member puts the cave's exact position one step from the reader, the same
        // disclosure as a feature member the caller may place.
        exposing.UnionWith(members
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && displays.ContainsKey(m.Id))
            .Select(m => m.Id));

        // Document members with a file stamped with a position of its own — the geotagged
        // photo — that the photo-position rule discloses to this caller. An undisclosable
        // capture point shows the caller nothing, so it does not count against the
        // siblings either; nor does a capture point the caller cannot reach: a superseded
        // file's geotag counts only for callers the document's own rules let into version
        // history, because for everyone else no route serves it.
        var documentMembers = members
            .Where(m => m.EntityType == AttachedEntityType.Document && displays.ContainsKey(m.Id))
            .ToList();
        var documentIds = documentMembers.Select(m => m.EntityId!.Value).Distinct().ToList();
        if (documentIds.Count > 0)
        {
            var geotagged = await (
                    from version in db.DocumentVersions.AsNoTracking()
                    join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                    where documentIds.Contains(version.DocumentId) && file.Geom != null
                    select new { version.DocumentId, FileId = file.Id, version.IsCurrent })
                .ToListAsync(ct);
            if (geotagged.Count > 0)
            {
                var disclosable = await photoPositions.DisclosableIdsAsync(
                    ctx, [.. geotagged.Select(g => g.FileId)], ct);
                var exposingDocuments = geotagged
                    .Where(g => g.IsCurrent && disclosable.Contains(g.FileId))
                    .Select(g => g.DocumentId)
                    .ToHashSet();

                var supersededOnly = geotagged
                    .Where(g => !g.IsCurrent && disclosable.Contains(g.FileId))
                    .Select(g => g.DocumentId)
                    .Where(id => !exposingDocuments.Contains(id))
                    .Distinct()
                    .ToList();
                if (supersededOnly.Count > 0)
                {
                    exposingDocuments.UnionWith(
                        await VersionReaderDocumentIdsAsync(db, access, ctx, supersededOnly, ct));
                }

                exposing.UnionWith(documentMembers
                    .Where(m => exposingDocuments.Contains(m.EntityId!.Value))
                    .Select(m => m.Id));
            }
        }

        return exposing;
    }

    /// <summary>
    /// Of the given documents, those whose superseded versions this caller may see — the
    /// document-write walk, the same one the version-history endpoint answers with,
    /// decided against each document's currently served file. Per-document, so callers
    /// keep the input small: documents behind stale pins, documents whose only geotag is
    /// a superseded file.
    /// </summary>
    private static async Task<HashSet<Guid>> VersionReaderDocumentIdsAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct)
    {
        var readers = new HashSet<Guid>();
        if (documentIds.Count == 0)
        {
            return readers;
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(ct);
        var currentFiles = (await (
                from version in db.DocumentVersions.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                where documentIds.Contains(version.DocumentId) && version.IsCurrent
                orderby file.CreatedAt, file.Id
                select new { version.DocumentId, File = file })
            .ToListAsync(ct))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.First().File);
        foreach (var document in documents)
        {
            if (await DocumentAccessRules.CanWriteAsync(
                    db, access, ctx, document, currentFiles.GetValueOrDefault(document.Id), ct))
            {
                readers.Add(document.Id);
            }
        }

        return readers;
    }

    /// <summary>
    /// Member DTOs from already-resolved displays. The anchor travels under the
    /// anchor-presentation rule: an unreadable target takes payload, pin and state with
    /// it — a payload can quote what it anchors to, and that a document was re-versioned
    /// is a fact about the document — while a superseded pin is disclosed as degraded to
    /// every reader but its file id is handed only to callers the document's own rules
    /// let into superseded versions, so the marker never routes a read-only caller into
    /// history their document read would refuse.
    /// </summary>
    private static async Task<Dictionary<Guid, ResLinkMemberDto>> AssembleMembersAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto> displays,
        CancellationToken ct)
    {
        // Only readable members' pins are even asked about: for an unreadable target
        // everything is withheld anyway, and its version chain must not be queried on a
        // path whose timing could differ.
        var pins = members
            .Where(m => m.AnchorFileId is not null && displays.ContainsKey(m.Id))
            .Select(m => m.AnchorFileId!.Value)
            .Distinct()
            .ToList();
        var stalePins = pins.Count == 0
            ? new HashSet<Guid>()
            : await (
                    from file in db.StoredFiles.AsNoTracking()
                    join version in db.DocumentVersions.AsNoTracking()
                        on file.DocumentVersionId equals version.Id
                    where pins.Contains(file.Id) && !version.IsCurrent
                    select file.Id)
                .ToHashSetAsync(ct);

        // Of the documents behind stale pins, those whose superseded versions this
        // caller may see. Stale pins are rare, so the per-document walk is bounded by
        // their count, not the page's.
        var staleDocumentIds = members
            .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId is not null
                && m.AnchorFileId is { } pin && stalePins.Contains(pin)
                && displays.ContainsKey(m.Id))
            .Select(m => m.EntityId!.Value)
            .Distinct()
            .ToList();
        var versionReaders = await VersionReaderDocumentIdsAsync(db, access, ctx, staleDocumentIds, ct);

        return members.ToDictionary(m => m.Id, m =>
        {
            var display = displays.GetValueOrDefault(m.Id);
            var facing = ResLinkRules.PresentAnchor(
                targetReadable: display is not null,
                pinSuperseded: m.AnchorFileId is { } pin && stalePins.Contains(pin),
                supersededVersionsReadable: m.EntityId is { } documentId
                    && versionReaders.Contains(documentId));
            return new ResLinkMemberDto(
                m.Id,
                ResLinkTargets.NameOf(m.FeatureId, m.EntityType),
                m.FeatureId ?? m.EntityId!.Value,
                m.IsMain,
                m.SortOrder,
                m.Note,
                m.AnchorKind,
                facing.PayloadShown ? ParseAnchor(m.Anchor) : null,
                facing.PinShown ? m.AnchorFileId : null,
                facing.DegradationShown ? ResLinkAnchorState.Degraded : ResLinkAnchorState.Exact,
                display);
        });
    }
}
