// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// One way a notification leaves this installation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A channel is something that leaves the system.</b> Email does; text message, WhatsApp and
/// push would. In-app does not — the notification row's own existence is its in-app presence,
/// written in the producer's transaction with no address and no failure mode — so there is
/// deliberately no in-app implementation of this and never will be. That is the first useful thing
/// this interface says, and it is why an operator's view of what is going wrong is exactly a view
/// over deliveries.
/// </para>
/// <para>
/// Everything transport-shaped about a notification lives behind one of these: whether the wording
/// can travel this way at all, whether this recipient can be reached here, whether they want it
/// here and when, and how it is handed over. Routing asks every registered channel and writes one
/// delivery row per answer that is not a refusal, so adding a transport is adding an
/// implementation and a value on <see cref="NotificationChannel"/> — never another branch in the
/// router.
/// </para>
/// <para>
/// In Infrastructure rather than Domain because a channel reads the recipient's account row to
/// find an address and a preference, and reading user rows is exactly what this layer exists to
/// do on a feature slice's behalf. The decisions themselves stay pure and live in Domain.
/// </para>
/// </remarks>
public interface INotificationChannel
{
    /// <summary>
    /// Which channel this is. One implementation per value, and the value is what the delivery row
    /// carries, so a row always names the thing that will try to send it.
    /// </summary>
    NotificationChannel Channel { get; }

    /// <summary>
    /// Whether this channel can carry this wording at all.
    /// </summary>
    /// <remarks>
    /// A template is written for one transport — an email has a subject and a text message does
    /// not — so refusing here is a statement about the message, never about the recipient. The
    /// router keeps the two apart deliberately: a notification nothing is willing to carry is a
    /// fault an operator has to be able to see, whereas a recipient who wants no email is not a
    /// fault at all and produces no row.
    /// </remarks>
    bool Carries(MessageTemplateDefinition template);

    /// <summary>
    /// Whether there is anywhere to send to on this channel right now.
    /// </summary>
    /// <remarks>
    /// Asked twice, at routing and again at the send, because an address can be removed in
    /// between — and a delivery to an account that no longer has one is dead rather than retried,
    /// since no number of attempts will conjure an address back.
    /// </remarks>
    bool CanReach(SilexGisUser recipient);

    /// <summary>
    /// Whether this channel would carry this notification to this recipient, and when. Reads the
    /// recipient's own settings, which are per channel.
    /// </summary>
    NotificationRoute Decide(SilexGisUser recipient, NotificationCategory category, bool categoryEnabled);

    /// <summary>Hands one message to whoever carries it out of the system.</summary>
    Task<MessageResult> SendAsync(
        SilexGisUser recipient,
        string templateKey,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct);
}

/// <summary>
/// The registered channels, one per value of <see cref="NotificationChannel"/>.
/// </summary>
/// <remarks>
/// Fully populated by construction, and loud when it is not: a channel a delivery row can name but
/// nothing implements is a wiring error, and it surfaces here rather than as a row that is claimed
/// on every poll and never sent. Two implementations claiming the same channel fail the same way,
/// which is what keeps the one-delivery-per-channel index honest before the database has to.
/// </remarks>
public sealed class NotificationChannels
{
    private readonly Dictionary<NotificationChannel, INotificationChannel> byChannel;

    public NotificationChannels(IEnumerable<INotificationChannel> channels)
    {
        byChannel = channels.ToDictionary(channel => channel.Channel);
        All = [.. byChannel.Values.OrderBy(channel => channel.Channel)];
    }

    /// <summary>Every channel, in the enum's own order, so a fan-out is deterministic.</summary>
    public IReadOnlyList<INotificationChannel> All { get; }

    /// <summary>The implementation a delivery row names.</summary>
    public INotificationChannel Of(NotificationChannel channel) =>
        byChannel.TryGetValue(channel, out var found)
            ? found
            : throw new InvalidOperationException($"No notification channel is registered for {channel}.");
}
