// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Features.Import;
using SilexGis.Domain;
using SilexGis.Domain.Documents;

namespace SilexGis.Api.Features.Photos;

/// <summary>
/// One photograph as the gallery draws it.
/// </summary>
/// <param name="ThumbnailUrl">A small rendering for the grid; never the upload.</param>
/// <param name="PreviewUrl">
/// A large rendering for the viewer. Large enough to enlarge into, which the ordinary
/// thumbnail is not — a 60-megapixel panorama at 1200 pixels is unreadable the moment anybody
/// zooms.
/// </param>
/// <param name="Width">
/// The picture's dimensions <em>as rendered</em> — with any recorded turn already applied, so a
/// grid can lay a tile out in the right shape before the bytes arrive. Null when nothing has
/// read the dimensions.
/// </param>
/// <param name="Position">
/// Where it was taken, and null both for a picture with no position <em>and</em> for a caller
/// who may not be told this one. Decided by the same rule as the original bytes, because a
/// photograph's fix is a position rather than a fact about one.
/// </param>
/// <param name="Photo">
/// What the picture states about how it was taken — camera, lens, exposure. Shown to anyone who
/// may read the file: how a photograph was taken is not a position and is not protected.
/// </param>
public sealed record PhotoDto(
    Guid DocumentId,
    Guid FileId,
    string Title,
    string OriginalName,
    long SizeBytes,
    int? Width,
    int? Height,
    int OrientationQuarterTurns,
    Visibility Visibility,
    Guid? CavingGroupId,
    DateTimeOffset? TakenAt,
    DateTimeOffset CreatedAt,
    string ThumbnailUrl,
    string PreviewUrl,
    string ContentUrl,
    bool MayDownloadOriginal,
    PhotoCreditDto Credit,
    PhotoExif? Photo,
    PhotoPositionDto? Position);

/// <summary>
/// Who took a photograph and what may be done with it.
/// </summary>
/// <param name="PhotographerName">
/// The caver's display name when one is credited, or the free-text name when the photographer
/// is not on the roster. Resolved through the same rule as every other place a person is named,
/// so it is never an address.
/// </param>
/// <param name="LicenceCode">One of the known codes, or null when nobody has said.</param>
/// <param name="PlaceName">
/// Where it was taken, in words. Deliberately not a position and never derived from one:
/// anybody who may read the picture may read this.
/// </param>
public sealed record PhotoCreditDto(
    Guid? PhotographerCaverId,
    string? PhotographerName,
    string? Caption,
    string? LicenceCode,
    string? PlaceName,
    bool InPublicGallery);

/// <summary>What a gallery filter can ask about.</summary>
/// <param name="CaveId">Photographs hanging on one cave, or on anything below it.</param>
/// <param name="FeatureId">Photographs hanging on one feature exactly.</param>
/// <param name="TripLogId">Photographs of one trip.</param>
/// <param name="CaverId">Photographs credited to one caver.</param>
/// <param name="TagId">Photographs carrying one tag.</param>
/// <param name="AlbumId">Photographs in one album — in the album's own order.</param>
/// <param name="Camera">Photographs whose EXIF names this camera.</param>
/// <param name="Bbox">
/// The map extent, as minLon,minLat,maxLon,maxLat. Only photographs whose position this caller
/// may be told are ever matched, so the map and the grid agree about what is there.
/// </param>
/// <param name="Unplaced">True for photographs with no position at all.</param>
public sealed record PhotoQuery(
    Guid? CaveId,
    Guid? FeatureId,
    Guid? TripLogId,
    Guid? CaverId,
    long? TagId,
    Guid? AlbumId,
    Guid? UploadBatchId,
    string? Camera,
    string? Bbox,
    bool? Unplaced,
    DateOnly? From,
    DateOnly? To,
    string? Search);

/// <summary>Editing what a photograph says about itself.</summary>
public sealed record PhotoCreditWriteRequest(
    Guid? PhotographerCaverId,
    string? PhotographerName,
    string? Caption,
    string? LicenceCode,
    string? PlaceName);

public sealed class PhotoCreditWriteRequestValidator : AbstractValidator<PhotoCreditWriteRequest>
{
    public PhotoCreditWriteRequestValidator()
    {
        RuleFor(x => x.PhotographerName).MaximumLength(200);
        RuleFor(x => x.Caption).MaximumLength(1000);
        RuleFor(x => x.PlaceName).MaximumLength(300);

        // A closed vocabulary, because the field exists to be acted on: "may this go in the
        // bulletin" has to be answerable by looking, and free text answers nothing.
        RuleFor(x => x.LicenceCode)
            .Must(PhotoLicences.IsKnown)
            .WithMessage("Unknown licence.");
    }
}

/// <summary>What a bulk operation does to a selection of photographs.</summary>
/// <param name="DocumentIds">The photographs to act on.</param>
/// <param name="AddTagIds">Tags to apply.</param>
/// <param name="RemoveTagIds">Tags to take off.</param>
/// <param name="Visibility">A new read audience, or null to leave it alone.</param>
/// <param name="RotateQuarterTurns">
/// A turn to apply on top of whatever each picture already has. Nothing is rewritten — the turn
/// is recorded and every rendering is drawn with it.
/// </param>
/// <param name="AttachEntityType">An object to attach them all to.</param>
/// <param name="AddToAlbumId">An album to add them all to.</param>
/// <param name="Delete">Delete them — softly, so they can be got back inside the window.</param>
public sealed record PhotoBulkRequest(
    IReadOnlyList<Guid> DocumentIds,
    IReadOnlyList<long>? AddTagIds,
    IReadOnlyList<long>? RemoveTagIds,
    Visibility? Visibility,
    int? RotateQuarterTurns,
    string? AttachEntityType,
    Guid? AttachEntityId,
    Guid? AddToAlbumId,
    bool? Delete);

public sealed class PhotoBulkRequestValidator : AbstractValidator<PhotoBulkRequest>
{
    /// <summary>
    /// How many photographs one request may act on. Every one costs an access walk, and a
    /// selection larger than this is a job rather than a request.
    /// </summary>
    public const int MaxDocuments = 500;

    public PhotoBulkRequestValidator()
    {
        RuleFor(x => x.DocumentIds).NotEmpty();
        RuleFor(x => x.DocumentIds).Must(ids => ids is null || ids.Count <= MaxDocuments)
            .WithMessage($"At most {MaxDocuments} photographs in one request.");
        RuleFor(x => x.Visibility).IsInEnum();

        // A request asking for nothing would report success having done nothing, which reads as
        // a defect rather than as a no-op.
        RuleFor(x => x)
            .Must(x => x.AddTagIds is { Count: > 0 }
                || x.RemoveTagIds is { Count: > 0 }
                || x.Visibility is not null
                || x.RotateQuarterTurns is not null
                || x.AttachEntityId is not null
                || x.AddToAlbumId is not null
                || x.Delete == true)
            .WithMessage("Name something for the operation to do.");
    }
}

/// <summary>
/// What a bulk operation did, per photograph.
/// </summary>
/// <param name="Refused">
/// Photographs that were not changed, each with the code saying why. One the caller may not read
/// is absent from both lists — a refusal naming it would confirm it exists.
/// </param>
public sealed record PhotoBulkResultDto(
    IReadOnlyList<Guid> Changed,
    IReadOnlyDictionary<Guid, string> Refused);

/// <summary>A set of photographs holding byte-identical content.</summary>
/// <param name="Sha256">The hash they share.</param>
/// <param name="Photos">The copies, oldest first — the first is the one the rest duplicate.</param>
public sealed record PhotoDuplicateGroupDto(string Sha256, IReadOnlyList<PhotoDto> Photos);

/// <summary>A deleted photograph, as the restore list shows it.</summary>
/// <param name="RestorableUntil">
/// When it stops being restorable and its bytes become eligible to go. What a "12 days left"
/// line is drawn from.
/// </param>
public sealed record DeletedPhotoDto(
    Guid DocumentId,
    string Title,
    DateTimeOffset DeletedAt,
    DateTimeOffset RestorableUntil,
    Guid? DeletedByUserId,
    string? DeletedByName);
