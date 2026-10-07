// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.Extensions.Primitives;
using Shouldly;
using SilexGis.Api.Common;

namespace SilexGis.Api.Tests;

/// <summary>
/// The three judgments behind the notice that the stated number of proxies may be too low: how
/// many forwarded entries a walk left unread, whether the address it arrived at looks like a proxy,
/// and how often the notice may be written.
/// </summary>
/// <remarks>
/// No host: these are the rules themselves. That the notice is actually written, by a running
/// application, for the shapes real deployments produce — and is silent for the right ones — is
/// asserted beside the tests of the walk it watches.
/// </remarks>
public class ProxyHopsCheckTests
{
    private static readonly DateTimeOffset Noon = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unread_entries_are_counted_across_lines_and_blanks_are_not_entries()
    {
        ProxyHopsCheck.UnreadEntries(StringValues.Empty).ShouldBe(0);
        ProxyHopsCheck.UnreadEntries(new StringValues("")).ShouldBe(0);
        ProxyHopsCheck.UnreadEntries(new StringValues(" , ")).ShouldBe(0);
        ProxyHopsCheck.UnreadEntries(new StringValues("203.0.113.10")).ShouldBe(1);
        ProxyHopsCheck.UnreadEntries(new StringValues("203.0.113.10, 198.51.100.4")).ShouldBe(2);
        // A header sent twice is two lines of the same list.
        ProxyHopsCheck.UnreadEntries(new StringValues(["203.0.113.10", "198.51.100.4, 198.51.100.5"])).ShouldBe(3);
    }

    [Fact]
    public void An_address_inside_a_trusted_network_looks_like_a_proxy_and_a_public_one_like_a_caller()
    {
        var shipped = TrustedProxies.Options(1, TrustedProxies.DefaultNetworks);

        ProxyHopsCheck.Suspicion(IPAddress.Parse("172.18.0.5"), shipped).ShouldBe(ProxySuspicion.NamedProxy);
        ProxyHopsCheck.Suspicion(IPAddress.Parse("127.0.0.1"), shipped).ShouldBe(ProxySuspicion.NamedProxy);
        ProxyHopsCheck.Suspicion(IPAddress.Parse("::1"), shipped).ShouldBe(ProxySuspicion.NamedProxy);
        // The same proxy seen through a listener bound on IPv6.
        ProxyHopsCheck.Suspicion(IPAddress.Parse("::ffff:172.18.0.5"), shipped).ShouldBe(ProxySuspicion.NamedProxy);

        ProxyHopsCheck.Suspicion(IPAddress.Parse("203.0.113.10"), shipped).ShouldBeNull();
        ProxyHopsCheck.Suspicion(IPAddress.Parse("2001:db8::1"), shipped).ShouldBeNull();
        ProxyHopsCheck.Suspicion(null, shipped).ShouldBeNull();
    }

    [Fact]
    public void Once_a_network_is_named_a_private_address_outside_the_list_is_the_other_kind_of_suspect()
    {
        // Naming any network replaces the built-in private ranges. The front on a public address
        // is then a proxy by the operator's own statement, and the packaged proxy on a private
        // one no longer is — which is the mistake the second wording of the notice is for.
        var named = TrustedProxies.Options(3, ["203.0.113.0/24"]);

        ProxyHopsCheck.Suspicion(IPAddress.Parse("203.0.113.50"), named).ShouldBe(ProxySuspicion.NamedProxy);
        ProxyHopsCheck.Suspicion(IPAddress.Parse("172.18.0.5"), named).ShouldBe(ProxySuspicion.UnnamedPrivateAddress);
        ProxyHopsCheck.Suspicion(IPAddress.Parse("198.51.100.1"), named).ShouldBeNull();
    }

    [Fact]
    public void The_notice_passes_once_and_then_not_again_until_its_interval_is_over()
    {
        var throttle = new ProxyHopsCheck.Throttle(ProxyHopsCheck.Interval);

        ProxyHopsCheck.Interval.ShouldBe(TimeSpan.FromHours(1));
        throttle.TryPass(Noon).ShouldBeTrue();
        throttle.TryPass(Noon).ShouldBeFalse();
        throttle.TryPass(Noon.AddMinutes(59)).ShouldBeFalse();
        throttle.TryPass(Noon.AddHours(1)).ShouldBeTrue();
        throttle.TryPass(Noon.AddHours(1).AddSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void Many_requests_arriving_together_write_one_notice()
    {
        var throttle = new ProxyHopsCheck.Throttle(ProxyHopsCheck.Interval);
        var passed = 0;

        Parallel.For(0, 64, _ =>
        {
            if (throttle.TryPass(Noon)) Interlocked.Increment(ref passed);
        });

        passed.ShouldBe(1);
    }
}
