// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a saved filter may store, and what it reads as to somebody other than its author.
/// </summary>
public class SavedFilterRulesTests
{
    private static readonly IReadOnlyDictionary<string, WorldVocabulary> Everything =
        new Dictionary<string, WorldVocabulary> { ["feature"] = FeatureFilterFields.Vocabulary };

    /// <summary>A reader whose feature vocabulary lacks the region — "not for you", as a future
    /// per-caller vocabulary could make it.</summary>
    private static readonly IReadOnlyDictionary<string, WorldVocabulary> WithoutRegion =
        new Dictionary<string, WorldVocabulary>
        {
            ["feature"] = FeatureFilterFields.Vocabulary with
            {
                Fields = [.. FeatureFilterFields.Vocabulary.Fields.Where(f => f.Key != FeatureFilterFields.Region)],
            },
        };

    private static FilterDocument About(FilterNode? where) =>
        new() { Scope = [new WorldScope("feature", where)] };

    private static ConditionNode Region(string text) =>
        new(FeatureFilterFields.Region, FilterOp.Contains, [new TextValue(text)]);

    [Fact]
    public void A_document_carrying_an_anchor_is_refused_on_save()
    {
        // An anchor is a position somebody authored; stored in a filter shared with a club it would
        // hand that position to every member who opened it.
        var document = About(Region("Bihor")) with { Anchor = new PointAnchor(22.5, 46.5) };

        SavedFilterRules.ProblemsOnSave(document, Everything).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_document_the_author_may_ask_is_accepted()
    {
        SavedFilterRules.ProblemsOnSave(About(Region("Bihor")), Everything).ShouldBeEmpty();
    }

    [Fact]
    public void A_document_naming_a_field_nobody_declares_is_refused_on_save()
    {
        var document = About(new ConditionNode("altitude", FilterOp.LessThan, [new NumberValue(800)]));

        SavedFilterRules.ProblemsOnSave(document, Everything).ShouldNotBeEmpty();
    }

    [Fact]
    public void Unreadable_reads_the_same_for_a_field_withheld_and_a_field_that_does_not_exist()
    {
        var withheld = About(Region("Bihor"));
        var invented = About(new ConditionNode("noSuchField", FilterOp.Contains, [new TextValue("x")]));

        // Both are simply "cannot be read by you" — the state carries nothing that would tell the
        // reader which, and that is the whole of the protection.
        SavedFilterRules.IsReadableBy(withheld, WithoutRegion).ShouldBeFalse();
        SavedFilterRules.IsReadableBy(invented, WithoutRegion).ShouldBeFalse();
        SavedFilterRules.IsReadableBy(withheld, Everything).ShouldBeTrue();
    }

    [Fact]
    public void A_document_that_could_not_be_parsed_is_unreadable_rather_than_everything()
    {
        SavedFilterRules.IsReadableBy(null, Everything).ShouldBeFalse();
        SavedFilterRules.IsReadableBy(FilterDocument.Deserialize("""{"version":1,"scope":null}"""), Everything)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_stored_anchor_makes_a_document_unreadable_rather_than_measured_from()
    {
        var document = About(Region("Bihor")) with { Anchor = new ObjectAnchor("feature", Guid.NewGuid()) };

        SavedFilterRules.IsReadableBy(document, Everything).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Visibility.Private, true)]
    [InlineData(Visibility.CavingGroup, true)]
    [InlineData(Visibility.Authenticated, false)]
    [InlineData(Visibility.Public, false)]
    public void A_saved_filter_is_private_or_shared_with_one_caving_group(Visibility visibility, bool allowed)
    {
        SavedFilterRules.IsAllowedVisibility(visibility).ShouldBe(allowed);
    }

    [Fact]
    public void The_identities_a_document_names_are_grouped_by_where_their_choices_come_from()
    {
        var document = About(new AllOfNode(
        [
            new ConditionNode(FeatureFilterFields.CaveTypeId, FilterOp.In, [new IdValue("3"), new IdValue("9")]),
            new AnyOfNode(
            [
                new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue("bats")]),
                new NotNode(new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue("show-cave")])),
            ]),
            Region("Bihor"),
        ]));

        var references = SavedFilterRules.References(document, Everything);

        references["caveTypes"].ShouldBe(["3", "9"], ignoreOrder: true);
        references["tags"].ShouldBe(["bats", "show-cave"], ignoreOrder: true);
        // Free text names no identity, so it is never checked against anything.
        references.Keys.ShouldNotContain("region");
    }

    [Fact]
    public void A_document_read_back_from_jsonb_in_the_databases_key_order_is_still_readable()
    {
        // PostgreSQL's jsonb keeps keys shortest first, so the discriminators come back after the
        // members beside them. Read strictly, every stored filter would turn unreadable on storage.
        const string asJsonbReturnsIt =
            """{"sort":"updated","scope":[{"where":{"of":[{"op":"contains","node":"condition","field":"region","values":[{"type":"text","value":"Bihor"}]}],"node":"allOf"},"world":"feature"}],"version":1,"descending":true}""";

        var document = FilterDocument.Deserialize(asJsonbReturnsIt);

        document.ShouldNotBeNull();
        SavedFilterRules.IsReadableBy(document, Everything).ShouldBeTrue();
        document.Scope[0].Where.ShouldBeOfType<AllOfNode>().Of[0].ShouldBeOfType<ConditionNode>()
            .Values[0].ShouldBeOfType<TextValue>().Value.ShouldBe("Bihor");
    }

    [Fact]
    public void A_saved_filter_is_governed_with_the_saved_map_views()
    {
        AccessDomains.Of(new SavedFilter { Name = "x" }).ShouldBe(AccessDomain.MapViews);
    }
}
