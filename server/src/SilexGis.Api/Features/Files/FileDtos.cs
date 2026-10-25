// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Api.Features.Import;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Features.Files;

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

/// <summary>
/// Upload limits this installation applies, and how much room the caller has left.
/// Published so a client checks a file before transferring it rather than after, and so no
/// client build carries a number that could disagree with the server's.
/// </summary>
/// <param name="MaxUploadBytes">Largest single file accepted.</param>
/// <param name="RemainingBytes">
/// How much this caller may still store, or null when nothing limits them. Stated up front
/// because a quota discovered at the end of a 400 MB transfer is the same as no warning at
/// all — and because "you have 2 GB left" is a thing somebody can act on before they start.
/// </param>
/// <param name="QuotaBytes">Their total allowance, or null when they have none.</param>
/// <param name="UsedBytes">How much they already hold. Null when nothing is counting.</param>
/// <param name="AcceptedExtensions">
/// What this installation takes, lower-case and dotted. Empty means everything, which is the
/// default: an archive whose purpose is holding whatever a caving club has accumulated should
/// not have a list of formats somebody has to remember to extend.
/// </param>
/// <param name="RefusedExtensions">What it will not take, whatever the list above says.</param>
/// <param name="ChunkBytes">
/// The piece size a resumable upload should send. Advice — the server accepts whatever
/// arrives — but following it keeps a dropped connection cheap.
/// </param>
/// <param name="ResumableThresholdBytes">
/// Above this size a client should open a resumable session rather than send one request.
/// Below it the extra round trips cost more than the resumption is worth.
/// </param>
/// <param name="ArchiveExtensions">
/// Archives this installation can expand server-side. Empty would mean the feature is off;
/// it is what the upload dialog offers "expand this" for.
/// </param>
/// <param name="ConversionAvailable">
/// Whether this installation runs the optional service that lays an office document out as a
/// portable one. A standing fact about the installation, false on most small ones, and said
/// here so a page can leave out a choice that could only be refused — a generated write-up
/// offered as a PDF, for one — rather than offer it and apologise.
/// </param>
public sealed record FileConfigDto(
    long MaxUploadBytes,
    long? RemainingBytes,
    long? QuotaBytes,
    long? UsedBytes,
    IReadOnlyList<string> AcceptedExtensions,
    IReadOnlyList<string> RefusedExtensions,
    int ChunkBytes,
    long ResumableThresholdBytes,
    IReadOnlyList<string> ArchiveExtensions,
    bool ConversionAvailable);

/// <summary>
/// Whether content with this hash is already here, as far as this caller is concerned.
/// </summary>
/// <param name="Duplicate">
/// True only when a document holding exactly these bytes exists <em>and</em> this caller may
/// read it. A copy they may not read is reported as no copy at all — telling them it exists
/// is the disclosure the whole rule is there to prevent — and they get their own copy, with
/// the pair recorded for an administrator who can see both.
/// </param>
/// <param name="DocumentId">The document already holding it, when there is one to name.</param>
/// <param name="Title">Its title, so the warning can say what it collides with.</param>
public sealed record DuplicateCheckDto(bool Duplicate, Guid? DocumentId, string? Title);

/// <summary>How far a resumable upload has got.</summary>
/// <param name="Id">The session, named in every later piece.</param>
/// <param name="ReceivedBytes">
/// The offset to send from. A resuming client asks for this and continues; it is read back
/// from the stored bytes rather than remembered, so it never describes bytes that did not
/// survive.
/// </param>
/// <param name="DeclaredSizeBytes">What the file was said to be, and what completion measures against.</param>
/// <param name="ChunkBytes">The piece size to send.</param>
/// <param name="ExpiresAt">
/// When the session stops being resumable and its bytes are swept up. For staged bytes, when
/// they go if no sync upload has adopted them.
/// </param>
/// <param name="StagedForSync">Whether the transfer was opened to stage bytes for a sync upload.</param>
/// <param name="StagedAt">
/// When staged bytes were completed and described; absent while they are still arriving. A
/// phone asking whether its staged bytes are still waiting reads this, and an answer of
/// "not found" means they are gone and must be sent again.
/// </param>
/// <param name="Sha256">The hash the server computed for staged bytes, once they are staged.</param>
public sealed record UploadSessionDto(
    Guid Id,
    long ReceivedBytes,
    long DeclaredSizeBytes,
    int ChunkBytes,
    DateTimeOffset ExpiresAt,
    bool StagedForSync,
    DateTimeOffset? StagedAt,
    string? Sha256);

/// <summary>
/// Bytes a phone sent ahead of the sync upload that will name them, completed and described
/// but not yet anybody's document.
/// </summary>
/// <param name="Id">What the upload names to adopt them — the transfer's own id.</param>
/// <param name="FileName">The name the transfer was opened with.</param>
/// <param name="SizeBytes">How many bytes are held.</param>
/// <param name="Sha256">
/// The lowercase hex SHA-256 the server computed. The upload's document row must state the
/// same hash, so a phone can compare it with its own before it sends the row.
/// </param>
/// <param name="MimeType">The type sniffed from the content, not taken from the name.</param>
/// <param name="Kind">What kind of file the content is.</param>
/// <param name="StagedAt">When the bytes were staged.</param>
/// <param name="ExpiresAt">When they are swept if no upload has adopted them.</param>
public sealed record StagedUploadDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string Sha256,
    string MimeType,
    FileKind Kind,
    DateTimeOffset StagedAt,
    DateTimeOffset ExpiresAt);

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
