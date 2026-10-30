// SPDX-License-Identifier: AGPL-3.0-or-later
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
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
        message.To.Add(RecipientAddress(to));
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

        try
        {
            await client.SendAsync(message, ct);
        }
        catch (SmtpCommandException ex) when (RefusedForGood(ex))
        {
            // The server answered, and what it said about the recipient will be said again on
            // every attempt. Marked so the caller stops asking instead of retrying for hours.
            throw new PermanentDeliveryException(
                $"The mail server refused the recipient ({(int)ex.StatusCode}): {ex.Message}", ex);
        }

        await client.DisconnectAsync(quit: true, ct);
        logger.LogDebug("Sent mail to {Recipient} via {Host}", to, mail.Host);
    }

    /// <summary>
    /// An address MimeKit cannot read is refused here, before a connection is made, and is
    /// permanent for the same reason a server's "no such mailbox" is: the text will not change
    /// between attempts.
    /// </summary>
    private static MailboxAddress RecipientAddress(string to)
    {
        try
        {
            return MailboxAddress.Parse(to);
        }
        catch (Exception ex) when (ex is ParseException or FormatException)
        {
            throw new PermanentDeliveryException("The recipient address is not a valid email address.", ex);
        }
    }

    /// <summary>
    /// A 5xx answer to the recipient command that says something about <i>the address</i>:
    /// mailbox unknown, address malformed or not allowed. A 4xx answer to the same command is a
    /// "try later" (greylisting, a full queue) and is deliberately not matched — it is the
    /// ordinary retry ladder's case. Failures of the sender or of the message itself are not
    /// matched either: they say nothing about this address.
    /// </summary>
    /// <remarks>
    /// A 5xx at the recipient command can equally be about the installation: relaying denied
    /// because no login is configured, or a sending address on a blocklist (the 5.7.x class).
    /// Those are mended by the operator, after which the queued mail should still go out, so
    /// they stay on the retry ladder instead of being settled as dead on the first try.
    /// </remarks>
    private static bool RefusedForGood(SmtpCommandException ex) =>
        ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted && IsAddressRefusal((int)ex.StatusCode, ex.Message);

    /// <summary>Whether a 5xx answer to the recipient command is about the address rather than the setup.</summary>
    public static bool IsAddressRefusal(int statusCode, string? text)
    {
        if (statusCode < 500)
        {
            return false;
        }

        text ??= string.Empty;
        var enhanced = EnhancedStatus.Match(text);
        if (enhanced.Success)
        {
            // 5.1.x is the address class (bad mailbox, bad domain, bad syntax); every other
            // class (5.7.x policy and relaying, 5.2.x mailbox full, 5.4.x routing) can be mended
            // or can pass without anything about the address changing.
            return enhanced.Groups[1].Value == "1";
        }

        // No enhanced code: only the plain "no such mailbox" family counts, and not when the text
        // speaks of the installation's standing with the server.
        return statusCode is 550 or 551 or 553 && !SetupWords.IsMatch(text);
    }

    private static readonly System.Text.RegularExpressions.Regex EnhancedStatus =
        new(@"(?<!\d)5\.(\d{1,3})\.\d{1,3}(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SetupWords =
        new(
            "relay|denied|polic|block|spam|reput|listed|authent|not permitted|rejected",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
