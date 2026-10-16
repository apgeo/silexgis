// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Api.Common;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Map;

/// <summary>One component of an area's karstification index, as the area's own statistics publish it.</summary>
/// <param name="Name">The component's stable name: <c>cave_density</c> or <c>depression_area_ratio</c>.</param>
/// <param name="Value">The measured value in its own units, or null when it could not be measured.</param>
/// <param name="Normalised">The value on a nought-to-one scale, or null when absent.</param>
/// <param name="Reference">The value that scores one.</param>
public sealed record KarstIndexComponentDto(string Name, double? Value, double? Normalised, double Reference);

/// <summary>One area in the window with its karstification index.</summary>
/// <param name="Id">The area.</param>
/// <param name="Name">Its name, to label it.</param>
/// <param name="Outline">Its outline, simplified for the zoom asked at, to fill.</param>
/// <param name="AreaKm2">The ground the outline encloses, measured on the stored outline.</param>
/// <param name="CaveCount">Caves declared to be in the area that the caller may read.</param>
/// <param name="CavesPerKm2">Those caves over that ground.</param>
/// <param name="DepressionCount">Depression outlines declared to be in it.</param>
/// <param name="DepressionAreaRatio">Share of the ground under them; null when none are mapped.</param>
/// <param name="Score">The index, nought to one, or null when nothing could be measured.</param>
/// <param name="Class">The class the score falls in.</param>
/// <param name="Components">The components, absent ones included.</param>
public sealed record KarstIndexAreaDto(
    Guid Id,
    string? Name,
    GeoJsonGeometry Outline,
    double? AreaKm2,
    int CaveCount,
    double? CavesPerKm2,
    int DepressionCount,
    double? DepressionAreaRatio,
    double? Score,
    KarstificationClass Class,
    IReadOnlyList<KarstIndexComponentDto> Components);

/// <summary>The karstification index over the areas a map window holds.</summary>
/// <param name="Basis">
/// How caves are assigned to an area: <c>declared</c> — by the containment chain somebody recorded,
/// never by where a cave's point falls — the same basis as an area's own statistics.
/// </param>
/// <param name="Areas">The areas, in a stable order.</param>
/// <param name="Truncated">
/// Whether the window held more areas than one answer carries; the client says some are not shown.
/// </param>
/// <param name="MaxAreas">The most areas one answer carries on this installation.</param>
public sealed record KarstIndexWindowDto(
    string Basis,
    IReadOnlyList<KarstIndexAreaDto> Areas,
    bool Truncated,
    int MaxAreas);

/// <param name="Bbox">The window, as <c>west,south,east,north</c> in degrees.</param>
/// <param name="Zoom">The map zoom the outlines are drawn at, which sets how far they are simplified.</param>
/// <remarks>
/// Every property names its query string explicitly, as the other map routes do, so the generated
/// client sends <c>bbox</c> and <c>zoom</c> rather than the C# names.
/// </remarks>
public sealed record MapKarstIndexRequest(
    [property: FromQuery(Name = "bbox")] string? Bbox,
    [property: FromQuery(Name = "zoom")] int? Zoom);

public sealed class MapKarstIndexRequestValidator : AbstractValidator<MapKarstIndexRequest>
{
    public MapKarstIndexRequestValidator()
    {
        RuleFor(x => x.Bbox).NotEmpty();
        RuleFor(x => x.Zoom)
            .InclusiveBetween(0, MapOptions.HighestZoom)
            .When(x => x.Zoom is not null);
    }
}
