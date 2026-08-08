// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A column of positions out of a club's spreadsheet, which is three people's habits in one
/// file. Every form below is one somebody actually writes.
/// </summary>
public class CoordinateTextTests
{
    [Theory]
    [InlineData("46.5432", 46.5432)]
    [InlineData("46,5432", 46.5432)]           // decimal comma, as most of Europe writes it
    [InlineData("+46.5432", 46.5432)]
    [InlineData("-46.5432", -46.5432)]
    [InlineData("46°32'35.5\"N", 46.54319444)]
    [InlineData("N 46 32 35.5", 46.54319444)]
    [InlineData("46 32.5915 N", 46.54319167)]  // degrees and decimal minutes, the GPS default
    [InlineData("46°32.5915'", 46.54319167)]
    [InlineData("S 46.5432", -46.5432)]
    [InlineData("46.5432 V", -46.5432)]        // "vest" — Romanian for west
    [InlineData("23.1234 E", 23.1234)]
    public void The_forms_a_file_actually_carries_all_read(string text, double expected)
    {
        var parsed = CoordinateText.Parse(text);
        parsed.ShouldNotBeNull();
        parsed.Value.ShouldBe(expected, 0.000001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nord")]
    [InlineData("46 75 12")]     // 75 minutes is a value split on the wrong thing, not a coordinate
    [InlineData("1 2 3 4")]
    public void What_cannot_be_a_coordinate_is_refused_rather_than_guessed_at(string? text)
    {
        CoordinateText.Parse(text).ShouldBeNull();
    }

    [Fact]
    public void A_thousands_separator_is_told_apart_from_a_decimal_comma()
    {
        // Both marks present means the comma groups thousands; only a comma means it is the
        // decimal point. Getting this backwards turns an altitude of 1,234.5 m into 12345.
        CoordinateText.ParseNumber("1,234.5").ShouldBe(1234.5);
        CoordinateText.ParseNumber("1234,5").ShouldBe(1234.5);
        CoordinateText.ParseNumber("431").ShouldBe(431);
        CoordinateText.ParseNumber("not a number").ShouldBeNull();
    }
}
