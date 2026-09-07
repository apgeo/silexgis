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
/// Rule ownership and sharing: everyone reads, your own set is yours, and anything a group or
/// the installation inherits is an administrator's — because promoting a set changes what
/// everybody else's next import proposes.
/// </summary>
public sealed class TermRuleSetTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;
    private HttpClient other = null!;
    private HttpClient admin = null!;
    private string tag = null!;

    public TermRuleSetTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tr-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tr-other-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tr-admin-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"tr-editor-{tag}@t.local");
        other = await AuthHelper.BearerClientAsync(factory, $"tr-other-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tr-admin-{tag}@t.local");

        // The seeder runs at startup only when auto-migrate is on; make the shipped set present
        // for this database whichever way the suite arrived at it.
        using var scope = factory.Services.CreateAsyncScope();
        await TermRuleSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    [Fact]
    public async Task The_installation_ships_a_set_that_a_first_import_can_use()
    {
        var effective = await GetJsonAsync(editor, "/api/v1/term-rule-sets/effective");
        effective.GetProperty("set").ValueKind.ShouldBe(JsonValueKind.Object);
        effective.GetProperty("set").GetProperty("rules").EnumerateArray().ShouldNotBeEmpty();

        // Nobody wrote it, so nobody owns it and nobody may delete it — it is the last fallback
        // when every other scope is empty.
        var set = effective.GetProperty("set").GetProperty("set");
        set.GetProperty("isSeeded").GetBoolean().ShouldBeTrue();
        set.GetProperty("ownerUserId").ValueKind.ShouldBe(JsonValueKind.Null);
        set.GetProperty("canDelete").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Editing_a_set_you_do_not_own_gives_you_one_of_your_own()
    {
        var shipped = (await GetJsonAsync(editor, "/api/v1/term-rule-sets/effective"))
            .GetProperty("set").GetProperty("set").GetProperty("id").GetGuid();

        // The installation default is not editable in place by an ordinary account…
        var refused = await editor.PutAsJsonAsync($"/api/v1/term-rule-sets/{shipped}", new
        {
            name = "Hijacked",
            description = (string?)null,
            rules = Array.Empty<object>(),
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(refused)).ShouldBe("term_rules.not_editable");

        // …and copying it is what "each user can modify them" means: the shipped set stays
        // exactly as it was, and the copy is theirs.
        var copy = await editor.PostAsJsonAsync("/api/v1/term-rule-sets", new
        {
            name = $"My rules {tag}",
            description = (string?)null,
            rules = (object?)null,
            copyFromId = shipped,
        });
        copy.StatusCode.ShouldBe(HttpStatusCode.Created, await copy.Content.ReadAsStringAsync());
        var mine = JsonDocument.Parse(await copy.Content.ReadAsStringAsync()).RootElement;
        mine.GetProperty("rules").EnumerateArray().Count().ShouldBeGreaterThan(0);
        mine.GetProperty("set").GetProperty("canEdit").GetBoolean().ShouldBeTrue();

        // …and it becomes what their imports start from, without changing anybody else's.
        var mineId = mine.GetProperty("set").GetProperty("id").GetGuid();
        (await GetJsonAsync(editor, "/api/v1/term-rule-sets/effective"))
            .GetProperty("set").GetProperty("set").GetProperty("id").GetGuid().ShouldBe(mineId);
        (await GetJsonAsync(other, "/api/v1/term-rule-sets/effective"))
            .GetProperty("set").GetProperty("set").GetProperty("id").GetGuid().ShouldBe(shipped);
    }

    [Fact]
    public async Task Somebody_elses_set_is_readable_but_not_writable()
    {
        var mineId = await CreateAsync(editor, $"Readable {tag}");

        // Rules carry no privacy: a club that could not show another club its rules could not
        // hand them over either.
        (await editor.GetAsync($"/api/v1/term-rule-sets/{mineId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var seen = await GetJsonAsync(other, $"/api/v1/term-rule-sets/{mineId}");
        seen.GetProperty("set").GetProperty("canEdit").GetBoolean().ShouldBeFalse();

        var refused = await other.PutAsJsonAsync($"/api/v1/term-rule-sets/{mineId}", new
        {
            name = "Theirs now",
            description = (string?)null,
            rules = Array.Empty<object>(),
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await other.DeleteAsync($"/api/v1/term-rule-sets/{mineId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_an_administrator_moves_a_set_to_a_scope_everybody_inherits()
    {
        var mineId = await CreateAsync(editor, $"Promotable {tag}");

        var refused = await editor.PostAsJsonAsync($"/api/v1/term-rule-sets/{mineId}/scope", new
        {
            scope = "installation",
            cavingGroupId = (Guid?)null,
            isDefault = true,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(refused)).ShouldBe("term_rules.promotion_forbidden");

        var promoted = await admin.PostAsJsonAsync($"/api/v1/term-rule-sets/{mineId}/scope", new
        {
            scope = "installation",
            cavingGroupId = (Guid?)null,
            isDefault = true,
        });
        promoted.StatusCode.ShouldBe(HttpStatusCode.OK, await promoted.Content.ReadAsStringAsync());

        // The incumbent stands down: one default per scope, so the next account reads this one.
        (await GetJsonAsync(other, "/api/v1/term-rule-sets/effective"))
            .GetProperty("set").GetProperty("set").GetProperty("id").GetGuid().ShouldBe(mineId);

        // Put the installation back so the rest of the suite is not standing on this test.
        await admin.PostAsJsonAsync($"/api/v1/term-rule-sets/{mineId}/scope", new
        {
            scope = "user",
            cavingGroupId = (Guid?)null,
            isDefault = false,
        });
    }

    [Fact]
    public async Task A_set_travels_between_installations_as_a_file()
    {
        var mineId = await CreateAsync(editor, $"Exportable {tag}");

        var export = await editor.GetAsync($"/api/v1/term-rule-sets/{mineId}/export");
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var document = JsonDocument.Parse(await export.Content.ReadAsStringAsync()).RootElement;
        document.GetProperty("rules").EnumerateArray().Count().ShouldBe(1);

        // What comes back in is somebody's own set and never anybody's default: a file from
        // another club is a proposal, not a change to what every import proposes.
        var imported = await other.PostAsJsonAsync("/api/v1/term-rule-sets/import", new
        {
            document = JsonSerializer.Deserialize<JsonElement>(document.GetRawText()),
            name = $"From a friend {tag}",
        });
        imported.StatusCode.ShouldBe(HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
        var set = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()).RootElement.GetProperty("set");
        set.GetProperty("scope").GetString().ShouldBe("user");
        set.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_rule_that_could_not_be_evaluated_is_refused_at_the_door()
    {
        // The first thing that reads a club's rules may be somebody else's server, so a
        // pattern that cannot be compiled must not be storable.
        var refused = await editor.PostAsJsonAsync("/api/v1/term-rule-sets", new
        {
            name = $"Broken {tag}",
            description = (string?)null,
            rules = new object[]
            {
                new
                {
                    id = "bad",
                    name = "Bad",
                    matchMode = "regex",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["ro"] = ["[unclosed"] },
                    target = "cave",
                    caveTypeCode = "cave",
                },
            },
            copyFromId = (Guid?)null,
        });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(refused)).ShouldBe("term_rules.invalid");
    }

    [Fact]
    public async Task The_shipped_set_cannot_be_deleted_even_by_an_administrator()
    {
        var shipped = (await GetJsonAsync(admin, "/api/v1/term-rule-sets/effective"))
            .GetProperty("set").GetProperty("set").GetProperty("id").GetGuid();

        var refused = await admin.DeleteAsync($"/api/v1/term-rule-sets/{shipped}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(refused)).ShouldBe("term_rules.seeded_undeletable");
    }

    // ---------- helpers ----------

    private static async Task<Guid> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/term-rule-sets", new
        {
            name,
            description = (string?)null,
            rules = new object[]
            {
                new
                {
                    id = "cave",
                    name = "Cave",
                    matchMode = "wholeWord",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["ro"] = ["peșteră"] },
                    target = "cave",
                    caveTypeCode = "cave",
                    strip = "leading",
                },
            },
            copyFromId = (Guid?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("set").GetProperty("id").GetGuid();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        other?.Dispose();
        admin?.Dispose();
        factory.Dispose();
    }
}
