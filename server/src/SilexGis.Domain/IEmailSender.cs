// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Outbound email seam (password reset, future confirmations). A real SMTP implementation
/// is configuration-gated (06-deployment.md §3 Smtp); without it, a logging no-op is used.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
