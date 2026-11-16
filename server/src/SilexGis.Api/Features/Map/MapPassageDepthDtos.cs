// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace SilexGis.Api.Features.Map;

/// <summary>What one answer of the passage-depth layer will do at most, so the cost of a pan is known.</summary>
public static class PassageDepthLimits
{
    /// <summary>The most caves one answer draws.</summary>
    public const int MaxCaves = 25;

    /// <summary>
    /// The ground readings one answer spends, spread over the caves it draws. The elevation reader
    /// answers one batch at a time for the whole installation, so this is also the most one pan can
    /// make anybody else wait.
    /// </summary>
    public const int StationBudget = 1500;

    /// <summary>The fewest stations a cave is given, however many caves share the budget.</summary>
    public const int MinStationsPerCave = 20;
}

/// <summary>
/// One cave's passage in one depth class, as the lines to draw.
/// </summary>
/// <param name="CaveId">The cave the passage belongs to.</param>
/// <param name="DepthClass">
/// The class of rock cover over these lines: 0 is below the first edge of the answer's
/// <c>classEdgesM</c> (a passage reading at or above its ground), and class <c>n</c> lies from edge
/// <c>n-1</c> up to edge <c>n</c>; the last class is everything above the last edge.
/// </param>
/// <param name="Paths">Each path is a list of <c>[longitude, latitude]</c> vertices.</param>
public sealed record PassageDepthLineDto(Guid CaveId, int DepthClass, IReadOnlyList<IReadOnlyList<double[]>> Paths);

/// <summary>The passage a map window holds, coloured by the rock over it.</summary>
/// <param name="HasTerrain">
/// Whether any elevation raster is built. False means nothing could be coloured and
/// <paramref name="Lines"/> is empty; true with empty lines means the rasters do not reach the
/// window or no cave in it carries altitudes.
/// </param>
/// <param name="ClassEdgesM">The class edges in metres of rock, ascending; the legend is drawn from them.</param>
/// <param name="Lines">The lines, by cave and class.</param>
/// <param name="CaveCount">Caves the caller may place exactly that carry altitudes and were read in this window.</param>
/// <param name="Truncated">Whether the window held more such caves than one answer draws.</param>
/// <param name="MaxCaves">The most caves one answer draws.</param>
public sealed record PassageDepthWindowDto(
    bool HasTerrain,
    IReadOnlyList<double> ClassEdgesM,
    IReadOnlyList<PassageDepthLineDto> Lines,
    int CaveCount,
    bool Truncated,
    int MaxCaves);

/// <param name="Bbox">The window, as <c>west,south,east,north</c> in degrees.</param>
public sealed record MapPassageDepthRequest([property: FromQuery(Name = "bbox")] string? Bbox);

public sealed class MapPassageDepthRequestValidator : AbstractValidator<MapPassageDepthRequest>
{
    public MapPassageDepthRequestValidator()
    {
        RuleFor(x => x.Bbox).NotEmpty();
    }
}
