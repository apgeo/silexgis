// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Messaging;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading the language a browser asks for, out of the header it sends with every request.
/// </summary>
public class AcceptLanguageTests
{
    [Theory]
    [InlineData("ro", "ro")]
    [InlineData("ro-RO", "ro")]
    [InlineData("en-GB,en;q=0.9", "en")]
    // The weight decides, not the order it happens to be written in.
    [InlineData("en;q=0.4,ro;q=0.9", "ro")]
    // Equal weights keep the first, which is the order the browser itself considers preferred.
    [InlineData("ro;q=0.8,en;q=0.8", "ro")]
    // A language this installation has no wording in is passed over rather than answered.
    [InlineData("de-DE,de;q=0.9,ro;q=0.5", "ro")]
    public void The_best_language_this_installation_has_is_the_one_asked_for(string header, string expected)
    {
        AcceptLanguage.Preferred(header).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("de,fr;q=0.8")]
    // "anything" is not a request for a particular language.
    [InlineData("*")]
    // A weight of zero is the header's way of refusing one, not of preferring it faintly.
    [InlineData("ro;q=0")]
    public void Asking_for_nothing_this_installation_has_answers_nothing(string? header)
    {
        // Null rather than English, so the caller can fall back to the language the account
        // itself chose instead of overruling it with a default nobody asked for.
        AcceptLanguage.Preferred(header).ShouldBeNull();
    }

    [Fact]
    public void A_malformed_weight_is_read_as_no_weight_rather_than_as_a_refusal()
    {
        // Headers arrive from anywhere. Something unparseable must not silently mean "not this
        // one", which would leave a reader with the wrong language and no way to tell why.
        AcceptLanguage.Preferred("ro;q=banana").ShouldBe("ro");
    }
}
