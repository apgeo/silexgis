// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Features;

/// <summary>
/// One class of one terrain measure: how many entrances and sinkholes fall in it, how much of the
/// background does, and how the two compare.
/// </summary>
/// <param name="Code">Stable code of the class (for slope <c>lt5</c>, for aspect <c>n</c>, …).</param>
/// <param name="Entrances">Placeable cave entrances in this class.</param>
/// <param name="EntranceShare">Their share of the entrances read, 0 to 1; null with none read.</param>
/// <param name="EntranceRatio">Entrance share divided by background share: above one the entrances
/// sit here more than the ground does. Null where it would say nothing.</param>
/// <param name="Sinkholes">Placeable sinkholes in this class.</param>
/// <param name="SinkholeShare">Their share of the sinkholes read.</param>
/// <param name="SinkholeRatio">Sinkhole share divided by background share.</param>
/// <param name="Background">Background points in this class.</param>
/// <param name="BackgroundShare">Their share of the background read.</param>
public sealed record TerrainClassRowDto(
    string Code,
    int Entrances,
    double? EntranceShare,
    double? EntranceRatio,
    int Sinkholes,
    double? SinkholeShare,
    double? SinkholeRatio,
    int Background,
    double? BackgroundShare);

/// <summary>
/// One measure, entrances and sinkholes against the background.
/// </summary>
/// <param name="Measure"><c>slope</c>, <c>aspect</c> or <c>position</c>.</param>
/// <param name="Available">False when no computed picture could answer; <see cref="Reason"/> says why.</param>
/// <param name="Reason"><c>no_layer</c> when no current picture of that measure is computed for the
/// active terrain; <c>needs_slope</c> for facing without a steepness picture to tell flat ground
/// from sloping ground; <c>slope_unit</c> when the only steepness picture is in percent.</param>
/// <param name="EntrancesRead">Entrances that got a value.</param>
/// <param name="SinkholesRead">Sinkholes that got a value.</param>
/// <param name="BackgroundRead">Background points that got a value.</param>
/// <param name="Classes">One row per class, in order.</param>
public sealed record TerrainMeasureDto(
    string Measure,
    bool Available,
    string? Reason,
    int EntrancesRead,
    int SinkholesRead,
    int BackgroundRead,
    IReadOnlyList<TerrainClassRowDto> Classes);

/// <summary>
/// Entrances and sinkholes of one area against the ground they sit in, over the computed pictures of
/// the active terrain.
/// </summary>
/// <param name="AreaId">The area asked about.</param>
/// <param name="HasTerrain">Whether this installation serves any elevation at all.</param>
/// <param name="Entrances">How many entrances the caller may place in the area.</param>
/// <param name="Sinkholes">How many sinkholes the caller may place in the area.</param>
/// <param name="BackgroundPoints">How many grid points inside the outline the background was read
/// at. The background is a regular grid sample of the outline, not a count of every cell.</param>
/// <param name="Measures">Slope, aspect and position.</param>
public sealed record AreaTerrainStatisticsDto(
    Guid AreaId,
    bool HasTerrain,
    int Entrances,
    int Sinkholes,
    int BackgroundPoints,
    IReadOnlyList<TerrainMeasureDto> Measures);
