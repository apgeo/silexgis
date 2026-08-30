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
    /// Whether this installation has been given what this transport needs to carry anything.
    /// </summary>
    /// <remarks>
    /// A statement about the installation, and the third one the router asks alongside "can this
    /// wording travel here" and "can this recipient be reached here". It is separate from both
    /// because it has a different answer when it is false: a transport with no settings is not
    /// here at all, so it writes no delivery row, the same as a channel nobody implemented. A row
    /// would be worse than nothing on a transport that bills per message — the message goes only
    /// to the log, the row settles as sent, and a day's spending would count money nobody was
    /// ever asked for while the ceiling it is counted against refused the next announcement.
    /// <para>
    /// Not every transport answers this the same way, which is why it is asked of the transport.
    /// A message with nowhere to be posted is written to the log on purpose here — that is the
    /// documented mode for a small installation and it costs nothing — so a channel whose
    /// unconfigured behaviour is free says so by answering true.
    /// </para>
    /// </remarks>
    ValueTask<bool> IsUsableAsync(CancellationToken ct);

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
    /// Which cell of the preference matrix this channel is. What a recipient has chosen there is
    /// resolved once, by the router, so no channel gets to disagree about whose choice wins.
    /// </summary>
    NotificationChannelKind Kind { get; }

    /// <summary>
    /// Whether this channel would carry this notification to this recipient, and when, given what
    /// they have chosen for it.
    /// </summary>
    NotificationRoute Decide(SilexGisUser recipient, NotificationChannelChoice choice);

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
        Installed = All.Aggregate(NotificationChannelKind.InApp, (set, channel) => set | channel.Kind);
    }

    /// <summary>Every channel, in the enum's own order, so a fan-out is deterministic.</summary>
    public IReadOnlyList<INotificationChannel> All { get; }

    /// <summary>
    /// Every channel this installation has, as a preference-matrix set. In-app is always in it:
    /// it has no transport that could be missing. A delivery channel is in it only when something
    /// implements it, so a preference for a transport nobody wired up resolves to nothing rather
    /// than to messages that are never sent.
    /// </summary>
    public NotificationChannelKind Installed { get; }

    /// <summary>
    /// The channels here that charge the operator for every message and whose preference cell is
    /// in <paramref name="kinds"/> — what a day's spending is counted over.
    /// </summary>
    /// <remarks>
    /// Asked of the registered implementations rather than by mapping a delivery value back to a
    /// preference cell. The implementation already carries both halves, and the round trip through
    /// a value-to-cell table would be a second place the same pairing is written down — one that
    /// only the first charging transport ever executes, and therefore one whose first execution
    /// would be in production. Empty on an installation with nothing charging wired, which is the
    /// honest answer rather than a special case: a count over no channels is zero.
    /// </remarks>
    public IReadOnlyList<NotificationChannel> PaidFor(NotificationChannelKind kinds) =>
        [.. All
            .Where(channel => channel.Kind is not NotificationChannelKind.None
                && (NotificationChannelKinds.Paid & channel.Kind) == channel.Kind
                && (kinds & channel.Kind) == channel.Kind)
            .Select(channel => channel.Channel)];

    /// <summary>The implementation a delivery row names.</summary>
    public INotificationChannel Of(NotificationChannel channel) =>
        byChannel.TryGetValue(channel, out var found)
            ? found
            : throw new InvalidOperationException($"No notification channel is registered for {channel}.");
}
