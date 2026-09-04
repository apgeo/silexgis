// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// What to make of a photograph a neighbouring library holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no position in this request, and there must never be one.</b> The caller names a
/// photograph; where it was taken is read from the library that holds it. A body carrying a
/// latitude would be a way to put an object anywhere at all while the result looked as though a
/// camera had measured it, and the provenance is the entire value of this gesture.
/// </para>
/// <para>
/// The two optional fields are each required by exactly one kind, which is why neither is on the
/// record twice and neither is a general-purpose "options" bag: an entrance belongs to a cave and
/// cannot exist without one, and a feature of no built-in kind is defined by the installation's own
/// taxonomy row. Everything else a created object can carry — its type, its visibility, the club it
/// belongs to, where it sits in the hierarchy — is left at its default and edited afterwards on the
/// object's own form. This is a way of getting a position into the registry, not a second editor.
/// </para>
/// </remarks>
/// <param name="Kind">
/// A cave, an entrance of a cave, or any other kind this installation's taxonomy defines. A
/// centerline is not offered: it is a line, and a photograph is a point.
/// </param>
/// <param name="Name">What to call it.</param>
/// <param name="FeatureTypeId">
/// Which of the installation's own kinds, required for <see cref="FeatureKind.Generic"/> and
/// meaningless for the rest.
/// </param>
/// <param name="CaveFeatureId">
/// The cave the new entrance belongs to, required for <see cref="FeatureKind.CaveEntrance"/>.
/// Naming it is a write on that cave and is asked against it — adding an entrance moves the cave's
/// own point on the map, because a cave's position is its main entrance's.
/// </param>
public sealed record PhotoLibraryFeatureRequest(
    FeatureKind Kind,
    string Name,
    long? FeatureTypeId,
    Guid? CaveFeatureId);

public sealed class PhotoLibraryFeatureRequestValidator : AbstractValidator<PhotoLibraryFeatureRequest>
{
    public PhotoLibraryFeatureRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();

        // A centerline is a MultiLineString and a photograph is one point, so the kind is refused
        // here rather than left to fail deep inside the write service with a geometry message that
        // would read as a defect.
        RuleFor(x => x.Kind)
            .Must(kind => kind is FeatureKind.Cave or FeatureKind.CaveEntrance or FeatureKind.Generic)
            .WithMessage("A photograph can become a cave, an entrance, or a feature of another kind.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);

        RuleFor(x => x.FeatureTypeId)
            .NotNull().GreaterThan(0)
            .When(x => x.Kind == FeatureKind.Generic)
            .WithMessage("A feature of another kind needs one of this installation's kinds.");

        RuleFor(x => x.CaveFeatureId)
            .NotNull().NotEqual(Guid.Empty)
            .When(x => x.Kind == FeatureKind.CaveEntrance)
            .WithMessage("An entrance belongs to a cave, so the cave has to be named.");
    }
}

/// <summary>
/// What was created, and what was already standing near it.
/// </summary>
/// <param name="FeatureId">The new object.</param>
/// <param name="Name">What it ended up called.</param>
/// <param name="Kind">What it ended up being.</param>
/// <param name="Nearby">
/// Objects already in the registry within a short distance of the photograph's position, nearest
/// first. <b>Information, never a refusal.</b> Forty photographs of one entrance would otherwise
/// quietly become forty caves, and the person who took them is the one who can tell whether this is
/// the forty-first picture of a hole already recorded or a second hole four metres from it. An
/// empty list is not a promise that nothing is there: the search runs over what this caller may
/// both read and place exactly, so an object they may read but not place is left out on purpose.
/// </param>
public sealed record PhotoLibraryFeatureCreatedDto(
    Guid FeatureId,
    string Name,
    FeatureKind Kind,
    IReadOnlyList<PhotoLibraryNearbyFeatureDto> Nearby);

/// <param name="DistanceMeters">
/// From the photograph's position to the object, rounded to a tenth of a metre — a photograph is
/// taken from where the photographer stood, so anything finer would be false precision.
/// </param>
/// <remarks>
/// The cave an entrance belongs to is deliberately not carried. It would be the beginning of an
/// offer to file the new object onto something that already exists, which is a write on that thing
/// and a different gesture from this one; a warning that quietly grew a second verb is how the two
/// end up sharing an authorisation rule that only fits one of them.
/// </remarks>
public sealed record PhotoLibraryNearbyFeatureDto(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters);
