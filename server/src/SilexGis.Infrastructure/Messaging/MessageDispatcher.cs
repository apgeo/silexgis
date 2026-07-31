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
        var definition = MessageTemplateCatalog.Find(templateKey)
            ?? throw new ArgumentException($"No message template named '{templateKey}'.", nameof(templateKey));

        var text = await templates.ResolveAsync(definition, locale, ct);

        // Every template may name the installation without each caller having to pass it.
        var withBranding = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["appName"] = InstanceName,
        };

        var body = MessageTemplateRenderer.Tidy(MessageTemplateRenderer.Render(text.Body, withBranding));
        var subject = MessageTemplateRenderer.Render(text.Subject ?? string.Empty, withBranding).Trim();

        var configured = definition.Channel == MessageChannel.Email
            ? await emailDelivery.IsConfiguredAsync(ct)
            : await smsDelivery.IsConfiguredAsync(ct);

        try
        {
            if (definition.Channel == MessageChannel.Email)
            {
                await emailSender.SendAsync(recipient, subject, body, ct);
            }
            else
            {
                await smsSender.SendAsync(recipient, body, ct);
            }

            return configured ? MessageResult.Delivered() : MessageResult.LoggedOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send {TemplateKey} on {Channel}", templateKey, definition.Channel);
            return MessageResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// What the installation calls itself in the messages it sends. Read straight from
    /// configuration rather than through options because this is the only thing here that needs
    /// it, and it is a deployment fact rather than an administrator-editable setting.
    /// </summary>
    private string InstanceName =>
        configuration.GetValue("About:InstanceName", "SilexGIS") ?? "SilexGIS";
}
