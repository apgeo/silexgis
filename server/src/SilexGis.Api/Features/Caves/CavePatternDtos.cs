// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// What kind of cave one survey's shape suggests, and every rule applied to reach it.
/// </summary>
/// <remarks>
/// The suggestion carries its whole reasoning rather than a label, and the client shows it that way.
/// A pattern name on its own is an assertion a reader cannot check; the rules that fired, the
/// figures they fired on, and the rules that looked and stayed silent are what make it a reading
/// somebody who knows the cave can disagree with.
/// </remarks>
/// <param name="CaveId">The cave described.</param>
/// <param name="Basis">Which body of line work the figures came from.</param>
/// <param name="IsApproximation">True when the figures came from the shape-based reduction rather
/// than from the flags the survey file's own software wrote.</param>
/// <param name="SurveyModelId">Which uploaded file the figures were measured over; null when none
/// answered.</param>
/// <param name="HasAltitudes">Whether the line work carried a third coordinate. When false every
/// rule about the profile could not be assessed, and the suggestion says so rather than describing
/// the cave as level.</param>
/// <param name="Suggestion">The pattern, the scores, the rules and the caveats.</param>
/// <param name="Network">The network figures the rules read, or null when the line work joined no
/// named stations. Reported beside the suggestion so the counts a rule fired on are visible without
/// a second request.</param>
public sealed record CavePatternDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    PatternSuggestion Suggestion,
    PassageNetworkFigures? Network);

/// <summary>Which cave to describe. The route takes nothing else — a suggestion that moves under a
/// query parameter is a different suggestion, and the reader would have no way to know which one
/// they were shown.</summary>
/// <param name="Id">The cave.</param>
public sealed record CavePatternRequest(Guid Id);

/// <summary>Refuses a request naming no cave before it reaches the access check.</summary>
public sealed class CavePatternRequestValidator : AbstractValidator<CavePatternRequest>
{
    public CavePatternRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
