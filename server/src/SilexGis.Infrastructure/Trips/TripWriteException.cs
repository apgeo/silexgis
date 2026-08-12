// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// A refusal from a trip write path that carries the stable code a caller is answered with,
/// so the endpoint maps a code to a status rather than reading an exception message.
/// </summary>
public sealed class TripWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
