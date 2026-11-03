// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Features.Photos;

/// <summary>
/// Every photograph in the installation, in one place.
/// </summary>
/// <remarks>
/// <para>
/// A gallery is a <em>bulk</em> path to material that per-object pages release one at a time,
/// and photographs carry coordinates inside them. So every rule the per-object surfaces apply
/// is applied here and applied first: the listing is the ordinary document read rule narrowed
/// to pictures, a capture position is disclosed only where the bytes carrying it would be, and
/// every URL a tile loads is a rendering this application drew rather than the upload.
/// </para>
/// <para>
/// The map extent is the one condition that cannot be a term of the query, because whether a
/// photograph's fix may be shown is a per-caller answer. It is resolved into a bounded set of
/// ids first, so the count beside the grid never announces how many were withheld.
/// </para>
/// </remarks>
public static class PhotoEndpoints
{
    public const string NotFoundCode = "photo.not_found";
    public const string WriteForbiddenCode = "photo.write_forbidden";
    public const string NotAPhotographCode = "photo.not_a_photograph";
    public const string PublishForbiddenCode = "photo.publish_forbidden";

    /// <summary>
    /// A bulk attachment target that does not exist or that the caller may not read — the same
    /// code the attachment routes answer, because it is the same question.
    /// </summary>
    public const string AttachTargetNotFoundCode = "attachment.entity_not_found";

    /// <summary>A bulk request named a tag that does not exist (or no longer does).</summary>
    public const string TagNotFoundCode = "tag.not_found";

    /// <summary>A picture belonging to no club cannot be given club visibility.</summary>
    public const string CavingGroupRequiredCode = "photo.caving_group_required";

    public static RouteGroupBuilder MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        var photos = api.MapGroup("/photos").WithTags("Photos");

        photos.MapGet("/", ListAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Photographs the caller may see, newest first, narrowed by cave, feature, trip, camp, caver, "
                + "tag, album, camera, date or map extent. `filter` (a filter document as a link token) or "
                + "`savedFilterId` narrows it by the photograph world's conditions, applied after visibility "
                + "and counted over the narrowed set.");
        photos.MapGet("/duplicates", DuplicatesAsync)
            .WithSummary("Groups of photographs holding byte-identical content.");
        photos.MapGet("/deleted", DeletedAsync)
            .WithSummary("Photographs the caller deleted that can still be restored.");
        photos.MapGet("/{documentId:guid}", GetAsync)
            .WithSummary("One photograph: its credit, what it states about itself, and where it hangs.");
        photos.MapPut("/{documentId:guid}/credit", UpdateCreditAsync)
            .WithValidation<PhotoCreditWriteRequest>()
            .WithSummary("Sets the photographer, caption, licence and place; requires write access.");
        photos.MapPut("/{documentId:guid}/public", SetPublicAsync)
            .WithSummary("Puts a photograph in the installation's public gallery, or takes it out.");
        photos.MapPost("/{documentId:guid}/restore", RestoreAsync)
            .WithSummary("Restores a deleted photograph inside its window.");
        photos.MapPost("/bulk", BulkAsync)
            .WithValidation<PhotoBulkRequest>()
            .WithSummary("Tags, attaches, re-audiences, rotates, files into an album or deletes many photographs at once.");

        return api;
    }

    /// <summary>The gallery.</summary>
    private static async Task<Results<Ok<PhotoPage>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        [AsParameters] PhotoQuery query,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        string? filter = null,
        Guid? savedFilterId = null,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var narrowing = await surfaceFilters.ForAsync<Document>(
            ctx, PhotographFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // The filter only intersects the pictures this caller may already see, and the gallery's
        // own narrowings and the map extent are applied on top of it: the count, the page and the
        // extent's candidate set are all taken over the composed set. Nothing the filter asks
        // about is a position, so whether a picture's fix is disclosed is still decided below, per
        // picture, exactly as it is without a filter.
        var photographs = narrowing.Narrowing!
            .Apply(await PhotographReads.VisiblePhotographsAsync(db, ctx, ct))
            .Narrow(db, ctx, query);
        var extentCutShort = false;

        if (WindowProblems.Optional(query.Bbox, out var window) is { } windowProblem)
        {
            return windowProblem;
        }

        if (window is { } box)
        {
            var inExtent = await PhotoQueries.InExtentAsync(
                db, ctx, disclosure, photographs, box.ToPolygon(), ct);
            var documentIds = inExtent.DocumentIds;
            photographs = photographs.Where(d => documentIds.Contains(d.Id));
            extentCutShort = inExtent.CutShort;
        }

        var (p, size) = Paging.Normalize(page, pageSize);

        // An album is the one listing with an order somebody chose. Everywhere else the answer
        // is newest first, which is what a gallery of an accumulating archive is for.
        var ordered = query.AlbumId is { } albumId
            ? photographs.OrderBy(d => db.AlbumItems
                .Where(i => i.AlbumId == albumId && i.DocumentId == d.Id)
                .Select(i => i.SortOrder)
                .FirstOrDefault())
                .ThenBy(d => d.Id)
            : photographs.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id);

        // Paged over the documents, then projected: the response's shape changes with the
        // projection, so the page is rebuilt around it rather than edited.
        var documents = await ordered.ToPagedAsync(p, size, d => d, ct);
        var items = await ProjectAsync(db, ctx, tokens, disclosure, documents.Items, ct);
        return TypedResults.Ok(new PhotoPage(
            items, documents.Page, documents.PageSize, documents.TotalItems, extentCutShort));
    }

    /// <summary>One photograph, with everything its panel shows.</summary>
    private static async Task<Results<Ok<PhotoDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid documentId,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await ReadableAsync(db, access, ctx, documentId, ct);
        if (subject is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var rows = await ProjectAsync(db, ctx, tokens, disclosure, [subject], ct);
        return rows.Count == 0 ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(rows[0]);
    }

    /// <summary>Sets what a photograph says about itself.</summary>
    private static async Task<Results<Ok<PhotoDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateCreditAsync(
        Guid documentId,
        PhotoCreditWriteRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await ReadableAsync(db, access, ctx, documentId, ct);
        if (subject is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await WritableAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        if (request.PhotographerCaverId is { } caverId
            && !await db.Cavers.AsNoTracking().AnyAsync(c => c.Id == caverId, ct))
        {
            return ApiProblems.BadRequest("photo.caver_not_found", "The caver does not exist.");
        }

        var details = await db.PhotoDetails.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (details is null)
        {
            details = new PhotoDetails { DocumentId = documentId };
            db.PhotoDetails.Add(details);
        }

        details.PhotographerCaverId = request.PhotographerCaverId;
        // A roster credit and a typed name are alternatives, not a pair: keeping both would show
        // one picture under two photographers depending on which the reader looked at.
        details.PhotographerName = request.PhotographerCaverId is null ? request.PhotographerName : null;
        details.Caption = request.Caption;
        details.LicenceCode = string.IsNullOrWhiteSpace(request.LicenceCode) ? null : request.LicenceCode;
        details.PlaceName = request.PlaceName;

        await db.SaveChangesAsync(ct);

        var rows = await ProjectAsync(db, ctx, tokens, disclosure, [subject], ct);
        return rows.Count == 0 ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(rows[0]);
    }

    /// <summary>
    /// Publishes a photograph to the installation's public gallery, or withdraws it.
    /// </summary>
    /// <remarks>
    /// A full administrator's decision, and deliberately not the same one as the document's read
    /// audience. Making something readable by every account here is an ordinary editorial act;
    /// putting it on the internet is not, and the two should not be reachable by the same
    /// control or by the same person's ordinary rights.
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> SetPublicAsync(
        Guid documentId,
        bool published,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden(PublishForbiddenCode);
        }

        var subject = await ReadableAsync(db, access, ctx, documentId, ct);
        if (subject is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var details = await db.PhotoDetails.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (details is null)
        {
            details = new PhotoDetails { DocumentId = documentId };
            db.PhotoDetails.Add(details);
        }

        details.InPublicGallery = published;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Photographs holding byte-identical content — twelve near-identical entrance shots being
    /// the normal state of a club archive, exact copies are what can be said for certain.
    /// </summary>
    /// <remarks>
    /// Only groups where the caller may read more than one member are reported. A group of two
    /// showing one picture is not a duplicate anybody can act on, and it would say that a copy
    /// exists somewhere they cannot see.
    /// </remarks>
    private static async Task<Results<Ok<List<PhotoDuplicateGroupDto>>, UnauthorizedHttpResult>> DuplicatesAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        int? limit = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var groups = Math.Clamp(limit ?? 50, 1, 200);
        var photographs = await PhotographReads.VisiblePhotographsAsync(db, ctx, ct);

        // The hashes worth looking at first: computed over what this caller may read, so a
        // hash shared with a picture they cannot see is not a group at all.
        var hashes = await (from file in db.StoredFiles.AsNoTracking()
                            join version in db.DocumentVersions.AsNoTracking()
                                on file.DocumentVersionId equals version.Id
                            where version.IsCurrent && photographs.Any(d => d.Id == version.DocumentId)
                            group file by file.Sha256 into byHash
                            where byHash.Count() > 1
                            select byHash.Key)
            .Take(groups)
            .ToListAsync(ct);

        if (hashes.Count == 0)
        {
            return TypedResults.Ok(new List<PhotoDuplicateGroupDto>());
        }

        var members = await (from file in db.StoredFiles.AsNoTracking()
                             join version in db.DocumentVersions.AsNoTracking()
                                 on file.DocumentVersionId equals version.Id
                             join document in db.Documents.AsNoTracking()
                                 on version.DocumentId equals document.Id
                             where version.IsCurrent
                                 && hashes.Contains(file.Sha256)
                                 && photographs.Any(d => d.Id == document.Id)
                             orderby file.CreatedAt, file.Id
                             select new { file.Sha256, Document = document })
            .ToListAsync(ct);

        // Projected once for every member of every group — the position decision is per caller
        // and over the whole set anyway — and then dealt back out into the groups.
        var groupsOfMembers = members.GroupBy(m => m.Sha256).Where(g => g.Count() > 1).ToList();
        var projected = await ProjectAsync(
            db,
            ctx,
            tokens,
            disclosure,
            [.. groupsOfMembers.SelectMany(g => g).Select(m => m.Document).DistinctBy(d => d.Id)],
            ct);
        var byDocument = projected.ToDictionary(p => p.DocumentId);

        var result = new List<PhotoDuplicateGroupDto>();
        foreach (var group in groupsOfMembers)
        {
            List<PhotoDto> photos =
            [
                .. group.Select(m => byDocument.GetValueOrDefault(m.Document.Id)).OfType<PhotoDto>(),
            ];
            if (photos.Count > 1)
            {
                result.Add(new PhotoDuplicateGroupDto(group.Key, photos));
            }
        }

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// The deleted photographs the caller could put back.
    /// </summary>
    /// <remarks>
    /// The same answer the document list gives, narrowed to pictures: what a person may restore is
    /// what they may edit, not what they happened to delete themselves.
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<DeletedPhotoDto>>, UnauthorizedHttpResult>> DeletedAsync(
        DocumentDeletionService deletion,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var (shown, total) = await deletion.ListAsync(ctx, photographsOnly: true, p, size, ct);
        return TypedResults.Ok(new PagedResult<DeletedPhotoDto>(
            [
                .. shown.Select(r => new DeletedPhotoDto(
                    r.Document.Id,
                    r.Document.Title,
                    r.Document.DeletedAt!.Value,
                    r.RestorableUntil,
                    r.Document.DeletedByUserId,
                    null)),
            ],
            p,
            size,
            total));
    }

    /// <summary>Puts a deleted photograph back, if this installation still keeps it.</summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RestoreAsync(
        Guid documentId,
        DocumentDeletionService deletion,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return await deletion.RestoreAsync(ctx, documentId, ct) switch
        {
            DocumentRestoreOutcome.Restored => TypedResults.NoContent(),
            DocumentRestoreOutcome.Forbidden => ApiProblems.Forbidden(WriteForbiddenCode),
            DocumentRestoreOutcome.NotDeleted =>
                ApiProblems.Conflict(SoftDeleteRules.NotDeletedCode, "This photograph is not deleted."),
            DocumentRestoreOutcome.WindowPassed => ApiProblems.Conflict(
                SoftDeleteRules.RestoreWindowPassedCode, "The restore window for this photograph has passed."),
            _ => ApiProblems.NotFound(NotFoundCode),
        };
    }

    /// <summary>
    /// Does one thing to many photographs.
    /// </summary>
    /// <remarks>
    /// Partial results, and every photograph judged exactly as it would be one at a time. A
    /// selection of two hundred pictures will, on a real archive, contain one somebody else
    /// owns; refusing the whole request for it would make bulk editing unusable on exactly the
    /// archives it exists for. A picture the caller may not read is in neither list.
    /// </remarks>
    private static async Task<Results<Ok<PhotoBulkResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> BulkAsync(
        PhotoBulkRequest request,
        SilexGisDbContext db,
        AlbumWriteService albums,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Keyed on the id, which is what makes the attachment happen, rather than on the type:
        // an id with no type would otherwise reach the attach step as "a feature" having been
        // asked nothing. A blank type does not parse, so it is refused here as well as by the
        // validator.
        AttachedEntityType? attachType = null;
        if (request.AttachEntityId is { } targetId)
        {
            if (!AttachmentTargets.TryParse(request.AttachEntityType, out attachType))
            {
                return ApiProblems.BadRequest("attachment.entity_type_unknown");
            }

            // Attaching hands the picture to everyone who can read the target, so it takes write
            // on the target — asked once for the whole request, because it does not vary by
            // picture. A target the caller may not read is answered exactly as one that does not
            // exist, so the route says nothing about which ids are real.
            var target = new AttachmentTarget(attachType, targetId);
            if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
            {
                return await FileAccessRules.CanReadTargetAsync(db, access, ctx, target, ct)
                    ? ApiProblems.Forbidden()
                    : ApiProblems.NotFound(AttachTargetNotFoundCode);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.AttachEntityType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown");
        }

        // Asked once, before any picture is touched: a tag deleted while the gallery had it on
        // screen is a refusal naming the tag, not a foreign-key failure that rolls back every
        // other picture as a server error.
        var addTagIds = (request.AddTagIds ?? []).Distinct().ToList();
        if (addTagIds.Count > 0)
        {
            var known = await db.Tags.AsNoTracking()
                .Where(t => addTagIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            if (known.Count != addTagIds.Count)
            {
                return ApiProblems.BadRequest(
                    TagNotFoundCode,
                    $"No tag {string.Join(", ", addTagIds.Except(known))}.");
            }
        }

        // The album routes' own split: an album the caller cannot read is answered as one that
        // does not exist, and only a readable one says that writing it is what is missing.
        if (request.AddToAlbumId is { } albumId
            && !await AlbumAccess.CanWriteAsync(db, access, ctx, albumId, ct))
        {
            return await AlbumAccess.ReadableAsync(db, access, ctx, albumId, ct) is null
                ? ApiProblems.NotFound(AlbumEndpoints.NotFoundCode)
                : ApiProblems.Forbidden(AlbumEndpoints.WriteForbiddenCode);
        }

        var changed = new List<Guid>();
        var refused = new Dictionary<Guid, string>();

        // Filing a picture into an album changes the album, not the picture, so it takes what the
        // album's own items route takes: write on the album (asked above) and read on the picture.
        // Anything else in the request changes the picture itself and takes write on it.
        var changesThePhotographs = ChangesThePhotographs(request);

        // Everything the walk below needs is read for the whole request up front — which ids are
        // readable photographs, their documents and current files, the taggings and attachments
        // already there — so a five-hundred-picture request costs a handful of statements plus the
        // write decision for each picture, rather than ten statements a picture.
        var ids = request.DocumentIds.Distinct().ToList();
        var readableIds = await PhotographReads.ReadableAmongAsync(db, ctx, ids, ct);
        var work = await BulkWorkAsync(db, request, addTagIds, readableIds, attachType, ct);

        foreach (var documentId in ids)
        {
            // Never an existence oracle: unreadable is answered as absent.
            if (!work.Documents.TryGetValue(documentId, out var subject))
            {
                continue;
            }

            var file = work.CurrentFiles.GetValueOrDefault(documentId);
            if (changesThePhotographs
                && !await DocumentAccessRules.CanWriteAsync(db, access, ctx, subject, file, ct))
            {
                refused[documentId] = WriteForbiddenCode;
                continue;
            }

            // Club visibility means "the club this belongs to may read it". On a picture that
            // belongs to no club it would be a band that admits nobody, silently — the document
            // update route refuses it for the same reason. The bulk request names no club, so a
            // picture without one is refused rather than hidden from everybody.
            if (request.Visibility == Visibility.CavingGroup && subject.CavingGroupId is null)
            {
                refused[documentId] = CavingGroupRequiredCode;
                continue;
            }

            Apply(db, request, addTagIds, subject, file, work, attachType, ctx.UserId);
            changed.Add(documentId);
        }

        if (request.AddToAlbumId is { } album && changed.Count > 0)
        {
            try
            {
                await albums.AddAsync(album, changed, ctx.UserId, ct);
            }
            catch (AlbumWriteException e)
            {
                return ApiProblems.Conflict(e.Code, e.Message);
            }
        }

        // Nothing purges a rendering here, and that is deliberate rather than forgotten:
        // renderings are cached under a name carrying the turn, so a rotated picture has a
        // different entry and the previous one stays valid for anybody mid-view. Turning a
        // picture back is then instant.
        var turned = db.ChangeTracker.Entries<StoredFile>()
            .Where(e => e.State == EntityState.Modified && e.Property(f => f.OrientationQuarterTurns).IsModified)
            .Select(e => e.Entity.Id)
            .ToList();

        // The survey stations marked on a turned scan turn with it, so each still marks the spot it
        // was placed on.
        await StationPointTurns.TurnAsync(db, turned, request.RotateQuarterTurns ?? 0, ct);
        await db.SaveChangesAsync(ct);

        // A turn moves the frame the browser measures points in and leaves the stored frame a phone
        // measures pins in alone, so the places pinned on a turned picture are redrawn from their
        // pixels; what a phone holds of them does not change.
        if (turned.Count > 0)
        {
            await new PlacePointStore(db).RefreshFramesAsync(turned, ct);
        }

        return TypedResults.Ok(new PhotoBulkResultDto(changed, refused));
    }

    /// <summary>What a bulk request reads before it walks its photographs, read once for all of them.</summary>
    private sealed record BulkWork(
        IReadOnlyDictionary<Guid, Document> Documents,
        IReadOnlyDictionary<Guid, StoredFile> CurrentFiles,
        HashSet<(long TagId, Guid FileId)> Tagged,
        ILookup<Guid, Tagging> Removable,
        HashSet<Guid> AttachedFiles);

    private static async Task<BulkWork> BulkWorkAsync(
        SilexGisDbContext db,
        PhotoBulkRequest request,
        IReadOnlyList<long> addTagIds,
        IReadOnlyCollection<Guid> readableIds,
        AttachedEntityType? attachType,
        CancellationToken ct)
    {
        var ids = readableIds.ToList();

        // Tracked only when the request changes the document row itself; otherwise read as data.
        var documentRows = request.Delete == true || request.Visibility is not null
            ? db.Documents.Where(d => ids.Contains(d.Id))
            : db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id));
        var documents = await documentRows.ToDictionaryAsync(d => d.Id, ct);

        // The file a document serves is the first of its current revision. Tracked, because a
        // rotation is written on it.
        var files = await (from file in db.StoredFiles
                           join version in db.DocumentVersions.AsNoTracking()
                               on file.DocumentVersionId equals version.Id
                           where version.IsCurrent && ids.Contains(version.DocumentId)
                           select new { version.DocumentId, File = file })
            .ToListAsync(ct);
        var currentFiles = files
            .GroupBy(f => f.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.File.CreatedAt).ThenBy(f => f.File.Id).First().File);
        var fileIds = currentFiles.Values.Select(f => (Guid?)f.Id).ToList();

        var tagged = new HashSet<(long, Guid)>();
        if (addTagIds.Count > 0 && fileIds.Count > 0)
        {
            var existing = await db.Taggings.AsNoTracking()
                .Where(t => t.EntityType == AttachedEntityType.StoredFile
                    && fileIds.Contains(t.EntityId)
                    && addTagIds.Contains(t.TagId))
                .Select(t => new { t.TagId, FileId = t.EntityId!.Value })
                .ToListAsync(ct);
            tagged = [.. existing.Select(t => (t.TagId, t.FileId))];
        }

        var removing = request.RemoveTagIds ?? [];
        var removable = (removing.Count > 0 && fileIds.Count > 0
                ? await db.Taggings
                    .Where(t => removing.Contains(t.TagId)
                        && t.EntityType == AttachedEntityType.StoredFile
                        && fileIds.Contains(t.EntityId))
                    .ToListAsync(ct)
                : [])
            .ToLookup(t => t.EntityId!.Value);

        var attached = new HashSet<Guid>();
        if (request.AttachEntityId is { } targetId && fileIds.Count > 0)
        {
            var attachedFiles = await db.Attachments.AsNoTracking()
                .Where(a => fileIds.Contains(a.FileId)
                    && (attachType == null ? a.FeatureId == targetId : a.EntityId == targetId))
                .Select(a => a.FileId)
                .ToListAsync(ct);
            attached = [.. attachedFiles];
        }

        return new BulkWork(documents, currentFiles, tagged, removable, attached);
    }

    /// <summary>Applies one bulk request to one photograph. Tracked, not saved.</summary>
    private static void Apply(
        SilexGisDbContext db,
        PhotoBulkRequest request,
        IReadOnlyList<long> addTagIds,
        Document document,
        StoredFile? file,
        BulkWork work,
        AttachedEntityType? attachType,
        Guid userId)
    {
        if (request.Delete == true)
        {
            document.MarkDeleted(DateTimeOffset.UtcNow, userId);
            return;
        }

        if (request.Visibility is { } visibility)
        {
            document.Visibility = visibility;
        }

        if (file is null)
        {
            return;
        }

        if (request.RotateQuarterTurns is { } turns && turns % PhotoOrientation.Turns != 0)
        {
            file.OrientationQuarterTurns = PhotoOrientation.Rotate(file.OrientationQuarterTurns, turns);
        }

        foreach (var tagId in addTagIds)
        {
            if (work.Tagged.Add((tagId, file.Id)))
            {
                db.Taggings.Add(new Tagging
                {
                    TagId = tagId,
                    EntityType = AttachedEntityType.StoredFile,
                    EntityId = file.Id,
                    AddedBy = userId,
                });
            }
        }

        db.Taggings.RemoveRange(work.Removable[file.Id]);

        if (request.AttachEntityId is { } targetId && work.AttachedFiles.Add(file.Id))
        {
            db.Attachments.Add(new Attachment
            {
                FileId = file.Id,
                FeatureId = attachType is null ? targetId : null,
                EntityType = attachType,
                EntityId = attachType is null ? null : targetId,
                Role = AttachmentRole.PhotoSurface,
                AddedBy = userId,
            });
        }
    }

    /// <summary>
    /// The photograph behind an id, when this caller may read it and it is one.
    /// </summary>
    private static async Task<Document?> ReadableAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var content = await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (content?.File.Kind != FileKind.Image)
        {
            return null;
        }

        return await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content.File, ct)
            ? document
            : null;
    }

    /// <summary>Whether a bulk request changes the photographs themselves, not only an album.</summary>
    private static bool ChangesThePhotographs(PhotoBulkRequest request) =>
        request.AddTagIds is { Count: > 0 }
        || request.RemoveTagIds is { Count: > 0 }
        || request.Visibility is not null
        || request.RotateQuarterTurns is not null
        || request.AttachEntityId is not null
        || request.Delete == true;

    private static async Task<bool> WritableAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Document document, CancellationToken ct)
    {
        var content = await DocumentQueries.CurrentFileAsync(db, document.Id, ct);
        return await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content?.File, ct);
    }

    /// <summary>
    /// Turns documents into responses, resolving the one question that is decided per caller
    /// rather than per row: which of these positions may be disclosed.
    /// </summary>
    /// <remarks>
    /// Asked once for the whole page. A photograph hanging on two features — one open, one
    /// guarded — has to be decided over everything it hangs on at once, because the bytes do not
    /// care which row was followed to reach them.
    /// </remarks>
    private static async Task<IReadOnlyList<PhotoDto>> ProjectAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
        IReadOnlyList<Document> documents,
        CancellationToken ct)
    {
        var rows = await PhotographReads.RowsAsync(db, documents, ct);
        var reach = await disclosure.ReachAsync(ctx, [.. rows.Select(r => r.File.Id)], ct);
        return [.. rows.Select(r => r.ToDto(tokens, reach))];
    }
}
