// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Map;
using SilexGis.Domain.Settings;

namespace SilexGis.Domain.Tests;

public sealed class MapSymbolTests
{
    [Theory]
    [InlineData("pit.png")]
    [InlineData("osm_cave.svg")]
    [InlineData("fgdc_bedding-inclined.svg")]
    [InlineData("shape:cave")]
    [InlineData("shape:circle")]
    [InlineData("shape:cross:000000")]
    [InlineData("shape:cave:d46b08")]
    public void A_bundled_file_name_or_a_drawn_shape_is_a_symbol(string reference) =>
        MapSymbol.IsValid(reference).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../pit.png")]
    [InlineData("/feature_symbols/pit.png")]
    [InlineData("https://elsewhere.example/pin.svg")]
    [InlineData("Pit.png")]
    [InlineData("pit.gif")]
    [InlineData("pit.svg ")]
    [InlineData("shape:hexagon")]
    [InlineData("shape:cave:D46B08")]
    [InlineData("shape:cave:#d46b08")]
    [InlineData("shape:cave:d46b0")]
    [InlineData("shape:")]
    [InlineData("shape:cave:d46b08:extra")]
    public void Anything_else_is_not(string? reference) =>
        MapSymbol.IsValid(reference).ShouldBeFalse();

    [Fact]
    public void A_name_longer_than_the_column_is_not_a_symbol() =>
        MapSymbol.IsValid(new string('a', MapSymbol.MaxLength) + ".svg").ShouldBeFalse();

    [Fact]
    public void Every_listed_shape_is_accepted_and_the_defaults_are_symbols()
    {
        foreach (var shape in MapSymbol.Shapes)
        {
            MapSymbol.IsValid($"shape:{shape}").ShouldBeTrue(shape);
        }

        MapSymbol.IsValid(MapSymbol.DefaultEntrance).ShouldBeTrue();
        MapSymbol.IsValid(MapSymbol.DefaultApproximateEntrance).ShouldBeTrue();
        MapSymbol.DefaultApproximateEntrance.ShouldNotBe(MapSymbol.DefaultEntrance);
    }

    [Fact]
    public void A_stored_section_with_a_malformed_reference_is_read_with_the_default_in_its_place()
    {
        var stored = new MapSymbolSettings { EntranceSymbol = "osm_cave.svg", ApproximateEntranceSymbol = "../x.svg" };

        var read = stored.Sanitised();

        read.EntranceSymbol.ShouldBe("osm_cave.svg");
        read.ApproximateEntranceSymbol.ShouldBe(MapSymbol.DefaultApproximateEntrance);
        new MapSymbolSettings { EntranceSymbol = null! }.Sanitised().EntranceSymbol.ShouldBe(MapSymbol.DefaultEntrance);
    }
}
