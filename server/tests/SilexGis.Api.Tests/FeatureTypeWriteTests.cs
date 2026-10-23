// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// An installation adds its own feature kinds and adjusts the shipped ones, within limits that keep
/// stored features agreeing with their kind. Each refusal is asserted beside the edit it does
/// <em>not</em> refuse, because a guard that refused everything would pass a test that only
/// checked the refusals.
/// </summary>
public sealed class FeatureTypeWriteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;  // Editor — draws features, does not keep the vocabulary
    private HttpClient viewer = null!;
    private HttpClient admin = null!;
    private HttpClient anonymous = null!;

    public FeatureTypeWriteTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ftw-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ftw-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ftw-adm-{suffix}@t.local");

        editor = await AuthHelper.BearerClientAsync(factory, $"ftw-ed-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ftw-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"ftw-adm-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        editor?.Dispose();
        viewer?.Dispose();
        admin?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private static object Kind(
        string code,
        string name = "Bat roost",
        string category = "surface",
        string[]? classes = null,
        bool requiresParent = false,
        string protectedDisplay = "snapPoint",
        string? symbolFile = "flag.png",
        string? style = null,
        string? propertiesSchema = null,
        int sortOrder = 900) => new
        {
            code,
            name,
            description = (string?)null,
            sortOrder,
            category,
            acceptedGeometryClasses = classes ?? ["point", "multiPoint"],
            requiresParent,
            protectedDisplay,
            symbolFile,
            style,
            propertiesSchema,
        };

    private static string Code() => $"club_{Guid.NewGuid().ToString("N")[..8]}";

    private async Task<JsonElement> CreateAsync(object request)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/feature-types", request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<JsonElement> ShippedAsync(string code) =>
        (await viewer.GetFromJsonAsync<JsonElement>("/api/v1/feature-types"))
            .EnumerateArray().Single(t => t.GetProperty("code").GetString() == code);

    private async Task PlacePointAsync(long typeId)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"roost {Guid.NewGuid():N}",
            featureTypeId = typeId,
            geometry = new { type = "Point", coordinates = new[] { 23.5, 45.5 } },
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_administrator_adds_a_kind_that_every_account_reads_and_the_map_accepts()
    {
        var code = Code();
        var created = await CreateAsync(Kind(code));
        created.GetProperty("isSeeded").GetBoolean().ShouldBeFalse();
        created.GetProperty("propertiesSchemaVersion").GetInt32().ShouldBe(1);

        var listed = (await viewer.GetFromJsonAsync<JsonElement>("/api/v1/feature-types"))
            .EnumerateArray().Single(t => t.GetProperty("code").GetString() == code);
        listed.GetProperty("name").GetString().ShouldBe("Bat roost");
        listed.GetProperty("symbolFile").GetString().ShouldBe("flag.png");

        // The kind is usable at once: a feature of it can be drawn.
        await PlacePointAsync(created.GetProperty("id").GetInt64());

        // Every shipped kind says so, read off the same list the seeder writes.
        foreach (var shipped in TaxonomySeeder.ShippedFeatureTypeCodes)
        {
            (await ShippedAsync(shipped)).GetProperty("isSeeded").GetBoolean().ShouldBeTrue(shipped);
        }
    }

    [Fact]
    public async Task Keeping_the_vocabulary_is_administration()
    {
        (await anonymous.PostAsJsonAsync("/api/v1/feature-types", Kind(Code())))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await editor.PostAsJsonAsync("/api/v1/feature-types", Kind(Code())))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var pitch = await ShippedAsync("pitch");
        var id = pitch.GetProperty("id").GetInt64();
        (await editor.PutAsJsonAsync($"/api/v1/feature-types/{id}", Kind("pitch", name: "Shaft")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var club = await CreateAsync(Kind(Code()));
        (await viewer.DeleteAsync($"/api/v1/feature-types/{club.GetProperty("id").GetInt64()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_request_that_cannot_describe_a_kind_is_refused_before_anything_is_written()
    {
        var bad = new[]
        {
            Kind("Has Spaces"),
            Kind(Code(), classes: []),
            Kind(Code(), symbolFile: "../../etc/passwd"),
            Kind(Code(), symbolFile: "https://elsewhere.example/pin.png"),
        };
        foreach (var request in bad)
        {
            (await admin.PostAsJsonAsync("/api/v1/feature-types", request))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var unusableSchema = await admin.PostAsJsonAsync(
            "/api/v1/feature-types", Kind(Code(), propertiesSchema: """{"type":"nonsense"}"""));
        unusableSchema.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(unusableSchema)).ShouldBe("feature_type.schema_invalid");

        var notAnObject = await admin.PostAsJsonAsync("/api/v1/feature-types", Kind(Code(), style: "[1,2]"));
        notAnObject.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(notAnObject)).ShouldBe("feature_type.style_invalid");

        // A shipped code is taken even by the kind it names, and so is a club's own.
        (await CodeOf(await admin.PostAsJsonAsync("/api/v1/feature-types", Kind("pitch"))))
            .ShouldBe("feature_type.code_taken");
        var code = Code();
        _ = await CreateAsync(Kind(code));
        (await CodeOf(await admin.PostAsJsonAsync("/api/v1/feature-types", Kind(code))))
            .ShouldBe("feature_type.code_taken");

        // The positive twin: the same request written well is accepted, style and schema included.
        _ = await CreateAsync(Kind(
            Code(),
            style: """{"strokeColor":"#aa3300"}""",
            propertiesSchema: """{"type":"object","properties":{"count":{"type":"integer","title":"Bats"}}}"""));
    }

    [Fact]
    public async Task A_shipped_kind_keeps_what_behaviours_and_protection_rest_on_and_is_otherwise_the_installations()
    {
        var place = await ShippedAsync("cave_place");
        var id = place.GetProperty("id").GetInt64();
        var url = $"/api/v1/feature-types/{id}";
        object As(string code = "cave_place", string category = "underground", bool requiresParent = true,
            string protectedDisplay = "withhold", string[]? classes = null, string name = "Cave place") =>
            Kind(code, name: name, category: category, classes: classes ?? ["point", "multiPoint"],
                requiresParent: requiresParent, protectedDisplay: protectedDisplay,
                symbolFile: "generic_feature.png", propertiesSchema: place.GetProperty("propertiesSchema").GetString());

        // Its wording, symbol and order are the installation's own.
        var renamed = await admin.PutAsJsonAsync(url, As(name: "Place underground"));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        (await ShippedAsync("cave_place")).GetProperty("name").GetString().ShouldBe("Place underground");

        // Its code, category, protected display and need for a parent are not.
        foreach (var request in new[]
        {
            As(code: "cave_spot"),
            As(category: "surface"),
            As(protectedDisplay: "snapPoint"),
            As(requiresParent: false),
            As(classes: ["point"]),
        })
        {
            var refused = await admin.PutAsJsonAsync(url, request);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeOf(refused)).ShouldBe("feature_type.seeded_immutable");
        }

        var after = await ShippedAsync("cave_place");
        after.GetProperty("protectedDisplay").GetString().ShouldBe("withhold");
        after.GetProperty("requiresParent").GetBoolean().ShouldBeTrue();

        // And it cannot be deleted.
        var delete = await admin.DeleteAsync(url);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(delete)).ShouldBe("feature_type.seeded_immutable");

        // Put the shipped wording back for the rest of the class.
        (await admin.PutAsJsonAsync(url, As())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Once_features_use_a_kind_it_only_loosens()
    {
        var club = await CreateAsync(Kind(Code()));
        var id = club.GetProperty("id").GetInt64();
        var code = club.GetProperty("code").GetString()!;
        var url = $"/api/v1/feature-types/{id}";

        // Unused, everything about it may change.
        (await admin.PutAsJsonAsync(url, Kind(code, category: "area", classes: ["polygon"])))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync(url, Kind(code))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await PlacePointAsync(id);

        foreach (var request in new[]
        {
            Kind(code, category: "area"),
            Kind(code, classes: ["multiPoint"]),
            Kind(code, requiresParent: true),
        })
        {
            var refused = await admin.PutAsJsonAsync(url, request);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeOf(refused)).ShouldBe("feature_type.in_use");
        }

        // Loosening stays open: another shape, a new name.
        var widened = await admin.PutAsJsonAsync(
            url, Kind(code, name: "Roost", classes: ["point", "multiPoint", "polygon"]));
        widened.StatusCode.ShouldBe(HttpStatusCode.OK, await widened.Content.ReadAsStringAsync());

        // A kind in use cannot be deleted; an unused one can.
        var inUse = await admin.DeleteAsync(url);
        inUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(inUse)).ShouldBe("feature_type.in_use");

        var unused = await CreateAsync(Kind(Code()));
        (await admin.DeleteAsync($"/api/v1/feature-types/{unused.GetProperty("id").GetInt64()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_schema_version_moves_when_the_schema_means_something_new_and_only_then()
    {
        const string schema = """{"type":"object","properties":{"count":{"type":"integer"}}}""";
        const string reformatted = """
            { "properties": { "count": { "type": "integer" } }, "type": "object" }
            """;
        const string changed = """{"type":"object","properties":{"count":{"type":"number"}}}""";

        var code = Code();
        var club = await CreateAsync(Kind(code, propertiesSchema: schema));
        var url = $"/api/v1/feature-types/{club.GetProperty("id").GetInt64()}";

        var same = await admin.PutAsJsonAsync(url, Kind(code, propertiesSchema: reformatted));
        JsonDocument.Parse(await same.Content.ReadAsStringAsync()).RootElement
            .GetProperty("propertiesSchemaVersion").GetInt32().ShouldBe(1);

        var moved = await admin.PutAsJsonAsync(url, Kind(code, propertiesSchema: changed));
        JsonDocument.Parse(await moved.Content.ReadAsStringAsync()).RootElement
            .GetProperty("propertiesSchemaVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_changed_protected_display_is_recorded_in_the_audit_trail()
    {
        var code = Code();
        var club = await CreateAsync(Kind(code));
        var id = club.GetProperty("id").GetInt64();

        (await admin.PutAsJsonAsync($"/api/v1/feature-types/{id}", Kind(code, protectedDisplay: "withhold")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var changes = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(SilexGis.Domain.Entities.FeatureType) && a.EntityId == key)
            .ToListAsync();
        changes.ShouldContain(a => a.Action == "updated" && a.Changes != null
            && a.Changes.Contains("protectedDisplay", StringComparison.OrdinalIgnoreCase));
    }
}
