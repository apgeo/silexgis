// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Sockets;

namespace SilexGis.Domain.Settings;

/// <summary>
/// What the mail and SMS settings may point at, and what a stored secret is tied to.
/// </summary>
/// <remarks>
/// <para>
/// A stored secret (the mail server's password, the SMS gateway's authorisation header) was typed
/// for one server. Whoever may edit the settings without knowing the secret must not be able to
/// point the settings somewhere else and have the installation hand the secret over there, so a
/// move to another mail host or port, or to another gateway address, drops the secret unless the
/// same save types one again.
/// </para>
/// <para>
/// The SMS gateway is a request the server makes to an address an administrator typed. Left open,
/// that address could be the cloud metadata service, the database, or any service on the
/// installation's own network, and the test button would report what answered. So an address on
/// this machine or on a private, shared, link-local or multicast range is internal and refused,
/// unless the installation's own configuration says its gateway lives there.
/// </para>
/// </remarks>
public static class MessagingEndpointRules
{
    /// <summary>The save dropped the stored mail password because the server moved.</summary>
    public const string MailPasswordClearedCode = "settings.mail_password_cleared";

    /// <summary>The save dropped the stored gateway authorisation header because the address moved.</summary>
    public const string SmsAuthHeaderClearedCode = "settings.sms_auth_header_cleared";

    /// <summary>The SMS gateway address is (or resolves to) an internal address.</summary>
    public const string SmsGatewayInternalCode = "settings.sms_gateway_internal";

    /// <summary>True when <paramref name="host"/>/<paramref name="port"/> name another mail server than the stored one.</summary>
    public static bool MailServerMoved(MailSettings current, string host, int port) =>
        !string.Equals(current.Host.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase)
        || current.Port != port;

    /// <summary>
    /// True when <paramref name="url"/> is another gateway address than the stored one. The whole
    /// address is compared: a template's path and query are part of where the request goes.
    /// </summary>
    public static bool SmsGatewayMoved(SmsSettings current, string url) =>
        !string.Equals(current.Url.Trim(), url.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// True for an address that is not somebody's service on the public internet: this machine,
    /// the private and shared (carrier-grade, also used by tailnets) ranges, link-local (where the
    /// cloud metadata service answers), unique-local, unspecified, broadcast and multicast.
    /// </summary>
    public static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && (b[1] & 0xC0) == 64)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();

            // Addresses that carry an IPv4 address inside them reach that IPv4 address: through a
            // NAT64 gateway (64:ff9b::/96), a 6to4 relay (2002::/16) or the old IPv4-compatible
            // form (::a.b.c.d). They are judged by the address they carry, or 64:ff9b::a9fe:a9fe
            // would be a way to the cloud metadata service that the IPv4 check never sees. The
            // local-use NAT64 prefix (64:ff9b:1::/48) is a network's own translator by definition.
            if (EmbeddedIPv4(b) is { } embedded)
            {
                return IsInternal(embedded);
            }

            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4] == 0x00 && b[5] == 0x01)
            {
                return true;
            }

            return address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC;
        }

        // Anything that is neither IPv4 nor IPv6 is not a gateway on the internet.
        return true;
    }

    /// <summary>
    /// The IPv4 address an IPv6 address carries under one of the translation prefixes, or null.
    /// The unspecified and loopback addresses share the IPv4-compatible prefix and are left to
    /// their own checks.
    /// </summary>
    private static IPAddress? EmbeddedIPv4(byte[] b)
    {
        // 64:ff9b::/96 — the well-known NAT64 prefix, the IPv4 address in the last four bytes.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        // 2002::/16 — 6to4, the IPv4 address in the two groups after the prefix.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return new IPAddress(b.AsSpan(2, 4));
        }

        // ::a.b.c.d — IPv4-compatible, deprecated but still parsed and routed by some stacks.
        if (b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0
            && b.AsSpan(12, 4).IndexOfAnyExcept((byte)0) >= 0
            && !(b[12] == 0 && b[13] == 0 && b[14] == 0 && b[15] == 1))
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        return null;
    }
}
