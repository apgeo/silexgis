// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using SilexGis.Domain.Trips;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// The part of writing a document from a layout that is the same whatever is being written up.
/// </summary>
/// <remarks>
/// Two kinds of thing are written up from the same small language, and how a line behaves when one
/// of its names has nothing behind it is a rule about the language rather than about trips or
/// camps. Written once here so the two cannot come to disagree: the day one of them started
/// printing a dangling separator, or keeping a heading nothing came out under, would be the day a
/// club's two documents stopped looking like one product — and nothing would fail to say so.
///
/// What each name means is emphatically <em>not</em> here. That is the half where the disclosure
/// rules live, it is answered out of a reading its producer already has, and it stays beside the
/// thing being written up.
/// </remarks>
public static class ReportComposition
{
    /// <summary>
    /// What a line of the layout says once its names have been filled in, or null when it says
    /// nothing and the line should not be written at all.
    /// </summary>
    /// <param name="text">The line as the layout wrote it, braces and all.</param>
    /// <param name="resolve">
    /// What one name says, or null for nothing. Null and empty mean the same thing here: a name
    /// nothing was recorded under and a name this reader is not given are indistinguishable on
    /// purpose, so a layout cannot be used to find out which of the two happened.
    /// </param>
    /// <remarks>
    /// A name with nothing behind it takes the punctuation written next to it with it, so a line
    /// reading "{purpose} · {dates} · {club}" with no club comes out without a dangling separator.
    /// Only punctuation standing on its own between two names is touched; nothing alters the words
    /// a person wrote, and nothing collapses the line breaks inside prose.
    /// </remarks>
    public static string? Fill(string text, Func<string, string?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        var written = new StringBuilder();
        string? waiting = null;
        var anything = false;

        foreach (var token in ReportTemplateFormat.Tokens(text))
        {
            if (!token.IsPlaceholder)
            {
                waiting += token.Text;
                continue;
            }

            var value = resolve(token.Text);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (waiting is not null && !HasWords(waiting))
                {
                    waiting = null;
                }

                continue;
            }

            if (waiting is not null && (anything || HasWords(waiting)))
            {
                written.Append(waiting);
            }

            waiting = null;
            written.Append(value);
            anything = true;
        }

        if (waiting is not null && HasWords(waiting))
        {
            written.Append(waiting);
        }

        var filled = written.ToString().Trim();
        return HasWords(filled) ? filled : null;
    }

    /// <summary>Whether a piece of text says anything at all, rather than only punctuation.</summary>
    public static bool HasWords(string text) =>
        text is not null && text.Any(char.IsLetterOrDigit);

    /// <summary>
    /// Takes out every heading nothing came out under.
    /// </summary>
    /// <remarks>
    /// A layout asks for a part before it can know whether there is anything to put in it, so an
    /// empty part is ordinary rather than a mistake — and a bare "Safety" heading on a circulated
    /// document reads as "nothing happened", which is a different statement from the one the record
    /// actually makes.
    /// </remarks>
    public static List<DocumentBlock> Pruned(List<DocumentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var kept = new List<DocumentBlock>(blocks.Count);
        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index].Kind != DocumentBlockKind.Heading)
            {
                kept.Add(blocks[index]);
                continue;
            }

            var next = index + 1;
            if (next < blocks.Count && blocks[next].Kind != DocumentBlockKind.Heading)
            {
                kept.Add(blocks[index]);
            }
        }

        return kept;
    }
}
