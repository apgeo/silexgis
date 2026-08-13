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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

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

    public static RouteGroupBuilder MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        var photos = api.MapGroup("/photos").WithTags("Photos");

        photos.MapGet("/", ListAsync)
            .WithSummary("Photographs the caller may see, newest first, narrowed by cave, feature, trip, caver, tag, album, camera, date or map extent.");
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
    private static async Task<Results<Ok<PagedResult<PhotoDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        [AsParameters] PhotoQuery query,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        PhotoPositionDisclosure disclosure,
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

        var photographs = (await PhotographReads.VisiblePhotographsAsync(db, ctx, ct)).Narrow(db, query);

        if (!string.IsNullOrWhiteSpace(query.Bbox))
        {
            if (!Bbox.TryParse(query.Bbox, out var box))
            {
                return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
            }

            var inExtent = await PhotoQueries.InExtentAsync(
                db, ctx, disclosure, photographs, box.ToPolygon(), ct);
            photographs = photographs.Where(d => inExtent.Contains(d.Id));
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
        return TypedResults.Ok(new PagedResult<PhotoDto>(
            items, documents.Page, documents.PageSize, documents.TotalItems));
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

        var result = new List<PhotoDuplicateGroupDto>();
        foreach (var group in members.GroupBy(m => m.Sha256).Where(g => g.Count() > 1))
        {
            var photos = await ProjectAsync(
                db, ctx, tokens, disclosure, [.. group.Select(m => m.Document)], ct);
            if (photos.Count > 1)
            {
                result.Add(new PhotoDuplicateGroupDto(group.Key, photos));
            }
        }

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// What the caller deleted and can still get back.
    /// </summary>
    /// <remarks>
    /// Their own, and a full administrator's view of everybody's. Deliberately not "everything
    /// you may write": a restore list is a list of things that are gone, and the useful question
    /// is "what did I just delete" rather than "what has anyone deleted anywhere".
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<DeletedPhotoDto>>, UnauthorizedHttpResult>> DeletedAsync(
        SilexGisDbContext db,
        IOptions<DocumentRetentionOptions> retention,
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

        var window = retention.Value.Retention;
        var (p, size) = Paging.Normalize(page, pageSize);

        // Through the model-wide filter, which hides exactly the rows this list is about.
        var deleted = db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && (ctx.IsFullAdmin || d.DeletedByUserId == ctx.UserId))
            .OrderByDescending(d => d.DeletedAt)
            .ThenByDescending(d => d.Id);

        return TypedResults.Ok(await deleted.ToPagedAsync(
            p,
            size,
            d => new DeletedPhotoDto(
                d.Id,
                d.Title,
                d.DeletedAt!.Value,
                d.DeletedAt!.Value + window,
                d.DeletedByUserId,
                null),
            ct));
    }

    /// <summary>Puts a deleted photograph back, if its window has not run out.</summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RestoreAsync(
        Guid documentId,
        SilexGisDbContext db,
        IOptions<DocumentRetentionOptions> retention,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Seen through the filter, and narrowed to what this caller deleted: the ordinary read
        // rule cannot answer for a row it hides, and "who deleted it" is the claim that stands
        // in for it. A full administrator can reach any of them.
        var document = await db.Documents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                d => d.Id == documentId
                    && d.DeletedAt != null
                    && (ctx.IsFullAdmin || d.DeletedByUserId == ctx.UserId),
                ct);
        if (document is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!SoftDeleteRules.IsRestorable(document.DeletedAt!.Value, DateTimeOffset.UtcNow, retention.Value.Retention))
        {
            // Past the window the bytes may already be gone, so a restore would produce a row
            // pointing at nothing — worse than refusing.
            return ApiProblems.Conflict(
                SoftDeleteRules.NotDeletedCode, "The restore window for this photograph has passed.");
        }

        document.DeletedAt = null;
        document.DeletedByUserId = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
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

        AttachedEntityType? attachType = null;
        if (!string.IsNullOrWhiteSpace(request.AttachEntityType))
        {
            if (request.AttachEntityId is not { } targetId
                || !AttachmentTargets.TryParse(request.AttachEntityType, out attachType))
            {
                return ApiProblems.BadRequest("attachment.entity_type_unknown");
            }

            // Attaching hands the picture to everyone who can read the target, so it takes write
            // on the target — asked once for the whole request, because it does not vary by
            // picture.
            var target = new AttachmentTarget(attachType, targetId);
            if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
            {
                return ApiProblems.Forbidden();
            }
        }

        if (request.AddToAlbumId is { } albumId
            && !await AlbumAccess.CanWriteAsync(db, access, ctx, albumId, ct))
        {
            return ApiProblems.Forbidden(AlbumEndpoints.WriteForbiddenCode);
        }

        var changed = new List<Guid>();
        var refused = new Dictionary<Guid, string>();

        foreach (var documentId in request.DocumentIds.Distinct())
        {
            var subject = await ReadableAsync(db, access, ctx, documentId, ct);
            if (subject is null)
            {
                // Never an existence oracle: unreadable is answered as absent.
                continue;
            }

            if (!await WritableAsync(db, access, ctx, subject, ct))
            {
                refused[documentId] = WriteForbiddenCode;
                continue;
            }

            await ApplyAsync(db, request, subject, attachType, ctx.UserId, ct);
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
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new PhotoBulkResultDto(changed, refused));
    }

    /// <summary>Applies one bulk request to one photograph. Tracked, not saved.</summary>
    private static async Task ApplyAsync(
        SilexGisDbContext db,
        PhotoBulkRequest request,
        Document document,
        AttachedEntityType? attachType,
        Guid userId,
        CancellationToken ct)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(
            f => db.DocumentVersions.Any(v =>
                v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == document.Id),
            ct);

        if (request.Delete == true)
        {
            var tracked = await db.Documents.FirstAsync(d => d.Id == document.Id, ct);
            tracked.DeletedAt = DateTimeOffset.UtcNow;
            tracked.DeletedByUserId = userId;
            return;
        }

        if (request.Visibility is { } visibility)
        {
            var tracked = await db.Documents.FirstAsync(d => d.Id == document.Id, ct);
            tracked.Visibility = visibility;
        }

        if (file is null)
        {
            return;
        }

        if (request.RotateQuarterTurns is { } turns && turns % PhotoOrientation.Turns != 0)
        {
            file.OrientationQuarterTurns = PhotoOrientation.Rotate(file.OrientationQuarterTurns, turns);
        }

        foreach (var tagId in request.AddTagIds ?? [])
        {
            var already = await db.Taggings.AnyAsync(
                t => t.TagId == tagId
                    && t.EntityType == AttachedEntityType.StoredFile
                    && t.EntityId == file.Id,
                ct);
            if (!already)
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

        var removing = request.RemoveTagIds ?? [];
        if (removing.Count > 0)
        {
            var going = await db.Taggings
                .Where(t => removing.Contains(t.TagId)
                    && t.EntityType == AttachedEntityType.StoredFile
                    && t.EntityId == file.Id)
                .ToListAsync(ct);
            db.Taggings.RemoveRange(going);
        }

        if (request.AttachEntityId is { } targetId)
        {
            var already = await db.Attachments.AnyAsync(
                a => a.FileId == file.Id
                    && (attachType == null ? a.FeatureId == targetId : a.EntityId == targetId),
                ct);
            if (!already)
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
        var disclosable = await disclosure.DisclosableIdsAsync(ctx, [.. rows.Select(r => r.File.Id)], ct);
        return [.. rows.Select(r => r.ToDto(tokens, disclosable.Contains(r.File.Id)))];
    }
}
