// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>
/// Outcome of trying to send one message. Sending never throws at the caller: the flows that
/// send are already committed by the time the message goes out, and a mail server that is down
/// must not turn a successful registration into a 500.
/// </summary>
/// <param name="Sent">True when the channel accepted the message.</param>
/// <param name="ChannelConfigured">
/// False when nothing was configured and the message was only logged. Lets an endpoint answer
/// "no code is coming, ask an administrator" instead of leaving someone waiting.
/// </param>
/// <param name="Error">Why it failed, for the operator's log and the admin test-send button.</param>
/// <param name="Segments">
/// How many pieces the text handed over was split into, on a channel that bills by the piece —
/// what the carrier actually charges for this hand-over. Taken here because this is the last
/// place the rendered text exists: above it there is only a template key, and the number of
/// pieces depends on the recipient's language and on whatever the operator has rewritten the
/// wording to say. Zero when nothing was handed over that is billed this way, which includes
/// every channel that is not, so a caller can tell "nothing to charge" from any real amount.
/// </param>
public readonly record struct MessageResult(bool Sent, bool ChannelConfigured, string? Error, int Segments = 0)
{
    public static MessageResult Delivered(int segments = 0) => new(true, true, null, segments);

    public static MessageResult LoggedOnly(int segments = 0) => new(true, false, null, segments);

    public static MessageResult Failed(string error, int segments = 0) => new(false, true, error, segments);
}

/// <summary>
/// Renders a catalogued message in the recipient's language and hands it to the right channel.
/// The single place that knows a template key becomes actual text.
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>
    /// Sends the template identified by <paramref name="templateKey"/> to an address or number.
    /// The channel is the one the catalogue declares for that key.
    /// </summary>
    /// <param name="locale">Recipient's language; falls back to English when unsupported.</param>
    /// <param name="values">Placeholder values. Missing ones render as nothing.</param>
    Task<MessageResult> SendAsync(
        string templateKey,
        string recipient,
        string? locale,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default);

    /// <summary>
    /// The most pieces this message could cost on <paramref name="channel"/>, given the values
    /// that are already known — or zero when nothing that channel carries is billed that way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the guard that has to answer "would sending this take the day past what we will spend"
    /// before anything has been handed to a transport. It renders the same wording the send would
    /// render, through the same resolution — so an operator's rewrite is weighed rather than the
    /// shipped words — and counts the pieces a carrier would split it into.
    /// </para>
    /// <para>
    /// The recipient's language is the one thing still unknown at that moment, and it is the thing
    /// that decides the answer: the same sentence costs twice as much in a language whose marks
    /// fall outside the narrow alphabet. So every language the installation renders in is weighed
    /// and the largest answer is returned. That over-states an audience who all read the cheap
    /// language, which is the safe direction: the alternative is a ceiling that admits work it
    /// cannot pay for.
    /// </para>
    /// </remarks>
    Task<int> WeighAsync(
        string templateKey,
        MessageChannel channel,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default);

    /// <summary>
    /// Renders only a message's subject line, without sending anything.
    /// </summary>
    /// <remarks>
    /// The daily summary needs one line per event, in the recipient's language and editable by the
    /// operator — which is exactly what each event's own subject already is. Rendering it here
    /// rather than in the summary's own template avoids a second set of catalogue entries saying
    /// the same things, and keeps the branding and tidying rules in one place.
    /// </remarks>
    Task<string> RenderSubjectAsync(
        string templateKey,
        string? locale,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default);
}
