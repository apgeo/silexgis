// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading text out of Rich Text Format. Everything asserted here is a way the format can put
/// something that is not text where text goes — a table of fonts, a picture's bytes, a code
/// point spelled twice for two kinds of reader — and every one of them ends up in the search
/// index if it is not removed.
/// </summary>
public class RichTextFormatTests
{
    [Fact]
    public void Paragraphs_become_line_breaks()
    {
        Extract(@"{\rtf1\ansi\ansicpg1252 Entrance survey\par Second visit}")
            .ShouldBe("Entrance survey\nSecond visit");
    }

    [Fact]
    public void The_font_and_colour_tables_are_not_text()
    {
        Extract(@"{\rtf1\ansi{\fonttbl{\f0\froman Times New Roman;}}"
            + @"{\colortbl;\red255\green0\blue0;}Body}")
            .ShouldBe("Body");
    }

    [Fact]
    public void A_destination_marked_ignorable_is_skipped_whole()
    {
        Extract(@"{\rtf1\ansi Before{\*\generator Riched20 10.0.19041;}After")
            .ShouldBe("BeforeAfter");
    }

    /// <summary>
    /// The escape carries a byte, and which letter that byte is depends on the code page the
    /// file declared. Reading it under the wrong one is the same silent corruption a plain text
    /// file suffers, so the declaration is honoured rather than assumed.
    /// </summary>
    [Fact]
    public void Escaped_bytes_are_read_in_the_code_page_the_file_declares()
    {
        Extract(@"{\rtf1\ansi\ansicpg1250 Pe\'BAtera}").ShouldBe("Peştera");
        Extract(@"{\rtf1\ansi\ansicpg1252 Caf\'e9}").ShouldBe("Café");
    }

    /// <summary>
    /// A Unicode escape is followed by the same character written for a reader that predates
    /// Unicode. Emitting both would double every accented letter in the document.
    /// </summary>
    [Fact]
    public void A_unicode_escape_replaces_its_own_fallback()
    {
        Extract(@"{\rtf1\ansi \u350?ura Mare}").ShouldBe("Şura Mare");
        Extract(@"{\rtf1\ansi\uc2 \u350\'3f\'3fura}").ShouldBe("Şura");
        Extract(@"{\rtf1\ansi\uc0 \u350 ura}").ShouldBe("Şura");
    }

    [Fact]
    public void Braces_and_backslashes_can_be_written_literally()
    {
        Extract(@"{\rtf1\ansi a\\b\{c\}d}").ShouldBe(@"a\b{c}d");
    }

    /// <summary>
    /// Binary runs declare their length instead of being delimited, so a reader that treated
    /// them as syntax would resume parsing in the middle of a picture.
    /// </summary>
    [Fact]
    public void A_binary_run_is_measured_and_skipped()
    {
        Extract(@"{\rtf1\ansi A\bin3 XYZB}").ShouldBe("AB");
    }

    /// <summary>
    /// The length of a binary run is whatever the file says it is, and a file that came from
    /// outside may say a number larger than the document. Adding it to the current position
    /// must not be allowed to wrap: a negative index still satisfies the loop's own bound and
    /// the next byte read would be off the front of the document.
    /// </summary>
    [Theory]
    [InlineData(@"{\rtf1\ansi A\bin9999999999 XYZB}")]
    [InlineData(@"{\rtf1\ansi A\bin2147483647 XYZB}")]
    public void A_binary_run_longer_than_the_document_ends_it(string rtf)
    {
        // Everything after the declared run is inside it as far as the file is concerned, so
        // the text is what came before — and reading stops rather than failing.
        Extract(rtf).ShouldBe("A");
    }

    [Fact]
    public void A_group_ends_the_skipping_it_started()
    {
        Extract(@"{\rtf1\ansi{\fonttbl{\f0 Times;}}Kept{\*\pn none}Also kept}")
            .ShouldBe("KeptAlso kept");
    }

    [Fact]
    public void Control_words_that_stand_for_a_character_produce_it()
    {
        Extract(@"{\rtf1\ansi A\tab B\line C\emdash D\ldblquote E\rdblquote}")
            .ShouldBe("A\tB\nC—D“E”");
    }

    /// <summary>
    /// A writer may break the file's own lines anywhere, and the format says a reader ignores
    /// those breaks outright — the space between two words is written as a space, not implied
    /// by the break. Turning them into spaces instead would insert one wherever a writer
    /// happened to wrap.
    /// </summary>
    [Fact]
    public void Source_line_breaks_are_not_document_line_breaks()
    {
        Extract("{\\rtf1\\ansi One \r\ntwo \r\nthree}").ShouldBe("One two three");
        Extract("{\\rtf1\\ansi Un\r\nbroken}").ShouldBe("Unbroken");
    }

    [Fact]
    public void A_truncated_document_yields_what_it_held_rather_than_failing()
    {
        Extract(@"{\rtf1\ansi Partial\'B").ShouldBe("Partial");
        Extract(@"{\rtf1\ansi Partial\").ShouldBe("Partial");
    }

    /// <summary>Latin-1 keeps every byte's value, which is what an RTF file's own syntax is.</summary>
    private static string Extract(string rtf) =>
        RichTextFormat.ToPlainText(Encoding.Latin1.GetBytes(rtf));
}
