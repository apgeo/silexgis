// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a cave's declared depths mean, and where the declaration stops and measuring begins.
/// </summary>
public class DeclaredDepthPlacesTests
{
    private static readonly DeclaredDepthPlaces.Declared[] Declarations =
    [
        new(0m, "grind.intrare.G0", "Intrare"),
        new(138m, "grind.niagara.3A", "P. Niagara"),
        new(540m, "grind.bivuac.BV0", "Bivuac P.W.G."),
        new(775m, "grind.puturi.GP42", null),
    ];

    [Fact]
    public void A_declared_depth_answers_with_the_station_the_cave_named()
    {
        var found = DeclaredDepthPlaces.For(Declarations, 138m).ShouldNotBeNull();
        found.ViewerStationName.ShouldBe("grind.niagara.3A");
        found.PlaceLabel.ShouldBe("P. Niagara");
    }

    [Fact]
    public void A_depth_nobody_declared_falls_through_rather_than_snapping_to_the_nearest_one()
    {
        // <b>The load-bearing one.</b> Reading a declaration as "the nearest declared depth" would
        // re-introduce the guessing it was written to replace: a party at −402 would be filed at a
        // declared −400 with nobody saying so. Falling through leaves it to measurement, which is
        // honest about being approximate and says how far off it was.
        DeclaredDepthPlaces.For(Declarations, 139m).ShouldBeNull();
        DeclaredDepthPlaces.For(Declarations, 137.9m).ShouldBeNull();
        DeclaredDepthPlaces.For(Declarations, 400m).ShouldBeNull();

        // Asserted beside the exact hit, so this cannot pass by the lookup answering null always.
        DeclaredDepthPlaces.For(Declarations, 138m).ShouldNotBeNull();
    }

    [Fact]
    public void A_cave_that_declared_nothing_answers_nothing()
    {
        DeclaredDepthPlaces.For([], 138m).ShouldBeNull();
    }

    [Fact]
    public void Zero_is_a_declarable_depth_like_any_other()
    {
        // The entrance is a real place a party is reported from, which is why a standing is its own
        // column on an imported sheet rather than being read off a depth of zero.
        DeclaredDepthPlaces.For(Declarations, 0m).ShouldNotBeNull()
            .ViewerStationName.ShouldBe("grind.intrare.G0");
    }

    [Fact]
    public void A_place_can_be_named_instead_of_a_depth_or_a_station()
    {
        DeclaredDepthPlaces.ByLabel(Declarations, "P. Niagara").ShouldNotBeNull()
            .ViewerStationName.ShouldBe("grind.niagara.3A");
    }

    [Fact]
    public void A_place_name_is_matched_the_way_a_persons_name_is()
    {
        // One folder for the whole project: a label typed without diacritics, in another case, or
        // with a doubled space is the same place. This is the fault that makes a chooser look like
        // it works while matching nothing.
        DeclaredDepthPlaces.ByLabel(Declarations, "bivuac p.w.g.").ShouldNotBeNull()
            .ViewerStationName.ShouldBe("grind.bivuac.BV0");
        DeclaredDepthPlaces.ByLabel(Declarations, "  INTRARE  ").ShouldNotBeNull()
            .ViewerStationName.ShouldBe("grind.intrare.G0");
    }

    [Fact]
    public void A_name_two_declarations_answer_to_is_no_answer_rather_than_the_first()
    {
        // Two places a club calls one thing is a fault in the declarations. Picking one would file
        // a report at whichever happened to be written first — silently, and differently on another
        // installation whose rows went in in another order.
        DeclaredDepthPlaces.Declared[] twice =
        [
            new(100m, "cave.a.1", "Sala Mare"),
            new(200m, "cave.b.2", "Sala Mare"),
        ];
        DeclaredDepthPlaces.ByLabel(twice, "Sala Mare").ShouldBeNull();

        // And the unambiguous one beside it, so this is a rule about ambiguity rather than a
        // lookup that never answers.
        DeclaredDepthPlaces.ByLabel(twice, "sala mare").ShouldBeNull();
        DeclaredDepthPlaces.ByLabel(Declarations, "Intrare").ShouldNotBeNull();
    }

    [Fact]
    public void A_declaration_with_no_word_for_it_is_not_found_by_an_empty_name()
    {
        // The −775 row has no label. An empty ask must not match it, or a blank column in a sheet
        // would file every row at whichever place happens to be nameless.
        DeclaredDepthPlaces.ByLabel(Declarations, null).ShouldBeNull();
        DeclaredDepthPlaces.ByLabel(Declarations, "").ShouldBeNull();
        DeclaredDepthPlaces.ByLabel(Declarations, "   ").ShouldBeNull();
    }
}
