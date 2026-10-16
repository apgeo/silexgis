// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The token a link, a list request and an export all carry, read the way the browser writes it.
/// </summary>
/// <remarks>
/// The generated contract sees a string and can check nothing about it, so the agreement is pinned
/// by literals: the same tokens appear in the client's <c>url.test.ts</c>, where they are asserted to
/// be what its encoder produces for the same documents. Change either side's spelling and exactly
/// one of the two tests fails.
/// </remarks>
public class FilterTokenTests
{
    /// <summary>
    /// The browser's encoding of the document pinned in <c>FilterWireContractTests</c>. Keep identical
    /// to the client's copy.
    /// </summary>
    private const string AsTheBrowserWritesIt =
        "eyJ2ZXJzaW9uIjoxLCJzY29wZSI6W3sid29ybGQiOiJmZWF0dXJlIiwid2hlcmUiOnsibm9kZSI6ImFsbE9mIiwib2YiOlt7Im5vZGUiOiJjb25kaXRpb24iLCJmaWVsZCI6Im5hbWUiLCJvcCI6ImNvbnRhaW5zIiwidmFsdWVzIjpbeyJ0eXBlIjoidGV4dCIsInZhbHVlIjoidXJzIn1dfSx7Im5vZGUiOiJjb25kaXRpb24iLCJmaWVsZCI6ImNyZWF0ZWRBdCIsIm9wIjoiZ3JlYXRlclRoYW4iLCJ2YWx1ZXMiOlt7InR5cGUiOiJpbnN0YW50IiwidmFsdWUiOiIyMDI2LTAxLTAxVDAwOjAwOjAwKzAwOjAwIn1dfV19fV0sInNvcnQiOiJ0aXRsZSIsImRlc2NlbmRpbmciOmZhbHNlfQ";

    /// <summary>
    /// A region with a Romanian letter in it — the case a byte-level misreading would get wrong, since
    /// the browser encodes UTF-8 bytes and a decoder reading them as Latin-1 would find a different
    /// region. Keep identical to the client's copy.
    /// </summary>
    private const string WithARomanianLetter =
        "eyJ2ZXJzaW9uIjoxLCJzY29wZSI6W3sid29ybGQiOiJmZWF0dXJlIiwid2hlcmUiOnsibm9kZSI6ImNvbmRpdGlvbiIsImZpZWxkIjoicmVnaW9uIiwib3AiOiJjb250YWlucyIsInZhbHVlcyI6W3sidHlwZSI6InRleHQiLCJ2YWx1ZSI6IkJpaG9yIMiZIn1dfX1dLCJzb3J0IjoidXBkYXRlZCIsImRlc2NlbmRpbmciOnRydWV9";

    [Fact]
    public void The_token_the_browser_writes_is_the_document_this_reads()
    {
        var document = FilterToken.Decode(AsTheBrowserWritesIt);

        document.ShouldNotBeNull();
        document.Sort.ShouldBe(SortKey.Title);
        var group = document.Scope.ShouldHaveSingleItem().Where.ShouldBeOfType<AllOfNode>();
        group.Of[0].ShouldBeOfType<ConditionNode>().Values.ShouldHaveSingleItem()
            .ShouldBeOfType<TextValue>().Value.ShouldBe("urs");
    }

    [Fact]
    public void Text_outside_ascii_survives_the_trip()
    {
        var document = FilterToken.Decode(WithARomanianLetter);

        var condition = document!.Scope.ShouldHaveSingleItem().Where.ShouldBeOfType<ConditionNode>();
        condition.Values.ShouldHaveSingleItem().ShouldBeOfType<TextValue>().Value.ShouldBe("Bihor ș");
    }

    [Fact]
    public void What_this_writes_it_reads_back()
    {
        var document = FilterToken.Decode(WithARomanianLetter)!;

        FilterToken.Decode(FilterToken.Encode(document)).ShouldBe(document, new DocumentComparer());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("eyJ2ZXJzaW9uIjox")] // a token cut short by a chat client
    [InlineData("eyJ2ZXJzaW9uIjo5OSwic2NvcGUiOltdLCJzb3J0IjoidXBkYXRlZCIsImRlc2NlbmRpbmciOnRydWV9")] // version 99
    [InlineData("__8")] // bytes that are not UTF-8
    public void A_token_this_cannot_read_reads_as_nothing_rather_than_as_everything(string token)
    {
        FilterToken.Decode(token).ShouldBeNull();
    }

    /// <summary>
    /// Documents that parse but have a member left out or spelled as null. The deserialiser fills
    /// such a hole with null rather than refusing it, so each of these used to come out of the
    /// decoder as a document and reach a validator that dereferenced the hole — a server error for
    /// what is only a bad link.
    /// </summary>
    [Theory]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"allOf"}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"anyOf","of":null}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"not"}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name","op":"contains"}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","op":"contains","values":[{"type":"text","value":"x"}]}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":null}]}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"tag","op":"in","values":[{"type":"id"}]}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"name","op":"contains","values":[null]}}]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"allOf","of":[{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":"x"}]},{"node":"anyOf","of":[{"node":"not"}]}]}}]}""")]
    [InlineData("""{"version":1,"scope":null}""")]
    [InlineData("""{"version":1,"scope":[null]}""")]
    [InlineData("""{"version":1,"scope":[{"where":null}]}""")]
    public void A_token_with_a_member_missing_reads_as_nothing_rather_than_as_a_document(string json)
    {
        FilterToken.Decode(Tokenise(json)).ShouldBeNull();
        FilterDocument.Deserialize(json).ShouldBeNull();
    }

    [Fact]
    public void A_token_past_the_ceiling_is_not_decoded_at_all()
    {
        FilterToken.Decode(new string('A', FilterToken.MaxLength + 1)).ShouldBeNull();
    }

    /// <summary>A document's text as the browser would carry it: base64url over its UTF-8, unpadded.</summary>
    private static string Tokenise(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Documents compare by their serialised form, which is what a link and a saved row hold.</summary>
    private sealed class DocumentComparer : IEqualityComparer<FilterDocument?>
    {
        public bool Equals(FilterDocument? x, FilterDocument? y) =>
            x is not null && y is not null && FilterDocument.Serialize(x) == FilterDocument.Serialize(y);

        public int GetHashCode(FilterDocument? obj) => 0;
    }
}
