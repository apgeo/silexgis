// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;

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

            return configured ? MessageResult.Delivered() : MessageResult.LoggedOnly();
        }
        catch (NotSupportedException)
        {
            // Never softened into a MessageResult: see above.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send {TemplateKey} on {Channel}", templateKey, definition.Channel);
            return MessageResult.Failed(ex.Message);
        }
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

        // Every template may name the installation without each caller having to pass it.
        var withBranding = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["appName"] = InstanceName,
        };

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
