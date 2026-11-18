// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.CavePotential;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Cave-potential models as objects of their own: written, read, shared with a caving group,
/// copied, changed and removed.
/// </summary>
/// <remarks>
/// The people are a Viewer author with a clubmate and an outsider, all Viewers on purpose: the
/// seeded Editors group reads past visibility at the widest scope, so a test that an Editor cannot
/// see a model would prove nothing about the audience column.
/// </remarks>
public sealed class CavePotentialModelTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Url = "/api/v1/cave-potential/models";
    private const string LayersUrl = "/api/v1/cave-potential/evidence-layers";

    private readonly SilexGisApiFactory factory;
    private string tag = null!;
    private Guid authorId;
    private Guid clubmateId;
    private Guid outsiderId;
    private Guid cavingGroupId;
    private HttpClient author = null!;
    private HttpClient clubmate = null!;
    private HttpClient outsider = null!;

    public CavePotentialModelTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        authorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cp-author-{tag}@t.local");
        clubmateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cp-club-{tag}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cp-out-{tag}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Potential Club {tag}", Slug = $"potential-club-{tag}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, authorId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
        }

        author = await AuthHelper.BearerClientAsync(factory, $"cp-author-{tag}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"cp-club-{tag}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"cp-out-{tag}@t.local");
    }

    // ---------- the routes are for signed-in callers ----------

    [Fact]
    public async Task An_anonymous_caller_is_turned_away_from_every_route()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync(LayersUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Url, Direct("x"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"{Url}/{id}", Direct("x"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Url}/{id}/clone", new { name = "x" })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Url}/weighting", new { method = "directWeights", definition = TerrainOnly() }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- the vocabulary ----------

    [Fact]
    public async Task The_evidence_layers_say_how_each_may_be_scored_and_which_are_made_from_positions()
    {
        var layers = (await outsider.GetFromJsonAsync<JsonElement>(LayersUrl)).EnumerateArray().ToList();

        layers.Count.ShouldBe(11);
        var slope = layers.Single(l => l.GetProperty("code").GetString() == "slope");
        slope.GetProperty("valueKind").GetString().ShouldBe("continuous");
        slope.GetProperty("usesPositions").GetBoolean().ShouldBeFalse();
        slope.GetProperty("transforms").EnumerateArray().Select(t => t.GetString())
            .ShouldBe(["classes", "fuzzyLinear"]);

        var aspect = layers.Single(l => l.GetProperty("code").GetString() == "aspect");
        aspect.GetProperty("transforms").EnumerateArray().Select(t => t.GetString()).ShouldBe(["classes"]);

        var rock = layers.Single(l => l.GetProperty("code").GetString() == "lithology");
        rock.GetProperty("source").GetString().ShouldBe("features");
        rock.GetProperty("usesPositions").GetBoolean().ShouldBeTrue();
        rock.GetProperty("transforms").EnumerateArray().Select(t => t.GetString()).ShouldBe(["categories"]);
        rock.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldContain("limestone");

        layers.Where(l => l.GetProperty("usesPositions").GetBoolean())
            .Select(l => l.GetProperty("code").GetString())
            .ShouldBe(["fault_distance", "lithology", "sinkhole_distance", "entrance_density"]);
    }

    // ---------- writing one ----------

    [Fact]
    public async Task A_model_is_saved_read_back_and_listed_as_its_authors_with_its_weights()
    {
        var created = await CreateAsync(author, Direct($"Plateau {tag}", description: "  first try  "));

        created.GetProperty("mine").GetBoolean().ShouldBeTrue();
        created.GetProperty("mayEdit").GetBoolean().ShouldBeTrue();
        created.GetProperty("mayDelete").GetBoolean().ShouldBeTrue();
        created.GetProperty("description").GetString().ShouldBe("first try");
        created.GetProperty("visibility").GetString().ShouldBe("private");
        created.GetProperty("method").GetString().ShouldBe("directWeights");
        created.GetProperty("usesPositions").GetBoolean().ShouldBeFalse();
        var id = created.GetProperty("id").GetGuid();

        var read = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        read.GetProperty("name").GetString().ShouldBe($"Plateau {tag}");
        var layers = read.GetProperty("definition").GetProperty("layers");
        layers.GetArrayLength().ShouldBe(2);
        layers[0].GetProperty("layer").GetString().ShouldBe("slope");
        layers[0].GetProperty("transform").GetProperty("kind").GetString().ShouldBe("classes");
        layers[1].GetProperty("transform").GetProperty("low").GetDouble().ShouldBe(200);

        // Weights of 3 and 1, as shares of one; given outright, so nothing to be consistent about.
        var weighting = read.GetProperty("weighting");
        weighting.GetProperty("weights")[0].GetProperty("layer").GetString().ShouldBe("slope");
        weighting.GetProperty("weights")[0].GetProperty("weight").GetDouble().ShouldBe(0.75, 1e-12);
        weighting.GetProperty("weights")[1].GetProperty("weight").GetDouble().ShouldBe(0.25, 1e-12);
        weighting.GetProperty("consistency").ValueKind.ShouldBe(JsonValueKind.Null);

        (await ListedIdsAsync(author)).ShouldContain(id);
    }

    [Fact]
    public async Task Only_the_definitions_own_members_are_stored()
    {
        // A place written beside the definition's members never reaches the row: a model shared
        // with a club must not be a way of handing its members a position.
        var body = """
            {"name":"Carried","method":"directWeights","visibility":"private",
             "definition":{"area":{"lon":22.61873,"lat":46.58219},"layers":[
               {"layer":"slope","weight":1,"note":"here",
                "transform":{"kind":"fuzzyLinear","low":0,"high":30}}]}}
            """;

        var response = await author.PostAsync(
            Url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.CavePotentialModels.AsNoTracking().SingleAsync(m => m.Id == id);
        stored.Definition.ShouldContain("fuzzyLinear");
        stored.Definition.ShouldNotContain("22.61873");
        stored.Definition.ShouldNotContain("area");
        stored.Definition.ShouldNotContain("note");
    }

    [Fact]
    public async Task Weights_from_comparisons_are_worked_out_with_their_consistency()
    {
        var created = await CreateAsync(author, Pairwise($"Compared {tag}", [[3, 5], [3]]));

        created.GetProperty("method").GetString().ShouldBe("pairwiseComparison");
        // Rock and the distance to faults are both read from recorded features.
        created.GetProperty("usesPositions").GetBoolean().ShouldBeTrue();
        var weighting = created.GetProperty("weighting");
        var weights = weighting.GetProperty("weights").EnumerateArray()
            .Select(w => w.GetProperty("weight").GetDouble()).ToList();
        weights.ShouldBe([0.6370, 0.2583, 0.1047], 0.0001);
        var consistency = weighting.GetProperty("consistency");
        consistency.GetProperty("principalEigenvalue").GetDouble().ShouldBe(3.0385, 0.0001);
        consistency.GetProperty("randomIndex").GetDouble().ShouldBe(0.58);
        consistency.GetProperty("ratio").GetDouble().ShouldBe(0.0332, 0.0001);
        consistency.GetProperty("acceptable").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Inconsistent_comparisons_are_stored_and_flagged_not_refused()
    {
        // Rock far outweighs slope, slope far outweighs faults, and yet faults outweigh rock.
        var created = await CreateAsync(author, Pairwise($"Contradictory {tag}", [[9, 1d / 9], [9]]));

        var consistency = created.GetProperty("weighting").GetProperty("consistency");
        consistency.GetProperty("ratio").GetDouble().ShouldBeGreaterThan(0.1);
        consistency.GetProperty("acceptable").GetBoolean().ShouldBeFalse();
        (await author.GetAsync($"{Url}/{created.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public static TheoryData<string, string> BrokenDefinitions => new()
    {
        // no layers at all
        { """{"layers":[]}""", "definition.layers" },
        // a layer nobody ships
        { """{"layers":[{"layer":"altitude","weight":1,"transform":{"kind":"fuzzyLinear","low":0,"high":1}}]}""", "definition.layers[0].layer" },
        // a ramp over a compass bearing
        { """{"layers":[{"layer":"aspect","weight":1,"transform":{"kind":"fuzzyLinear","low":0,"high":360}}]}""", "definition.layers[0].transform.kind" },
        // a ramp with one bound
        { """{"layers":[{"layer":"slope","weight":1,"transform":{"kind":"fuzzyLinear","low":5,"high":5}}]}""", "definition.layers[0].transform.high" },
        // a table whose bounds fall
        { """{"layers":[{"layer":"slope","weight":1,"transform":{"kind":"classes","classes":[{"upperBound":20,"score":0.1},{"upperBound":5,"score":1},{"score":0.3}]}}]}""", "definition.layers[0].transform.classes[1].upperBound" },
        // a score above one
        { """{"layers":[{"layer":"slope","weight":1,"transform":{"kind":"classes","classes":[{"upperBound":20,"score":3},{"score":0.3}]}}]}""", "definition.layers[0].transform.classes[0].score" },
        // a rock outside the vocabulary
        { """{"layers":[{"layer":"lithology","weight":1,"transform":{"kind":"categories","categories":[{"code":"chalk","score":1}]}}]}""", "definition.layers[0].transform.categories[0].code" },
        // no weight where weights are given outright
        { """{"layers":[{"layer":"slope","transform":{"kind":"fuzzyLinear","low":0,"high":30}}]}""", "definition.layers[0].weight" },
    };

    [Theory]
    [MemberData(nameof(BrokenDefinitions))]
    public async Task A_definition_that_breaks_a_rule_is_refused_naming_the_member_at_fault(string definition, string member)
    {
        var before = await CountMineAsync();
        var body = $$"""{"name":"Broken","method":"directWeights","visibility":"private","definition":{{definition}}}""";

        var response = await author.PostAsync(
            Url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldContain(member);
        (await CountMineAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Comparisons_of_the_wrong_shape_and_a_missing_definition_are_refused()
    {
        var before = await CountMineAsync();

        var lopsided = await author.PostAsJsonAsync(Url, Pairwise("x", [[3, 5]]));
        lopsided.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await lopsided.Content.ReadAsStringAsync());
        (await lopsided.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")
            .EnumerateObject().Select(e => e.Name).ShouldContain("definition.pairwise");

        var offScale = await author.PostAsJsonAsync(Url, Pairwise("x", [[3, 12], [3]]));
        offScale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var none = await author.PostAsJsonAsync(Url, new { name = "x", method = "directWeights", visibility = "private" });
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await none.Content.ReadAsStringAsync());

        (await CountMineAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData("", "private", false)]
    [InlineData("ok", "public", false)] // nobody without an account is ever a model's audience
    [InlineData("ok", "cavingGroup", false)] // shared, but with no group named
    [InlineData("ok", "private", true)] // bound to a group, it would not be private
    [InlineData("ok", "authenticated", true)] // everyone signed in, and a group as well
    public async Task A_model_whose_name_or_audience_breaks_a_rule_is_refused(string name, string visibility, bool withGroup)
    {
        var before = await CountMineAsync();

        var response = await author.PostAsJsonAsync(Url, new
        {
            name,
            method = "directWeights",
            definition = TerrainOnly(),
            visibility,
            cavingGroupId = withGroup ? cavingGroupId : (Guid?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await CountMineAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task The_author_rewrites_a_model_and_its_weights_follow()
    {
        var id = (await CreateAsync(author, Direct($"Draft {tag}"))).GetProperty("id").GetGuid();

        var response = await author.PutAsJsonAsync($"{Url}/{id}", Pairwise($"Final {tag}", [[1, 1], [1]]));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        updated.GetProperty("name").GetString().ShouldBe($"Final {tag}");
        updated.GetProperty("method").GetString().ShouldBe("pairwiseComparison");
        updated.GetProperty("weighting").GetProperty("weights").EnumerateArray()
            .Select(w => w.GetProperty("weight").GetDouble()).ShouldBe([1d / 3, 1d / 3, 1d / 3], 1e-9);

        // A rewrite that breaks a rule leaves the stored model as it was.
        var broken = await author.PutAsJsonAsync($"{Url}/{id}", Pairwise($"Final {tag}", [[1]]));
        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var kept = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        kept.GetProperty("definition").GetProperty("layers").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task The_author_deletes_a_model()
    {
        var id = (await CreateAsync(author, Direct($"Gone {tag}"))).GetProperty("id").GetGuid();

        (await author.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await author.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await author.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListedIdsAsync(author)).ShouldNotContain(id);
    }

    // ---------- who may read it ----------

    [Fact]
    public async Task A_private_model_is_its_authors_alone()
    {
        var id = (await CreateAsync(author, Direct($"Mine {tag}"))).GetProperty("id").GetGuid();

        // The author reads it; a clubmate and an outsider, neither holding any grant over it, are
        // told there is nothing there, by the same answer a made-up id gets.
        (await author.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var other in new[] { clubmate, outsider })
        {
            var refused = await other.GetAsync($"{Url}/{id}");
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeAsync(refused)).ShouldBe(CavePotentialModelEndpoints.NotFoundCode);
            (await ListedIdsAsync(other)).ShouldNotContain(id);
        }

        var absent = await outsider.GetAsync($"{Url}/{Guid.NewGuid()}");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(absent)).ShouldBe(CavePotentialModelEndpoints.NotFoundCode);
    }

    [Fact]
    public async Task A_model_shared_with_a_caving_group_reaches_its_members_and_nobody_else()
    {
        var id = (await CreateAsync(author, Direct($"Club's {tag}", group: cavingGroupId)))
            .GetProperty("id").GetGuid();

        var seen = await clubmate.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        seen.GetProperty("mine").GetBoolean().ShouldBeFalse();
        seen.GetProperty("mayEdit").GetBoolean().ShouldBeFalse();
        seen.GetProperty("mayDelete").GetBoolean().ShouldBeFalse();
        seen.GetProperty("weighting").GetProperty("weights").GetArrayLength().ShouldBe(2);
        (await ListedIdsAsync(clubmate)).ShouldContain(id);

        (await outsider.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListedIdsAsync(outsider)).ShouldNotContain(id);
    }

    [Fact]
    public async Task A_model_shared_with_everyone_signed_in_reaches_an_account_in_no_club_and_nobody_anonymous()
    {
        var id = (await CreateAsync(author, Direct($"Everyone's {tag}", visibility: "authenticated")))
            .GetProperty("id").GetGuid();

        (await outsider.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListedIdsAsync(outsider)).ShouldContain(id);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Made private again, it is gone for the outsider.
        (await author.PutAsJsonAsync($"{Url}/{id}", Direct($"Everyone's {tag}"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sharing_with_a_group_one_is_not_in_is_refused()
    {
        Guid strangers;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Other Potential Club {tag}", Slug = $"other-potential-club-{tag}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            strangers = group.Id;
        }

        var response = await author.PostAsJsonAsync(Url, Direct($"Theirs {tag}", group: strangers));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And an existing model cannot be handed to them afterwards either.
        var id = (await CreateAsync(author, Direct($"Kept {tag}"))).GetProperty("id").GetGuid();
        var moved = await author.PutAsJsonAsync($"{Url}/{id}", Direct($"Kept {tag}", group: strangers));
        moved.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(moved)).ShouldBe(CavingGroupBindingRules.ForbiddenCode);
    }

    // ---------- who may change it ----------

    [Fact]
    public async Task A_shared_model_is_changed_and_deleted_only_by_its_author()
    {
        var id = (await CreateAsync(author, Direct($"Shared {tag}", group: cavingGroupId)))
            .GetProperty("id").GetGuid();

        // A clubmate may read it, so is told plainly that it is not theirs to change.
        (await clubmate.PutAsJsonAsync($"{Url}/{id}", Direct($"Taken {tag}", group: cavingGroupId)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clubmate.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An outsider may not read it, and learns nothing from trying to change it.
        (await outsider.PutAsJsonAsync($"{Url}/{id}", Direct($"Taken {tag}")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var kept = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        kept.GetProperty("name").GetString().ShouldBe($"Shared {tag}");
        (await author.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---------- copying one ----------

    [Fact]
    public async Task A_reader_takes_a_private_copy_of_a_shared_model_and_changes_it_without_touching_the_original()
    {
        var original = await CreateAsync(author, Pairwise($"Club recipe {tag}", [[3, 5], [3]], group: cavingGroupId));
        var id = original.GetProperty("id").GetGuid();

        var response = await clubmate.PostAsJsonAsync($"{Url}/{id}/clone", new { name = $"  My recipe {tag}  " });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var copy = await response.Content.ReadFromJsonAsync<JsonElement>();
        var copyId = copy.GetProperty("id").GetGuid();

        copyId.ShouldNotBe(id);
        copy.GetProperty("name").GetString().ShouldBe($"My recipe {tag}");
        copy.GetProperty("mine").GetBoolean().ShouldBeTrue();
        copy.GetProperty("mayEdit").GetBoolean().ShouldBeTrue();
        // The copy is private whoever the original was shared with.
        copy.GetProperty("visibility").GetString().ShouldBe("private");
        copy.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
        copy.GetProperty("method").GetString().ShouldBe("pairwiseComparison");
        copy.GetProperty("definition").GetRawText().ShouldBe(original.GetProperty("definition").GetRawText());
        (await author.GetAsync($"{Url}/{copyId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The copy is the clubmate's to rewrite; the original still says what its author wrote.
        (await clubmate.PutAsJsonAsync($"{Url}/{copyId}", Direct($"My recipe {tag}"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var kept = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        kept.GetProperty("method").GetString().ShouldBe("pairwiseComparison");
        kept.GetProperty("definition").GetRawText().ShouldBe(original.GetProperty("definition").GetRawText());
        kept.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
    }

    [Fact]
    public async Task A_model_one_may_not_read_cannot_be_copied_and_a_copy_needs_a_name()
    {
        var id = (await CreateAsync(author, Direct($"Private recipe {tag}"))).GetProperty("id").GetGuid();
        var before = await CountOwnedByAsync(null);

        // The author may copy it; the outsider, who holds no grant over it, is told there is
        // nothing there — and nothing is created for them.
        var refused = await outsider.PostAsJsonAsync($"{Url}/{id}/clone", new { name = "Lifted" });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe(CavePotentialModelEndpoints.NotFoundCode);
        (await CountOwnedByAsync(null)).ShouldBe(before);

        (await author.PostAsJsonAsync($"{Url}/{id}/clone", new { name = " " })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CountOwnedByAsync(null)).ShouldBe(before);

        var own = await author.PostAsJsonAsync($"{Url}/{id}/clone", new { name = $"Second {tag}" });
        own.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountOwnedByAsync(null)).ShouldBe(before + 1);
    }

    // ---------- weights before saving ----------

    [Fact]
    public async Task The_weights_of_an_unsaved_definition_are_worked_out_and_nothing_is_stored()
    {
        var before = await CountOwnedByAsync(null);

        var response = await outsider.PostAsJsonAsync(
            $"{Url}/weighting", new { method = "pairwiseComparison", definition = ThreeLayers([[3, 5], [3]]) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var weighting = await response.Content.ReadFromJsonAsync<JsonElement>();

        weighting.GetProperty("weights").EnumerateArray().Select(w => w.GetProperty("layer").GetString())
            .ShouldBe(["lithology", "slope", "fault_distance"]);
        weighting.GetProperty("consistency").GetProperty("ratio").GetDouble().ShouldBe(0.0332, 0.0001);

        var broken = await outsider.PostAsJsonAsync(
            $"{Url}/weighting", new { method = "pairwiseComparison", definition = ThreeLayers([[3]]) });
        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(broken)).ShouldBe("validation.failed");

        (await CountOwnedByAsync(null)).ShouldBe(before);
    }

    // ---------- helpers ----------

    // ---------- models that learn from known entrances ----------

    private static object Learning(string name, string method, object? training, double? weight = null) => new
    {
        name,
        method,
        visibility = "private",
        definition = new
        {
            layers = new object[]
            {
                new
                {
                    layer = "slope",
                    weight,
                    transform = new { kind = "classes", classes = new object[] { new { upperBound = 10 }, new { } } },
                },
                new { layer = "lithology", weight = (double?)null, transform = new { kind = "categories" } },
            },
            training,
        },
    };

    [Theory]
    [InlineData("frequencyRatio")]
    [InlineData("weightsOfEvidence")]
    public async Task A_model_that_learns_from_entrances_is_saved_with_its_split_and_copied_whole(string method)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var created = await CreateAsync(author, Learning($"Learnt {tag}", method, new { heldOutShare = 0.25, seed = 42 }));

        created.GetProperty("method").GetString().ShouldBe(method);
        // No weights until it is run, and made from positions whatever it weighs.
        created.GetProperty("weighting").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetProperty("usesPositions").GetBoolean().ShouldBeTrue();
        var training = created.GetProperty("definition").GetProperty("training");
        training.GetProperty("heldOutShare").GetDouble().ShouldBe(0.25);
        training.GetProperty("seed").GetInt32().ShouldBe(42);

        // A copy is the same kind of model with the same split: the two would keep back the same cells.
        var copied = await author.PostAsJsonAsync($"{Url}/{created.GetProperty("id").GetGuid()}/clone", new { name = $"Copy {tag}" });
        copied.StatusCode.ShouldBe(HttpStatusCode.Created, await copied.Content.ReadAsStringAsync());
        var copy = await copied.Content.ReadFromJsonAsync<JsonElement>();
        copy.GetProperty("method").GetString().ShouldBe(method);
        copy.GetProperty("definition").GetProperty("training").GetProperty("seed").GetInt32().ShouldBe(42);
        copy.GetProperty("definition").GetProperty("layers").GetArrayLength().ShouldBe(2);

        // Asked for the weights of such a definition, the answer is an empty list and not an error.
        var weights = await author.PostAsJsonAsync($"{Url}/weighting", Learning("x", method, new { heldOutShare = 0.25, seed = 42 }));
        weights.StatusCode.ShouldBe(HttpStatusCode.OK, await weights.Content.ReadAsStringAsync());
        (await weights.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("weights").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_model_that_learns_from_entrances_is_refused_a_weight_a_missing_split_or_a_share_out_of_bounds()
    {
        var before = await CountMineAsync();

        async Task<List<string>> RefusedAsync(object body)
        {
            var response = await author.PostAsJsonAsync(Url, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            return [.. (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("errors").EnumerateObject().Select(e => e.Name)];
        }

        (await RefusedAsync(Learning("x", "weightsOfEvidence", new { heldOutShare = 0.3, seed = 1 }, weight: 2)))
            .ShouldContain("definition.layers[0].weight");
        (await RefusedAsync(Learning("x", "frequencyRatio", training: null)))
            .ShouldContain("definition.training");
        (await RefusedAsync(Learning("x", "frequencyRatio", new { heldOutShare = 0.9, seed = 1 })))
            .ShouldContain("definition.training.heldOutShare");
        // And a judged model is refused a split it would never use.
        (await RefusedAsync(Learning("x", "directWeights", new { heldOutShare = 0.3, seed = 1 }, weight: 1)))
            .ShouldContain("definition.training");

        (await CountMineAsync()).ShouldBe(before);
    }

    private static object TerrainOnly() => new
    {
        layers = new object[]
        {
            new
            {
                layer = "slope",
                weight = 3,
                transform = new
                {
                    kind = "classes",
                    classes = new object[]
                    {
                        new { upperBound = 5, score = 0.2 },
                        new { upperBound = 20, score = 1 },
                        new { score = 0.4 },
                    },
                },
            },
            new
            {
                layer = "elevation",
                weight = 1,
                transform = new { kind = "fuzzyLinear", low = 200, high = 1500 },
            },
        },
    };

    private static object ThreeLayers(double[][] pairwise) => new
    {
        layers = new object[]
        {
            new
            {
                layer = "lithology",
                transform = new
                {
                    kind = "categories",
                    categories = new object[] { new { code = "limestone", score = 1 }, new { code = "dolomite", score = 0.7 } },
                },
            },
            new
            {
                layer = "slope",
                transform = new { kind = "fuzzyLinear", low = 0, high = 30 },
            },
            new
            {
                layer = "fault_distance",
                transform = new { kind = "fuzzyLinear", low = 500, high = 0 },
            },
        },
        pairwise,
    };

    private static object Direct(
        string name, string? description = null, Guid? group = null, string? visibility = null) => new
        {
            name,
            description,
            method = "directWeights",
            definition = TerrainOnly(),
            cavingGroupId = group,
            visibility = visibility ?? (group is null ? "private" : "cavingGroup"),
        };

    private static object Pairwise(string name, double[][] pairwise, Guid? group = null) => new
    {
        name,
        method = "pairwiseComparison",
        definition = ThreeLayers(pairwise),
        cavingGroupId = group,
        visibility = group is null ? "private" : "cavingGroup",
    };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<List<Guid>> ListedIdsAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray().Select(m => m.GetProperty("id").GetGuid())];

    private Task<int> CountMineAsync() => CountOwnedByAsync(authorId);

    /// <summary>
    /// How many models this test's own people hold — never the whole table, which other test
    /// classes sharing the database write to as well.
    /// </summary>
    private async Task<int> CountOwnedByAsync(Guid? owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        Guid[] people = [authorId, clubmateId, outsiderId];
        return await db.CavePotentialModels.CountAsync(
            m => owner == null ? people.Contains(m.OwnerUserId) : m.OwnerUserId == owner);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
