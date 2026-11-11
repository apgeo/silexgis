// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The tag routes' guards and their two races: coining a tag is the tag vocabulary's business
/// while applying one is the target's, what a request names is validated, a target that is not
/// there is a coded miss, and a request that loses a race to another writer of the same tag or
/// the same tagging answers what that writer stored.
/// </summary>
/// <remarks>
/// The races are made to happen rather than hoped for: another writer inserts the row the request
/// is about to insert and holds its transaction open, so the request reads no such row, waits on
/// the unique index, and meets the violation the moment the other writer commits.
/// </remarks>
public sealed class TagEndpointTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];
    private HttpClient editor = null!;
    private HttpClient coinless = null!;
    private Guid editorId;
    private Guid coinlessId;
    private long caveTypeId;

    public TagEndpointTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tag-edi-{marker}@t.local");
        coinlessId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tag-coin-{marker}@t.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

            // The one right taken away: coining new tags. Writing caves, and so applying a tag
            // that exists to one, is left as the Editor role has it.
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = coinlessId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Tags,
                Actions = AccessAction.Create,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        editor = await AuthHelper.BearerClientAsync(factory, $"tag-edi-{marker}@t.local");
        coinless = await AuthHelper.BearerClientAsync(factory, $"tag-coin-{marker}@t.local");
    }

    [Fact]
    public async Task Coining_a_tag_needs_the_right_to_create_tags_and_applying_one_does_not()
    {
        var cave = await CreateCaveAsync($"Coin cave {marker}");
        var existing = $"existing-{marker}";
        (await TagAsync(editor, existing, cave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var coined = await TagAsync(coinless, $"coined-{marker}", cave);
        coined.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await coined.Content.ReadAsStringAsync());
        (await CodeOf(coined)).ShouldBe(CreateRules.ForbiddenCode);
        (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/tags?search=coined-{marker}"))
            .GetArrayLength().ShouldBe(0);

        var other = await CreateCaveAsync($"Second coin cave {marker}");
        (await TagAsync(coinless, existing, other)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TagAsync(editor, $"coined-{marker}", other)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    public static TheoryData<string> InvalidTaggings => new()
    {
        """{"tagName":"","entityType":"feature","entityId":"0192f0d3-0000-7000-8000-000000000001"}""",
        "{\"tagName\":\"" + new string('t', 81) + "\",\"entityType\":\"feature\",\"entityId\":\"0192f0d3-0000-7000-8000-000000000001\"}",
        """{"tagName":"!!!","entityType":"feature","entityId":"0192f0d3-0000-7000-8000-000000000001"}""",
        """{"tagName":"fine","entityType":"feature","entityId":"00000000-0000-0000-0000-000000000000"}""",
        """{"tagName":"fine","entityType":"no-such-kind","entityId":"0192f0d3-0000-7000-8000-000000000001"}""",
    };

    [Theory]
    [MemberData(nameof(InvalidTaggings))]
    public async Task A_tagging_outside_the_rules_is_refused_with_a_validation_code(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await editor.PostAsync("/api/v1/taggings/", content);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await CodeOf(response)).ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Every_tag_route_needs_an_account_and_a_target_or_tagging_that_exists()
    {
        var cave = await CreateCaveAsync($"Guarded tag cave {marker}");
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/tags")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync($"/api/v1/taggings/?entityType=feature&entityId={cave}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.PostAsJsonAsync("/api/v1/taggings/", new { tagName = "x", entityType = "feature", entityId = cave }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.DeleteAsync("/api/v1/taggings/1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // A target that does not exist is a coded miss on both reading and writing its tags, beside
        // the same requests answered for a target that does.
        var nowhere = Guid.NewGuid();
        var unknownRead = await editor.GetAsync($"/api/v1/taggings/?entityType=feature&entityId={nowhere}");
        unknownRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(unknownRead)).ShouldBe("tagging.entity_not_found");
        var unknownWrite = await TagAsync(editor, $"nowhere-{marker}", nowhere);
        unknownWrite.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(unknownWrite)).ShouldBe("tagging.entity_not_found");
        (await editor.GetAsync($"/api/v1/taggings/?entityType=feature&entityId={cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A deleted target is no target.
        var applied = await TagAsync(editor, $"deleted-{marker}", cave);
        applied.StatusCode.ShouldBe(HttpStatusCode.Created);
        var taggingId = (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        (await editor.DeleteAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var deletedTarget = await TagAsync(editor, $"deleted-{marker}", cave);
        deletedTarget.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(deletedTarget)).ShouldBe("tagging.entity_not_found");

        // A tagging id that names nothing, beside one that does.
        var missing = await editor.DeleteAsync($"/api/v1/taggings/{long.MaxValue}");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(missing)).ShouldBe("tagging.not_found");
        var live = await CreateCaveAsync($"Untagged cave {marker}");
        var kept = await TagAsync(editor, $"removed-{marker}", live);
        var keptId = (await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        (await editor.DeleteAsync($"/api/v1/taggings/{keptId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        taggingId.ShouldNotBe(keptId);
    }

    /// <summary>
    /// A search term is matched literally: underscores and percent signs are characters a tag
    /// name can hold, not wildcards that match every tag.
    /// </summary>
    [Fact]
    public async Task The_catalogue_search_matches_wildcard_characters_literally()
    {
        var cave = await CreateCaveAsync($"Literal tag cave {marker}");
        (await TagAsync(editor, $"under__{marker}", cave)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TagAsync(editor, $"plain{marker}", cave)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TagAsync(editor, $"{marker}50% off", cave)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TagAsync(editor, $"{marker}500 off", cave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var underscores = await NamesAsync("__");
        underscores.ShouldContain($"under__{marker}");
        underscores.ShouldAllBe(n => n.Contains("__"));
        underscores.ShouldNotContain($"plain{marker}");

        (await NamesAsync($"{marker}50%")).ShouldBe([$"{marker}50% off"]);
    }

    /// <summary>
    /// Two requests coining one tag: the loser, refused by the tag's unique index, adopts the tag
    /// the winner coined and tags its target with it.
    /// </summary>
    [Fact]
    public async Task A_request_that_loses_the_race_to_coin_a_tag_adopts_the_winners_tag()
    {
        var cave = await CreateCaveAsync($"Coin race cave {marker}");
        var name = $"coin-race-{marker}";

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"INSERT INTO tags (name, slug, created_at, updated_at) VALUES ('{name}', '{Tag.Slugify(name)}', now(), now())",
            () => TagAsync(editor, name, cave));

        raced.StatusCode.ShouldBe(HttpStatusCode.Created, await raced.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tagIds = await db.Tags.AsNoTracking().Where(t => t.Name == name).Select(t => t.Id).ToListAsync();
        tagIds.Count.ShouldBe(1);
        (await raced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tag").GetProperty("id").GetInt64()
            .ShouldBe(tagIds[0]);
        (await db.Taggings.AsNoTracking().CountAsync(x => x.TagId == tagIds[0] && x.FeatureId == cave)).ShouldBe(1);
    }

    /// <summary>
    /// Two requests applying one tag to one target: the loser, refused by the tagging's unique
    /// index, answers the winner's tagging as the success it would have reported a moment later.
    /// </summary>
    [Fact]
    public async Task A_request_that_loses_the_race_to_apply_a_tag_answers_the_winners_tagging()
    {
        var cave = await CreateCaveAsync($"Apply race cave {marker}");
        var name = $"apply-race-{marker}";
        var seeded = await CreateCaveAsync($"Seed cave {marker}");
        (await TagAsync(editor, name, seeded)).StatusCode.ShouldBe(HttpStatusCode.Created);
        long tagId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            tagId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .Tags.Where(t => t.Name == name).Select(t => t.Id).SingleAsync();
        }

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"INSERT INTO taggings (tag_id, feature_id, added_by) VALUES ({tagId}, '{cave}', '{editorId}')",
            () => TagAsync(editor, name, cave));

        raced.StatusCode.ShouldBe(HttpStatusCode.Created, await raced.Content.ReadAsStringAsync());
        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.Taggings.AsNoTracking().Where(x => x.TagId == tagId && x.FeatureId == cave)
            .Select(x => x.Id).ToListAsync();
        stored.Count.ShouldBe(1);
        (await raced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64().ShouldBe(stored[0]);
    }

    private async Task<List<string>> NamesAsync(string search) =>
        [.. (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/tags?search={Uri.EscapeDataString(search)}"))
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()!)];

    private static Task<HttpResponseMessage> TagAsync(HttpClient client, string tagName, Guid featureId) =>
        client.PostAsJsonAsync("/api/v1/taggings/", new { tagName, entityType = "feature", entityId = featureId });

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
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

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        editor?.Dispose();
        coinless?.Dispose();
        factory.Dispose();
    }
}
