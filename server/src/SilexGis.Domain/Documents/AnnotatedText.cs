// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Documents;

/// <summary>
/// What a block of annotated text is. Wire values are short strings rather than numbers
/// because the body is a stored file — a long-lived artifact somebody may one day read with
/// something that is not this application — and a file full of <c>"type": 4</c> is a file
/// nobody can read without this source tree. Append only: a value written into a stored body
/// is a contract with every body already on disk.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnnotatedBlockType
{
    /// <summary>Ordinary prose.</summary>
    [JsonStringEnumMemberName("p")]
    Paragraph,

    [JsonStringEnumMemberName("h1")]
    Heading1,

    [JsonStringEnumMemberName("h2")]
    Heading2,

    [JsonStringEnumMemberName("h3")]
    Heading3,

    /// <summary>One item of an unordered list. Consecutive items render as one list.</summary>
    [JsonStringEnumMemberName("ul")]
    BulletItem,

    /// <summary>One item of an ordered list. Consecutive items render as one list.</summary>
    [JsonStringEnumMemberName("ol")]
    NumberItem,

    [JsonStringEnumMemberName("quote")]
    Quote,

    /// <summary>Pre-formatted text — a station list, a fragment of a survey file.</summary>
    [JsonStringEnumMemberName("code")]
    Code,
}

/// <summary>Inline emphasis. Deliberately typographic only: nothing here carries a link,
/// because a link is a resource link and lives in its own table.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnnotatedMarkKind
{
    [JsonStringEnumMemberName("b")]
    Bold,

    [JsonStringEnumMemberName("i")]
    Italic,

    [JsonStringEnumMemberName("u")]
    Underline,

    [JsonStringEnumMemberName("code")]
    Code,
}

/// <summary>
/// A run of emphasis within one block, in the same units the block's text is measured in.
/// <paramref name="End"/> is exclusive, matching the text-range anchor.
/// </summary>
public sealed record AnnotatedMark(int Start, int End, AnnotatedMarkKind Kind);

/// <summary>
/// One block: a type, one line of text, and the emphasis over it.
/// <para>
/// The text is a single line and carries no markup of its own. That is what makes the offset
/// contract below hold: the block's characters are exactly the characters that reach the
/// canonical stream, in order, with nothing added or interpreted on the way.
/// </para>
/// </summary>
public sealed record AnnotatedBlock(
    AnnotatedBlockType Type,
    string Text,
    IReadOnlyList<AnnotatedMark>? Marks = null);

/// <summary>The whole body of a link-annotated text document.</summary>
public sealed record AnnotatedTextBody(IReadOnlyList<AnnotatedBlock> Blocks);

/// <summary>
/// The link-annotated text format: its media type, its validity rules, and — the reason this
/// file exists — the single definition of the character stream that resource-link text-range
/// anchors index into.
///
/// <para>
/// <b>The offset contract.</b> A <c>textRange</c> anchor stores <c>{start, end}</c> as offsets
/// into a document's extracted text. For every other format that stream is whatever a reader
/// managed to pull out of somebody else's file, and the client can only ever ask the server
/// where a passage is. For this format the stream is <em>defined</em> — it is the blocks' own
/// text joined by a blank line, and nothing else — so the client can compute an offset itself,
/// exactly, without a round trip and without reproducing anybody's tag-stripping heuristics.
/// That is the whole reason the body is stored as blocks of plain text rather than as markup:
/// a renderer that had to strip tags to find the stream would be a second implementation of
/// this function, in another language, that has to agree with it to the character or every
/// highlight in the application lands on the wrong words.
/// </para>
///
/// <para>
/// <b>Offsets are UTF-16 code units</b>, which is what <c>string.Length</c> counts here and what
/// <c>String.prototype.length</c> counts in the browser. They agree by construction, including
/// across the surrogate pairs an emoji is made of. Anything that "corrected" either side to
/// count runes or bytes would move every anchor past the first non-ASCII character in the
/// document, silently and only for some documents.
/// </para>
///
/// <para>
/// <b>The stream is stable under <see cref="PageText.Normalize"/>.</b> Extracted text is stored
/// normalised, so if normalising the canonical stream changed it, the offsets computed here
/// would address a different string than the one anchors are resolved against. The validity
/// rules below are exactly what makes normalising a no-op: no carriage returns, no control
/// characters, no leading or trailing space on a block, no empty block (two of those in a row
/// would produce a run of blank lines that normalising collapses), and single-line blocks —
/// so the only newlines in the stream are the two this file puts between blocks, which is the
/// most a run of them may be.
/// </para>
/// </summary>
public static class AnnotatedText
{
    /// <summary>
    /// The media type a link-annotated body is stored under.
    /// <para>
    /// Its own type rather than <c>application/json</c>, for two reasons that both bite. The
    /// viewer dispatches on the recorded format, and a body filed as generic JSON would be
    /// shown to a reader as its own source. And the text reader dispatches on it too: this
    /// format must be read by the reader that understands blocks, which produces the prose,
    /// and not by the plain-text reader, which would index the punctuation of the JSON around
    /// it and make every document findable by searching for <c>"blocks"</c>.
    /// </para>
    /// </summary>
    public const string MediaType = "application/vnd.silexgis.annotated-text+json";

    /// <summary>Extension the stored file is written with; only ever seen by the file store.</summary>
    public const string FileExtension = ".sgtext";

    /// <summary>What separates two blocks in the canonical stream: one blank line.</summary>
    public const string BlockSeparator = "\n\n";

    public const string BodyInvalidCode = "annotatedtext.body_invalid";

    /// <summary>
    /// Blocks per body. High enough for a monograph chapter, finite because the whole body is
    /// read, rendered and highlighted in one go — there is no paging inside one of these.
    /// </summary>
    public const int MaxBlocks = 5_000;

    /// <summary>Characters in one block. A long paragraph is a few hundred.</summary>
    public const int MaxBlockCharacters = 20_000;

    /// <summary>
    /// Characters in the whole canonical stream. Comfortably under
    /// <see cref="PageText.MaxCharacters"/>, so a body that validates here is never truncated
    /// by the reader afterwards — which would silently strand every anchor past the cut.
    /// </summary>
    public const int MaxCharacters = 500_000;

    /// <summary>Emphasis runs in one block.</summary>
    public const int MaxMarksPerBlock = 500;

    /// <summary>
    /// The character stream this document's anchors are measured against: every block's text,
    /// in order, joined by a blank line.
    /// </summary>
    public static string CanonicalText(IReadOnlyList<AnnotatedBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return string.Join(BlockSeparator, blocks.Select(b => b.Text));
    }

    /// <summary>
    /// Where each block begins in <see cref="CanonicalText"/>, by block index.
    /// <para>
    /// Returned rather than recomputed by callers, because "sum of the lengths before me, plus
    /// two for each separator" is the kind of arithmetic that is written slightly differently
    /// every time it is written.
    /// </para>
    /// </summary>
    public static int[] BlockStarts(IReadOnlyList<AnnotatedBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var starts = new int[blocks.Count];
        var at = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            starts[i] = at;
            at += blocks[i].Text.Length + BlockSeparator.Length;
        }

        return starts;
    }

    /// <summary>
    /// What is wrong with a body, or null when it is well formed. Every rule here is either
    /// a bound or a condition of the offset contract; there is nothing stylistic.
    /// </summary>
    public static string? BodyProblem(AnnotatedTextBody? body)
    {
        if (body?.Blocks is null)
        {
            return "the body has no blocks";
        }

        if (body.Blocks.Count == 0)
        {
            return "a body holds at least one block";
        }

        if (body.Blocks.Count > MaxBlocks)
        {
            return $"a body holds at most {MaxBlocks} blocks";
        }

        var total = 0;
        for (var i = 0; i < body.Blocks.Count; i++)
        {
            var problem = BlockProblem(body.Blocks[i]);
            if (problem is not null)
            {
                return $"block {i}: {problem}";
            }

            total += body.Blocks[i].Text.Length + BlockSeparator.Length;
            if (total > MaxCharacters)
            {
                return $"a body holds at most {MaxCharacters} characters";
            }
        }

        return null;
    }

    private static string? BlockProblem(AnnotatedBlock? block)
    {
        if (block is null)
        {
            return "missing";
        }

        if (!Enum.IsDefined(block.Type))
        {
            return "unknown block type";
        }

        var text = block.Text;
        if (string.IsNullOrEmpty(text))
        {
            return "text is empty — a blank line is the space between blocks, never a block";
        }

        if (text.Length > MaxBlockCharacters)
        {
            return $"text is longer than {MaxBlockCharacters} characters";
        }

        // Each of the three below is a condition of the stream surviving normalisation
        // unchanged, not tidiness. See the type comment.
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return "text carries a control character; a block is one line of plain text";
            }
        }

        if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
        {
            return "text has leading or trailing whitespace";
        }

        return MarksProblem(block);
    }

    private static string? MarksProblem(AnnotatedBlock block)
    {
        if (block.Marks is null || block.Marks.Count == 0)
        {
            return null;
        }

        if (block.Marks.Count > MaxMarksPerBlock)
        {
            return $"more than {MaxMarksPerBlock} emphasis runs";
        }

        // Same-kind overlap is refused rather than merged. Merging here would mean the body
        // that comes back out is not the body that went in, and an editor that round-trips its
        // own document would see it change under it; the client normalises before sending.
        var seen = new List<AnnotatedMark>(block.Marks.Count);
        foreach (var mark in block.Marks)
        {
            if (mark is null || !Enum.IsDefined(mark.Kind))
            {
                return "unknown emphasis kind";
            }

            if (mark.Start < 0 || mark.End > block.Text.Length || mark.End <= mark.Start)
            {
                return "an emphasis run lies outside the block's text, or is empty";
            }

            if (seen.Any(other => other.Kind == mark.Kind && other.Start < mark.End && mark.Start < other.End))
            {
                return "two emphasis runs of the same kind overlap";
            }

            seen.Add(mark);
        }

        return null;
    }

    /// <summary>Serialiser settings for the stored body — one home, because the bytes written
    /// and the bytes read have to agree about naming and about how the enums are spelled.</summary>
    public static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web)
    {
        // The file is read by people as well as by programs when something has gone wrong.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The body as the bytes that get stored.</summary>
    public static byte[] Serialize(AnnotatedTextBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return JsonSerializer.SerializeToUtf8Bytes(body, BodyJson);
    }

    /// <summary>
    /// A stored body read back, or null when the bytes are not one. Null rather than an
    /// exception because the caller is a viewer: a body that cannot be parsed is a document
    /// that says so, not a request that fails.
    /// </summary>
    public static AnnotatedTextBody? Deserialize(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var body = JsonSerializer.Deserialize<AnnotatedTextBody>(utf8, BodyJson);
            return body?.Blocks is null ? null : body;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a stored body from a stream, bounded so a crafted file cannot decide how
    /// much memory is used.</summary>
    public static async Task<AnnotatedTextBody?> ReadAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The bound is the serialised form of the largest body the rules admit, plus room for
        // the structure around the text. Anything larger was not written by this application.
        const int maxBytes = (MaxCharacters * 4) + (MaxBlocks * 256);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Deserialize(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>
    /// A plain-text draft turned into blocks: blank-line-separated paragraphs, everything
    /// typographically flat. What the paste box and the import path both start from.
    /// </summary>
    public static AnnotatedTextBody FromPlainText(string? text)
    {
        var blocks = new List<AnnotatedBlock>();
        foreach (var paragraph in (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split("\n\n", StringSplitOptions.None))
        {
            // Single newlines inside a paragraph are wrapping, not structure, so they become
            // spaces — a block is one line by contract, and keeping them would be invalid.
            var flattened = new StringBuilder(paragraph.Length);
            foreach (var c in paragraph)
            {
                flattened.Append(char.IsControl(c) ? ' ' : c);
            }

            var trimmed = flattened.ToString().Trim();
            if (trimmed.Length > 0)
            {
                blocks.Add(new AnnotatedBlock(AnnotatedBlockType.Paragraph, trimmed));
            }
        }

        return new AnnotatedTextBody(blocks);
    }
}
