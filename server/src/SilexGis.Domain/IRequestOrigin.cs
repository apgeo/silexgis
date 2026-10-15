// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;

namespace SilexGis.Domain;

/// <summary>
/// Where the request being served came from, for the audit trail. Null outside a request — a
/// background job, a seeder, a command-line task — because nobody's address is behind those.
/// </summary>
/// <remarks>
/// The address is the caller's as the web host has established it: behind the installation's own
/// reverse proxies, the client those proxies forwarded rather than the proxy itself, and never an
/// address a caller merely claimed in a header that no trusted proxy wrote.
/// </remarks>
public interface IRequestOrigin
{
    IPAddress? ClientAddress { get; }
}
