// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using ImageMagick;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// Photos and documents for a phone: a catalogue of what hangs on the set's features, read in
/// pages like the features themselves, and addresses for the files a caver then chooses to fetch.
/// Nothing is pushed — the catalogue lists, and bytes move only for the items a phone names.
/// </summary>
public static class SyncDocumentEndpoints
{
    /// <summary>The most files one request may ask addresses for.</summary>
    public const int LinkItemsMax = 200;

    /// <summary>The word that asks for a file itself rather than a picture of it.</summary>
    public const string OriginalRendition = "original";

    /// <summary>
    /// A link changed when it was itself last written, when the document it hangs was, or when
    /// the object it hangs on was — whichever is latest. The document's time means a renamed
    /// document or a new revision moves every link to it. The feature's time is what carries a
    /// link to a device that already read past it: a feature can come into what the set hands
    /// this caller through a change to its own row alone (a location guard lifted, a move under
    /// one of the roots), the feature stream then delivers it again, and its links have to come
    /// with it, or the device holds the cave and never its photos.
    /// </summary>
    private static readonly SyncStreamOrder<SyncLinkRow> ByChange = new(
        r => r.Link.UpdatedAt > r.Document.UpdatedAt
            ? (r.Link.UpdatedAt > r.TargetUpdatedAt ? r.Link.UpdatedAt : r.TargetUpdatedAt)
            : (r.Document.UpdatedAt > r.TargetUpdatedAt ? r.Document.UpdatedAt : r.TargetUpdatedAt),
        r => r.Link.Id);

    private static readonly SyncStreamOrder<SyncLinkGone> ByGone = new(g => g.At, g => g.Id);

    public static void MapSyncDocumentEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/documents", DocumentsAsync)
            .WithName("syncDocuments")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the document links on a sync set's features, and the links that have gone.");
    }

    public static void MapSyncFileLinkEndpoints(this RouteGroupBuilder sync)
    {
        sync.MapPost("/files/links", LinksAsync).WithValidation<SyncFileLinksRequest>()
            .WithName("syncFileLinks")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Addresses to fetch named documents' files from, each re-checked against the caller's access.");
    }

    private static async Task<Results<Ok<SyncDocumentsPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        DocumentsAsync(
            Guid id,
            string? cursor,
            int? pageSize,
            SilexGisDbContext db,
            FeatureProtection protection,
            PhotoPositionDisclosure photos,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (opening, problem) = await SyncStreamPaging.OpenAsync(
            db, ctx, SyncStream.Documents, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncDocumentsPageDto(
                opening.Set.Revision, opening.Settings, [], [], opening.Cursor, false));
        }

        // Only what hangs on a feature the feature stream would hand this caller right now: a link
        // on a withheld cave is absent, not stubbed, because a row hanging there says where to look.
        var handed = await SyncScope.HandedAsync(db, protection, ctx, opening.Roots, ct);
        var handedIds = handed.Select(f => f.Id);
        var (linked, onRecordings, onScans) = Candidates(db, ctx, handed);
        var (liveFeature, liveRecording, liveScan) = await CarriedAsync(db, ctx, handed, ct);

        // Three kinds of going. A link detached — by any path, including the cascades no application
        // code sees — is in the removal log the database keeps. A link whose document was deleted
        // is still a row, and goes when the document did. A link on a recording or a scan that was
        // removed goes when its object did. All are told for links on what the set hands this
        // caller, as an identifier and a moment that locate nothing.
        var removed = db.SyncLinkRemovals.AsNoTracking()
            .Where(r => r.FeatureId != null && handedIds.Contains(r.FeatureId.Value))
            .Select(r => new SyncLinkGone { Id = r.AttachmentId, At = r.RemovedAt });
        var orphaned = await OrphanedAsync(
            db,
            linked.Where(x => !db.Documents.Any(d => d.Id == x.DocumentId))
                .Select(x => new LinkedDocument(x.Link.Id, x.DocumentId)),
            ct);
        orphaned.AddRange(await OrphanedAsync(
            db,
            onRecordings.Where(x => !db.Documents.Any(d => d.Id == x.DocumentId))
                .Select(x => new LinkedDocument(x.Link.Id, x.DocumentId)),
            ct));
        orphaned.AddRange(await OrphanedAsync(
            db,
            onScans.Where(x => !db.Documents.Any(d => d.Id == x.DocumentId))
                .Select(x => new LinkedDocument(x.Link.Id, x.DocumentId)),
            ct));
        var objectsGone = await GoneWithObjectAsync(db, ctx, handed, opening.From, ct);

        liveFeature = SyncStreamPaging.After(liveFeature, ByChange, opening.From);
        liveRecording = SyncStreamPaging.After(liveRecording, ByChange, opening.From);
        liveScan = SyncStreamPaging.After(liveScan, ByChange, opening.From);
        removed = SyncStreamPaging.After(removed, ByGone, opening.From);

        var featureRows = await SyncStreamPaging.Probe(liveFeature, ByChange, opening.Size).ToListAsync(ct);
        var recordingRows = await SyncStreamPaging.Probe(liveRecording, ByChange, opening.Size).ToListAsync(ct);
        var scanRows = await SyncStreamPaging.Probe(liveScan, ByChange, opening.Size).ToListAsync(ct);
        var removedRows = await SyncStreamPaging.Probe(removed, ByGone, opening.Size).ToListAsync(ct);
        var orphanedRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After(orphaned.AsQueryable(), ByGone, opening.From), ByGone, opening.Size).ToList();
        var objectRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After(objectsGone.AsQueryable(), ByGone, opening.From), ByGone, opening.Size).ToList();

        var cut = SyncStreamPaging.Cut(
            opening,
            featureRows.Select(ByChange.Entry),
            recordingRows.Select(ByChange.Entry),
            scanRows.Select(ByChange.Entry),
            removedRows.Select(ByGone.Entry),
            orphanedRows.Select(ByGone.Entry),
            objectRows.Select(ByGone.Entry));

        var links = await ShapeAsync(db, photos, ctx, [.. cut.Rows.OfType<SyncLinkRow>()], ct);

        return TypedResults.Ok(new SyncDocumentsPageDto(
            opening.Set.Revision,
            opening.Settings,
            links,
            [.. cut.Rows.OfType<SyncLinkGone>().Select(g => new SyncTombstoneDto(g.Id, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// Links as the stream hands them to this caller, each with its document as a catalogue entry
    /// — the reach of every file decided by the one reach rule for all of them at once.
    /// <paramref name="rows"/> are links the stream carries for this caller.
    /// </summary>
    internal static async Task<List<SyncDocumentLinkDto>> ShapeAsync(
        SilexGisDbContext db,
        PhotoPositionDisclosure photos,
        AccessContext ctx,
        IReadOnlyList<SyncLinkRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var reach = await photos.ReachAsync(ctx, [.. rows.Select(r => r.File.Id)], ct);
        var documentIds = rows.Select(r => r.Document.Id).Distinct().ToList();
        var details = await db.PhotoDetails.AsNoTracking()
            .Where(p => documentIds.Contains(p.DocumentId))
            .ToDictionaryAsync(p => p.DocumentId, ct);
        var photographers = await PhotographerResolution.LinkedCaversAsync(
            db,
            [.. rows.Where(r => r.File.Kind == FileKind.Image).Select(r => r.Document)],
            details,
            ct);

        return [.. rows.Select(r => new SyncDocumentLinkDto(
            r.Link.Id,
            r.Document.Id,
            new SyncLinkTargetDto(r.TargetKind, r.TargetId),
            r.Link.Role,
            r.Link.Caption,
            r.Link.SortOrder,
            r.Link.IsPrimary,
            r.Link.InDeviceDefaultSet,
            r.Link.UpdatedAt,
            SyncDocuments.ToDto(
                r.Document,
                r.File,
                details.GetValueOrDefault(r.Document.Id),
                reach,
                photographers.TryGetValue(r.Document.Id, out var caver) ? caver : null)))];
    }

    /// <summary>
    /// The links the stream carries for this caller, one query per kind of object they hang on:
    /// on a feature in <paramref name="handed"/>, on a recording the caller may read in a cave in
    /// it, and on a scan of such a recording whose place is in it too (or that names none) — each
    /// narrowed to documents the caller may read. Every question about which links, and so which
    /// documents, a caller is given starts here.
    /// </summary>
    internal static async Task<(IQueryable<SyncLinkRow> Features, IQueryable<SyncLinkRow> Recordings, IQueryable<SyncLinkRow> Scans)>
        CarriedAsync(SilexGisDbContext db, AccessContext ctx, IQueryable<Domain.Entities.Feature> handed, CancellationToken ct)
    {
        var (linked, onRecordings, onScans) = Candidates(db, ctx, handed);

        // Being able to read the cave a document hangs on is how most documents are reached, but
        // a rule written against the document itself is asked first and its deny is final — the
        // stream lists only what the caller may read, decided before the page is cut.
        return (
            await ReadableLinksAsync(db, ctx, linked, ct),
            await ReadableLinksAsync(db, ctx, onRecordings, ct),
            await ReadableLinksAsync(db, ctx, onScans, ct));
    }

    /// <summary>
    /// The links on what the set hands this caller, before the documents' own rules are asked.
    /// </summary>
    private static (IQueryable<LinkedFile> Features, IQueryable<LinkedFile> Recordings, IQueryable<LinkedFile> Scans)
        Candidates(SilexGisDbContext db, AccessContext ctx, IQueryable<Domain.Entities.Feature> handed)
    {
        var handedIds = handed.Select(f => f.Id);

        var linked =
            from link in db.Attachments.AsNoTracking()
            join feature in handed on link.FeatureId equals (Guid?)feature.Id
            join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            select new LinkedFile
            {
                Link = link,
                File = file,
                DocumentId = version.DocumentId,
                TargetKind = SyncLinkTargetKind.Feature,
                TargetId = feature.Id,
                TargetUpdatedAt = feature.UpdatedAt,
            };

        // A photo on a recording is carried when the recording's cave is handed and the caller may
        // read the recording; one on a scan, when the scan's place is handed as well. A scan that
        // names no place carries only the recording's conditions. Both are asked of the same
        // answer the feature stream and the trips stream read, so a photo can never reach a phone
        // beside a scan the trips stream withholds from it.
        var recordings =
            from recording in db.TripRecordings.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs)
            join cave in handed on recording.CaveFeatureId equals cave.Id
            select new { recording.Id, CaveUpdatedAt = cave.UpdatedAt, recording.UpdatedAt };
        var onRecordings =
            from link in db.Attachments.AsNoTracking()
            where link.EntityType == AttachedEntityType.TripRecording
            join recording in recordings on link.EntityId equals (Guid?)recording.Id
            join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            select new LinkedFile
            {
                Link = link,
                File = file,
                DocumentId = version.DocumentId,
                TargetKind = SyncLinkTargetKind.TripRecording,
                TargetId = recording.Id,
                TargetUpdatedAt = recording.UpdatedAt > recording.CaveUpdatedAt ? recording.UpdatedAt : recording.CaveUpdatedAt,
            };
        var onScans =
            from link in db.Attachments.AsNoTracking()
            where link.EntityType == AttachedEntityType.TripRecordingPoint
            join scan in db.TripRecordingPoints.AsNoTracking() on link.EntityId equals (Guid?)scan.Id
            where scan.DeletedAt == null
            join recording in recordings on scan.RecordingId equals recording.Id
            where scan.PlaceFeatureId == null || handedIds.Contains(scan.PlaceFeatureId.Value)
            join place in handed on scan.PlaceFeatureId equals (Guid?)place.Id into places
            from place in places.DefaultIfEmpty()
            join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            let scanOrRecording = scan.UpdatedAt > recording.UpdatedAt ? scan.UpdatedAt : recording.UpdatedAt
            let caveOrPlace = place == null || recording.CaveUpdatedAt > place.UpdatedAt
                ? recording.CaveUpdatedAt
                : place.UpdatedAt
            select new LinkedFile
            {
                Link = link,
                File = file,
                DocumentId = version.DocumentId,
                TargetKind = SyncLinkTargetKind.TripPoint,
                TargetId = scan.Id,
                TargetUpdatedAt = scanOrRecording > caveOrPlace ? scanOrRecording : caveOrPlace,
            };

        return (linked, onRecordings, onScans);
    }

    /// <summary>
    /// The links on handed features whose document has been deleted, each stamped with the moment
    /// the document went. <paramref name="unlisted"/> is the links whose document no longer reads
    /// as there, found under every filter.
    /// </summary>
    /// <remarks>
    /// Asked in two statements rather than one, and the split is the point. Reading a deleted
    /// document means looking past the filter that hides deleted rows, and that switch cannot be
    /// thrown for one table of a statement: it would lift the same filter off the features the
    /// links are restricted to, and a deleted feature would count as handed. So the links are found
    /// under every filter — a link whose document no longer reads as there — and only their
    /// documents' deletion times are read past it, by identifier. Both are small: a deleted
    /// document's links last until its file is purged, and then the removal log has them.
    /// </remarks>
    private static async Task<List<SyncLinkGone>> OrphanedAsync(
        SilexGisDbContext db, IQueryable<LinkedDocument> unlisted, CancellationToken ct)
    {
        var pairs = await unlisted.ToListAsync(ct);
        if (pairs.Count == 0)
        {
            return [];
        }

        var documentIds = pairs.Select(p => p.DocumentId).Distinct().ToList();
        var deletedAt = await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Where(d => documentIds.Contains(d.Id) && d.DeletedAt != null)
            .Select(d => new { d.Id, DeletedAt = d.DeletedAt!.Value })
            .ToDictionaryAsync(d => d.Id, d => d.DeletedAt, ct);

        return [.. pairs
            .Where(p => deletedAt.ContainsKey(p.DocumentId))
            .Select(p => new SyncLinkGone { Id = p.LinkId, At = deletedAt[p.DocumentId] })];
    }

    /// <summary>
    /// The candidate links narrowed to the ones whose document this caller may read, shaped as
    /// rows of the stream. A query, so each kind of target pages over readable documents only.
    /// </summary>
    private static async Task<IQueryable<SyncLinkRow>> ReadableLinksAsync(
        SilexGisDbContext db, AccessContext ctx, IQueryable<LinkedFile> linked, CancellationToken ct)
    {
        var readable = await SyncDocuments.ReadableAsync(
            db,
            ctx,
            db.Documents.AsNoTracking().Where(d => linked.Select(x => x.DocumentId).Contains(d.Id)),
            ct);
        return
            from x in linked
            join document in readable on x.DocumentId equals document.Id
            select new SyncLinkRow
            {
                Link = x.Link,
                File = x.File,
                Document = document,
                TargetKind = x.TargetKind,
                TargetId = x.TargetId,
                TargetUpdatedAt = x.TargetUpdatedAt,
            };
    }

    /// <summary>
    /// The links on recordings and scans that have gone with the object they hung on or been
    /// detached from it, each stamped with the moment, for the recordings and scans this caller may
    /// read in caves — and, for a scan, places — the set hands this caller.
    /// </summary>
    /// <remarks>
    /// Asked in two statements for the reason the trips stream's removals are: reading a removed
    /// recording means looking past the filter that hides removed rows, and that switch cannot be
    /// thrown for one table of a statement without lifting the filter off the features too, so a
    /// deleted cave would count as handed. The candidate links are found past the filter on the
    /// recordings and scans alone; their objects' caves and places are then asked about, under
    /// every filter, by identifier.
    /// </remarks>
    private static async Task<List<SyncLinkGone>> GoneWithObjectAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IQueryable<Domain.Entities.Feature> handed,
        SyncCursor? from,
        CancellationToken ct)
    {
        // A scan has gone when it was removed or its recording was; whichever is recorded
        // is the moment, and the earlier one when both are.
        var detached = db.SyncLinkRemovals.AsNoTracking()
            .Where(r => r.EntityId != null
                && (r.EntityType == AttachedEntityType.TripRecording
                    || r.EntityType == AttachedEntityType.TripRecordingPoint))
            .Select(r => new SyncLinkGone
            {
                Id = r.AttachmentId, At = r.RemovedAt, EntityType = r.EntityType, EntityId = r.EntityId,
            });
        var onRemovedRecordings =
            from link in db.Attachments.AsNoTracking()
            where link.EntityType == AttachedEntityType.TripRecording
            join recording in db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
                on link.EntityId equals (Guid?)recording.Id
            where recording.DeletedAt != null
            select new SyncLinkGone
            {
                Id = link.Id, At = recording.DeletedAt!.Value, EntityType = link.EntityType, EntityId = link.EntityId,
            };
        var onRemovedScans =
            from link in db.Attachments.AsNoTracking()
            where link.EntityType == AttachedEntityType.TripRecordingPoint
            join scan in db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                on link.EntityId equals (Guid?)scan.Id
            join recording in db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
                on scan.RecordingId equals recording.Id
            where scan.DeletedAt != null || recording.DeletedAt != null
            select new SyncLinkGone
            {
                Id = link.Id,
                At = scan.DeletedAt ?? recording.DeletedAt!.Value,
                EntityType = link.EntityType,
                EntityId = link.EntityId,
            };

        var candidates = new List<SyncLinkGone>();
        foreach (var source in new[] { detached, onRemovedRecordings, onRemovedScans })
        {
            candidates.AddRange(await SyncStreamPaging.After(source, ByGone, from).ToListAsync(ct));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var scanIds = candidates.Where(c => c.EntityType == AttachedEntityType.TripRecordingPoint)
            .Select(c => c.EntityId!.Value).Distinct().ToList();
        var scans = scanIds.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                .Where(p => scanIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.RecordingId, p.PlaceFeatureId), ct);
        var recordingIds = candidates.Where(c => c.EntityType == AttachedEntityType.TripRecording)
            .Select(c => c.EntityId!.Value)
            .Concat(scans.Values.Select(v => v.RecordingId))
            .Distinct().ToList();
        var recordings = await db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(r => recordingIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.CaveFeatureId, ct);

        var featureIds = recordings.Values
            .Concat(scans.Values.Where(v => v.PlaceFeatureId != null).Select(v => v.PlaceFeatureId!.Value))
            .Distinct().ToList();
        var handedIds = (await handed.Where(f => featureIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        return [.. candidates.Where(c =>
        {
            Guid recordingId;
            Guid? place = null;
            if (c.EntityType == AttachedEntityType.TripRecordingPoint)
            {
                if (!scans.TryGetValue(c.EntityId!.Value, out var scan))
                {
                    return false;
                }

                (recordingId, place) = scan;
            }
            else
            {
                recordingId = c.EntityId!.Value;
            }

            return recordings.TryGetValue(recordingId, out var cave)
                && handedIds.Contains(cave)
                && (place is not { } placeId || handedIds.Contains(placeId));
        })];
    }

    /// <summary>
    /// Addresses for the files a phone chose to fetch. Each item is decided again here — the
    /// catalogue the phone read may be hours old, and access can have been withdrawn since — so an
    /// item is answered only when the caller may read the document now, at the reach the one reach
    /// rule gives now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The address of a file itself lives for a sitting rather than for one fetch: the content
    /// route answers ranges, and a phone on a poor connection resumes a large file over several of
    /// them. A picture is small and is fetched at once, so its address keeps the short lifetime.
    /// </para>
    /// <para>
    /// A photo handed without its location is a copy made on first request, so nothing knows its
    /// size or hash before somebody asks for it. When the administrator chose to have phones told
    /// them, the copy is made here, for each such item, and its numbers go beside its address —
    /// which is what makes this request slow for many such photos, and why it is a choice. A copy
    /// that cannot be made is answered with its address and no numbers: fetching it fails the same
    /// way whether or not they were asked for.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<SyncFileLinksDto>, UnauthorizedHttpResult>> LinksAsync(
        SyncFileLinksRequest request,
        SilexGisDbContext db,
        PhotoPositionDisclosure photos,
        IFileAccessTokenService tokens,
        IAppSettingsService settings,
        LocationFreeCopyService locationFreeCopies,
        ILogger<LocationFreeCopyService> logger,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var ids = request.Items.Select(i => i.DocumentId).Distinct().ToList();
        var readable = await SyncDocuments.ReadableAsync(
            db, ctx, db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)), ct);

        // The file a document serves is the first of its current revision — the row attachments
        // point at, spelled the way the document routes spell it.
        var files = await readable
            .Select(d => new
            {
                DocumentId = d.Id,
                File = db.StoredFiles.AsNoTracking()
                    .Where(f => db.DocumentVersions
                        .Any(v => v.Id == f.DocumentVersionId && v.DocumentId == d.Id && v.IsCurrent))
                    .OrderBy(f => f.CreatedAt)
                    .ThenBy(f => f.Id)
                    .FirstOrDefault(),
            })
            .Where(x => x.File != null)
            .ToDictionaryAsync(x => x.DocumentId, x => x.File!, ct);

        var reach = await photos.ReachAsync(ctx, [.. files.Values.Select(f => f.Id)], ct);
        var describeCopies = (await settings.GetProtectionAsync(ct)).DescribeLocationFreeCopies;
        var described = new Dictionary<Guid, (long SizeBytes, string Sha256)?>();
        // The same clock the address's own expiry is stamped against, so the time a phone is told
        // is the time the address actually stops opening.
        var now = DateTimeOffset.UtcNow;

        var links = new List<SyncFileLinkDto>();
        var refused = new List<SyncFileLinkRefusalDto>();
        foreach (var item in request.Items)
        {
            var rendition = item.Rendition.Clone();

            // A document that is not there, was deleted, or that this caller may no longer read
            // are one answer: telling them apart would say which of the three it is.
            if (!files.TryGetValue(item.DocumentId, out var file))
            {
                refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.DocumentUnavailable));
                continue;
            }

            if (rendition.ValueKind == JsonValueKind.String)
            {
                var delivery = FileDeliveries.Of(reach.OriginalOf(file));
                if (delivery == FileDelivery.DerivativesOnly)
                {
                    refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.OriginalWithheld));
                    continue;
                }

                var copy = delivery == FileDelivery.LocationFree && describeCopies
                    ? await DescribeCopyAsync(file)
                    : null;
                var token = tokens.CreateToken(file.Id, delivery, FileAccessLifetime.Session);
                links.Add(new SyncFileLinkDto(
                    item.DocumentId,
                    rendition,
                    $"/api/v1/files/{file.Id}/content?token={Uri.EscapeDataString(token)}",
                    now.Add(FileAccessTokenService.SessionLifetime),
                    copy?.SizeBytes,
                    copy?.Sha256));
                continue;
            }

            if (file.Kind != FileKind.Image)
            {
                refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.RenditionUnavailable));
                continue;
            }

            // A picture opens under the narrowest reach there is: the rendering route accepts
            // every reach alike, so nothing a picture needs is lost and the address can open
            // nothing else.
            var size = rendition.GetInt32();
            var pictureToken = tokens.CreateToken(file.Id, FileDelivery.DerivativesOnly);
            links.Add(new SyncFileLinkDto(
                item.DocumentId,
                rendition,
                $"/api/v1/files/{file.Id}/thumbnail?size={size}&token={Uri.EscapeDataString(pictureToken)}",
                now.Add(FileAccessTokenService.FetchLifetime),
                null,
                null));
        }

        return TypedResults.Ok(new SyncFileLinksDto(links, refused));

        // Once per file, however many items name it.
        async Task<(long SizeBytes, string Sha256)?> DescribeCopyAsync(StoredFile file)
        {
            if (described.TryGetValue(file.Id, out var known))
            {
                return known;
            }

            try
            {
                known = await locationFreeCopies.DescribeAsync(file, ct);
            }
            catch (MagickException e)
            {
                logger.LogWarning(e, "Could not write a location-free copy of file {FileId}", file.Id);
                known = null;
            }

            described[file.Id] = known;
            return known;
        }
    }
}

/// <summary>A link as the documents stream reads it, before it is shaped for the device.</summary>
internal sealed class SyncLinkRow
{
    public required Attachment Link { get; init; }

    public required StoredFile File { get; init; }

    public required Document Document { get; init; }

    /// <summary>What kind of object the link hangs on.</summary>
    public required SyncLinkTargetKind TargetKind { get; init; }

    /// <summary>The object the link hangs on: a feature, a recording or a scan.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>
    /// When the object the link hangs on last changed, together with whatever decides that a
    /// phone is handed it: the feature's own time, a recording's and its cave's, or a scan's, its
    /// recording's, its cave's and its place's.
    /// </summary>
    public required DateTimeOffset TargetUpdatedAt { get; init; }
}

/// <summary>A link with its file and its document's identifier, before the document's own rules are asked.</summary>
internal sealed class LinkedFile
{
    public required Attachment Link { get; init; }

    public required StoredFile File { get; init; }

    public required Guid DocumentId { get; init; }

    public required SyncLinkTargetKind TargetKind { get; init; }

    public required Guid TargetId { get; init; }

    public required DateTimeOffset TargetUpdatedAt { get; init; }
}

/// <summary>A link and the document it hangs, by identifier.</summary>
internal sealed record LinkedDocument(Guid LinkId, Guid DocumentId);

/// <summary>A link that has gone, as the documents stream reads it.</summary>
internal sealed class SyncLinkGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>For a link on a recording or a scan: which, so the object can be asked about before the tombstone is told.</summary>
    public AttachedEntityType? EntityType { get; init; }

    public Guid? EntityId { get; init; }
}

/// <summary>The refusal codes the address route answers per item.</summary>
internal static class SyncCodes
{
    /// <summary>No such document, deleted, or not readable by this caller now.</summary>
    public const string DocumentUnavailable = "sync.document_unavailable";

    /// <summary>The file itself is not handed to this caller; pictures of it may be.</summary>
    public const string OriginalWithheld = "sync.original_withheld";

    /// <summary>The server draws no picture of this kind of file.</summary>
    public const string RenditionUnavailable = "sync.rendition_unavailable";

    /// <summary>
    /// The staged bytes a document row names are not the bytes it describes: their hash or their
    /// size differs from what the row states.
    /// </summary>
    public const string FileHashMismatch = "sync.file_hash_mismatch";

    /// <summary>
    /// A document row brings new content but names no staged bytes this account still holds —
    /// never sent, withdrawn, already adopted, or swept after the staging retention.
    /// </summary>
    public const string FileBytesMissing = "sync.file_bytes_missing";
}

public sealed class SyncFileLinksRequestValidator : AbstractValidator<SyncFileLinksRequest>
{
    public SyncFileLinksRequestValidator()
    {
        RuleFor(x => x.Items).NotNull()
            .Must(items => items is { Count: >= 1 and <= SyncDocumentEndpoints.LinkItemsMax })
            .WithMessage($"Between 1 and {SyncDocumentEndpoints.LinkItemsMax} items.");
        RuleForEach(x => x.Items).NotNull().ChildRules(item =>
        {
            item.RuleFor(i => i.DocumentId).NotEmpty();
            item.RuleFor(i => i.Rendition).Must(IsKnownRendition)
                .WithMessage("\"original\", or one of 2400, 1200, 480 and 160.");
        });
    }

    private static bool IsKnownRendition(JsonElement rendition) => rendition.ValueKind switch
    {
        JsonValueKind.String => rendition.GetString() == SyncDocumentEndpoints.OriginalRendition,
        JsonValueKind.Number => rendition.TryGetInt32(out var size) && ThumbnailService.AllowedSizes.Contains(size),
        _ => false,
    };
}
