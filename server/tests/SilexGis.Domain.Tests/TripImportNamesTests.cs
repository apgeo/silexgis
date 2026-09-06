// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a name written in a spreadsheet is compared, and which names are enough of a name to make
/// a person out of. The asymmetry is the subject: everything is matched, only some things are
/// created, and the reason is that one of the two mistakes cannot be taken back.
/// </summary>
public class TripImportNamesTests
{
    [Theory]
    [InlineData("Ioana Câmpioana", "ioana campioana")]
    [InlineData("IOANA CAMPIOANA", "ioana campioana")]
    [InlineData("  Ioana   Campioana ", "ioana campioana")]
    [InlineData("Peștera Urșilor", "pestera ursilor")]
    // Both spellings of the Romanian s: the comma-below letter and the cedilla one older
    // keyboards produced. A club sheet carries whichever the machine that typed it wrote.
    [InlineData("Peştera Urşilor", "pestera ursilor")]
    public void Two_spellings_of_one_name_compare_as_one(string written, string expected) =>
        TripImportNames.Key(written).ShouldBe(expected);

    [Fact]
    public void An_empty_cell_has_no_key() => TripImportNames.Key("   ").ShouldBe(string.Empty);

    [Theory]
    [InlineData("Ion Anghel")]
    [InlineData("Ioana Câmpioana")]
    [InlineData("Ana Maria Popescu")]
    public void A_full_name_may_become_a_person(string name) =>
        TripImportNames.MayCreatePerson(name).ShouldBeTrue();

    [Theory]
    // The bare initial: every import would otherwise invent a fresh person under this name,
    // and no later merge could say which of them was which.
    [InlineData("Ion A.")]
    [InlineData("A. Ion")]
    [InlineData("I. A.")]
    // A single given name identifies nobody in a club that has two of them.
    [InlineData("Mihai")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_initial_or_a_lone_word_creates_nobody(string name) =>
        TripImportNames.MayCreatePerson(name).ShouldBeFalse();

    [Fact]
    public void Refusing_to_create_is_not_refusing_to_match()
    {
        // The same name that may create nobody still compares, so a person already in the
        // roster under it is found rather than duplicated.
        TripImportNames.MayCreatePerson("Ion A.").ShouldBeFalse();
        TripImportNames.Key("Ion A.").ShouldBe(TripImportNames.Key("ion a."));
    }
}
