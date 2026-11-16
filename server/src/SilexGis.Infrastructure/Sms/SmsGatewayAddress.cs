// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.Sms;

/// <summary>
/// Applies the internal-address rule to the SMS gateway: once when the address is saved, so the
/// administrator is told straight away, and again on every connection, because a name can
/// resolve somewhere else after it was saved.
/// </summary>
public static class SmsGatewayAddress
{
    private static readonly TimeSpan ResolveBudget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// True when <paramref name="host"/> is an internal address or resolves to one. A name that
    /// does not resolve now is not refused here — the connection check refuses it if it later
    /// resolves inside.
    /// </summary>
    public static async Task<bool> PointsInsideAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return MessagingEndpointRules.IsInternal(literal);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ResolveBudget);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, budget.Token);
            return addresses.Any(MessagingEndpointRules.IsInternal);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false;
        }
    }

    /// <summary>
    /// The gateway client's connection step: resolves the host itself, refuses when any address
    /// is internal, and connects to exactly the addresses it checked — so a second, different
    /// answer from the resolver cannot slip in between the check and the connection.
    /// </summary>
    public static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(MessagingEndpointRules.IsInternal))
        {
            throw new HttpRequestException(
                "The SMS gateway's address is on an internal network, which this installation does not allow.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
