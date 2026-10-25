// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The survey file as it is compiled — a copy of what a field device uploaded, never the archived
/// file itself — and what the copy had to be changed in to compile what was sent.
/// </summary>
/// <param name="Text">The text to compile.</param>
/// <param name="SketchesDrawn">The uploaded sketches the copy now reads and draws, in the order the survey names them.</param>
/// <param name="FixUncommented">Whether the survey's own fixed station, written as a comment, was made live.</param>
public sealed record TherionCompileText(string Text, IReadOnlyList<string> SketchesDrawn, bool FixUncommented);

/// <summary>
/// A line of an uploaded Therion file that would make the compiler read a file other than the
/// upload's own. Names the uploaded file, the line and the command, never the path it gave: the
/// path is the uploader's text, and a crafted one is exactly what a status message must not repeat.
/// </summary>
public sealed record TherionUnsafeReference(string File, int Line, string Command);

/// <summary>
/// The rules for turning an uploaded Therion survey into the copy the compiler reads.
/// </summary>
/// <remarks>
/// <para>
/// TopoDroid writes a survey's sketches and its fixed station as comments unless its user ticked
/// an export option: <c># input "&lt;survey&gt;-1p.th2"</c>, a commented <c>map … -projection …</c>
/// block per sketch, and <c># fix &lt;station&gt; &lt;long&gt; &lt;lat&gt; &lt;alt&gt;</c> under a live
/// <c>cs</c>. Compiled as written, such a survey has no drawings and no position even when the
/// sketches came with it. So the copy makes live exactly those lines — an <c>input</c> of a sketch
/// that was uploaded, a <c>map</c> block every scrap of which comes from a sketch that is now read,
/// and the first commented <c>fix</c> when a coordinate system is live and no live <c>fix</c>
/// exists (a second fix would bend the survey into a loop Therion compiles without complaint).
/// Nothing else written as a comment is touched.
/// </para>
/// <para>
/// The compiler opens any path a survey names and quotes what it read into its log, so before it
/// runs every live line that reads another file has to name one of the upload's own files by its
/// bare name. A sandbox stands behind this; this is the line in front of it.
/// </para>
/// <para>
/// The Romanian letters with a comma below are printed by the compiler's default fonts as plain
/// letters, so the copy uses the cedilla forms, which those fonts do carry.
/// </para>
/// </remarks>
public static class TherionCompileCopy
{
    /// <summary>The commands with which a Therion file reads another file.</summary>
    public static readonly IReadOnlyList<string> ReadingCommands = ["input", "source", "import", "load", "include"];

    private static readonly Regex CommentedInput = new(
        @"^(?<indent>\s*)#\s*(?<command>input\s+(?:""(?<quoted>[^""]+)""|(?<bare>\S+)))\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CommentedMapStart = new(
        @"^(?<indent>\s*)#\s*(?<command>map\s+\S+.*-projection\s+\S+.*)$", RegexOptions.CultureInvariant);

    private static readonly Regex CommentedMapEnd = new(@"^(?<indent>\s*)#\s*(?<command>endmap)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex CommentedMember = new(@"^(?<indent>\s*)#\s*(?<member>\S+)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex CommentedFix = new(
        @"^(?<indent>\s*)#\s*(?<command>fix\s+\S+\s+[-+]?[0-9][0-9.]*\s+[-+]?[0-9][0-9.]*.*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The copy of the uploaded survey <paramref name="surveyText"/> to compile, given the bare
    /// names of the sketches uploaded with it.
    /// </summary>
    public static TherionCompileText ForCompile(string surveyText, IReadOnlyCollection<string> uploadedSketches)
    {
        var sketches = uploadedSketches.ToHashSet(StringComparer.Ordinal);
        var lines = SplitLines(surveyText);

        var liveInputs = new HashSet<string>(StringComparer.Ordinal);
        var drawn = new List<string>();
        foreach (var (line, _) in Live(lines))
        {
            if (Tokens(line) is [var command, var path, ..] && command == "input" && sketches.Contains(SketchName(path)))
            {
                liveInputs.Add(SketchName(path));
            }
        }

        // The sketches' inputs first, so a map block can be judged on what is read in the end.
        for (var i = 0; i < lines.Count; i++)
        {
            var match = CommentedInput.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var path = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value;
            if (sketches.Contains(SketchName(path)) && liveInputs.Add(SketchName(path)))
            {
                lines[i] = match.Groups["indent"].Value + match.Groups["command"].Value;
            }
        }

        foreach (var (line, _) in Live(lines))
        {
            if (Tokens(line) is [var command, var path, ..] && command == "input" && liveInputs.Contains(SketchName(path))
                && !drawn.Contains(SketchName(path)))
            {
                drawn.Add(SketchName(path));
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var start = CommentedMapStart.Match(lines[i]);
            if (!start.Success)
            {
                continue;
            }

            var end = -1;
            var members = new List<int>();
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (CommentedMapEnd.IsMatch(lines[j]))
                {
                    end = j;
                    break;
                }

                if (!CommentedMember.IsMatch(lines[j]))
                {
                    break;
                }

                members.Add(j);
            }

            var drawable = end > 0 && members.Count > 0 && members.All(m =>
                liveInputs.Contains(CommentedMember.Match(lines[m]).Groups["member"].Value + ".th2"));
            if (!drawable)
            {
                continue;
            }

            lines[i] = start.Groups["indent"].Value + start.Groups["command"].Value.TrimEnd();
            foreach (var m in members)
            {
                var member = CommentedMember.Match(lines[m]);
                lines[m] = member.Groups["indent"].Value + "  " + member.Groups["member"].Value;
            }

            var close = CommentedMapEnd.Match(lines[end]);
            lines[end] = close.Groups["indent"].Value + close.Groups["command"].Value;
            i = end;
        }

        var fixUncommented = false;
        var live = Live(lines).Select(l => Tokens(l.Line)).ToList();
        if (live.Any(t => t is ["cs", ..]) && !live.Any(t => t is ["fix", ..]))
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var match = CommentedFix.Match(lines[i]);
                if (match.Success && CsLiveBefore(lines, i))
                {
                    lines[i] = match.Groups["indent"].Value + match.Groups["command"].Value.TrimEnd();
                    fixUncommented = true;
                    break;
                }
            }
        }

        return new TherionCompileText(Normalise(string.Join('\n', lines)), drawn, fixUncommented);
    }

    /// <summary>
    /// <paramref name="text"/> with the comma-below letters <c>ș ț Ș Ț</c> written as their cedilla
    /// forms <c>ş ţ Ş Ţ</c>. Applied to every compiled copy, never to an archived file.
    /// </summary>
    public static string Normalise(string text) =>
        text.Replace('ș', 'ş').Replace('ț', 'ţ').Replace('Ș', 'Ş').Replace('Ț', 'Ţ');

    /// <summary>
    /// The live lines of the uploaded file <paramref name="fileName"/> that would read a file other
    /// than one of <paramref name="uploadedFiles"/> (bare names): a reading command, or a scrap's
    /// <c>-sketch</c> image, whose path is rooted, climbs (<c>..</c>), has any directory part, or
    /// names nothing that was uploaded. A name may leave out the <c>.th</c> or <c>.th2</c> it has.
    /// </summary>
    public static IReadOnlyList<TherionUnsafeReference> UnsafeReferences(
        string fileName, string text, IReadOnlyCollection<string> uploadedFiles)
    {
        var uploaded = uploadedFiles.ToHashSet(StringComparer.Ordinal);
        var found = new List<TherionUnsafeReference>();
        foreach (var (line, number) in Live(SplitLines(text)))
        {
            var tokens = Tokens(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            var command = tokens[0].ToLowerInvariant();
            if (ReadingCommands.Contains(command) && (tokens.Count < 2 || !Names(tokens[1], uploaded)))
            {
                found.Add(new TherionUnsafeReference(fileName, number, command));
                continue;
            }

            for (var t = 1; t < tokens.Count; t++)
            {
                if (string.Equals(tokens[t], "-sketch", StringComparison.OrdinalIgnoreCase)
                    && (t + 1 >= tokens.Count || !Names(tokens[t + 1], uploaded)))
                {
                    found.Add(new TherionUnsafeReference(fileName, number, "-sketch"));
                }
            }
        }

        return found;
    }

    private static bool Names(string path, HashSet<string> uploaded)
    {
        var p = path.Trim();
        if (p.Length == 0 || p is "." or ".." || p.IndexOfAny(['/', '\\', ':']) >= 0 || p.Any(char.IsControl))
        {
            return false;
        }

        return uploaded.Contains(p) || uploaded.Contains(p + ".th") || uploaded.Contains(p + ".th2");
    }

    private static string SketchName(string path)
    {
        var p = path.Trim();
        return p.EndsWith(".th2", StringComparison.Ordinal) ? p : p + ".th2";
    }

    private static bool CsLiveBefore(List<string> lines, int index) =>
        Live(lines.Take(index).ToList()).Any(l => Tokens(l.Line) is ["cs", ..]);

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

    /// <summary>Each line with its comment cut off, with its one-based number; lines left empty are skipped.</summary>
    private static IEnumerable<(string Line, int Number)> Live(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var code = WithoutComment(lines[i]).Trim();
            if (code.Length > 0)
            {
                yield return (code, i + 1);
            }
        }
    }

    // A comment runs from a '#' outside double quotes to the end of the line.
    private static string WithoutComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == '#' && !quoted)
            {
                return line[..i];
            }
        }

        return line;
    }

    // Whitespace-separated words, a double-quoted string being one word without its quotes; a
    // doubled quote inside one stands for a quote, as Therion writes it.
    private static List<string> Tokens(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            var word = new StringBuilder();
            if (line[i] == '"')
            {
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        word.Append('"');
                        i += 2;
                    }
                    else if (line[i] == '"')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        word.Append(line[i++]);
                    }
                }
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i]))
                {
                    word.Append(line[i++]);
                }
            }

            tokens.Add(word.ToString());
        }

        return tokens;
    }
}
