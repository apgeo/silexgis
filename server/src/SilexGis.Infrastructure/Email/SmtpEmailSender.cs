// SPDX-License-Identifier: AGPL-3.0-or-later
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using SilexGis.Domain;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.Email;

/// <summary>
/// Delivers mail through the configured SMTP server, falling back to the log when there is none.
/// </summary>
/// <remarks>
/// <para>
/// A connection per message. Installations of this size send a handful of messages a day, and a
/// pooled connection would have to survive an idle timeout that every server sets differently —
/// the reconnect logic would cost more than it saves.
/// </para>
/// <para>
/// Failures throw. Callers that are mid-flow catch and carry on (the account is already created,
/// the reset token is already issued), while the administrator's test-send wants the message.
/// </para>
/// </remarks>
public sealed class SmtpEmailSender(
    IAppSettingsService settings,
    LoggingEmailSender fallback,
    ILogger<SmtpEmailSender> logger) : IEmailSender, IEmailDelivery
{
    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        (await settings.GetMailAsync(ct)).IsUsable;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var mail = await settings.GetMailAsync(ct);
        if (!mail.IsUsable)
        {
            await fallback.SendAsync(to, subject, body, ct);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(mail.FromName, mail.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        if (!string.IsNullOrWhiteSpace(mail.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(mail.ReplyTo));
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = mail.TimeoutSeconds * 1000 };
        if (mail.AcceptInvalidCertificate)
        {
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        await client.ConnectAsync(mail.Host, mail.Port, SecurityFor(mail.Security), ct);
        if (!string.IsNullOrWhiteSpace(mail.Username))
        {
            await client.AuthenticateAsync(mail.Username, mail.Password ?? string.Empty, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
        logger.LogDebug("Sent mail to {Recipient} via {Host}", to, mail.Host);
    }

    private static SecureSocketOptions SecurityFor(MailTransportSecurity security) => security switch
    {
        MailTransportSecurity.None => SecureSocketOptions.None,
        MailTransportSecurity.StartTls => SecureSocketOptions.StartTls,
        MailTransportSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        // Auto: upgrade when the server advertises it, and treat 465 as implicit TLS the way
        // every mail client does — the port is not registered for anything else.
        _ => SecureSocketOptions.Auto,
    };
}
