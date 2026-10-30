// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Common;

/// <summary>
/// File metadata plus freshly minted delivery URLs. The URLs embed a short-lived
/// capability token — clients use them as-is in img/src and download links and refetch
/// the metadata when a token expires.
/// </summary>
/// <param name="MayDownloadOriginal">
/// Whether <see cref="ContentUrl"/> will hand over the whole file. For a photo whose location
/// this caller may not have — or for every reader when the installation keeps no location in
/// photo files — that is the full-size picture with its location data taken out, not the
/// stored bytes. It is false only where no such copy can be made, a video or a format that
/// cannot be written back: the delivery route refuses such a request whatever the response
/// said, because a token carries no identity and that route is the only place the decision can
/// be honoured. The field exists so a client can label a control it must not offer, rather
/// than discovering the refusal by following the link — the URL itself stays present and
/// answers as a missing file, which is what every other withheld thing answers.
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
/// position <em>and</em> for a caller who may not be given this one — decided by the same
/// disclosure rule that decides whether the file goes out with its location inside it, because
/// a photograph's fix is a position rather than a fact about one. Splitting it from <paramref name="Photo"/> above
/// is what lets a viewer be shown the camera and not the coordinates.
/// </param>
/// <param name="LocationRemoved">
/// True when <see cref="ContentUrl"/> hands over a copy of the picture with its location data
/// taken out rather than the stored bytes, so a page can say so beside the picture. It is the
/// delivery decision itself, not a guess from the position above: a photograph with no fix still
/// travels as a rewritten copy for these readers.
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
    PhotoPositionDto? Position,
    bool LocationRemoved);
