// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using SilexGis.Domain.Calendar;

namespace SilexGis.Domain.Tests;

public class CalendarFeedTokensTests
{
    [Fact]
    public void A_minted_token_is_32_random_bytes_in_base64url_and_its_stored_form_is_the_hash()
    {
        var (token, hash) = CalendarFeedTokens.Mint();

        // 32 bytes encode to 43 characters without padding, and the alphabet is URL-safe, so the
        // token can sit in a path with nothing escaped.
        token.Length.ShouldBe(43);
        token.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
        hash.ShouldBe(Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        hash.ShouldNotBe(token);
        CalendarFeedTokens.Hash(token).ShouldBe(hash);
    }

    [Fact]
    public void Two_mints_differ()
    {
        var first = CalendarFeedTokens.Mint();
        var second = CalendarFeedTokens.Mint();

        first.Token.ShouldNotBe(second.Token);
        first.Hash.ShouldNotBe(second.Hash);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("a", true)]
    public void Plausibility_refuses_nothing_at_all_and_accepts_anything_short(string? token, bool plausible) =>
        CalendarFeedTokens.IsPlausible(token).ShouldBe(plausible);

    [Fact]
    public void Plausibility_refuses_a_token_past_the_bound_and_accepts_one_at_it()
    {
        CalendarFeedTokens.IsPlausible(new string('x', CalendarFeedTokens.MaxTokenLength)).ShouldBeTrue();
        CalendarFeedTokens.IsPlausible(new string('x', CalendarFeedTokens.MaxTokenLength + 1)).ShouldBeFalse();
    }
}
