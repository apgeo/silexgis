// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Catalogue;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The basin tree carried from the catalogue's own site, because its programmatic interface
/// answers a cave's basin as a bare number and offers no way to resolve it.
/// </summary>
public class SpeologieBasinsTests
{
    /// <summary>
    /// The table is whole. Asserted because it is scraped from a page that is not a documented
    /// interface: the way this breaks is quietly, with a table that read half the tree.
    /// </summary>
    [Fact]
    public void The_tree_is_complete_and_every_parent_resolves()
    {
        SpeologieBasins.All.Count.ShouldBeGreaterThan(600);
        SpeologieBasins.ById.Count.ShouldBe(SpeologieBasins.All.Count);

        var orphans = SpeologieBasins.All
            .Where(b => b.ParentId is not null && !SpeologieBasins.ById.ContainsKey(b.ParentId.Value))
            .ToArray();

        orphans.ShouldBeEmpty();
        SpeologieBasins.All.ShouldAllBe(b => b.Depth >= 1 && b.Depth <= 8);
        SpeologieBasins.All.Count(b => b.ParentId is null).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The value the whole table exists for: an identifier a cave record carries becomes a place.
    /// </summary>
    [Fact]
    public void A_basin_identifier_becomes_a_name_and_a_way_down_to_it()
    {
        var padis = SpeologieBasins.Find(605);

        padis.ShouldNotBeNull();
        padis.Name.ShouldBe("3440 - Bazinul Padiş");
        padis.Code.ShouldBe("3440");
        padis.Label.ShouldBe("Bazinul Padiş");

        // The path is what places the cave for a reader who has never heard of the basin.
        var path = SpeologieBasins.PathOf(605);
        path.ShouldNotBeNull();
        path.ShouldStartWith("Munţii Apuseni");
        path.ShouldEndWith("Bazinul Padiş");
        path.ShouldContain("Munţii Bihorului");
    }

    /// <summary>
    /// An identifier the table does not hold is answered as unknown rather than guessed at, so a
    /// cave keeps an empty basin field instead of a wrong one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(999999)]
    public void An_unknown_identifier_resolves_to_nothing(int? id)
    {
        SpeologieBasins.Find(id).ShouldBeNull();
        SpeologieBasins.PathOf(id).ShouldBeNull();
        SpeologieBasins.Ancestry(id).ShouldBeEmpty();
    }

    /// <summary>
    /// Choosing a level of the tree means everything under it. This is what makes the tree a
    /// filter rather than a list — picking a massif and being shown only the one basin named after
    /// it, with every valley inside it excluded, would look broken.
    /// </summary>
    [Fact]
    public void A_basin_is_within_every_level_above_it()
    {
        var ancestry = SpeologieBasins.Ancestry(605);
        ancestry.Count.ShouldBeGreaterThan(1);

        foreach (var level in ancestry)
        {
            SpeologieBasins.IsWithin(605, level.Id).ShouldBeTrue($"605 sits inside {level.Name}");
        }

        // And not inside something it is not.
        var elsewhere = SpeologieBasins.All.First(b => !ancestry.Any(a => a.Id == b.Id) && b.Depth == 1);
        SpeologieBasins.IsWithin(605, elsewhere.Id).ShouldBeFalse();
    }

    /// <summary>
    /// A name the catalogue did not write as "code - label" keeps its whole name, because a name
    /// shown in full is right and one split in the wrong place is not.
    /// </summary>
    [Fact]
    public void A_name_without_a_code_keeps_all_of_itself()
    {
        var uncoded = SpeologieBasins.All.Where(b => b.Code is null).ToArray();

        uncoded.ShouldAllBe(b => b.Label == b.Name);
        uncoded.ShouldAllBe(b => b.Label.Length > 0);
    }

    /// <summary>The picker's order: outermost level first, so the massifs are reachable before the valleys.</summary>
    [Fact]
    public void The_listing_puts_the_outermost_levels_first()
    {
        var depths = SpeologieBasins.All.Select(b => b.Depth).ToArray();
        depths.ShouldBe(depths.OrderBy(d => d).ToArray());
    }
}
