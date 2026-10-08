// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Features.TripTracking;

namespace SilexGis.Api.Tests;

/// <summary>
/// The validator of a published-trip answer, asked directly: which two answers it calls the same.
/// </summary>
/// <remarks>
/// Every "the same" here stands beside a "different" made from the same answer, because a function
/// that returned one value for everything would pass the first half of each of these alone.
/// </remarks>
public sealed class PublicTripValidatorTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Noon = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static string Of(object answer, DateTimeOffset? at = null) =>
        PublicTripValidator.Of(answer, Options, at ?? Noon);

    private static object Envelope(string title, string signature, int size = 256, string station = "a.1") => new
    {
        title,
        model = new
        {
            modelUrl = $"/api/v1/files/0f0e/content?token={signature}",
            meshUrl = (string?)null,
            pictures = new[]
            {
                new { station, thumbnailUrl = $"/api/v1/files/0a0b/thumbnail?size={size}&token={signature}" },
            },
        },
    };

    [Fact]
    public void An_answer_signed_afresh_is_the_same_answer_and_any_other_difference_is_not()
    {
        var first = Of(Envelope("Evening trip", "signature-one"));

        Of(Envelope("Evening trip", "signature-two")).ShouldBe(first);

        Of(Envelope("Evening trip, renamed", "signature-one")).ShouldNotBe(first);
        // What sits beside a signature in the same address is part of the answer: only the
        // signature's own value is left out.
        Of(Envelope("Evening trip", "signature-one", size: 512)).ShouldNotBe(first);
        Of(Envelope("Evening trip", "signature-one", station: "a.2")).ShouldNotBe(first);
    }

    [Fact]
    public void Only_an_answer_carrying_a_signed_address_gets_a_new_validator_when_its_stretch_of_time_is_over()
    {
        var bucket = PublicTripValidator.SignedAddressBucket;
        bucket.ShouldBeGreaterThan(TimeSpan.Zero);
        // Counted from a fixed origin, so the start of one stretch and its last second are known.
        var start = DateTimeOffset.FromUnixTimeSeconds(
            Noon.ToUnixTimeSeconds() / (long)bucket.TotalSeconds * (long)bucket.TotalSeconds);
        var lastSecond = start + bucket - TimeSpan.FromSeconds(1);
        var next = start + bucket;

        var signed = Envelope("Evening trip", "signature-one");
        Of(signed, lastSecond).ShouldBe(Of(signed, start));
        Of(signed, next).ShouldNotBe(Of(signed, start));

        var list = new { trips = new[] { new { title = "Evening trip", state = "armed" } }, more = false };
        Of(list, next).ShouldBe(Of(list, start));
        Of(list, next + (10 * bucket)).ShouldBe(Of(list, start));
    }

    [Fact]
    public void A_text_that_only_resembles_a_signed_address_is_compared_whole()
    {
        // No "?" or "&" in front of it: this is somebody's wording, not a parameter of an address.
        var first = Of(new { title = "our token=blue" });
        Of(new { title = "our token=green" }).ShouldNotBe(first);
        Of(new { title = "our token=blue" }, Noon + (10 * PublicTripValidator.SignedAddressBucket)).ShouldBe(first);
    }

    [Fact]
    public void The_value_is_safe_inside_a_list_of_tags_and_behind_a_compressing_proxy()
    {
        var value = Of(Envelope("Evening trip", "signature-one"));

        value.Length.ShouldBe(32);
        value.ShouldAllBe(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    [Fact]
    public void An_address_signed_twice_over_and_one_with_a_fragment_lose_only_the_signatures()
    {
        var first = Of(new { url = "/x?token=one&size=4&token=two#frame" });

        Of(new { url = "/x?token=uno&size=4&token=dos#frame" }).ShouldBe(first);
        Of(new { url = "/x?token=one&size=5&token=two#frame" }).ShouldNotBe(first);
        Of(new { url = "/x?token=one&size=4&token=two#other" }).ShouldNotBe(first);
    }
}
