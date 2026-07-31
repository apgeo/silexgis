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
public readonly record struct MessageResult(bool Sent, bool ChannelConfigured, string? Error)
{
    public static MessageResult Delivered() => new(true, true, null);

    public static MessageResult LoggedOnly() => new(true, false, null);

    public static MessageResult Failed(string error) => new(false, true, error);
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
}
