// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Notifications leaving by text message — the one channel that charges per message.
/// </summary>
/// <remarks>
/// <para>
/// Everything text-specific about routing a notification is here and nowhere else: that a message
/// has to have been written for a phone before it can travel this way, that a number counts as an
/// address only once somebody has proved it is theirs, and which cell of the preference matrix
/// governs it. Nothing outside this file learns that a second transport exists.
/// </para>
/// <para>
/// <b>An unverified number is not an address.</b> An account carries a live number and a number
/// awaiting confirmation, and only the live one is ever proved: it takes a new value only after a
/// code texted to that value comes back. So the pending number is somebody's unchecked typing —
/// possibly a stranger's phone, possibly a typo — and is a destination for exactly one message,
/// the code that proves it. Reading it here would text a roster's business to whoever really owns
/// that number, at the installation's expense, and no later check would catch it because by then
/// the message has left.
/// </para>
/// <para>
/// <b>What travels this way says less than the mailbox copy of it.</b> A text leaves the
/// installation and obeys none of its rules afterwards: it does not expire, it cannot be
/// withdrawn, and nothing re-checks the reader's access at the moment they look at it. That is a
/// property of the wording, not of this class — the catalogue holds a separate form of any message
/// that can travel both ways, and this asks for it by transport rather than by name.
/// </para>
/// </remarks>
public sealed class SmsNotificationChannel(IMessageDispatcher dispatcher, ISmsDelivery gateway)
    : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <summary>
    /// Whether this installation has a gateway to hand a text to.
    /// </summary>
    /// <remarks>
    /// Being implemented is not being installed, and on the one transport that bills per message
    /// the difference is money. With no gateway the message goes to the log — free, and invisible
    /// to whoever it was addressed to — while a row for it would settle as sent and be counted
    /// as a day's spending, so an installation with the switch on and no gateway would refuse
    /// announcements once it had "spent" a ceiling on messages nobody was ever billed for. Every
    /// other surface already reads this same answer to say the channel is unavailable; without it
    /// here, routing would be the only part of the system disagreeing.
    /// </remarks>
    public ValueTask<bool> IsUsableAsync(CancellationToken ct) => gateway.IsConfiguredAsync(ct);

    /// <summary>
    /// Whether the message this wording belongs to has a form written for a phone.
    /// </summary>
    /// <remarks>
    /// A statement about the message and never about the recipient. The catalogue answers it, and
    /// answers the send with the same lookup, so what this agrees to carry and what actually gets
    /// rendered cannot come apart. A message with no text form is simply not carried this way and
    /// produces no row at all — it is not a fault, because the mailbox copy of it is going out.
    /// </remarks>
    public bool Carries(MessageTemplateDefinition template) =>
        MessageTemplateCatalog.On(template.Key, MessageChannel.Sms) is not null;

    /// <summary>
    /// Whether this account has a number it has proved is its own.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing and neither implies the other. The confirmed flag alone can
    /// stand over a number that was afterwards removed; a number alone can be one nobody has
    /// answered a code on. Only the pair means an address.
    /// </remarks>
    public bool CanReach(SilexGisUser recipient) =>
        recipient.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(recipient.PhoneNumber);

    public NotificationChannelKind Kind => NotificationChannelKind.Sms;

    public NotificationRoute Decide(SilexGisUser recipient, NotificationChannelChoice choice) =>
        NotificationRouting.Decide(choice, CanReach(recipient));

    public Task<MessageResult> SendAsync(
        SilexGisUser recipient,
        string templateKey,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct) =>
        MessageTemplateCatalog.On(templateKey, MessageChannel.Sms) is { } wording
            ? dispatcher.SendAsync(wording.Key, recipient.PhoneNumber!, recipient.Locale, values, ct)
            // Unreachable while the catalogue is the same one that agreed to carry this, and
            // reported rather than thrown so that a catalogue edited between the two answers shows
            // up in the operator's view of failed deliveries instead of stopping the whole pass.
            : Task.FromResult(MessageResult.Failed($"No text message wording for '{templateKey}'."));
}
