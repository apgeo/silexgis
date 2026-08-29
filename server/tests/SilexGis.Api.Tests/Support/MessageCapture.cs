// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Api.Tests.Support;

/// <summary>One message the application tried to send.</summary>
public sealed record SentMessage(string Channel, string Recipient, string? Subject, string Body)
{
    /// <summary>
    /// The six-digit code in the body. Read out of the rendered text rather than intercepted
    /// earlier, so a test that finds it has proved the template really carried it.
    /// </summary>
    public string Code =>
        Regex.Match(Body, @"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } match
            ? match.Value
            : throw new InvalidOperationException($"No six-digit code in: {Body}");

    /// <summary>The confirmation or reset link in the body.</summary>
    public Uri Link =>
        Regex.Match(Body, @"https?://\S+", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } match
            ? new Uri(match.Value)
            : throw new InvalidOperationException($"No link in: {Body}");
}

/// <summary>
/// Collects everything the application sends, in order.
/// </summary>
/// <remarks>
/// Substituted for the two transports rather than for the dispatcher, so the templates, the
/// language selection and the placeholder substitution all still run — a test asserting on a code
/// is asserting on the message a person would actually receive.
/// </remarks>
public sealed class MessageCapture
{
    private readonly List<SentMessage> messages = [];
    private readonly Lock gate = new();

    /// <summary>Whether the fake transports report themselves as configured.</summary>
    public bool MailConfigured { get; set; } = true;

    public bool SmsConfigured { get; set; } = true;

    /// <summary>Makes the next send throw, standing in for a mail server that is refusing.</summary>
    public bool FailNextSend { get; set; }

    /// <summary>
    /// An address every send to which throws, until it is cleared.
    /// </summary>
    /// <remarks>
    /// <see cref="FailNextSend"/> cannot be aimed. A drain is not scoped to a recipient, so it
    /// settles whatever else is due in the shared database at that moment — and a one-shot failure
    /// then lands on whichever message the pass happened to reach first, which may be somebody
    /// else's entirely. A test that needs *its own* send to fail names the address instead.
    /// </remarks>
    public string? FailSendsTo { get; set; }

    public IReadOnlyList<SentMessage> Messages
    {
        get
        {
            lock (gate)
            {
                return [.. messages];
            }
        }
    }

    public SentMessage Last
    {
        get
        {
            lock (gate)
            {
                return messages.Count > 0
                    ? messages[^1]
                    : throw new InvalidOperationException("No message was sent.");
            }
        }
    }

    public SentMessage LastTo(string recipient)
    {
        lock (gate)
        {
            return messages.LastOrDefault(m => string.Equals(m.Recipient, recipient, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Nothing was sent to {recipient}. Sent: {string.Join(", ", messages.Select(m => m.Recipient))}");
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            messages.Clear();
        }
    }

    internal void Record(string channel, string recipient, string? subject, string body)
    {
        if (FailNextSend)
        {
            FailNextSend = false;
            throw new InvalidOperationException("Simulated delivery failure.");
        }

        if (FailSendsTo is { } address && string.Equals(recipient, address, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulated delivery failure.");
        }

        lock (gate)
        {
            messages.Add(new SentMessage(channel, recipient, subject, body));
        }
    }
}
