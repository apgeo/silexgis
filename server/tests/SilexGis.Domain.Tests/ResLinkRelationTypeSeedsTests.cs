// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Domain.Tests;

public class ResLinkRelationTypeSeedsTests
{
    [Fact]
    public void The_map_vocabulary_is_seeded_directed_and_appended_at_the_end()
    {
        // Appended, never inserted: a row's sort order is its position in the list, so these
        // four must be the last four — a middle insert would renumber everything after it on a
        // fresh install only, and nothing on screen would say why two installs disagree.
        string[] expectedTail = ["map-plan-of", "map-profile-of", "map-other-of", "map-station-point"];
        var tail = ResLinkRelationTypeSeeds.All.Skip(ResLinkRelationTypeSeeds.All.Count - 4).ToList();
        tail.Select(s => s.Code).ShouldBe(expectedTail);

        foreach (var seed in tail)
        {
            // Directed with the document as the main member is what makes edit rights follow
            // document write access; an undirected code could never be corrected to that later,
            // because directedness is immutable once any installation has linked with it.
            seed.Directed.ShouldBeTrue(seed.Code);
            seed.InverseName.ShouldNotBeNull(seed.Code);
            ResLinkRelationTypeSeeds.IsSeeded(seed.Code).ShouldBeTrue(seed.Code);
        }
    }

    [Fact]
    public void The_map_view_codes_are_the_three_map_of_codes_and_nothing_else()
    {
        // The explicit list, pinned: the map tab surfaces are built from exactly these.
        ResLinkRelationTypeSeeds.MapViewCodes
            .ShouldBe(["map-plan-of", "map-profile-of", "map-other-of"]);

        // Every view code is seeded — the designed surface never keys off an unseeded code.
        foreach (var code in ResLinkRelationTypeSeeds.MapViewCodes)
        {
            ResLinkRelationTypeSeeds.IsSeeded(code).ShouldBeTrue(code);
        }
    }

    [Fact]
    public void The_pin_code_shares_the_prefix_but_never_joins_the_view_codes()
    {
        // The absence that matters: a "map-" prefix derivation would swallow the pin code and
        // turn every pin link into a phantom map tab. Its positive twin — the code exists, is
        // the constant, and is seeded — is asserted alongside so this test cannot pass by the
        // code simply not existing.
        ResLinkRelationTypeSeeds.MapStationPointCode.ShouldBe("map-station-point");
        ResLinkRelationTypeSeeds.MapStationPointCode.ShouldStartWith("map-");
        ResLinkRelationTypeSeeds.IsSeeded(ResLinkRelationTypeSeeds.MapStationPointCode).ShouldBeTrue();
        ResLinkRelationTypeSeeds.MapViewCodes
            .ShouldNotContain(ResLinkRelationTypeSeeds.MapStationPointCode);
    }
}
