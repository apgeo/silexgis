// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;

namespace SilexGis.Api.Common;

/// <summary>
/// Notices, and says in the log, when the address a request was counted under looks like one of
/// this installation's own reverse proxies rather than a caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this makes visible.</b> How many proxies stand in front of the API is a number an
/// operator states, and a number one too low fails silently: nothing errors, every page loads,
/// and every reader behind the outermost proxy is counted as that proxy — one sign-in budget and
/// one published-page budget for the whole internet, which the first busy evening spends for
/// everybody. The only symptom is refusals nobody can explain. The same happens when an operator
/// names a trusted network of their own and so replaces the built-in private ranges without
/// naming them again: the walk stops at the first proxy it no longer recognises.
/// </para>
/// <para>
/// <b>What it looks at.</b> After the forwarded-headers walk, two facts are left on the request:
/// the address the walk arrived at, and whatever entries of <c>X-Forwarded-For</c> it did not
/// read. Entries left over behind an address that is a proxy by the operator's own statement — or
/// that is a private or loopback address, which is where a packaged proxy speaks from — is the
/// shape of a walk that stopped one proxy short.
/// </para>
/// <para>
/// <b>Why it only says "may".</b> A caller who really is on a trusted network, and who writes an
/// <c>X-Forwarded-For</c> entry of their own, produces exactly the same two facts. The two cannot
/// be told apart from inside one request, so this is a prompt to look and never a finding, and
/// it is worded that way.
/// </para>
/// <para>
/// <b>Why it states a test before the remedy.</b> A third thing produces the same two facts: a
/// gateway that passes connections on under its own address and writes no header — a container
/// network's gateway is the usual one. The packaged web front then adds that gateway's address
/// after whatever the caller wrote, and the walk correctly stops on it. Counting such a gateway
/// as a proxy would be the one harmful answer: the walk would step past it onto the entry the
/// caller wrote, and every caller would then choose the address they are counted under. So the
/// message says what makes an address a hop — it adds the caller's address to the header itself —
/// before it names the setting, and says that for a gateway no number is right.
/// </para>
/// <para>
/// <b>What it does not do.</b> It changes nothing about the request: not its address, not its
/// headers, not which budget it is counted under. A diagnostic that also corrected what it saw
/// would be a second, unstated rule about whom to trust, running beside the stated one.
/// </para>
/// <para>
/// <b>What it writes.</b> The address the walk arrived at, the stated number of proxies, and how
/// many entries were left — a count. Never the entries themselves, which on a misconfigured
/// installation are its readers' addresses, and no path of its own: the path the logging
/// pipeline attaches to every event of a request reaches this one too, with a credential in it
/// replaced by its handle exactly as on every other event.
/// </para>
/// <para>
/// <b>At most once an hour.</b> When the count is wrong it is wrong for every request, and one
/// line an hour is enough to be found by somebody reading the log because readers are being
/// refused; a line per request would bury everything else in it.
/// </para>
/// </remarks>
public sealed class ProxyHopsCheck(
    RequestDelegate next,
    ForwardedHeadersOptions proxies,
    TimeProvider clock,
    ILogger<ProxyHopsCheck> logger)
{
    /// <summary>The shortest time between two of these warnings.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private static readonly IReadOnlyList<System.Net.IPNetwork> PrivateNetworks =
        [.. TrustedProxies.DefaultNetworks.Select(network => System.Net.IPNetwork.Parse(network))];

    private readonly Throttle throttle = new(Interval);

    // Each placeholder appears once in a message: the logging framework fills them by position,
    // so a name written twice would take two values.
    public Task InvokeAsync(HttpContext context)
    {
        var unread = UnreadEntries(context.Request.Headers["X-Forwarded-For"]);
        if (unread > 0
            && Suspicion(context.Connection.RemoteIpAddress, proxies) is { } suspicion
            && throttle.TryPass(clock.GetUtcNow()))
        {
            if (suspicion == ProxySuspicion.NamedProxy)
            {
                logger.LogWarning(
                    "The proxy count may be too low. A request was counted under {Address}, which is inside a "
                    + "network this installation trusts as its own reverse proxies, with forwarded addresses "
                    + "left unread behind it: {Unread}. First establish what that address is. If it is a reverse "
                    + "proxy of yours that adds the caller's address to X-Forwarded-For itself, there is one more "
                    + "proxy in front of this server than Proxy:Hops ({Hops}) says, and every reader behind it "
                    + "shares one request budget: raise SILEXGIS__Proxy__Hops to the number of proxies. If it "
                    + "is a gateway that passes connections on without writing that header (the gateway address "
                    + "of a container network is the usual one), it is not a proxy to count and no number fixes "
                    + "it: do not raise the setting, because counting it would let any caller write the address "
                    + "they are counted under. If it is a person on that network whose own software wrote the "
                    + "header, nothing is wrong. "
                    + "Nothing about how requests are counted has been changed by this notice, and it is "
                    + "written at most once an hour.",
                    context.Connection.RemoteIpAddress?.ToString(), unread, proxies.ForwardLimit);
            }
            else
            {
                logger.LogWarning(
                    "The list of trusted proxy networks may be missing one. A request was counted under "
                    + "{Address}, a private address that is not inside any network named under "
                    + "Proxy:TrustedNetworks, with forwarded addresses left unread behind it: {Unread}. Naming "
                    + "any network replaces the built-in private ranges, so first establish what that address is. If "
                    + "it is a reverse proxy of yours that adds the caller's address to X-Forwarded-For itself, "
                    + "its network has to be named too (SILEXGIS__Proxy__TrustedNetworks__0, __1, …) and "
                    + "counted in Proxy:Hops ({Hops}); until then every reader behind it shares one request "
                    + "budget. If it is a gateway that passes connections on without writing that header, it is "
                    + "not a proxy: do not name or count it, because that would let any caller write the address "
                    + "they are counted under. If it is a person on a private network whose own software wrote "
                    + "the header, nothing is wrong. Nothing about how requests are counted has been changed by this "
                    + "notice, and it is written at most once an hour.",
                    context.Connection.RemoteIpAddress?.ToString(), unread, proxies.ForwardLimit);
            }
        }

        return next(context);
    }

    /// <summary>
    /// How many entries of the forwarded header the walk left unread. The walk removes the header
    /// when it reads all of it and otherwise leaves what it did not read.
    /// </summary>
    public static int UnreadEntries(StringValues forwardedFor)
    {
        var count = 0;
        foreach (var line in forwardedFor)
        {
            if (line is null) continue;
            foreach (var entry in line.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(entry)) count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Whether the address a walk arrived at looks like a proxy, and by which of the two readings;
    /// nothing when it looks like a caller.
    /// </summary>
    public static ProxySuspicion? Suspicion(IPAddress? arrivedAt, ForwardedHeadersOptions proxies)
    {
        if (arrivedAt is null) return null;
        // A proxy speaking IPv4 to a listener bound on IPv6 arrives as a mapped address; the walk
        // itself compares the IPv4 form, so this does too.
        var address = arrivedAt.IsIPv4MappedToIPv6 ? arrivedAt.MapToIPv4() : arrivedAt;

        if (proxies.KnownProxies.Contains(address)
            || proxies.KnownIPNetworks.Any(network => network.Contains(address)))
        {
            return ProxySuspicion.NamedProxy;
        }

        return PrivateNetworks.Any(network => network.Contains(address))
            ? ProxySuspicion.UnnamedPrivateAddress
            : null;
    }

    /// <summary>
    /// Lets one caller through per interval, whichever thread asks first.
    /// </summary>
    public sealed class Throttle(TimeSpan interval)
    {
        // Ticks of the moment the next warning may be written; zero until the first.
        private long notBefore;

        public bool TryPass(DateTimeOffset now)
        {
            var due = Interlocked.Read(ref notBefore);
            return now.UtcTicks >= due
                && Interlocked.CompareExchange(ref notBefore, now.UtcTicks + interval.Ticks, due) == due;
        }
    }
}

/// <summary>Which reading makes an address look like a proxy rather than a caller.</summary>
public enum ProxySuspicion
{
    /// <summary>Inside a network the installation trusts as its own proxies.</summary>
    NamedProxy,

    /// <summary>A private or loopback address that the installation's own list no longer covers.</summary>
    UnnamedPrivateAddress,
}
