// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Photos;

/// <summary>
/// A photograph as somebody who is not signed in sees it.
///
/// <para>
/// A type of its own rather than the ordinary photograph response with fields blanked out, and
/// that is the whole point of it. What an anonymous visitor may be shown is a picture, a
/// caption and a credit — and nothing that could place it. Written as a separate shape, a
/// position or a delivery URL for the upload cannot leak into this surface by somebody adding a
/// field to the response every other surface shares; it is impossible by construction rather
/// than by care, which is the same reason the position was split from the camera facts.
/// </para>
/// </summary>
/// <param name="ThumbnailUrl">A rendering. There is deliberately no URL here for the upload.</param>
public sealed record PublicPhotoDto(
    Guid DocumentId,
    string Title,
    string? Caption,
    string? PhotographerName,
    string? LicenceCode,
    string? PlaceName,
    int? Width,
    int? Height,
    string ThumbnailUrl,
    string PreviewUrl);

/// <summary>An album as a share link opens it.</summary>
public sealed record PublicAlbumDto(
    string Title,
    string? Description,
    IReadOnlyList<PublicPhotoDto> Photos);

/// <summary>
/// The two surfaces an anonymous visitor can reach: the installation's curated gallery, and one
/// album somebody minted a link for.
/// </summary>
/// <remarks>
/// <para>
/// Both are on the documented anonymous allow-list, and both are narrow by construction. The
/// curated gallery shows only what an administrator explicitly published — not everything a
/// visibility band happens to admit, because publishing to the internet is a different decision
/// from making something readable inside the installation, taken by a different person. The
/// share link shows one album and only its pictures.
/// </para>
/// <para>
/// Neither ever hands over an upload, a capture position, or a path from a photograph to the
/// cave it was taken at. That is enforced by what these routes build rather than by what they
/// remember to withhold: the response type has no field for any of it, and the delivery tokens
/// are minted for renderings only, which the delivery route honours whatever a caller does with
/// the URL afterwards.
/// </para>
/// </remarks>
public static class PublicPhotoEndpoints
{
    /// <summary>
    /// How many pictures a share link or the public gallery hands over at once. A public
    /// surface is not a bulk export, and a link somebody put on a website should not become the
    /// cheapest way to enumerate an archive.
    /// </summary>
    private const int MaxPublic = 200;

    public static RouteGroupBuilder MapPublicPhotoEndpoints(this RouteGroupBuilder api)
    {
        var publicPhotos = api.MapGroup("/public").WithTags("Photos");

        publicPhotos.MapGet("/photos", GalleryAsync).AllowAnonymous()
            .WithSummary("The installation's curated public gallery: photographs an administrator published.");
        publicPhotos.MapGet("/albums/{token}", SharedAlbumAsync).AllowAnonymous()
            .WithSummary("One album, opened by its share link; renderings only.");

        return api;
    }

    /// <summary>
    /// The curated gallery. Only photographs somebody marked, and only as renderings.
    /// </summary>
    private static async Task<Ok<PagedResult<PublicPhotoDto>>> GalleryAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        // No visibility filter, and none is wanted: the flag *is* the decision. Reading the
        // documents' own bands here as well would mean an administrator publishing something
        // and it silently not appearing, which is the kind of gap that gets worked around by
        // making the picture public — a worse outcome than the one being avoided.
        var published = from details in db.PhotoDetails.AsNoTracking()
                        join document in db.Documents.AsNoTracking() on details.DocumentId equals document.Id
                        where details.InPublicGallery
                        orderby document.CreatedAt descending, document.Id descending
                        select new { Document = document, Details = details };

        var (p, size) = Paging.Normalize(page, Math.Min(pageSize ?? 50, MaxPublic));
        var rows = await published.ToPagedAsync(p, size, x => new { x.Document, x.Details }, ct);

        var items = await ProjectAsync(
            db, tokens, [.. rows.Items.Select(r => r.Document)], ct);
        return TypedResults.Ok(new PagedResult<PublicPhotoDto>(
            items, rows.Page, rows.PageSize, rows.TotalItems));
    }

    /// <summary>
    /// An album opened by its link.
    /// </summary>
    /// <remarks>
    /// A link for signed-in callers only is resolved here as not found rather than as a
    /// redirect: this route is the anonymous one, and telling an anonymous caller that a
    /// particular token exists but needs an account is itself an answer about the token.
    /// </remarks>
    private static async Task<Results<Ok<PublicAlbumDto>, ProblemHttpResult>> SharedAlbumAsync(
        string token,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ApiProblems.NotFound(AlbumEndpoints.NotFoundCode);
        }

        var hash = AlbumEndpoints.HashToken(token);
        var share = await db.AlbumShares.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TokenHash == hash && s.RevokedAt == null && s.Mode == FeatureShareMode.Public, ct);
        if (share is null)
        {
            // Revoked, unknown or sign-in-only all answer the same way: a token is the whole of
            // the caller's claim, and distinguishing the reasons would say which tokens exist.
            return ApiProblems.NotFound(AlbumEndpoints.NotFoundCode);
        }

        var album = await db.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Id == share.AlbumId, ct);
        if (album is null)
        {
            return ApiProblems.NotFound(AlbumEndpoints.NotFoundCode);
        }

        var documents = await (from item in db.AlbumItems.AsNoTracking()
                               join document in db.Documents.AsNoTracking() on item.DocumentId equals document.Id
                               where item.AlbumId == album.Id
                               orderby item.SortOrder, item.Id
                               select document)
            .Take(MaxPublic)
            .ToListAsync(ct);

        var photos = await ProjectAsync(db, tokens, documents, ct);
        return TypedResults.Ok(new PublicAlbumDto(album.Title, album.Description, photos));
    }

    /// <summary>
    /// Builds the anonymous responses.
    /// </summary>
    /// <remarks>
    /// Every token minted here is for renderings only. That is what makes these routes safe
    /// regardless of what the pictures are of: the delivery route refuses the stored bytes for
    /// such a token however the URL is used, so a photograph carrying a GPS fix cannot leave by
    /// this door even if one is published by mistake.
    /// </remarks>
    private static async Task<IReadOnlyList<PublicPhotoDto>> ProjectAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IReadOnlyList<Document> documents,
        CancellationToken ct)
    {
        var rows = await PhotographReads.RowsAsync(db, documents, ct);

        return
        [
            .. rows.Select(row =>
            {
                var token = tokens.CreateToken(row.File.Id, FileDelivery.DerivativesOnly);
                var exif = Domain.Documents.PhotoExif.FromMetadata(row.File.Metadata);
                var (width, height) = exif.WidthPixels is { } w && exif.HeightPixels is { } h
                    ? Domain.Documents.PhotoOrientation.Apply(w, h, row.File.OrientationQuarterTurns)
                    : (0, 0);

                return new PublicPhotoDto(
                    row.Document.Id,
                    row.Document.Title,
                    row.Details?.Caption,
                    row.PhotographerLabel ?? row.Details?.PhotographerName,
                    row.Details?.LicenceCode,
                    // A place name is a caption somebody typed, not a position — the two are
                    // separate columns precisely so this one can be shown here.
                    row.Details?.PlaceName,
                    width == 0 ? null : width,
                    height == 0 ? null : height,
                    Rendering(row.File.Id, token, PhotoMapping.ThumbnailSize),
                    Rendering(row.File.Id, token, PhotoMapping.PreviewSize));
            }),
        ];
    }

    private static string Rendering(Guid fileId, string token, int size) =>
        $"/api/v1/files/{fileId}/thumbnail?size={size}&token={Uri.EscapeDataString(token)}";
}
