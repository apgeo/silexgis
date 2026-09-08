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
/// What a karst area adds up to, and — the reason this file exists — what it adds up to it over.
///
/// <para>
/// Membership is the declared one: the containment chain somebody entered, never a spatial test
/// against the outline. Both halves are asserted here, because the two answers differ and the whole
/// value of naming the basis on the wire is that a reader can tell which they got. The cave that is
/// inside the polygon but parented elsewhere is in no total and appears only as the drift hint.
/// </para>
/// <para>
/// The fixtures sit off the Portuguese coast, away from every other class in this suite, because
/// the drift hint is a spatial search and a neighbour's fixture inside the same outline would be
/// found by it.
/// </para>
/// </summary>
public sealed class AreaKarstStatisticsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private long sinkholeTypeId;
    private long limestoneRockTypeId;

    public AreaKarstStatisticsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"kst-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"kst-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
            sinkholeTypeId = await db.FeatureTypes
                .Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            limestoneRockTypeId = await db.RockTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"kst-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"kst-view-{suffix}@t.local");
    }

    [Fact]
    public async Task Membership_is_the_declared_one_and_a_cave_only_inside_the_outline_is_a_hint_not_a_count()
    {
        var areaId = await CreateKarstAreaAsync("Kst Declared", -9.60, 38.60, -9.40, 38.80);
        var elsewhereId = await CreateKarstAreaAsync("Kst Elsewhere", -9.90, 38.60, -9.80, 38.70);

        // Two caves put in the area by hand, and one whose coordinate falls inside the same outline
        // but which somebody parented to a different area. The spatial answer here is three; the
        // declared answer is two, and the endpoint gives the declared one.
        await CreateCaveWithEntranceAsync(owner, "Kst In One", -9.55, 38.65, parentId: areaId);
        await CreateCaveWithEntranceAsync(owner, "Kst In Two", -9.50, 38.70, parentId: areaId);
        await CreateCaveWithEntranceAsync(owner, "Kst Adrift", -9.45, 38.75, parentId: elsewhereId);

        var body = await StatisticsAsync(owner, areaId);

        body["basis"]!.GetValue<string>().ShouldBe("declared");
        body["caveCount"]!.GetValue<int>().ShouldBe(2);
        body["entranceCount"]!.GetValue<int>().ShouldBe(2);

        // The drift is reported beside the total, never inside it.
        body["unparentedInsideCount"]!.GetValue<int>().ShouldBe(1);
        body["placeableCaveCount"]!.GetValue<int>().ShouldBe(2);

        // And the area itself is not one of the features in it: the containment closure holds a
        // row for the area, and a count that forgot to exclude it would read three.
        body["areaKm2"]!.GetValue<double>().ShouldBeGreaterThan(0d);
        body["cavesPerKm2"]!.GetValue<double>()
            .ShouldBe(2d / body["areaKm2"]!.GetValue<double>(), 1e-9);
    }

    [Fact]
    public async Task Totals_extremes_and_the_rock_breakdown_are_over_the_declared_set()
    {
        var areaId = await CreateKarstAreaAsync("Kst Totals", -8.60, 38.60, -8.40, 38.80);

        await CreateCaveWithEntranceAsync(
            owner, "Kst Long", -8.55, 38.65, parentId: areaId,
            surveyedLength: 4200d, depth: 90d, rockTypeId: limestoneRockTypeId);
        await CreateCaveWithEntranceAsync(
            owner, "Kst Deep", -8.50, 38.70, parentId: areaId,
            surveyedLength: 1100d, depth: 310d, rockTypeId: limestoneRockTypeId);
        // A third with nothing measured: it counts, it contributes no length, and its unrecorded
        // rock type is a row of its own rather than a cave quietly dropped from the breakdown.
        await CreateCaveWithEntranceAsync(owner, "Kst Plain", -8.45, 38.75, parentId: areaId);

        var body = await StatisticsAsync(owner, areaId);

        body["caveCount"]!.GetValue<int>().ShouldBe(3);
        body["surveyedLengthM"]!.GetValue<double>().ShouldBe(5300d, 1e-6);
        body["surveyedCaveCount"]!.GetValue<int>().ShouldBe(2);
        body["surveyedMetresPerKm2"]!.GetValue<double>()
            .ShouldBe(5300d / body["areaKm2"]!.GetValue<double>(), 1e-6);

        Names(body["deepestCaves"]!)[0].ShouldContain("Kst Deep");
        Names(body["longestCaves"]!)[0].ShouldContain("Kst Long");

        var rocks = body["rockTypes"]!.AsArray();
        rocks.Count.ShouldBe(2);
        rocks.Single(r => r!["rockTypeId"] is not null)!["caveCount"]!.GetValue<int>().ShouldBe(2);
        rocks.Single(r => r!["rockTypeId"] is null)!["caveCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task The_index_names_the_component_it_could_not_measure_rather_than_scoring_it_zero()
    {
        var areaId = await CreateKarstAreaAsync("Kst Index", -7.60, 38.60, -7.40, 38.80);
        await CreateCaveWithEntranceAsync(owner, "Kst Idx One", -7.55, 38.65, parentId: areaId);

        // No depression outline has been drawn here. That is not a measured ratio of nought —
        // nobody can tell an area without dolines from an area whose dolines nobody has mapped —
        // so the component is absent and the index is the cave-density reading alone.
        var withoutDepressions = await StatisticsAsync(owner, areaId);
        var absent = Component(withoutDepressions, "depression_area_ratio");
        absent["value"]!.ShouldBeNull();
        absent["normalised"]!.ShouldBeNull();
        withoutDepressions["depressionCount"]!.GetValue<int>().ShouldBe(0);
        withoutDepressions["depressionAreaRatio"]!.ShouldBeNull();

        // The reference is on the wire beside it, so a reader can reconstruct the normalisation
        // rather than take the score on trust.
        absent["reference"]!.GetValue<double>().ShouldBeGreaterThan(0d);

        // The measurable half was measured, so the index is a number rather than unknown.
        Component(withoutDepressions, "cave_density")["value"]!.GetValue<double>()
            .ShouldBeGreaterThan(0d);
        withoutDepressions["karstification"]!["score"]!.GetValue<double>().ShouldBeGreaterThan(0d);

        // An area with no outline can measure neither component, and reads as unknown rather than
        // as the least karstified area in the registry.
        var shapelessId = await CreateShapelessAreaAsync("Kst Shapeless");
        var shapeless = await StatisticsAsync(owner, shapelessId);
        shapeless["areaKm2"]!.ShouldBeNull();
        shapeless["karstification"]!["score"]!.ShouldBeNull();
        shapeless["karstification"]!["class"]!.GetValue<string>()
            .ToLowerInvariant().ShouldBe("unknown");
        Component(shapeless, "cave_density")["value"]!.ShouldBeNull();
    }

    [Fact]
    public async Task A_depression_outline_raises_the_index_and_is_reported_with_its_own_area()
    {
        var areaId = await CreateKarstAreaAsync("Kst Dep", -6.60, 38.60, -6.40, 38.80);
        await CreateDepressionAsync("Kst Doline", areaId, -6.55, 38.65, -6.50, 38.70);

        var body = await StatisticsAsync(owner, areaId);

        body["depressionCount"]!.GetValue<int>().ShouldBe(1);
        body["depressionAreaKm2"]!.GetValue<double>().ShouldBeGreaterThan(0d);
        body["depressionAreaRatio"]!.GetValue<double>().ShouldBeGreaterThan(0d);
        Component(body, "depression_area_ratio")["value"]!.GetValue<double>()
            .ShouldBe(body["depressionAreaRatio"]!.GetValue<double>(), 1e-9);
        body["karstification"]!["score"]!.GetValue<double>().ShouldBeGreaterThan(0d);
    }

    [Fact]
    public async Task A_cave_the_caller_cannot_read_is_in_no_total_and_in_no_hint()
    {
        var areaId = await CreateKarstAreaAsync("Kst Visible", -5.60, 38.60, -5.40, 38.80);

        // The unreadable state, built explicitly, and it has to be a deny entry rather than a
        // private visibility: readability cascades down the containment chain, so a private cave
        // under a readable area is still readable, and marking it private would have proved
        // nothing. An object-scoped deny on the cave is the one thing that overrides the cascade.
        var hiddenId = await CreateCaveWithEntranceAsync(
            owner, "Kst Private", -5.55, 38.65, parentId: areaId);
        await CreateCaveWithEntranceAsync(owner, "Kst Open", -5.50, 38.70, parentId: areaId);
        await DenyReadAsync(owner, hiddenId, viewerId);

        // The positive half in the same test: the owner sees both, so what follows is the
        // visibility filter and not a query that finds nothing here.
        (await StatisticsAsync(owner, areaId))["caveCount"]!.GetValue<int>().ShouldBe(2);

        var seen = await StatisticsAsync(viewer, areaId);
        seen["caveCount"]!.GetValue<int>().ShouldBe(1);
        Names(seen["deepestCaves"]!).ShouldNotContain(n => n.Contains("Kst Private"));
    }

    [Fact]
    public async Task An_area_the_caller_cannot_read_answers_as_one_that_is_not_there()
    {
        var areaId = await CreateKarstAreaAsync("Kst Hidden", -4.60, 38.60, -4.40, 38.80, "private");

        // Positive half: the owner gets an answer for the very same area.
        (await StatisticsAsync(owner, areaId))["areaId"]!.GetValue<Guid>().ShouldBe(areaId);

        var response = await viewer.GetAsync($"/api/v1/features/{areaId}/karst-statistics");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("feature.not_found");
    }

    [Fact]
    public async Task An_area_the_caller_can_read_but_may_not_place_answers_as_one_that_is_not_there()
    {
        // Every figure here is measured against the outline: its ground area is the denominator of
        // each per-square-kilometre number, the depression ratio is areas inside it, and the drift
        // hint tests caves for being inside it. An outline places itself, so serving any of that to
        // a caller who may not be shown where it is publishes the boundary — and the drift hint
        // publishes it *interactively*, since a caller who can put a cave of their own at a
        // coordinate of their choosing learns from the hint whether that coordinate is inside, and
        // can bisect the edge to any precision they like.
        var areaId = await CreateKarstAreaAsync(
            "Kst Guarded", -3.60, 37.60, -3.40, 37.80, locationProtected: true);

        // Readable: the feature itself answers for the Viewer, so what follows is the placement
        // rule and not a visibility one.
        (await viewer.GetAsync($"/api/v1/features/{areaId}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Positive half: the owner, who may place it exactly, gets the whole answer.
        (await StatisticsAsync(owner, areaId))["areaId"]!.GetValue<Guid>().ShouldBe(areaId);

        // And the Viewer is told it is not there, in the words an area that never existed gets,
        // so the refusal itself says nothing about whether one is hidden.
        foreach (var route in new[] { "karst-statistics", "karst-statistics.csv" })
        {
            var response = await viewer.GetAsync($"/api/v1/features/{areaId}/{route}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, route);
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
                .GetProperty("code").GetString().ShouldBe("feature.not_found");
        }
    }

    [Fact]
    public async Task The_same_figures_come_back_as_a_spreadsheet()
    {
        var areaId = await CreateKarstAreaAsync("Kst Csv", -2.60, 36.60, -2.40, 36.80);
        await CreateCaveWithEntranceAsync(
            owner, "Kst Csv Cave", -2.50, 36.70, parentId: areaId, surveyedLength: 400d, depth: 30d);

        var json = await StatisticsAsync(owner, areaId);

        var response = await owner.GetAsync($"/api/v1/features/{areaId}/karst-statistics.csv");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        rows[0].ShouldBe("measure,value");

        var values = rows.Skip(1)
            .Select(r => r.Split(',', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);

        // The basis travels with the figures. A count copied into a spreadsheet without it is a
        // different claim — these are the caves declared to be in the area, not the ones that
        // happen to fall inside its outline.
        values["membership_basis"].ShouldBe(json["basis"]!.GetValue<string>());
        values["cave_count"].ShouldBe(json["caveCount"]!.GetValue<int>().ToString());
        values["surveyed_length_m"].ShouldNotBeEmpty();
        values.ShouldContainKey("karstification_class");
        values.ShouldContainKey("unparented_inside_count");

        // Numbers are written the way a spreadsheet in any locale reads them back: a comma decimal
        // separator here would split the row and shift every field after it.
        values["area_km2"].ShouldNotContain(",");
    }

    [Fact]
    public async Task The_declared_containment_walk_is_answered_from_the_index_and_not_by_a_scan()
    {
        // The predicate that walks the hierarchy reads identically written two ways and plans
        // nothing like it: the GIN index on the ancestry array serves array containment, and a
        // scalar compared against ANY of that array is not rewritten into anything indexable. The
        // difference is invisible in the result and shows only in the plan, which is why it is
        // asserted here rather than assumed.
        var areaId = await CreateKarstAreaAsync("Kst Plan", -1.60, 35.60, -1.40, 35.80);
        await CreateCaveWithEntranceAsync(owner, "Kst Plan Cave", -1.50, 35.70, parentId: areaId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = new AccessContext(Guid.NewGuid(), isFullAdmin: false, [], []);
        var (sql, parameters) = KarstAreaStatisticsSql.BuildPlaceableCaveCount(ctx, areaId);

        // The form itself. Scoped to the containment predicate rather than to the whole statement,
        // because the visibility fragment legitimately compares a scalar against the same array for
        // a different purpose, and an assertion wide enough to catch that catches everything.
        sql.ShouldContain("ancestor_ids @> ARRAY[@ka_area_id]::uuid[]");
        sql.ShouldNotContain("@ka_area_id = ANY(f.ancestor_ids)");

        var connection = db.Database.GetDbConnection();

        // The index the form depends on is really there, under the name the plan will use.
        // Asserting only the SQL would pass just as well against a database that never had it.
        (await connection.QueryAsync<string>(
            "SELECT indexname FROM pg_indexes WHERE tablename = 'features'"))
            .ShouldContain("ix_features_ancestor_ids");

        // And the planner can actually reach it. Sequential scans are turned off for the one
        // statement, because a test database holds a handful of rows and the planner would scan
        // them whatever the predicate said — which is exactly how a predicate that *cannot* be
        // indexed passes a plan assertion at test scale. With the scan closed off, a form the GIN
        // index can serve produces an index scan and a form it cannot still produces a sequential
        // one, so the two are told apart.
        // In a transaction, because that is the only place SET LOCAL does anything: outside one
        // PostgreSQL answers "SET LOCAL can only be used in transaction blocks", leaves the setting
        // alone and carries on. This ran outside one until the classes stopped sharing a database,
        // and the assertion below passed on the strength of a features table other classes had
        // filled — the planner reached for the index because the table was big, not because the
        // predicate could use it. That is the exact failure the paragraph above says it is here to
        // prevent, so it is worth stating twice: without the transaction this test proves nothing.
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SET LOCAL enable_seqscan = off", transaction: transaction);
        var plan = string.Join(
            '\n',
            await connection.QueryAsync<string>($"EXPLAIN {sql}", parameters, transaction: transaction));

        plan.ShouldContain("ix_features_ancestor_ids", Case.Insensitive, plan);
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/features/{Guid.NewGuid()}/karst-statistics"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>An object-scoped deny of Read for one user, which overrides every cascade above it.</summary>
    private static async Task DenyReadAsync(HttpClient client, Guid featureId, Guid subjectId)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId,
                    effect = "deny",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static List<string> Names(JsonNode list) =>
        [.. list.AsArray().Select(c => c!["name"]?.GetValue<string>() ?? string.Empty)];

    private static JsonNode Component(JsonObject body, string name) =>
        body["karstification"]!["components"]!.AsArray()
            .Single(c => c!["name"]!.GetValue<string>() == name)!;

    private async Task<JsonObject> StatisticsAsync(HttpClient client, Guid areaId)
    {
        var response = await client.GetAsync($"/api/v1/features/{areaId}/karst-statistics");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    /// <summary>A name no other run of this class can collide with, inside the length the API takes.</summary>
    private static string Unique(string name)
    {
        var candidate = $"{name} {Guid.NewGuid():N}";
        return candidate.Length <= 40 ? candidate : candidate[..40];
    }

    private async Task<Guid> CreateCaveWithEntranceAsync(
        HttpClient client,
        string name,
        double lon,
        double lat,
        Guid? parentId = null,
        double? surveyedLength = null,
        double? depth = null,
        long? rockTypeId = null,
        string visibility = "authenticated")
    {
        var cave = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = Unique(name),
            caveTypeId,
            visibility,
            parentId,
            surveyedLength,
            depth,
            rockTypeId,
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
        entrance.StatusCode.ShouldBe(
            HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());

        return caveId;
    }

    private async Task<Guid> CreateKarstAreaAsync(
        string name, double west, double south, double east, double north,
        string visibility = "authenticated",
        bool locationProtected = false)
    {
        var ring = new[]
        {
            new[] { west, south }, new[] { east, south }, new[] { east, north },
            new[] { west, north }, new[] { west, south },
        };
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            visibility,
            locationProtected,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateShapelessAreaAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = karstAreaTypeId,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateDepressionAsync(
        string name, Guid parentId, double west, double south, double east, double north)
    {
        var ring = new[]
        {
            new[] { west, south }, new[] { east, south }, new[] { east, north },
            new[] { west, north }, new[] { west, south },
        };
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = Unique(name),
            featureTypeId = sinkholeTypeId,
            parents = new[] { new { parentId, isPrimary = true } },
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
