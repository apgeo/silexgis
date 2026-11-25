// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Bodies a write route used to take as written or fail on with a server fault: a number past
/// what its column holds, an audience nobody defined, a member left out or sent as null where a
/// validator then read it, a list without a bound, a name longer than the names derived from it
/// can hold, and a search whose wildcard characters were not taken literally. Each is now a
/// validation failure naming the member — and the value just inside the bound still lands.
/// </summary>
public sealed class RequestValidationGapTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private long caveTypeId;
    private long entranceTypeId;

    public RequestValidationGapTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var email = $"rvg-{Guid.NewGuid():N}"[..16] + "@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, email);
        admin = await AuthHelper.BearerClientAsync(factory, email);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// A cave's measures are bounded by their columns: the largest value the column holds lands,
    /// one cent above it is refused naming the member rather than overflowing the column. Altitude
    /// is bounded by the earth.
    /// </summary>
    [Theory]
    [InlineData("surveyedLength", 99_999_999.99, 100_000_000.00)]
    [InlineData("realExtension", 99_999_999.99, 100_000_000.00)]
    [InlineData("negativeDepth", -99_999_999.99, -100_000_000.00)]
    [InlineData("showCaveLength", 99_999_999.99, 100_000_000.00)]
    [InlineData("ramificationIndex", 999.999, 1000)]
    [InlineData("altitude", 9000, 9000.01)]
    public async Task A_cave_measure_past_its_column_is_refused_and_the_largest_it_holds_lands(
        string member, double largest, double beyond)
    {
        using var landed = await admin.PostAsJsonAsync("/api/v1/caves", CaveBody(member, (decimal)largest));
        landed.StatusCode.ShouldBe(HttpStatusCode.Created, await landed.Content.ReadAsStringAsync());

        using var refused = await admin.PostAsJsonAsync("/api/v1/caves", CaveBody(member, (decimal)beyond));
        await ShouldBeRefusedNamingAsync(refused, member);
    }

    [Fact]
    public async Task An_entrance_altitude_off_the_earth_is_refused_and_one_on_it_lands()
    {
        using var cave = await admin.PostAsJsonAsync("/api/v1/caves", CaveBody("altitude", 100m));
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, await cave.Content.ReadAsStringAsync());
        var caveId = JsonDocument.Parse(await cave.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var refused = await admin.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", EntranceBody(1_000_000_000m));
        await ShouldBeRefusedNamingAsync(refused, "altitude");

        using var landed = await admin.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", EntranceBody(640m));
        landed.StatusCode.ShouldBe(HttpStatusCode.Created, await landed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An enum binds any number of its underlying type; an audience outside the defined ones is
    /// refused on every write that carries one, instead of being stored as a fifth audience.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/features")]
    [InlineData("/api/v1/map-views")]
    [InlineData("/api/v1/trip-logs")]
    public async Task An_audience_nobody_defined_is_refused(string route)
    {
        var body = new JsonObject
        {
            ["kind"] = "generic",
            ["name"] = "Undefined audience",
            ["title"] = "Undefined audience",
            ["featureTypeId"] = 1,
            ["config"] = new JsonObject(),
            ["isHome"] = false,
            ["participants"] = new JsonArray(),
            ["visibility"] = 99,
        };

        using var refused = await admin.PostAsJsonAsync(route, body);
        await ShouldBeRefusedNamingAsync(refused, "visibility");
    }

    /// <summary>
    /// A member left out binds as null even where the record does not allow one; the validators
    /// that then read it answered a server fault. Each is a validation failure now.
    /// </summary>
    [Fact]
    public async Task A_member_left_out_or_sent_as_null_is_a_validation_failure_not_a_server_fault()
    {
        using var errors = await admin.PostAsJsonAsync("/api/v1/diagnostics/client-errors", new { });
        await ShouldBeRefusedNamingAsync(errors, "errors");

        using var access = await admin.PutAsJsonAsync($"/api/v1/objects/feature/{Guid.NewGuid()}/access", new { });
        await ShouldBeRefusedNamingAsync(access, "entries");

        using var nullEntry = await admin.PutAsJsonAsync(
            $"/api/v1/objects/feature/{Guid.NewGuid()}/access", new { entries = new object?[] { null } });
        await ShouldBeRefusedNamingAsync(nullEntry, "entries[0]");

        using var address = await admin.PostAsJsonAsync("/api/v1/me/addresses", new
        {
            label = "Base camp",
            sortOrder = 0,
            geom = new { type = "Point" },
        });
        await ShouldBeRefusedNamingAsync(address, "geom.coordinates");
    }

    /// <summary>
    /// A null level band bound from <c>[null]</c> was skipped by the band rules and stored as a
    /// reading; beside a real band it was dereferenced. Both are refused and no reading is written.
    /// </summary>
    [Fact]
    public async Task A_null_level_band_is_refused_and_nothing_is_stored()
    {
        using var cave = await admin.PostAsJsonAsync("/api/v1/caves", CaveBody("altitude", 100m));
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, await cave.Content.ReadAsStringAsync());
        var caveId = JsonDocument.Parse(await cave.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var alone = await admin.PutAsJsonAsync(
            $"/api/v1/caves/{caveId}/level-bands", new { bands = new object?[] { null }, note = (string?)null });
        await ShouldBeRefusedNamingAsync(alone, "bands[0]");

        using var beside = await admin.PutAsJsonAsync($"/api/v1/caves/{caveId}/level-bands", new
        {
            bands = new object?[] { new { fromM = 1d, toM = 2d, label = (string?)null }, null },
            note = (string?)null,
        });
        await ShouldBeRefusedNamingAsync(beside, "bands[1]");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.CaveLevelBands.CountAsync(b => b.CaveFeatureId == caveId)).ShouldBe(0);

        using var landed = await admin.PutAsJsonAsync($"/api/v1/caves/{caveId}/level-bands", new
        {
            bands = new[] { new { fromM = 1d, toM = 2d, label = (string?)null } },
            note = (string?)null,
        });
        landed.StatusCode.ShouldBe(HttpStatusCode.OK, await landed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A set's membership replace names at most a bounded number of features: one more is a
    /// validation failure, while the bound itself passes validation and reaches the handler.
    /// </summary>
    [Fact]
    public async Task A_feature_set_replace_past_the_bound_is_refused()
    {
        var route = $"/api/v1/feature-sets/{Guid.NewGuid()}/members";
        using var refused = await admin.PutAsJsonAsync(
            route, new { featureIds = Enumerable.Range(0, 5_001).Select(_ => Guid.NewGuid()).ToArray() });
        await ShouldBeRefusedNamingAsync(refused, "featureIds");

        using var atBound = await admin.PutAsJsonAsync(
            route, new { featureIds = Enumerable.Range(0, 5_000).Select(_ => Guid.NewGuid()).ToArray() });
        var text = await atBound.Content.ReadAsStringAsync();
        ((int)atBound.StatusCode).ShouldBeLessThan(500, text);
        if (atBound.StatusCode == HttpStatusCode.BadRequest)
        {
            JsonNode.Parse(text)!["code"]!.GetValue<string>().ShouldNotBe("validation.failed", text);
        }
    }

    /// <summary>
    /// A caving group's name fills at most its column, and the permission groups created with it
    /// derive their names from it: a name of the full width creates the club and both groups
    /// within their own columns, and one character more is refused on create and on rename.
    /// </summary>
    [Fact]
    public async Task A_caving_group_name_of_the_column_width_creates_its_groups_and_a_longer_one_is_refused()
    {
        var name = ("Clubul de speologie " + Guid.NewGuid().ToString("N") + new string('x', 100))[..100];
        using var created = await admin.PostAsJsonAsync("/api/v1/caving-groups", new { name, type = "cavingClub" });
        var text = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stem = name[..60];
            var derived = await db.PermissionGroups.Where(g => g.Name.StartsWith(stem)).ToListAsync();
            derived.Count.ShouldBe(2);
            derived.ShouldAllBe(g => g.Name.Length <= 100 && g.Slug.Length <= 100);
            derived.ShouldContain(g => g.Name.EndsWith(" — managers"));
            derived.ShouldContain(g => g.Name.EndsWith(" — members"));
        }

        using var tooLong = await admin.PostAsJsonAsync(
            "/api/v1/caving-groups", new { name = name + "y", type = "cavingClub" });
        await ShouldBeRefusedNamingAsync(tooLong, "name");

        using var renamed = await admin.PutAsJsonAsync($"/api/v1/caving-groups/{id}", new { name = name + "y", type = "cavingClub" });
        await ShouldBeRefusedNamingAsync(renamed, "name");
    }

    /// <summary>
    /// The link picker matches what was typed literally: an underscore is a character to find,
    /// not "any one character".
    /// </summary>
    [Fact]
    public async Task The_link_picker_takes_wildcard_characters_literally()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var underscored = $"Pk{marker}A_B";
        var lookalike = $"Pk{marker}AxB";
        foreach (var name in new[] { underscored, lookalike })
        {
            using var created = await admin.PostAsJsonAsync("/api/v1/caving-groups", new { name, type = "cavingClub" });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        var hits = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/targets/search?type=cavingGroup&q={Uri.EscapeDataString($"{marker}A_B")}");
        var names = hits.EnumerateArray().Select(h => h.GetProperty("title").GetString()).ToList();
        names.ShouldContain(n => n != null && n.Contains(underscored));
        names.ShouldNotContain(n => n != null && n.Contains(lookalike));
    }

    /// <summary>
    /// A position outside the world's range is refused by every route that takes a free-form
    /// geometry, with that route's own code, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task A_geometry_outside_the_world_is_refused_on_features_trips_and_expeditions()
    {
        var offWorld = new { type = "Point", coordinates = new[] { 500.0, 95.0 } };
        long sinkholeTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        using var feature = await admin.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = "Off the world",
            featureTypeId = sinkholeTypeId,
            geometry = offWorld,
            visibility = "private",
        });
        (await CodeAsync(feature)).ShouldBe("feature.geometry_invalid");

        using var trip = await admin.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = "Off the world",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            geom = offWorld,
            visibility = "private",
        });
        (await CodeAsync(trip)).ShouldBe("trip_log.geometry_invalid");

        using var expedition = await admin.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = "Off the world",
            description = (string?)null,
            startDate = "2026-07-18",
            endDate = (string?)null,
            cavingGroupId = (Guid?)null,
            geom = new { type = "Polygon", coordinates = new[] { new[] { new[] { 25.0, 45.0 }, new[] { 200.0, 45.0 }, new[] { 25.0, 46.0 }, new[] { 25.0, 45.0 } } } },
            visibility = "private",
        });
        (await CodeAsync(expedition)).ShouldBe("expedition.geometry_invalid");

        static async Task<string?> CodeAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
            return JsonNode.Parse(text)!["code"]?.GetValue<string>();
        }
    }

    /// <summary>
    /// A centerline file with an altitude no survey could hold is refused as unreadable, rather
    /// than measured into a length its column cannot store; one with ordinary altitudes lands.
    /// </summary>
    [Fact]
    public async Task A_centerline_with_an_absurd_altitude_is_refused_as_unreadable()
    {
        using var cave = await admin.PostAsJsonAsync("/api/v1/caves", CaveBody("altitude", 100m));
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, await cave.Content.ReadAsStringAsync());
        var caveId = JsonDocument.Parse(await cave.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var refused = await UploadCenterlineAsync(caveId, "[[25.45,45.55,900],[25.451,45.55,1e15]]");
        var text = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        JsonNode.Parse(text)!["code"]!.GetValue<string>().ShouldBe("centerline.unreadable");

        using var landed = await UploadCenterlineAsync(caveId, "[[25.45,45.55,900],[25.451,45.55,850]]");
        landed.StatusCode.ShouldBe(HttpStatusCode.Created, await landed.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> UploadCenterlineAsync(Guid caveId, string coordinates)
    {
        var body = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":" + coordinates + "}}]}";
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new("application/geo+json");
        using var form = new MultipartFormDataContent { { content, "file", "Centerline.geojson" } };
        return await admin.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
    }

    /// <summary>
    /// A trip's participant list is bounded — every new name on it becomes a roster entry — and so
    /// are a feature's free-form properties; the bound itself lands.
    /// </summary>
    [Fact]
    public async Task A_trip_naming_too_many_people_and_a_feature_with_oversized_properties_are_refused()
    {
        object Trip(int people) => new
        {
            title = $"Crowd {Guid.NewGuid():N}"[..20],
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Enumerable.Range(0, people).Select(_ => new { caverId = (Guid?)null, newCaverName = "x" }).ToArray(),
            visibility = "private",
        };

        using var crowd = await admin.PostAsJsonAsync("/api/v1/trip-logs/", Trip(501));
        await ShouldBeRefusedNamingAsync(crowd, "participants");

        long sinkholeTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        object Feature(int length) => new
        {
            kind = "generic",
            name = "Heavy",
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
            properties = new { note = new string('p', length) },
            visibility = "private",
        };
        using var heavy = await admin.PostAsJsonAsync("/api/v1/features", Feature(262_144));
        await ShouldBeRefusedNamingAsync(heavy, "properties");
        using var light = await admin.PostAsJsonAsync("/api/v1/features", Feature(1_000));
        light.StatusCode.ShouldBe(HttpStatusCode.Created, await light.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Club visibility without a club would be an audience that admits nobody, so a feature, a
    /// saved map view and a trip each refuse it naming the missing club.
    /// </summary>
    [Fact]
    public async Task Club_visibility_without_a_club_is_refused_on_features_map_views_trips_and_events()
    {
        long sinkholeTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        using var feature = await admin.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = "Clubless",
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
            cavingGroupId = (Guid?)null,
            visibility = "cavingGroup",
        });
        await ShouldBeRefusedNamingAsync(feature, "cavingGroupId");

        using var view = await admin.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name = "Clubless view",
            description = (string?)null,
            config = new { },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility = "cavingGroup",
        });
        await ShouldBeRefusedNamingAsync(view, "cavingGroupId");

        using var trip = await admin.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = "Clubless trip",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            cavingGroupId = (Guid?)null,
            visibility = "cavingGroup",
        });
        await ShouldBeRefusedNamingAsync(trip, "cavingGroupId");

        object EventBody(string visibility) => new
        {
            title = "Clubless meeting",
            kind = "clubMeeting",
            startDate = "2054-10-05",
            endDate = (string?)null,
            place = (string?)null,
            visibility,
            cavingGroupId = (Guid?)null,
            recurrence = (object?)null,
        };

        using var meeting = await admin.PostAsJsonAsync("/api/v1/events", EventBody("cavingGroup"));
        await ShouldBeRefusedNamingAsync(meeting, "cavingGroupId");

        using var open = await admin.PostAsJsonAsync("/api/v1/events", EventBody("authenticated"));
        open.StatusCode.ShouldBe(HttpStatusCode.Created, await open.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_gallery_refuses_an_over_long_search_and_takes_one_at_the_bound()
    {
        using var refused = await admin.GetAsync($"/api/v1/photos/?search={new string('s', 201)}");
        await ShouldBeRefusedNamingAsync(refused, "search");

        using var taken = await admin.GetAsync($"/api/v1/photos/?search={new string('s', 200)}");
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_roster_refuses_an_over_long_search_and_an_over_long_note_and_takes_both_at_the_bound()
    {
        using var refusedSearch = await admin.GetAsync($"/api/v1/cavers/?search={new string('s', 201)}");
        await ShouldBeRefusedNamingAsync(refusedSearch, "search");

        using var takenSearch = await admin.GetAsync($"/api/v1/cavers/?search={new string('s', 200)}");
        takenSearch.StatusCode.ShouldBe(HttpStatusCode.OK, await takenSearch.Content.ReadAsStringAsync());

        var name = $"Bound caver {Guid.NewGuid():N}"[..24];
        using var refusedNote = await admin.PostAsJsonAsync("/api/v1/cavers",
            new { fullName = name, notes = new string('n', 10_001) });
        await ShouldBeRefusedNamingAsync(refusedNote, "notes");

        using var created = await admin.PostAsJsonAsync("/api/v1/cavers",
            new { fullName = name, notes = new string('n', 10_000) });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var refusedEdit = await admin.PutAsJsonAsync($"/api/v1/cavers/{id}",
            new { fullName = name, notes = new string('n', 10_001) });
        await ShouldBeRefusedNamingAsync(refusedEdit, "notes");
    }

    private object CaveBody(string member, decimal value)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Bound cave {Guid.NewGuid():N}"[..30],
            ["caveTypeId"] = caveTypeId,
            ["visibility"] = "authenticated",
            ["locationProtected"] = false,
            ["explorationStatus"] = "unknown",
            ["isShowCave"] = false,
        };
        body[member] = value;
        return body;
    }

    private object EntranceBody(decimal altitude) => new
    {
        name = "Bound entrance",
        entranceTypeId,
        isMain = true,
        geom = new { type = "Point", coordinates = new[] { 25.4611, 45.5322 } },
        altitude,
        positionQuality = "gps",
    };

    /// <summary>A 400 with the generic validation code and an error for the named member.</summary>
    private static async Task ShouldBeRefusedNamingAsync(HttpResponseMessage response, string member)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        var problem = JsonNode.Parse(text)!;
        problem["code"]!.GetValue<string>().ShouldBe("validation.failed", text);
        problem["errors"]!.AsObject().Select(e => e.Key.ToLowerInvariant())
            .ShouldContain(member.ToLowerInvariant(), text);
    }

    public void Dispose()
    {
        admin?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
