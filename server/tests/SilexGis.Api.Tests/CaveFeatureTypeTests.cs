// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Catalogue;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Filters;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Caves carry one shipped feature type bound to their kind: every door a cave comes in by gives it
/// that type, the type cannot be given to anything else, a club may add fields of its own to it but
/// not take away what importers write, and "type is cave" and a club field work in the filter.
/// </summary>
public sealed class CaveFeatureTypeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient owner = null!;  // Editor
    private HttpClient reader = null!; // Viewer, no exact view
    private string marker = null!;
    private long caveKindTypeId;
    private long caveClassId;
    private long entranceTypeId;
    private long surfaceAreaTypeId;

    public CaveFeatureTypeTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"cft-adm-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cft-own-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cft-read-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveKindTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.Cave).Select(t => t.Id).SingleAsync();
            caveClassId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            surfaceAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "surface_area").Select(t => t.Id).SingleAsync();
        }

        admin = await AuthHelper.BearerClientAsync(factory, $"cft-adm-{marker}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cft-own-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cft-read-{marker}@t.local");
    }

    [Fact]
    public async Task The_cave_type_ships_bound_to_caves_with_the_hill_after_it_and_no_surface_pit()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var types = await db.FeatureTypes.AsNoTracking().ToDictionaryAsync(t => t.Code);

        var cave = types[FeatureTypeSeeds.Cave];
        cave.BoundKind.ShouldBe(FeatureKind.Cave);
        cave.Category.ShouldBe(FeatureCategory.Underground);
        cave.ProtectedDisplay.ShouldBe(ProtectedDisplay.SnapPoint);
        cave.RequiresParent.ShouldBeFalse();
        cave.PropertiesSchema.ShouldNotBeNull();

        // Every other kind is generic: the cave type is the only bound one.
        types.Values.Where(t => t.BoundKind != FeatureKind.Generic).Select(t => t.Code)
            .ShouldBe([FeatureTypeSeeds.Cave]);

        // A hill is modelled exactly on a massif.
        var hill = types[FeatureTypeSeeds.Hill];
        var massif = types[FeatureTypeSeeds.Massif];
        hill.Category.ShouldBe(massif.Category);
        hill.AcceptedGeometryClasses.ShouldBe(massif.AcceptedGeometryClasses, ignoreOrder: true);
        hill.ProtectedDisplay.ShouldBe(massif.ProtectedDisplay);
        hill.RequiresParent.ShouldBe(massif.RequiresParent);

        // Appended, in list order, after every kind that shipped before them.
        cave.SortOrder.ShouldBe(types["surface_area"].SortOrder + 10);
        hill.SortOrder.ShouldBe(cave.SortOrder + 10);

        // One pit: the cave classification. The surface kind no longer ships.
        types.ShouldNotContainKey("pit");
        (await db.CaveTypes.AnyAsync(t => t.Code == "pit")).ShouldBeTrue();
    }

    [Fact]
    public async Task Every_door_a_cave_comes_in_by_gives_it_the_cave_type()
    {
        // The cave form.
        var viaForm = await CreateCaveAsync($"Form cave {marker}");

        // A device upload, under a surface area.
        var area = await CreateAreaAsync();
        var setResponse = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = new[] { area },
            settings = new { },
        });
        setResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await setResponse.Content.ReadAsStringAsync());
        var set = (await setResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var viaDevice = Guid.CreateVersion7();
        var upload = await UploadAsync(set, new
        {
            id = viaDevice,
            kind = "cave",
            parentId = area,
            baseRevision = (DateTimeOffset?)null,
            deleted = false,
            name = $"Device cave {marker}",
            caveTypeCode = "cave",
            isMain = false,
            properties = new Dictionary<string, object>
            {
                ["speleolocCaveLocalIndex"] = "C7",
                ["speleolocSchemaVersion"] = 1,
            },
        });
        upload.GetProperty("rows")[0].GetProperty("status").GetString().ShouldBe("created", upload.ToString());

        // The write service every importer, the demo seeder and the photo routes go through.
        Guid viaWriter;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            var feature = new Feature
            {
                Name = $"Imported cave {marker}",
                OwnerUserId = await db.Users.Where(u => u.Email == $"cft-own-{marker}@t.local")
                    .Select(u => u.Id).SingleAsync(),
            };
            var cave = new Cave { CaveTypeId = caveClassId };
            feature.Cave = cave;
            await writer.CreateCaveAsync(feature, cave, []);
            await db.SaveChangesAsync();
            viaWriter = feature.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var ids = new[] { viaForm, viaDevice, viaWriter };
            (await db.Features.IgnoreQueryFilters()
                    .CountAsync(f => ids.Contains(f.Id) && f.Kind == FeatureKind.Cave && f.FeatureTypeId == caveKindTypeId))
                .ShouldBe(3);

            // Nothing in the whole database is a cave without its type, whoever made it.
            (await db.Features.IgnoreQueryFilters()
                    .CountAsync(f => f.Kind == FeatureKind.Cave && f.FeatureTypeId != caveKindTypeId))
                .ShouldBe(0);
        }

        // The device reads it back under the kind's stable code.
        var page = await GetAsync(owner, $"/api/v1/sync/sets/{set}/download");
        var row = page.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == viaDevice);
        row.GetProperty("kind").GetString().ShouldBe("cave");
        row.GetProperty("featureTypeCode").GetString().ShouldBe(FeatureTypeSeeds.Cave);
    }

    [Fact]
    public async Task No_generic_feature_and_no_permission_rule_can_be_given_the_cave_type()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Not a cave {marker}",
            featureTypeId = caveKindTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.1, 45.1 } },
            visibility = "private",
            locationProtected = false,
            parents = Array.Empty<object>(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(created)).ShouldBe(FeatureWriteService.TypeBoundCode);

        // Retyping an existing generic feature as a cave is refused the same way.
        var area = await CreateAreaAsync();
        var current = await GetAsync(owner, $"/api/v1/features/{area}");
        var retyped = await owner.PutWithIfMatchAsync($"/api/v1/features/{area}", new
        {
            name = $"Area {marker}",
            featureTypeId = caveKindTypeId,
            visibility = "authenticated",
            locationProtected = false,
            description = (string?)null,
            properties = new { },
        });
        retyped.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await retyped.Content.ReadAsStringAsync());
        (await CodeOf(retyped)).ShouldBe(FeatureWriteService.TypeBoundCode);
        current.GetProperty("feature").GetProperty("featureTypeCode").GetString().ShouldBe("surface_area");

        // A device naming the cave type for a generic row is told the installation has no such place kind.
        var set = await CreateSetAsync(area);
        var upload = await UploadAsync(set, new
        {
            id = Guid.CreateVersion7(),
            kind = "generic",
            parentId = area,
            baseRevision = (DateTimeOffset?)null,
            deleted = false,
            name = $"Device not-a-cave {marker}",
            featureTypeCode = FeatureTypeSeeds.Cave,
            isMain = false,
            geometry = new { type = "Point", coordinates = new[] { 25.1, 45.1 } },
        });
        upload.GetProperty("rows")[0].GetProperty("code").GetString().ShouldBe("sync.type_unknown");

        // A permission rule narrowed by the cave type: "every cave" is the cave kind already.
        var ruleset = await admin.PostAsJsonAsync("/api/v1/permission-groups", new
        {
            name = $"Cave rules {marker}",
            description = (string?)null,
        });
        ruleset.StatusCode.ShouldBe(HttpStatusCode.Created, await ruleset.Content.ReadAsStringAsync());
        var groupId = (await ruleset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        object Rule(long typeId) => new
        {
            entries = new[]
            {
                new { effect = "allow", domain = "features", actions = "read", scopeKind = "all", featureTypeId = typeId },
            },
        };
        var refused = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", Rule(caveKindTypeId));
        refused.IsSuccessStatusCode.ShouldBeFalse(await refused.Content.ReadAsStringAsync());

        // The same rule over a generic kind is accepted, so the refusal is about the cave type.
        var accepted = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", Rule(surfaceAreaTypeId));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_cave_type_keeps_its_kind_its_display_and_the_fields_importers_write()
    {
        var shipped = await GetTypeAsync(caveKindTypeId);
        shipped.GetProperty("boundKind").GetString().ShouldBe("cave");
        shipped.GetProperty("isSeeded").GetBoolean().ShouldBeTrue();

        // Not deleted, not re-categorised, not given another display.
        var deleted = await admin.DeleteAsync($"/api/v1/feature-types/{caveKindTypeId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(deleted)).ShouldBe(FeatureTypeWriteService.SeededImmutableCode);

        var withheld = await PutTypeAsync(shipped, protectedDisplay: "withhold");
        withheld.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(withheld)).ShouldBe(FeatureTypeWriteService.SeededImmutableCode);

        var schema = JsonNode.Parse(shipped.GetProperty("propertiesSchema").GetString()!)!.AsObject();

        // A shipped field taken away, retyped, made required or the schema closed: refused.
        var dropped = schema.DeepClone().AsObject();
        dropped["properties"]!.AsObject().Remove(SpeologieMapping.Keys.Id);
        var retyped = schema.DeepClone().AsObject();
        retyped["properties"]![SpeologieMapping.Keys.HydroBasinId]!["type"] = "string";
        var required = schema.DeepClone().AsObject();
        required["required"] = new JsonArray("speologieSlug");
        var closed = schema.DeepClone().AsObject();
        closed["additionalProperties"] = false;
        foreach (var refused in new[] { dropped, retyped, required, closed })
        {
            var response = await PutTypeAsync(shipped, schema: refused.ToJsonString());
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused.ToJsonString());
            (await CodeOf(response)).ShouldBe(FeatureTypeWriteService.BoundSchemaCode);
        }

        (await PutTypeAsync(shipped, clearSchema: true)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A club's own field is what the type is for.
        var widened = schema.DeepClone().AsObject();
        widened["properties"]!["riggingMetres"] = new JsonObject
        {
            ["type"] = "number",
            ["title"] = "Rope needed (m)",
            ["minimum"] = 0,
        };
        var accepted = await PutTypeAsync(shipped, schema: widened.ToJsonString());
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("boundKind").GetString()
            .ShouldBe("cave");

        // The cave's own field is now validated on the cave form, and stored when it is right.
        var cave = await CreateCaveAsync($"Rigged cave {marker}", properties: new { riggingMetres = 45 });
        var bad = await owner.PutWithIfMatchAsync($"/api/v1/caves/{cave}", CaveBody(
            $"Rigged cave {marker}", properties: new { riggingMetres = "lots" }));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(bad)).ShouldBe("feature.properties_invalid");

        // And it works in the filter, scoped to caves by the type condition, as a sinkhole's
        // diameter does: one cave matches, the shallower one does not.
        var shallow = await CreateCaveAsync($"Short rope cave {marker}", properties: new { riggingMetres = 10 });
        var document = new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode(
                [
                    new ConditionNode(
                        FeatureFilterFields.TypeId, FilterOp.Equals,
                        [new IdValue(caveKindTypeId.ToString(CultureInfo.InvariantCulture))]),
                    new ConditionNode(
                        FeaturePropertyFields.FieldKey(FeatureTypeSeeds.Cave, "riggingMetres"),
                        FilterOp.GreaterOrEqual, [new NumberValue(30)]),
                    new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(marker)]),
                ])),
            ],
        };
        var page = await GetAsync(owner, $"/api/v1/features?pageSize=100&filter={FilterToken.Encode(document)}");
        var ids = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        ids.ShouldBe([cave]);
        ids.ShouldNotContain(shallow);

        // "Type is cave" alone names every cave of this class's and nothing else.
        var typeOnly = new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode(
                [
                    new ConditionNode(
                        FeatureFilterFields.TypeId, FilterOp.Equals,
                        [new IdValue(caveKindTypeId.ToString(CultureInfo.InvariantCulture))]),
                    new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(marker)]),
                ])),
            ],
        };
        var all = await GetAsync(owner, $"/api/v1/features?pageSize=100&filter={FilterToken.Encode(typeOnly)}");
        var allIds = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        allIds.ShouldContain(cave);
        allIds.ShouldContain(shallow);
        all.GetProperty("items").EnumerateArray()
            .All(i => i.GetProperty("kind").GetString() == "cave").ShouldBeTrue();

        // Put the shipped schema back, so the other cases see the type as it ships.
        (await PutTypeAsync(await GetTypeAsync(caveKindTypeId), schema: schema.ToJsonString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task What_the_catalogue_and_a_photo_library_write_into_a_cave_is_what_its_schema_declares()
    {
        string schema;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            schema = (await db.FeatureTypes.SingleAsync(t => t.Id == caveKindTypeId)).PropertiesSchema!;
        }

        using var parsed = JsonDocument.Parse(schema);
        var declared = parsed.RootElement.GetProperty("properties").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetProperty("type").GetString());

        // A catalogue record with every field the mapping reads filled in.
        var record = new SpeologieRecord(
            Id: 4242, Title: "Peștera de probă", Slug: "pestera-de-proba", Descriere: "<p>Text.</p>",
            Judet: "bh", Localitate: "Satul de probă", Munte: "bihor", Lungime: 120.5, Denivelare: 30,
            DenNegativa: -30, Altitudine: 812, NrHidro: "2.12", BazinHidroId: 605, Roca: "00",
            Scufundabila: "1", Clasificare: "clasaB", Stiinta: "bio, geo", Disparuta: true, CodAp: "2.613");
        var catalogue = SpeologieMapping.ToCaveValues(record, DateTimeOffset.UtcNow, 4000).Properties;
        var photo = PhotoLibraryProvenance.Properties("immich", "asset-1");

        var validator = factory.Services.GetRequiredService<ITypedPropertiesValidator>();
        foreach (var bag in new[] { catalogue, photo })
        {
            validator.Validate(schema, bag.ToJsonString()).ShouldBeEmpty(bag.ToJsonString());
            foreach (var (key, value) in bag)
            {
                declared.ShouldContainKey(key);
                var expected = value!.GetValueKind() switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Number => "integer",
                    var other => other.ToString(),
                };
                declared[key].ShouldBe(expected, key);
            }
        }

        // The locality and the altitude went to their redacted columns, never to the bag.
        catalogue.ToJsonString().ShouldNotContain("Satul de probă");
        catalogue.ToJsonString().ShouldNotContain("812");

        // The declared set is closed in this test, so nothing locating joins it unnoticed: the bag
        // is handed out unfiltered, on the share path too.
        declared.Keys.ShouldBe(
        [
            "speologieId", "speologieSlug", "speologieUrl", "speologieJudet", "speologieNrHidro",
            "speologieBazinHidroId", "speologieRoca", "speologieScufundabila", "speologieStiinta",
            "speologieCodAp", "speologieDisparuta", "speologieRetrievedAt",
            "photoLibrarySource", "photoLibraryReference",
            "speleolocCaveLocalIndex", "speleolocSchemaVersion",
        ], ignoreOrder: true);

        // A cave imported with that bag can still be edited through the cave form, which sends its
        // stored bag back unchanged.
        Guid imported;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            var feature = new Feature
            {
                Name = $"Catalogue cave {marker}",
                OwnerUserId = await db.Users.Where(u => u.Email == $"cft-own-{marker}@t.local")
                    .Select(u => u.Id).SingleAsync(),
                Visibility = Visibility.Authenticated,
                Properties = catalogue.ToJsonString(),
            };
            var cave = new Cave { CaveTypeId = caveClassId };
            feature.Cave = cave;
            await writer.CreateCaveAsync(feature, cave, []);
            await db.SaveChangesAsync();
            imported = feature.Id;
        }

        var stored = await GetAsync(owner, $"/api/v1/caves/{imported}");
        var edited = await owner.PutWithIfMatchAsync($"/api/v1/caves/{imported}", CaveBody(
            $"Catalogue cave {marker} edited", properties: stored.GetProperty("properties")));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_protected_cave_is_shown_by_its_kind_whatever_its_types_stored_display_says()
    {
        var cave = await CreateCaveAsync($"Kind-first cave {marker}", locationProtected: true);
        var entrance = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            name = $"Entrance {marker}",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.4611, 45.5322, 640.0 } },
            positionQuality = "gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        var token = (await (await owner.PostAsJsonAsync(
                $"/api/v1/features/{cave}/shares", new { mode = "public", includeSubtree = true }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        // Behind the locked admin route: the stored value itself says withhold.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.FeatureTypes.Where(t => t.Id == caveKindTypeId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ProtectedDisplay, ProtectedDisplay.Withhold));
        }

        try
        {
            using var anonymous = factory.CreateClient();
            var shared = await anonymous.GetAsync($"/api/v1/shared/features/{token}");
            shared.StatusCode.ShouldBe(HttpStatusCode.OK);
            var envelope = await shared.Content.ReadFromJsonAsync<JsonElement>();
            envelope.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
            envelope.GetProperty("feature").GetProperty("geometry").ValueKind.ShouldBe(JsonValueKind.Object);

            var detail = await GetAsync(reader, $"/api/v1/features/{cave}");
            detail.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
            detail.GetProperty("feature").GetProperty("omittedLocation").GetBoolean().ShouldBeFalse();

            var file = await GetAsync(reader, $"/api/v1/export/features?format=geojson&search={marker}");
            file.GetProperty("features").EnumerateArray()
                .Count(f => f.GetProperty("properties").GetProperty("kind").GetString() == "cave")
                .ShouldBe(1);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.FeatureTypes.Where(t => t.Id == caveKindTypeId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ProtectedDisplay, ProtectedDisplay.SnapPoint));
        }
    }

    private object CaveBody(string name, object? properties = null, bool locationProtected = false) => new
    {
        name,
        caveTypeId = caveClassId,
        visibility = "authenticated",
        locationProtected,
        explorationStatus = "unknown",
        isShowCave = false,
        properties,
    };

    private async Task<Guid> CreateCaveAsync(string name, object? properties = null, bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody(name, properties, locationProtected));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Area {marker}",
            featureTypeId = surfaceAreaTypeId,
            visibility = "authenticated",
            locationProtected = false,
            parents = Array.Empty<object>(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateSetAsync(Guid root)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}",
            uploadVisibility = "private",
            rootFeatureIds = new[] { root },
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> UploadAsync(Guid set, object row)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { row },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetTypeAsync(long id) =>
        (await GetAsync(owner, "/api/v1/feature-types")).EnumerateArray()
            .Single(t => t.GetProperty("id").GetInt64() == id);

    private Task<HttpResponseMessage> PutTypeAsync(
        JsonElement type, string? protectedDisplay = null, string? schema = null, bool clearSchema = false) =>
        admin.PutAsJsonAsync($"/api/v1/feature-types/{type.GetProperty("id").GetInt64()}", new
        {
            code = type.GetProperty("code").GetString(),
            name = type.GetProperty("name").GetString(),
            description = (string?)null,
            sortOrder = type.GetProperty("sortOrder").GetInt32(),
            category = type.GetProperty("category").GetString(),
            acceptedGeometryClasses = type.GetProperty("acceptedGeometryClasses"),
            requiresParent = type.GetProperty("requiresParent").GetBoolean(),
            protectedDisplay = protectedDisplay ?? type.GetProperty("protectedDisplay").GetString(),
            symbolFile = (string?)null,
            style = (string?)null,
            propertiesSchema = clearSchema ? null : schema ?? type.GetProperty("propertiesSchema").GetString(),
        });

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin?.Dispose();
        owner?.Dispose();
        reader?.Dispose();
    }
}
