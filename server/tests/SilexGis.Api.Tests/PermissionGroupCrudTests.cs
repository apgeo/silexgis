// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The permission-group surface as a whole: an ordinary group created, renamed and deleted; the
/// name it is known by kept unique however it is spelled; the request shape validated; an unknown
/// id answered as not found; and every write route closed to an anonymous caller and to a signed-in
/// account outside the permission model — each refusal beside the same request succeeding for a
/// full administrator, so a route that simply broke cannot pass as refused.
/// </summary>
public sealed class PermissionGroupCrudTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;
    private HttpClient viewer = null!;
    private readonly Dictionary<Guid, string> originalNames = [];
    private Guid viewerId;

    public PermissionGroupCrudTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pgc-adm-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pgc-vie-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"pgc-adm-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pgc-vie-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin.Dispose();
        viewer.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task An_ordinary_group_is_created_renamed_and_deleted()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/permission-groups",
            new { name = $"  Surveyors {suffix} ", description = "first" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("name").GetString().ShouldBe($"Surveyors {suffix}", "the name is stored trimmed");
        body.GetProperty("slug").GetString().ShouldBe($"surveyors-{suffix}");
        body.GetProperty("isProtected").GetBoolean().ShouldBeFalse();

        var renamed = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}",
            new { name = $"Mappers {suffix}", description = "second" });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/permission-groups/{id}");
        read.GetProperty("name").GetString().ShouldBe($"Mappers {suffix}");
        read.GetProperty("description").GetString().ShouldBe("second");
        // The slug is the group's lasting address — the installation's own seeded groups are found
        // by it at every start — so a rename changes the name and leaves the slug where it was.
        read.GetProperty("slug").GetString().ShouldBe($"surveyors-{suffix}");

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/permission-groups");
        listed.EnumerateArray().Count(g => g.GetProperty("id").GetGuid() == id).ShouldBe(1);

        var deleted = await admin.DeleteAsync($"/api/v1/permission-groups/{id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        await ShouldAnswerAsync(await admin.GetAsync($"/api/v1/permission-groups/{id}"),
            HttpStatusCode.NotFound, PermissionGroupEndpoints.NotFoundCode);
    }

    [Fact]
    public async Task A_name_another_group_carries_is_refused_however_it_is_spelled()
    {
        var taken = $"Diggers {suffix}";
        (await admin.PostAsJsonAsync("/api/v1/permission-groups", new { name = taken }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        foreach (var spelling in new[] { taken, taken.ToUpperInvariant(), $"diggers  {suffix}!" })
        {
            await ShouldAnswerAsync(
                await admin.PostAsJsonAsync("/api/v1/permission-groups", new { name = spelling }),
                HttpStatusCode.BadRequest, PermissionGroupEndpoints.NameTakenCode);
        }

        var otherId = await CreateAsync($"Climbers {suffix}");
        foreach (var spelling in new[] { taken, taken.ToLowerInvariant() })
        {
            await ShouldAnswerAsync(
                await admin.PutAsJsonAsync($"/api/v1/permission-groups/{otherId}", new { name = spelling }),
                HttpStatusCode.BadRequest, PermissionGroupEndpoints.NameTakenCode);
        }
    }

    [Fact]
    public async Task The_request_shape_is_validated_on_create_and_on_rename()
    {
        var id = await CreateAsync($"Shaped {suffix}");

        foreach (var name in new[] { "", new string('n', 101) })
        {
            (await admin.PostAsJsonAsync("/api/v1/permission-groups", new { name }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}", new { name }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A name with no letters or digits has no address to be known by.
        await ShouldAnswerAsync(await admin.PostAsJsonAsync("/api/v1/permission-groups", new { name = "!!!" }),
            HttpStatusCode.BadRequest, "permission_group.name_invalid");
        await ShouldAnswerAsync(await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}", new { name = "— —" }),
            HttpStatusCode.BadRequest, "permission_group.name_invalid");

        // The longest name allowed saves.
        var longest = $"{suffix}{new string('n', 92)}";
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}", new { name = longest }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_group_is_answered_as_not_found()
    {
        var unknown = Guid.NewGuid();
        HttpResponseMessage[] answers =
        [
            await admin.GetAsync($"/api/v1/permission-groups/{unknown}"),
            await admin.PutAsJsonAsync($"/api/v1/permission-groups/{unknown}", new { name = $"Nobody {suffix}" }),
            await admin.DeleteAsync($"/api/v1/permission-groups/{unknown}"),
            await admin.GetAsync($"/api/v1/permission-groups/{unknown}/entries"),
            await admin.PutAsJsonAsync($"/api/v1/permission-groups/{unknown}/entries", new { entries = Array.Empty<object>() }),
            await admin.GetAsync($"/api/v1/permission-groups/{unknown}/members"),
            await admin.PostAsJsonAsync($"/api/v1/permission-groups/{unknown}/members",
                new { memberKind = "user", memberId = viewerId }),
            await admin.DeleteAsync($"/api/v1/permission-groups/{unknown}/members/user/{viewerId}"),
        ];

        foreach (var answer in answers)
        {
            await ShouldAnswerAsync(answer, HttpStatusCode.NotFound, PermissionGroupEndpoints.NotFoundCode);
        }
    }

    [Fact]
    public async Task Every_write_route_refuses_an_anonymous_caller()
    {
        var id = await CreateAsync($"Anonymous target {suffix}");
        using var anonymous = factory.CreateClient();

        foreach (var answer in await WriteEachRouteAsync(anonymous, id))
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, answer.RequestMessage?.RequestUri?.ToString());
        }

        // The group is untouched, and the same routes work for somebody signed in who may write it.
        foreach (var answer in await WriteEachRouteAsync(admin, id))
        {
            ((int)answer.StatusCode).ShouldBeLessThan(300,
                $"{answer.RequestMessage?.Method} {answer.RequestMessage?.RequestUri}: {await answer.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Every_write_route_refuses_an_account_outside_the_permission_model()
    {
        var id = await CreateAsync($"Forbidden target {suffix}");

        foreach (var answer in await WriteEachRouteAsync(viewer, id))
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
                $"{answer.RequestMessage?.Method} {answer.RequestMessage?.RequestUri}: {await answer.Content.ReadAsStringAsync()}");
        }

        // The group the viewer was refused on is still there, named as it was, with nobody in it.
        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/permission-groups/{id}");
        read.GetProperty("name").GetString().ShouldBe($"Forbidden target {suffix}");
        read.GetProperty("memberCount").GetInt32().ShouldBe(0);

        foreach (var answer in await WriteEachRouteAsync(admin, id))
        {
            ((int)answer.StatusCode).ShouldBeLessThan(300,
                $"{answer.RequestMessage?.Method} {answer.RequestMessage?.RequestUri}: {await answer.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>
    /// The rule editor saves the whole list every time. A rule the save leaves as it was keeps its
    /// row — and so its author and its history — and only what changed is written.
    /// </summary>
    [Fact]
    public async Task Saving_a_rule_list_keeps_the_rules_it_did_not_change()
    {
        var id = await CreateAsync($"Kept rules {suffix}");
        var read = new { effect = "allow", domain = "features", actions = "read", scopeKind = "all" };
        var trips = new { effect = "allow", domain = "tripLogs", actions = "read", scopeKind = "all" };

        var first = await ReplaceEntriesAsync(id, read);
        var readRow = first.Single().GetProperty("id").GetInt64();

        var second = await ReplaceEntriesAsync(id, read, trips);
        second.Length.ShouldBe(2);
        second.Single(e => e.GetProperty("domain").GetString() == "features").GetProperty("id").GetInt64()
            .ShouldBe(readRow, "the rule nobody changed keeps its row");
        var tripsRow = second.Single(e => e.GetProperty("domain").GetString() == "tripLogs").GetProperty("id").GetInt64();

        // A rule whose actions change is a different rule: its old row goes and a new one comes.
        var third = await ReplaceEntriesAsync(id, trips, read with { actions = "read, write" });
        third.Length.ShouldBe(2);
        third.Single(e => e.GetProperty("domain").GetString() == "tripLogs").GetProperty("id").GetInt64()
            .ShouldBe(tripsRow);
        third.Single(e => e.GetProperty("domain").GetString() == "features").GetProperty("id").GetInt64()
            .ShouldNotBe(readRow);

        (await ReplaceEntriesAsync(id)).ShouldBeEmpty();
    }

    // ---- helpers ----

    private async Task<JsonElement[]> ReplaceEntriesAsync(Guid id, params object[] entries)
    {
        var saved = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}/entries", new { entries });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return [.. (await saved.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    /// <summary>
    /// One call to each of the seven routes that change something or say what somebody else could
    /// do — the preview, create, rename, rule replace, trustee add, trustee removal and delete —
    /// ordered so that, for a caller who may make them, each succeeds: the trustee added is the
    /// one removed, and the group deleted last is the one created first in the sequence.
    /// </summary>
    private async Task<List<HttpResponseMessage>> WriteEachRouteAsync(HttpClient client, Guid id)
    {
        var name = $"Route {Guid.NewGuid():N}"[..20];
        var answers = new List<HttpResponseMessage>
        {
            await client.PostAsJsonAsync("/api/v1/permission-groups/preview", new { subjectKind = "user", subjectId = viewerId }),
            await client.PostAsJsonAsync("/api/v1/permission-groups", new { name }),
            await client.PutAsJsonAsync($"/api/v1/permission-groups/{id}", new { name = $"{name} renamed" }),
            await client.PutAsJsonAsync($"/api/v1/permission-groups/{id}/entries", new { entries = Array.Empty<object>() }),
            await client.PostAsJsonAsync($"/api/v1/permission-groups/{id}/members", new { memberKind = "user", memberId = viewerId }),
            await client.DeleteAsync($"/api/v1/permission-groups/{id}/members/user/{viewerId}"),
        };

        // The delete aims at the group this sequence created, so the target survives for the next caller.
        var createdId = answers[1].StatusCode == HttpStatusCode.Created
            ? (await answers[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
            : id;
        answers.Add(await client.DeleteAsync($"/api/v1/permission-groups/{createdId}"));

        // A rename that succeeded is put back, so the name assertions after it still hold.
        if (answers[2].IsSuccessStatusCode)
        {
            (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{id}", new { name = originalNames[id] }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return answers;
    }

    private async Task<Guid> CreateAsync(string name)
    {
        var created = await admin.PostAsJsonAsync("/api/v1/permission-groups", new { name });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        originalNames[id] = name;
        return id;
    }

    private static async Task ShouldAnswerAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
