// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a written record says which person it means. A trip's people and a camp's stays both
/// answer to this one rule, so what it accepts is pinned here rather than once per surface.
/// </summary>
public class CaverReferenceRulesTests
{
    private static readonly Guid Somebody = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void An_entry_in_the_directory_names_one_person() =>
        CaverReferenceRules.NamesOnePerson(Somebody, null).ShouldBeTrue();

    [Fact]
    public void A_name_names_one_person() =>
        CaverReferenceRules.NamesOnePerson(null, "Ana Pop").ShouldBeTrue();

    [Fact]
    public void An_entry_and_a_name_together_name_nobody_in_particular()
    {
        // Two ways of saying who, and no rule for which one wins: the entry may be one person and
        // the name another. Refused rather than resolved in favour of either.
        CaverReferenceRules.NamesOnePerson(Somebody, "Ana Pop").ShouldBeFalse();
    }

    [Fact]
    public void Neither_an_entry_nor_a_name_names_nobody() =>
        CaverReferenceRules.NamesOnePerson(null, null).ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_name_of_nothing_but_spaces_is_no_name(string blank)
    {
        // On its own it names nobody — and it must not, or it would become a person with no name
        // that nobody could ever find again to merge away.
        CaverReferenceRules.NamesOnePerson(null, blank).ShouldBeFalse();

        // Beside an entry it changes nothing: the entry is the one way the row says who.
        CaverReferenceRules.NamesOnePerson(Somebody, blank).ShouldBeTrue();
    }
}
