// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Common;

internal static class FileMapping
{
    /// <summary>
    /// A file plus the revision it belongs to: version number and document date are
    /// version detail, so they are read from there rather than duplicated per file.
    /// The document id comes from the same revision — a file id changes with every new
    /// version, so it is the only identifier that can name the thing being looked at.
    /// </summary>
    /// <param name="reach">
    /// What this caller may be handed of each file and told about its position, resolved for
    /// the batch the file came in. Required, because a mint site that has not resolved it has
    /// not answered the question the delivery address exists to carry.
    /// </param>
    /// <param name="rendition">
    /// The converted copy of this file, where one has been produced. Passed in rather than
    /// looked up here: which file's pages can be drawn is a fact the response has to state, and
    /// a mapping that fetched it would issue a query per row of every listing.
    /// </param>
    public static FileDto ToDto(
        this StoredFile f,
        DocumentVersion version,
        IFileAccessTokenService tokens,
        PhotoReach reach,
        StoredFile? rendition = null)
    {
        var delivery = FileDeliveries.Of(reach.OriginalOf(f));
        var token = tokens.CreateToken(f.Id, delivery);

        // A portable document draws its own pages; anything else draws them only through the
        // copy something made of it. Both cases end in a delivery URL for one file, which is
        // what a client needs and all it needs.
        var pagesFile = PageRenderService.CanRender(f.MimeType) ? f
            : rendition is not null && PageRenderService.CanRender(rendition.MimeType) ? rendition
            : null;
        var pagesUrl = pagesFile is null ? null
            : pagesFile.Id == f.Id ? ContentUrl(f.Id, token)
            // A location-free copy is made of pictures only, and a converted copy is never one, so
            // the pages of a converted copy are drawn under the renderings reach rather than
            // under a reach its content route would have nothing to serve for.
            : ContentUrl(pagesFile.Id, tokens.CreateToken(
                pagesFile.Id, delivery == FileDelivery.LocationFree ? FileDelivery.DerivativesOnly : delivery));

        return new FileDto(
            f.Id,
            version.DocumentId,
            f.OriginalName,
            f.MimeType,
            f.SizeBytes,
            f.Sha256,
            f.Kind,
            version.VersionNumber,
            version.DocumentDate,
            f.CreatedAt,
            ContentUrl(f.Id, token),
            f.Kind == FileKind.Image
                ? $"/api/v1/files/{f.Id}/thumbnail?size=480&token={Uri.EscapeDataString(token)}"
                : null,
            delivery != FileDelivery.DerivativesOnly,
            pagesUrl,
            (pagesFile ?? f).PageCount,
            f.Conversion,
            f.ContentCreatedAt,
            PhotoExifOf(f),
            // Gated on the disclosure answer, never on the switch: the switch decides what travels
            // inside the file, and a reader who may not be told where the photo was taken is not
            // told it here either, whichever bytes the address above opens.
            reach.PositionDisclosable(f.Id) ? PositionOf(f) : null,
            delivery == FileDelivery.LocationFree);
    }

    private static PhotoExif? PhotoExifOf(StoredFile f)
    {
        var exif = PhotoExif.FromMetadata(f.Metadata);
        return exif.IsEmpty ? null : exif;
    }

    private static PhotoPositionDto? PositionOf(StoredFile f) => f.Geom is null
        ? null
        : new PhotoPositionDto(
            f.Id,
            GeoJsonGeometry.From(f.Geom),
            f.PositionSource,
            f.AltitudeMeters,
            f.DirectionDegrees,
            f.DirectionIsMagnetic,
            f.PositionDop,
            PositionConfidence.Of(f.PositionDop));

    public static string ContentUrl(Guid fileId, string token) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(token)}";
}
