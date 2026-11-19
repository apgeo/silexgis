// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation-side vocabularies — cave, entrance and rock types, link kinds and feature
/// kinds — keep the codes they have shipped, in the order they shipped them.
/// </summary>
/// <remarks>
/// A shipped row's display order is its position in its seed list, and the seeder never re-sorts a
/// row an installation already has. A code inserted in the middle renumbers everything after it on
/// a fresh installation only, and nothing on screen says why two installations disagree. These
/// lists live beside the seeder rather than in the domain, which is why they are pinned here and
/// not with the domain vocabularies. A new code goes at the end of its seed list, then at the end
/// of its line here.
/// </remarks>
public class ShippedTaxonomyOrderTests
{
    public static TheoryData<string, string[], string[]> Vocabularies() => new()
    {
        {
            "cave types",
            ["cave", "pit", "spring_cave", "mine", "rock_shelter"],
            [.. TaxonomySeeder.CaveTypes.Select(t => t.Code)]
        },
        {
            "entrance types",
            ["natural", "excavated", "artificial", "collapsed"],
            [.. TaxonomySeeder.EntranceTypes.Select(t => t.Code)]
        },
        {
            "rock types",
            ["limestone", "dolomite", "marble", "gypsum", "salt", "conglomerate", "sandstone", "volcanic", "other"],
            [.. TaxonomySeeder.RockTypes.Select(t => t.Code)]
        },
        {
            "link kinds",
            ["associated_cave", "hydro_connection", "same_system", "related"],
            [.. TaxonomySeeder.LinkKinds.Select(t => t.Code)]
        },
        {
            "feature kinds",
            ["sinkhole", "pit", "pitch", "chimney", "tunnel", "lake", "water_flow", "fracture_line", "peak", "wall",
                "bivouac", "exploration_point", "desobstruction", "continuation", "calm", "detritus", "driller", "flag",
                "generic", "arrow", "stalactite", "calcite_dome", "cave_sector", "karst_area", "massif", "cave_system",
                "building", "work_area", "cave_area", "cave_place", "surface_area", "cave", "hill"],
            [.. TaxonomySeeder.FeatureTypeSeedOrder]
        },
    };

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void A_shipped_list_only_ever_grows_at_its_end(string vocabulary, string[] shipped, string[] current)
    {
        current.Length.ShouldBeGreaterThanOrEqualTo(shipped.Length,
            $"a shipped code was removed from the {vocabulary}");
        current.Take(shipped.Length).ShouldBe(shipped,
            $"the {vocabulary} changed inside what has shipped; new codes go at the end, " +
            "because the seeder orders rows by position and never re-sorts an existing one");
    }
}
