// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SilexGis.Api.Tests;

/// <summary>
/// Performance fixture: 50k synthetic entrances must stay browsable.
/// Bounds are deliberately generous (CI variance); timings are logged for trend-watching.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PerformanceTests : IDisposable
{
    private const int CaveCount = 1000;
    private const int EntrancesPerCave = 50; // 50k total
    private const long BudgetMs = 1000;

    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;

    public PerformanceTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    [Fact]
    public async Task Endpoints_stay_responsive_with_50k_entrances()
    {
        var email = $"perf-{Guid.NewGuid():N}@t.local";
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        await SeedSyntheticAsync(ownerId);
        using var client = await AuthHelper.BearerClientAsync(factory, email);

        // Wide-area clusters, focused points, table page, indexed search.
        var requests = new (string Name, string Url)[]
        {
            ("clusters z7 (country)", "/api/v1/map/cave-entrances?bbox=19.9,43.5,29.1,48.1&zoom=7"),
            ("points z14 (local)", "/api/v1/map/cave-entrances?bbox=24.9,45.4,25.15,45.6&zoom=14"),
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

        await AssertLocalBboxUsesSpatialIndexAsync();
    }

    /// <summary>
    /// The wall-clock budget above catches regressions loosely; this pins the reason the map
    /// scales — a focused bbox must ride the GIST index on cave_entrances.geom, never a
    /// sequential scan of all 50k rows. EXPLAIN ANALYZE is the durable contract here.
    /// </summary>
    private async Task AssertLocalBboxUsesSpatialIndexAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var planLines = await connection.QueryAsync<string>("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT e.geom
            FROM cave_entrances e
            JOIN caves c ON c.id = e.cave_id
            WHERE c.deleted_at IS NULL
              AND e.geom && ST_MakeEnvelope(24.9, 45.4, 25.15, 45.6, 4326)
            """);
        var plan = string.Join("\n", planLines);
        output.WriteLine(plan);

        plan.ShouldContain("ix_cave_entrances_geom", Case.Insensitive,
            "the map bbox filter must use the spatial index, not scan every entrance");
        plan.ShouldNotContain("Seq Scan on cave_entrances", Case.Insensitive);
    }

    private async Task SeedSyntheticAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));

        var stopwatch = Stopwatch.StartNew();
        await db.Database.ExecuteSqlAsync($"""
            WITH new_caves AS (
                INSERT INTO caves (
                    id, name, cave_type_id, exploration_status, is_show_cave,
                    location_protected, entrance_count, owner_user_id, visibility,
                    created_at, updated_at)
                SELECT
                    gen_random_uuid(), 'Perf Cave ' || g,
                    (SELECT id FROM cave_types WHERE code = 'cave'),
                    0, false, false, {EntrancesPerCave}, {ownerId},
                    {(short)Visibility.Authenticated}, now(), now()
                FROM generate_series(1, {CaveCount}) g
                RETURNING id
            )
            INSERT INTO cave_entrances (
                id, cave_id, entrance_type_id, is_main, geom, position_quality,
                created_at, updated_at)
            SELECT
                gen_random_uuid(), c.id,
                (SELECT id FROM entrance_types WHERE code = 'natural'),
                e = 1,
                ST_SetSRID(ST_MakePoint(20 + random() * 9, 43.6 + random() * 4.4), 4326),
                1, now(), now()
            FROM new_caves c, generate_series(1, {EntrancesPerCave}) e
            """);

        // Fresh bulk-loaded tables have no statistics yet (autoanalyze hasn't run) and the
        // planner picks pathological plans (measured 50x). Real databases are analyzed.
        await db.Database.ExecuteSqlRawAsync("ANALYZE caves; ANALYZE cave_entrances;");
        output.WriteLine($"seeded {CaveCount * EntrancesPerCave} entrances in {stopwatch.ElapsedMilliseconds} ms");
    }

    public void Dispose() => factory.Dispose();
}
