// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a filter looks like once it is written down.
/// </summary>
/// <remarks>
/// A filter is not only a request body. It is saved in a row, put into a link somebody pastes into
/// a chat, and referenced by a saved view — so what it serialises to is a stored format, and the
/// cost of anything superfluous in it is paid on every save and every URL rather than once.
/// </remarks>
public class FilterDocumentSerialisationTests
{
    private static FilterDocument Nested()
    {
        FilterNode leaf(string name) =>
            new ConditionNode("name", FilterOp.Contains, [new TextValue(name)]);

        return new FilterDocument
        {
            Scope =
            [
                new WorldScope("feature", new AnyOfNode(
                [
                    new AllOfNode([leaf("a"), leaf("b")]),
                    new NotNode(new AllOfNode([leaf("c"), leaf("d")])),
                ])),
            ],
        };
    }

    [Fact]
    public void A_document_writes_each_node_once()
    {
        // The walk helper on the base is the same nodes under a second name. Written out, it would
        // duplicate every subtree, and duplicate it again inside each copy.
        var json = FilterDocument.Serialize(Nested());

        json.ShouldNotContain("children", Case.Insensitive);

        // Four conditions, each named once. A count above four means a subtree was written twice.
        json.Split("\"contains\"").Length.ShouldBe(5);
    }

    [Fact]
    public void A_document_survives_being_written_down_and_read_back()
    {
        var original = Nested();

        var restored = FilterDocument.Deserialize(FilterDocument.Serialize(original));

        restored.ShouldNotBeNull();
        restored.Scope.Count.ShouldBe(1);
        restored.Scope[0].Where.ShouldBeOfType<AnyOfNode>()
            .Of.Count.ShouldBe(2);
        // The walk still works after the round trip, which is what the ignored member is for.
        restored.Scope[0].Where!.Children.Count.ShouldBe(2);
    }

    [Fact]
    public void A_stored_filter_stays_small_enough_to_put_in_a_link()
    {
        // Not a micro-optimisation: a URL that a chat client truncates is a link that opens the
        // page unfiltered, and nobody can tell that from a filter with no matches.
        FilterDocument.Serialize(Nested()).Length.ShouldBeLessThan(600);
    }
}
