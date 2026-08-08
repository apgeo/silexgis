// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Domain.Import;

/// <summary>
/// A string folded to the form terms are compared in — lower case, no diacritics — kept
/// alongside a map back to the original.
///
/// <para>
/// The map is what makes stripping the matched term out of a name safe. Folding is not
/// guaranteed to preserve character positions: a precomposed letter decomposes into a base
/// plus one or more combining marks, and dropping the marks shortens the string. For Latin
/// text the two happen to line up, but a name carrying anything else would silently cut at
/// the wrong offset — a bug that appears only for somebody else's alphabet, which is the
/// worst kind. Recording where each folded character came from costs one array and removes
/// the class of error entirely.
/// </para>
/// </summary>
public sealed class FoldedText
{
    private FoldedText(string value, int[] sourceIndex, int sourceLength)
    {
        Value = value;
        this.sourceIndex = sourceIndex;
        SourceLength = sourceLength;
    }

    private readonly int[] sourceIndex;

    /// <summary>The folded form: lower case, diacritics removed.</summary>
    public string Value { get; }

    /// <summary>Length of the string this was folded from.</summary>
    public int SourceLength { get; }

    public static FoldedText Empty { get; } = new(string.Empty, [], 0);

    public static FoldedText Of(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Empty;
        }

        // Decomposing first is what turns ş (cedilla, what older Windows fonts produced for
        // Romanian) and ș (comma below, the correct letter) into the same base character —
        // field GPS units and desktop tools disagree about which one they write, and a term
        // list must not have to carry both.
        var builder = new StringBuilder(value.Length);
        var indices = new List<int>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var decomposed = value[i].ToString().Normalize(NormalizationForm.FormD);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
                indices.Add(i);
            }
        }

        return new FoldedText(builder.ToString(), [.. indices], value.Length);
    }

    /// <summary>The index in the original string that folded character <paramref name="index"/> came from.</summary>
    public int SourceIndexOf(int index) => sourceIndex[index];

    /// <summary>
    /// The half-open range of the original string covered by a folded range. The end is
    /// exclusive and lands after the last original character the folded range touched, so a
    /// letter that folded to several characters is never cut in half.
    /// </summary>
    public (int Start, int End) SourceRange(int start, int length)
    {
        if (length <= 0 || sourceIndex.Length == 0)
        {
            return (0, 0);
        }

        var first = sourceIndex[start];
        var lastFolded = Math.Min(start + length, sourceIndex.Length) - 1;
        var last = sourceIndex[lastFolded];
        return (first, last + 1);
    }

    /// <summary>True when the folded character at <paramref name="index"/> is part of a word.</summary>
    public bool IsWordCharacter(int index) =>
        index >= 0 && index < Value.Length && (char.IsLetterOrDigit(Value[index]) || Value[index] == '_');
}
