// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapLayers;

public sealed record MapLayerDto(
    long Id, string Name, MapLayerKind LayerKind, string UrlTemplate, string? Options,
    string? Attribution, bool IsBase, bool IsDefault, int SortOrder);

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
                l.Attribution, l.IsBase, l.IsDefault, l.SortOrder))
            .ToListAsync(ct));
    }
}
