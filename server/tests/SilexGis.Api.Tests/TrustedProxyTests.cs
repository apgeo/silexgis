// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
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

    private SilexGisApiFactory Factory(
        string? hops = null, string? network = null, string? secondNetwork = null, LogCapture? logs = null)
    {
        // Only what a test states is configured; the rest is the shipped default, which is the
        // packaged stack's one hop over the private networks.
        var settings = new Dictionary<string, string?>
        {
            ["TripTracking:PublicRateLimitPerMinute"] = Budget,
        };
        if (hops is not null) settings["Proxy:Hops"] = hops;
        if (network is not null) settings["Proxy:TrustedNetworks:0"] = network;
        if (secondNetwork is not null) settings["Proxy:TrustedNetworks:1"] = secondNetwork;
        return logs is null
            ? new SilexGisApiFactory(postgres.ConnectionString, settings)
            : new SilexGisApiFactory(
                postgres.ConnectionString, settings, services => services.AddSingleton<ILogEventSink>(logs));
    }

    /// <summary>The notices about the proxy settings among everything a host logged.</summary>
    private static List<LogEvent> ProxyNotices(LogCapture logs) =>
        [.. logs.Events.Where(e => e.Level == LogEventLevel.Warning
            && e.MessageTemplate.Text.Contains("Proxy:Hops", StringComparison.Ordinal))];

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

    [Fact]
    public async Task Behind_three_proxies_on_mixed_networks_the_limiter_keys_on_the_reader()
    {
        // A site's own same-origin proxy on a public address, relaying for its readers, in front
        // of the TLS overlay's two: the reader, then that proxy appended by Caddy, then Caddy
        // appended by nginx. Three hops, and two kinds of network — the private one Compose puts
        // Caddy and nginx on, and the public one the site's proxy speaks from. With both named
        // and all three counted, each reader of the site has a budget of their own.
        var logs = new LogCapture();
        using var factory = Factory(hops: "3", network: "172.16.0.0/12", secondNetwork: "203.0.113.0/24", logs: logs);
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "198.51.100.1, 203.0.113.50, 172.18.0.5");

        (await ReadAsAsync(client, "198.51.100.2, 203.0.113.50, 172.18.0.5")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Three counted, three walked: nothing was left behind a proxy, so there is nothing to say.
        ProxyNotices(logs).ShouldBeEmpty();
    }

    [Fact]
    public async Task Naming_only_the_public_front_drops_the_private_proxies_and_every_reader_shares_one_budget()
    {
        // The mistake the three-hop shape invites. Naming any network replaces the built-in
        // private ranges, so an operator who names the site's proxy and nothing else has stopped
        // trusting Caddy: the walk stops there, however many hops are counted, and every reader
        // of the site is counted as Caddy. Nothing errors — which is why the application says so.
        var logs = new LogCapture();
        using var factory = Factory(hops: "3", network: "203.0.113.0/24", logs: logs);
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "198.51.100.1, 203.0.113.50, 172.18.0.5");

        (await ReadAsAsync(client, "198.51.100.2, 203.0.113.50, 172.18.0.5")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var notice = ProxyNotices(logs).ShouldHaveSingleItem();
        notice.RenderMessage().ShouldContain("Proxy:TrustedNetworks");
        notice.RenderMessage().ShouldContain("172.18.0.5");
        notice.RenderMessage().ShouldContain("may");
    }

    [Fact]
    public async Task A_hop_count_one_too_low_is_said_in_the_log_once_and_changes_nothing_about_the_count()
    {
        // The TLS overlay's shape with the count left at the packaged stack's one: the walk
        // stops at Caddy with the caller's entry unread behind it.
        var logs = new LogCapture();
        using var factory = Factory(logs: logs);
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "203.0.113.10, 10.9.9.9");

        // Diagnostic only: the second caller behind the same Caddy is still counted as Caddy and
        // still refused, exactly as before the notice existed.
        (await ReadAsAsync(client, "203.0.113.11, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Five requests, each with the same shape, and one notice: at most one an hour.
        var notice = ProxyNotices(logs).ShouldHaveSingleItem();
        var message = notice.RenderMessage();
        message.ShouldContain("may be too low");
        message.ShouldContain("10.9.9.9");
        message.ShouldContain("SILEXGIS__Proxy__Hops");
        notice.Properties["Hops"].ToString().ShouldBe("1");
        notice.Properties["Unread"].ToString().ShouldBe("1");

        // What it must not carry: the entries it did not read, which here are a reader's address,
        // and the token in the address that was asked for, which on this route is a credential.
        // Looked for in every property as well as in the message, because a structured sink
        // writes those too.
        var written = message + " " + string.Join(" ", notice.Properties.Select(p => p.Value.ToString()));
        written.ShouldNotContain("203.0.113.1");
        written.ShouldNotContain("not-a-real-token");

        // The notice writes no path of its own. The logging pipeline attaches the request's path
        // to every event written while a request is being served, this one included, and the only
        // form it may have here is the one with the token replaced by its handle.
        var scrubbed = Diagnostics.CredentialUrlScrubber.Scrub("/api/v1/public/trips/not-a-real-token")!;
        scrubbed.ShouldNotContain("not-a-real-token");
        written.Replace(scrubbed, string.Empty, StringComparison.Ordinal).ShouldNotContain("/public/trips");
    }

    [Fact]
    public async Task The_right_hop_count_says_nothing_and_the_same_requests_one_hop_short_do()
    {
        // Silence is only evidence beside a host that does speak: the same requests, to an
        // application told the right count and to one told one fewer.
        var right = new LogCapture();
        using (var factory = Factory(hops: "2", logs: right))
        using (var client = factory.CreateClient())
        {
            (await ReadAsAsync(client, "203.0.113.10, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadAsAsync(client, "203.0.113.11, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var short_ = new LogCapture();
        using (var factory = Factory(hops: "1", logs: short_))
        using (var client = factory.CreateClient())
        {
            (await ReadAsAsync(client, "203.0.113.10, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadAsAsync(client, "203.0.113.11, 10.9.9.9")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        ProxyNotices(right).ShouldBeEmpty();
        ProxyNotices(short_).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_caller_who_writes_an_entry_of_their_own_behind_one_proxy_is_not_mistaken_for_a_proxy()
    {
        // The packaged stack, one hop, and a caller on a public address whose own software wrote
        // an entry in front of the real one. Entries are left unread — but behind a caller, not
        // behind a proxy, so the notice has nothing to say. Without this the notice would fire on
        // every installation that has one corporate forward proxy among its readers.
        var logs = new LogCapture();
        using var factory = Factory(logs: logs);
        using var client = factory.CreateClient();

        (await ReadAsAsync(client, "192.168.1.20, 203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        ProxyNotices(logs).ShouldBeEmpty();
    }
}
