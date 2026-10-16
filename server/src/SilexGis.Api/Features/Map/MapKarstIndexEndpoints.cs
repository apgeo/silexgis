// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The karstification index of every area a map window holds, in one request, so a map can fill
/// each area by how karstified it reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>An area is answered only for a caller who may place it exactly</b> — the same gate an area's
/// own statistics ask, applied to every area — and is in the window only by an outline that caller
/// may see. Any other area is simply absent: no count of the absent ones, no placeholder, and a
/// cap that is applied after membership, so an area the caller may not place can never take a
/// slot and push a visible one out.
/// </para>
/// <para>
/// <b>The figures are the area statistics' own.</b> Caves and depressions are counted by declared
/// containment over what the caller may read, protected caves included, and the reading is derived
/// by the one Domain rule both routes call; for any one area this answer and that one agree.
/// </para>
/// </remarks>
public static class MapKarstIndexEndpoints
{
    /// <summary>How caves are assigned to areas; the area statistics publish the same word.</summary>
    private const string DeclaredBasis = "declared";

    /// <summary>
    /// The area kinds the index is published for: karst areas and the massifs they sit in. Other
    /// outlines — a work area, a cave system's footprint — are not karst units to rank.
    /// </summary>
    private static readonly string[] AreaTypeCodes = [FeatureTypeSeeds.KarstArea, FeatureTypeSeeds.Massif];

    public static RouteGroupBuilder MapMapKarstIndexEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map/karst-index", KarstIndexAsync)
            .WithTags("Map")
            .WithValidation<MapKarstIndexRequest>()
            .WithSummary(
                "The karstification index of each karst area and massif in a bbox that the caller "
                + "may place exactly, with its outline to draw, its class and its components. "
                + "Areas the caller may not place are absent.");
        return api;
    }

    private static async Task<Results<Ok<KarstIndexWindowDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        KarstIndexAsync(
            [AsParameters] MapKarstIndexRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            IOptions<AccessOptions> accessOptions,
            IOptions<MapOptions> mapOptions,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (MapEndpoints.WindowProblem(request.Bbox, out var box) is { } windowProblem)
        {
            return windowProblem;
        }

        var maxAreas = mapOptions.Value.KarstIndexMaxAreas;
        var zoom = Math.Clamp(request.Zoom ?? 14, 0, MapOptions.HighestZoom);

        var typeIds = await db.FeatureTypes.AsNoTracking()
            .Where(t => AreaTypeCodes.Contains(t.Code))
            .Select(t => t.Id)
            .ToListAsync(ct);
        var depressionTypeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.Sinkhole)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        // Protected areas are in the window only by the shared membership rule, which leaves a
        // protected outline the caller may not place out of every window.
        var protectedMembers = await WindowMembershipSql.ProtectedMembersAsync(
            db, ctx, box.West, box.South, box.East, box.North, accessOptions.Value.LocationGridMeters,
            [FeatureKind.Generic], ct);

        // One more than the cap, so a window holding more is recognised as truncated.
        var candidates = await KarstIndexWindowSql.AreasAsync(
            db, ctx, box.West, box.South, box.East, box.North, typeIds, protectedMembers, maxAreas + 1, ct);

        // The placement gate an area's own statistics ask, applied to every area. The membership
        // rule above should already have kept out every outline that fails it; asking again is
        // what makes this route's answer the gate's answer rather than the window rule's.
        var placeable = await protection.PlaceableIdsAsync(
            ctx, [.. candidates.Select(c => (c.Id, c.IsProtected))], ct);
        var gated = candidates.Where(c => placeable.Contains(c.Id)).ToList();
        var truncated = gated.Count > maxAreas;
        var shown = gated.Take(maxAreas).ToList();

        var totals = await KarstIndexWindowSql.TotalsAsync(
            db, ctx, [.. shown.Select(a => a.Id)], depressionTypeId ?? 0L, OnePixelDegrees(zoom), ct);
        var totalsById = totals.ToDictionary(t => t.AreaId);

        var areas = new List<KarstIndexAreaDto>(shown.Count);
        foreach (var area in shown)
        {
            if (!totalsById.TryGetValue(area.Id, out var row) || ToOutline(row.OutlineGeoJson) is not { } outline)
            {
                continue;
            }

            var reading = KarstificationIndex.Read(
                row.AreaM2, row.CaveCount, row.DepressionCount, row.DepressionAreaM2);
            areas.Add(new KarstIndexAreaDto(
                area.Id,
                area.Name,
                outline,
                reading.AreaKm2,
                row.CaveCount,
                reading.CavesPerKm2,
                row.DepressionCount,
                reading.DepressionAreaRatio,
                reading.Score,
                reading.Class,
                [.. reading.Components.Select(c =>
                    new KarstIndexComponentDto(c.Name, c.Value, c.Normalised, c.Reference))]));
        }

        return TypedResults.Ok(new KarstIndexWindowDto(DeclaredBasis, areas, truncated, maxAreas));
    }

    /// <summary>One screen pixel at this zoom in degrees: the most an outline drawn there may move.</summary>
    private static double OnePixelDegrees(int zoom) => 360d / (256d * Math.Pow(2, zoom));

    private static GeoJsonGeometry? ToOutline(string? geoJson)
    {
        if (geoJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(geoJson);
        var root = document.RootElement;
        return root.TryGetProperty("type", out var type) && root.TryGetProperty("coordinates", out var coordinates)
            ? new GeoJsonGeometry(type.GetString() ?? string.Empty, coordinates.Clone())
            : null;
    }
}
