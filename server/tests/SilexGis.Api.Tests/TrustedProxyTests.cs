// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// Which address the per-address limiters see when the API stands behind more than one proxy.
/// </summary>
/// <remarks>
/// <para>
/// The API always sits behind at least the packaged nginx, and under the TLS overlay behind Caddy
/// as well. Each proxy appends its peer to <c>X-Forwarded-For</c>, so the caller's own address is
/// as many entries from the end as there are proxies. The limiters key on whatever the
/// forwarded-headers middleware leaves as the connection's address, and the failure these tests
/// pin is the quiet one: taking the wrong entry does not error, it keys every reader on the
/// internet to the address of the outermost proxy, and the whole published surface, sign-in
/// included, then shares one budget that anybody can spend.
/// </para>
/// <para>
/// Driven on the published-trip route with a token that resolves to nothing, because that route
/// is anonymous and its limiter runs before the handler: a refused request costs a permit exactly
/// as a served one does. The test server has no peer address of its own, so the last entry of the
/// header stands in for the proxy that would be the peer in a real deployment.
/// </para>
/// </remarks>
public sealed class TrustedProxyTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;

    /// <summary>Small enough to exhaust in a handful of requests, one factory per test.</summary>
    private const string Budget = "3";

    public TrustedProxyTests(PostgresFixture postgres) => this.postgres = postgres;

    private SilexGisApiFactory Factory(string? hops = null, string? network = null)
    {
        // Only what a test states is configured; the rest is the shipped default, which is the
        // packaged stack's one hop over the private networks.
        var settings = new Dictionary<string, string?>
        {
            ["TripTracking:PublicRateLimitPerMinute"] = Budget,
        };
        if (hops is not null) settings["Proxy:Hops"] = hops;
        if (network is not null) settings["Proxy:TrustedNetworks:0"] = network;
        return new SilexGisApiFactory(postgres.ConnectionString, settings);
    }

    private static Task<HttpResponseMessage> ReadAsAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/trips/not-a-real-token");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    private static async Task ExhaustAsync(HttpClient client, string forwardedFor)
    {
        for (var i = 0; i < int.Parse(Budget); i++)
        {
            (await ReadAsAsync(client, forwardedFor)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await ReadAsAsync(client, forwardedFor)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Behind_two_proxies_the_limiter_keys_on_the_caller_and_not_on_the_outer_proxy()
    {
        // The TLS overlay's shape: the caller, then Caddy appended by nginx. Told there are two
        // hops, the API keys on the caller — so a second caller behind the same Caddy has a budget
        // of their own, and the whole readership of a club's article does not share one.
        using var factory = Factory(hops: "2");
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "203.0.113.10, 10.9.9.9");

        (await ReadAsAsync(client, "203.0.113.11, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_hop_count_is_exact_so_a_caller_cannot_buy_a_fresh_budget_by_writing_an_entry()
    {
        // One proxy, which is the packaged stack's default. A caller who writes an entry of their
        // own in front of the real one is not walked past the one hop: the forged entry buys
        // nothing, and the budget is still the caller's.
        using var factory = Factory();
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "203.0.113.10");

        (await ReadAsAsync(client, "198.51.100.1, 203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ReadAsAsync(client, "198.51.100.2, 203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_walk_stops_at_the_first_address_that_is_not_a_known_proxy_whatever_the_hop_count()
    {
        // Three hops allowed, but the entry before the last is a public address, which no proxy
        // of this installation speaks from. The walk stops there: what a caller wrote in front of
        // their own address is never reached, however generous the hop count.
        using var factory = Factory(hops: "3");
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "10.1.1.1, 203.0.113.50, 10.9.9.9");

        (await ReadAsAsync(client, "10.1.1.2, 203.0.113.50, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_front_on_a_public_address_is_walked_past_once_an_operator_names_its_network()
    {
        // A CDN or a proxy on another machine speaks from a public address. Named as a trusted
        // network and counted as a hop, it is walked past to the caller behind it.
        using var factory = Factory(hops: "2", network: "203.0.113.0/24");
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "198.51.100.1, 203.0.113.50");

        (await ReadAsAsync(client, "198.51.100.2, 203.0.113.50")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
