// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// How big one cave's passages are, from the wall distances recorded at its stations.
/// </summary>
/// <param name="CaveId">The cave measured.</param>
/// <param name="Basis">Which body of line work produced the figures. Wall distances exist only in a
/// parsed survey file, so this is never the shape-based reduction: a cave whose line work is only a
/// drawing has no cross-sections at all, and saying it was approximated would claim an approximation
/// was computed when nothing was.</param>
/// <param name="IsApproximation">True when the basis is the shape-based reduction. Carried for the
/// same reason every other survey figure carries it, and false throughout here.</param>
/// <param name="SurveyModelId">Which uploaded file the figures were measured over; null when none
/// answered. A cave may hold several and only one of them is measured.</param>
/// <param name="HasReadings">Whether any wall distance was recorded at all. False is "nobody
/// measured the walls", which is a different statement from a cave with no survey and from passages
/// of no size.</param>
/// <param name="Summary">The sizes, the distributions, the volume and the vertical slices; null when
/// nothing was measured.</param>
public sealed record CaveCrossSectionDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasReadings,
    CrossSectionSummary? Summary);

/// <summary>Which cave to measure. The route takes nothing else.</summary>
/// <param name="Id">The cave.</param>
public sealed record CaveCrossSectionRequest(Guid Id);

/// <summary>Refuses a request naming no cave before it reaches the access check.</summary>
public sealed class CaveCrossSectionRequestValidator : AbstractValidator<CaveCrossSectionRequest>
{
    public CaveCrossSectionRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
