// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.Tests;

/// <summary>The examples of RFC 7396's appendix, which define what a merge patch means.</summary>
public class JsonMergePatchTests
{
    [Theory]
    [InlineData("""{"a":"b"}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"b":"c"}""", """{"a":"b","b":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"a":null}""", """{}""")]
    [InlineData("""{"a":"b","b":"c"}""", """{"a":null}""", """{"b":"c"}""")]
    [InlineData("""{"a":["b"]}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"c"}""", """{"a":["b"]}""", """{"a":["b"]}""")]
    [InlineData("""{"a":{"b":"c"}}""", """{"a":{"b":"d","c":null}}""", """{"a":{"b":"d"}}""")]
    [InlineData("""{"a":[{"b":"c"}]}""", """{"a":[1]}""", """{"a":[1]}""")]
    [InlineData("""["a","b"]""", """["c","d"]""", """["c","d"]""")]
    [InlineData("""{"a":"b"}""", """["c"]""", """["c"]""")]
    [InlineData("""{"a":"foo"}""", "null", "null")]
    [InlineData("""{"a":"foo"}""", "\"bar\"", "\"bar\"")]
    [InlineData("""{"e":null}""", """{"a":1}""", """{"e":null,"a":1}""")]
    [InlineData("""[1,2]""", """{"a":"b","c":null}""", """{"a":"b"}""")]
    [InlineData("""{}""", """{"a":{"bb":{"ccc":null}}}""", """{"a":{"bb":{}}}""")]
    public void A_patch_merges_as_the_standard_defines(string target, string patch, string expected)
    {
        var result = JsonMergePatch.Apply(JsonNode.Parse(target), JsonNode.Parse(patch));

        (result?.ToJsonString() ?? "null").ShouldBe(JsonNode.Parse(expected)?.ToJsonString() ?? "null");
    }

    [Fact]
    public void The_stored_document_and_the_patch_are_left_as_they_were()
    {
        var target = JsonNode.Parse("""{"appearance":{"theme":"dark"}}""");
        var patch = JsonNode.Parse("""{"appearance":{"density":"compact"}}""");

        _ = JsonMergePatch.Apply(target, patch);

        target!.ToJsonString().ShouldBe("""{"appearance":{"theme":"dark"}}""");
        patch!.ToJsonString().ShouldBe("""{"appearance":{"density":"compact"}}""");
    }
}
