// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// A refusal from a trip write path that carries the stable code a caller is answered with,
/// so the endpoint maps a code to a status rather than reading an exception message.
/// </summary>
public sealed class TripWriteException(string code, string message, bool denied = false)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;

    /// <summary>
    /// Whether the refusal is about what this caller is allowed to do rather than about what
    /// they asked for. The two answer with different statuses and the distinction is not
    /// recoverable from the code alone, so it travels with the refusal instead of being inferred
    /// from a list of codes that a later refusal would have to remember to join.
    /// </summary>
    public bool Denied { get; } = denied;
}
