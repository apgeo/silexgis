// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The invented network the graph view is explored against: that it is gated, that it is laid
/// down once however often it is asked for, that it has the volume and the busy nodes it exists
/// to provide, and that it really does show two accounts different things.
/// </summary>
/// <remarks>
/// The visibility check is a pair on purpose. The viewer's list being shorter proves nothing on
/// its own — a viewer that could read nothing would pass — so the same read as the administrator
/// is asserted to contain the restricted rows, and the viewer's to contain the public ones.
/// It runs on its own database and application, because it mints accounts and a few thousand rows.
/// </remarks>
public sealed class GraphDemoSeedTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "graph-admin@dev.local";
    private const string AdminPassword = "dev-graph-admin-pass-1";
    private const string TestPassword = "dev-graph-test-pass-2";

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_graph_demo_{Guid.NewGuid():N}";
    private string connectionString = null!;
    private SilexGisApiFactory factory = null!;

    public GraphDemoSeedTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Admin:Email"] = AdminEmail,
                ["Admin:Password"] = AdminPassword,
                [GraphDemoSeeder.AllowKey] = "true",
                ["TestLogins:Enabled"] = "true",
                ["TestLogins:Password"] = TestPassword,
                ["Auth:RateLimitPerMinute"] = "200",
            });

        // Boots the application: migrations, the bootstrap administrator and the test accounts.
        using var client = factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Seeding_twice_writes_the_network_once_and_with_its_volume_and_busy_nodes()
    {
        GraphDemoSeedResult first;
        using (var scope = factory.Services.CreateScope())
        {
            first = await GraphDemoSeeder.SeedAsync(scope.ServiceProvider);
        }

        first.Outcome.ShouldBe(GraphDemoSeedOutcome.Seeded);
        first.Counts["caves"].ShouldBe(GraphDemoSeeder.CaveCount);
        first.Counts["entrances"].ShouldBeGreaterThan(GraphDemoSeeder.CaveCount);
        first.Counts["trips"].ShouldBe(GraphDemoSeeder.TripCount);
        first.Counts["busyCaverTrips"].ShouldBe(GraphDemoSeeder.BusyCaverTrips);
        first.Counts["resourceLinks"].ShouldBe(GraphDemoSeeder.ResLinkCount);
        first.Counts["documents"].ShouldBe(GraphDemoSeeder.DocumentCount);
        first.Counts["cavingGroups"].ShouldBe(3);
        first.Counts["protectedCaves"].ShouldBe(10);
        first.Counts["directedResourceLinks"].ShouldBeGreaterThan(20);
        first.Notes.ShouldBeEmpty("the test accounts exist, so nothing is left unsaid");

        // Volume targets of the rest, within a fifth of the figure the dataset is meant to have.
        WithinAFifth(first.Counts["genericFeatures"], 60, "generic features");
        WithinAFifth(first.Counts["featureLinks"], 120, "feature links");
        WithinAFifth(first.Counts["cavers"], 40, "cavers");
        WithinAFifth(first.Counts["tags"], 30, "tags");
        WithinAFifth(first.Counts["centerlines"], 20, "centerlines");
        WithinAFifth(first.Counts["cabinets"], 10, "cabinets");
        first.Counts["expeditions"].ShouldBe(GraphDemoSeeder.ExpeditionCount);
        first.Counts["relationTypesUsed"].ShouldBeGreaterThanOrEqualTo(27);

        Dictionary<string, long> afterFirst;
        await using (var counting = CreateContext())
        {
            afterFirst = await RowCountsAsync(counting);

            // One tag on a hundred items, and the busy cave in well over a hundred links.
            var busyTag = await counting.Tags.Where(t => t.Name == GraphDemoSeeder.TagName(1)).Select(t => t.Id).SingleAsync();
            (await counting.Taggings.CountAsync(t => t.TagId == busyTag)).ShouldBe(GraphDemoSeeder.BusyTagItems);

            var busyCave = await counting.Caves.Where(c => c.IdentificationCode == GraphDemoSeeder.CaveCode(1)).Select(c => c.Id).SingleAsync();
            var inResourceLinks = await counting.ResLinkMembers.CountAsync(m => m.FeatureId == busyCave);
            var inFeatureLinks = await counting.FeatureLinks.CountAsync(l => l.FromId == busyCave || l.ToId == busyCave);
            (inResourceLinks + inFeatureLinks).ShouldBeGreaterThanOrEqualTo(150);
        }

        using (var scope = factory.Services.CreateScope())
        {
            (await GraphDemoSeeder.SeedAsync(scope.ServiceProvider)).Outcome.ShouldBe(GraphDemoSeedOutcome.Seeded);
        }

        await using var db = CreateContext();
        (await RowCountsAsync(db)).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task The_viewer_and_the_administrator_see_different_networks()
    {
        using (var scope = factory.Services.CreateScope())
        {
            (await GraphDemoSeeder.SeedAsync(scope.ServiceProvider)).Outcome.ShouldBe(GraphDemoSeedOutcome.Seeded);
        }

        // The viewer is in Beta and not in Alfa: construct that state explicitly.
        await using (var db = CreateContext())
        {
            var viewerGroups = await (
                from m in db.CavingGroupMemberships
                join caver in db.Cavers on m.CaverId equals caver.Id
                join user in db.Users on caver.UserId equals user.Id
                where user.Email == GraphDemoSeeder.ViewerEmail
                join g in db.CavingGroups on m.CavingGroupId equals g.Id
                select g.Slug).ToListAsync();
            viewerGroups.ShouldBe([GraphDemoSeeder.BetaSlug]);
        }

        using var viewer = await AuthHelper.BearerClientAsync(factory, GraphDemoSeeder.ViewerEmail, TestPassword);
        using var admin = await AuthHelper.BearerClientAsync(factory, AdminEmail, AdminPassword);

        var adminNames = await CaveNamesAsync(admin);
        var viewerNames = await CaveNamesAsync(viewer);

        await using var check = CreateContext();
        var alfaOnly = await check.Features
            .Where(f => f.Kind == FeatureKind.Cave && f.Visibility == Visibility.CavingGroup
                && check.CavingGroups.Any(g => g.Id == f.CavingGroupId && g.Slug == GraphDemoSeeder.AlfaSlug))
            .Select(f => f.Name!).ToListAsync();
        alfaOnly.Count.ShouldBeGreaterThan(10);
        var beta = await check.Features
            .Where(f => f.Kind == FeatureKind.Cave && f.Visibility == Visibility.CavingGroup
                && check.CavingGroups.Any(g => g.Id == f.CavingGroupId && g.Slug == GraphDemoSeeder.BetaSlug))
            .Select(f => f.Name!).ToListAsync();
        beta.ShouldNotBeEmpty();

        // The pair: the administrator reads the restricted rows, the viewer does not, and the
        // viewer still reads a club row it is entitled to and the public ones.
        alfaOnly.ShouldBeSubsetOf(adminNames);
        viewerNames.ShouldNotContain(alfaOnly[0]);
        viewerNames.Intersect(alfaOnly).ShouldBeEmpty();
        beta.ShouldBeSubsetOf(viewerNames);
        viewerNames.ShouldContain(GraphDemoSeeder.CaveName(1));

        // Resource-link members: on the busiest cave the administrator resolves every member and
        // the viewer fewer, the difference being members the viewer may not read. The viewer still
        // resolves some, so an empty answer cannot pass for "restricted".
        var busyCave = await check.Caves.Where(c => c.IdentificationCode == GraphDemoSeeder.CaveCode(1)).Select(c => c.Id).SingleAsync();
        var adminResolved = await ResolvedMembersAsync(admin, busyCave);
        var viewerResolved = await ResolvedMembersAsync(viewer, busyCave);
        adminResolved.Unresolved.ShouldBe(0);
        viewerResolved.Resolved.ShouldBeGreaterThan(0);
        viewerResolved.Unresolved.ShouldBeGreaterThan(0, "members the viewer may not read are withheld");
        viewerResolved.Resolved.ShouldBeLessThan(adminResolved.Resolved);

        // Exact position: a protected cave's entrances are exact for the administrator and
        // approximate for the viewer, who is in no club that cave is bound to.
        var protectedCave = await check.Features
            .Where(f => f.Kind == FeatureKind.Cave && f.LocationProtected && f.Name!.StartsWith("Peștera Graf "))
            .OrderBy(f => f.Name).Select(f => f.Id).FirstAsync();
        var adminEntrances = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{protectedCave}/entrances");
        var viewerEntrances = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{protectedCave}/entrances");
        adminEntrances.GetArrayLength().ShouldBeGreaterThan(0);
        adminEntrances[0].GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        viewerEntrances.GetArrayLength().ShouldBeGreaterThan(0);
        viewerEntrances[0].GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_host_that_is_not_in_development_refuses_without_the_option()
    {
        using var refusing = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(GraphDemoSeeder.AllowKey, "false");
        });

        using (var client = refusing.CreateClient())
        {
            using var scope = refusing.Services.CreateScope();
            var result = await GraphDemoSeeder.SeedAsync(scope.ServiceProvider);
            result.Outcome.ShouldBe(GraphDemoSeedOutcome.NotPermitted);
            result.Counts.ShouldBeEmpty();
        }

        await using var db = CreateContext();
        (await db.Caves.AnyAsync(c => c.IdentificationCode!.StartsWith(GraphDemoSeeder.CaveCodePrefix))).ShouldBeFalse();
    }

    private static void WithinAFifth(int actual, int target, string what) =>
        actual.ShouldBeInRange((int)(target * 0.8), (int)Math.Ceiling(target * 1.2), what);

    private static async Task<(int Resolved, int Unresolved)> ResolvedMembersAsync(HttpClient client, Guid feature)
    {
        int resolved = 0, unresolved = 0;
        for (var page = 1; ; page++)
        {
            var response = await client.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={feature}&page={page}&pageSize=100");
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
            var body = JsonDocument.Parse(payload).RootElement;
            var items = body.GetProperty("items");
            foreach (var link in items.EnumerateArray())
            {
                foreach (var member in link.GetProperty("members").EnumerateArray())
                {
                    if (member.GetProperty("display").ValueKind == JsonValueKind.Null) unresolved++;
                    else resolved++;
                }
            }

            if (items.GetArrayLength() == 0 || page * 100 >= body.GetProperty("totalItems").GetInt32()) break;
        }

        return (resolved, unresolved);
    }

    private static async Task<HashSet<string>> CaveNamesAsync(HttpClient client)
    {
        var names = new HashSet<string>();
        for (var page = 1; ; page++)
        {
            var response = await client.GetAsync($"/api/v1/caves?page={page}&pageSize=100");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            var items = body["items"]!.AsArray();
            foreach (var item in items) names.Add(item!["name"]!.GetValue<string>());
            if (names.Count >= body["totalItems"]!.GetValue<int>() || items.Count == 0) break;
        }

        return names;
    }

    private SilexGisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    private static async Task<Dictionary<string, long>> RowCountsAsync(SilexGisDbContext db)
    {
        var names = new List<string>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using (var list = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name",
                connection))
            await using (var reader = await list.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            }

            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{name}\"", connection);
                counts[name] = (long)(await count.ExecuteScalarAsync())!;
            }

            return counts;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
