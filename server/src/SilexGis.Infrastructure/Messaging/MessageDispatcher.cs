// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Notifications;

namespace SilexGis.Infrastructure.Messaging;

/// <summary>
/// Turns a template key and a bag of values into an actual message on the right channel.
/// </summary>
/// <remarks>
/// The one place that knows how a catalogued message becomes text, so callers name what they are
/// sending rather than writing prose inline — which is what made the wording unconfigurable
/// before. Sending never throws: every caller is past the point of no return (the account exists,
/// the pending change is committed, the code is already valid) and a mail server that is refusing
/// connections must not turn that into a failed request. The result says what happened instead.
/// </remarks>
public sealed class MessageDispatcher(
    MessageTemplateStore templates,
    IEmailSender emailSender,
    IEmailDelivery emailDelivery,
    ISmsSender smsSender,
    ISmsDelivery smsDelivery,
    IConfiguration configuration,
    ILogger<MessageDispatcher> logger) : IMessageDispatcher
{
    public async Task<MessageResult> SendAsync(
        string templateKey,
        string recipient,
        string? locale,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        var (definition, body, subject) = await ComposeAsync(templateKey, locale, values, ct);

        // Exhaustive on purpose, and outside the try. A channel this method has no sender for is a
        // programming error rather than a delivery failure: reporting it as a failed send would put
        // it on the retry ladder forever, and an "or else" branch would quietly post a message meant
        // for one transport down another. Both switches must name every channel the enum has.
        var configured = definition.Channel switch
        {
            MessageChannel.Email => await emailDelivery.IsConfiguredAsync(ct),
            MessageChannel.Sms => await smsDelivery.IsConfiguredAsync(ct),
            _ => throw new NotSupportedException(UnknownChannel(definition.Channel)),
        };

        // What the carrier will bill for this hand-over, from the exact text that is about to
        // leave rather than from the wording it came out of: the recipient's own language decides
        // how many pieces it takes, and an operator may have rewritten that wording since. Taken
        // before the send so a hand-over the gateway then rejects reports the same amount — the
        // far side may well have sent it, and a charge is not undone by being told it failed.
        var segments = definition.Channel is MessageChannel.Sms ? TextMessageSegments.Count(body) : 0;

        try
        {
            switch (definition.Channel)
            {
                case MessageChannel.Email:
                    await emailSender.SendAsync(recipient, subject, body, ct);
                    break;

                case MessageChannel.Sms:
                    await smsSender.SendAsync(recipient, body, ct);
                    break;

                default:
                    throw new NotSupportedException(UnknownChannel(definition.Channel));
            }

            if (!configured)
            {
                // Which template went nowhere, beside the recipient and subject the logging sender
                // writes. Never the values or the text: they hold the links and codes.
                logger.LogInformation(
                    "Message {TemplateKey} on {Channel} was not delivered: the channel is not configured",
                    templateKey, definition.Channel);
            }

            return configured ? MessageResult.Delivered(segments) : MessageResult.LoggedOnly(segments);
        }
        catch (NotSupportedException)
        {
            // Never softened into a MessageResult: see above.
            throw;
        }
        catch (PermanentDeliveryException ex)
        {
            logger.LogWarning(
                "Could not send {TemplateKey} on {Channel}: refused for good ({Reason})",
                templateKey, definition.Channel, ex.Message);
            return MessageResult.FailedPermanently(ex.Message, segments);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send {TemplateKey} on {Channel}", templateKey, definition.Channel);
            return MessageResult.Failed(ex.Message, segments);
        }
    }

    public async Task<int> WeighAsync(
        string templateKey,
        MessageChannel channel,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        // Only the transport that bills by the piece has an answer here. Asked through the same
        // lookup the send uses, so what is weighed is the wording that would actually leave —
        // including the case where a message has a second, shorter form written for a phone.
        if (channel is not MessageChannel.Sms
            || MessageTemplateCatalog.On(templateKey, channel) is not { } wording)
        {
            return 0;
        }

        var most = 0;
        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            var (_, body, _) = await ComposeAsync(wording.Key, locale, values, ct);
            most = Math.Max(most, TextMessageSegments.Count(body));
        }

        return most;
    }

    public async Task<string> RenderSubjectAsync(
        string templateKey,
        string? locale,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        var (_, _, subject) = await ComposeAsync(templateKey, locale, values, ct);
        return subject;
    }

    /// <summary>
    /// Resolves a template — the operator's wording if they rewrote it, the shipped wording
    /// otherwise — and renders it. Shared so that a subject rendered for the daily summary is
    /// character-for-character the subject the standalone message would have carried.
    /// </summary>
    private async Task<(MessageTemplateDefinition Definition, string Body, string Subject)> ComposeAsync(
        string templateKey,
        string? locale,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        var definition = MessageTemplateCatalog.Find(templateKey)
            ?? throw new ArgumentException($"No message template named '{templateKey}'.", nameof(templateKey));

        var text = await templates.ResolveAsync(definition, locale, ct);

        // Every template may name the installation, and give its address, without each caller
        // having to pass them. The address matters most for a message sent straight through
        // rather than queued, such as the warning to a previous email address: only the queue's
        // sender used to supply it, so such a message rendered its link as nothing. A value a
        // caller did pass wins; the queue's sender reads the same setting, so the two agree.
        var withBranding = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["appName"] = InstanceName,
        };
        if (!withBranding.TryGetValue("siteUrl", out var siteUrl) || string.IsNullOrEmpty(siteUrl))
        {
            withBranding["siteUrl"] = NotificationLinks.SiteUrl(configuration);
        }

        return (
            definition,
            MessageTemplateRenderer.Tidy(MessageTemplateRenderer.Render(text.Body, withBranding)),
            MessageTemplateRenderer.Render(text.Subject ?? string.Empty, withBranding).Trim());
    }

    /// <summary>
    /// What the installation calls itself in the messages it sends. Read straight from
    /// configuration rather than through options because this is the only thing here that needs
    /// it, and it is a deployment fact rather than an administrator-editable setting.
    /// </summary>
    private string InstanceName =>
        configuration.GetValue("About:InstanceName", "SilexGIS") ?? "SilexGIS";

    private static string UnknownChannel(MessageChannel channel) =>
        $"No sender is wired up for the {channel} channel.";
}
