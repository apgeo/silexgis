// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.DataProtection;
using Shouldly;
using SilexGis.Api.Common;

namespace SilexGis.Api.Tests;

/// <summary>
/// The address-borne permission to fetch one computed terrain raster: how long it lasts, what it
/// opens, and that a changed token opens nothing.
/// </summary>
/// <remarks>
/// The protector compares a token's expiry with the wall clock when it is read, so the clock moved
/// here is the one the token was minted on: a token minted nine minutes ago has a minute left and
/// one minted eleven minutes ago has expired a minute since, which is the same question asked from
/// the other end.
/// </remarks>
public sealed class TerrainRasterTokenServiceTests
{
    private readonly IDataProtectionProvider provider = new EphemeralDataProtectionProvider();

    private readonly Guid layer = Guid.NewGuid();

    [Fact]
    public void A_token_opens_its_raster_for_ten_minutes_and_not_after()
    {
        var nineMinutesAgo = Minted(TimeSpan.FromMinutes(-9));
        nineMinutesAgo.Validate(nineMinutesAgo.CreateToken(layer, 7), layer, 7).ShouldBeTrue();

        var elevenMinutesAgo = Minted(TimeSpan.FromMinutes(-11));
        elevenMinutesAgo.Validate(elevenMinutesAgo.CreateToken(layer, 7), layer, 7).ShouldBeFalse();
    }

    [Fact]
    public void A_token_opens_only_the_raster_and_layer_it_was_minted_for()
    {
        var service = Minted(TimeSpan.Zero);
        var token = service.CreateToken(layer, 7);

        service.Validate(token, layer, 7).ShouldBeTrue();
        service.Validate(token, layer, 8).ShouldBeFalse();
        service.Validate(token, Guid.NewGuid(), 7).ShouldBeFalse();
        service.Validate(null, layer, 7).ShouldBeFalse();
        service.Validate("", layer, 7).ShouldBeFalse();
    }

    [Fact]
    public void A_token_with_any_one_character_changed_opens_nothing()
    {
        var service = Minted(TimeSpan.Zero);
        var token = service.CreateToken(layer, 7);
        service.Validate(token, layer, 7).ShouldBeTrue();

        // Every position but the last: the last character of unpadded base64 can carry bits the
        // decoder drops, so changing it may leave the bytes as they were. Everywhere else a changed
        // character is a changed byte, and the signature over it must fail.
        for (var i = 0; i < token.Length - 1; i++)
        {
            var flipped = token[i] == 'A' ? 'B' : 'A';
            var changed = string.Concat(token.AsSpan(0, i), flipped.ToString(), token.AsSpan(i + 1));
            service.Validate(changed, layer, 7).ShouldBeFalse($"changed at {i}");
        }
    }

    private TerrainRasterTokenService Minted(TimeSpan offset) =>
        new(provider, new Offset(offset));

    private sealed class Offset(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow().Add(offset);
    }
}
