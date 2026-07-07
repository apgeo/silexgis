// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapLayers;

public sealed record MapLayerDto(
    long Id, string Name, MapLayerKind LayerKind, string UrlTemplate, string? Options,
    string? Attribution, bool IsBase, bool IsDefault, int SortOrder);

public static class MapLayerEndpoints
{
    public static RouteGroupBuilder MapMapLayerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/map-layers", async (SilexGisDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.MapLayers.AsNoTracking()
                    .Where(l => l.Enabled)
                    .OrderBy(l => l.SortOrder)
                    .Select(l => new MapLayerDto(
                        l.Id, l.Name, l.LayerKind, l.UrlTemplate, l.Options,
                        l.Attribution, l.IsBase, l.IsDefault, l.SortOrder))
                    .ToListAsync(ct)))
            .WithTags("MapLayers")
            .WithSummary("Enabled layer catalog for the map workspace.");

        return api;
    }
}
