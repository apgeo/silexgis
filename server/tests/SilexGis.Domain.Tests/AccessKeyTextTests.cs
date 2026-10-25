// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

public class AccessKeyTextTests
{
    [Fact]
    public void A_minted_key_is_the_marker_and_32_random_bytes_and_only_its_hash_and_prefix_are_kept()
    {
        var (key, hash, prefix) = AccessKeyText.Mint();

        key.ShouldStartWith("sgx_");
        key.Length.ShouldBe(AccessKeyText.Length);
        key[4..].ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
        hash.ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        AccessKeyText.Hash(key).ShouldBe(hash);
        prefix.ShouldBe(key[..12]);
    }

    [Fact]
    public void Two_mints_differ()
    {
        var first = AccessKeyText.Mint();
        var second = AccessKeyText.Mint();

        first.Key.ShouldNotBe(second.Key);
        first.Hash.ShouldNotBe(second.Hash);
    }

    [Theory]
    [InlineData("sgx_abc", true)]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.e30.sig", false)]
    [InlineData("SGX_abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_marker_says_a_bearer_value_is_a_key(string? value, bool isKey) =>
        AccessKeyText.IsKey(value).ShouldBe(isKey);

    [Fact]
    public void An_oversized_value_is_not_worth_hashing()
    {
        AccessKeyText.IsPlausible(AccessKeyText.Mint().Key).ShouldBeTrue();
        AccessKeyText.IsPlausible("sgx_" + new string('a', 200)).ShouldBeFalse();
        AccessKeyText.IsPlausible("not-a-key").ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, 365, 365)]
    [InlineData(null, 90, 90)]
    [InlineData(30, 365, 30)]
    [InlineData(365, 365, 365)]
    [InlineData(366, 365, null)]
    [InlineData(0, 365, null)]
    [InlineData(-5, 365, null)]
    [InlineData(3650, 99999, 3650)]
    [InlineData(3651, 99999, null)]
    public void A_lifetime_is_the_one_asked_for_inside_the_ceiling_or_the_default_under_it(
        int? requested, int configuredMax, int? expected) =>
        AccessKeyLifetime.Resolve(requested, configuredMax).ShouldBe(expected);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(500, 500)]
    [InlineData(100000, 3650)]
    public void The_configured_ceiling_is_kept_between_a_day_and_ten_years(int configured, int ceiling) =>
        AccessKeyLifetime.Ceiling(configured).ShouldBe(ceiling);

    [Theory]
    [InlineData(365, new[] { 30, 90, 365 })]
    [InlineData(100, new[] { 30, 90 })]
    [InlineData(10, new[] { 10 })]
    public void The_form_offers_the_usual_lifetimes_that_fit_or_the_ceiling(int configured, int[] offered) =>
        AccessKeyLifetime.Offered(configured).ShouldBe(offered);

    [Fact]
    public void A_key_reads_revoked_over_expired_and_active_only_while_neither()
    {
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var key = new ApiAccessKey { Name = "phone", Prefix = "sgx_abcdefgh", SecretHash = [1], ExpiresAt = now.AddDays(1) };

        key.StateAt(now).ShouldBe(AccessKeyState.Active);
        key.StateAt(now.AddDays(1)).ShouldBe(AccessKeyState.Expired);

        key.RevokedAt = now;
        key.StateAt(now).ShouldBe(AccessKeyState.Revoked);
        key.StateAt(now.AddDays(2)).ShouldBe(AccessKeyState.Revoked);
    }
}
