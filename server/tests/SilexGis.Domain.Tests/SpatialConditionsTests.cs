// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a spatial condition may hold, refused before anything touches the database, and what a
/// saved filter may keep of one.
/// </summary>
public class SpatialConditionsTests
{
    private static readonly WorldVocabulary Features = new(
        "feature",
        "worlds.feature",
        [
            new FieldDescriptor("name", "fields.name", FieldKind.Text),
            new FieldDescriptor("position", "fields.position", FieldKind.Spatial),
        ],
        [SortKey.Updated]);

    private static readonly Dictionary<string, WorldVocabulary> Vocabularies = new(StringComparer.Ordinal)
    {
        ["feature"] = Features,
    };

    private static readonly PointValue Somewhere = new(23.5, 46.5);

    private static FilterDocument Over(FilterNode where) => new() { Scope = [new WorldScope("feature", where)] };

    /// <summary>The codes of the problems a document over <paramref name="where"/> is refused with.</summary>
    private static IReadOnlyList<string> Validate(FilterNode where) =>
        [.. FilterValidation.Validate(Over(where), Vocabularies).Select(p => p.Code)];

    private static ConditionNode Within(params FilterValue[] values) => new("position", FilterOp.Within, values);

    private static ConditionNode Inside(params FilterValue[] values) => new("position", FilterOp.Inside, values);

    private static PolygonValue Square(double lon, double lat, double half) => new(
    [
        [[lon - half, lat - half], [lon + half, lat - half], [lon + half, lat + half], [lon - half, lat + half], [lon - half, lat - half]],
    ]);

    [Fact]
    public void Every_anchor_shape_is_accepted_in_its_place()
    {
        Validate(Within(new NumberValue(500), Somewhere)).ShouldBeEmpty();
        Validate(Within(new NumberValue(500), new ObjectValue("feature", Guid.NewGuid()))).ShouldBeEmpty();
        Validate(Within(new NumberValue(500), new SavedFilterValue(Guid.NewGuid()))).ShouldBeEmpty();
        Validate(Inside(Square(23.5, 46.5, 0.1))).ShouldBeEmpty();
        Validate(Inside(new ObjectValue("feature", Guid.NewGuid()))).ShouldBeEmpty();
        Validate(new NotNode(Inside(Square(23.5, 46.5, 0.1)))).ShouldBeEmpty();
    }

    [Fact]
    public void A_radius_is_a_distance_within_bounds_and_comes_first()
    {
        Validate(Within(new NumberValue(0), Somewhere)).ShouldNotBeEmpty();
        Validate(Within(new NumberValue(-5), Somewhere)).ShouldNotBeEmpty();
        Validate(Within(new NumberValue(SpatialConditions.MaxRadiusMeters + 1), Somewhere)).ShouldNotBeEmpty();
        Validate(Within(new NumberValue(SpatialConditions.MaxRadiusMeters), Somewhere)).ShouldBeEmpty();
        // The anchor first and the radius second is a different question, and is refused.
        Validate(Within(Somewhere, new NumberValue(500))).ShouldNotBeEmpty();
        // A radius alone is a distance from nowhere.
        Validate(Within(new NumberValue(500))).ShouldNotBeEmpty();
    }

    [Fact]
    public void An_anchor_of_the_wrong_kind_or_in_the_wrong_place_is_refused()
    {
        Validate(Within(new NumberValue(500), new IdValue("x"))).ShouldNotBeEmpty();
        Validate(Within(new NumberValue(500), Square(23.5, 46.5, 0.1))).ShouldNotBeEmpty();
        Validate(Inside(Somewhere)).ShouldNotBeEmpty();
        Validate(Inside(new SavedFilterValue(Guid.NewGuid()))).ShouldNotBeEmpty();
        // An object of another world: no other world's rows carry a position a filter may measure.
        Validate(Within(new NumberValue(500), new ObjectValue("tripLog", Guid.NewGuid()))).ShouldNotBeEmpty();
        // A spatial value on a field that is not spatial.
        FilterValidation.Validate(Over(new ConditionNode("name", FilterOp.Equals, [Somewhere])), Vocabularies)
            .ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(181, 46)]
    [InlineData(23, 91)]
    [InlineData(double.NaN, 46)]
    public void A_point_off_the_globe_is_refused(double lon, double lat)
    {
        Validate(Within(new NumberValue(500), new PointValue(lon, lat))).ShouldNotBeEmpty();
    }

    [Fact]
    public void An_outline_that_is_not_a_simple_closed_polygon_is_refused()
    {
        // Open ring.
        Validate(Inside(new PolygonValue([[[0, 0], [1, 0], [1, 1], [0, 1]]]))).ShouldNotBeEmpty();
        // Bow tie: edges cross.
        Validate(Inside(new PolygonValue([[[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]]]))).ShouldNotBeEmpty();
        // A position that is not a pair.
        Validate(Inside(new PolygonValue([[[0, 0, 5], [1, 0], [1, 1], [0, 0]]]))).ShouldNotBeEmpty();
        // Too many positions to be something somebody drew.
        var many = Enumerable.Range(0, SpatialConditions.MaxPolygonPositions + 1)
            .Select(i => Math.PI * 2 * i / (SpatialConditions.MaxPolygonPositions + 1))
            .Select(a => new[] { 23 + (0.1 * Math.Cos(a)), 46 + (0.1 * Math.Sin(a)) })
            .ToList();
        many.Add(many[0]);
        Validate(Inside(new PolygonValue([[.. many]]))).ShouldNotBeEmpty();
    }

    [Fact]
    public void Each_spatial_problem_is_named_by_a_code_on_the_condition_it_is_about()
    {
        var radius = FilterValidation.Validate(
            Over(Within(new NumberValue(SpatialConditions.MaxRadiusMeters + 1), Somewhere)), Vocabularies).ShouldHaveSingleItem();
        radius.Code.ShouldBe(FilterProblemCodes.RadiusOutOfRange);
        radius.Path.ShouldBe("/scope/0/where");
        radius.Field.ShouldBe("position");
        radius.Limit.ShouldBe((int)SpatialConditions.MaxRadiusMeters);

        Validate(Within(new NumberValue(500), new PointValue(181, 46))).ShouldBe([FilterProblemCodes.PointOffGlobe]);
        Validate(Inside(new PolygonValue([[[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]]])))
            .ShouldBe([FilterProblemCodes.OutlineInvalid]);
        Validate(Within(new NumberValue(500), new ObjectValue("tripLog", Guid.NewGuid())))
            .ShouldBe([FilterProblemCodes.AnchorKind]);
        Validate(Inside(Somewhere)).ShouldBe([FilterProblemCodes.ValueKind]);
    }

    [Fact]
    public void A_proximity_problem_is_placed_at_the_document_anchor()
    {
        var ranking = new Dictionary<string, WorldVocabulary>(StringComparer.Ordinal)
        {
            ["feature"] = Features with { Sorts = [SortKey.Updated, SortKey.Proximity] },
        };
        var nearest = Over(Within(new NumberValue(500), Somewhere)) with { Sort = SortKey.Proximity };

        var missing = SpatialConditions.ProximityProblems(nearest, ranking).ShouldHaveSingleItem();
        missing.Code.ShouldBe(FilterProblemCodes.AnchorMissing);
        missing.Path.ShouldBe("/anchor");
        SpatialConditions.ProximityProblems(nearest with { Anchor = new PointAnchor(181, 0) }, ranking)
            .ShouldHaveSingleItem().Code.ShouldBe(FilterProblemCodes.PointOffGlobe);
        SpatialConditions.ProximityProblems(nearest with { Anchor = new ObjectAnchor("tripLog", Guid.NewGuid()) }, ranking)
            .ShouldHaveSingleItem().Code.ShouldBe(FilterProblemCodes.AnchorKind);
    }

    [Fact]
    public void A_hole_in_a_value_reads_as_malformed_rather_than_faulting()
    {
        FilterValidation.IsWellFormed(Over(Inside(new PolygonValue(null!)))).ShouldBeFalse();
        FilterValidation.IsWellFormed(Over(Inside(new ObjectValue(null!, Guid.NewGuid())))).ShouldBeFalse();
        FilterValidation.IsWellFormed(Over(Within(new NumberValue(5), Somewhere))).ShouldBeTrue();
    }

    [Fact]
    public void A_saved_filter_never_keeps_a_point_or_an_outline_but_keeps_what_it_looks_up()
    {
        var point = Over(new AllOfNode([new NotNode(Within(new NumberValue(500), Somewhere))]));
        SavedFilterRules.ProblemsOnSave(point, Vocabularies).ShouldNotBeEmpty();
        SavedFilterRules.IsReadableBy(point, Vocabularies).ShouldBeFalse();

        var outline = Over(Inside(Square(23.5, 46.5, 0.1)));
        SavedFilterRules.ProblemsOnSave(outline, Vocabularies).ShouldNotBeEmpty();

        var looked = Over(new AnyOfNode(
        [
            Within(new NumberValue(500), new ObjectValue("feature", Guid.NewGuid())),
            Within(new NumberValue(500), new SavedFilterValue(Guid.NewGuid())),
            Inside(new ObjectValue("feature", Guid.NewGuid())),
        ]));
        SavedFilterRules.ProblemsOnSave(looked, Vocabularies).ShouldBeEmpty();
        SavedFilterRules.IsReadableBy(looked, Vocabularies).ShouldBeTrue();
    }

    [Fact]
    public void Measuring_from_a_saved_filter_is_found_wherever_it_sits_in_the_tree()
    {
        SpatialConditions.MeasuresFromSavedFilter(
            new AllOfNode([new NotNode(Within(new NumberValue(5), new SavedFilterValue(Guid.NewGuid())))]))
            .ShouldBeTrue();
        SpatialConditions.MeasuresFromSavedFilter(Within(new NumberValue(5), Somewhere)).ShouldBeFalse();
        SpatialConditions.MeasuresFromSavedFilter(null).ShouldBeFalse();
    }

    [Fact]
    public void The_new_values_travel_with_a_stable_spelling()
    {
        var document = Over(new AllOfNode(
        [
            Within(new NumberValue(500), new PointValue(23.5, 46.5)),
            Within(new NumberValue(500), new ObjectValue("feature", Guid.Empty)),
            Within(new NumberValue(500), new SavedFilterValue(Guid.Empty)),
            Inside(new PolygonValue([[[0, 0], [1, 0], [1, 1], [0, 0]]])),
        ]));
        var json = FilterDocument.Serialize(document);
        json.ShouldContain("\"type\":\"point\",\"longitude\":23.5,\"latitude\":46.5");
        json.ShouldContain("\"type\":\"object\",\"world\":\"feature\",\"id\":\"00000000-0000-0000-0000-000000000000\"");
        json.ShouldContain("\"type\":\"savedFilter\",\"id\":\"00000000-0000-0000-0000-000000000000\"");
        json.ShouldContain("\"type\":\"polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]");
        FilterDocument.Serialize(FilterDocument.Deserialize(json)!).ShouldBe(json);
    }

    [Fact]
    public void Nearest_first_needs_a_place_it_can_rank_from_and_nothing_else_reads_one()
    {
        var ranking = new Dictionary<string, WorldVocabulary>(StringComparer.Ordinal)
        {
            ["feature"] = Features with { Sorts = [SortKey.Updated, SortKey.Proximity] },
            ["tripLog"] = new("tripLog", "worlds.tripLog", [], [SortKey.Updated]),
        };
        var nearest = Over(Within(new NumberValue(500), Somewhere)) with { Sort = SortKey.Proximity };

        SpatialConditions.ProximityProblems(nearest, ranking).ShouldNotBeEmpty("nothing to measure from");
        SpatialConditions.ProximityProblems(nearest with { Anchor = new PointAnchor(23.5, 46.5) }, ranking).ShouldBeEmpty();
        SpatialConditions.ProximityProblems(nearest with { Anchor = new ObjectAnchor("feature", Guid.NewGuid()) }, ranking)
            .ShouldBeEmpty();

        // Off the globe, an object of a world that does not rank, of a world not asked about, or
        // with no identity.
        SpatialConditions.ProximityProblems(nearest with { Anchor = new PointAnchor(181, 0) }, ranking).ShouldNotBeEmpty();
        SpatialConditions.ProximityProblems(nearest with { Anchor = new PointAnchor(0, double.NaN) }, ranking).ShouldNotBeEmpty();
        SpatialConditions.ProximityProblems(nearest with { Anchor = new ObjectAnchor("tripLog", Guid.NewGuid()) }, ranking)
            .ShouldNotBeEmpty();
        SpatialConditions.ProximityProblems(nearest with { Anchor = new ObjectAnchor("feature", Guid.Empty) }, ranking)
            .ShouldNotBeEmpty();
        var tripsOnly = new FilterDocument { Scope = [new WorldScope("tripLog", null)], Sort = SortKey.Proximity };
        SpatialConditions.ProximityProblems(tripsOnly with { Anchor = new ObjectAnchor("feature", Guid.NewGuid()) }, ranking)
            .ShouldNotBeEmpty();

        // Beside any other sort an anchor is not read, and its absence is no problem.
        SpatialConditions.ProximityProblems(Over(Within(new NumberValue(500), Somewhere)), ranking).ShouldBeEmpty();
        SpatialConditions.ProximityProblems(
            Over(Within(new NumberValue(500), Somewhere)) with { Anchor = new PointAnchor(999, 999) }, ranking).ShouldBeEmpty();
    }
}
