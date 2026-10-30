// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Email;

/// <summary>
/// Fallback sender used when SMTP is not configured: records that a message was due and to whom, and
/// nothing of what it said.
/// </summary>
/// <remarks>
/// The body is never written, and neither is the subject: a subject is operator-editable wording
/// that may carry the same placeholders as the body, and the shipped sign-in-code subject does carry
/// the code itself. Bodies carry the one thing a log must not hold — a reset link, a
/// confirmation link, an invitation, a sign-in code — and a log is the artefact that gets copied to
/// support threads and shipped in bundles. An operator who needs a link for somebody on an
/// installation without mail gets it from the administration screen, which hands an invitation
/// link over once in its response; configure SMTP for everything else.
/// </remarks>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        LogEmail(logger, to);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "SMTP not configured — email not sent (neither subject nor body is logged). To: {To}")]
    private static partial void LogEmail(ILogger logger, string to);
}
