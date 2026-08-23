// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Notifications leaving by email — the one channel this installation sends notifications on.
/// </summary>
/// <remarks>
/// Everything email-specific about routing a notification is here and nowhere else: that an email
/// needs an address, which cell of the preference matrix governs it, and that only email wording
/// can travel this way. The router knows none of it, which is what lets a second transport be an
/// added file rather than an edited one.
/// </remarks>
public sealed class EmailNotificationChannel(IMessageDispatcher dispatcher) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool Carries(MessageTemplateDefinition template) => template.Channel == MessageChannel.Email;

    public bool CanReach(SilexGisUser recipient) => !string.IsNullOrWhiteSpace(recipient.Email);

    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    public NotificationRoute Decide(SilexGisUser recipient, NotificationChannelChoice choice) =>
        NotificationRouting.Decide(choice, CanReach(recipient));

    public Task<MessageResult> SendAsync(
        SilexGisUser recipient,
        string templateKey,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct) =>
        dispatcher.SendAsync(templateKey, recipient.Email!, recipient.Locale, values, ct);
}
