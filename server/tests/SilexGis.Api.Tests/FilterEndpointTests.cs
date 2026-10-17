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
using SilexGis.Domain.Features;
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
public sealed class FilterEndpointTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public FilterEndpointTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return ValueTask.CompletedTask;
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
        // moves, nothing appears, and they have their answer. What somebody did on a trip is
        // refused for the same reason and harder: "trips where this person led" assembles a claim
        // about that person's competence out of rows the asker may never open.
        var trips = body.Worlds.Single(w => w.World == "tripLog");
        foreach (var forbidden in new[]
        {
            "cave", "caveId", "participant", "participantId", "caver",
            "role", "participantRole", "participantRoleId",
        })
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

    // ---------- a feature type's own typed properties ----------

    private static string DepthField => FeaturePropertyFields.FieldKey(FeatureTypeSeeds.Sinkhole, "depth_m");

    private async Task<long> SinkholeTypeIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.Sinkhole).Select(t => t.Id).SingleAsync();
    }

    private static FilterNode SinkholesNamed(long typeId, string text, params FilterNode[] more) =>
        new AllOfNode([
            new ConditionNode(FeatureFilterFields.TypeId, FilterOp.Equals, [new IdValue(typeId.ToString())]),
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(text)]),
            .. more]);

    [Fact]
    public async Task The_vocabulary_offers_each_typed_property_titled_by_its_schema_and_tied_to_its_type()
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-props");
        var sinkhole = await SinkholeTypeIdAsync();

        var body = await client.GetFromJsonAsync<FilterVocabularyResponse>(
            "/api/v1/filters/vocabulary", Wire);

        var depth = body!.Worlds.Single(w => w.World == "feature").Fields.Single(f => f.Key == DepthField);
        depth.Kind.ShouldBe(FieldKind.Number);
        depth.Label.ShouldBe("Depth (m)");
        depth.LabelKey.ShouldBe(FeaturePropertyFields.LabelKey);
        depth.Ops.ShouldContain(FilterOp.GreaterOrEqual);
        depth.Requires.ShouldBe(new FilterFieldRequirementDto(FeatureFilterFields.TypeId, sinkhole.ToString()));

        // The fields every installation has carry neither: they are named by translation alone.
        var name = body.Worlds.Single(w => w.World == "feature").Fields.Single(f => f.Key == FeatureFilterFields.Name);
        name.Label.ShouldBeNull();
        name.Requires.ShouldBeNull();
    }

    [Fact]
    public async Task A_typed_property_narrows_by_number_and_counts_only_what_the_caller_may_see()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        var sinkhole = await SinkholeTypeIdAsync();
        await SeedSinkholesAsync(ownerId, sinkhole, Visibility.Public,
            """{"depth_m":2}""", """{"depth_m":4.5}""", """{"depth_m":30}""", """{"depth_m":"3"}""");
        // Shallow enough to match, and private to its owner: the outsider must not count it.
        await SeedSinkholesAsync(ownerId, sinkhole, Visibility.Private, """{"depth_m":1}""");

        var atMost = new ConditionNode(DepthField, FilterOp.LessOrEqual, [new NumberValue(4.5)]);

        using var outsider = await ClientAsync(GlobalRoles.Viewer, "fe-prop-out");
        var seen = await QueryAsync(outsider, About("feature", SinkholesNamed(sinkhole, tag, atMost)));
        seen.Worlds[0].Total.ShouldBe(2);

        // The private row does match: its owner, asking the same question, gets it counted.
        using var owner = await ClientAsync(GlobalRoles.Editor, "fe-prop-own", OwnerEmail);
        var own = await QueryAsync(owner, About("feature", SinkholesNamed(sinkhole, tag, atMost)));
        own.Worlds[0].Total.ShouldBe(3);
    }

    [Fact]
    public async Task A_typed_property_asked_without_its_type_is_refused_like_a_field_that_does_not_exist()
    {
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-prop-alone");
        var sinkhole = await SinkholeTypeIdAsync();

        async Task<string> RefusalAsync(FilterNode where)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/filters/query", new FilterQueryRequest(About("feature", where)), Wire);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            problem.GetProperty("code").GetString().ShouldBe("filter.invalid");
            return problem.GetProperty("detail").GetString()!;
        }

        var alone = await RefusalAsync(new ConditionNode(DepthField, FilterOp.LessThan, [new NumberValue(3)]));
        var invented = await RefusalAsync(new AllOfNode([
            new ConditionNode(FeatureFilterFields.TypeId, FilterOp.Equals, [new IdValue(sinkhole.ToString())]),
            new ConditionNode(
                FeaturePropertyFields.FieldKey(FeatureTypeSeeds.Sinkhole, "nothing_here"),
                FilterOp.LessThan,
                [new NumberValue(3)]),
        ]));

        alone.Replace("depth_m", "X").ShouldBe(invented.Replace("nothing_here", "X"));

        // Beside its type the same condition is an ordinary question.
        (await client.PostAsJsonAsync("/api/v1/filters/query", new FilterQueryRequest(About("feature",
            SinkholesNamed(sinkhole, tag, new ConditionNode(DepthField, FilterOp.LessThan, [new NumberValue(3)])))), Wire))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_dated_property_is_offered_as_an_instant_and_a_value_that_is_not_a_date_simply_does_not_match()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        var code = $"dated{tag}";
        long typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var type = new FeatureType
            {
                Code = code,
                Name = "Dated",
                PropertiesSchema = """
                    {"type":"object","properties":{
                      "surveyed_on":{"type":"string","format":"date","title":"Surveyed on"},
                      "seen_at":{"type":"string","format":"date-time"}
                    }}
                    """,
            };
            db.FeatureTypes.Add(type);
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        // Dates either side of the bound, one on it, one a date-time, and every way a stored value
        // can fail to be a date: a calendar date that does not exist, words, a number, a time that
        // names no offset, nothing at all. None of those may fail the question.
        await SeedSinkholesAsync(ownerId, typeId, Visibility.Public,
            """{"surveyed_on":"2025-05-31"}""",
            """{"surveyed_on":"2025-06-01"}""",
            """{"surveyed_on":"2025-06-02T08:00:00Z"}""",
            """{"surveyed_on":"2025-02-30"}""",
            """{"surveyed_on":"last spring"}""",
            """{"surveyed_on":20250601}""",
            """{"surveyed_on":"2025-07-01T08:00:00"}""",
            """{}""");

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-prop-dated");
        var vocabulary = await client.GetFromJsonAsync<FilterVocabularyResponse>(
            "/api/v1/filters/vocabulary", Wire);
        var fields = vocabulary!.Worlds.Single(w => w.World == "feature").Fields;
        var field = FeaturePropertyFields.FieldKey(code, "surveyed_on");
        var surveyed = fields.Single(f => f.Key == field);
        surveyed.Kind.ShouldBe(FieldKind.Instant);
        surveyed.Ops.ShouldBe(FilterOps.For(FieldKind.Instant));
        fields.Single(f => f.Key == FeaturePropertyFields.FieldKey(code, "seen_at")).Kind
            .ShouldBe(FieldKind.Instant);

        var juneFirst = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

        async Task<int?> CountAsync(FilterOp op, params DateTimeOffset[] values)
        {
            var body = await QueryAsync(client, About("feature", SinkholesNamed(typeId, tag,
                new ConditionNode(field, op, [.. values.Select(v => (FilterValue)new InstantValue(v))]))));
            return body.Worlds[0].Total;
        }

        (await CountAsync(FilterOp.GreaterOrEqual, juneFirst)).ShouldBe(2);
        (await CountAsync(FilterOp.LessThan, juneFirst)).ShouldBe(1);
        // A bound written in another zone means the same moment.
        (await CountAsync(FilterOp.LessOrEqual, new DateTimeOffset(2025, 6, 1, 23, 59, 0, TimeSpan.FromHours(3))))
            .ShouldBe(2);
        (await CountAsync(FilterOp.Between, juneFirst, juneFirst.AddDays(1).AddSeconds(-1))).ShouldBe(1);
        (await CountAsync(FilterOp.GreaterThan, juneFirst.AddDays(1))).ShouldBe(1);
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

    [Fact]
    public async Task A_document_with_a_member_missing_is_refused_as_unreadable_rather_than_failing()
    {
        // Each body names its document properly and leaves one member out of it, which model
        // binding fills with null; the validator used to dereference that and answer with a fault.
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-holed");

        string[] bodies =
        [
            """{"document":{"version":1,"scope":[{"world":"feature","where":{"node":"allOf"}}]}}""",
            """{"document":{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name","op":"contains"}}]}}""",
            """{"document":{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":null}]}}]}}""",
            """{"document":{"version":1,"scope":null}}""",
        ];

        foreach (var body in bodies)
        {
            var response = await client.PostAsync(
                "/api/v1/filters/query",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
            (await response.Content.ReadAsStringAsync()).ShouldContain("filter.malformed", Case.Sensitive, body);
        }
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
    public async Task A_trip_is_found_by_where_its_write_up_has_got_to_and_a_draft_is_not_hidden_by_it()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedTripsAsync(ownerId, Visibility.Public, 2, ActivityState.Draft);
        await SeedTripsAsync(ownerId, Visibility.Public, 3, ActivityState.Published);

        // Somebody other than the owner, on the plainest role there is: whatever comes back comes
        // back because the trips are public, not because this caller wrote them.
        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-state");

        var drafts = await QueryAsync(client, About("tripLog", new AllOfNode(
        [
            new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]),
            new ConditionNode(TripLogFilterFields.State, FilterOp.Equals,
                [new IdValue(nameof(ActivityState.Draft))]),
        ])));

        // A draft is unannounced, not hidden: a stranger asking for public trips gets it, and the
        // state is a way of narrowing what came back rather than a rule about who may read it.
        drafts.Worlds[0].Total.ShouldBe(2);

        var announced = await QueryAsync(client, About("tripLog", new AllOfNode(
        [
            new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]),
            new ConditionNode(TripLogFilterFields.State, FilterOp.In,
                [new IdValue(nameof(ActivityState.Published)), new IdValue(nameof(ActivityState.Cancelled))]),
        ])));

        announced.Worlds[0].Total.ShouldBe(3);

        // And unnarrowed, every one of them — so the two answers above are a filter narrowing the
        // set, not the visibility walk quietly withholding half of it.
        var all = await QueryAsync(client, About("tripLog",
            new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)])));

        all.Worlds[0].Total.ShouldBe(5);
    }

    /// <summary>
    /// Trips are found by whether something went wrong on them, and the fact is disclosed to
    /// whoever may read the trip rather than to whoever may edit it.
    /// </summary>
    /// <remarks>
    /// The caller here is a stranger on the plainest role there is, reading public trips they did
    /// not write. That is the whole point of admitting the field: unlike "which trips visited cave
    /// X", the answer is assembled entirely out of rows this caller already reads, so it discloses
    /// nothing a plain listing would not. What went wrong is a different question with a narrower
    /// audience and is no part of this vocabulary.
    /// </remarks>
    [Fact]
    public async Task A_trip_is_found_by_whether_something_went_wrong_on_it()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        await SeedTripsAsync(ownerId, Visibility.Public, 2, ActivityState.Published, hadIncident: true);
        await SeedTripsAsync(ownerId, Visibility.Public, 4, ActivityState.Published);

        using var client = await ClientAsync(GlobalRoles.Viewer, "fe-incident");

        var mine = new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);

        var wentWrong = await QueryAsync(client, About("tripLog", new AllOfNode(
        [
            mine,
            new ConditionNode(TripLogFilterFields.HadIncident, FilterOp.Equals, [new BooleanValue(true)]),
        ])));
        wentWrong.Worlds[0].Total.ShouldBe(2);

        // The other side of the same question, asked explicitly: a trip that records no incident
        // is not merely absent from the first answer, it is present in this one. Both halves
        // matter, because a leaf that matched nothing at all would pass the first assertion.
        var wentFine = await QueryAsync(client, About("tripLog", new AllOfNode(
        [
            mine,
            new ConditionNode(TripLogFilterFields.HadIncident, FilterOp.Equals, [new BooleanValue(false)]),
        ])));
        wentFine.Worlds[0].Total.ShouldBe(4);

        var all = await QueryAsync(client, About("tripLog", mine));
        all.Worlds[0].Total.ShouldBe(6);
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

    // ---------- the context line under a hit ----------

    [Fact]
    public async Task A_hits_context_line_names_a_cave_only_to_a_reader_who_may_read_it_and_never_a_position()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        using var author = await AuthHelper.BearerClientAsync(factory, OwnerEmail);
        using var reader = await ClientAsync(GlobalRoles.Viewer, "fe-context");

        var openRegion = $"Open region {tag}";
        var hiddenRegion = $"Hidden region {tag}";
        var hiddenName = $"Hidden cave {tag}";
        var (protectedCave, protectedEntrance) = await CreateCaveAsync(
            author, $"Guarded cave {tag}", openRegion, "authenticated", locationProtected: true);
        var (_, strandedEntrance) = await CreateCaveAsync(
            author, hiddenName, hiddenRegion, "private", locationProtected: true);

        // The entrance is readable on its own visibility while its cave stays private. The walk
        // admits a row by any ancestor's visibility, its own included, so this is a state the
        // reader can genuinely be in: the entrance is theirs to see and its cave is not.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == strandedEntrance)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.Visibility, Visibility.Authenticated));
        }

        var hits = (await QueryAsync(reader, About("feature", Named(tag)), count: false))
            .Worlds.Single().Hits.ToDictionary(h => h.Id);

        // A protected cave still names its region — the registry shows that to every reader of
        // the cave — and its entrance names the cave it belongs to and that region.
        hits[protectedCave].Subtitle.ShouldNotBeNull().ShouldContain(openRegion);
        hits[protectedEntrance].Subtitle.ShouldNotBeNull().ShouldContain($"Guarded cave {tag}");
        hits[protectedEntrance].Subtitle.ShouldNotBeNull().ShouldContain(openRegion);

        // The stranded entrance is listed, and says what it is, and nothing about its cave.
        var stranded = hits[strandedEntrance].Subtitle;
        stranded.ShouldNotBeNullOrWhiteSpace();
        stranded.ShouldNotContain(hiddenName);
        stranded.ShouldNotContain(hiddenRegion);

        // No line says anything positional: not the altitude, not a coordinate.
        foreach (var hit in hits.Values)
        {
            (hit.Subtitle ?? string.Empty).ShouldNotContain("1234");
            (hit.Subtitle ?? string.Empty).ShouldNotContain("23.4567");
        }

        // The map's search box writes the same line from the same rule.
        var search = await reader.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/search?q={Uri.EscapeDataString(tag)}");
        var searched = search.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid(), f => f.GetProperty("subtitle").GetString());
        searched[strandedEntrance].ShouldBe(stranded);
        searched[protectedEntrance].ShouldBe(hits[protectedEntrance].Subtitle);

        // And the same entrance, asked by somebody who may read the cave, names it.
        var owned = (await QueryAsync(author, About("feature", Named(tag)), count: false))
            .Worlds.Single().Hits.Single(h => h.Id == strandedEntrance);
        owned.Subtitle.ShouldNotBeNull().ShouldContain(hiddenName);
        owned.Subtitle.ShouldNotBeNull().ShouldContain(hiddenRegion);
    }

    private async Task<(Guid Cave, Guid Entrance)> CreateCaveAsync(
        HttpClient author, string name, string region, string visibility, bool locationProtected)
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
            region,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var caveId = System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var entrance = await author.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = $"{name} entrance",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 23.4567891, 46.5432198 } },
            altitude = 1234.5m,
            positionQuality = "Gps",
        });
        var entrancePayload = await entrance.Content.ReadAsStringAsync();
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, entrancePayload);
        var entranceId = System.Text.Json.JsonDocument.Parse(entrancePayload).RootElement.GetProperty("id").GetGuid();
        return (caveId, entranceId);
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
            // The row's ancestor array and the closure table are two places the write service keeps
            // in step; a fixture that sets only the array leaves the rows looking corrupt to the
            // integrity check, which scans the whole database.
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task SeedSinkholesAsync(Guid ownerId, long typeId, Visibility visibility, params string[] properties)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        foreach (var (document, i) in properties.Select((p, i) => (p, i)))
        {
            var id = Guid.NewGuid();
            db.Features.Add(new Feature
            {
                Id = id,
                Name = $"Sinkhole {visibility} {i} {tag}",
                Kind = FeatureKind.Generic,
                FeatureTypeId = typeId,
                OwnerUserId = ownerId,
                Visibility = visibility,
                AncestorIds = [id],
                Properties = document,
            });
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedTripsAsync(
        Guid ownerId,
        Visibility visibility,
        int count,
        ActivityState state = ActivityState.Draft,
        bool hadIncident = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        for (var i = 0; i < count; i++)
        {
            db.TripLogs.Add(new TripLog
            {
                Title = $"Trip {i} {state} {hadIncident} {tag}",
                TripDate = new DateOnly(2026, 5, 3),
                OwnerUserId = ownerId,
                Visibility = visibility,
                State = state,
                HadIncident = hadIncident,
            });
        }

        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
