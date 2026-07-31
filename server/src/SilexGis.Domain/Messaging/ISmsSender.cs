// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>
/// Outbound SMS seam, mirroring <see cref="IEmailSender"/>. Implementations talk to whatever
/// gateway the operator configured; without one, a logging no-op stands in.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string to, string text, CancellationToken ct = default);
}

/// <summary>
/// Whether this installation can actually deliver text messages, asked before anything is sent
/// so a settings page can say plainly that a channel is inert rather than let someone switch on
/// a second factor whose codes would never arrive.
/// </summary>
public interface ISmsDelivery
{
    /// <summary>False when messages are only written to the log for the operator to read.</summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);
}
