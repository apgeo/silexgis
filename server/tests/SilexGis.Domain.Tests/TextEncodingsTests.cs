// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading text out of bytes nobody labelled. The failure this guards against is silent: a
/// file read under the wrong single-byte encoding still produces text, with the accented
/// letters turned into other characters, and the damage is only visible to someone who tries
/// to search for a word that contains one.
/// </summary>
public class TextEncodingsTests
{
    [Fact]
    public void Ascii_reads_as_utf8()
    {
        var decoded = TextEncodings.Decode("Peak cave survey"u8);

        decoded.Text.ShouldBe("Peak cave survey");
        decoded.Encoding.ShouldBe(TextEncodings.Utf8);
    }

    [Fact]
    public void Utf8_diacritics_survive_and_are_recognised_as_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("Peștera Șura Mare, județul Hunedoara");

        var decoded = TextEncodings.Decode(bytes);

        decoded.Text.ShouldBe("Peștera Șura Mare, județul Hunedoara");
        decoded.Encoding.ShouldBe(TextEncodings.Utf8);
    }

    [Fact]
    public void A_byte_order_mark_is_believed_and_removed()
    {
        byte[] utf8 = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Cheile Turzii")];
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Cheile Turzii")];
        byte[] utf16Be = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Cheile Turzii")];

        var fromUtf8 = TextEncodings.Decode(utf8);
        var fromUtf16 = TextEncodings.Decode(utf16);
        var fromUtf16Be = TextEncodings.Decode(utf16Be);

        fromUtf8.Text.ShouldBe("Cheile Turzii");
        fromUtf8.Encoding.ShouldBe(TextEncodings.Utf8);
        fromUtf16.Text.ShouldBe("Cheile Turzii");
        fromUtf16.Encoding.ShouldBe(TextEncodings.Utf16Le);
        fromUtf16Be.Text.ShouldBe("Cheile Turzii");
        fromUtf16Be.Encoding.ShouldBe(TextEncodings.Utf16Be);
    }

    /// <summary>
    /// The legacy Romanian case. Both Central European code pages put the s- and t-cedilla
    /// letters in the same places, so the text comes out right either way — what matters is
    /// that the bytes are not read as UTF-8, which they are not valid as.
    /// </summary>
    [Fact]
    public void Romanian_written_in_a_central_european_code_page_keeps_its_letters()
    {
        // "Peştera Şura Mare" with s-cedilla at 0xBA and its capital at 0xAA.
        byte[] bytes =
        [
            0x50, 0x65, 0xBA, 0x74, 0x65, 0x72, 0x61, 0x20,
            0xAA, 0x75, 0x72, 0x61, 0x20, 0x4D, 0x61, 0x72, 0x65,
        ];

        var decoded = TextEncodings.Decode(bytes);

        decoded.Text.ShouldBe("Peştera Şura Mare");
        decoded.Encoding.ShouldBe(TextEncodings.Windows1250);
    }

    /// <summary>
    /// Bytes only Windows-1250 defines settle the question outright: ISO-8859-2 has control
    /// characters there, and no one types a control character into a survey report.
    /// </summary>
    [Fact]
    public void Typographic_quotes_identify_windows_1250()
    {
        // 0x93 and 0x94 are the curly double quotes; 0x96 is an en dash.
        byte[] bytes = [0x93, 0x53, 0x61, 0x6C, 0x61, 0x94, 0x20, 0x96, 0x20, 0x31];

        var decoded = TextEncodings.Decode(bytes);

        decoded.Encoding.ShouldBe(TextEncodings.Windows1250);
        decoded.Text.ShouldBe("“Sala” – 1");
    }

    /// <summary>
    /// The two code pages disagree over 0xA0-0xBF, and the bytes that are letters in only one
    /// of them are the evidence. The same run read as Windows-1250 is punctuation, which is
    /// what makes the choice decidable rather than arbitrary.
    /// </summary>
    [Fact]
    public void Letters_that_exist_only_in_latin2_choose_iso_8859_2()
    {
        // 0xB1 and 0xB6 are a-ogonek and s-acute in ISO-8859-2, but a plus-minus sign and a
        // pilcrow in Windows-1250.
        byte[] bytes = [0x54, 0xB1, 0xB6, 0x6B, 0xB1];

        var decoded = TextEncodings.Decode(bytes);

        decoded.Encoding.ShouldBe(TextEncodings.Iso88592);
        decoded.Text.ShouldBe("Tąśką");
        TextEncodings.DecodeSingleByte(bytes, TextEncodings.Windows1250)
            .ShouldBe("T±¶k±");
    }

    [Fact]
    public void The_two_code_pages_agree_on_every_letter_above_0xBF()
    {
        var bytes = new byte[0x40];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(0xC0 + i);
        }

        TextEncodings.DecodeSingleByte(bytes, TextEncodings.Windows1250)
            .ShouldBe(TextEncodings.DecodeSingleByte(bytes, TextEncodings.Iso88592));
    }

    [Fact]
    public void Naming_an_encoding_this_decoder_has_no_table_for_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => TextEncodings.DecodeSingleByte([0x41], "koi8-r"));
    }
}
