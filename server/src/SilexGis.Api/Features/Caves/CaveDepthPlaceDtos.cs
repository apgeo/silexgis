// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// One place a cave has named: the station it is, what people call it, and — for most of them —
/// the depth it means.
/// </summary>
/// <remarks>
/// Listed on every read in one order: the places with a depth by depth, shallowest first, because
/// that is the order the cave is described in and the order a chooser has to offer — somebody
/// picking where a party is thinks downwards from the entrance, not alphabetically; then the
/// places named without a depth, in the order they were declared, which is the order the club
/// wrote its own list in.
/// </remarks>
/// <param name="DepthM">
/// Metres below the entrance datum, a magnitude to one decimal; null for a place named without a
/// depth, which no reported depth is ever placed at.
/// </param>
/// <param name="Note">
/// The short text shown beside the name where a single depth does not say enough, such as a depth
/// range written as text; null where there is none.
/// </param>
/// <param name="Landmark">
/// Whether the place is named on the drawing itself, and not only in lists of the cave's places.
/// </param>
/// <param name="StationInSurvey">
/// Whether the survey marked as this cave's current one holds a station of that name: true or false
/// where that can be said, and null where it cannot — the cave has no current survey that has been
/// read (another upload standing in for it does not count), or this caller may not be told about
/// the cave's survey. A surface marks only an outright false. It is a
/// statement about the cave's survey and not about any watch: a watch says the same of its own
/// survey when it lists the places it can offer.
/// </param>
public sealed record CaveDepthPlaceDto(
    Guid Id,
    decimal? DepthM,
    string StationName,
    string? PlaceLabel,
    string? Note,
    bool Landmark,
    bool? StationInSurvey);

/// <summary>
/// A place being written. With a depth, the depth identifies it, so writing one that already
/// exists replaces its station, label, note and landmark flag rather than adding a second row for
/// the same depth. Without one, the station identifies it among the places named without a depth,
/// so writing the same station again replaces its label, note and flag.
/// </summary>
/// <param name="Landmark">Absent or null is false: the place is named in lists only.</param>
public sealed record CaveDepthPlaceWriteRequest(
    decimal? DepthM,
    string? StationName,
    string? PlaceLabel,
    string? Note = null,
    bool? Landmark = null);

public sealed class CaveDepthPlaceWriteRequestValidator : AbstractValidator<CaveDepthPlaceWriteRequest>
{
    public CaveDepthPlaceWriteRequestValidator()
    {
        // The same bound a reported depth is held to, so a cave cannot declare a depth that no
        // report could ever be given at.
        RuleFor(x => x.DepthM!.Value)
            .InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);

        // A declaration with no station declares nothing: the whole point of the row is that this
        // depth, or this name, means that station.
        RuleFor(x => x.StationName)
            .NotEmpty()
            .MaximumLength(TripTrackingRules.MaxStationNameLength);

        // A place may have no word people use for it, which is an ordinary thing and not an
        // omission — so this is bounded rather than required...
        RuleFor(x => x.PlaceLabel).MaximumLength(TripTrackingRules.MaxTitleLength);

        // ...where there is a depth. A station with neither a depth nor a name declares nothing:
        // it places no reported depth and gives a list nothing to show, so it is refused rather
        // than kept as a row that means nothing.
        RuleFor(x => x.PlaceLabel)
            .Must(label => !string.IsNullOrWhiteSpace(label))
            .When(x => x.DepthM is null)
            .WithMessage("A place without a depth needs a name: without either it declares nothing.");

        RuleFor(x => x.Note).MaximumLength(TripTrackingRules.MaxPlaceNoteLength);
    }
}
