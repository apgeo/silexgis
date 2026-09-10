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
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Grottocenter;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The numbers other registers know a cave by, and the optional errand that goes to find one.
///
/// <para>
/// Nothing here reaches the network: the far end answers from a stub wired in at the message
/// handler, and the stub records every request. That is what makes the half of this feature that
/// matters testable at all — the assertions below are about what would have <em>left</em> the
/// machine, and there is no way to make them against a timeout.
/// </para>
/// <para>
/// The class is written twice over the same fixture, once with the integration on and once with
/// it off, because "off by default" is a claim about the shipped configuration and the only way
/// to check it is to run the same request against an installation that was told nothing.
/// </para>
/// </summary>
public sealed class CaveExternalIdTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly string connectionString;
    private readonly LookupStub upstream = new();
    private readonly SilexGisApiFactory enabled;
    private readonly SilexGisApiFactory shipped;

    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private string tag = null!;

    public CaveExternalIdTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;

        enabled = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                // Supplied the way an operator supplies it — SILEXGIS__Grottocenter__Enabled.
                ["Grottocenter:Enabled"] = "true",
                ["Grottocenter:Endpoint"] = "https://grottocenter.invalid/api/v1",
                // The politeness gap protects somebody else's service, and the stub is not one.
                ["Grottocenter:MinRequestIntervalMs"] = "0",
            },
            services => services
                .AddHttpClient(GrottocenterClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => upstream));

        // Told nothing at all: this is the configuration an installation has out of the box.
        shipped = new SilexGisApiFactory(
            connectionString,
            null,
            services => services
                .AddHttpClient(GrottocenterClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => upstream));
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(enabled, GlobalRoles.Editor, $"xid-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(enabled, GlobalRoles.Viewer, $"xid-viewer-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(enabled, $"xid-editor-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(enabled, $"xid-viewer-{tag}@t.local");
    }

    /// <summary>
    /// The condition the whole integration was allowed on: an installation nobody configured
    /// makes no outbound request.
    /// </summary>
    /// <remarks>
    /// Asserted on the handler rather than on a timeout or an error message. A request that was
    /// attempted and failed is indistinguishable from one that was never made if all you have is
    /// the response, and the difference is the entire promise: the name of a cave either left
    /// this machine or it did not.
    /// </remarks>
    [Fact]
    public async Task An_installation_that_did_not_turn_the_lookup_on_sends_nothing()
    {
        var caveId = await CreateCaveAsync($"XID Cave {tag}");
        upstream.Reset();

        var client = await AuthHelper.BearerClientAsync(shipped, $"xid-editor-{tag}@t.local");
        var response = await client.PostAsync(LookupUrl(caveId), null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await ReadAsync(response);
        body.GetProperty("configured").GetBoolean().ShouldBeFalse();
        body.GetProperty("candidates").GetArrayLength().ShouldBe(0);

        upstream.Requests.ShouldBeEmpty();
    }

    /// <summary>
    /// What a lookup sends, and what it keeps. Both halves, because either alone would pass while
    /// the feature did the wrong thing.
    /// </summary>
    [Fact]
    public async Task A_lookup_sends_only_the_name_and_stores_nothing_by_asking()
    {
        var name = $"XID Cave {tag}";
        var caveId = await CreateCaveAsync(name);
        upstream.Reset();
        upstream.Answers($$"""
            {"results":[{"id":"77","name":"Somebody Else's Cave","country":"RO","latitude":45.1,"longitude":25.2}]}
            """);

        var response = await editor.PostAsync(LookupUrl(caveId), null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await ReadAsync(response);
        body.GetProperty("configured").GetBoolean().ShouldBeTrue();
        var candidate = body.GetProperty("candidates").EnumerateArray().Single();
        candidate.GetProperty("externalId").GetString().ShouldBe("77");
        candidate.GetProperty("name").GetString().ShouldBe("Somebody Else's Cave");
        candidate.GetProperty("url").GetString().ShouldNotBeNull().ShouldContain("77");

        // The name was asked about, and nothing else went with it. The cave's position is the one
        // thing that must not leave, and this errand would carry it for every cave anybody ever
        // pressed the button on.
        var asked = upstream.Requests.ShouldHaveSingleItem();
        Uri.UnescapeDataString(asked).ShouldContain(name);
        asked.ShouldNotContain("45.");
        asked.ShouldNotContain("25.");

        // And asking stored nothing: what came back is offered to a person, not filed.
        (await StoredAsync(caveId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_identifier_is_stored_when_it_is_accepted_and_cleared_when_it_is_taken_back()
    {
        var caveId = await CreateCaveAsync($"XID Cave {tag}");

        var stored = await PutAsync(editor, caveId, "grottocenter", "77");
        stored.StatusCode.ShouldBe(HttpStatusCode.OK, await stored.Content.ReadAsStringAsync());

        var listed = await ReadAsync(await editor.GetAsync(ListUrl(caveId)));
        var row = listed.EnumerateArray().Single();
        row.GetProperty("system").GetString().ShouldBe("grottocenter");
        row.GetProperty("value").GetString().ShouldBe("77");
        row.GetProperty("url").GetString().ShouldNotBeNull().ShouldContain("77");

        // The value only. Nothing the far end said about the cave came with it, so nothing here
        // can go stale while still looking authoritative.
        var kept = await StoredAsync(caveId);
        kept.ShouldHaveSingleItem().Value.ShouldBe("77");

        // Written a second time it replaces rather than accumulates: two numbers for one cave in
        // one register is a contradiction, not extra information.
        (await PutAsync(editor, caveId, "grottocenter", "78")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(caveId)).ShouldHaveSingleItem().Value.ShouldBe("78");

        // A second register stands beside it rather than displacing it.
        (await PutAsync(editor, caveId, "national_cadastre", "2233/7")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(caveId)).Count.ShouldBe(2);

        // Cleared by writing nothing — the way a number entered by mistake is taken back.
        (await PutAsync(editor, caveId, "grottocenter", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(caveId)).ShouldHaveSingleItem().System.ShouldBe(ExternalIdSystem.NationalCadastre);
    }

    /// <summary>
    /// Recording a number is a write on the cave, and so is sending its name out to look one up.
    /// </summary>
    [Fact]
    public async Task A_reader_who_may_not_edit_the_cave_records_nothing_and_asks_nobody()
    {
        var caveId = await CreateCaveAsync($"XID Cave {tag}");
        upstream.Reset();

        (await PutAsync(viewer, caveId, "grottocenter", "77")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsync(LookupUrl(caveId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The refusal happened here rather than after the errand: nothing left the machine on
        // behalf of somebody who may not edit the cave.
        upstream.Requests.ShouldBeEmpty();
        (await StoredAsync(caveId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_register_this_application_does_not_know_is_refused()
    {
        var caveId = await CreateCaveAsync($"XID Cave {tag}");

        var response = await PutAsync(editor, caveId, "some_other_register", "77");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("external_id.unknown_system");
        (await StoredAsync(caveId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_anonymous_caller_reaches_none_of_it()
    {
        var caveId = await CreateCaveAsync($"XID Cave {tag}");
        var anonymous = enabled.CreateClient();

        (await anonymous.GetAsync(ListUrl(caveId))).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        (await anonymous.PostAsync(LookupUrl(caveId), null)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    // ---- fixture ----

    private static string ListUrl(Guid caveId) => $"/api/v1/caves/{caveId}/external-ids";

    private static string LookupUrl(Guid caveId) =>
        $"/api/v1/caves/{caveId}/external-ids/grottocenter/lookup";

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client, Guid caveId, string system, string? value) =>
        client.PutAsJsonAsync($"/api/v1/caves/{caveId}/external-ids/{system}", new { value });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<List<FeatureExternalId>> StoredAsync(Guid caveId)
    {
        using var scope = enabled.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.FeatureExternalIds.AsNoTracking()
            .Where(x => x.FeatureId == caveId)
            .OrderBy(x => x.System)
            .ToListAsync();
    }

    private async Task<long> CaveTypeIdAsync()
    {
        using var scope = enabled.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId = await CaveTypeIdAsync(),
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        enabled.Dispose();
        shipped.Dispose();
        upstream.Dispose();
    }

    /// <summary>
    /// Stands in for grottocenter.org, and records what would have been sent to it.
    /// </summary>
    private sealed class LookupStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<string> requests = [];

        private string body = """{"results":[]}""";

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (gate)
                {
                    return [.. requests];
                }
            }
        }

        public void Answers(string json) => body = json;

        public void Reset()
        {
            lock (gate)
            {
                requests.Clear();
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
