// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.ResLinks;

/// <summary>A text-range anchor's payload, read into the fields the rules reason about.</summary>
/// <param name="Start">Offset into the text stream the anchor was measured against.</param>
/// <param name="End">Exclusive end of the same.</param>
/// <param name="Quote">The words themselves — what survives the offsets going stale.</param>
/// <param name="Prefix">Characters immediately before the quote, when the author's client
/// recorded them. What tells two occurrences of the same sentence apart.</param>
/// <param name="Suffix">Characters immediately after, for the same reason.</param>
/// <param name="Page">Page the quote was on, for paged formats. Carried through untouched.</param>
public readonly record struct TextRangeAnchor(
    int Start, int End, string Quote, string? Prefix, string? Suffix, int? Page);

/// <summary>Where a quote landed in a new stream, and how sure that is.</summary>
public enum ReanchorOutcome
{
    /// <summary>The quote is where it was — the text around the passage did not move.</summary>
    Unmoved,

    /// <summary>Found elsewhere, unambiguously or by its recorded context.</summary>
    Moved,

    /// <summary>The words are not in the new stream. The anchor is left as it was written.</summary>
    Lost,
}

/// <summary>The result of re-measuring one anchor against a new stream.</summary>
public readonly record struct ReanchorResult(ReanchorOutcome Outcome, int Start, int End);

/// <summary>
/// Re-measuring text-range anchors after the text underneath them changes.
///
/// <para>
/// This is what stops an edit from being a quiet act of vandalism. Offsets into a character
/// stream are only meaningful against the stream they were taken from: insert a sentence in the
/// first paragraph and every anchor below it now points a few characters early — still
/// resolving, still rendering, still confidently highlighting the wrong words. Nothing about
/// that failure is visible to the person who made the edit or to the next person to read the
/// document, which is why the quote is stored alongside the offsets in the first place.
/// </para>
///
/// <para>
/// The quote is the anchor; the offsets are a cache of where it was last seen. So the rule is:
/// find the words again, and if they are found, the offsets are whatever they are now. Where
/// the same words occur more than once the recorded context decides — the characters that were
/// immediately before and after the passage when it was authored — and where the context does
/// not decide either, the occurrence nearest to where the anchor used to be wins, because text
/// that moves usually moves a little.
/// </para>
///
/// <para>
/// <b>Not finding the quote is not a failure to be papered over.</b> The passage really is gone,
/// and the honest outcomes are to leave the anchor exactly as its author wrote it and let it
/// read as degraded. Guessing — nearest paragraph, fuzziest match, clamp to the end of the
/// stream — would convert "this link pointed at something that no longer exists" into "this link
/// points at this other sentence", which is the one outcome a reader cannot detect.
/// </para>
///
/// <para>
/// Written against a plain stream and a plain anchor rather than against the annotated-text
/// format, because the same problem arrives with every re-extraction: a document re-scanned at a
/// better resolution, a converter improved, a word-processor file re-read by a newer reader. The
/// caller supplies the stream; this decides where the words went.
/// </para>
/// </summary>
public static class TextAnchorReanchoring
{
    /// <summary>
    /// How much context on each side an author's client should record, and how much of it is
    /// compared. Long enough to separate two occurrences of an ordinary sentence, short enough
    /// that an edit just outside the passage does not make the context stop matching.
    /// </summary>
    public const int ContextLength = 32;

    /// <summary>
    /// Reads a stored text-range payload into its fields, or null when the payload is not one.
    /// Defensive by design: payloads are free-form JSON written by whatever client wrote them.
    /// </summary>
    public static TextRangeAnchor? Read(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("start", out var startElement)
                || !root.TryGetProperty("end", out var endElement)
                || !root.TryGetProperty("quote", out var quoteElement)
                || startElement.ValueKind != JsonValueKind.Number
                || endElement.ValueKind != JsonValueKind.Number
                || quoteElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!startElement.TryGetInt32(out var start) || !endElement.TryGetInt32(out var end))
            {
                return null;
            }

            var quote = quoteElement.GetString();
            if (string.IsNullOrEmpty(quote))
            {
                return null;
            }

            return new TextRangeAnchor(
                start,
                end,
                quote,
                OptionalString(root, "prefix"),
                OptionalString(root, "suffix"),
                OptionalInt(root, "page"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The payload as it is stored, with the offsets replaced and everything else the
    /// author wrote carried through unchanged.</summary>
    public static string Write(TextRangeAnchor anchor, int start, int end)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("start", start);
            writer.WriteNumber("end", end);
            writer.WriteString("quote", anchor.Quote);
            if (anchor.Prefix is not null)
            {
                writer.WriteString("prefix", anchor.Prefix);
            }

            if (anchor.Suffix is not null)
            {
                writer.WriteString("suffix", anchor.Suffix);
            }

            if (anchor.Page is not null)
            {
                writer.WriteNumber("page", anchor.Page.Value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Where this anchor's words are in <paramref name="stream"/> now.
    /// </summary>
    public static ReanchorResult Locate(TextRangeAnchor anchor, string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var quote = anchor.Quote;
        if (quote.Length == 0 || quote.Length > stream.Length)
        {
            return new ReanchorResult(ReanchorOutcome.Lost, anchor.Start, anchor.End);
        }

        // The overwhelmingly common case, checked first and exactly: nothing above this
        // passage changed, so the offsets still name it. Worth its own branch because a body
        // of a few thousand anchors is otherwise a few thousand scans of the whole stream.
        if (anchor.Start >= 0
            && anchor.End == anchor.Start + quote.Length
            && anchor.End <= stream.Length
            && string.CompareOrdinal(stream, anchor.Start, quote, 0, quote.Length) == 0)
        {
            return new ReanchorResult(ReanchorOutcome.Unmoved, anchor.Start, anchor.End);
        }

        var best = -1;
        var bestScore = int.MinValue;
        var at = stream.IndexOf(quote, StringComparison.Ordinal);
        while (at >= 0)
        {
            var score = ContextScore(anchor, stream, at);
            // Ties go to the occurrence nearest where the anchor used to be. Strictly greater
            // keeps the first such occurrence when even the distances tie, so the answer does
            // not depend on which direction the scan happened to run.
            if (score > bestScore)
            {
                bestScore = score;
                best = at;
            }

            at = stream.IndexOf(quote, at + 1, StringComparison.Ordinal);
        }

        if (best < 0)
        {
            return new ReanchorResult(ReanchorOutcome.Lost, anchor.Start, anchor.End);
        }

        return new ReanchorResult(ReanchorOutcome.Moved, best, best + quote.Length);
    }

    /// <summary>
    /// How well an occurrence matches what the author recorded around the passage. Context
    /// agreement dominates — it is evidence about *this* passage — and distance from the old
    /// position only separates occurrences the context could not.
    /// </summary>
    private static int ContextScore(TextRangeAnchor anchor, string stream, int at)
    {
        var score = 0;
        if (!string.IsNullOrEmpty(anchor.Prefix))
        {
            score += CommonSuffixLength(stream.AsSpan(0, at), anchor.Prefix.AsSpan());
        }

        if (!string.IsNullOrEmpty(anchor.Suffix))
        {
            var afterAt = at + anchor.Quote.Length;
            score += CommonPrefixLength(stream.AsSpan(afterAt), anchor.Suffix.AsSpan());
        }

        // Scaled so that no distance can outweigh a single character of agreeing context, and
        // so a nearer occurrence still beats a farther one when there is no context at all.
        var distance = Math.Abs(at - anchor.Start);
        return (score * (ContextLength * 4)) - Math.Min(distance, (ContextLength * 4) - 1);
    }

    /// <summary>Characters the end of <paramref name="before"/> shares with the end of the
    /// recorded prefix — the prefix is what ran up to the quote, so they are compared
    /// backwards from the passage.</summary>
    private static int CommonSuffixLength(ReadOnlySpan<char> before, ReadOnlySpan<char> prefix)
    {
        var n = 0;
        while (n < before.Length && n < prefix.Length && before[^(n + 1)] == prefix[^(n + 1)])
        {
            n++;
        }

        return n;
    }

    private static int CommonPrefixLength(ReadOnlySpan<char> after, ReadOnlySpan<char> suffix)
    {
        var n = 0;
        while (n < after.Length && n < suffix.Length && after[n] == suffix[n])
        {
            n++;
        }

        return n;
    }

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static int? OptionalInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt32(out var value)
            ? value
            : null;
}
