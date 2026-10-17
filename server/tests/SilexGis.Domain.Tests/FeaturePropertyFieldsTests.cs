// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Typed properties as filter fields: which ones a schema offers, and the rule that a property is
/// asked only beside the type that declares it.
/// </summary>
public class FeaturePropertyFieldsTests
{
    private const string SinkholeSchema = """
        {"type":"object","properties":{
          "depth_m":{"type":"number","title":"Depth (m)"},
          "visits":{"type":"integer"},
          "note":{"type":"string","title":"Note","enum":["a","b"]},
          "wet":{"type":["boolean","null"],"title":"Wet"},
          "shape":{"type":"object","title":"Shape"},
          "either":{"type":["number","string"]},
          "untyped":{"title":"No type"}
        }}
        """;

    private static readonly FeatureTypeSchema Sinkhole = new(7, "sinkhole", SinkholeSchema);

    private static WorldVocabulary Folded(params FeatureTypeSchema[] types) =>
        FeaturePropertyFields.Fold(FeatureFilterFields.Vocabulary, types);

    private static IReadOnlyList<FilterProblem> Validate(WorldVocabulary vocabulary, FilterNode where) =>
        FilterValidation.Validate(
            new FilterDocument { Scope = [new WorldScope("feature", where)], Sort = SortKey.Updated },
            new Dictionary<string, WorldVocabulary>(StringComparer.Ordinal) { ["feature"] = vocabulary });

    private static ConditionNode TypeIs(string id) =>
        new(FeatureFilterFields.TypeId, FilterOp.Equals, [new IdValue(id)]);

    private static ConditionNode DepthUnder(double value) =>
        new("property:sinkhole:depth_m", FilterOp.LessThan, [new NumberValue(value)]);

    // ---------- what a schema offers ----------

    [Fact]
    public void A_schema_offers_each_comparable_property_with_its_kind_title_and_type()
    {
        var fields = FeaturePropertyFields.Read(Sinkhole);

        fields.Select(f => (f.Key, f.Kind)).ShouldBe(
        [
            ("property:sinkhole:depth_m", FieldKind.Number),
            ("property:sinkhole:visits", FieldKind.Number),
            ("property:sinkhole:note", FieldKind.Text),
            ("property:sinkhole:wet", FieldKind.Boolean),
        ]);

        var depth = fields[0];
        depth.Label.ShouldBe("Depth (m)");
        depth.LabelKey.ShouldBe(FeaturePropertyFields.LabelKey);
        depth.Requires.ShouldBe(new FieldRequirement(FeatureFilterFields.TypeId, "7"));
        // A property with no title is named by its key rather than shown blank.
        fields[1].Label.ShouldBe("visits");
    }

    [Fact]
    public void A_string_declared_as_a_date_or_date_time_is_an_instant_and_any_other_format_is_text()
    {
        var fields = FeaturePropertyFields.Read(new FeatureTypeSchema(3, "dated", """
            {"properties":{
              "surveyed_on":{"type":"string","format":"date"},
              "seen_at":{"type":["string","null"],"format":"date-time"},
              "mail":{"type":"string","format":"email"},
              "clock":{"type":"string","format":"time"}
            }}
            """));

        fields.Select(f => (f.Key, f.Kind)).ShouldBe(
        [
            ("property:dated:surveyed_on", FieldKind.Instant),
            ("property:dated:seen_at", FieldKind.Instant),
            ("property:dated:mail", FieldKind.Text),
            ("property:dated:clock", FieldKind.Text),
        ]);
    }

    [Fact]
    public void A_dated_property_takes_the_instant_comparisons_and_refuses_the_text_ones()
    {
        var vocabulary = FeaturePropertyFields.Fold(FeatureFilterFields.Vocabulary,
            [new FeatureTypeSchema(3, "dated", """{"properties":{"on":{"type":"string","format":"date"}}}""")]);
        var type = new ConditionNode(FeatureFilterFields.TypeId, FilterOp.Equals, [new IdValue("3")]);
        var day = new InstantValue(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Validate(vocabulary, new AllOfNode([type, new ConditionNode(
            "property:dated:on", FilterOp.GreaterOrEqual, [day])])).ShouldBeEmpty();
        Validate(vocabulary, new AllOfNode([type, new ConditionNode(
            "property:dated:on", FilterOp.Contains, [new TextValue("2025")])])).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"properties":[]}""")]
    public void A_schema_that_declares_nothing_readable_offers_nothing(string? schema)
    {
        FeaturePropertyFields.Read(new FeatureTypeSchema(1, "x", schema)).ShouldBeEmpty();
    }

    [Fact]
    public void A_type_code_that_could_not_be_split_back_out_offers_nothing()
    {
        // The field key is split at the first colon after the prefix, so a code holding one would
        // name a different type than the one that declared it.
        FeaturePropertyFields.Read(new FeatureTypeSchema(1, "a:b", SinkholeSchema)).ShouldBeEmpty();
    }

    [Fact]
    public void Folding_no_types_leaves_the_vocabulary_as_it_was()
    {
        Folded().ShouldBeSameAs(FeatureFilterFields.Vocabulary);
        Folded(Sinkhole).Fields.Count.ShouldBe(FeatureFilterFields.Vocabulary.Fields.Count + 4);
    }

    // ---------- asked only beside its type ----------

    [Fact]
    public void A_property_beside_its_type_is_accepted()
    {
        Validate(Folded(Sinkhole), new AllOfNode([TypeIs("7"), DepthUnder(30)])).ShouldBeEmpty();
    }

    [Fact]
    public void The_type_holds_for_everything_nested_under_its_group()
    {
        // Under an any-of, under a negation: every row these can match is still a row of type 7.
        var tree = new AllOfNode(
        [
            new ConditionNode(FeatureFilterFields.TypeId, FilterOp.In, [new IdValue("7")]),
            new AnyOfNode([DepthUnder(3), new NotNode(DepthUnder(30))]),
        ]);

        Validate(Folded(Sinkhole), tree).ShouldBeEmpty();
    }

    [Fact]
    public void A_property_without_its_type_is_refused_in_the_words_used_for_an_unknown_field()
    {
        var vocabulary = Folded(Sinkhole);

        var alone = Validate(vocabulary, DepthUnder(30));
        var unknown = Validate(vocabulary, new ConditionNode(
            "property:sinkhole:nothing_here", FilterOp.LessThan, [new NumberValue(30)]));

        alone.Count.ShouldBe(1);
        unknown.Count.ShouldBe(1);
        Masked(alone[0], "depth_m").ShouldBe(Masked(unknown[0], "nothing_here"));
    }

    private static FilterProblem Masked(FilterProblem problem, string sent) =>
        problem with { Field = problem.Field?.Replace(sent, "X"), Detail = problem.Detail.Replace(sent, "X") };

    [Fact]
    public void A_property_refusal_has_the_code_of_an_unknown_field()
    {
        Validate(Folded(Sinkhole), DepthUnder(30)).ShouldHaveSingleItem().Code
            .ShouldBe(FilterProblemCodes.UnknownField);
    }

    public static TheoryData<string, FilterNode> NotFixingTheType() => new()
    {
        { "another type", new AllOfNode([TypeIs("8"), DepthUnder(30)]) },
        { "either of two types", new AllOfNode([
            new ConditionNode(FeatureFilterFields.TypeId, FilterOp.In, [new IdValue("7"), new IdValue("8")]),
            DepthUnder(30)]) },
        { "the type as one branch of an any-of", new AnyOfNode([TypeIs("7"), DepthUnder(30)]) },
        { "the type negated", new AllOfNode([new NotNode(TypeIs("7")), DepthUnder(30)]) },
        { "the type in a sibling group", new AllOfNode([
            new AnyOfNode([TypeIs("7"), TypeIs("7")]), DepthUnder(30)]) },
        { "the type nested below the property's group", new AllOfNode([
            new AllOfNode([TypeIs("7"), new ConditionNode(FeatureFilterFields.Name, FilterOp.IsNotEmpty, [])]),
            DepthUnder(30)]) },
    };

    [Theory]
    [MemberData(nameof(NotFixingTheType))]
    public void A_property_whose_type_is_not_fixed_for_every_row_it_could_match_is_refused(
        string shape, FilterNode tree)
    {
        Validate(Folded(Sinkhole), tree).ShouldNotBeEmpty(shape);
    }

    [Fact]
    public void A_property_of_a_type_left_out_of_the_fold_reads_like_one_that_never_existed()
    {
        // The caller who may read no feature types gets the bare vocabulary; their refusal for a
        // real property and for an invented one must be the same sentence.
        var bare = FeatureFilterFields.Vocabulary;
        var real = Validate(bare, new AllOfNode([TypeIs("7"), DepthUnder(30)]));
        var invented = Validate(bare, new AllOfNode([TypeIs("7"), new ConditionNode(
            "property:sinkhole:invented", FilterOp.LessThan, [new NumberValue(30)])]));

        real.Count.ShouldBe(1);
        Masked(real[0], "depth_m").ShouldBe(Masked(invented.Single(), "invented"));

        // And the same question is accepted for a caller whose fold holds the type.
        Validate(Folded(Sinkhole), new AllOfNode([TypeIs("7"), DepthUnder(30)])).ShouldBeEmpty();
    }

    [Fact]
    public void A_property_takes_only_the_comparisons_and_values_of_its_kind()
    {
        var vocabulary = Folded(Sinkhole);

        Validate(vocabulary, new AllOfNode([TypeIs("7"), new ConditionNode(
            "property:sinkhole:depth_m", FilterOp.Contains, [new TextValue("3")])])).ShouldNotBeEmpty();
        Validate(vocabulary, new AllOfNode([TypeIs("7"), new ConditionNode(
            "property:sinkhole:depth_m", FilterOp.Equals, [new TextValue("3")])])).ShouldNotBeEmpty();
        Validate(vocabulary, new AllOfNode([TypeIs("7"), new ConditionNode(
            "property:sinkhole:depth_m", FilterOp.Between, [new NumberValue(1), new NumberValue(3)])]))
            .ShouldBeEmpty();
    }
}
