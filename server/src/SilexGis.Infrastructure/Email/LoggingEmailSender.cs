// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Email;

/// <summary>
/// Fallback sender used when SMTP is not configured: logs the message so operators of
/// small installations can still complete flows (e.g. copy a reset link from the log).
/// Message bodies may contain secrets (reset tokens) — logged at Information deliberately,
/// matching the "no SMTP" operating mode; configure SMTP for anything internet-facing.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        LogEmail(logger, to, subject, body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "SMTP not configured — email not sent. To: {To} | Subject: {Subject} | Body: {Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
