// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// A cave's depth and height taken across its entrances and its survey.
/// </summary>
/// <param name="CaveId">The cave this was measured for.</param>
/// <param name="Basis">Which body of line work answered the survey half.</param>
/// <param name="IsApproximation">True when the survey half came from the shape-based reduction.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured, when one was.</param>
/// <param name="HasAltitudes">Whether the line work carried real altitudes. When false every figure
/// that needs the survey is null: a plan drawing of a cave is not a flat cave.</param>
/// <param name="Figures">Entrance extremes, total depth, height and potential depth.</param>
/// <param name="SurveyHighestM">Highest point of the survey, metres.</param>
/// <param name="SurveyLowestM">Lowest point of the survey, metres.</param>
/// <param name="PotentialDepth">The potential depth computed from the entrances and the area's
/// resurgence level, set beside the one typed into the record.</param>
public sealed record CaveSystemFiguresDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    CaveSystemFiguresSummary Figures,
    double? SurveyHighestM,
    double? SurveyLowestM,
    MorphometryComparison PotentialDepth);

/// <summary>Which cave. The route takes nothing else.</summary>
public sealed record CaveSystemFiguresRequest(Guid Id);

/// <summary>Refuses a request naming no cave.</summary>
public sealed class CaveSystemFiguresRequestValidator : AbstractValidator<CaveSystemFiguresRequest>
{
    public CaveSystemFiguresRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
