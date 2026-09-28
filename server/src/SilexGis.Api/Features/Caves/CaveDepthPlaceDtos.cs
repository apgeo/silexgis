// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// One depth a cave has declared the meaning of: the station it means, and what people call it.
/// </summary>
/// <remarks>
/// Ordered by depth on every read, shallowest first, because that is the order the cave is
/// described in and the order a chooser has to offer: somebody picking where a party is thinks
/// downwards from the entrance, not alphabetically.
/// </remarks>
public sealed record CaveDepthPlaceDto(
    Guid Id,
    decimal DepthM,
    string StationName,
    string? PlaceLabel);

/// <summary>
/// A declaration being written. The depth identifies it, so writing one that already exists
/// replaces its station and label rather than adding a second row for the same depth.
/// </summary>
public sealed record CaveDepthPlaceWriteRequest(
    decimal? DepthM,
    string? StationName,
    string? PlaceLabel);

public sealed class CaveDepthPlaceWriteRequestValidator : AbstractValidator<CaveDepthPlaceWriteRequest>
{
    public CaveDepthPlaceWriteRequestValidator()
    {
        RuleFor(x => x.DepthM).NotNull();

        // The same bound a reported depth is held to, so a cave cannot declare a depth that no
        // report could ever be given at.
        RuleFor(x => x.DepthM!.Value)
            .InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);

        // A declaration with no station declares nothing: the whole point of the row is that this
        // depth means that station.
        RuleFor(x => x.StationName)
            .NotEmpty()
            .MaximumLength(TripTrackingRules.MaxStationNameLength);

        // A place may have no word people use for it, which is an ordinary thing and not an
        // omission — so this is bounded rather than required.
        RuleFor(x => x.PlaceLabel).MaximumLength(TripTrackingRules.MaxTitleLength);
    }
}
