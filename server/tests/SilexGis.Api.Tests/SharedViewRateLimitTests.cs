// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// The per-address bound on the anonymous shared-view routes: the view itself and the two layers
/// it draws each spatial query from.
/// </summary>
/// <remarks>
/// Driven with a token that resolves to nothing, because the limiter runs before the handler: a
/// refused request costs a permit exactly as a served one does, so no shared view has to exist.
/// The test server has no peer address of its own, and with the packaged single proxy hop the one
/// <c>X-Forwarded-For</c> entry is the address the limiter keys on.
/// </remarks>
public sealed class SharedViewRateLimitTests : IClassFixture<PostgresFixture>
{
    private const int Budget = 3;

    private readonly PostgresFixture postgres;

    public SharedViewRateLimitTests(PostgresFixture postgres) => this.postgres = postgres;

    public static TheoryData<string> Routes() => new()
    {
        "/api/v1/shared/views/{0}",
        "/api/v1/shared/views/{0}/cave-entrances?west=22&south=46&east=23&north=47&zoom=10",
        "/api/v1/shared/views/{0}/features?west=22&south=46&east=23&north=47&zoom=10",
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Each_shared_view_route_is_bounded_per_address(string route)
    {
        using var factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { [SharedViewRateLimits.ConfigurationKey] = Budget.ToString() });
        using var client = factory.CreateClient();
        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, route, Guid.NewGuid());

        for (var i = 0; i < Budget; i++)
        {
            (await ReadAsAsync(client, url, "203.0.113.20")).StatusCode
                .ShouldNotBe(HttpStatusCode.TooManyRequests, $"request {i + 1} of {Budget}");
        }

        (await ReadAsAsync(client, url, "203.0.113.20")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // The bound is the address's, not the route's: another caller is still answered.
        (await ReadAsAsync(client, url, "203.0.113.21")).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    private static Task<HttpResponseMessage> ReadAsAsync(HttpClient client, string url, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }
}
