// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>One reading of a cave's descent profile. Carries no position.</summary>
/// <param name="DistanceAlongM">Passage length before this point, in the line work's recorded order.</param>
/// <param name="AltitudeM">Altitude of the passage here, in the survey's datum.</param>
/// <param name="DepthM">Metres below the highest point the survey reaches.</param>
/// <param name="PathIndex">Which polyline of the line work this fell on, when it has ordered paths.</param>
/// <param name="SegmentIndex">Which piece of that line work this fell on.</param>
public sealed record CaveDepthProfileSampleDto(
    double DistanceAlongM, double AltitudeM, double DepthM, int? PathIndex, int SegmentIndex);

/// <summary>
/// How deep a cave's passages lie, piece by piece along their length.
/// </summary>
/// <param name="CaveId">The cave this was measured for.</param>
/// <param name="Basis">Which body of line work answered.</param>
/// <param name="IsApproximation">True when the line work came from the shape-based reduction.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured, when one was.</param>
/// <param name="HasAltitudes">Whether the line work carried real altitudes. When false there are no
/// samples at all: a plan drawing is not a flat cave.</param>
/// <param name="PassageLengthM">Length the samples are spread over.</param>
/// <param name="HighestM">Highest altitude on the profile.</param>
/// <param name="LowestM">Lowest altitude on the profile.</param>
/// <param name="DeepestM">Highest minus lowest.</param>
/// <param name="Samples">The readings, in increasing distance along.</param>
public sealed record CaveDepthProfileDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    double PassageLengthM,
    double? HighestM,
    double? LowestM,
    double? DeepestM,
    IReadOnlyList<CaveDepthProfileSampleDto> Samples);

/// <summary>Which cave. The route takes nothing else.</summary>
public sealed record CaveDepthProfileRequest(Guid Id);

/// <summary>Refuses a request naming no cave.</summary>
public sealed class CaveDepthProfileRequestValidator : AbstractValidator<CaveDepthProfileRequest>
{
    public CaveDepthProfileRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
