// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// The vertical figures of the caves filed under one area, taken together.
/// </summary>
/// <param name="AreaId">The area this was measured for.</param>
/// <param name="Basis">How membership was decided; always the declared hierarchy.</param>
/// <param name="CaveCount">Member caves the caller may place exactly — what was measured, not what
/// the area holds.</param>
/// <param name="SurveyedCaveCount">How many of those carried line work with altitudes.</param>
/// <param name="Truncated">True when more placeable caves exist than one answer measures.</param>
/// <param name="IsApproximation">True when any member's survey half came from the shape-based
/// reduction rather than from parsed survey legs.</param>
/// <param name="HasAltitudes">Whether any member's line work carried real altitudes. When false the
/// figures that need the survey are null.</param>
/// <param name="Figures">Entrance extremes and spread, system depth, height and potential depth.</param>
/// <param name="SurveyHighestM">Highest surveyed point among the members, metres.</param>
/// <param name="SurveyLowestM">Lowest surveyed point among the members, metres.</param>
public sealed record AreaSystemFiguresDto(
    Guid AreaId,
    string Basis,
    int CaveCount,
    int SurveyedCaveCount,
    bool Truncated,
    bool IsApproximation,
    bool HasAltitudes,
    CaveSystemFiguresSummary Figures,
    double? SurveyHighestM,
    double? SurveyLowestM);
