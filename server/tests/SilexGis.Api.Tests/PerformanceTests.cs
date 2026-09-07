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
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;
using Xunit.Abstractions;

namespace SilexGis.Api.Tests;

/// <summary>
/// Performance fixture: 100k synthetic entrance features must stay browsable.
/// Bounds are deliberately generous (CI variance); timings are logged for trend-watching.
/// The wall-clock budget catches regressions loosely — the EXPLAIN pins below state the
/// reason the map scales, and those are the durable contract.
/// </summary>
public sealed class PerformanceTests : IDisposable, IClassFixture<PostgresFixture>
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

    /// <summary>
    /// The box the seeded entrances are scattered across, at full double precision and with no
    /// location protection on them.
    /// </summary>
    /// <remarks>
    /// Public because another class in this assembly depends on it and cannot see that it does.
    /// A test that asserts no protected coordinate reaches a caller has to search whole payloads
    /// for the digits of its own point, and a hundred thousand unprotected points scattered here
    /// will sooner or later put those same digits in a payload legitimately — which that search
    /// cannot tell from a disclosure. Its point therefore has to sit outside this box, and it
    /// asserts that against these constants so that widening the box fails there immediately,
    /// naming the real cause, rather than surfacing later as an apparent security regression.
    /// </remarks>
    public const double SeedWest = 20;
    public const double SeedEast = 29;
    public const double SeedSouth = 43.6;
    public const double SeedNorth = 48;

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
            // The only request here whose bbox matches far more rows than the layer will
            // return: the feature layer does not cluster, so a country-wide view is what
            // makes the point cap bite, and the cap orders before it truncates. Ordering a
            // large match set is exactly where the planner could abandon the geometry index
            // for the primary key, so the capped path needs a measurement of its own.
            ("features layer (country, capped)",
                "/api/v1/map/features?bbox=19.9,43.5,29.1,48.1&kinds=caveEntrance"),
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
        await AssertSurveySubstratePlansAsync(strangerId);
    }

    /// <summary>
    /// The two cave-scoped lookups a cave's survey statistics are computed over. Both splice the
    /// whole access walk over the feature table, and both are given a single cave id — so the
    /// feature row they need must be reached by its primary key, and the protection-root probe
    /// beside it by an index. A sequential scan of a hundred thousand features to answer a
    /// question about one cave is the regression this pins, and it is the shape that appears
    /// silently as an installation's feature table grows rather than at the moment it is written.
    /// </summary>
    /// <remarks>
    /// The seeded caves hold no survey models and no centerlines, so these plans are the ones the
    /// planner chooses for an empty result — which is why each pin also asserts that the feature
    /// lookup is <i>in</i> the plan. Without that half, a plan that never reached the feature
    /// table at all would satisfy "no sequential scan of features" while proving nothing.
    /// </remarks>
    private async Task AssertSurveySubstratePlansAsync(Guid strangerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var caveId = await connection.QuerySingleAsync<Guid>(
            $"SELECT id FROM features WHERE kind = {(short)FeatureKind.Cave} LIMIT 1");

        // Not the owner and holding nothing: the caller for whom both arms of the access walk
        // actually run rather than short-circuiting on ownership.
        var ctx = new AccessContext(strangerId, false, [], []);

        var (legSql, legParameters) = SurveySegmentSql.BuildForCave(ctx, caveId);
        var legs = await ExplainAsync(connection, legParameters, "survey leg substrate", legSql);
        AssertReachesFeaturesByIndex(legs, "survey leg substrate");

        // The step that picks the one survey model answering for this cave is cave-scoped too, and
        // both of the tables it reads grow with the installation rather than with the cave.
        legs.ShouldNotContain("Seq Scan on survey_models", Case.Insensitive,
            "choosing which survey model answers must not scan every model in the installation");
        legs.ShouldNotContain("Seq Scan on centerlines", Case.Insensitive,
            "nor every centerline in it");

        var (sourceSql, sourceParameters) = CenterlineSegmentSql.BuildSourceQuery(ctx, caveId);
        var source = await ExplainAsync(
            connection, sourceParameters, "centerline substrate source", sourceSql);
        AssertReachesFeaturesByIndex(source, "centerline substrate source");
    }

    /// <summary>
    /// The two halves of a plan pin over a cave-scoped feature lookup: no sequential scan of the
    /// feature table, and evidence that the feature table was reached at all. The second half is
    /// what keeps the first from being satisfied by a plan that never got there.
    /// </summary>
    /// <remarks>
    /// Either of the two id-keyed indexes on <c>features</c> counts: the lookup that carries a
    /// kind rides the id+kind alternate key, one without a kind rides the primary key, and both
    /// are index probes on one row. Note that neither of them is the geometry index — a lookup by
    /// id has no geometry in it to index — so a plan pin phrased in terms of the GIST index would
    /// be pinning something these queries never touch.
    /// </remarks>
    private static void AssertReachesFeaturesByIndex(string plan, string label)
    {
        plan.ShouldNotContain("Seq Scan on features", Case.Insensitive,
            $"{label}: a question about one cave must never scan the whole feature table");

        (plan.Contains("ak_features_id_kind", StringComparison.OrdinalIgnoreCase)
                || plan.Contains("pk_features", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue(
                $"{label}: the cave's own feature row must be reached through an id-keyed index — "
                + "and if neither appears, the plan never touched the feature table at all, which "
                + "would leave the assertion above satisfied while proving nothing");
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

        // Not the owner, in no caving group, holding no entries: visibility is carried
        // entirely by the read-time inheritance arm (the entrance rows are PRIVATE and
        // admit only through their cave), and the exact-view fragment's protection-root
        // lookup runs for real on every row the bbox returns — the work these pins are about.
        var ctx = new AccessContext(strangerId, false, [], []);

        // The caving group-id set must reach PostgreSQL as a single uuid[] placeholder. Dapper's
        // default handling of an array expands it into one placeholder per element, which
        // turns the ANY test into a per-row construct (measured 50x slower at this size).
        // Built as a pair, over one parameter set: this is the shape the entrance layer
        // itself uses, and the only one that can be spliced into a single statement.
        var (visibilitySql, exactSql, visibilityParameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        visibilitySql.ShouldContain("= ANY(@acc_caving_group_ids)", Case.Sensitive,
            "the visibility fragment must pass caving group ids as ONE uuid[] parameter");

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
        AssertAncestorProbeIsIndexed(clusters, "clusters (guarded)");

        // The point layer above the cluster zoom asks the same question through
        // ST_Intersects (the operator the EF query produces); it must reach the same index.
        var (pointsVisibilitySql, pointsVisibilityParameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
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
        AssertAncestorProbeIsIndexed(points, "points (guarded)");

        // Batched id lookup: the shape protection and DTO enrichment use after a layer query
        // has produced its rows. The id set is one uuid[] parameter, and 50 ids out of 100k
        // rows must be a primary-key lookup.
        var sampleIds = (await connection.QueryAsync<Guid>(
                "SELECT id FROM features WHERE kind = 2 AND owner_user_id = @owner LIMIT 50",
                new { owner = ownerId }))
            .ToArray();
        sampleIds.Length.ShouldBe(50);

        var idParameters = new DynamicParameters();
        idParameters.Add("ids", AccessSql.UuidArray(sampleIds));
        var batch = await ExplainAsync(connection, idParameters, "id batch", """
            SELECT f.id, f.ancestor_ids
            FROM features f
            WHERE f.id = ANY(@ids)
            """);
        batch.ShouldContain("pk_features", Case.Insensitive,
            "a batched id lookup must ride the primary key, not scan the feature table");
        batch.ShouldNotContain("Seq Scan on features", Case.Insensitive);

        // An entry-holding caller gets the full level-walk CASE instead of the lean
        // built-ins-only shape; its arms may cost rows, never access paths — the bbox
        // must keep riding the entrance index with the whole chain in place.
        var entryCtx = new AccessContext(strangerId, false, [Guid.NewGuid()],
        [
            new AccessEntrySnapshot(1, Guid.NewGuid(), null, null, AccessEffect.Deny,
                AccessDomain.Features, AccessAction.Read, AccessScopeKind.Object, Guid.NewGuid(), null, null, null),
            new AccessEntrySnapshot(2, Guid.NewGuid(), null, null, AccessEffect.Allow,
                AccessDomain.Features, AccessAction.Read, AccessScopeKind.Subtree, Guid.NewGuid(), null, null, null),
            new AccessEntrySnapshot(3, Guid.NewGuid(), null, null, AccessEffect.Allow,
                AccessDomain.Features, AccessAction.Read, AccessScopeKind.FeatureSet, null, Guid.NewGuid(), null, null),
            new AccessEntrySnapshot(4, Guid.NewGuid(), null, null, AccessEffect.Allow,
                AccessDomain.Features, AccessAction.Read, AccessScopeKind.All, null, null, null, null),
        ]);
        var (walkSql, walkParameters) = AccessSql.FeatureVisibleToFragment(entryCtx, "f");
        AddViewport(walkParameters);
        var walk = await ExplainAsync(connection, walkParameters, "points (guarded, entry-holding)", $"""
            SELECT count(*)
            FROM features f
            WHERE f.kind = 2
              AND f.deleted_at IS NULL
              AND ST_Intersects(f.geom, ST_MakeEnvelope(@west, @south, @east, @north, 4326))
              AND {walkSql}
            """);
        AssertRidesEntranceIndex(walk, "points (guarded, entry-holding)");
    }

    /// <summary>
    /// The read-time visibility-inheritance arm (and the exact-view rule beside it) probe
    /// the ancestor rows once per candidate: <c>EXISTS(… WHERE anc.id = ANY(f.ancestor_ids))</c>.
    /// That probe must be an index probe — the plan prints its <c>Index Cond</c> — never a
    /// per-row scan of the feature table. With the probe index-served at 100k rows
    /// (~6 µs/row measured), `ancestor_ids` itself needs no GIN index: the array is only
    /// ever the argument of the probe, not the driving side of a lookup.
    /// </summary>
    private static void AssertAncestorProbeIsIndexed(string plan, string label)
    {
        plan.ShouldContain("Index Cond: (id = ANY (f.ancestor_ids))", Case.Sensitive,
            $"{label}: the visibility-inheritance EXISTS arm must probe ancestors through an "
            + "index, not scan for them");
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
    /// the cave, effective protection, and the cave mirror (entrance count and the cave
    /// feature's representative point = its main entrance). Entrance rows are seeded
    /// PRIVATE under authenticated caves — the shape the read-time visibility cascade
    /// produces in real data, and the worst case for the inheritance arm these pins guard.
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
                    ST_SetSRID(
                        ST_MakePoint(
                            {SeedWest} + random() * {SeedEast - SeedWest},
                            {SeedSouth} + random() * {SeedNorth - SeedSouth}),
                        4326) AS geom
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
                    {(short)Visibility.Private},
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

        // One survey model per seeded cave, so the plan pins over the survey tables measure
        // something. They cannot otherwise: a pin forbidding a sequential scan of an EMPTY table
        // asserts nothing about scale — the planner is right to scan nothing sequentially, and
        // whether it says so depends on when autoanalyze last ran, which made the pin fail or
        // pass by luck. The point of those pins is that choosing one cave's survey must not walk
        // every survey in the installation, and only a populated table can show that.
        //
        // Every model points at the same stored file. The file is real enough to satisfy its
        // foreign keys (a document and one revision) and nothing here reads its bytes, because
        // what is being measured is which rows the planner visits, not what they contain.
        await db.Database.ExecuteSqlAsync(
            $$"""
            WITH doc AS (
                INSERT INTO documents (
                    id, title, metadata, owner_user_id, visibility, created_at, updated_at)
                VALUES (
                    gen_random_uuid(), 'Perf survey source', '{}', {{ownerId}},
                    {{(short)Visibility.Authenticated}}, now(), now())
                RETURNING id
            ),
            version AS (
                INSERT INTO document_versions (
                    id, document_id, version_number, is_current, created_at, updated_at)
                SELECT gen_random_uuid(), doc.id, 1, true, now(), now() FROM doc
                RETURNING id
            ),
            file AS (
                INSERT INTO files (
                    id, storage_path, original_name, mime_type, sha256, size_bytes,
                    document_version_id, kind, metadata, position_source, conversion,
                    text_extraction, direction_is_magnetic, orientation_quarter_turns,
                    created_at, updated_at)
                SELECT
                    gen_random_uuid(), 'perf/none', 'perf.3d', 'application/octet-stream',
                    repeat('0', 64), 0, version.id, {{(short)FileKind.Survey}}, '{}',
                    0, 0, 0, false, 0, now(), now()
                FROM version
                RETURNING id
            ),
            centerline_ids AS MATERIALIZED (
                SELECT gen_random_uuid() AS centerline_id, c.id AS cave_id, c.geom
                FROM features c
                WHERE c.kind = {{(short)FeatureKind.Cave}}
                  AND c.name LIKE 'Perf Cave %'
                  AND c.geom IS NOT NULL
            ),
            centerline_features AS (
                INSERT INTO features (
                    id, kind, category, name, geom, location_protected, is_protected_effective,
                    ancestor_ids, owner_user_id, visibility, created_at, updated_at)
                SELECT
                    ci.centerline_id, {{(short)FeatureKind.Centerline}},
                    {{(short)FeatureCategory.Underground}}, 'Perf centerline',
                    ST_Force3D(ST_Multi(ST_MakeLine(ci.geom, ST_Translate(ci.geom, 0.001, 0.001)))),
                    false, false,
                    ARRAY[ci.centerline_id, ci.cave_id],
                    {{ownerId}}, {{(short)Visibility.Authenticated}}, now(), now()
                FROM centerline_ids ci
                RETURNING id
            ),
            centerline_rows AS (
                INSERT INTO centerlines (
                    id, cave_feature_id, kind, is_default, skeleton, path_count, source, length_m)
                SELECT
                    ci.centerline_id, ci.cave_id, {{(short)FeatureKind.Centerline}}, true,
                    ST_Multi(ST_MakeLine(ci.geom, ST_Translate(ci.geom, 0.001, 0.001))), 1,
                    {{(short)CenterlineSource.Uploaded}}, 100
                FROM centerline_ids ci
                RETURNING id
            ),
            centerline_edges AS (
                INSERT INTO feature_hierarchy_edges (
                    parent_id, child_id, is_primary, created_at, updated_at)
                SELECT ci.cave_id, ci.centerline_id, true, now(), now()
                FROM centerline_ids ci
                RETURNING id
            ),
            centerline_closure AS (
                INSERT INTO feature_ancestors (feature_id, ancestor_id)
                SELECT ci.centerline_id, ci.centerline_id FROM centerline_ids ci
                UNION ALL
                SELECT ci.centerline_id, ci.cave_id FROM centerline_ids ci
                RETURNING feature_id
            )
            INSERT INTO survey_models (
                id, cave_feature_id, name, file_id, format, status,
                source_precision_lost, created_at, updated_at)
            SELECT
                gen_random_uuid(), f.id, 'Perf survey', file.id,
                {{(short)SurveyModelFormat.Survex3d}}, {{(short)SurveyModelStatus.Ready}},
                false, now(), now()
            FROM features f, file
            WHERE f.kind = {{(short)FeatureKind.Cave}}
              AND f.name LIKE 'Perf Cave %'
            """);

        // Fresh bulk-loaded tables have no statistics yet (autoanalyze hasn't run) and the
        // planner picks pathological plans (measured 50x). Real databases are analyzed.
        await db.Database.ExecuteSqlRawAsync(
            "ANALYZE features; ANALYZE caves; ANALYZE cave_entrances; "
            + "ANALYZE feature_hierarchy_edges; ANALYZE feature_ancestors; "
            + "ANALYZE survey_models; ANALYZE centerlines; "
            + "ANALYZE access_entries; ANALYZE permission_group_members; ANALYZE feature_set_members;");

        output.WriteLine(
            $"seeded {CaveCount * EntrancesPerCave} entrance features under {CaveCount} caves "
            + $"in {stopwatch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// What a caller pays, on every request, for rules written onto one row at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sharing a camp writes one rule onto each trip the camp gathered, so a club that has been
    /// lent three fortnight camps of forty trips holds a hundred and twenty such rules. Every
    /// one of them is read back on every request that camp's grantees make — the whole set the
    /// caller holds is loaded once, flattened into an id array per domain and action, and that
    /// array is spliced into every statement that lists trips: the list itself, the dashboard,
    /// the map, search, statistics, the leads board and the timeline.
    /// </para>
    /// <para>
    /// Two costs, measured separately because they are paid in different places: loading and
    /// materialising the rows (once per request) and carrying the array through the guard
    /// (once per statement). Both are logged; what is asserted is the durable property — the
    /// ids ride as ONE native uuid[] parameter, not as a hundred and twenty placeholders, and
    /// the guard's shape does not change with the size of the set.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Rules_written_onto_single_trips_ride_one_array_into_every_trip_query()
    {
        const int TripCount = 2000;
        const int CampSize = 40;
        const int Camps = 3;

        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"cost-own-{Guid.NewGuid():N}@t.local");
        // Holds nothing anywhere except the rules the camps wrote: a Viewer, because the
        // seeded Editors ruleset reads trips at the all-scope and would carry no ids at all.
        var holderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"cost-hold-{Guid.NewGuid():N}@t.local");
        var emptyHandedId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"cost-none-{Guid.NewGuid():N}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var tripIds = new List<Guid>(TripCount);
        for (var i = 0; i < TripCount; i++)
        {
            var trip = new TripLog
            {
                Title = $"Cost trip {i}",
                TripDate = new DateOnly(2026, 1, 1).AddDays(i % 365),
                OwnerUserId = ownerId,
                Visibility = Visibility.Private,
            };
            tripIds.Add(trip.Id);
            db.TripLogs.Add(trip);
        }

        for (var i = 0; i < Camps * CampSize; i++)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = holderId,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.TripLogs,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.Object,
                ScopeId = tripIds[i],
            });
        }

        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ANALYZE trip_logs; ANALYZE access_entries;");

        var bare = await MeasureResolveAsync(db, emptyHandedId);
        var loaded = await MeasureResolveAsync(db, holderId);
        output.WriteLine(
            $"context resolve: holding nothing {bare} ms, holding {Camps * CampSize} "
            + $"object rules {loaded} ms");

        var bareCtx = await AccessContextResolver.ResolveAsync(db, emptyHandedId);
        var loadedCtx = await AccessContextResolver.ResolveAsync(db, holderId);
        loadedCtx.For(AccessDomain.TripLogs, AccessAction.Read).AllowObjectIds.Length
            .ShouldBe(Camps * CampSize, "every rule the camps wrote is carried into the filter");

        var (bareSql, bareParameters) = AccessSql.VisibleToFragment(bareCtx, AccessDomain.TripLogs, "t");
        var (loadedSql, loadedParameters) = AccessSql.VisibleToFragment(loadedCtx, AccessDomain.TripLogs, "t");

        loadedSql.ShouldContain("= ANY(@acc_r_allow_obj)", Case.Sensitive,
            "the ids must arrive as one native array parameter — expanded into a placeholder "
            + "each, the ANY test was measured 50x slower");

        // The set grows the array, never the statement. A caller holding one such rule and a
        // caller holding a hundred and twenty send PostgreSQL the same text, so the plan is
        // cached once and the ids are data. (The caller holding none sends a shorter one: the
        // guard drops to its lean shape when there is nothing to consult, which is the point
        // of that branch and not a regression.)
        var singleCtx = new AccessContext(holderId, false, [],
        [
            new AccessEntrySnapshot(1, null, AccessSubjectKind.User, holderId, AccessEffect.Allow,
                AccessDomain.TripLogs, AccessAction.Read, AccessScopeKind.Object, null, tripIds[0], null, null),
        ]);
        var (singleSql, _) = AccessSql.VisibleToFragment(singleCtx, AccessDomain.TripLogs, "t");
        loadedSql.Length.ShouldBe(singleSql.Length,
            "the statement's shape is the same whatever the set holds; only the array grows");

        var bareCount = $"SELECT count(*) FROM trip_logs t WHERE {bareSql}";
        var loadedCount = $"SELECT count(*) FROM trip_logs t WHERE {loadedSql}";
        await ExplainAsync(connection, bareParameters, "trips (guarded, no object rules)", bareCount);
        await ExplainAsync(
            connection, loadedParameters, $"trips (guarded, {Camps * CampSize} object rules)", loadedCount);

        var withoutMs = await MeasureQueryAsync(connection, bareParameters, bareCount);
        var withMs = await MeasureQueryAsync(connection, loadedParameters, loadedCount);
        output.WriteLine(
            $"trip count over {TripCount} rows: no object rules {withoutMs} ms, "
            + $"{Camps * CampSize} object rules {withMs} ms");
    }

    private static async Task<double> MeasureResolveAsync(SilexGisDbContext db, Guid userId)
    {
        await AccessContextResolver.ResolveAsync(db, userId);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            await AccessContextResolver.ResolveAsync(db, userId);
        }

        return Math.Round(stopwatch.Elapsed.TotalMilliseconds / 10, 2);
    }

    private static async Task<double> MeasureQueryAsync(
        DbConnection connection, DynamicParameters parameters, string sql)
    {
        await connection.ExecuteScalarAsync<long>(sql, parameters);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            await connection.ExecuteScalarAsync<long>(sql, parameters);
        }

        return Math.Round(stopwatch.Elapsed.TotalMilliseconds / 10, 2);
    }

    public void Dispose() => factory.Dispose();
}
