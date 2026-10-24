// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Opt-in, reports only: the feature list and the country-wide cluster map for an ordinary member,
/// once through a club holding one grant and once through a club holding a thousand separate cave
/// grants, at a size chosen by the caller. A measurement, not a gate — without its variable it does
/// nothing and passes, so a green result from it says nothing about speed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The performance fixture times every main map request as the rows' owner and
/// as a member reading through one club grant, at a hundred thousand entrances. Two questions it
/// leaves open: how the same requests behave at the size an installation is actually expected to
/// reach — a few thousand caves or entrances — and how a member fares whose club holds a grant per
/// cave rather than one, which is how a club that shares caves one by one ends up.
/// </para>
/// <para>
/// <b>How to run it.</b> Set <c>SILEXGIS_MEASURE_GRANT_SCALE</c> to <c>5000</c> (2,500 caves of two
/// entrances each) or <c>100000</c> (2,000 caves of fifty, the performance fixture's own seed), take
/// the gate lock so nothing else runs, and run this class alone with a detailed console logger. Each
/// request is warmed once and then timed five times; the median, the slowest and the machine's load
/// are written to the test output. The only assertion beyond "it answered" is a generous sanity
/// budget.
/// </para>
/// </remarks>
[Collection(SerialCollection.Name)]
public sealed class GrantScaleMeasurementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string OptInVariable = "SILEXGIS_MEASURE_GRANT_SCALE";

    /// <summary>Separate cave grants the many-grants club holds.</summary>
    private const int ManyGrants = 1000;

    private const int Rounds = 5;

    /// <summary>A request slower than this is broken, not slow.</summary>
    private const long SanityBudgetMs = 30_000;

    private const double SeedWest = 20;
    private const double SeedEast = 29;
    private const double SeedSouth = 43.6;
    private const double SeedNorth = 48;

    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;

    public GrantScaleMeasurementTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        factory = new SilexGisApiFactory(postgres.ConnectionString);
    }

    private static int? Requested =>
        int.TryParse(Environment.GetEnvironmentVariable(OptInVariable), out var scale) && scale is 5000 or 100000
            ? scale
            : null;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Opt_in_reports_only_member_reads_through_one_grant_and_through_a_thousand()
    {
        if (Requested is not { } scale)
        {
            output.WriteLine($"Not requested: set {OptInVariable}=5000 or 100000 to measure.");
            return;
        }

        var (caves, entrancesPerCave) = scale == 5000 ? (2500, 2) : (2000, 50);
        var ownerEmail = $"scale-owner-{Guid.NewGuid():N}@t.local";
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        var seeding = Stopwatch.StartNew();
        await SeedAsync(ownerId, caves, entrancesPerCave);
        output.WriteLine(Invariant($"seeded {caves} caves of {entrancesPerCave} entrances in {seeding.Elapsed.TotalSeconds:0.0} s"));

        var oneGrantEmail = await MemberAsync(ownerId, grants: 1);
        var manyGrantsEmail = await MemberAsync(ownerId, grants: ManyGrants);

        using var owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);
        using var oneGrant = await AuthHelper.BearerClientAsync(factory, oneGrantEmail);
        using var manyGrants = await AuthHelper.BearerClientAsync(factory, manyGrantsEmail);

        var requests = new (string Name, string Url)[]
        {
            ("features list p1", "/api/v1/features?pageSize=50"),
            ("features list (country bbox)", "/api/v1/features?pageSize=50&bbox=19.9,43.5,29.1,48.1"),
            ("clusters z7 (country)", "/api/v1/map/cave-entrances?bbox=19.9,43.5,29.1,48.1&zoom=7"),
        };

        foreach (var (caller, http) in new[]
        {
            ("owner", owner), ("member, one club grant", oneGrant), ($"member, {ManyGrants} club grants", manyGrants),
        })
        {
            foreach (var (name, url) in requests)
            {
                (await http.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK, $"{caller}: {url}");
                var timings = new List<long>();
                for (var round = 0; round < Rounds; round++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var response = await http.GetAsync(url);
                    _ = await response.Content.ReadAsByteArrayAsync();
                    stopwatch.Stop();
                    response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{caller}: {url}");
                    timings.Add(stopwatch.ElapsedMilliseconds);
                }

                timings.Sort();
                output.WriteLine(Invariant(
                    $"{scale} entrances | {caller} | {name}: median {timings[Rounds / 2]} ms, slowest {timings[^1]} ms ({LoadReading()})"));
                timings[^1].ShouldBeLessThan(SanityBudgetMs, $"{caller}: {name}");
            }
        }
    }

    /// <summary>
    /// A viewer on the roster of a club granted read of this many seeded caves' subtrees, each its
    /// own grant — so every one of them is a separate row the visibility walk has to consider.
    /// </summary>
    private async Task<string> MemberAsync(Guid ownerId, int grants)
    {
        var email = $"scale-member-{grants}-{Guid.NewGuid():N}@t.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var granted = await db.Features.AsNoTracking()
            .Where(f => f.Kind == FeatureKind.Cave && f.OwnerUserId == ownerId)
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .Take(grants)
            .ToListAsync();
        granted.Count.ShouldBe(grants);

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var group = new CavingGroup { Name = $"Scale club {stamp}", Slug = $"scale-club-{stamp}" };
        var caverId = await db.Cavers.Where(c => c.UserId == userId).Select(c => c.Id).SingleAsync();
        db.CavingGroups.Add(group);
        db.CavingGroupMemberships.Add(new CavingGroupMembership { CaverId = caverId, CavingGroupId = group.Id });
        foreach (var cave in granted)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.CavingGroup,
                SubjectId = group.Id,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.Subtree,
                ScopeFeatureId = cave,
            });
        }

        await db.SaveChangesAsync();
        return email;
    }

    /// <summary>
    /// The performance fixture's own synthetic shape: authenticated caves, each with private
    /// entrances scattered over the country, written in one statement with their hierarchy edges
    /// and closure rows.
    /// </summary>
    private async Task SeedAsync(Guid ownerId, int caves, int entrancesPerCave)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        await db.Database.ExecuteSqlAsync($"""
            WITH cave_rows AS MATERIALIZED (
                SELECT gen_random_uuid() AS cave_id, g FROM generate_series(1, {caves}) g
            ),
            entrance_rows AS MATERIALIZED (
                SELECT gen_random_uuid() AS entrance_id, c.cave_id, e,
                    ST_SetSRID(ST_MakePoint(
                        {SeedWest} + random() * {SeedEast - SeedWest},
                        {SeedSouth} + random() * {SeedNorth - SeedSouth}), 4326) AS geom
                FROM cave_rows c, generate_series(1, {entrancesPerCave}) e
            ),
            cave_features AS (
                INSERT INTO features (
                    id, kind, category, name, geom, location_protected, is_protected_effective,
                    ancestor_ids, owner_user_id, visibility, created_at, updated_at)
                SELECT c.cave_id, {(short)FeatureKind.Cave}, {(short)FeatureCategory.Underground},
                    'Scale Cave ' || c.g, main.geom, false, false, ARRAY[c.cave_id], {ownerId},
                    {(short)Visibility.Authenticated}, now(), now()
                FROM cave_rows c JOIN entrance_rows main ON main.cave_id = c.cave_id AND main.e = 1
                RETURNING id
            ),
            cave_subtypes AS (
                INSERT INTO caves (id, kind, cave_type_id, exploration_status, is_show_cave, entrance_count)
                SELECT c.cave_id, {(short)FeatureKind.Cave}, (SELECT id FROM cave_types WHERE code = 'cave'),
                    {(short)ExplorationStatus.Unknown}, false, {entrancesPerCave}
                FROM cave_rows c
                RETURNING id
            ),
            entrance_features AS (
                INSERT INTO features (
                    id, kind, category, geom, location_protected, is_protected_effective,
                    ancestor_ids, owner_user_id, visibility, created_at, updated_at)
                SELECT r.entrance_id, {(short)FeatureKind.CaveEntrance}, {(short)FeatureCategory.Surface},
                    r.geom, false, false, ARRAY[r.entrance_id, r.cave_id], {ownerId},
                    {(short)Visibility.Private}, now(), now()
                FROM entrance_rows r
                RETURNING id
            ),
            entrance_subtypes AS (
                INSERT INTO cave_entrances (id, kind, cave_feature_id, entrance_type_id, is_main, position_quality)
                SELECT r.entrance_id, {(short)FeatureKind.CaveEntrance}, r.cave_id,
                    (SELECT id FROM entrance_types WHERE code = 'natural'), r.e = 1, {(short)PositionQuality.Gps}
                FROM entrance_rows r
                RETURNING id
            ),
            primary_edges AS (
                INSERT INTO feature_hierarchy_edges (parent_id, child_id, is_primary, created_at, updated_at)
                SELECT r.cave_id, r.entrance_id, true, now(), now() FROM entrance_rows r
                RETURNING id
            ),
            closure AS (
                INSERT INTO feature_ancestors (feature_id, ancestor_id)
                SELECT c.cave_id, c.cave_id FROM cave_rows c
                UNION ALL SELECT r.entrance_id, r.entrance_id FROM entrance_rows r
                UNION ALL SELECT r.entrance_id, r.cave_id FROM entrance_rows r
                RETURNING feature_id
            )
            SELECT count(*) FROM closure
            """);
        await db.Database.ExecuteSqlRawAsync("ANALYZE features; ANALYZE feature_ancestors; ANALYZE access_entries;");
    }

    private static string LoadReading()
    {
        try
        {
            var first = File.ReadAllText("/proc/loadavg").Split(' ')[0];
            var load = double.Parse(first, CultureInfo.InvariantCulture) / Environment.ProcessorCount;
            return Invariant($"load {load:0.00} per core across {Environment.ProcessorCount}");
        }
        catch (Exception reading) when (reading is IOException or UnauthorizedAccessException or FormatException)
        {
            return "load unknown";
        }
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
