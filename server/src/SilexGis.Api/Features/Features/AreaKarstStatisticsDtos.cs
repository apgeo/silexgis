// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Features;

/// <summary>One cave standing at an end of a range in an area.</summary>
public sealed record AreaCaveExtremeDto(Guid FeatureId, string? Name, double Value);

/// <summary>How many of the area's caves carry one rock type.</summary>
/// <param name="RockTypeId">Null for the caves whose rock type has never been recorded.</param>
public sealed record AreaRockTypeCountDto(long? RockTypeId, string? Code, string? Name, int CaveCount);

/// <summary>One reading behind the karstification index.</summary>
/// <param name="Value">The measurement in its own units, or null when it could not be taken.</param>
/// <param name="Normalised">The same reading on a nought-to-one scale, or null when absent.</param>
/// <param name="Reference">The value that scores one — a published convention, so two installations rank alike.</param>
public sealed record KarstificationComponentDto(
    string Name, double? Value, double? Normalised, double Reference);

/// <summary>
/// The composite karstification reading for an area, with the components it was built from.
/// </summary>
/// <param name="Score">
/// The mean of the components that could be measured, from nought to one, or null when none could.
/// </param>
/// <param name="Class">
/// The class the score falls in. <c>Unknown</c> means nothing was measurable, which is not the same
/// statement as a low score.
/// </param>
/// <param name="Components">
/// Every component, present or absent. An absent one is carried with a null value rather than
/// omitted, because an index built from fewer readings is a weaker claim and the payload has to say
/// so — an area whose depressions nobody has drawn must not read as an area that has none.
/// </param>
public sealed record KarstificationIndexDto(
    double? Score,
    KarstificationClass Class,
    IReadOnlyList<KarstificationComponentDto> Components);

/// <summary>
/// What one karst area adds up to.
/// </summary>
/// <param name="Basis">
/// How membership was decided. It is always <c>declared</c>: a cave is in the area because the
/// hierarchy says so, not because its coordinate falls inside the outline. The field is on the wire
/// and shown in the interface because the two answers differ, and a reader comparing this total
/// with what they can see on the map needs to know which question was asked.
/// </param>
/// <param name="AreaKm2">
/// The outline's ground area, measured on the spheroid. Null when the area row carries no outline,
/// in which case every density here is null too — there is nothing to divide by.
/// </param>
/// <param name="CaveCount">
/// Caves declared to be in the area, at any depth of the chain, that this caller may read. Caves
/// they may not read are absent from it entirely, so this is not the registry's own total.
/// </param>
/// <param name="UnparentedInsideCount">
/// Caves whose position falls inside the outline but which are not declared to be in the area — a
/// data-quality hint about how far the hierarchy has drifted from the map, and no part of any
/// statistic above. It is counted only over the caves this caller may place exactly, because the
/// test is a spatial one and running it against a position they may not see would make the hint a
/// way of asking where a cave is.
/// </param>
/// <param name="PlaceableCaveCount">
/// How many of the area's own caves this caller may place exactly. It is the calibration the drift
/// hint has to be read against: the hint is a spatial test that can only run over positions the
/// caller may see, so a reader who can place a tenth of the caves should read a hint of nought as
/// "none in the tenth I can see" rather than as "the hierarchy is clean".
/// </param>
public sealed record AreaKarstStatisticsDto(
    Guid AreaId,
    string Basis,
    double? AreaKm2,
    int CaveCount,
    int EntranceCount,
    double? CavesPerKm2,
    double? EntrancesPerKm2,
    double? SurveyedLengthM,
    int SurveyedCaveCount,
    double? SurveyedMetresPerKm2,
    int DepressionCount,
    double? DepressionAreaKm2,
    double? DepressionAreaRatio,
    IReadOnlyList<AreaCaveExtremeDto> DeepestCaves,
    IReadOnlyList<AreaCaveExtremeDto> LongestCaves,
    IReadOnlyList<AreaRockTypeCountDto> RockTypes,
    KarstificationIndexDto Karstification,
    int UnparentedInsideCount,
    int PlaceableCaveCount);

/// <summary>
/// The bounds on an area-statistics request, in one place so the handler and anything that reads it
/// cannot drift apart.
/// </summary>
public static class AreaKarstStatisticsLimits
{
    /// <summary>How many caves each extreme list names.</summary>
    public const int ExtremeCount = 5;

    /// <summary>The membership basis this endpoint answers over, as it appears on the wire.</summary>
    public const string DeclaredBasis = "declared";
}
