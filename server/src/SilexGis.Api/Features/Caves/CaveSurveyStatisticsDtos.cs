// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// The orientation of one cave's passage: which way it runs, how strongly, and how steeply.
/// </summary>
/// <param name="CaveId">The cave these were measured for.</param>
/// <param name="Basis">Which body of line work answered. This is not decoration and a reader must
/// not drop it: the two possible answers do not measure the same passage, and comparing a
/// flag-based rose with a shape-based one as though they were the same measurement is exactly the
/// mistake it exists to prevent.</param>
/// <param name="IsApproximation">True when <paramref name="Basis"/> is the shape-based reduction —
/// the form a presentation layer can act on without having to know the vocabulary.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured, when one was; null for the
/// shape-based reduction and for a cave with no line work. A cave may hold several — a corrected
/// re-export is a new upload rather than a replacement — and exactly one of them answers, so this
/// says which. Two answers about one cave are the same measurement only if this matches.</param>
/// <param name="HasAltitudes">Whether the line work carried a third coordinate. When false
/// <paramref name="Dip"/> is null: a cave drawn in plan is not a level cave.</param>
/// <param name="SegmentCount">How many pieces of passage the rose was built from.</param>
/// <param name="TotalLengthM">How many metres of passage they add up to.</param>
/// <param name="ByCount">The trend with every piece counting once.</param>
/// <param name="ByLength">The trend with every metre counting once. The two disagree on purpose:
/// counting legs measures where the surveyor put stations, counting metres measures where the cave
/// is.</param>
/// <param name="Bins">The rose, eighteen ten-degree sectors of the 0–180 half-circle, always all
/// of them and always in order.</param>
/// <param name="Dip">How steeply the passage runs, or null when there are no altitudes to say
/// with.</param>
public sealed record CaveOrientationDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    int SegmentCount,
    double TotalLengthM,
    OrientationMeasure ByCount,
    OrientationMeasure ByLength,
    IReadOnlyList<OrientationBin> Bins,
    DipSummary? Dip);

/// <summary>
/// What one cave's survey measures it to be, set beside what its record claims.
/// </summary>
/// <param name="CaveId">The cave these were measured for.</param>
/// <param name="Basis">Which body of line work answered; see <see cref="CaveOrientationDto"/>.</param>
/// <param name="IsApproximation">True when the answer came from the shape-based reduction.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured; see
/// <see cref="CaveOrientationDto"/>.</param>
/// <param name="HasAltitudes">Whether the line work carried a third coordinate. Repeated here from
/// the indices so that both routes of this pair answer the question in the same place: a reader
/// deciding whether to draw the vertical half of a page should not have to look for it one level
/// down on one route and at the top on the other. When false every vertical figure below is null,
/// because a cave drawn in plan is not a level cave.</param>
/// <param name="Indices">The measured shape: lengths, extents, ratios and sinuosity.</param>
/// <param name="Paths">Sinuosity path by path, longest first, so a reader can see whether one
/// wandering passage carries the whole figure. Empty for line work that is a network rather than a
/// set of paths.</param>
/// <param name="Length">The computed length of passage against the length somebody typed in.</param>
/// <param name="Depth">The computed vertical extent against the depth somebody typed in.</param>
/// <param name="DeclaredDisagrees">Whether either comparison disagrees — what a page puts a warning
/// on. A registry's typed-in morphometry is frequently older than the survey it is being compared
/// against, so this says the pair disagrees, never which of the two is wrong.</param>
public sealed record CaveStatisticsDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    CaveIndexSummary Indices,
    IReadOnlyList<PathSinuosity> Paths,
    MorphometryComparison Length,
    MorphometryComparison Depth,
    bool DeclaredDisagrees);

/// <summary>
/// What the two cave survey statistics routes are asked. They take no options: everything either
/// route can say, it says every time, because a figure that moves under a query parameter is a
/// figure a reader can use to ask a question the permission rules already refused.
/// </summary>
/// <param name="Id">The cave.</param>
public sealed record CaveSurveyStatisticsRequest(Guid Id);

/// <summary>
/// The one rule there is to state about a request carrying nothing but a cave id.
/// </summary>
/// <remarks>
/// The route constraint accepts the all-zero guid as a well-formed one, and no cave is ever it. It
/// is refused here as a malformed request rather than allowed through to be answered as a cave
/// nobody can see, so the two genuinely different failures — you asked for nonsense, and you asked
/// for something you may not have — do not arrive looking identical.
/// </remarks>
public sealed class CaveSurveyStatisticsRequestValidator : AbstractValidator<CaveSurveyStatisticsRequest>
{
    public CaveSurveyStatisticsRequestValidator() =>
        RuleFor(x => x.Id).NotEmpty().WithMessage("A cave id is required.");
}
