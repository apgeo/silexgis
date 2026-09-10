// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading a club sheet whose bytes are not UTF-8. A archive that runs back far enough holds
/// spreadsheets saved on tools that wrote a Central European code page, and read as UTF-8 those
/// do not fail loudly — every accented letter turns into a replacement character and the reviewer
/// is left reading a page of names with the diacritics eaten.
/// </summary>
public class TripCsvEncodingsTests
{
    /// <summary>
    /// An invented two-row sheet in the shape these files arrive in, carrying the Romanian letters
    /// a legacy code page spells with a cedilla rather than a comma below.
    /// </summary>
    private const string SheetText =
        "Nr crt.,Data inceput,Titlu,Tara,Masiv/zona,Participanti,Tip\r\n"
        + "1,5/1/2024,Peştera Urşilor,România,M. Căpăţânii,\"Ion Anghel, Ana Marinescu\",pestera\r\n"
        + "2,5/2/2024,Avenul din Şesuri,România,M. Bihor,\"Ana Marinescu\",pestera\r\n";

    [Fact]
    public void A_sheet_in_an_older_code_page_reads_with_its_diacritics_intact()
    {
        var bytes = InCodePage(SheetText);

        var decoded = TripCsvEncodings.Decode(bytes, stated: null);

        // Nothing in the file states its encoding, so the answer is worked out and reported as
        // worked out — which is what lets the reviewer see that it was a guess at all.
        decoded.Encoding.ShouldBe(TripCsvEncoding.Windows1250);
        decoded.Source.ShouldBe(TripCsvEncodingSource.Guessed);
        decoded.Text.ShouldNotContain('�');

        var parsed = TripCsvParser.Parse(decoded.Text);
        parsed.Rows.Count.ShouldBe(2);
        parsed.Rows.ShouldAllBe(r => !r.HasError);
        parsed.Rows[0].Title.ShouldBe("Peştera Urşilor");
        parsed.Rows[0].Massif.ShouldBe("M. Căpăţânii");
        parsed.Rows[0].Country.ShouldBe("România");
        parsed.Rows[1].Title.ShouldBe("Avenul din Şesuri");
    }

    [Fact]
    public void The_same_bytes_read_as_utf8_lose_every_accented_letter()
    {
        // The negative half of the case above, constructed rather than assumed: these exact bytes
        // are what the importer used to hand the reviewer, and the damage is silent.
        var mangled = TripCsvEncodings.Decode(InCodePage(SheetText), TripCsvEncoding.Utf8);

        mangled.Source.ShouldBe(TripCsvEncodingSource.Stated);
        mangled.Text.ShouldContain('�');
        mangled.Text.ShouldNotContain("Peştera");
    }

    [Fact]
    public void The_reviewer_may_overrule_the_encoding_that_was_guessed()
    {
        var bytes = InCodePage(SheetText);

        var stated = TripCsvEncodings.Decode(bytes, TripCsvEncoding.Windows1252);

        stated.Encoding.ShouldBe(TripCsvEncoding.Windows1252);
        stated.Source.ShouldBe(TripCsvEncodingSource.Stated);

        // Western European reads the same bytes as different letters, so the override is visibly
        // in force rather than quietly ignored.
        var parsed = TripCsvParser.Parse(stated.Text);
        parsed.Rows[0].Massif.ShouldBe("M. Cãpãþânii");
        parsed.Rows[0].Title.ShouldBe("Peºtera Urºilor");
    }

    [Fact]
    public void A_sheet_written_in_utf8_is_read_as_utf8_and_says_so()
    {
        var decoded = TripCsvEncodings.Decode(Encoding.UTF8.GetBytes(SheetText), stated: null);

        decoded.Encoding.ShouldBe(TripCsvEncoding.Utf8);
        decoded.Source.ShouldBe(TripCsvEncodingSource.Utf8);
        decoded.Text.ShouldBe(SheetText);
    }

    [Fact]
    public void A_byte_order_mark_settles_the_encoding_and_is_not_left_in_the_first_header()
    {
        var withMark = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(SheetText)).ToArray();

        var decoded = TripCsvEncodings.Decode(withMark, stated: null);

        decoded.Encoding.ShouldBe(TripCsvEncoding.Utf8);
        decoded.Source.ShouldBe(TripCsvEncodingSource.Mark);
        TripCsvParser.Parse(decoded.Text).Header[0].ShouldBe("Nr crt.");
    }

    /// <summary>
    /// The sheet as Windows-1250 bytes. Written out by hand, and deliberately not by asking the
    /// decoder under test to run backwards: the five substitutions below are the whole of what
    /// separates this file from ASCII, and every one of them is a position Windows-1250 and
    /// ISO-8859-2 define identically, so the sheet is a fair test of choosing between them.
    /// </summary>
    private static byte[] InCodePage(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = text[i] switch
            {
                'ş' => 0xBA, // s with cedilla
                'Ş' => 0xAA, // capital s with cedilla
                'ţ' => 0xFE, // t with cedilla
                'ă' => 0xE3, // a with breve
                'â' => 0xE2, // a with circumflex
                'î' => 0xEE, // i with circumflex
                var c when c < 0x80 => (byte)c,
                var c => throw new InvalidOperationException(
                    $"The fixture carries U+{(int)c:X4}, which this hand-written table does not spell."),
            };
        }

        return bytes;
    }
}
