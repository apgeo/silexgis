// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The ceiling on a selection is an installation setting: published to the client in the map's
/// configuration, and the bound on how many objects one request for the selection's summary may
/// name — the two must move together, or a selection the client allows would be refused.
/// </summary>
public sealed class SelectionMaxConfigurationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory shipped;
    private readonly SilexGisApiFactory configured;

    private HttpClient shippedViewer = null!;
    private HttpClient configuredViewer = null!;

    public SelectionMaxConfigurationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        shipped = new SilexGisApiFactory(postgres.ConnectionString);
        configured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:SelectionMax"] = "3",
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Viewer, $"sel-a-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(configured, GlobalRoles.Viewer, $"sel-b-{suffix}@t.local");
        shippedViewer = await AuthHelper.BearerClientAsync(shipped, $"sel-a-{suffix}@t.local");
        configuredViewer = await AuthHelper.BearerClientAsync(configured, $"sel-b-{suffix}@t.local");
    }

    [Fact]
    public async Task The_shipped_ceiling_is_200_and_is_published()
    {
        var config = (await shippedViewer.GetFromJsonAsync<JsonObject>("/api/v1/map/config"))!;
        config["selectionMax"]!.GetValue<int>().ShouldBe(200);
        (await AskAboutAsync(shippedViewer, 200)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeTooManyAsync(await AskAboutAsync(shippedViewer, 201));
    }

    [Fact]
    public async Task A_configured_ceiling_is_published_and_bounds_the_summary_request()
    {
        var config = (await configuredViewer.GetFromJsonAsync<JsonObject>("/api/v1/map/config"))!;
        config["selectionMax"]!.GetValue<int>().ShouldBe(3);

        (await AskAboutAsync(configuredViewer, 3)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeTooManyAsync(await AskAboutAsync(configuredViewer, 4));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    public void A_ceiling_outside_one_to_the_largest_page_refuses_to_start(string value)
    {
        using var misconfigured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:SelectionMax"] = value,
        });
        var refusal = Should.Throw<OptionsValidationException>(() => misconfigured.CreateClient());
        refusal.Message.ShouldContain("Map:SelectionMax");
    }

    /// <summary>The selection summary's request: the feature list asked about this many ids.</summary>
    private static Task<HttpResponseMessage> AskAboutAsync(HttpClient client, int count)
    {
        var ids = string.Join("&", Enumerable.Range(0, count).Select(_ => $"ids={Guid.NewGuid()}"));
        return client.GetAsync($"/api/v1/features?{ids}&pageSize={count}");
    }

    private static async Task ShouldBeTooManyAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.too_many_ids");
    }

    public ValueTask DisposeAsync()
    {
        shippedViewer.Dispose();
        configuredViewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        shipped.Dispose();
        configured.Dispose();
    }
}
