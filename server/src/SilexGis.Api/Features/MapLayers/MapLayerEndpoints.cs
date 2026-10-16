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
/// <c>{TileMatrix}</c>, <c>{TileRow}</c> and <c>{TileCol}</c>.
/// </param>
/// <param name="Wms">How to ask a <c>wms</c> layer for pictures; null for every other kind.</param>
/// <param name="Wmts">Which tiles of a <c>wmts</c> layer to fetch; null for every other kind.</param>
public sealed record MapLayerDto(
    long Id, string Name, MapLayerKind LayerKind, string UrlTemplate,
    MapLayerWmsOptions? Wms, MapLayerWmtsOptions? Wmts,
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

        // Only what a map can draw is published: a kind this version has nothing for, or a
        // service layer whose stored options do not read back, would be a radio button or a
        // switch that draws nothing. The catalogue reports such entries when it is read; a row
        // put into the table some other way is simply not offered.
        return TypedResults.Ok(rows
            .Where(l => MapLayerOptionsJson.IsDrawable(l.LayerKind, l.Options))
            .Select(ToDto)
            .ToList());
    }

    /// <summary>A stored layer as the map reads it.</summary>
    internal static MapLayerDto ToDto(MapLayer l) => new(
        l.Id, l.Name, l.LayerKind, l.UrlTemplate,
        MapLayerOptionsJson.Wms(l.LayerKind, l.Options),
        MapLayerOptionsJson.Wmts(l.LayerKind, l.Options),
        l.Attribution, l.GroupName, l.MinZoom, l.MaxZoom,
        l.IsBase, l.IsDefault, l.SortOrder,
        // The administrator's answer where there is one, the catalogue's otherwise — and never
        // for a map service, whose pictures are kept out of documents whatever either said.
        MapLayerDocumentRules.Effective(l.InDocuments, l.InDocumentsChoice, l.LayerKind));
}
