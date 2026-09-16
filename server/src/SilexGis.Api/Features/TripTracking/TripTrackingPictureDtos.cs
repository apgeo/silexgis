// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// One photograph being hung on one moment of the trip.
/// </summary>
/// <param name="DocumentId">The photograph, as a document of this installation. Never a file id:
/// a document is what a person reads, a file is the version it currently serves, and file ids
/// change under a document that stays the same picture.</param>
/// <param name="At">
/// The moment the picture is <em>of</em> — off a camera clock in the ordinary case, because these
/// arrive when somebody empties a memory card days after the trip. Never the moment of the upload.
/// </param>
/// <param name="CaverId">
/// Who the picture is about, when it is about one person. Left unset it is a picture of the trip at
/// that moment: it shows on the replay's timeline and is never drawn at anybody's position, because
/// a party that has split is in two places and a guessed one would be a photograph placed where
/// nobody was.
/// </param>
/// <param name="Caption">A line about this particular attachment — why this picture belongs to this
/// moment. Not the photograph's own caption, which lives on the photograph.</param>
public sealed record TrackingPictureInput(
    Guid DocumentId,
    DateTimeOffset At,
    Guid? CaverId,
    string? Caption);

/// <summary>A memory card's worth of pictures, placed on the moments they were taken at.</summary>
/// <remarks>
/// Bulk because that is the act: nobody uploads from underground, so the ordinary use is thirty
/// pictures at thirty different instants, read off the files themselves. One request per picture
/// would be thirty round trips and a half-attached card when the twentieth fails.
/// </remarks>
public sealed record TrackingPictureWriteRequest(IReadOnlyList<TrackingPictureInput>? Items);

/// <summary>One membership this write created — the row a later detach names.</summary>
public sealed record TrackingPictureAttachedDto(
    Guid MemberId,
    Guid DocumentId,
    DateTimeOffset At,
    Guid? CaverId);

/// <summary>
/// What the write did, per photograph.
/// </summary>
/// <param name="Refused">
/// Photographs that were not attached, each with the code saying why. A photograph this caller may
/// not read — and one that does not exist — is in <b>neither</b> list: naming it as refused would
/// confirm it exists, which is the one thing the refusal must not do. A photograph they may read
/// that is not a picture at all is named, because they could learn that by opening it.
/// </param>
public sealed record TrackingPictureResultDto(
    IReadOnlyList<TrackingPictureAttachedDto> Attached,
    IReadOnlyDictionary<Guid, string> Refused);

public sealed class TrackingPictureWriteRequestValidator : AbstractValidator<TrackingPictureWriteRequest>
{
    public TrackingPictureWriteRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Name at least one photograph.");
        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count <= TripTrackingRules.MaxPicturesPerWrite)
            .WithMessage($"At most {TripTrackingRules.MaxPicturesPerWrite} photographs in one request.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.DocumentId).NotEmpty();
            // A moment has to be stated. The field is not nullable, so an item that simply leaves
            // it out deserialises to the first instant of year one and would be stored as a real
            // reading — a photograph filed two thousand years before the trip, accepted silently
            // and then sitting outside every window anything draws. That is a malformed request
            // rather than a camera clock, and it is the one thing about a moment this refuses: a
            // clock that is merely *wrong* is deliberately accepted, because refusing would throw
            // away the record of a photograph over a number the person attaching it can correct.
            item.RuleFor(i => i.At).NotEmpty().WithMessage("Say which moment each photograph is of.");
            item.RuleFor(i => i.Caption).MaximumLength(TripTrackingRules.MaxPictureCaptionLength);
        });
    }
}
