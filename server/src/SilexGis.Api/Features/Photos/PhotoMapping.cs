// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Api.Features.Photos;

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
    /// <param name="reach">
    /// What this caller may be handed of the picture and told about where it was taken,
    /// resolved for the page the row came in. Required: a call site that has not resolved it
    /// has not answered the question the delivery address carries.
    /// </param>
    public static PhotoDto ToDto(
        this PhotoRow row, IFileAccessTokenService tokens, PhotoReach reach)
    {
        var file = row.File;
        var delivery = FileDeliveries.Of(reach.OriginalOf(file));
        var token = tokens.CreateToken(file.Id, delivery);

        var exif = PhotoExif.FromMetadata(file.Metadata);
        var (width, height) = SizeOf(file, exif);

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
            delivery != FileDelivery.DerivativesOnly,
            CreditOf(row),
            exif.IsEmpty ? null : exif,
            // Gated on the disclosure answer, never on the switch: the switch decides what travels
            // inside the file, and a reader who may not be told where the picture was taken is
            // not told it here either, whichever bytes the address above opens.
            reach.PositionDisclosable(file.Id) ? PositionOf(file) : null,
            delivery == FileDelivery.LocationFree);
    }

    /// <summary>
    /// The dimensions a rendering will actually have — the orientation code and the recorded turn
    /// both applied.
    /// </summary>
    /// <remarks>
    /// What a grid needs before it has any bytes. A tile sized from the stored dimensions alone
    /// is the wrong shape for every rotated picture in the archive, and a grid that reflows once
    /// the images arrive is the thing lazy loading is supposed to avoid. The stated size is the
    /// stored one, before any orientation code: a phone's portrait photograph is usually stored
    /// on its side with a code saying so, and is drawn upright, so the code has to be applied
    /// here as the renderer applies it. The code is the one the renderer obeys, read when the
    /// file was written; a file written before that was recorded falls back to its camera
    /// profile's, which is the same code for every camera's JPEG.
    /// </remarks>
    internal static (int? Width, int? Height) SizeOf(StoredFile file, PhotoExif exif)
    {
        if (exif.WidthPixels is not { } width || exif.HeightPixels is not { } height)
        {
            return (null, null);
        }

        var (w, h) = PhotoOrientation.UprightSize(
            width, height, file.PixelOrientation ?? exif.Orientation, file.OrientationQuarterTurns);
        return (w, h);
    }

    private static PhotoCreditDto CreditOf(PhotoRow row) => new(
        row.PhotographerCaverId,
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
