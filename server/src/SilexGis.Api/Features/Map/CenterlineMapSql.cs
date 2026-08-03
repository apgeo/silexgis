// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// One row of the centerline map query: the chosen representation already clipped, simplified
/// and serialised by PostGIS. <see cref="GeoJson"/> is null when the row did not make the cut,
/// in which case it still reports why so the client can say how much is not being shown.
/// <see cref="HasZ"/> reports what the served geometry actually carries — a caller that asked
/// for altitudes can be handed a flat representation (see the query), and must be told.
/// <see cref="TopZ"/> describes the whole centerline rather than the part being served, which is
/// why it cannot be read off the payload.
/// </summary>
public sealed record CenterlineMapRow(
    Guid Id,
    Guid CaveId,
    string? Name,
    decimal? LengthM,
    int Paths,
    bool Detail,
    bool Included,
    bool Withheld,
    bool HasZ,
    double? TopZ,
    string? GeoJson);

/// <summary>
/// Dapper SQL for the centerline overlay (raw SQL lives only in *Sql.cs files).
/// <para>
/// The work happens in PostGIS rather than in EF for a measured reason: reading whole entities
/// materialises every coordinate as a .NET object, which for one real cave meant 167k objects
/// and a 178 ms request; producing the same bytes in the database and passing them through cost
/// roughly half that, and clipping shrinks what has to be produced at all.
/// </para>
/// <para>
/// A centerline is a feature (the geometry and access trio live on the supertype row) with a
/// subtype row carrying the skeleton and the stored path counts. Which geometry a row gets is
/// decided per row: the stored skeleton at overview zooms, the bbox-clipped survey geometry
/// once the viewport is small enough. Two guards run on stored counts alone, so an oversized
/// centerline never gets fetched out of TOAST storage to find out how big it is: the low-zoom
/// gate, and a running per-request budget.
/// </para>
/// </summary>
public static class CenterlineMapSql
{
    /// <summary>
    /// Centerline feature ids whose bounding box meets the viewport, visibility-filtered.
    /// The <c>&amp;&amp;</c> operator answers from the GIST index alone, so this never pulls
    /// a multi-megabyte geometry out of TOAST storage — it exists so the location-protection
    /// rule can be evaluated (in Domain, where it lives) before any geometry is produced.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> CenterlineIdsInViewAsync(
        SilexGisDbContext db, AccessContext ctx, Bbox box, CancellationToken ct)
    {
        var (visibilitySql, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        parameters.Add("west", box.West);
        parameters.Add("south", box.South);
        parameters.Add("east", box.East);
        parameters.Add("north", box.North);

        var sql = $"""
            SELECT f.id
            FROM features f
            WHERE f.kind = {(short)FeatureKind.Centerline}
              AND f.deleted_at IS NULL
              AND f.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
              AND {visibilitySql}
            """;

        var connection = db.Database.GetDbConnection();
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    public static async Task<IReadOnlyList<CenterlineMapRow>> QueryAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        bool detail,
        bool withZ,
        int maxPaths,
        bool gateActive,
        int gatePaths,
        double simplifyToleranceDegrees,
        IReadOnlyCollection<Guid> exactViewCenterlineIds,
        CancellationToken ct)
    {
        var (visibilitySql, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        parameters.Add("west", box.West);
        parameters.Add("south", box.South);
        parameters.Add("east", box.East);
        parameters.Add("north", box.North);
        parameters.Add("detail", detail);
        parameters.Add("with_z", withZ);
        parameters.Add("max_paths", maxPaths);
        parameters.Add("gate_active", gateActive);
        parameters.Add("gate_paths", gatePaths);
        parameters.Add("tolerance", simplifyToleranceDegrees);
        // Only centerlines that PASSED the caller's exact-view check (evaluated in Domain
        // over each feature's protected roots, before this query runs) are eligible, so
        // their geometry is never even produced for anyone else. They are also not counted
        // anywhere in the response: a centerline traces the cave's exact position, so its
        // existence is not disclosed.
        //
        // An allow-list, deliberately: this query runs after the id and protection reads,
        // in its own statement. A row created — or newly protected — in between is absent
        // from the allow-list and is simply not served, whereas a deny-list would serve it
        // at full survey precision without ever having checked it.
        parameters.Add("exact_ids", AccessSql.UuidArray([.. exactViewCenterlineIds]));

        // ST_ClipByBox2D is 2D-only, hence the ST_Force2D first; it is also the reason the
        // skeleton is stored flat. The clip box is the viewport, so a stroke whose line leaves
        // the screen still has a coordinate to run to.
        //
        // The altitude-preserving variant (@with_z) cannot use that clip at all. Fed a 3D
        // geometry, ST_ClipByBox2D neither errors nor drops the Z flag: it keeps Z on the
        // vertices it passes through and writes NaN on every vertex it creates at the box
        // boundary. ST_AsGeoJSON then emits a literal NaN, which is not valid JSON, so the
        // response would fail to parse. Two alternatives were measured on an 80,000-component
        // survey geometry with a box covering a quarter of its extent:
        //
        //   ST_CollectionExtract(ST_Intersection(geom, box), 2)  ~215-250 ms, Z interpolated
        //   ST_Collect over ST_Dump(geom) filtered by `&&`        ~70-85 ms,  Z exact
        //   (the 2D clip above, for scale)                        ~60-68 ms,  no Z
        //
        // The per-component filter wins twice. It costs a fraction of the intersection, and it
        // never cuts a component, so every altitude it returns is one a surveyor measured rather
        // than one interpolated along a shot. Its price is over-reach: a component crossing the
        // viewport edge comes back whole, and `&&` is a bounding-box test so a diagonal shot
        // whose box meets the viewport is kept even if its line does not. Survey exports are one
        // short shot per component, which makes both effects negligible; a single long imported
        // polyline is the bad case, and the existing 2D clip already accepts over-reach at the
        // edge for the same drawing reason. ST_Intersection has a second problem the filter does
        // not: a component that merely touches the box yields a point, turning the result into a
        // GEOMETRYCOLLECTION whose GeoJSON has no `coordinates` member at all.
        //
        // ST_Collect returns NULL when nothing matches, whereas the 2D clip returns an empty
        // geometry — hence the COALESCE, without which an off-screen cave would be indistinguishable
        // from a gated one and would wrongly fall back to its skeleton.
        //
        // The budget is a running total over the cheapest rows first: with several caves in
        // view, the small ones all get drawn and the one that would blow the request is the one
        // reported as withheld.
        // Every CTE here is MATERIALIZED on purpose. Inlined (the default since PostgreSQL 12)
        // the clip expression is substituted into each place that references it and re-evaluated
        // — measured at 707 ms for one real cave against 130 ms once materialised.
        var sql = $"""
            WITH src AS MATERIALIZED (
                SELECT f.id, c.cave_feature_id AS cave_id, f.name, c.length_m,
                       (@gate_active AND COALESCE(c.skeleton_path_count, c.path_count) > @gate_paths) AS gated,
                       CASE
                           WHEN @gate_active AND COALESCE(c.skeleton_path_count, c.path_count) > @gate_paths
                               THEN NULL
                           WHEN @detail AND @with_z
                               THEN CASE
                                        -- Whole centerline inside the viewport: nothing to clip,
                                        -- and skipping the dump is the cheapest branch there is.
                                        WHEN f.geom @ ST_MakeEnvelope(@west, @south, @east, @north, 4326)
                                            THEN f.geom
                                        ELSE COALESCE(
                                            (SELECT ST_Collect(d.geom)
                                             FROM ST_Dump(f.geom) d
                                             WHERE d.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)),
                                            ST_SetSRID('MULTILINESTRING EMPTY'::geometry, 4326))
                                    END
                           WHEN @detail
                               THEN ST_ClipByBox2D(
                                    ST_Force2D(f.geom),
                                    ST_MakeEnvelope(@west, @south, @east, @north, 4326))
                       END AS detail_g,
                       CASE
                           WHEN @gate_active AND COALESCE(c.skeleton_path_count, c.path_count) > @gate_paths
                               THEN NULL
                           -- The stored skeleton is a typed 2D column and cannot carry altitude,
                           -- so an overview row is flat even under @with_z. It is still served —
                           -- blanking the overlay at exactly the zooms where the skeleton is the
                           -- only affordable representation would be worse — and the row reports
                           -- what it carries so the caller is never left guessing. When there is
                           -- no stored skeleton the survey geometry IS the overview, and under
                           -- @with_z it keeps its altitudes.
                           ELSE COALESCE(
                               c.skeleton,
                               CASE WHEN @with_z THEN f.geom ELSE ST_Force2D(f.geom) END)
                       END AS overview_g,
                       -- The highest altitude in the WHOLE centerline, not in the part being
                       -- served. A caller drawing a survey against a globe has to know where the
                       -- cave meets the ground, and the payload cannot tell it: at detail zoom the
                       -- geometry is cut to the viewport, so the highest point within it changes
                       -- as the viewer pans and anchoring to that would slide the whole survey up
                       -- and down. The guards keep this free: it is asked only of rows that will
                       -- carry altitudes at all, so a row served from the flat stored skeleton
                       -- never pulls the survey geometry out of storage to answer it, and a gated
                       -- row — which is served no geometry — is not asked either.
                       CASE
                           WHEN @with_z
                                AND NOT (@gate_active AND COALESCE(c.skeleton_path_count, c.path_count) > @gate_paths)
                                AND (@detail OR c.skeleton IS NULL)
                               THEN ST_ZMax(f.geom)
                       END AS top_z
                FROM features f
                JOIN centerlines c ON c.id = f.id
                WHERE f.deleted_at IS NULL
                  AND f.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
                  AND f.id = ANY(@exact_ids)
                  AND {visibilitySql}
            ),
            counted AS MATERIALIZED (
                SELECT src.*,
                       CASE WHEN detail_g IS NULL THEN NULL ELSE ST_NumGeometries(detail_g) END AS detail_paths
                FROM src
            ),
            -- Detail that does not fit the budget falls back to the skeleton for that cave. The
            -- alternative — dropping it — would blank an overlay that had been perfectly usable
            -- one zoom level out, which is worse than showing less of it.
            chosen AS MATERIALIZED (
                SELECT id, cave_id, name, length_m, gated, top_z,
                       COALESCE(detail_paths > 0 AND detail_paths <= @max_paths, false) AS detail,
                       CASE WHEN detail_paths > 0 AND detail_paths <= @max_paths THEN detail_g
                            -- An empty clip at detail zoom means the cave is off-screen; falling
                            -- back would redraw the whole of it outside the viewport.
                            WHEN detail_paths = 0 THEN NULL
                            ELSE overview_g
                       END AS g
                FROM counted
            ),
            sized AS MATERIALIZED (
                SELECT id, cave_id, name, length_m, gated, detail, g, top_z,
                       CASE WHEN g IS NULL THEN 0 ELSE ST_NumGeometries(g) END AS paths,
                       -- Asked of the geometry itself rather than inferred from which branch
                       -- produced it: an uploaded 2D survey is stored with a zero Z ordinate,
                       -- and a row that predates that rule has none at all.
                       COALESCE(ST_NDims(g) = 3, false) AS has_z
                FROM chosen
            ),
            ranked AS MATERIALIZED (
                SELECT sized.*,
                       SUM(CASE WHEN gated THEN 0 ELSE paths END)
                           OVER (ORDER BY paths, id ROWS UNBOUNDED PRECEDING) AS running
                FROM sized
            )
            SELECT id AS "Id",
                   cave_id AS "CaveId",
                   name AS "Name",
                   length_m AS "LengthM",
                   paths AS "Paths",
                   detail AS "Detail",
                   (NOT gated AND paths > 0 AND running <= @max_paths) AS "Included",
                   (gated OR (paths > 0 AND running > @max_paths)) AS "Withheld",
                   has_z AS "HasZ",
                   top_z AS "TopZ",
                   CASE WHEN NOT gated AND paths > 0 AND running <= @max_paths
                        THEN ST_AsGeoJSON(ST_Simplify(g, @tolerance), 15)
                   END AS "GeoJson"
            FROM ranked
            ORDER BY paths, id
            """;

        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<CenterlineMapRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }
}
