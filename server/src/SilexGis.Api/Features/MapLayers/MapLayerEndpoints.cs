// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
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
public sealed record MapLayerDto(
    long Id, string Name, MapLayerKind LayerKind, string UrlTemplate, string? Options,
    string? Attribution, string? GroupName, int MinZoom, int MaxZoom,
    bool IsBase, bool IsDefault, int SortOrder);

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

        return TypedResults.Ok(await db.MapLayers.AsNoTracking()
            .Where(l => l.Enabled)
            .OrderBy(l => l.SortOrder)
            .Select(l => new MapLayerDto(
                l.Id, l.Name, l.LayerKind, l.UrlTemplate, l.Options,
                l.Attribution, l.GroupName, l.MinZoom, l.MaxZoom,
                l.IsBase, l.IsDefault, l.SortOrder))
            .ToListAsync(ct));
    }
}
