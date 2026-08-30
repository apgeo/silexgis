// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The operator's test-send is a route that texts a number typed into a box. Its own class
/// because it needs a request budget tight enough to trip deliberately, which would starve any
/// other test sharing the factory.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminTestSendRateLimitTests : IAsyncLifetime, IDisposable
{
    /// <summary>Three requests obtain the bearer token; the fourth is the first test-send.</summary>
    private const string Budget = "4";

    private readonly SilexGisApiFactory factory;
    private string adminEmail = null!;

    public AdminTestSendRateLimitTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = Budget,
        });

    public async Task InitializeAsync()
    {
        adminEmail = $"testsend-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
    }

    [Fact]
    public async Task Test_texts_cannot_be_fired_off_in_a_loop()
    {
        // The abuse: one text per call to a number the caller supplies, on a route with nothing
        // between the request and the gateway. "May change the settings" is not "may text any
        // number in the world as fast as a script can ask", and each one costs the operator money.
        using var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var first = await SendAsync(admin);
        first.ShouldBe(HttpStatusCode.OK);

        var refused = false;
        for (var attempt = 0; attempt < 3 && !refused; attempt++)
        {
            refused = await SendAsync(admin) == HttpStatusCode.TooManyRequests;
        }

        refused.ShouldBeTrue("repeated test-sends were never refused");
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient admin) =>
        (await admin.PostAsJsonAsync(
            "/api/v1/admin/settings/sms/test", new { recipient = "+40712345678" })).StatusCode;

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
