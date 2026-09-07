// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation's starting interface arrangement, and the two things it must not be: a policy
/// that overwrites what somebody chose, and a key inside the per-user preferences document.
/// </summary>
public sealed class UiDefaultsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private string tag = null!;

    public UiDefaultsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ud-admin-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ud-editor-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"ud-admin-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"ud-editor-{tag}@t.local");
    }

    [Fact]
    public async Task An_installation_that_has_published_nothing_answers_an_empty_arrangement()
    {
        var response = await editor.GetAsync("/api/v1/ui-defaults");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("panel").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task An_administrator_publishes_a_starting_arrangement_and_everybody_reads_it()
    {
        var saved = await admin.PutAsJsonAsync("/api/v1/admin/settings/interface", new
        {
            panelDefaults = """{"main":{"order":["history","details"],"width":30}}""",
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // Read by an ordinary account: a default nobody but an administrator can see would be a
        // default that never applies.
        var read = await Eventually(() => editor.GetAsync("/api/v1/ui-defaults"), body =>
            body.GetProperty("panel").TryGetProperty("main", out _));

        read.GetProperty("panel").GetProperty("main").GetProperty("width").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task Publishing_one_is_not_something_an_ordinary_account_may_do()
    {
        var response = await editor.PutAsJsonAsync("/api/v1/admin/settings/interface", new
        {
            panelDefaults = """{"main":{"width":10}}""",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Something_that_is_not_a_json_object_is_refused_rather_than_stored()
    {
        // Stored, it would reach every client on every page load as an unparseable arrangement.
        var response = await admin.PutAsJsonAsync("/api/v1/admin/settings/interface", new
        {
            panelDefaults = "[\"not\",\"an\",\"object\"]",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_starting_arrangement_is_not_mixed_into_the_callers_own_preferences()
    {
        // The two documents are separate on purpose: the caller's is opaque and replaced
        // wholesale by whoever writes it, so an installation value living inside it would be
        // deleted by the first client that saved its own subtree.
        await admin.PutAsJsonAsync("/api/v1/admin/settings/interface", new
        {
            panelDefaults = """{"main":{"width":30}}""",
        });

        var mine = await editor.GetAsync("/api/v1/me/preferences");
        var body = JsonDocument.Parse(await mine.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("preferences").TryGetProperty("panel", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Reading_the_starting_arrangement_needs_an_account()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/ui-defaults")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Retries while the settings cache still holds the previous value. Sections are cached per
    /// process for a short window, so a test that saves one and immediately depends on it is
    /// racing the cache rather than the application.
    /// </summary>
    private static async Task<JsonElement> Eventually(
        Func<Task<HttpResponseMessage>> send, Func<JsonElement, bool> settled)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (true)
        {
            var response = await send();
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            if (settled(body) || DateTimeOffset.UtcNow > deadline)
            {
                return body;
            }

            await Task.Delay(1000);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin?.Dispose();
        editor?.Dispose();
        factory.Dispose();
    }
}
