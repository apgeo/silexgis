// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The exact bytes the browser sends, read back here.
/// </summary>
/// <remarks>
/// <para>
/// The generated client cannot check this. A polymorphic list of values comes out of the OpenAPI
/// tool as an unknown, so nothing in either build would notice if the two sides disagreed about how
/// a value says what type it is — and the disagreement does not fail loudly. A document whose
/// discriminator is spelled wrongly is simply refused as malformed, at run time, in somebody's face.
/// </para>
/// <para>
/// So the contract is pinned by a literal instead. The same string appears in the client's
/// <c>filters.test.ts</c>, where it is asserted to be what <c>JSON.stringify</c> produces. If either
/// side changes how it spells a document, exactly one of the two tests fails and names the problem.
/// </para>
/// </remarks>
public class FilterWireContractTests
{
    /// <summary>
    /// Written by the client, character for character. Keep the two copies identical.
    /// </summary>
    private const string AsTheBrowserSendsIt =
        """
        {"version":1,"scope":[{"world":"feature","where":{"node":"allOf","of":[{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":"urs"}]},{"node":"condition","field":"createdAt","op":"greaterThan","values":[{"type":"instant","value":"2026-01-01T00:00:00+00:00"}]}]}}],"sort":"title","descending":false}
        """;

    /// <summary>
    /// The two inclusive comparisons, as the client writes them. Pinned apart from the first
    /// literal because an operator's spelling is its own contract: a saved filter holds it, and a
    /// client that wrote "gte" would have its "at least 300 m" refused as malformed.
    /// </summary>
    private const string InclusiveComparisons =
        """
        {"version":1,"scope":[{"world":"feature","where":{"node":"allOf","of":[{"node":"condition","field":"surveyedLength","op":"greaterOrEqual","values":[{"type":"number","value":300}]},{"node":"condition","field":"updatedAt","op":"lessOrEqual","values":[{"type":"instant","value":"2026-01-01T00:00:00+00:00"}]}]}}],"sort":"updated","descending":true}
        """;

    /// <summary>
    /// Every spatial value and the nearest-first anchor, as the client writes them: a radius and a
    /// point, an object, another saved filter; a pasted outline and an area object; and the point
    /// nearest first is measured from. Pinned on its own because each value names its kind by a
    /// discriminator the generated contract cannot see.
    /// </summary>
    private const string SpatialValues =
        """
        {"version":1,"scope":[{"world":"feature","where":{"node":"anyOf","of":[{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":500},{"type":"point","longitude":23.5,"latitude":45.25}]},{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":200},{"type":"object","world":"feature","id":"0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10"}]},{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":1000},{"type":"savedFilter","id":"5c1e2d3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f"}]},{"node":"condition","field":"position","op":"inside","values":[{"type":"polygon","coordinates":[[[23,45],[23.01,45],[23.01,45.01],[23,45]]]}]},{"node":"condition","field":"position","op":"inside","values":[{"type":"object","world":"feature","id":"7d2c9e14-6b3a-4f58-a1c2-0e9d8b7a6f54"}]}]}}],"sort":"proximity","descending":false,"anchor":{"anchor":"point","longitude":23.5,"latitude":45.25}}
        """;

    [Fact]
    public void The_spatial_values_and_the_anchor_read_and_write_back_as_the_client_spells_them()
    {
        var document = FilterDocument.Deserialize(SpatialValues);

        document.ShouldNotBeNull("The client's spelling of a spatial condition is not one this can read.");
        document.Sort.ShouldBe(SortKey.Proximity);
        var anchor = document.Anchor.ShouldBeOfType<PointAnchor>();
        anchor.Longitude.ShouldBe(23.5);
        anchor.Latitude.ShouldBe(45.25);

        var rows = document.Scope.ShouldHaveSingleItem().Where.ShouldBeOfType<AnyOfNode>().Of
            .Select(n => n.ShouldBeOfType<ConditionNode>()).ToList();
        rows.ShouldAllBe(r => r.Field == FeatureFilterFields.Position);

        rows[0].Op.ShouldBe(FilterOp.Within);
        rows[0].Values[0].ShouldBeOfType<NumberValue>().Value.ShouldBe(500);
        rows[0].Values[1].ShouldBe(new PointValue(23.5, 45.25));
        rows[1].Values[1].ShouldBe(new ObjectValue("feature", Guid.Parse("0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10")));
        rows[2].Values[1].ShouldBe(new SavedFilterValue(Guid.Parse("5c1e2d3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f")));
        rows[3].Op.ShouldBe(FilterOp.Inside);
        var ring = rows[3].Values.ShouldHaveSingleItem().ShouldBeOfType<PolygonValue>().Coordinates.ShouldHaveSingleItem();
        ring.Length.ShouldBe(4);
        ring[1].ShouldBe([23.01, 45]);
        rows[4].Values.ShouldHaveSingleItem()
            .ShouldBe(new ObjectValue("feature", Guid.Parse("7d2c9e14-6b3a-4f58-a1c2-0e9d8b7a6f54")));

        FilterDocument.Serialize(document).ShouldBe(SpatialValues);
    }

    [Fact]
    public void The_inclusive_comparisons_read_and_write_back_as_the_client_spells_them()
    {
        var document = FilterDocument.Deserialize(InclusiveComparisons);

        document.ShouldNotBeNull("The client's spelling of 'at least' or 'at most' is not one this can read.");
        var group = document.Scope.ShouldHaveSingleItem().Where.ShouldBeOfType<AllOfNode>();

        var atLeast = group.Of[0].ShouldBeOfType<ConditionNode>();
        atLeast.Op.ShouldBe(FilterOp.GreaterOrEqual);
        atLeast.Values.ShouldHaveSingleItem().ShouldBeOfType<NumberValue>().Value.ShouldBe(300);

        var atMost = group.Of[1].ShouldBeOfType<ConditionNode>();
        atMost.Op.ShouldBe(FilterOp.LessOrEqual);
        atMost.Values.ShouldHaveSingleItem().ShouldBeOfType<InstantValue>()
            .Value.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        FilterDocument.Serialize(document).ShouldBe(InclusiveComparisons);
    }

    [Theory]
    [InlineData(FilterOp.GreaterOrEqual, 11)]
    [InlineData(FilterOp.LessOrEqual, 12)]
    public void The_inclusive_comparisons_keep_their_stored_numbers(FilterOp op, int stored)
    {
        // Appended, never renumbered: the numbers are what an enum column or a cached plan would
        // hold, and a member slipped in before them would quietly change every later one.
        ((int)op).ShouldBe(stored);
    }

    [Fact]
    public void The_document_the_client_writes_is_the_document_this_reads()
    {
        var document = FilterDocument.Deserialize(AsTheBrowserSendsIt);

        document.ShouldNotBeNull("The client's spelling of a filter is not one this can read.");
        document.Version.ShouldBe(FilterDocument.CurrentVersion);
        document.Sort.ShouldBe(SortKey.Title);
        document.Descending.ShouldBeFalse();

        var scope = document.Scope.ShouldHaveSingleItem();
        scope.World.ShouldBe("feature");

        var group = scope.Where.ShouldBeOfType<AllOfNode>();
        group.Of.Count.ShouldBe(2);

        var text = group.Of[0].ShouldBeOfType<ConditionNode>();
        text.Field.ShouldBe("name");
        text.Op.ShouldBe(FilterOp.Contains);
        text.Values.ShouldHaveSingleItem().ShouldBeOfType<TextValue>().Value.ShouldBe("urs");

        var instant = group.Of[1].ShouldBeOfType<ConditionNode>();
        instant.Op.ShouldBe(FilterOp.GreaterThan);
        instant.Values.ShouldHaveSingleItem().ShouldBeOfType<InstantValue>()
            .Value.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void And_writing_it_back_out_produces_the_same_bytes()
    {
        // Both directions, because a filter is saved as well as sent: a document this could read
        // but wrote differently would drift the moment somebody opened and re-saved it.
        var document = FilterDocument.Deserialize(AsTheBrowserSendsIt);

        FilterDocument.Serialize(document!).ShouldBe(AsTheBrowserSendsIt);
    }

    [Theory]
    [InlineData("kind", "The discriminator was renamed; the client would be refused.")]
    [InlineData("valueType", "A plausible alternative spelling, which is exactly the risk.")]
    public void A_value_that_names_its_type_differently_is_refused_rather_than_guessed(
        string discriminator, string why)
    {
        var wrong = AsTheBrowserSendsIt.Replace("\"type\":", $"\"{discriminator}\":");

        // Null rather than a value defaulted to text. Guessing would turn the number 4.5 into the
        // text "4.5" somewhere downstream, and that filter would find nothing while looking correct.
        FilterDocument.Deserialize(wrong).ShouldBeNull(why);
    }
}
