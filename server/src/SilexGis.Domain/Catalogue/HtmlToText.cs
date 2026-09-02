// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Catalogue;

/// <summary>
/// Turns a foreign system's HTML into the plain text this application stores.
///
/// <para>
/// Descriptions here are stored and rendered as text, never as markup, so this is a conversion
/// rather than a sanitiser: nothing downstream is asked to decide which tags are safe, because
/// nothing downstream ever sees a tag. That distinction is the point. A sanitiser has to be
/// right about every construct an attacker might reach for; a converter only has to be right
/// about what a paragraph break looks like.
/// </para>
/// <para>
/// The input is genuinely hostile in the ordinary sense as well as the security one: the
/// catalogue's descriptions were pasted out of word processors and carry <c>&lt;w:…&gt;</c> and
/// <c>&lt;o:…&gt;</c> namespace tags, <c>&lt;xml&gt;</c> islands, editor residue, inline styles
/// and non-breaking spaces. Measured over the catalogue, the median description is about half a
/// kilobyte and the largest single one is over a megabyte — which is why
/// <see cref="Convert(string?, int)"/> takes a budget instead of trusting the source to be
/// reasonable.
/// </para>
/// </summary>
public static partial class HtmlToText
{
    /// <summary>
    /// How much markup will be looked at, as a multiple of the requested text budget. Markup is
    /// several times the size of the text it carries, so this is generous; its only job is to
    /// stop one pathological record from costing unbounded work when the answer is going to be
    /// truncated anyway.
    /// </summary>
    private const int MarkupBudgetFactor = 8;

    /// <summary>Appended when the source was longer than the budget, so a reader can tell.</summary>
    public const string TruncationMarker = "…";

    /// <summary>
    /// Converts <paramref name="html"/> to plain text, at most <paramref name="maxChars"/> of it.
    /// Returns null when there is nothing left to say — an empty input, or markup carrying no
    /// text — so a caller can leave a field unset rather than store an empty string.
    /// </summary>
    /// <param name="html">The source markup. May be null, empty, or not markup at all.</param>
    /// <param name="maxChars">
    /// Ceiling on the returned text. Truncation happens on a word boundary where one is near, so
    /// a cut description does not end mid-word, and is marked with <see cref="TruncationMarker"/>.
    /// </param>
    public static string? Convert(string? html, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var budget = Math.Max(1, maxChars);

        // Cut the markup before doing any work on it rather than after. The alternative —
        // converting a megabyte and then keeping the first few thousand characters — spends all
        // the time and all the memory to throw the result away.
        var source = html.Length > budget * MarkupBudgetFactor
            ? html[..(budget * MarkupBudgetFactor)]
            : html;
        var sourceWasCut = source.Length < html.Length;

        var text = Strip(source);

        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= budget && !sourceWasCut)
        {
            return text;
        }

        return Truncate(text, budget);
    }

    private static string Strip(string source)
    {
        var s = source;

        // Order matters: the element-with-content removals must run before the blanket tag
        // removal, or their bodies survive as text once their tags are gone.
        s = ScriptOrStyle().Replace(s, " ");
        s = Comment().Replace(s, " ");
        s = ProcessingInstruction().Replace(s, " ");
        s = XmlIsland().Replace(s, " ");

        // A link is information a description often depends on ("see the survey at …"), so the
        // address is kept beside the text rather than dropped with the tag. Only absolute http
        // addresses: a relative one is meaningless once it has left the site it was written on.
        s = Anchor().Replace(s, m =>
        {
            var href = m.Groups["href"].Value.Trim();
            var inner = Tag().Replace(m.Groups["text"].Value, " ").Trim();
            inner = WhitespaceRun().Replace(WebUtility.HtmlDecode(inner), " ").Trim();

            if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return inner;
            }

            return inner.Length == 0 || string.Equals(inner, href, StringComparison.OrdinalIgnoreCase)
                ? href
                : $"{inner} ({href})";
        });

        // Structure that a reader would see as a break becomes one, so paragraphs and list items
        // survive the loss of their markup. A list item additionally keeps its bullet: a run-on
        // of unmarked lines reads as prose and loses the fact that it was a list.
        s = LineBreak().Replace(s, "\n");
        s = ListItemOpen().Replace(s, "\n• ");
        s = BlockBoundary().Replace(s, "\n");

        // Table cells are the one remaining boundary that is neither a block nor nothing: two
        // cells run together read as one word, and there is no line between them to draw.
        s = CellBoundary().Replace(s, " ");

        // Everything still standing is an inline tag, and an inline tag separates nothing —
        // removing it leaves the text either side joined, which is what it looked like on screen.
        // Replacing these with a space instead puts one before every full stop that happened to
        // follow an emphasis, and the catalogue's descriptions were pasted out of word processors,
        // so they carry an emphasis or a span around almost every other phrase.
        s = Tag().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);

        // A non-breaking space survives decoding as U+00A0 and would otherwise reach the reader
        // as an invisible oddity that breaks word wrapping and search alike.
        s = s.Replace(' ', ' ').Replace('​', ' ');

        return Tidy(s);
    }

    /// <summary>
    /// Collapses the whitespace the markup left behind: runs of spaces to one, runs of blank
    /// lines to one blank line, and no leading or trailing space on any line.
    /// </summary>
    private static string Tidy(string s)
    {
        var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(s.Length);
        var blankRun = 0;

        foreach (var raw in lines)
        {
            var line = WhitespaceRun().Replace(raw, " ").Trim();

            if (line.Length == 0)
            {
                blankRun++;
                continue;
            }

            if (builder.Length > 0)
            {
                // One blank line between blocks, however many the markup implied.
                builder.Append(blankRun > 0 ? "\n\n" : "\n");
            }

            blankRun = 0;
            builder.Append(line);
        }

        return builder.ToString().Trim();
    }

    private static string Truncate(string text, int budget)
    {
        if (text.Length <= budget)
        {
            return text;
        }

        var cut = text[..budget];

        // Prefer a word boundary, but only a nearby one: searching the whole string for a space
        // would let a long unbroken run (a URL, a table pasted without spaces) throw away most
        // of the text that was kept.
        var space = cut.LastIndexOfAny([' ', '\n']);
        if (space > budget - 80 && space > 0)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd() + TruncationMarker;
    }

    // Bounded, linear patterns only — these run over untrusted input that can be a megabyte long,
    // so nothing here may backtrack. Each is anchored on a literal and consumes a negated class.
    [GeneratedRegex(@"<\s*(?<tag>script|style)\b[^>]*>.*?<\s*/\s*\k<tag>\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<[!?][^>]*>")]
    private static partial Regex ProcessingInstruction();

    /// <summary>Word's XML islands: the whole block is machine residue, not text.</summary>
    [GeneratedRegex(@"<\s*xml\b[^>]*>.*?<\s*/\s*xml\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex XmlIsland();

    [GeneratedRegex(@"<\s*a\b[^>]*?\bhref\s*=\s*[""'](?<href>[^""']*)[""'][^>]*>(?<text>.*?)<\s*/\s*a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<\s*br\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<\s*li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemOpen();

    /// <summary>Opening or closing tags of elements a reader sees as starting a new block.</summary>
    [GeneratedRegex(@"<\s*/?\s*(p|div|tr|table|thead|tbody|ul|ol|li|h[1-6]|blockquote|pre|section|article|hr)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex BlockBoundary();

    /// <summary>Table cells: not a line of their own, but not nothing either.</summary>
    [GeneratedRegex(@"<\s*/?\s*(td|th)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex CellBoundary();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex WhitespaceRun();
}
