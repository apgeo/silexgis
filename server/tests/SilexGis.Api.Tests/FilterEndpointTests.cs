// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The one route everything asks, over real HTTP.
/// </summary>
/// <remarks>
/// The conformance suite proves each world in isolation. This proves the route around them: that a
/// question is refused before it reaches a world when it names something that does not exist, that
/// what comes back is what the caller may see, and that neither the rows nor the count nor a
/// refusal's wording says anything about rows they may not.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class FilterEndpointTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public FilterEndpointTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return Task.CompletedTask;
    }

    /// <summary>
    /// The wire's own conventions, so a response is read here the way a browser reads it.
    /// </summary>
    /// <remarks>
    /// The application writes enum members as camel-cased names rather than numbers, which is what
    /// keeps a saved filter readable and its meaning stable when members are renumbered. A test
    /// reading with the defaults would be testing a contract nothing else uses.
    /// </remarks>
    private static readonly System.Text.Json.JsonSerializerOptions Wire = Build();

    private static System.Text.Json.JsonSerializerOptions Build()
    {
        var options = new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.CamelCase));
        return options;
    }

    private static FilterDocument About(string world, FilterNode? where = null) =>
        new() { Scope = [new WorldScope(world, where)] };

    private static FilterNode Named(string text) =>
        new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(text)]);

    // ---------- what the route offers ----------

    [Fact]
    public async Task Anonymous_callers_are_turned_away()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/filters/vocabulary")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/filters/query",
            new FilterQueryRequest(About("feature")))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_vocabulary_says_what_can_be_asked_and_how_large_a_filter_may_be()
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-vocab");

        var body = await client.GetFromJsonAsync<FilterVocabularyResponse>(
            "/api/v1/filters/vocabulary", Wire);

        body.ShouldNotBeNull();
        var features = body.Worlds.Single(w => w.World == "feature");
        features.Fields.Select(f => f.Key).ShouldContain(FeatureFilterFields.Name);

        // Every registered world describes itself here, so a builder needs no list of its own.
        body.Worlds.Select(w => w.World).ShouldContain("tripLog");

        // What a trip cannot be asked is the load-bearing part: which caves it visited, and who was
        // on it. Either would answer a question about a row the caller may not read — the count
        // moves, nothing appears, and they have their answer.
        var trips = body.Worlds.Single(w => w.World == "tripLog");
        foreach (var forbidden in new[] { "cave", "caveId", "participant", "participantId", "caver" })
        {
            trips.Fields.Select(f => f.Key).ShouldNotContain(forbidden);
        }

        // Published so a builder can stop somebody before they send something it will refuse. The
        // server checks them again regardless — this copy is a courtesy, never the enforcement.
        body.Limits.MaxNodes.ShouldBe(FilterValidation.MaxNodes);
        body.Limits.MaxDepth.ShouldBe(FilterValidation.MaxDepth);

        // Nothing offers a sort or a field it cannot serve, which is what stops a person saving a
        // filter that quietly returns nothing and looks like a filter with no matches.
        features.Sorts.ShouldNotContain(SortKey.Proximity);
        features.Fields.Select(f => f.Kind).ShouldNotContain(FieldKind.Spatial);

        // Each field carries the operators it admits, so the builder never keeps its own copy of
        // that table and cannot drift into offering something the server refuses.
        var name = features.Fields.Single(f => f.Key == FeatureFilterFields.Name);
        name.Ops.ShouldBe(FilterOps.For(FieldKind.Text));
    }

    // ---------- and what it refuses ----------

    [Theory]
    [InlineData("no-such-world", null)]
    [InlineData("feature", "notAField")]
    public async Task A_question_about_something_that_does_not_exist_is_refused_before_it_runs(
        string world, string? field)
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-refuse");

        FilterNode? where = field is null
            ? null
            : new ConditionNode(field, FilterOp.Contains, [new TextValue("x")]);

        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(About(world, where)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.invalid");
    }

    [Fact]
    public async Task An_empty_scope_is_refused_rather_than_answered_as_everything()
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-empty");

        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(new FilterDocument()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_document_this_version_cannot_read_is_refused_rather_than_crashing()
    {
        // A value that does not say which type it is. The reader raises NotSupportedException for
        // this rather than a JsonException, which is a different path through model binding — and a
        // filter arriving from an older client, or a link somebody edited, must be a refusal rather
        // than a fault the caller can do nothing about.
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-malformed");

        const string body = """
            {"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name",
            "op":"contains","values":[{"kind":"text","value":"urs"}]}}],
            "sort":"updated","descending":true}
            """;

        var response = await client.PostAsync(
            "/api/v1/filters/query",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        ((int)response.StatusCode).ShouldBeLessThan(500,
            "A filter this version cannot read is the caller's mistake, not a fault.");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- what comes back ----------

    [Fact]
    public async Task A_filter_returns_the_callers_own_rows_and_counts_them()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedAsync(ownerId, Visibility.Private, 3);

        using var client = await ClientAsync(GlobalRoles.Editor, "fe-own", OwnerEmail);
        var body = await QueryAsync(client, About("feature", Named(tag)));

        var world = body.Worlds.ShouldHaveSingleItem();
        world.World.ShouldBe("feature");
        world.Hits.Count.ShouldBe(3);
        world.Hits.ShouldAllBe(h => h.Title.Contains(tag));

        // One world, so it was worth counting.
        body.Counted.ShouldBeTrue();
        world.Total.ShouldBe(3);
    }

    [Fact]
    public async Task Somebody_elses_private_rows_are_neither_returned_nor_counted()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedAsync(ownerId, Visibility.Private, 3);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-outsider");
        var body = await QueryAsync(client, About("feature", Named(tag)));

        var world = body.Worlds.ShouldHaveSingleItem();
        world.Hits.ShouldBeEmpty();

        // The count is the half that is easy to leave behind. A total taken before the visibility
        // walk would report the rows without listing them — a disclosure with nothing on screen.
        world.Total.ShouldBe(0);
    }

    [Fact]
    public async Task A_hit_says_whether_it_can_be_placed_and_never_where()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedAsync(ownerId, Visibility.Public, 1);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-place");
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(About("feature", Named(tag))));
        var raw = await response.Content.ReadAsStringAsync();

        raw.ShouldContain("placeable");

        // Asserted against the wire rather than the object, because the rule is about what leaves
        // the process. Whoever adds a coordinate to a hit will add it to the record first.
        foreach (var word in new[] { "longitude", "latitude", "geom", "coordinates", "wkt" })
        {
            raw.ShouldNotContain(word, Case.Insensitive);
        }
    }

    [Fact]
    public async Task A_filter_over_two_worlds_answers_for_both_and_still_counts()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedAsync(ownerId, Visibility.Public, 2);
        await SeedTripsAsync(ownerId, Visibility.Public, 3);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-two");
        var body = await QueryAsync(client, new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", Named(tag)),
                new WorldScope("tripLog",
                    new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)])),
            ],
        });

        // In the order the document named them, which is the order somebody reads them in.
        body.Worlds.Select(w => w.World).ShouldBe(["feature", "tripLog"]);
        body.Worlds[0].Total.ShouldBe(2);
        body.Worlds[1].Total.ShouldBe(3);

        // Two is where a person is still comparing "how many of these against how many of those",
        // so the totals are still worth the pass they cost. Whether a wider scope stops counting
        // cannot be driven from here until a third world is registered.
        body.Counted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_request_may_ask_not_to_be_counted_and_may_never_ask_to_be()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedAsync(ownerId, Visibility.Public, 2);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-count");

        // A type-ahead asks for rows, not for a population statistic about what somebody typed.
        var quiet = await QueryAsync(client, About("feature", Named(tag)), count: false);
        quiet.Counted.ShouldBeFalse();
        quiet.Worlds.ShouldAllBe(w => w.Total == null);
        quiet.Worlds[0].Hits.Count.ShouldBe(2);

        var counted = await QueryAsync(client, About("feature", Named(tag)));
        counted.Counted.ShouldBeTrue();
        counted.Worlds[0].Total.ShouldBe(2);
    }

    [Fact]
    public async Task A_world_offers_only_sorts_it_can_actually_deliver()
    {
        // A sort a world declares but substitutes is worse than one it does not offer: the list
        // comes back in an order nobody chose and nothing on screen says so.
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-sorts");
        var body = await client.GetFromJsonAsync<FilterVocabularyResponse>(
            "/api/v1/filters/vocabulary", Wire);

        body.ShouldNotBeNull();

        // A trip is found by when it happened; a feature has no such date and does not pretend to.
        body.Worlds.Single(w => w.World == "tripLog").Sorts.ShouldContain(SortKey.Occurred);
        body.Worlds.Single(w => w.World == "feature").Sorts.ShouldNotContain(SortKey.Occurred);
    }

    // ---------- describing a choice somebody already made ----------

    [Fact]
    public async Task Resolving_ids_describes_the_ones_the_caller_may_see_and_omits_the_rest()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        var mine = await SeedAsync(ownerId, Visibility.Public, 1);
        var theirs = await SeedAsync(ownerId, Visibility.Private, 1);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-resolve");
        var response = await client.PostAsJsonAsync("/api/v1/filters/resolve",
            new FilterResolveRequest("feature", [.. mine, .. theirs, Guid.NewGuid()]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hits = await response.Content.ReadFromJsonAsync<List<FilterHitDto>>(Wire);
        hits.ShouldNotBeNull();

        // The private row and the invented one are both simply absent. Indistinguishable on
        // purpose: a refusal for one and silence for the other would confirm the first exists.
        hits.Select(h => h.Id).ShouldBe(mine);
    }

    [Fact]
    public async Task Resolving_refuses_a_list_longer_than_a_condition_may_carry()
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-many");

        var ids = Enumerable.Range(0, FilterValidation.MaxValuesPerCondition + 1)
            .Select(_ => Guid.NewGuid()).ToList();

        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/resolve", new FilterResolveRequest("feature", ids));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("filter.too_many_ids");
    }

    // ---------- helpers ----------

    private string OwnerEmail => $"fe-owner-{tag}@t.local";

    private async Task<HttpClient> ClientAsync(string role, string prefix, string? email = null)
    {
        var address = email ?? $"{prefix}-{tag}@t.local";
        if (email is null)
        {
            await AuthHelper.CreateUserAsync(factory, role, address);
        }

        return await AuthHelper.BearerClientAsync(factory, address);
    }

    private static async Task<FilterQueryResponse> QueryAsync(
        HttpClient client, FilterDocument document, bool count = true)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(document, Count: count));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadFromJsonAsync<FilterQueryResponse>(Wire);
        body.ShouldNotBeNull();
        return body;
    }

    private async Task<List<Guid>> SeedAsync(Guid ownerId, Visibility visibility, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();

        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.Features.Add(new Feature
            {
                Id = id,
                Name = $"Row {i} {tag}",
                Kind = FeatureKind.Generic,
                FeatureTypeId = typeId,
                OwnerUserId = ownerId,
                Visibility = visibility,
                // A feature is visible through its ancestor chain, and a rootless one's chain is
                // itself — a row inserted without it is invisible to everybody but its owner.
                AncestorIds = [id],
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task SeedTripsAsync(Guid ownerId, Visibility visibility, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        for (var i = 0; i < count; i++)
        {
            db.TripLogs.Add(new TripLog
            {
                Title = $"Trip {i} {tag}",
                TripDate = new DateOnly(2026, 5, 3),
                OwnerUserId = ownerId,
                Visibility = visibility,
            });
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
