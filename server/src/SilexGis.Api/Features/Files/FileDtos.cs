// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Api.Features.Import;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Features.Files;

/// <summary>
/// File metadata plus freshly minted delivery URLs. The URLs embed a short-lived
/// capability token — clients use them as-is in img/src and download links and refetch
/// the metadata when a token expires.
/// </summary>
/// <param name="MayDownloadOriginal">
/// Whether <see cref="ContentUrl"/> will actually hand over the stored bytes. It is false
/// for a photo whose own coordinates this caller may not be given: the delivery route
/// refuses such a request whatever the response said, because a token carries no identity
/// and that route is the only place the decision can be honoured. The field exists so a
/// client can label a control it must not offer, rather than discovering the refusal by
/// following the link — the URL itself stays present and answers as a missing file, which
/// is what every other withheld thing answers.
/// </param>
/// <param name="PagesUrl">
/// The delivery URL of the file whose pages can be drawn, or null when nothing here has pages
/// to draw. It is this file for a portable document, and its converted copy for an office
/// document that has one — which is why the server names it rather than leaving a client to
/// work out from a media type which file to ask about. A page picture is fetched by asking this
/// URL's route for a page, carrying the token it already has.
/// </param>
/// <param name="PageCount">
/// How many pages the thing <paramref name="PagesUrl"/> draws actually has, once something has
/// counted them. Null means nobody has counted yet, which is not the same as one page.
/// </param>
/// <param name="Conversion">
/// How far turning this file into something with pages has got. The state a client has to act
/// on is the one that says this installation has no converter: the document is fine, nothing
/// here can lay it out, and that sentence is different from every failure sentence.
/// </param>
/// <param name="Photo">
/// What the picture states about how it was taken — camera, lens, exposure, orientation — and
/// when it was taken, with the zone the camera recorded. Present for anyone who may read the
/// file: how a photograph was taken is not a position and is not protected.
/// </param>
/// <param name="Position">
/// Where the picture was taken, and how that was arrived at. Null for anything without a
/// position <em>and</em> for a caller who may not be given this one — the same answer as
/// <paramref name="MayDownloadOriginal"/>, decided by the same rule, because a photograph's fix
/// is a position rather than a fact about one. Splitting it from <paramref name="Photo"/> above
/// is what lets a viewer be shown the camera and not the coordinates.
/// </param>
public sealed record FileDto(
    Guid Id,
    Guid DocumentId,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Sha256,
    FileKind Kind,
    int VersionNumber,
    DateOnly? DocumentDate,
    DateTimeOffset CreatedAt,
    string ContentUrl,
    string? ThumbnailUrl,
    bool MayDownloadOriginal,
    string? PagesUrl,
    int? PageCount,
    ConversionState Conversion,
    DateTimeOffset? ContentCreatedAt,
    PhotoExif? Photo,
    PhotoPositionDto? Position);

/// <summary>
/// Upload limits this installation applies. Published so a client checks a file before
/// transferring it rather than after, and so no client build carries a number that could
/// disagree with the server's.
/// </summary>
public sealed record FileConfigDto(long MaxUploadBytes);

/// <summary>
/// The extracted words of one page. The text is what a reader wrote into the search index,
/// character for character — a selection is measured against this, so anything that
/// normalised it here would move every offset composed from it.
/// </summary>
public sealed record PageTextDto(int Page, string Text);

/// <summary>One revision of a document (newest first). Superseded ones are editor-only.</summary>
public sealed record FileVersionDto(
    Guid Id,
    int VersionNumber,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    Guid? UploadedBy,
    string? UploaderName,
    DateTimeOffset CreatedAt,
    string ContentUrl,
    bool IsHead);

internal static class FileMapping
{
    /// <summary>
    /// A file plus the revision it belongs to: version number and document date are
    /// version detail, so they are read from there rather than duplicated per file.
    /// The document id comes from the same revision — a file id changes with every new
    /// version, so it is the only identifier that can name the thing being looked at.
    /// </summary>
    /// <param name="mayHaveOriginal">
    /// Whether this caller may be handed the stored bytes of a photo that records where it
    /// was taken. Defaults to no, and the default is the point: a photo's GPS fix is a
    /// position rather than a fact about one, so a mint site that has not resolved the
    /// caller's right to place what the photo shows must not hand out the original — while
    /// everything without a position of its own is unaffected, whatever this says.
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
        bool mayHaveOriginal = false,
        StoredFile? rendition = null)
    {
        var delivery = f.Geom is null || mayHaveOriginal ? FileDelivery.Full : FileDelivery.DerivativesOnly;
        var token = tokens.CreateToken(f.Id, delivery);

        // A portable document draws its own pages; anything else draws them only through the
        // copy something made of it. Both cases end in a delivery URL for one file, which is
        // what a client needs and all it needs.
        var pagesFile = PageRenderService.CanRender(f.MimeType) ? f
            : rendition is not null && PageRenderService.CanRender(rendition.MimeType) ? rendition
            : null;
        var pagesUrl = pagesFile is null ? null
            : pagesFile.Id == f.Id ? ContentUrl(f.Id, token)
            : ContentUrl(pagesFile.Id, tokens.CreateToken(pagesFile.Id, delivery));

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
            delivery == FileDelivery.Full,
            pagesUrl,
            (pagesFile ?? f).PageCount,
            f.Conversion,
            f.ContentCreatedAt,
            PhotoExifOf(f),
            // Gated on the same answer as the bytes, and it has to be: the original carries this
            // fix inside it, so a response that withheld the file and printed its coordinates
            // beside it would be withholding nothing at all.
            mayHaveOriginal ? PositionOf(f) : null);
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
