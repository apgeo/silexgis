// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The karstification index over a map window: which areas it answers for, and that what it says
/// about each is what the area's own statistics say.
///
/// <para>
/// An area is answered only for a caller who may place its outline exactly, and is otherwise
/// absent with nothing to say so. Each refusal case is built explicitly — a protected outline, an
/// object-scoped deny — and its positive half, a caller who is answered, sits in the same test.
/// </para>
/// <para>
/// The fixtures sit in the South Atlantic, away from every other class, and the cap is lowered to
/// three so that a window holding more than one answer carries can be built from a handful of rows.
/// </para>
/// </summary>
public sealed class MapKarstIndexTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int MaxAreas = 3;

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private long massifTypeId;
    private long sinkholeTypeId;

    public MapKarstIndexTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:KarstIndexMaxAreas"] = MaxAreas.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"kix-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"kix-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
            massifTypeId = await db.FeatureTypes.Where(t => t.Code == "massif").Select(t => t.Id).SingleAsync();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"kix-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"kix-view-{suffix}@t.local");
    }

    [Fact]
    public async Task Each_area_answers_with_what_its_own_statistics_say_counting_by_declaration()
    {
        var areaId = await CreateAreaAsync("Kix Agree", -30.0, -40.0, -29.8, -39.8);

        // Declared in the area: one open cave, one protected cave the viewer may not place, and one
        // whose point lies outside the outline altogether. A fourth lies inside the outline but is
        // filed nowhere. Counted by declaration that is three; counted spatially it would be three
        // as well but a different three, and the protected cave's true position would decide it.
        await CreateCaveAsync(owner, "Kix Open", -29.95, -39.95, parentId: areaId);
        await CreateCaveAsync(owner, "Kix Guarded", -29.90, -39.90, parentId: areaId, locationProtected: true);
        await CreateCaveAsync(owner, "Kix Outside", -29.50, -39.50, parentId: areaId);
        await CreateCaveAsync(owner, "Kix Unfiled", -29.85, -39.85);
        await CreateDepressionAsync("Kix Doline", areaId, -29.99, -39.99, -29.97, -39.97);

        foreach (var client in new[] { owner, viewer })
        {
            var area = Area(await WindowAsync(client, "-30.1,-40.1,-29.7,-39.7"), areaId);
            area.ShouldNotBeNull();
            area["caveCount"]!.GetValue<int>().ShouldBe(3);
            area["depressionCount"]!.GetValue<int>().ShouldBe(1);
            area["outline"]!["type"]!.GetValue<string>().ShouldBe("Polygon");

            // Agreement with the per-area route, for the same caller and the same area.
            var own = await StatisticsAsync(client, areaId);
            area["caveCount"]!.GetValue<int>().ShouldBe(own["caveCount"]!.GetValue<int>());
            area["areaKm2"]!.GetValue<double>().ShouldBe(own["areaKm2"]!.GetValue<double>(), 1e-9);
            area["cavesPerKm2"]!.GetValue<double>().ShouldBe(own["cavesPerKm2"]!.GetValue<double>(), 1e-12);
            area["depressionAreaRatio"]!.GetValue<double>()
                .ShouldBe(own["depressionAreaRatio"]!.GetValue<double>(), 1e-12);
            area["score"]!.GetValue<double>().ShouldBe(own["karstification"]!["score"]!.GetValue<double>(), 1e-12);
            area["class"]!.GetValue<string>().ShouldBe(own["karstification"]!["class"]!.GetValue<string>());
            area["components"]!.ToJsonString().ShouldBe(own["karstification"]!["components"]!.ToJsonString());
        }
    }

    [Fact]
    public async Task An_area_the_caller_may_read_but_not_place_is_absent_and_nothing_says_so()
    {
        const string bbox = "-28.1,-40.1,-27.3,-39.7";
        var guardedId = await CreateAreaAsync("Kix Guarded Area", -28.0, -40.0, -27.8, -39.8, locationProtected: true);
        var openId = await CreateAreaAsync("Kix Open Area", -27.6, -40.0, -27.4, -39.8);

        // Readable: the feature answers for the viewer, so what follows is placement, not visibility.
        (await viewer.GetAsync($"/api/v1/features/{guardedId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Positive half: the owner, who may place it, is answered for both.
        var owned = await WindowAsync(owner, bbox);
        Area(owned, guardedId).ShouldNotBeNull();
        Area(owned, openId).ShouldNotBeNull();

        var seen = await WindowAsync(viewer, bbox);
        Area(seen, openId).ShouldNotBeNull();
        Area(seen, guardedId).ShouldBeNull();
        seen["truncated"]!.GetValue<bool>().ShouldBeFalse();
        seen.ToJsonString().ShouldNotContain(guardedId.ToString());
    }

    [Fact]
    public async Task An_area_the_caller_cannot_read_is_absent()
    {
        const string bbox = "-26.1,-38.1,-25.3,-37.7";
        var deniedId = await CreateAreaAsync("Kix Denied", -26.0, -38.0, -25.8, -37.8);
        var openId = await CreateAreaAsync("Kix Readable", -25.6, -38.0, -25.4, -37.8);

        // Positive half before the deny: the viewer is answered for the area.
        Area(await WindowAsync(viewer, bbox), deniedId).ShouldNotBeNull();
        await DenyReadAsync(owner, deniedId, viewerId);

        var seen = await WindowAsync(viewer, bbox);
        Area(seen, deniedId).ShouldBeNull();
        Area(seen, openId).ShouldNotBeNull();
        Area(await WindowAsync(owner, bbox), deniedId).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_cap_is_applied_after_membership_so_a_hidden_area_takes_no_slot()
    {
        const string bbox = "-24.1,-40.1,-22.9,-39.7";
        _ = await CreateAreaAsync("Kix Cap One", -24.0, -40.0, -23.8, -39.8);
        _ = await CreateAreaAsync("Kix Cap Two", -23.7, -40.0, -23.5, -39.8);
        _ = await CreateAreaAsync("Kix Cap Three", -23.4, -40.0, -23.2, -39.8);
        _ = await CreateAreaAsync("Kix Cap Guarded", -23.1, -40.0, -23.0, -39.8, locationProtected: true);

        // The owner may place all four: one more than the cap, so the answer says it was cut.
        var owned = await WindowAsync(owner, bbox);
        owned["areas"]!.AsArray().Count.ShouldBe(MaxAreas);
        owned["truncated"]!.GetValue<bool>().ShouldBeTrue();
        owned["maxAreas"]!.GetValue<int>().ShouldBe(MaxAreas);

        // The viewer may place three. The fourth is not in their window at all, so it neither
        // takes a slot nor makes the answer read as cut — which would say an area is hidden there.
        var seen = await WindowAsync(viewer, bbox);
        seen["areas"]!.AsArray().Count.ShouldBe(MaxAreas);
        seen["truncated"]!.GetValue<bool>().ShouldBeFalse();

        _ = await CreateAreaAsync("Kix Cap Four", -24.05, -39.75, -24.0, -39.72);
        (await WindowAsync(viewer, bbox))["truncated"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Only_karst_areas_and_massifs_inside_the_window_are_answered()
    {
        const string bbox = "-22.1,-36.1,-21.3,-35.7";
        var karstId = await CreateAreaAsync("Kix Kind Karst", -22.0, -36.0, -21.8, -35.8);
        var massifId = await CreateAreaAsync("Kix Kind Massif", -21.7, -36.0, -21.5, -35.8, typeId: massifTypeId);
        var depressionId = await CreateDepressionAsync("Kix Kind Doline", null, -21.45, -35.95, -21.40, -35.90);
        var farId = await CreateAreaAsync("Kix Kind Far", -20.0, -36.0, -19.8, -35.8);

        var seen = await WindowAsync(viewer, bbox);
        Area(seen, karstId).ShouldNotBeNull();
        Area(seen, massifId).ShouldNotBeNull();
        Area(seen, depressionId).ShouldBeNull();
        Area(seen, farId).ShouldBeNull();
        seen["basis"]!.GetValue<string>().ShouldBe("declared");

        // An area with nothing declared in it reads as "nothing measured on the depressions" and a
        // zero cave density, the same as its own statistics.
        var karst = Area(seen, karstId)!;
        karst["caveCount"]!.GetValue<int>().ShouldBe(0);
        karst["depressionAreaRatio"].ShouldBeNull();
        karst["class"]!.GetValue<string>().ShouldBe("veryLow");
    }

    [Fact]
    public async Task The_route_refuses_what_it_cannot_answer_and_anonymous_callers()
    {
        await ExpectAsync(owner, string.Empty, HttpStatusCode.BadRequest, "validation.failed");
        await ExpectAsync(owner, "?bbox=not-a-box", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=10,10,5,5", HttpStatusCode.BadRequest, "map.invalid_bbox");
        await ExpectAsync(owner, "?bbox=-22,-36,-21,-35&zoom=99", HttpStatusCode.BadRequest, "validation.failed");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/map/karst-index?bbox=-22,-36,-21,-35"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_per_area_containment_walk_can_be_answered_from_the_ancestry_index()
    {
        // The bulk statement joins each area to its contents rather than binding one area id, so the
        // indexable spelling has a column on the right of the containment instead of a parameter. It
        // is planned against the unindexable spelling with sequential scans closed off, for the
        // reason the per-area statement's own plan test gives: at fixture scale any small index wins,
        // and what is claimed is only that this form can reach the ancestry index and the other not.
        var areaId = await CreateAreaAsync("Kix Plan", -20.0, -34.0, -19.8, -33.8);
        await CreateCaveAsync(owner, "Kix Plan Cave", -19.9, -33.9, parentId: areaId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = new AccessContext(Guid.NewGuid(), isFullAdmin: false, [], []);
        var (sql, _) = KarstIndexWindowSql.BuildTotals(ctx, [areaId], 0L, 0.0001d);
        sql.ShouldContain("f.ancestor_ids @> ARRAY[area.id]::uuid[]");
        sql.ShouldNotContain("area.id = ANY(f.ancestor_ids)");

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SET LOCAL enable_seqscan = off", transaction: transaction);

        async Task<string> PlanOfAsync(string predicate) => string.Join(
            '\n',
            await connection.QueryAsync<string>(
                "EXPLAIN SELECT count(*) FROM unnest(@ids::uuid[]) AS area(id) "
                + $"JOIN features f ON {predicate}",
                new { ids = new[] { areaId } },
                transaction: transaction));

        var indexable = await PlanOfAsync("f.ancestor_ids @> ARRAY[area.id]::uuid[]");
        var scalar = await PlanOfAsync("area.id = ANY(f.ancestor_ids)");

        indexable.ShouldContain("ix_features_ancestor_ids", Case.Insensitive, indexable);
        scalar.ShouldNotContain("ix_features_ancestor_ids", Case.Insensitive, scalar);
    }

    private static JsonObject? Area(JsonObject window, Guid id) =>
        window["areas"]!.AsArray()
            .Select(a => a!.AsObject())
            .SingleOrDefault(a => a["id"]!.GetValue<Guid>() == id);

    private static async Task<JsonObject> WindowAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/map/karst-index?bbox={bbox}&zoom=12");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task<JsonObject> StatisticsAsync(HttpClient client, Guid areaId)
    {
        var response = await client.GetAsync($"/api/v1/features/{areaId}/karst-statistics");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task ExpectAsync(HttpClient client, string query, HttpStatusCode status, string code)
    {
        var response = await client.GetAsync($"/api/v1/map/karst-index{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code, payload);
    }

    /// <summary>An object-scoped deny of Read for one user, which overrides every cascade above it.</summary>
    private static async Task DenyReadAsync(HttpClient client, Guid featureId, Guid subjectId)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId, effect = "deny", actions = "Read", scopeKind = "object" },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    private static object Ring(double west, double south, double east, double north) => new[]
    {
        new[]
        {
            new[] { west, south }, new[] { east, south }, new[] { east, north },
            new[] { west, north }, new[] { west, south },
        },
    };

    private async Task<Guid> CreateCaveAsync(
        HttpClient client, string name, double lon, double lat, Guid? parentId = null, bool locationProtected = false)
    {
        var cave = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = Unique(name),
            caveTypeId,
            visibility = "authenticated",
            parentId,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }

    private async Task<Guid> CreateAreaAsync(
        string name, double west, double south, double east, double north,
        bool locationProtected = false, long? typeId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = typeId ?? karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = Ring(west, south, east, north) },
            visibility = "authenticated",
            locationProtected,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateDepressionAsync(
        string name, Guid? parentId, double west, double south, double east, double north)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = sinkholeTypeId,
            parents = parentId is { } p ? new[] { new { parentId = p, isPrimary = true } } : null,
            geometry = new { type = "Polygon", coordinates = Ring(west, south, east, north) },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
