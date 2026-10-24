// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A cave typed by any of its names — its name, an other name, its registry code — is found by every
/// picker, which all ask the selector route with the names field; and a hit found by something other
/// than its title says what found it.
/// </summary>
/// <remarks>
/// What a match may be made on is the other half: only what the reader is shown on the cave's page.
/// A cave the reader may not read is not found by its code, and nothing positional is ever matched or
/// reported.
/// </remarks>
public sealed class CaveNameMatchTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Wire = Build();

    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public CaveNameMatchTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_cave_is_found_by_its_name_its_other_names_and_its_code_and_says_which_found_it()
    {
        using var author = await ClientAsync(GlobalRoles.Editor, "cn-author");
        using var reader = await ClientAsync(GlobalRoles.Viewer, "cn-reader");
        var cave = await CreateCaveAsync(
            author, $"Peștera Urșilor {tag}", $"Avenul Bătrân {tag}, Gura Mare", $"RX-{tag}-17", "authenticated", false);

        // By its name, any part, accents ignored: found, and nothing to explain.
        var byName = await HitAsync(reader, $"pestera ursilor {tag}");
        byName.ShouldNotBeNull().Id.ShouldBe(cave);
        byName.Matched.ShouldBeNull();

        // By an other name, typed without its diacritics and in another case.
        var byOther = (await HitAsync(reader, $"avenul batran {tag}")).ShouldNotBeNull();
        byOther.Id.ShouldBe(cave);
        byOther.Matched.ShouldNotBeNull().Field.ShouldBe(FeatureFilterFields.OtherToponyms);
        byOther.Matched.Text.ShouldBe($"Avenul Bătrân {tag}, Gura Mare");

        // By any part of its code, case ignored.
        var byCode = (await HitAsync(reader, $"x-{tag}-1")).ShouldNotBeNull();
        byCode.Id.ShouldBe(cave);
        byCode.Matched.ShouldNotBeNull().Field.ShouldBe(FeatureFilterFields.Code);
        byCode.Matched.Text.ShouldBe($"RX-{tag}-17");

        // The name still wins when it matches as well as another field does.
        var both = (await HitAsync(reader, tag)).ShouldNotBeNull();
        both.Matched.ShouldBeNull();

        // The plain name field is what it always was: the code does not find the cave through it.
        var nameOnly = await QueryAsync(reader, new ConditionNode(
            FeatureFilterFields.Name, FilterOp.Contains, [new TextValue($"RX-{tag}")]));
        nameOnly.Worlds.Single().Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cave_the_reader_may_not_read_is_never_found_by_its_code_or_another_name()
    {
        using var author = await ClientAsync(GlobalRoles.Editor, "cn-private-author");
        using var reader = await ClientAsync(GlobalRoles.Viewer, "cn-private-reader");
        var hidden = await CreateCaveAsync(
            author, $"Closed {tag}", $"Secret alias {tag}", $"HID-{tag}", "private", false);

        foreach (var text in new[] { $"HID-{tag}", $"secret alias {tag}" })
        {
            var answer = await QueryAsync(reader, Names(text));
            answer.Worlds.Single().Hits.ShouldBeEmpty();
            answer.Worlds.Single().Total.ShouldBe(0);
        }

        // The author, who may read it, finds it by either.
        (await HitAsync(author, $"HID-{tag}")).ShouldNotBeNull().Id.ShouldBe(hidden);
        (await HitAsync(author, $"secret alias {tag}")).ShouldNotBeNull().Id.ShouldBe(hidden);
    }

    [Fact]
    public async Task A_protected_cave_is_found_by_the_code_its_page_shows_and_nothing_positional_comes_back()
    {
        using var author = await ClientAsync(GlobalRoles.Editor, "cn-protected-author");
        using var reader = await ClientAsync(GlobalRoles.Viewer, "cn-protected-reader");
        var guarded = await CreateCaveAsync(
            author, $"Guarded {tag}", null, $"GRD-{tag}", "authenticated", true);

        // The code is on the cave's page for this reader, who may read the cave but not place it.
        var page = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{guarded}");
        page.GetProperty("identificationCode").GetString().ShouldBe($"GRD-{tag}");
        page.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var response = await reader.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(Document(Names($"grd-{tag}")), Count: false));
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        var hit = JsonSerializer.Deserialize<FilterQueryResponse>(raw, Wire)!.Worlds.Single().Hits.Single();
        hit.Id.ShouldBe(guarded);
        hit.Placeable.ShouldBeFalse();
        hit.Matched.ShouldNotBeNull().Text.ShouldBe($"GRD-{tag}");

        // Neither the coordinate nor the altitude of its entrance is anywhere in the answer.
        raw.ShouldNotContain("23.4567");
        raw.ShouldNotContain("1234");
    }

    [Fact]
    public async Task A_negated_names_condition_excludes_by_every_name_and_explains_nothing()
    {
        using var author = await ClientAsync(GlobalRoles.Editor, "cn-not-author");
        var aliased = await CreateCaveAsync(author, $"Plain {tag}", $"Alias {tag}", null, "authenticated", false);
        var other = await CreateCaveAsync(author, $"Other {tag}", null, null, "authenticated", false);

        var answer = await QueryAsync(author, new AllOfNode([
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]),
            new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
            new NotNode(Names($"alias {tag}")),
        ]));

        var hits = answer.Worlds.Single().Hits;
        hits.Select(h => h.Id).ShouldBe([other]);
        hits.Single().Matched.ShouldBeNull();
        hits.Select(h => h.Id).ShouldNotContain(aliased);
    }

    [Fact]
    public async Task An_entrance_is_found_by_its_own_name_through_the_names_field()
    {
        using var author = await ClientAsync(GlobalRoles.Editor, "cn-entrance-author");
        var cave = await CreateCaveAsync(author, $"Holder {tag}", $"Nickname {tag}", $"ENT-{tag}", "authenticated", false);

        var answer = await QueryAsync(author, new AllOfNode([
            new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("caveEntrance")]),
            Names($"Holder {tag} entrance"),
        ]));
        var hit = answer.Worlds.Single().Hits.Single();
        hit.Matched.ShouldBeNull();

        // The cave's other name and code are the cave's, not the entrance's.
        (await QueryAsync(author, new AllOfNode([
            new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("caveEntrance")]),
            Names($"ENT-{tag}"),
        ]))).Worlds.Single().Hits.ShouldBeEmpty();
        (await HitAsync(author, $"ENT-{tag}")).ShouldNotBeNull().Id.ShouldBe(cave);
    }

    [Fact]
    public async Task The_vocabulary_offers_the_names_field_as_text()
    {
        using var reader = await ClientAsync(GlobalRoles.Viewer, "cn-vocab");
        var body = await reader.GetFromJsonAsync<FilterVocabularyResponse>("/api/v1/filters/vocabulary", Wire);
        var names = body.ShouldNotBeNull().Worlds.Single(w => w.World == "feature")
            .Fields.Single(f => f.Key == FeatureFilterFields.Names);
        names.Kind.ShouldBe(FieldKind.Text);
        names.Ops.ShouldBe(FilterOps.For(FieldKind.Text));
    }

    [Fact]
    public async Task Anonymous_callers_cannot_ask()
    {
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/filters/query", new FilterQueryRequest(Document(Names("x")))))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- helpers ----------

    private static ConditionNode Names(string text) =>
        new(FeatureFilterFields.Names, FilterOp.Contains, [new TextValue(text)]);

    private static FilterDocument Document(FilterNode where) =>
        new() { Scope = [new WorldScope("feature", where)] };

    /// <summary>What a picker over caves asks: caves whose names contain the text.</summary>
    private async Task<FilterHitDto?> HitAsync(HttpClient client, string text)
    {
        var answer = await QueryAsync(client, new AllOfNode([
            new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
            Names(text),
        ]));
        return answer.Worlds.Single().Hits.SingleOrDefault();
    }

    private static async Task<FilterQueryResponse> QueryAsync(HttpClient client, FilterNode where)
    {
        var response = await client.PostAsJsonAsync("/api/v1/filters/query", new FilterQueryRequest(Document(where)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FilterQueryResponse>(Wire)).ShouldNotBeNull();
    }

    private async Task<Guid> CreateCaveAsync(
        HttpClient author, string name, string? otherToponyms, string? code, string visibility, bool locationProtected)
    {
        long caveTypeId;
        long entranceTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        var created = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            otherToponyms,
            identificationCode = code,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await author.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = $"{name} entrance",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 23.4567891, 46.5432198 } },
            altitude = 1234.5m,
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }

    private async Task<HttpClient> ClientAsync(string role, string prefix)
    {
        var address = $"{prefix}-{tag}@t.local";
        await AuthHelper.CreateUserAsync(factory, role, address);
        return await AuthHelper.BearerClientAsync(factory, address);
    }

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
