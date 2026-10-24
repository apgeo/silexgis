// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapLayers;

/// <param name="GroupName">
/// The heading to list this source under, or null for the ungrouped top of the list. Presentation
/// only — the client decides what a group looks like and which ones start collapsed.
/// </param>
/// <param name="MinZoom">Lowest zoom the source holds tiles for.</param>
/// <param name="MaxZoom">
/// Highest. Sent to the client so the layer stops being drawn rather than asking for tiles that
/// are not there: past its range a tile server answers 404 for the whole viewport at once, and a
/// map that goes blank on zoom-in reads as the application breaking.
/// </param>
/// <param name="InDocuments">
/// Whether this installation says the source's tiles may be copied into a document the
/// application produces: an administrator's decision where one was made, the catalogue's answer
/// otherwise. A client drawing a picture for such a document takes its background only from a
/// source that says yes; this is that answer, published, and never a guess made from the
/// address.
/// </param>
/// <param name="UrlTemplate">
/// For <c>xyz</c> a z/x/y tile template; for <c>wms</c> the service address the map adds its
/// requests to; for <c>wmts</c> either the service's key-value address or its REST template naming
/// <c>{TileMatrix}</c>, <c>{TileRow}</c> and <c>{TileCol}</c>; for <c>cog</c> the file's address;
/// for <c>vector</c> the data — a z/x/y tile template or a TileJSON address (<c>mvt</c>), a
/// PMTiles archive (<c>pmtiles</c>) or a GeoJSON file (<c>geojson</c>). It may be absolute or,
/// for a source the installation serves itself, relative to the application's own address.
/// </param>
/// <param name="Wms">How to ask a <c>wms</c> layer for pictures; null for every other kind.</param>
/// <param name="Wmts">Which tiles of a <c>wmts</c> layer to fetch; null for every other kind.</param>
/// <param name="Cog">How to read and place a <c>cog</c> layer's file; null for every other kind.</param>
/// <param name="Vector">
/// What a <c>vector</c> layer's data is and the style that draws it; null for every other kind. Its
/// 3D stand-in, when there is one, names another layer of this same answer.
/// </param>
public sealed record MapLayerDto(
    long Id, string Name, MapLayerKind LayerKind, string UrlTemplate,
    MapLayerWmsOptions? Wms, MapLayerWmtsOptions? Wmts,
    MapLayerCogOptions? Cog, MapLayerVectorOptions? Vector,
    string? Attribution, string? GroupName, int MinZoom, int MaxZoom,
    bool IsBase, bool IsDefault, int SortOrder, bool InDocuments);

/// <summary>
/// The base and overlay layers the map workspace offers. Every account reads them
/// through the All Users seed, so the catalogue is as open as it always was — the
/// difference is that it is now open by an entry an installation can tighten, rather
/// than by the absence of any rule.
/// </summary>
public static class MapLayerEndpoints
{
    public static RouteGroupBuilder MapMapLayerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map-layers", ListAsync)
            .WithTags("MapLayers")
            .WithSummary("Enabled layer catalog for the map workspace.");

        return api;
    }

    private static async Task<Results<Ok<List<MapLayerDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.MapLayers, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.MapLayers.AsNoTracking()
            .Where(l => l.Enabled)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        // Only what a map can draw is published: a kind with nothing to draw it, or a layer whose
        // stored options do not read back, would be a radio button or a switch that draws
        // nothing. The catalogue reports such entries when it is read; a row put into the table
        // some other way is simply not offered.
        var published = rows.Where(l => MapLayerOptionsJson.IsDrawable(l.LayerKind, l.Options)).ToList();
        // By name, as a stand-in is named; two rows whose names differ only in case can only have
        // been put in by hand, and the first of them stands.
        var byName = published
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return TypedResults.Ok(published.Select(l => ToDto(l, byName)).ToList());
    }

    /// <summary>A stored layer as the map reads it, among the others published with it.</summary>
    private static MapLayerDto ToDto(MapLayer l, IReadOnlyDictionary<string, MapLayer> published) => new(
        l.Id, l.Name, l.LayerKind, l.UrlTemplate,
        MapLayerOptionsJson.Wms(l.LayerKind, l.Options),
        MapLayerOptionsJson.Wmts(l.LayerKind, l.Options),
        MapLayerOptionsJson.Cog(l.LayerKind, l.Options),
        WithPublishedStandIn(l, MapLayerOptionsJson.Vector(l.LayerKind, l.Options), published),
        l.Attribution, l.GroupName, l.MinZoom, l.MaxZoom,
        l.IsBase, l.IsDefault, l.SortOrder,
        // The administrator's answer where there is one, the catalogue's otherwise — and never
        // for anything but plain tiles, whatever either said.
        MapLayerDocumentRules.Effective(l.InDocuments, l.InDocumentsChoice, l.LayerKind));

    /// <summary>
    /// A vector layer's options with its 3D stand-in named as the published layer it is, or
    /// dropped when no such layer is published — switched off, withheld for want of its key, or
    /// no longer one the 3D scene could drape in its place. The 3D scene finds the stand-in in
    /// the same answer by that name, so a name it cannot find there is never sent.
    /// </summary>
    private static MapLayerVectorOptions? WithPublishedStandIn(
        MapLayer layer, MapLayerVectorOptions? vector, IReadOnlyDictionary<string, MapLayer> published)
    {
        if (vector?.Scene3dStandIn is not { } wanted)
        {
            return vector;
        }

        return published.TryGetValue(wanted, out var standIn)
            && MapLayerKinds.CanStandIn3d(standIn.LayerKind, standIn.IsBase, layer.IsBase)
                ? vector with { Scene3dStandIn = standIn.Name }
                : vector with { Scene3dStandIn = null };
    }
}
