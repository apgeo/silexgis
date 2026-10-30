// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;

namespace SilexGis.Api.Common;

/// <summary>
/// Which of the addresses in front of the API are its own reverse proxies, and therefore how far
/// back along <c>X-Forwarded-For</c> the caller's real address is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not "trust every hop".</b> Three things key on the caller's address: the sign-in
/// limiter, the printed-code limiter and the published-trip limiter. Each is sized per address —
/// sixty readers behind one connection, a script refused after sixty guesses. The address they see
/// is whatever the forwarded-headers middleware leaves in the connection, and that middleware, told
/// to trust any peer and to consume one entry, takes the <em>last</em> entry of the header. Behind
/// the packaged nginx alone that is the caller. Behind the TLS overlay it is Caddy, which nginx
/// appends as its own peer — so every reader on the internet shares one budget, a club's whole
/// readership is refused together once sixty of them open the article, and a stranger with no
/// link can spend that budget for everybody with garbage requests. Sign-in collapses the same way.
/// </para>
/// <para>
/// <b>What is trusted instead.</b> Two facts, both stated in configuration: the networks a proxy
/// may speak from, and how many proxies there are. The middleware walks the header from the right
/// and stops at the first address that is not a known proxy or once it has walked the stated
/// number of hops, whichever comes first. Both bounds are needed. Without the networks, a hop
/// count of two lets a caller behind nginx alone forge an address by writing one entry of their
/// own. Without the hop count, a caller whose own address happens to be private — an installation
/// reachable only on a club's LAN — would be walked past into whatever they wrote. So the hop
/// count is exact: one proxy for the packaged stack and for an nginx on the host, two under the
/// TLS overlay, which sets it itself.
/// </para>
/// <para>
/// The default networks are the loopback and private ranges, which is where Compose puts its
/// services and where a proxy on the same host speaks from. A front that speaks from a public
/// address — a CDN, a proxy on another machine — is named by an operator under
/// <c>Proxy:TrustedNetworks</c>, in CIDR notation, and counted under <c>Proxy:Hops</c>.
/// </para>
/// </remarks>
public static class TrustedProxies
{
    /// <summary>How many reverse proxies sit between the internet and the API, exactly.</summary>
    public const string HopsKey = "Proxy:Hops";

    /// <summary>The networks those proxies speak from, as CIDR blocks. Private and loopback unless told otherwise.</summary>
    public const string NetworksKey = "Proxy:TrustedNetworks";

    public const int DefaultHops = 1;

    public static readonly IReadOnlyList<string> DefaultNetworks =
    [
        "127.0.0.0/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "fc00::/7",
        "fe80::/10",
    ];

    /// <summary>Reads both settings and answers the middleware options that enforce them.</summary>
    public static ForwardedHeadersOptions Read(IConfiguration configuration)
    {
        var hops = configuration.GetValue(HopsKey, DefaultHops);
        // Blank entries are dropped before choosing: a list holding only blanks (a variable passed
        // through empty) is an unset list, and keeping it would replace the defaults with nothing
        // — which the middleware reads as "trust every sender".
        var named = (configuration.GetSection(NetworksKey).Get<string[]>() ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToArray();
        var networks = named.Length > 0 ? named : DefaultNetworks;
        return Options(hops, networks);
    }

    /// <summary>Stable code at the front of the start-up failure for a network that is not a CIDR block.</summary>
    public const string MalformedNetworkCode = "proxy.trusted_network_malformed";

    /// <summary>
    /// The middleware options for a hop count and a list of networks.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A network is not a CIDR block. The start-up failure begins with
    /// <see cref="MalformedNetworkCode"/> and names the setting and the entry.
    /// </exception>
    /// <remarks>
    /// A malformed block refuses the start rather than being skipped. Skipping it reads as "that is
    /// not a proxy", which looks safe and is not: the typo is in a list whose whole purpose is to
    /// say where the real proxy speaks from, so dropping it silently makes every caller key on the
    /// proxy's own address — one shared rate-limit budget for the installation — with nothing in
    /// the log to say why. The opposite reading, trusting everything, is worse. Neither guess is
    /// the operator's intention, so the process says what it could not read and does not start.
    /// A blank entry is not a typo but an unset value (a variable passed through empty), and is
    /// ignored.
    /// </remarks>
    public static ForwardedHeadersOptions Options(int hops, IEnumerable<string> networks)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // A hop count below one would mean "no proxy", which no shipped topology is; the
            // middleware would then consume nothing and every limiter would key on the proxy.
            ForwardLimit = Math.Max(1, hops),
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        var index = 0;
        foreach (var network in networks)
        {
            if (!string.IsNullOrWhiteSpace(network))
            {
                if (!System.Net.IPNetwork.TryParse(network.Trim(), out var parsed))
                {
                    throw new InvalidOperationException(
                        $"{MalformedNetworkCode}: {NetworksKey} "
                        + $"(SILEXGIS__Proxy__TrustedNetworks__{index.ToString(CultureInfo.InvariantCulture)}) "
                        + $"holds '{network.Trim()}', which is not a network in CIDR notation such as "
                        + "203.0.113.0/24 or 2001:db8::/32. The list says where this installation's own "
                        + "reverse proxies speak from, and an entry that cannot be read would leave one "
                        + "of them untrusted (every caller then shares the proxy's address) or, if guessed "
                        + "at, trust the wrong ones. Correct or remove the entry.");
                }

                options.KnownIPNetworks.Add(parsed);
            }

            index++;
        }

        return options;
    }
}
