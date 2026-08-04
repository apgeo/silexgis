// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The stored shape of a page's text. Two readings of an unchanged page have to compare as
/// equal, or a re-extraction rewrites every page of every document and nothing that points at
/// stored text can tell a real change from a reformatting.
/// </summary>
public class PageTextTests
{
    [Fact]
    public void Line_endings_are_unified_and_trailing_space_dropped()
    {
        PageText.Normalize("Entrance   \r\nSurvey\t\r\n").ShouldBe("Entrance\nSurvey");
    }

    [Fact]
    public void Runs_of_blank_lines_become_one()
    {
        PageText.Normalize("First\n\n\n\n\nSecond").ShouldBe("First\n\nSecond");
    }

    [Fact]
    public void A_page_with_nothing_readable_on_it_is_null_rather_than_empty()
    {
        PageText.Normalize(null).ShouldBeNull();
        PageText.Normalize(string.Empty).ShouldBeNull();
        PageText.Normalize("   \n\t\n  ").ShouldBeNull();
    }

    [Fact]
    public void Tabs_survive_but_other_control_characters_do_not()
    {
        // Written as escapes: a literal control byte here would make this file binary to git.
        PageText.Normalize("a\tb\u0001c\u0007d").ShouldBe("a\tbcd");
    }

    [Fact]
    public void Text_beyond_the_cap_is_cut_rather_than_kept()
    {
        var text = new string('x', PageText.MaxCharacters + 500);

        PageText.Normalize(text)!.Length.ShouldBe(PageText.MaxCharacters);
    }
}
