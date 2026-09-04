// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.PhotoLibraries;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a request to build an object out of a photograph is allowed to say. These cases need no
/// database and no host: they are about the shape of the body alone, and the shape is where two of
/// the three kinds are half-defined — an entrance with no cave and a feature of no kind are both
/// requests nobody can answer, and both would otherwise fail much later with a message about
/// something else.
///
/// <para>Every name and identifier below is invented.</para>
/// </summary>
public sealed class PhotoLibraryFeatureRequestTests
{
    private static readonly PhotoLibraryFeatureRequestValidator Validator = new();

    private static bool IsValid(PhotoLibraryFeatureRequest request) => Validator.Validate(request).IsValid;

    [Fact]
    public void A_cave_needs_a_name_and_nothing_else()
    {
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Cave, "An invented cave", null, null))
            .ShouldBeTrue();

        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Cave, "   ", null, null)).ShouldBeFalse();
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Cave, new string('x', 256), null, null))
            .ShouldBeFalse();
    }

    /// <summary>
    /// An entrance belongs to a cave and cannot exist without one — there is nowhere to put it and
    /// no cave for its position to become the point of.
    /// </summary>
    [Fact]
    public void An_entrance_names_the_cave_it_belongs_to()
    {
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.CaveEntrance, "Upper entrance", null, null))
            .ShouldBeFalse();
        IsValid(new PhotoLibraryFeatureRequest(
            FeatureKind.CaveEntrance, "Upper entrance", null, Guid.Empty)).ShouldBeFalse();

        IsValid(new PhotoLibraryFeatureRequest(
            FeatureKind.CaveEntrance, "Upper entrance", null, Guid.CreateVersion7())).ShouldBeTrue();
    }

    /// <summary>
    /// "Another kind" is whatever this installation's own taxonomy defines, so a request that names
    /// none has not said what to create.
    /// </summary>
    [Fact]
    public void A_feature_of_another_kind_names_which_kind()
    {
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Generic, "A spring", null, null)).ShouldBeFalse();
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Generic, "A spring", 0, null)).ShouldBeFalse();

        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Generic, "A spring", 7, null)).ShouldBeTrue();
    }

    /// <summary>
    /// A photograph is one point and a centerline is a line, so the kind is refused on the body
    /// rather than deep inside the write service, where the answer would be about geometry and would
    /// read as a defect in this route.
    /// </summary>
    [Fact]
    public void A_photograph_cannot_become_a_centerline()
    {
        IsValid(new PhotoLibraryFeatureRequest(FeatureKind.Centerline, "Not a line", null, null))
            .ShouldBeFalse();
        IsValid(new PhotoLibraryFeatureRequest((FeatureKind)99, "Not a kind", null, null)).ShouldBeFalse();
    }
}
