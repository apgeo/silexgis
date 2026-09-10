// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Import.TripCsv;

/// <summary>
/// The character encodings an uploaded trip sheet is read under. A closed set rather than a free
/// label, because the reviewer's override travels over the wire and a name nothing can decode has
/// to be refused before it reaches a decoder rather than after.
/// </summary>
public enum TripCsvEncoding
{
    /// <summary>UTF-8, with or without a byte-order mark. What anything current writes.</summary>
    Utf8 = 0,

    /// <summary>UTF-16, little-endian. Chosen only from a byte-order mark, or by hand.</summary>
    Utf16Le = 1,

    /// <summary>UTF-16, big-endian. Chosen only from a byte-order mark, or by hand.</summary>
    Utf16Be = 2,

    /// <summary>Windows-1250, the Central European code page a Windows spreadsheet of that era wrote.</summary>
    Windows1250 = 3,

    /// <summary>ISO-8859-2 (Latin-2), the same alphabet as written by Unix tools.</summary>
    Iso88592 = 4,

    /// <summary>Windows-1252, Western European. Reachable only when the reviewer names it.</summary>
    Windows1252 = 5,
}

/// <summary>What settled the encoding a sheet was read under.</summary>
public enum TripCsvEncodingSource
{
    /// <summary>The reviewer named it, and it was used as named rather than detected.</summary>
    Stated = 0,

    /// <summary>A byte-order mark at the head of the file named it.</summary>
    Mark = 1,

    /// <summary>No mark, but the bytes are well-formed UTF-8, which settles it exactly.</summary>
    Utf8 = 2,

    /// <summary>The bytes are not UTF-8; this is the code page they most likely carry.</summary>
    Guessed = 3,
}

/// <summary>A sheet's bytes as text, with the encoding used and what settled it.</summary>
/// <param name="Text">The characters, with any byte-order mark removed.</param>
/// <param name="Encoding">The encoding the bytes were read under.</param>
/// <param name="Source">Why that encoding, so a wrong guess can be seen and corrected.</param>
public sealed record TripCsvDecodedText(
    string Text, TripCsvEncoding Encoding, TripCsvEncodingSource Source);

/// <summary>
/// Turns an uploaded sheet's bytes into text.
///
/// <para>
/// A spreadsheet carries no trustworthy statement of its encoding, and a club archive that runs
/// back far enough holds sheets saved on tools that wrote a Central European code page rather
/// than UTF-8. Reading those as UTF-8 does not fail loudly — every accented letter becomes a
/// replacement character, and the reviewer is left reading a page of names with the diacritics
/// eaten. So the encoding is worked out rather than assumed.
/// </para>
/// <para>
/// The ladder is deliberately short: a byte-order mark is a statement by whoever wrote the file
/// and is believed; failing that, the bytes are tried as UTF-8 <em>strictly</em>, which is an
/// exact test rather than a heuristic, since a run of accented single-byte text is almost never
/// well-formed UTF-8 as well; and only what fails that is guessed at, between the two code pages
/// the material is actually written in. Deciding by a strict decode is the same test as looking
/// for replacement characters afterwards, made exact — the replacement character is precisely
/// what a lenient UTF-8 decode leaves where the strict one refuses.
/// </para>
/// <para>
/// A guess is reported alongside the text and the reviewer may overrule it. That is the point of
/// recording it: a mis-guessed single-byte encoding is invisible in the text itself — it reads as
/// the wrong accents, not as an error — so a guess nobody can see or correct is worse than none.
/// </para>
/// </summary>
public static class TripCsvEncodings
{
    /// <summary>
    /// The text these bytes hold, read under <paramref name="stated"/> where the reviewer named
    /// one, and otherwise under the encoding they most likely carry.
    /// </summary>
    public static TripCsvDecodedText Decode(ReadOnlySpan<byte> bytes, TripCsvEncoding? stated)
    {
        if (stated is { } chosen)
        {
            return new TripCsvDecodedText(DecodeAs(bytes, chosen), chosen, TripCsvEncodingSource.Stated);
        }

        var decoded = TextEncodings.Decode(bytes);
        var encoding = FromLabel(decoded.Encoding);
        var source = HasByteOrderMark(bytes)
            ? TripCsvEncodingSource.Mark
            : encoding == TripCsvEncoding.Utf8
                ? TripCsvEncodingSource.Utf8
                : TripCsvEncodingSource.Guessed;

        return new TripCsvDecodedText(decoded.Text, encoding, source);
    }

    /// <summary>Reads the bytes under one named encoding, whatever they actually are.</summary>
    /// <remarks>
    /// Lenient on purpose. This path is the reviewer saying "read it as this", and answering a
    /// deliberate instruction with a failed upload teaches nothing; text with the wrong accents
    /// in it is visible evidence that the choice was wrong, and the choice can be changed again.
    /// </remarks>
    private static string DecodeAs(ReadOnlySpan<byte> bytes, TripCsvEncoding encoding) => encoding switch
    {
        TripCsvEncoding.Utf8 => Encoding.UTF8.GetString(StripMark(bytes, 0xEF, 0xBB, 0xBF)),
        TripCsvEncoding.Utf16Le => Encoding.Unicode.GetString(StripMark(bytes, 0xFF, 0xFE)),
        TripCsvEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(StripMark(bytes, 0xFE, 0xFF)),
        TripCsvEncoding.Windows1250 => TextEncodings.DecodeSingleByte(bytes, TextEncodings.Windows1250),
        TripCsvEncoding.Iso88592 => TextEncodings.DecodeSingleByte(bytes, TextEncodings.Iso88592),
        TripCsvEncoding.Windows1252 => TextEncodings.DecodeSingleByte(bytes, TextEncodings.Windows1252),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Not an encoding a sheet is read under."),
    };

    private static ReadOnlySpan<byte> StripMark(ReadOnlySpan<byte> bytes, params byte[] mark) =>
        bytes.StartsWith(mark) ? bytes[mark.Length..] : bytes;

    private static bool HasByteOrderMark(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith([(byte)0xEF, (byte)0xBB, (byte)0xBF])
        || bytes.StartsWith([(byte)0xFF, (byte)0xFE])
        || bytes.StartsWith([(byte)0xFE, (byte)0xFF]);

    private static TripCsvEncoding FromLabel(string label) => label switch
    {
        TextEncodings.Utf16Le => TripCsvEncoding.Utf16Le,
        TextEncodings.Utf16Be => TripCsvEncoding.Utf16Be,
        TextEncodings.Windows1250 => TripCsvEncoding.Windows1250,
        TextEncodings.Iso88592 => TripCsvEncoding.Iso88592,
        TextEncodings.Windows1252 => TripCsvEncoding.Windows1252,
        _ => TripCsvEncoding.Utf8,
    };
}
