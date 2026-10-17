// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A saved view keeps no place in its filter: a typed point, a pasted outline or the place nearest
/// first is measured from, wherever in the document it sits — and keeps everything else it held.
/// </summary>
public class SavedViewRulesTests
{
    private static readonly PointValue Somewhere = new(23.5, 46.5);

    private static FilterDocument Over(FilterNode where) => new() { Scope = [new WorldScope("feature", where)] };

    private static ConditionNode Within(FilterValue anchor) =>
        new("position", FilterOp.Within, [new NumberValue(500), anchor]);

    private static readonly ConditionNode Deep =
        new("depth", FilterOp.GreaterOrEqual, [new NumberValue(100)]);

    private static JsonElement View(FilterDocument? document, string extra = "")
    {
        var filter = document is null ? "null" : FilterDocument.Serialize(document);
        return JsonDocument.Parse(
            $$"""{"configVersion":1,"center":[25,45],"zoom":9,"tagFilter":null{{extra}},"filterDocument":{{filter}}}""")
            .RootElement.Clone();
    }

    public static TheoryData<string, FilterDocument> Positions => new()
    {
        { "a typed point", Over(Within(Somewhere)) },
        { "a pasted outline", Over(new ConditionNode("position", FilterOp.Inside,
            [new PolygonValue([[[23, 46], [24, 46], [24, 47], [23, 47], [23, 46]]])])) },
        { "a point under a negation inside a group", Over(new AllOfNode([Deep, new NotNode(Within(Somewhere))])) },
        { "the place nearest first is measured from", Over(Deep) with
            { Sort = SortKey.Proximity, Anchor = new PointAnchor(23.5, 46.5) } },
    };

    [Theory]
    [MemberData(nameof(Positions))]
    public void A_filter_holding_a_place_of_its_own_is_one_a_view_may_not_store(string what, FilterDocument document) =>
        SavedViewRules.FilterHoldsPosition(View(document)).ShouldBeTrue(what);

    [Fact]
    public void A_filter_naming_an_object_an_area_or_nothing_spatial_may_be_stored()
    {
        SavedViewRules.FilterHoldsPosition(View(Over(Deep))).ShouldBeFalse();
        SavedViewRules.FilterHoldsPosition(View(Over(Within(new ObjectValue("feature", Guid.NewGuid())))))
            .ShouldBeFalse("an object is looked up afresh under each reader's access");
        SavedViewRules.FilterHoldsPosition(View(Over(Deep) with { Sort = SortKey.Proximity }))
            .ShouldBeFalse("nearest first with no place of its own is given one by whoever runs it");
    }

    [Fact]
    public void A_view_with_no_filter_document_or_one_naming_a_saved_filter_holds_no_place()
    {
        SavedViewRules.FilterHoldsPosition(View(null)).ShouldBeFalse();
        SavedViewRules.FilterHoldsPosition(View(null, $$""","savedFilterId":"{{Guid.NewGuid()}}" """)).ShouldBeFalse();
        SavedViewRules.FilterHoldsPosition(JsonDocument.Parse("""{"configVersion":1}""").RootElement).ShouldBeFalse();
        SavedViewRules.FilterHoldsPosition(JsonDocument.Parse("[]").RootElement).ShouldBeFalse();
    }

    [Fact]
    public void A_document_this_version_cannot_read_is_left_to_the_readers_that_open_it_unfiltered()
    {
        var unreadable = JsonDocument.Parse(
            """{"configVersion":1,"filterDocument":{"version":99,"scope":[],"sort":"updated","descending":true}}""").RootElement;
        SavedViewRules.FilterHoldsPosition(unreadable).ShouldBeFalse();
    }
}
