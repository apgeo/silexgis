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
/// The feature section of the unified search: the kind filter narrows the question before the hit
/// budget is spent, the budget holds, a cave is found by its code and by its other names, a word
/// match is answered by the search vector where no substring would match, and what is typed is
/// matched literally rather than as a pattern.
/// </summary>
public sealed class SearchEndpointTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];
    private HttpClient editor = null!;
    private long caveTypeId;
    private long sinkholeTypeId;

    public SearchEndpointTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"srch-edi-{marker}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"srch-edi-{marker}@t.local");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
    }

    /// <summary>
    /// Twenty-five sinkholes and one cave share a word. Unnarrowed, the budget of twenty is spent
    /// and the cave may well not be in it; asked for caves, the cave is the answer.
    /// </summary>
    [Fact]
    public async Task The_kind_filter_narrows_before_the_hit_budget_is_spent()
    {
        var word = $"shared{marker}";
        for (var i = 0; i < 25; i++)
        {
            // Named so they sort before the cave: a filter applied after the budget would find no cave.
            await CreateSinkholeAsync($"A{i:D2} {word}");
        }

        var cave = await CreateCaveAsync($"Z cave {word}");

        var everything = await FeaturesAsync(word);
        everything.Count.ShouldBe(20);
        everything.ShouldNotContain(h => h.Id == cave);

        var caves = await FeaturesAsync(word, "cave");
        caves.Select(h => h.Id).ShouldBe([cave]);
        caves.Single().Kind.ShouldBe("cave");

        var invalid = await editor.GetAsync($"/api/v1/search?q={word}&kind=nonsense");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("feature.kind_invalid");
    }

    [Fact]
    public async Task A_cave_is_found_by_its_code_and_by_its_other_names()
    {
        var code = $"RO-{marker}";
        var toponym = $"toponym{marker}";
        var cave = await CreateCaveAsync($"Coded cave {Guid.NewGuid():N}"[..24], code, $"Old name {toponym}");

        (await FeaturesAsync(code)).Select(h => h.Id).ShouldContain(cave);
        (await FeaturesAsync(code[..^2])).Select(h => h.Id).ShouldContain(cave);
        (await FeaturesAsync(toponym)).Select(h => h.Id).ShouldContain(cave);
        (await FeaturesAsync($"absent{marker}")).ShouldBeEmpty();
    }

    /// <summary>
    /// Two words that are not side by side in the name: no substring of the name is the query, so
    /// only the word match can answer it.
    /// </summary>
    [Fact]
    public async Task Words_apart_in_a_name_are_found_by_the_word_match()
    {
        var feature = await CreateSinkholeAsync($"alpha{marker} middle gamma{marker}");
        (await FeaturesAsync($"alpha{marker} gamma{marker}")).Select(h => h.Id).ShouldBe([feature]);
    }

    /// <summary>
    /// What is typed is matched as written: a percent sign is a character, not "anything", and
    /// two underscores match names that hold two underscores rather than every name there is.
    /// </summary>
    [Fact]
    public async Task Wildcard_characters_in_the_query_are_matched_literally()
    {
        var percent = await CreateSinkholeAsync($"Pct {marker}50% sign");
        _ = await CreateSinkholeAsync($"Pct {marker}500 sign");
        (await FeaturesAsync($"{marker}50%")).Select(h => h.Id).ShouldBe([percent]);

        var underscored = await CreateSinkholeAsync($"Under__{marker}");
        _ = await CreateSinkholeAsync($"Plain {marker}");
        var hits = await FeaturesAsync("__");
        hits.Select(h => h.Id).ShouldContain(underscored);
        hits.ShouldAllBe(h => h.Name != null && h.Name.Contains("__"));
    }

    private sealed record Hit(Guid Id, string Kind, string? Name);

    private async Task<List<Hit>> FeaturesAsync(string q, string? kind = null)
    {
        var url = $"/api/v1/search?q={Uri.EscapeDataString(q)}" + (kind is null ? string.Empty : $"&kind={kind}");
        var response = await editor.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return [.. JsonDocument.Parse(body).RootElement.GetProperty("features").EnumerateArray()
            .Select(h => new Hit(h.GetProperty("id").GetGuid(), h.GetProperty("kind").GetString()!, h.GetProperty("name").GetString()))];
    }

    private async Task<Guid> CreateSinkholeAsync(string name)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.45, 45.53 } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name, string? identificationCode = null, string? otherToponyms = null)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            identificationCode,
            otherToponyms,
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        editor?.Dispose();
        factory.Dispose();
    }
}
