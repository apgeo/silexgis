// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Documents;

/// <summary>Text recovered from a run of bytes, together with the encoding it was read as.</summary>
/// <param name="Text">The decoded characters, with any byte-order mark removed.</param>
/// <param name="Encoding">
/// The encoding label, from the fixed set <see cref="TextEncodings"/> can produce. Recorded
/// rather than discarded because a mis-guessed single-byte encoding is invisible in the text
/// itself - it reads as the wrong accents - and the only way to review the guess later is to
/// know which one was made.
/// </param>
public sealed record DecodedText(string Text, string Encoding);

/// <summary>
/// Reads plain text out of bytes whose encoding nobody recorded.
/// <para>
/// An upload carries no trustworthy declaration of its character encoding: the browser's
/// <c>Content-Type</c> charset parameter is whatever the client sent, is dropped when the
/// media type is normalised, and is absent altogether from a file copied off a disk. Guessing
/// UTF-8 is wrong often enough to matter here - Romanian cave archives run back to the 1990s
/// and their text files are Windows-1250 and ISO-8859-2, which decode as UTF-8 either not at
/// all or as mojibake.
/// </para>
/// <para>
/// The single-byte tables are written out here rather than obtained from the runtime. .NET
/// carries only UTF-8/16/32, ASCII and Latin-1 in the box; every other code page needs an
/// extra package and a provider registration at startup, for fixed 128-entry tables that have
/// not changed since they were standardised. Writing them down costs less than the dependency
/// and makes the mapping reviewable. Each row is annotated with the code points it covers, and
/// the unit tests pin the entries that carry Romanian and Central European letters - several
/// positions are a no-break space, a soft hyphen or an undefined slot and are invisible on the
/// page, so the annotation and the tests are what makes them reviewable, not the glyphs.
/// </para>
/// </summary>
public static class TextEncodings
{
    /// <summary>UTF-8, with or without a byte-order mark.</summary>
    public const string Utf8 = "utf-8";

    /// <summary>UTF-16, little-endian; only ever chosen from a byte-order mark.</summary>
    public const string Utf16Le = "utf-16le";

    /// <summary>UTF-16, big-endian; only ever chosen from a byte-order mark.</summary>
    public const string Utf16Be = "utf-16be";

    /// <summary>Windows-1250, the Central European code page written by Windows tools.</summary>
    public const string Windows1250 = "windows-1250";

    /// <summary>ISO-8859-2 (Latin-2), the Central European code page written by Unix tools.</summary>
    public const string Iso88592 = "iso-8859-2";

    /// <summary>Windows-1252, Western European; reachable only when a caller names it.</summary>
    public const string Windows1252 = "windows-1252";

    /// <summary>
    /// Code points 0xC0-0xFF, which Windows-1250 and ISO-8859-2 define identically. Every
    /// Romanian letter an archive of this age can carry lives in here or at 0xAA/0xBA/0xDE/0xFE,
    /// which the two also agree on - so the two encodings differ over punctuation and over
    /// Polish, Czech and Slovak letters, not over Romanian ones.
    /// </summary>
    private const string CentralEuropeanLetters =
        "ŔÁÂĂÄĹĆÇ"   // C0-C7  R' A' A^ A( A: L' C' C,
        + "ČÉĘËĚÍÎĎ" // C8-CF  C< E' E; E: E< I' I^ D<
        + "ĐŃŇÓÔŐÖ×" // D0-D7  D/ N' N< O' O^ Odbl Odia x
        + "ŘŮÚŰÜÝŢß" // D8-DF  R< U0 U' Udbl Udia Y' T, ss
        + "ŕáâăäĺćç" // E0-E7  r' a' a^ a( a: l' c' c,
        + "čéęëěíîď" // E8-EF  c< e' e; e: e< i' i^ d<
        + "đńňóôőö÷" // F0-F7  d/ n' n< o' o^ odbl odia div
        + "řůúűüýţ˙"; // F8-FF  r< u0 u' udbl udia y' t, dot

    /// <summary>
    /// Windows-1250, code points 0x80-0xFF. Positions the code page leaves undefined are
    /// mapped to the replacement character so a byte is never silently turned into a
    /// different, valid letter.
    /// </summary>
    private const string Cp1250High =
        "€�‚�„…†‡"   // 80-87  euro _ lowq _ lowqq ellipsis dag ddag
        + "�‰Š‹ŚŤŽŹ" // 88-8F  _ permille S< < S' T' Z< Z'
        + "�‘’“”•–—" // 90-97  _ ' ' q q bullet en em
        + "�™š›śťžź" // 98-9F  _ tm s< > s' t' z< z'
        + " ˇ˘Ł¤Ą¦§" // A0-A7  nbsp caron breve L/ cur A; brk sect
        + "¨©Ş«¬­®Ż" // A8-AF  diaer (c) S, << not shy (r) Z.
        + "°±˛ł´µ¶·" // B0-B7  deg +- ogon l/ acute mu para midd
        + "¸ąş»Ľ˝ľż" // B8-BF  ced a; s, >> L< dblac l< z.
        + CentralEuropeanLetters;

    /// <summary>ISO-8859-2, code points 0xA0-0xFF. Everything below 0xA0 is its own code point.</summary>
    private const string Iso88592High =
        " Ą˘Ł¤ĽŚ§"   // A0-A7  nbsp A; breve L/ cur L< S' sect
        + "¨ŠŞŤŹ­ŽŻ" // A8-AF  diaer S< S, T' Z' shy Z< Z.
        + "°ą˛ł´ľśˇ" // B0-B7  deg a; ogon l/ acute l< s' caron
        + "¸šşťź˝žż" // B8-BF  ced s< s, t' z' dblac z< z.
        + CentralEuropeanLetters;

    /// <summary>
    /// Windows-1252, code points 0x80-0x9F. The rest of the code page is Latin-1, so only this
    /// range needs writing down. Carried because rich text files name their code page and 1252
    /// is the one Western European writers' tools stamped into them.
    /// </summary>
    private const string Cp1252Punctuation =
        "€�‚ƒ„…†‡"   // 80-87
        + "ˆ‰Š‹Œ�Ž�" // 88-8F
        + "�‘’“”•–—" // 90-97
        + "˜™š›œ�žŸ"; // 98-9F

    /// <summary>Decodes UTF-8 strictly: invalid bytes raise rather than becoming U+FFFD.</summary>
    /// <remarks>
    /// The shared <see cref="Encoding.UTF8"/> instance substitutes the replacement character
    /// for anything it cannot read, so it "succeeds" on single-byte text and is useless as a
    /// test. This instance is the only positive evidence in the ladder below.
    /// </remarks>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The text these bytes hold, decoded under the encoding they most likely carry.
    /// <para>
    /// The order is deliberate: a byte-order mark is a statement by whoever wrote the file and
    /// is believed; valid UTF-8 is checked next because that check is exact rather than
    /// heuristic - a run of accented single-byte text is almost never a well-formed multi-byte
    /// sequence as well - and only what fails both is guessed at.
    /// </para>
    /// </summary>
    public static DecodedText Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            // The mark is a claim about the whole file, so a damaged tail inside it is a
            // damaged file rather than evidence that the claim was about another encoding.
            return new DecodedText(Encoding.UTF8.GetString(bytes[3..]), Utf8);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new DecodedText(Encoding.Unicode.GetString(bytes[2..]), Utf16Le);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes[2..]), Utf16Be);
        }

        try
        {
            return new DecodedText(StrictUtf8.GetString(bytes), Utf8);
        }
        catch (DecoderFallbackException)
        {
            var encoding = GuessSingleByte(bytes);
            return new DecodedText(DecodeSingleByte(bytes, encoding), encoding);
        }
    }

    /// <summary>
    /// Decodes under a named single-byte encoding, for callers that were told which one to use
    /// - a rich text file naming its code page, say - rather than having to guess.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The encoding is not one of the single-byte ones this decoder carries a table for.
    /// </exception>
    public static string DecodeSingleByte(ReadOnlySpan<byte> bytes, string encoding)
    {
        var table = encoding switch
        {
            Windows1250 => Cp1250High,
            Iso88592 => Iso88592High,
            Windows1252 => Cp1252Punctuation,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding,
                "Not a single-byte encoding this decoder knows."),
        };

        // Where the table starts. Windows-1250 and -1252 redefine the range the C1 control
        // characters occupy; ISO-8859-2 leaves it alone. Anything the table does not cover
        // keeps its own value, which is correct for ASCII and for Latin-1's upper half.
        var first = encoding == Iso88592 ? 0xA0 : 0x80;
        var buffer = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            int b = bytes[i];
            var index = b - first;
            buffer[i] = index >= 0 && index < table.Length ? table[index] : (char)b;
        }

        return new string(buffer);
    }

    /// <summary>
    /// Which of the two Central European code pages a run of non-UTF-8 bytes is more likely to be.
    /// </summary>
    /// <remarks>
    /// Two signals, in order. Any byte in 0x80-0x9F settles it: ISO-8859-2 defines that range
    /// as C1 control characters, which do not appear in text anyone typed, while Windows-1250
    /// puts curly quotes, dashes and several letters there - so their presence means
    /// Windows-1250 and their absence says nothing. Failing that, the range the two disagree
    /// over (0xA0-0xBF) is scored by how many of the bytes present land on a letter rather than
    /// on punctuation or a spacing accent, because prose is made of letters. A tie goes to
    /// Windows-1250, which is what the overwhelming majority of legacy Romanian material was
    /// written on.
    /// </remarks>
    private static string GuessSingleByte(ReadOnlySpan<byte> bytes)
    {
        var windowsLetters = 0;
        var isoLetters = 0;
        foreach (var b in bytes)
        {
            if (b is >= 0x80 and <= 0x9F)
            {
                return Windows1250;
            }

            if (b is >= 0xA0 and <= 0xBF)
            {
                if (char.IsLetter(Cp1250High[b - 0x80]))
                {
                    windowsLetters++;
                }

                if (char.IsLetter(Iso88592High[b - 0xA0]))
                {
                    isoLetters++;
                }
            }
        }

        return isoLetters > windowsLetters ? Iso88592 : Windows1250;
    }
}
