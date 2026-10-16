// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// The data a shared map view's link draws: cave entrances and surface features at the widest
/// visibility level, for nobody in particular.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who is answered.</b> The link holder is the least-privileged reader there is (see
/// <see cref="FeatureAudienceSql.SharedLink"/>): what every account may read, narrowed to public
/// rows, with nothing placed exactly. A protected entrance or point feature is drawn snapped to the
/// protection grid, exactly as a signed-in reader without exact view is shown it; a protected line
/// or polygon is not drawn at all.
/// </para>
/// <para>
/// <b>The same statements as the workspace map.</b> Clusters are the cluster statement, the point
/// branch is the point query, and which protected rows are in the window is the window-membership
/// rule — each asked for this audience rather than rewritten for it, so a fix to one is a fix to
/// both and the oracle tests of one hold for the other.
/// </para>
/// <para>
/// <b>What it carries.</b> A position and a public name, and what drawing them needs — no feature
/// or cave identifier, no owner, no protection flag beyond <c>approximate</c>. An identifier would
/// be a handle to go and ask for the row elsewhere, and everything else is reached by signing in.
/// </para>
/// <para>
/// <b>When it answers.</b> Only while the link is a link — revoking it stops this at once — only for
/// an overlay the view was saved with switched on, and only while the installation allows data on
/// shared views at all. Every refusal of those is the same 404 an invented token gets.
/// </para>
/// </remarks>
public static class SharedViewMapEndpoints
{
    public static RouteGroupBuilder MapSharedViewDataEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/shared/views/{token:guid}/cave-entrances", EntrancesAsync)
            .WithTags("MapViews")
            .AllowAnonymous()
            .RequireRateLimiting(SharedViewRateLimits.PolicyName)
            .WithSummary("Public cave entrances for a shared view's link (anonymous): clusters below the cluster zoom, protected positions snapped.");
        api.MapGet("/shared/views/{token:guid}/features", FeaturesAsync)
            .WithTags("MapViews")
            .AllowAnonymous()
            .RequireRateLimiting(SharedViewRateLimits.PolicyName)
            .WithSummary("Public surface features for a shared view's link (anonymous): protected points snapped, protected outlines left out.");
        return api;
    }

    private static async Task<Results<Ok<FeatureCollection>, ProblemHttpResult>> EntrancesAsync(
        Guid token,
        string bbox,
        int? zoom,
        SilexGisDbContext db,
        IOptions<AccessOptions> accessOptions,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        if (await SharedConfigAsync(db, token, mapOptions.Value, ct) is not { EntrancesVisible: true })
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (MapEndpoints.WindowProblem(bbox, out var box) is { } windowProblem)
        {
            return windowProblem;
        }

        var gridMeters = accessOptions.Value.LocationGridMeters;
        var anyAccount = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);
        var effectiveZoom = Math.Clamp(zoom ?? 14, 0, MapOptions.HighestZoom);
        if (effectiveZoom < mapOptions.Value.ClusterMaxZoom)
        {
            return TypedResults.Ok(await MapSql.ClustersAsync(
                db, FeatureAudienceSql.SharedLink(anyAccount, "f"), box, effectiveZoom, gridMeters, tag: null, ct));
        }

        var protectedMembers = await WindowMembershipSql.ProtectedMembersAsync(
            db, FeatureAudienceSql.SharedLink(anyAccount, "f"),
            box.West, box.South, box.East, box.North, gridMeters, [FeatureKind.CaveEntrance], ct);
        var readable = FeatureAudienceSql.SharedLinkReadable(db, anyAccount).AsNoTracking();
        var rows = await MapEndpoints.EntrancePointsQuery(
                db, readable, box, protectedMembers, tag: null, mapOptions.Value.MaxPoints)
            .ToListAsync(ct);

        // A cave's name is shown only where the cave itself is public: an entrance can be public
        // by a level of its own while its cave is not.
        var caveIds = rows.Select(r => r.CaveId).Distinct().ToList();
        var caveNames = await FeatureAudienceSql.SharedLinkReadable(db, anyAccount).AsNoTracking()
            .Where(f => caveIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        // Nothing is placed exactly through a link: a protected row is always drawn snapped.
        return TypedResults.Ok(FeatureCollection.Of([.. rows.Select(row =>
        {
            var point = (Point)row.Geom;
            var caveName = caveNames.GetValueOrDefault(row.CaveId);
            return GeoFeature.Of(
                row.IsProtectedEffective ? LocationProtection.Snap(point, gridMeters) : point,
                new Dictionary<string, object?>
                {
                    ["name"] = row.Name ?? caveName,
                    ["caveName"] = caveName,
                    ["isMain"] = row.IsMain,
                    ["approximate"] = row.IsProtectedEffective,
                });
        })]));
    }

    private static async Task<Results<Ok<FeatureCollection>, ProblemHttpResult>> FeaturesAsync(
        Guid token,
        string bbox,
        SilexGisDbContext db,
        IOptions<AccessOptions> accessOptions,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        if (await SharedConfigAsync(db, token, mapOptions.Value, ct) is not { SurfaceFeaturesVisible: true })
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (MapEndpoints.WindowProblem(bbox, out var box) is { } windowProblem)
        {
            return windowProblem;
        }

        var gridMeters = accessOptions.Value.LocationGridMeters;
        var anyAccount = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);

        // The surface-feature overlay's own kind, and the same membership rule: a protected point is
        // in the window by its snapped position, a protected outline in none.
        var protectedMembers = await WindowMembershipSql.ProtectedMembersAsync(
            db, FeatureAudienceSql.SharedLink(anyAccount, "f"),
            box.West, box.South, box.East, box.North, gridMeters, [FeatureKind.Generic], ct);
        var polygon = box.ToPolygon();
        var rows = await FeatureAudienceSql.SharedLinkReadable(db, anyAccount).AsNoTracking()
            .Where(f => f.Kind == FeatureKind.Generic && f.Geom != null
                && ((!f.IsProtectedEffective && f.Geom!.Intersects(polygon))
                    || protectedMembers.Contains(f.Id)))
            .OrderBy(f => f.Id)
            .Take(mapOptions.Value.MaxPoints)
            .Select(f => new { f.Name, f.Geom, f.FeatureTypeId, f.IsProtectedEffective })
            .ToListAsync(ct);

        var typeIds = rows.Where(r => r.FeatureTypeId != null)
            .Select(r => r.FeatureTypeId!.Value).Distinct().ToList();
        var types = await db.FeatureTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Code, t.SymbolFile })
            .ToDictionaryAsync(t => t.Id, ct);

        var features = new List<GeoFeature>(rows.Count);
        foreach (var row in rows)
        {
            var type = row.FeatureTypeId is { } typeId ? types.GetValueOrDefault(typeId) : null;
            var properties = new Dictionary<string, object?>
            {
                ["name"] = row.Name,
                ["kind"] = "generic",
                ["typeCode"] = type?.Code,
                ["symbol"] = type?.SymbolFile,
                ["approximate"] = row.IsProtectedEffective,
            };

            if (!row.IsProtectedEffective)
            {
                features.Add(GeoFeature.Of(row.Geom!, properties));
            }
            else if (row.Geom is Point point)
            {
                features.Add(GeoFeature.Of(LocationProtection.Snap(point, gridMeters), properties));
            }

            // A protected outline is in no window for the link, so none reaches here; were one to,
            // it would be left out rather than drawn.
        }

        return TypedResults.Ok(FeatureCollection.Of(features));
    }

    /// <summary>
    /// What a live link's view says it may draw, or null when there is no such link or the
    /// installation draws no data on shared views — the two are answered alike.
    /// </summary>
    private static async Task<SharedViewConfig?> SharedConfigAsync(
        SilexGisDbContext db, Guid token, MapOptions options, CancellationToken ct)
    {
        if (!options.SharedViewData)
        {
            return null;
        }

        var config = await db.MapViews.AsNoTracking()
            .Where(v => v.ShareToken == token)
            .Select(v => v.Config)
            .FirstOrDefaultAsync(ct);
        return config is null ? null : SharedViewConfig.Read(config);
    }
}
