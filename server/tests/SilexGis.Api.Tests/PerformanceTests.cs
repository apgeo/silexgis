// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SilexGis.Api.Tests;

/// <summary>
/// Performance fixture: 100k synthetic entrance features must stay browsable.
/// Bounds are deliberately generous (CI variance); timings are logged for trend-watching.
/// The wall-clock budget catches regressions loosely — the EXPLAIN pins below state the
/// reason the map scales, and those are the durable contract.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PerformanceTests : IDisposable
{
    private const int CaveCount = 2000;
    private const int EntrancesPerCave = 50; // 100k entrance features
    private const long BudgetMs = 1000;

    /// <summary>
    /// The focused viewport every plan pin uses — roughly a thousandth of the seeded extent,
    /// so a sequential scan of the whole feature table is never the sane plan for it.
    /// </summary>
    private const double West = 24.9;
    private const double South = 45.4;
    private const double East = 25.15;
    private const double North = 45.6;

    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;

    public PerformanceTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    [Fact]
    public async Task Endpoints_stay_responsive_with_100k_entrance_features()
    {
        var email = $"perf-{Guid.NewGuid():N}@t.local";
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        // The plan pins run as somebody who owns none of the rows: that is the caller for
        // whom the guards actually do work (the owner arm of both fragments short-circuits).
        var strangerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"perf-view-{Guid.NewGuid():N}@t.local");
        await SeedSyntheticAsync(ownerId);
        using var client = await AuthHelper.BearerClientAsync(factory, email);

        // Wide-area clusters, focused points, the cross-kind layer, table page, indexed search.
        var requests = new (string Name, string Url)[]
        {
            ("clusters z7 (country)", "/api/v1/map/cave-entrances?bbox=19.9,43.5,29.1,48.1&zoom=7"),
            ("points z14 (local)", $"/api/v1/map/cave-entrances?bbox={Viewport}&zoom=14"),
            ("features layer z14", $"/api/v1/map/features?bbox={Viewport}&kinds=caveEntrance"),
            ("caves list p1", "/api/v1/caves?pageSize=50"),
            ("search 'perf'", "/api/v1/search?q=perf"),
        };

        foreach (var (name, url) in requests)
        {
            // Warmup (connection pool, query plan), then measure.
            (await client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync(url);
            stopwatch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
            output.WriteLine($"{name}: {stopwatch.ElapsedMilliseconds} ms");
            stopwatch.ElapsedMilliseconds.ShouldBeLessThan(BudgetMs, $"{name} exceeded {BudgetMs}ms");
        }

        await AssertEntranceLayerPlansAsync(ownerId, strangerId);
    }

    /// <summary>The viewport as a bbox query argument (invariant — the API parses '.' decimals).</summary>
    private static string Viewport => FormattableString.Invariant($"{West},{South},{East},{North}");

    /// <summary>
    /// The access paths the entrance map layer depends on, pinned against the seeded 100k rows:
    /// <list type="number">
    /// <item>the bbox filter rides <c>ix_features_geom_entrances</c> — the partial GIST index
    /// whose <c>kind = 2</c> predicate keeps the hot layer off every other kind's rows — under
    /// both operators the two entrance paths use (<c>&amp;&amp;</c> for the clustered SQL,
    /// <c>ST_Intersects</c> for the point query);</item>
    /// <item>the security fragments stay filters on top of that index scan: adding them must
    /// not change the access path the unguarded bbox gets, and the exact-view rule's
    /// protection-root lookup must ride the primary key instead of re-scanning per row;</item>
    /// <item>a batched id lookup (how protection and DTO enrichment fetch rows after a layer
    /// query) rides the primary key, with the id set arriving as ONE native <c>uuid[]</c>
    /// parameter — expanded into per-element placeholders it was measured 50x slower.</item>
    /// </list>
    /// </summary>
    private async Task AssertEntranceLayerPlansAsync(Guid ownerId, Guid strangerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        // Not the owner and in no team: the visibility fragment is carried by the rows'
        // Authenticated visibility, and the exact-view fragment's protection-root lookup is
        // evaluated for real on every row the bbox returns — the work these pins are about.
        var user = new UserContext(strangerId, new HashSet<string>(), new Dictionary<Guid, TeamRole>());

        // The team-id set must reach PostgreSQL as a single uuid[] placeholder. Dapper's
        // default handling of an array expands it into one placeholder per element, which
        // turns the ANY test into a per-row construct (measured 50x slower at this size).
        var (visibilitySql, visibilityParameters) = PermissionSql.FeatureVisibleToFragment(user, "f");
        visibilitySql.ShouldContain("= ANY(@vis_team_ids)", Case.Sensitive,
            "the visibility fragment must pass team ids as ONE uuid[] parameter");

        var exactSql = PermissionSql.ExactViewFragment("f");
        AddViewport(visibilityParameters);

        // Baseline: the bbox filter alone. Whatever plan this gets is the plan the guarded
        // query must keep — the security filter is allowed to cost rows, not access paths.
        var baselineParameters = new DynamicParameters();
        AddViewport(baselineParameters);
        var baseline = await ExplainAsync(connection, baselineParameters, "bbox only", """
            SELECT count(*)
            FROM features f
            WHERE f.kind = 2
              AND f.deleted_at IS NULL
              AND f.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
            """);
        AssertRidesEntranceIndex(baseline, "bbox only");

        // The clustered entrance layer's shape: bbox by the && operator, the visibility
        // fragment, and the exact-view fragment deciding per row whether the coordinate is
        // emitted as stored or snapped to the protection grid.
        var clusters = await ExplainAsync(connection, visibilityParameters, "clusters (guarded)", $"""
            SELECT count(*) AS total, count(*) FILTER (WHERE {exactSql}) AS exact_visible
            FROM features f
            WHERE f.kind = 2
              AND f.deleted_at IS NULL
              AND f.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
              AND {visibilitySql}
            """);
        AssertRidesEntranceIndex(clusters, "clusters (guarded)");

        // The point layer above the cluster zoom asks the same question through
        // ST_Intersects (the operator the EF query produces); it must reach the same index.
        var (pointsVisibilitySql, pointsVisibilityParameters) = PermissionSql.FeatureVisibleToFragment(user, "f");
        AddViewport(pointsVisibilityParameters);
        var points = await ExplainAsync(connection, pointsVisibilityParameters, "points (guarded)", $"""
            SELECT count(*)
            FROM features f
            WHERE f.kind = 2
              AND f.deleted_at IS NULL
              AND ST_Intersects(f.geom, ST_MakeEnvelope(@west, @south, @east, @north, 4326))
              AND {pointsVisibilitySql}
            """);
        AssertRidesEntranceIndex(points, "points (guarded)");

        // Batched id lookup: the shape protection and DTO enrichment use after a layer query
        // has produced its rows. The id set is one uuid[] parameter, and 50 ids out of 100k
        // rows must be a primary-key lookup.
        var sampleIds = (await connection.QueryAsync<Guid>(
                "SELECT id FROM features WHERE kind = 2 AND owner_user_id = @owner LIMIT 50",
                new { owner = ownerId }))
            .ToArray();
        sampleIds.Length.ShouldBe(50);

        var idParameters = new DynamicParameters();
        idParameters.Add("ids", PermissionSql.UuidArray(sampleIds));
        var batch = await ExplainAsync(connection, idParameters, "id batch", """
            SELECT f.id, f.ancestor_ids
            FROM features f
            WHERE f.id = ANY(@ids)
            """);
        batch.ShouldContain("pk_features", Case.Insensitive,
            "a batched id lookup must ride the primary key, not scan the feature table");
        batch.ShouldNotContain("Seq Scan on features", Case.Insensitive);
    }

    private static void AssertRidesEntranceIndex(string plan, string label)
    {
        plan.ShouldContain("ix_features_geom_entrances", Case.Insensitive,
            $"{label}: the entrance bbox filter must use the partial GIST index on kind = 2, "
            + "not scan every feature");

        // Covers both the layer's own scan and the exact-view rule's protection-root lookup:
        // a sequential scan of `features` under either alias is the regression this guards.
        plan.ShouldNotContain("Seq Scan on features", Case.Insensitive,
            $"{label}: no path in the entrance layer may sequentially scan the feature table");
    }

    private static void AddViewport(DynamicParameters parameters)
    {
        parameters.Add("west", West);
        parameters.Add("south", South);
        parameters.Add("east", East);
        parameters.Add("north", North);
    }

    private async Task<string> ExplainAsync(
        DbConnection connection, DynamicParameters parameters, string label, string sql)
    {
        var planLines = await connection.QueryAsync<string>($"EXPLAIN (ANALYZE, BUFFERS)\n{sql}", parameters);
        var plan = string.Join("\n", planLines);
        output.WriteLine($"--- {label} ---");
        output.WriteLine(plan);
        return plan;
    }

    /// <summary>
    /// Bulk-loads <see cref="CaveCount"/> caves with <see cref="EntrancesPerCave"/> entrances
    /// each, straight into the supertype tables. The feature write service is the only
    /// sanctioned mutator of the aggregate's derived state, but it is row-at-a-time by design;
    /// at 100k rows this test writes the same end state in one statement instead — so it must
    /// reproduce every invariant the service maintains, or the integrity verifier (which walks
    /// the whole shared database) would report this data as corrupt:
    /// primary containment edge per entrance, ancestor arrays and closure rows carrying self +
    /// the cave, effective protection, the cave mirror (entrance count and the cave feature's
    /// representative point = its main entrance), and the delegated access trio copied onto
    /// every entrance.
    /// </summary>
    private async Task SeedSyntheticAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var stopwatch = Stopwatch.StartNew();

        // The two id/geometry sets are MATERIALIZED so their volatile generators run exactly
        // once: every insert below must see the SAME uuids and the SAME points, and the cave
        // feature's geometry has to be byte-identical to its main entrance's.
        // Data-modifying CTEs all run once per statement and their foreign keys are checked
        // after it, so the six writes need no ordering between them.
        await db.Database.ExecuteSqlAsync($"""
            WITH cave_rows AS MATERIALIZED (
                SELECT gen_random_uuid() AS cave_id, g
                FROM generate_series(1, {CaveCount}) g
            ),
            entrance_rows AS MATERIALIZED (
                SELECT
                    gen_random_uuid() AS entrance_id,
                    c.cave_id,
                    e,
                    ST_SetSRID(ST_MakePoint(20 + random() * 9, 43.6 + random() * 4.4), 4326) AS geom
                FROM cave_rows c, generate_series(1, {EntrancesPerCave}) e
            ),
            cave_features AS (
                INSERT INTO features (
                    id, kind, category, name, geom, location_protected, is_protected_effective,
                    ancestor_ids, owner_user_id, visibility, created_at, updated_at)
                SELECT
                    c.cave_id,
                    {(short)FeatureKind.Cave},
                    {(short)FeatureCategory.Underground},
                    'Perf Cave ' || c.g,
                    main.geom,
                    false, false,
                    ARRAY[c.cave_id],
                    {ownerId},
                    {(short)Visibility.Authenticated},
                    now(), now()
                FROM cave_rows c
                JOIN entrance_rows main ON main.cave_id = c.cave_id AND main.e = 1
                RETURNING id
            ),
            cave_subtypes AS (
                INSERT INTO caves (
                    id, kind, cave_type_id, exploration_status, is_show_cave, entrance_count)
                SELECT
                    c.cave_id,
                    {(short)FeatureKind.Cave},
                    (SELECT id FROM cave_types WHERE code = 'cave'),
                    {(short)ExplorationStatus.Unknown},
                    false,
                    {EntrancesPerCave}
                FROM cave_rows c
                RETURNING id
            ),
            entrance_features AS (
                INSERT INTO features (
                    id, kind, category, geom, location_protected, is_protected_effective,
                    ancestor_ids, owner_user_id, visibility, created_at, updated_at)
                SELECT
                    r.entrance_id,
                    {(short)FeatureKind.CaveEntrance},
                    {(short)FeatureCategory.Surface},
                    r.geom,
                    false, false,
                    ARRAY[r.entrance_id, r.cave_id],
                    {ownerId},
                    {(short)Visibility.Authenticated},
                    now(), now()
                FROM entrance_rows r
                RETURNING id
            ),
            entrance_subtypes AS (
                INSERT INTO cave_entrances (
                    id, kind, cave_feature_id, entrance_type_id, is_main, position_quality)
                SELECT
                    r.entrance_id,
                    {(short)FeatureKind.CaveEntrance},
                    r.cave_id,
                    (SELECT id FROM entrance_types WHERE code = 'natural'),
                    r.e = 1,
                    {(short)PositionQuality.Gps}
                FROM entrance_rows r
                RETURNING id
            ),
            primary_edges AS (
                INSERT INTO feature_hierarchy_edges (
                    parent_id, child_id, is_primary, created_at, updated_at)
                SELECT r.cave_id, r.entrance_id, true, now(), now()
                FROM entrance_rows r
                RETURNING id
            ),
            closure AS (
                INSERT INTO feature_ancestors (feature_id, ancestor_id)
                SELECT c.cave_id, c.cave_id FROM cave_rows c
                UNION ALL
                SELECT r.entrance_id, r.entrance_id FROM entrance_rows r
                UNION ALL
                SELECT r.entrance_id, r.cave_id FROM entrance_rows r
                RETURNING feature_id
            )
            SELECT count(*) FROM closure
            """);

        // Fresh bulk-loaded tables have no statistics yet (autoanalyze hasn't run) and the
        // planner picks pathological plans (measured 50x). Real databases are analyzed.
        await db.Database.ExecuteSqlRawAsync(
            "ANALYZE features; ANALYZE caves; ANALYZE cave_entrances; "
            + "ANALYZE feature_hierarchy_edges; ANALYZE feature_ancestors; ANALYZE object_acl;");

        output.WriteLine(
            $"seeded {CaveCount * EntrancesPerCave} entrance features under {CaveCount} caves "
            + $"in {stopwatch.ElapsedMilliseconds} ms");
    }

    public void Dispose() => factory.Dispose();
}
