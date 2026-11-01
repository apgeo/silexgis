// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Domain.Tests;

/// <summary>The payload a phone's place-to-station mapping keeps: a station, and a splay end's offset.</summary>
public sealed class PlaceStationAnchorTests
{
    [Fact]
    public void A_station_and_a_splay_end_are_written_and_read_back_exactly()
    {
        var station = PlaceStationAnchor.Read(PlaceStationAnchor.Write("cave.upper.12", null));
        station.ShouldNotBeNull();
        station.Value.Station.ShouldBe("cave.upper.12");
        station.Value.Splay.ShouldBeNull();

        var splay = PlaceStationAnchor.Read(PlaceStationAnchor.Write("A.3", [1.5, -2, 0.125]));
        splay.ShouldNotBeNull();
        splay.Value.Station.ShouldBe("A.3");
        splay.Value.Splay.ShouldBe([1.5, -2, 0.125]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"splay":[1,2,3]}""")]
    [InlineData("""{"station":12}""")]
    [InlineData("""{"station":"A","splay":[1,2]}""")]
    [InlineData("""{"station":"A","splay":[1,"2",3]}""")]
    [InlineData("""{"shape":"point","x":0.1,"y":0.2}""")]
    [InlineData("not json")]
    public void Anything_else_is_not_a_station(string? payload) =>
        PlaceStationAnchor.Read(payload).ShouldBeNull();

    [Fact]
    public void A_station_is_required_and_bounded_and_a_splay_is_three_finite_numbers_within_reach()
    {
        PlaceStationAnchor.Problem("1.12", null).ShouldBeNull();
        PlaceStationAnchor.Problem("1.12", [0, 0, -10_000]).ShouldBeNull();
        PlaceStationAnchor.Problem(null, null).ShouldNotBeNull();
        PlaceStationAnchor.Problem("  ", null).ShouldNotBeNull();
        PlaceStationAnchor.Problem(new string('a', PlaceStationAnchor.StationMaxLength + 1), null).ShouldNotBeNull();
        PlaceStationAnchor.Problem(new string('a', PlaceStationAnchor.StationMaxLength), null).ShouldBeNull();
        PlaceStationAnchor.Problem("A", [1, 2]).ShouldNotBeNull();
        PlaceStationAnchor.Problem("A", [1, 2, 3, 4]).ShouldNotBeNull();
        PlaceStationAnchor.Problem("A", [1, double.NaN, 3]).ShouldNotBeNull();
        PlaceStationAnchor.Problem("A", [1, 2, 10_000.5]).ShouldNotBeNull();
    }

    [Fact]
    public void Two_splays_are_the_same_offset_only_when_both_are_absent_or_equal_on_every_axis()
    {
        PlaceStationAnchor.SameSplay(null, null).ShouldBeTrue();
        PlaceStationAnchor.SameSplay([1, 2, 3], [1, 2, 3]).ShouldBeTrue();
        PlaceStationAnchor.SameSplay([1, 2, 3], null).ShouldBeFalse();
        PlaceStationAnchor.SameSplay(null, [1, 2, 3]).ShouldBeFalse();
        PlaceStationAnchor.SameSplay([1, 2, 3], [1, 2, 4]).ShouldBeFalse();
    }
}
