// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// The language a document is written in, as the code stored on the document row.
/// <para>
/// Only the primary language subtag is kept. A stemmer is chosen for a language, not for a
/// place: Romanian written in Moldova stems exactly as Romanian written in Romania, so
/// "ro-MD", "ro-RO" and "ro" all resolve to the same answer and storing them apart would only
/// create three ways to miss the same lookup. Region, script and variant subtags are therefore
/// dropped rather than preserved, which is a deliberate narrowing of BCP-47 to the part that
/// changes behaviour.
/// </para>
/// <para>
/// The code is never authoritative. It is detected from the text and left correctable by whoever
/// owns the document, and an unrecognised or absent code is not an error - it falls back to
/// language-neutral indexing, which is what the rest of this installation's search already does.
/// </para>
/// </summary>
public static class DocumentLanguage
{
    /// <summary>
    /// Longest primary language subtag BCP-47 allows (2-8 ASCII letters), which is also the
    /// stored column width.
    /// </summary>
    public const int MaxLength = 8;

    /// <summary>
    /// Reduces a user-supplied or detected tag to the stored form: lower-case primary subtag,
    /// or null when there is nothing usable. Null rather than a sentinel, because "nobody has
    /// said" and "said, and it was not a language" deserve the same treatment - both index
    /// language-neutrally - and neither is worth a row of its own.
    /// </summary>
    public static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.Trim();

        // Both separators appear in the wild: BCP-47 writes "ro-RO", POSIX locales "ro_RO".
        var end = trimmed.AsSpan().IndexOfAny('-', '_');
        var primary = end < 0 ? trimmed : trimmed[..end];

        if (primary.Length is < 2 or > MaxLength)
        {
            return null;
        }

        foreach (var c in primary)
        {
            if (!char.IsAsciiLetter(c))
            {
                return null;
            }
        }

        return primary.ToLowerInvariant();
    }
}
