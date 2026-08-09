// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The gate that lets the compiler have no fallback.
///
/// <para>
/// Every condition that reaches compilation has already been proved to name a field its world
/// declares, to use an operator that field admits, and to carry the right number and kinds of
/// values. That is what makes "untranslatable means invalid" true rather than aspirational — and
/// it is why these cases are about refusal far more than about acceptance.
/// </para>
/// </summary>
public class FilterValidationTests
{
    private static readonly WorldVocabulary Features = new(
        "feature",
        "worlds.feature",
        [
            new FieldDescriptor("name", "fields.name", FieldKind.Text, Sortable: true),
            new FieldDescriptor("typeId", "fields.type", FieldKind.Id, Options: "featureTypes"),
            new FieldDescriptor("depth", "fields.depth", FieldKind.Number),
            new FieldDescriptor("updatedAt", "fields.updated", FieldKind.Instant, Sortable: true),
            new FieldDescriptor("protected", "fields.protected", FieldKind.Boolean),
            new FieldDescriptor("position", "fields.position", FieldKind.Spatial),
        ],
        [SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Proximity]);

    private static readonly WorldVocabulary Documents = new(
        "document",
        "worlds.document",
        [new FieldDescriptor("title", "fields.title", FieldKind.Text, Sortable: true)],
        [SortKey.Created, SortKey.Updated, SortKey.Title]);

    private static readonly Dictionary<string, WorldVocabulary> Vocabularies = new(StringComparer.Ordinal)
    {
        ["feature"] = Features,
        ["document"] = Documents,
    };

    private static IReadOnlyList<string> Validate(FilterDocument document) =>
        FilterValidation.Validate(document, Vocabularies);

    private static FilterDocument Over(string world, FilterNode? where, SortKey sort = SortKey.Updated) =>
        new() { Scope = [new WorldScope(world, where)], Sort = sort };

    private static ConditionNode Named(string value) =>
        new("name", FilterOp.Contains, [new TextValue(value)]);

    // ---------- what a good document looks like ----------

    [Fact]
    public void A_condition_naming_a_declared_field_with_an_allowed_operator_is_accepted()
    {
        Validate(Over("feature", Named("ursilor"))).ShouldBeEmpty();
    }

    [Fact]
    public void A_world_with_no_conditions_is_a_filter_meaning_everything_it_holds()
    {
        Validate(Over("feature", null)).ShouldBeEmpty();
    }

    [Fact]
    public void Groups_nest_and_negate()
    {
        var tree = new AllOfNode([
            Named("urs"),
            new AnyOfNode([
                new ConditionNode("depth", FilterOp.GreaterThan, [new NumberValue(100)]),
                new NotNode(new ConditionNode("protected", FilterOp.Equals, [new BooleanValue(true)])),
            ]),
        ]);

        Validate(Over("feature", tree)).ShouldBeEmpty();
    }

    // ---------- what it refuses, and why ----------

    [Fact]
    public void A_field_the_world_does_not_declare_is_refused()
    {
        var errors = Validate(Over("feature", new ConditionNode(
            "closestAddress", FilterOp.Contains, [new TextValue("Brasov")])));

        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_refusal_does_not_say_whether_the_field_exists_but_is_withheld()
    {
        // A field that exists and is not offerable — somebody's address, which cave a trip
        // visited — must read exactly like one that does not exist at all. Otherwise the refusal
        // itself answers the question the field was withheld to avoid answering.
        var withheld = Validate(Over("feature", new ConditionNode(
            "closestAddress", FilterOp.Contains, [new TextValue("x")])));
        var nonsense = Validate(Over("feature", new ConditionNode(
            "zzzNotAFieldAtAll", FilterOp.Contains, [new TextValue("x")])));

        withheld.Count.ShouldBe(1);
        nonsense.Count.ShouldBe(1);
        // Same sentence apart from the name the caller themselves supplied.
        withheld[0].Replace("closestAddress", "X").ShouldBe(nonsense[0].Replace("zzzNotAFieldAtAll", "X"));
    }

    [Fact]
    public void An_operator_the_field_does_not_admit_is_refused()
    {
        // A boolean has nothing to be "between".
        Validate(Over("feature", new ConditionNode(
            "protected", FilterOp.Between, [new BooleanValue(true), new BooleanValue(false)])))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused_rather_than_coerced()
    {
        // The reason this is strict: stored JSON tells 4.5 from "4.5", so a quoted number would
        // compile into a condition that matches nothing while looking perfectly correct.
        Validate(Over("feature", new ConditionNode(
            "depth", FilterOp.Equals, [new TextValue("100")]))).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(FilterOp.Between, 1)]
    [InlineData(FilterOp.Between, 3)]
    [InlineData(FilterOp.IsEmpty, 1)]
    public void An_operator_given_the_wrong_number_of_values_is_refused(FilterOp op, int count)
    {
        var values = Enumerable.Range(0, count).Select(i => (FilterValue)new NumberValue(i)).ToList();

        Validate(Over("feature", new ConditionNode("depth", op, values))).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_refused()
    {
        Validate(Over("feature", new ConditionNode(
            "depth", FilterOp.Between, [new NumberValue(200), new NumberValue(100)])))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void An_empty_group_is_refused_because_it_has_no_honest_meaning()
    {
        // Read as "everything" it silently widens a filter; read as "nothing" it silently empties
        // one. Neither reading is one somebody would expect from a group they left blank.
        Validate(Over("feature", new AllOfNode([]))).ShouldNotBeEmpty();
        Validate(Over("feature", new AnyOfNode([]))).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_world_the_caller_may_not_ask_about_is_refused_rather_than_dropped()
    {
        // Dropping it would answer a narrower question than the one asked while looking like it
        // had answered the whole of it.
        Validate(Over("tripLog", null)).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_same_world_named_twice_is_refused()
    {
        var document = new FilterDocument
        {
            Scope = [new WorldScope("feature", Named("a")), new WorldScope("feature", Named("b"))],
        };

        Validate(document).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_sort_no_world_in_the_scope_can_answer_is_refused()
    {
        // Silently falling back would give an arbitrary order, and an arbitrary order paged over
        // reads as rows appearing and vanishing between pages.
        Validate(Over("document", null, SortKey.Proximity)).ShouldNotBeEmpty();
        Validate(Over("feature", null, SortKey.Proximity)).ShouldBeEmpty();
    }

    [Fact]
    public void A_filter_with_no_scope_at_all_is_refused()
    {
        Validate(new FilterDocument()).ShouldNotBeEmpty();
    }

    // ---------- the guards ----------

    [Fact]
    public void A_document_nested_deeper_than_a_person_could_draw_is_refused()
    {
        FilterNode tree = Named("deep");
        for (var i = 0; i < FilterValidation.MaxDepth + 2; i++)
        {
            tree = new AllOfNode([tree]);
        }

        Validate(Over("feature", tree)).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_document_with_more_nodes_than_anybody_drew_is_refused()
    {
        var many = Enumerable.Range(0, FilterValidation.MaxNodes + 10)
            .Select(i => (FilterNode)Named($"n{i}"))
            .ToList();

        Validate(Over("feature", new AnyOfNode(many))).ShouldNotBeEmpty();
    }

    [Fact]
    public void One_condition_holds_a_bounded_number_of_values()
    {
        var values = Enumerable.Range(0, FilterValidation.MaxValuesPerCondition + 1)
            .Select(i => (FilterValue)new IdValue(i.ToString()))
            .ToList();

        Validate(Over("feature", new ConditionNode("typeId", FilterOp.In, values))).ShouldNotBeEmpty();
    }

    // ---------- the document as it travels ----------

    [Fact]
    public void A_document_survives_being_written_and_read_back()
    {
        var original = new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AllOfNode([
                    Named("urs"),
                    new NotNode(new ConditionNode("depth", FilterOp.LessThan, [new NumberValue(10)])),
                ])),
                new WorldScope("document", null),
            ],
            Sort = SortKey.Title,
            Descending = false,
        };

        var round = FilterDocument.Deserialize(FilterDocument.Serialize(original));

        round.ShouldNotBeNull();
        round.Sort.ShouldBe(SortKey.Title);
        round.Descending.ShouldBeFalse();
        round.Scope.Count.ShouldBe(2);
        // The node kinds survive, rather than collapsing into whichever shape deserialises first.
        round.Scope[0].Where.ShouldBeOfType<AllOfNode>()
            .Of[1].ShouldBeOfType<NotNode>();
        Validate(round).ShouldBeEmpty();
    }

    [Fact]
    public void A_document_written_by_a_newer_version_is_refused_rather_than_reinterpreted()
    {
        var future = FilterDocument.Serialize(new FilterDocument { Version = FilterDocument.CurrentVersion + 1 });

        FilterDocument.Deserialize(future).ShouldBeNull();
    }

    [Fact]
    public void A_stored_document_that_cannot_be_read_answers_nothing_rather_than_throwing()
    {
        // A saved filter that has become unreadable must not stop the page listing it from
        // rendering, so the failure is a null rather than an exception.
        FilterDocument.Deserialize("{ not json").ShouldBeNull();
        FilterDocument.Deserialize(null).ShouldBeNull();
        FilterDocument.Deserialize("").ShouldBeNull();
    }
}
