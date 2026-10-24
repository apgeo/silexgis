// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

public class MapLayerDocumentRulesTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void An_administrators_answer_stands_over_the_catalogues_and_no_answer_follows_it(
        bool catalogue, bool? administrator, bool expected)
    {
        MapLayerDocumentRules.Effective(catalogue, administrator, MapLayerKind.Xyz).ShouldBe(expected);
    }

    [Theory]
    [InlineData(MapLayerKind.Wms)]
    [InlineData(MapLayerKind.Wmts)]
    [InlineData(MapLayerKind.Cog)]
    [InlineData(MapLayerKind.Vector)]
    public void Nothing_but_plain_tiles_is_ever_copied_into_a_document_whatever_anybody_said(MapLayerKind kind)
    {
        MapLayerDocumentRules.Effective(true, null, kind).ShouldBeFalse();
        MapLayerDocumentRules.Effective(false, true, kind).ShouldBeFalse();
        MapLayerDocumentRules.ObstacleTo(isBase: true, "© Somebody", kind)
            .ShouldBe(MapLayerDocumentObstacle.NotPlainTiles);
    }

    [Fact]
    public void Following_the_catalogue_is_stored_as_nothing_so_it_stays_distinct_from_agreeing_with_it()
    {
        MapLayerDocumentRules.Stored(MapLayerDocumentChoice.Default).ShouldBeNull();
        MapLayerDocumentRules.Stored(MapLayerDocumentChoice.On).ShouldBe(true);
        MapLayerDocumentRules.Stored(MapLayerDocumentChoice.Off).ShouldBe(false);

        foreach (var choice in Enum.GetValues<MapLayerDocumentChoice>())
        {
            MapLayerDocumentRules.ChoiceOf(MapLayerDocumentRules.Stored(choice)).ShouldBe(choice);
        }
    }

    [Fact]
    public void An_overlay_and_an_uncredited_source_cannot_carry_the_mark_whoever_asks()
    {
        MapLayerDocumentRules.ObstacleTo(isBase: true, "© Somebody", MapLayerKind.Xyz).ShouldBeNull();
        MapLayerDocumentRules.ObstacleTo(isBase: false, "© Somebody", MapLayerKind.Xyz)
            .ShouldBe(MapLayerDocumentObstacle.NotABackground);
        MapLayerDocumentRules.ObstacleTo(isBase: true, null, MapLayerKind.Xyz).ShouldBe(MapLayerDocumentObstacle.NoAttribution);
        MapLayerDocumentRules.ObstacleTo(isBase: true, "   ", MapLayerKind.Xyz).ShouldBe(MapLayerDocumentObstacle.NoAttribution);
        // An overlay with no credit is first of all not a background.
        MapLayerDocumentRules.ObstacleTo(isBase: false, null, MapLayerKind.Xyz).ShouldBe(MapLayerDocumentObstacle.NotABackground);
    }
}
