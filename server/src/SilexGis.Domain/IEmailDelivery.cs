// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Whether this installation can actually deliver mail.
/// </summary>
/// <remarks>
/// Separate from <see cref="IEmailSender"/> because it answers a question the settings UI asks
/// before anything is sent: an installation with no mail server configured still accepts
/// notification preferences, and the page has to say plainly that nothing will arrive rather
/// than let someone tune settings that silently do nothing.
/// </remarks>
public interface IEmailDelivery
{
    /// <summary>False when messages are only written to the log for the operator to read.</summary>
    bool IsConfigured { get; }
}
