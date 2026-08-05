// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

public class DocumentLanguageTests
{
    [Theory]
    [InlineData("ro", "ro")]
    [InlineData("RO", "ro")]
    [InlineData("  en  ", "en")]
    [InlineData("ron", "ron")]
    public void Keeps_a_plain_code_lowercased(string input, string expected)
    {
        DocumentLanguage.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ro-RO")]
    [InlineData("ro-MD")]
    [InlineData("ro_RO")]
    [InlineData("ro-Latn-RO")]
    public void Drops_everything_after_the_primary_subtag(string tag)
    {
        // Every Romanian locale must land on the same stored code, because they all stem the
        // same way; keeping them apart would only be three ways to miss one lookup.
        DocumentLanguage.Normalize(tag).ShouldBe("ro");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("r")]
    [InlineData("toolongsubtag")]
    [InlineData("r0")]
    [InlineData("ro!")]
    [InlineData("-RO")]
    public void Answers_null_for_anything_that_is_not_a_language_subtag(string? tag)
    {
        DocumentLanguage.Normalize(tag).ShouldBeNull();
    }

    [Fact]
    public void Normalized_codes_fit_the_stored_column()
    {
        var longest = new string('a', DocumentLanguage.MaxLength);

        DocumentLanguage.Normalize(longest).ShouldBe(longest);
        DocumentLanguage.Normalize(longest + "a").ShouldBeNull();
    }
}
