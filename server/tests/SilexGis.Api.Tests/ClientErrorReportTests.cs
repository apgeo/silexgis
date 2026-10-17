// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Shouldly;
using SilexGis.Api.Features.Diagnostics;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// A signed-in person's browser reports the errors it saw into the installation's own log — and
/// nobody else's browser can, nor can any browser fill the log.
/// </summary>
public sealed class ClientErrorReportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly LogCapture logs = new();

    private HttpClient person = null!;
    private HttpClient anonymous = null!;
    private string email = "";

    public ClientErrorReportTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<ILogEventSink>(logs);
        });

    public async Task InitializeAsync()
    {
        email = $"cer-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        person = await AuthHelper.BearerClientAsync(factory, email);
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        person?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private static object Error(string fingerprint, string message = "x is undefined") => new
    {
        fingerprint,
        kind = "uncaught",
        name = "TypeError",
        message,
        frame = "src/pages/MapPage.tsx:120:7",
        stack = "TypeError: x is undefined\n    at MapPage (src/pages/MapPage.tsx:120:7)",
        pageUrl = "https://club.example/caves/123?share=SECRET-TOKEN",
        at = "2026-10-17T10:00:00.000Z",
        breadcrumbs = new[] { "route opened /caves/123", "click Save" },
        componentStack = (string?)null,
        userAgent = "Mozilla/5.0",
        viewport = "1280x800",
    };

    [Fact]
    public async Task A_signed_in_browser_s_errors_reach_the_log_with_their_identity_and_without_the_query()
    {
        var fingerprint = Guid.NewGuid().ToString("N")[..12];

        var response = await person.PostAsJsonAsync(
            "/api/v1/diagnostics/client-errors", new { errors = new[] { Error(fingerprint) } });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var line = logs.Lines.Single(l => l.Contains(fingerprint, StringComparison.Ordinal));
        line.ShouldStartWith("Warning");
        line.ShouldContain("x is undefined");
        line.ShouldContain("/caves/123");
        // The address's query can carry a share token; it never reaches the log line.
        line.ShouldNotContain("SECRET-TOKEN");

        var logged = logs.Events.Single(e => e.RenderMessage().Contains(fingerprint, StringComparison.Ordinal));
        logged.Properties.ShouldContainKey("ClientStack");
        logged.Properties.ShouldContainKey("UserId");
    }

    [Fact]
    public async Task Nobody_without_an_account_can_write_to_the_log()
    {
        var fingerprint = Guid.NewGuid().ToString("N")[..12];

        (await anonymous.PostAsJsonAsync(
                "/api/v1/diagnostics/client-errors", new { errors = new[] { Error(fingerprint) } }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        logs.Lines.ShouldNotContain(l => l.Contains(fingerprint, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_report_that_is_not_one_is_refused()
    {
        var tooMany = Enumerable.Range(0, ClientErrorBatchValidator.MaxErrors + 1)
            .Select(i => Error($"many{i}")).ToArray();
        var refused = new object[]
        {
            new { errors = Array.Empty<object>() },
            new { errors = tooMany },
            new { errors = new[] { Error("long", message: new string('m', 2_001)) } },
            new { errors = new[] { new { fingerprint = "k", kind = "invented", message = "m" } } },
        };

        foreach (var body in refused)
        {
            (await person.PostAsJsonAsync("/api/v1/diagnostics/client-errors", body))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A body past the size limit is refused before it is read.
        using var huge = new StringContent(
            "{\"errors\":[{\"fingerprint\":\"huge\",\"kind\":\"console\",\"message\":\""
                + new string('h', (int)ClientErrorEndpoints.MaxBodyBytes) + "\"}]}",
            Encoding.UTF8,
            "application/json");
        (await person.PostAsync("/api/v1/diagnostics/client-errors", huge))
            .StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_page_caught_in_a_loop_is_stopped_at_the_per_account_limit()
    {
        // A second account of its own, so the requests the other tests made do not count here.
        var other = $"cer-loop-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, other);
        using var looping = await AuthHelper.BearerClientAsync(factory, other);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= ClientErrorEndpoints.PerMinute; i++)
        {
            var response = await looping.PostAsJsonAsync(
                "/api/v1/diagnostics/client-errors", new { errors = new[] { Error($"loop{i}") } });
            statuses.Add(response.StatusCode);
        }

        statuses.Take(ClientErrorEndpoints.PerMinute).ShouldAllBe(s => s == HttpStatusCode.NoContent);
        statuses[^1].ShouldBe(HttpStatusCode.TooManyRequests);

        // The limit is the account's own: another signed-in person is still heard.
        (await person.PostAsJsonAsync(
                "/api/v1/diagnostics/client-errors", new { errors = new[] { Error("heard") } }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
