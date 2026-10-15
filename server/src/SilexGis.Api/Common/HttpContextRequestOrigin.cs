// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;

namespace SilexGis.Api.Common;

/// <summary>
/// The client address of the current request, read after the forwarded-headers middleware has
/// replaced the proxy's address with the caller's — so it honours exactly the proxies the
/// installation trusts, and no others.
/// </summary>
public sealed class HttpContextRequestOrigin(IHttpContextAccessor accessor) : SilexGis.Domain.IRequestOrigin
{
    public IPAddress? ClientAddress
    {
        get
        {
            var address = accessor.HttpContext?.Connection.RemoteIpAddress;
            // A dual-stack listener reports an IPv4 caller as ::ffff:a.b.c.d; recorded as the
            // plain IPv4 address so one caller is written down one way.
            return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
        }
    }
}
