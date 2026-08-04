// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Domain.Documents;

/// <summary>
/// Recovers the readable text from a Rich Text Format document.
/// <para>
/// RTF is a flat, seven-bit syntax: brace-delimited groups, backslash control words, and
/// everything else is the text itself. Only four things stand between that and plain text -
/// groups whose contents describe the document rather than being part of it (font and colour
/// tables, the generator's signature, embedded pictures), control words that stand for a
/// character (a paragraph break, a tab, a curly quote), the two spellings of characters outside
/// ASCII (a <c>\'hh</c> byte in the code page the file names, and a <c>\uN</c> code point
/// followed by a replacement a reader that understood it must discard), and binary runs, whose
/// bytes are measured rather than delimited.
/// </para>
/// <para>
/// Written here rather than taken from a library because that is the whole of the format that
/// matters for reading text out of one: what is thrown away is layout, and layout is exactly
/// what extraction does not keep. A parser that also understood tables and styles would be a
/// larger dependency for no additional text.
/// </para>
/// </summary>
public static class RichTextFormat
{
    /// <summary>Largest replacement run a Unicode escape may declare, as a sanity bound.</summary>
    private const int MaxUnicodeFallbackLength = 32;

    /// <summary>
    /// Groups whose contents describe the document rather than being part of it. Anything
    /// marked <c>\*</c> is skipped on that mark alone; these are the ones written without it.
    /// </summary>
    private static readonly string[] DescriptionGroups =
    [
        "fonttbl", "filetbl", "colortbl", "stylesheet", "listtable", "listoverridetable",
        "revtbl", "rsidtbl", "generator", "info", "pict", "objdata", "themedata",
        "colorschememapping", "datastore", "latentstyles", "xmlnstbl",
    ];

    /// <summary>The readable text of an RTF document, with its paragraph breaks kept.</summary>
    /// <remarks>
    /// Takes bytes rather than a string because the document's own characters outside ASCII are
    /// byte values in a code page the file names in its header - decoding it as text first
    /// would mean guessing that code page before having read it.
    /// </remarks>
    public static string ToPlainText(ReadOnlySpan<byte> rtf)
    {
        var sink = new PlainTextSink();
        var groups = new Stack<GroupState>();
        groups.Push(new GroupState(UnicodeFallbackLength: 1, Skip: false));
        var atGroupStart = false;
        var fallbackToSkip = 0;

        for (var i = 0; i < rtf.Length; i++)
        {
            var c = (char)rtf[i];

            if (c == '{')
            {
                sink.Flush();
                groups.Push(groups.Peek());
                atGroupStart = true;
                fallbackToSkip = 0;
                continue;
            }

            if (c == '}')
            {
                sink.Flush();
                if (groups.Count > 1)
                {
                    groups.Pop();
                }

                atGroupStart = false;
                fallbackToSkip = 0;
                continue;
            }

            if (c != '\\')
            {
                // A line break in the source is formatting of the file, not of the document.
                if (c is '\r' or '\n')
                {
                    continue;
                }

                if (fallbackToSkip > 0)
                {
                    fallbackToSkip--;
                }
                else if (!groups.Peek().Skip)
                {
                    if (rtf[i] >= 0x80)
                    {
                        // Some writers put the code page's own bytes straight into the text
                        // rather than escaping them; they decode the same way an escape does.
                        sink.AppendByte(rtf[i]);
                    }
                    else
                    {
                        sink.Append(c);
                    }
                }

                atGroupStart = false;
                continue;
            }

            if (i + 1 >= rtf.Length)
            {
                break;
            }

            var next = (char)rtf[i + 1];

            // The mark that says "a reader which does not know this destination must skip it",
            // which is exactly this reader's position on every destination it does not know.
            if (next == '*')
            {
                Replace(groups, groups.Peek() with { Skip = true });
                atGroupStart = false;
                i++;
                continue;
            }

            if (!char.IsAsciiLetter(next))
            {
                i = ControlSymbol(rtf, i, next, groups.Peek().Skip, ref fallbackToSkip, sink);
                atGroupStart = false;
                continue;
            }

            var wordEnd = i + 1;
            while (wordEnd < rtf.Length && char.IsAsciiLetter((char)rtf[wordEnd]))
            {
                wordEnd++;
            }

            var word = Ascii(rtf[(i + 1)..wordEnd]);
            var negative = wordEnd < rtf.Length && rtf[wordEnd] == (byte)'-';
            var digitsStart = negative ? wordEnd + 1 : wordEnd;
            var digitsEnd = digitsStart;
            while (digitsEnd < rtf.Length && char.IsAsciiDigit((char)rtf[digitsEnd])
                && digitsEnd - digitsStart < 10)
            {
                digitsEnd++;
            }

            int? parameter = null;
            if (digitsEnd > digitsStart)
            {
                var magnitude = long.Parse(Ascii(rtf[digitsStart..digitsEnd]),
                    CultureInfo.InvariantCulture);
                parameter = (int)Math.Clamp(negative ? -magnitude : magnitude,
                    int.MinValue, int.MaxValue);
            }

            // A single space after a control word is its delimiter, not text.
            var after = digitsEnd;
            if (after < rtf.Length && rtf[after] == (byte)' ')
            {
                after++;
            }

            i = after - 1;

            if (word == "bin" && parameter is > 0)
            {
                // Binary data measured in bytes rather than delimited: never text, and reading
                // it as syntax would derail everything that follows it. The declared length is
                // the file's own and may be anything up to the largest number the parameter can
                // hold, so the addition is done wide: adding it to the current position inside
                // an int would wrap to a negative index that still satisfies the loop condition.
                i = (int)Math.Min(rtf.Length, (long)after + parameter.Value) - 1;
                continue;
            }

            if (fallbackToSkip > 0)
            {
                fallbackToSkip--;
                atGroupStart = false;
                continue;
            }

            if (atGroupStart && DescriptionGroups.Contains(word, StringComparer.Ordinal))
            {
                Replace(groups, groups.Peek() with { Skip = true });
                atGroupStart = false;
                continue;
            }

            atGroupStart = false;
            ApplyControlWord(word, parameter, groups, sink, ref fallbackToSkip);
        }

        return sink.ToString();
    }

    /// <summary>
    /// Applies a control word: a code-page declaration, a character it stands for, or nothing.
    /// An unrecognised word is markup and is dropped, which is what makes this readable at all.
    /// </summary>
    private static void ApplyControlWord(
        string word, int? parameter, Stack<GroupState> groups, PlainTextSink sink,
        ref int fallbackToSkip)
    {
        switch (word)
        {
            case "ansicpg":
                // Anything already buffered was written under the previous declaration.
                sink.Flush();
                sink.CodePage = parameter == 1250
                    ? TextEncodings.Windows1250
                    : TextEncodings.Windows1252;
                return;
            case "uc" when parameter is >= 0 and <= MaxUnicodeFallbackLength:
                Replace(groups, groups.Peek() with { UnicodeFallbackLength = parameter.Value });
                return;
            case "u" when parameter is { } code:
                if (!groups.Peek().Skip)
                {
                    // The parameter is a signed 16-bit value, so anything above U+7FFF
                    // arrives negative and has to be brought back into range.
                    sink.Append((char)(code < 0 ? code + 0x10000 : code));
                }

                // Whatever follows is the same character spelled for a reader that cannot read
                // this one, and must not be emitted a second time.
                fallbackToSkip = groups.Peek().UnicodeFallbackLength;
                return;
            default:
                break;
        }

        if (groups.Peek().Skip)
        {
            return;
        }

        var replacement = word switch
        {
            "par" or "line" or "sect" or "page" or "row" => "\n",
            "tab" or "cell" or "nestcell" => "\t",
            "emdash" => "—",
            "endash" => "–",
            "lquote" => "‘",
            "rquote" => "’",
            "ldblquote" => "“",
            "rdblquote" => "”",
            "bullet" => "•",
            "emspace" or "enspace" or "qmspace" => " ",
            _ => null,
        };

        if (replacement is not null)
        {
            sink.Append(replacement);
        }
    }

    /// <summary>
    /// Handles a backslash followed by something that is not a letter, and reports the index of
    /// the last byte it consumed.
    /// </summary>
    private static int ControlSymbol(
        ReadOnlySpan<byte> rtf, int index, char symbol, bool skipping, ref int fallbackToSkip,
        PlainTextSink sink)
    {
        if (symbol == '\'')
        {
            if (index + 3 >= rtf.Length)
            {
                return rtf.Length - 1;
            }

            if (TryHexPair((char)rtf[index + 2], (char)rtf[index + 3], out var value))
            {
                if (fallbackToSkip > 0)
                {
                    fallbackToSkip--;
                }
                else if (!skipping)
                {
                    sink.AppendByte(value);
                }
            }

            return index + 3;
        }

        if (fallbackToSkip > 0)
        {
            fallbackToSkip--;
            return index + 1;
        }

        if (!skipping)
        {
            switch (symbol)
            {
                case '\\' or '{' or '}':
                    sink.Append(symbol);
                    break;
                case '~':
                    sink.Append(' ');
                    break;
                case '_':
                    sink.Append('-');
                    break;
                case '\r' or '\n':
                    // An escaped line break in the source is a paragraph break.
                    sink.Append('\n');
                    break;
                default:
                    // A discretionary hyphen and anything else carry no text of their own.
                    break;
            }
        }

        return index + 1;
    }

    /// <summary>Replaces the innermost group's state, which a stack cannot do in place.</summary>
    private static void Replace(Stack<GroupState> groups, GroupState state)
    {
        groups.Pop();
        groups.Push(state);
    }

    private static bool TryHexPair(char high, char low, out byte value)
    {
        value = 0;
        if (!TryHexDigit(high, out var h) || !TryHexDigit(low, out var l))
        {
            return false;
        }

        value = (byte)((h << 4) | l);
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    /// <summary>
    /// What a group inherits from the one enclosing it: how many characters follow a Unicode
    /// escape as its replacement, and whether the group's contents are text at all.
    /// </summary>
    private readonly record struct GroupState(int UnicodeFallbackLength, bool Skip);

    /// <summary>
    /// Collects the output. Bytes in the document's code page are buffered rather than decoded
    /// one at a time, because a code page is a byte-to-character table and decoding a run of
    /// them together is the only form in which that is true of every encoding a caller might
    /// later name.
    /// </summary>
    private sealed class PlainTextSink
    {
        private readonly StringBuilder text = new();
        private readonly List<byte> pending = [];

        /// <summary>The single-byte encoding the document declared for its escaped bytes.</summary>
        public string CodePage { get; set; } = TextEncodings.Windows1252;

        public void AppendByte(byte value) => pending.Add(value);

        public void Append(char value)
        {
            Flush();
            text.Append(value);
        }

        public void Append(string value)
        {
            Flush();
            text.Append(value);
        }

        public void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }

            text.Append(TextEncodings.DecodeSingleByte(pending.ToArray(), CodePage));
            pending.Clear();
        }

        public override string ToString()
        {
            Flush();
            return text.ToString();
        }
    }
}
