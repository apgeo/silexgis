// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// One picture of a candidate place.
/// </summary>
/// <param name="ThumbnailUrl">
/// A short-lived delivery URL for the picture's rendering. Renderings and not the original: this
/// application draws them and strips every metadata profile out, so they show what the picture
/// shows and carry nothing the picture did — including the coordinates the original holds inside
/// itself.
/// </param>
/// <param name="Confidence">
/// How much the fix behind this picture is worth, banded. Shown because a phone under a cliff is
/// tens of metres out and nothing else on the row would say so.
/// </param>
public sealed record PhotoCandidateMemberDto(
    Guid FileId,
    string OriginalName,
    string MimeType,
    FileKind Kind,
    DateTimeOffset? CapturedAt,
    PhotoPositionSource PositionSource,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    PositionConfidenceBand Confidence,
    bool HasOwnPosition,
    string? ThumbnailUrl);

/// <summary>An object already in the registry these pictures could be, and how far away it is.</summary>
public sealed record PhotoNearbyDto(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters,
    Guid? CaveFeatureId,
    string? CaveName);

/// <summary>
/// One place the drop proposes, with every picture of it.
/// </summary>
/// <param name="Key">
/// What a decision about this place is stored under. Derived from the pictures themselves — the
/// lowest of their ids — so the same drop reviewed after lunch names the same places.
/// </param>
/// <param name="TrackMatchSecondsFromFix">
/// How far this place sits from a recorded fix, when a track placed it. Null when the camera
/// placed it or a person did.
/// </param>
public sealed record PhotoCandidateDto(
    Guid Key,
    GeoJsonGeometry? Geom,
    PhotoPositionSource PositionSource,
    IReadOnlyList<PhotoCandidateMemberDto> Members,
    string? ProposedName,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    PositionConfidenceBand Confidence,
    double? TrackMatchSecondsFromFix,
    bool TrackMatchInterpolated,
    IReadOnlyList<PhotoNearbyDto> Nearby,
    PhotoDecision? Decision);

/// <summary>
/// What the drop amounts to, before anything is created.
/// </summary>
/// <param name="SelectableKeys">
/// The places that could be confirmed as they stand: something places them, and something says
/// what they should become. Answered by the server rather than worked out in the browser,
/// because "select all" on page one has to be right about a place on page three.
/// </param>
/// <param name="UnplacedCount">
/// Pictures nothing could place — no fix of their own, no track match, nobody has dragged them
/// onto the map. Counted separately because it is the number that tells a reviewer whether the
/// camera-clock offset is wrong.
/// </param>
/// <param name="TrackFixCount">
/// How many recorded fixes the chosen track offered. Zero with a track chosen means the file
/// holds no times — which is a fact about the file, not a failure of the review.
/// </param>
public sealed record PhotoPreviewDto(
    IReadOnlyList<PhotoCandidateDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<Guid> AllKeys,
    IReadOnlyList<Guid> SelectableKeys,
    int PlacedCount,
    int UnplacedCount,
    int PhotoCount,
    int TrackFixCount,
    DateTimeOffset? TrackFirstFixAt,
    DateTimeOffset? TrackLastFixAt,
    IReadOnlyList<Guid> UnreadableFileIds);

/// <summary>A recorded track a picture with no fix of its own can be placed against.</summary>
public sealed record PhotoTrackOptionDto(Guid GeofileId, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// Runs the grouping and placement over the drop. A computation over a body, so a POST.
/// <c>decisions</c> travels with it because a place somebody dragged onto the map is placed by
/// the decision alone: the preview would otherwise redraw it back where nothing was.
/// </summary>
public sealed record PhotoPreviewRequest(
    PhotoImportOptions Options,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyDictionary<string, PhotoDecision>? Decisions,
    int? Page,
    int? PageSize,
    bool? Placed,
    string? Search);

/// <summary>A review in progress: which pictures, what was chosen, what was decided.</summary>
public sealed record PhotoSessionDto(
    IReadOnlyList<Guid> FileIds,
    PhotoImportOptions Options,
    IReadOnlyDictionary<string, PhotoDecision> Decisions,
    DateTimeOffset? UpdatedAt);

/// <summary>Saves the review. Sent as the reviewer works, so a closed tab costs nothing.</summary>
public sealed record PhotoSessionWriteRequest(
    IReadOnlyList<Guid> FileIds,
    PhotoImportOptions Options,
    IReadOnlyDictionary<string, PhotoDecision> Decisions);

/// <summary>Confirms a review: every selected place is created or filed, as one revertible unit.</summary>
public sealed record PhotoCommitRequest(
    PhotoImportOptions Options,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<Guid> Selection,
    IReadOnlyDictionary<string, PhotoDecision> Decisions);

/// <summary>
/// Gives a picture a position by hand, or takes one away.
/// </summary>
/// <param name="Position">
/// [longitude, latitude], or null to forget a position somebody gave. Written to the record and
/// never back into the file: the picture's own bytes are what was uploaded, and an application
/// that rewrote them would be changing evidence.
/// </param>
/// <param name="ReplaceRecordedFix">
/// Required to be true when the camera recorded a fix of its own. The camera's answer is
/// evidence about where the photographer was, so overruling it is a deliberate act rather than
/// a side effect of dragging something.
/// </param>
public sealed record PhotoPositionRequest(
    IReadOnlyList<double>? Position,
    bool ReplaceRecordedFix);

/// <summary>Where a picture is now, and where it came from.</summary>
public sealed record PhotoPositionDto(
    Guid FileId,
    GeoJsonGeometry? Geom,
    PhotoPositionSource PositionSource,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    PositionConfidenceBand Confidence);

/// <summary>Moves an object to the position one of its pictures records.</summary>
public sealed record FeaturePositionFromPhotoRequest(Guid FileId);

public sealed class PhotoPreviewRequestValidator : AbstractValidator<PhotoPreviewRequest>
{
    public PhotoPreviewRequestValidator()
    {
        RuleFor(x => x.FileIds).NotNull()
            .Must(ids => ids is null || ids.Count <= PhotoImportOptions.MaxFiles)
            .WithMessage($"A review holds at most {PhotoImportOptions.MaxFiles} pictures.");
        RuleFor(x => x.Options).NotNull().SetValidator(new PhotoImportOptionsValidator()!);
    }
}

public sealed class PhotoSessionWriteRequestValidator : AbstractValidator<PhotoSessionWriteRequest>
{
    public PhotoSessionWriteRequestValidator()
    {
        RuleFor(x => x.FileIds).NotNull()
            .Must(ids => ids is null || ids.Count <= PhotoImportOptions.MaxFiles)
            .WithMessage($"A review holds at most {PhotoImportOptions.MaxFiles} pictures.");
        RuleFor(x => x.Options).NotNull().SetValidator(new PhotoImportOptionsValidator()!);
    }
}

public sealed class PhotoCommitRequestValidator : AbstractValidator<PhotoCommitRequest>
{
    public PhotoCommitRequestValidator()
    {
        RuleFor(x => x.FileIds).NotNull()
            .Must(ids => ids is null || ids.Count <= PhotoImportOptions.MaxFiles)
            .WithMessage($"A review holds at most {PhotoImportOptions.MaxFiles} pictures.");
        RuleFor(x => x.Selection).NotNull().NotEmpty()
            .WithMessage("Nothing was selected.");
        RuleFor(x => x.Options).NotNull().SetValidator(new PhotoImportOptionsValidator()!);
    }
}

public sealed class PhotoPositionRequestValidator : AbstractValidator<PhotoPositionRequest>
{
    public PhotoPositionRequestValidator()
    {
        RuleFor(x => x.Position)
            .Must(p => p is null || p.Count == 2)
            .WithMessage("A position is [longitude, latitude].");
        RuleFor(x => x.Position!)
            .Must(p => double.IsFinite(p[0]) && p[0] is >= -180 and <= 180)
            .WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Position is { Count: 2 });
        RuleFor(x => x.Position!)
            .Must(p => double.IsFinite(p[1]) && p[1] is >= -90 and <= 90)
            .WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Position is { Count: 2 });
    }
}

public sealed class FeaturePositionFromPhotoRequestValidator
    : AbstractValidator<FeaturePositionFromPhotoRequest>
{
    public FeaturePositionFromPhotoRequestValidator() => RuleFor(x => x.FileId).NotEmpty();
}

/// <summary>
/// The whole-drop choices. Bounded rather than trusted: every one of these sizes a query or a
/// loop, and the clock offset in particular is uploader-supplied arithmetic on a timestamp.
/// </summary>
public sealed class PhotoImportOptionsValidator : AbstractValidator<PhotoImportOptions>
{
    public PhotoImportOptionsValidator()
    {
        RuleFor(x => x.DefaultKind).IsInEnum();
        RuleFor(x => x.Elevation).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.ClusterRadiusMeters)
            .InclusiveBetween(0, PhotoImportOptions.MaxClusterRadiusMeters);
        RuleFor(x => x.ProximityRadiusMeters)
            .InclusiveBetween(0, PhotoImportOptions.MaxProximityRadiusMeters);
        RuleFor(x => x.TrackMatchToleranceSeconds)
            .InclusiveBetween(0, PhotoImportOptions.MaxTrackMatchToleranceSeconds);
        RuleFor(x => x.CameraClockOffsetSeconds)
            .InclusiveBetween(-PhotoImportOptions.MaxCameraClockOffsetSeconds, PhotoImportOptions.MaxCameraClockOffsetSeconds);
        RuleFor(x => x.NamePrefix).MaximumLength(100);
        RuleFor(x => x.DefaultCaveTypeCode).MaximumLength(100);
        RuleFor(x => x.DefaultEntranceTypeCode).MaximumLength(100);
        RuleFor(x => x.DefaultFeatureTypeCode).MaximumLength(100);
        RuleFor(x => x.TagIds).Must(ids => ids.Count <= 50)
            .WithMessage("At most 50 tags.");
    }
}
