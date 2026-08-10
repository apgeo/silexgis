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
