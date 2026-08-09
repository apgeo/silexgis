// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Api.Features.Import;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Api.Features.Photos;

/// <summary>
/// A photograph and everything a response about it is built from, gathered once so a listing
/// does not fetch the same rows per item.
/// </summary>
public sealed record PhotoRow(
    Document Document,
    StoredFile File,
    PhotoDetails? Details,
    string? PhotographerLabel);

/// <summary>
/// One place that turns a photograph into what a caller is shown.
///
/// <para>
/// Shared because there are now five surfaces showing the same picture — the gallery, one
/// photograph's own page, an album, a shared album, the public gallery — and the two things
/// that must never differ between them are which URL hands over renderings and which one hands
/// over the upload. A surface that built its own would eventually build a different one.
/// </para>
/// </summary>
internal static class PhotoMapping
{
    /// <summary>The rendering a grid tile loads.</summary>
    public const int ThumbnailSize = 480;

    /// <summary>
    /// The rendering the full-screen viewer loads. Deliberately far past a screen's width,
    /// because it is what somebody zooming into a photograph is looking at.
    /// </summary>
    public const int PreviewSize = 2400;

    /// <summary>
    /// Builds the response for one photograph.
    /// </summary>
    /// <param name="mayHaveOriginal">
    /// Whether this caller may be handed the stored bytes. Defaults to no, and the default is
    /// the point: a photograph's GPS fix is a position rather than a fact about one, so a call
    /// site that has not resolved the caller's right to place what the picture shows must not
    /// hand out the original — nor print the coordinates beside it, which is why both are
    /// gated on this one answer.
    /// </param>
    public static PhotoDto ToDto(
        this PhotoRow row, IFileAccessTokenService tokens, bool mayHaveOriginal)
    {
        var file = row.File;
        var delivery = file.Geom is null || mayHaveOriginal
            ? FileDelivery.Full
            : FileDelivery.DerivativesOnly;
        var token = tokens.CreateToken(file.Id, delivery);

        var exif = PhotoExif.FromMetadata(file.Metadata);
        var (width, height) = SizeOf(exif, file.OrientationQuarterTurns);

        return new PhotoDto(
            row.Document.Id,
            file.Id,
            row.Document.Title,
            file.OriginalName,
            file.SizeBytes,
            width,
            height,
            file.OrientationQuarterTurns,
            row.Document.Visibility,
            row.Document.CavingGroupId,
            file.ContentCreatedAt,
            row.Document.CreatedAt,
            Rendering(file.Id, token, ThumbnailSize),
            Rendering(file.Id, token, PreviewSize),
            $"/api/v1/files/{file.Id}/content?token={Uri.EscapeDataString(token)}",
            delivery == FileDelivery.Full,
            CreditOf(row),
            exif.IsEmpty ? null : exif,
            // Gated on the same answer as the bytes, and it has to be: the original carries this
            // fix inside it, so a response that withheld the file and printed its coordinates
            // beside it would be withholding nothing at all.
            mayHaveOriginal ? PositionOf(file) : null);
    }

    /// <summary>
    /// The dimensions a rendering will actually have — the recorded turn already applied.
    /// </summary>
    /// <remarks>
    /// What a grid needs before it has any bytes. A tile sized from the stored dimensions alone
    /// is the wrong shape for every rotated picture in the archive, and a grid that reflows once
    /// the images arrive is the thing lazy loading is supposed to avoid.
    /// </remarks>
    private static (int? Width, int? Height) SizeOf(PhotoExif exif, int quarterTurns)
    {
        if (exif.WidthPixels is not { } width || exif.HeightPixels is not { } height)
        {
            return (null, null);
        }

        var (w, h) = PhotoOrientation.Apply(width, height, quarterTurns);
        return (w, h);
    }

    private static PhotoCreditDto CreditOf(PhotoRow row) => new(
        row.Details?.PhotographerCaverId,
        // The caver's label when one is credited, and the free-text name only when nobody on the
        // roster is. Two names for one picture would be a picture with two photographers.
        row.PhotographerLabel ?? row.Details?.PhotographerName,
        row.Details?.Caption,
        row.Details?.LicenceCode,
        row.Details?.PlaceName,
        row.Details?.InPublicGallery ?? false);

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

    /// <summary>
    /// A rendering's URL. Never the upload: this application draws these bytes and strips every
    /// metadata profile out of them, so they show what the picture shows and carry nothing it
    /// carried — which is what makes them safe to offer to somebody who may not have the file.
    /// </summary>
    private static string Rendering(Guid fileId, string token, int size) =>
        $"/api/v1/files/{fileId}/thumbnail?size={size}&token={Uri.EscapeDataString(token)}";
}
