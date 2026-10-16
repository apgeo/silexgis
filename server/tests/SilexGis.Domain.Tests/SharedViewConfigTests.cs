// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a saved view's document says a shared link may draw — read so that anything unreadable
/// is something the link does not show — and the part of the world the link answers about.
/// </summary>
public class SharedViewConfigTests
{
    [Fact]
    public void Reads_the_camera_the_background_the_two_overlays_and_the_tag_a_link_can_draw()
    {
        var config = SharedViewConfig.Read(
            """{"configVersion":1,"center":[25,45],"zoom":9,"baseLayerId":7,"entrancesVisible":true,"surfaceFeaturesVisible":false,"photosVisible":true,"tagFilter":"leads"}""");

        Assert.Equal(
            new SharedViewConfig(7, true, false, "leads", new SharedViewCamera(25, 45, 9)),
            config);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"entrancesVisible":"true","surfaceFeaturesVisible":1,"baseLayerId":"7"}""")]
    [InlineData("""{"baseLayerId":7.5}""")]
    public void Anything_it_cannot_read_is_something_the_link_does_not_draw(string? document)
    {
        var config = SharedViewConfig.Read(document);

        Assert.False(config.EntrancesVisible);
        Assert.False(config.SurfaceFeaturesVisible);
        Assert.Null(config.BaseLayerId);
    }

    /// <summary>
    /// A view that does not say readably where it is shares no place, so its link draws no data;
    /// the background it chose still stands.
    /// </summary>
    [Theory]
    [InlineData("""{"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":[25,45],"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":[25],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":["25","45"],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":[200,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":[25,95],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    [InlineData("""{"center":[25,45],"zoom":-1,"entrancesVisible":true,"surfaceFeaturesVisible":true,"baseLayerId":3}""")]
    public void A_view_with_no_readable_camera_draws_no_data_on_its_link(string document)
    {
        var config = SharedViewConfig.Read(document);

        Assert.Null(config.Camera);
        Assert.False(config.EntrancesVisible);
        Assert.False(config.SurfaceFeaturesVisible);
        Assert.Equal(3, config.BaseLayerId);
    }

    /// <summary>
    /// A tag filter that is there but cannot be read narrows to nothing rather than to everything:
    /// read as "no filter", it would widen the view its owner shared.
    /// </summary>
    [Theory]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":7}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":["leads"]}""")]
    public void An_unreadable_tag_filter_draws_no_data_rather_than_all_of_it(string document)
    {
        var config = SharedViewConfig.Read(document);

        Assert.False(config.EntrancesVisible);
        Assert.False(config.SurfaceFeaturesVisible);
    }

    /// <summary>
    /// A view narrowed by a saved filter or a filter document draws no data through its link: a
    /// link runs no filter, so reading either as "no filter" would widen the view its owner shared.
    /// The same view with those keys null draws, which is what shows the keys switched it off.
    /// </summary>
    [Theory]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":null,"savedFilterId":"0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f"}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":null,"filterDocument":{"version":1,"scope":[],"sort":"updated","descending":true}}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"savedFilterId":7}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":"leads","filterDocument":"x"}""")]
    public void A_view_filtered_beyond_a_tag_draws_no_data_rather_than_all_of_it(string document)
    {
        var config = SharedViewConfig.Read(document);

        Assert.False(config.EntrancesVisible);
        Assert.False(config.SurfaceFeaturesVisible);

        var unfiltered = SharedViewConfig.Read(
            """{"center":[25,45],"zoom":9,"entrancesVisible":true,"surfaceFeaturesVisible":true,"tagFilter":null,"savedFilterId":null,"filterDocument":null}""");
        Assert.True(unfiltered.EntrancesVisible);
        Assert.True(unfiltered.SurfaceFeaturesVisible);
    }

    [Theory]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"tagFilter":null}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true,"tagFilter":"  "}""")]
    [InlineData("""{"center":[25,45],"zoom":9,"entrancesVisible":true}""")]
    public void No_tag_filter_is_no_narrowing(string document)
    {
        var config = SharedViewConfig.Read(document);

        Assert.True(config.EntrancesVisible);
        Assert.Null(config.TagFilter);
    }

    [Fact]
    public void A_zoom_above_the_highest_reads_as_the_highest()
    {
        var config = SharedViewConfig.Read("""{"center":[25,45],"zoom":31,"entrancesVisible":true}""");

        Assert.Equal(MapZoom.Highest, config.Camera!.Zoom);
        Assert.Equal(MapZoom.Highest - SharedViewReach.ZoomOutLevels, config.Camera.Reach.LowestZoom);
    }

    /// <summary>
    /// The reach is the view's extent on the reference screen with one extent of margin on every
    /// side: three screens wide and three high around the centre, in web-mercator metres.
    /// </summary>
    [Fact]
    public void The_reach_is_three_reference_screens_wide_and_high_round_the_centre()
    {
        var reach = SharedViewReach.Around(0, 0, 10);

        // At zoom 10 one pixel is 2πR / (256 · 2¹⁰) metres; at the equator a degree of longitude
        // is 2πR / 360 of them, so the half-width in degrees needs no projection to check.
        var halfWidthDegrees = 3 * SharedViewReach.ReferenceWidthPixels / 2d * 360d / (256d * 1024d);
        Assert.Equal(-halfWidthDegrees, reach.West, 9);
        Assert.Equal(halfWidthDegrees, reach.East, 9);
        Assert.True(reach.North > 0 && reach.South < 0);
        Assert.Equal(-reach.South, reach.North, 9);
        Assert.Equal(8, reach.LowestZoom);
    }

    [Fact]
    public void The_reach_narrows_in_longitude_by_half_with_each_zoom_level_in()
    {
        var wide = SharedViewReach.Around(25, 46, 11);
        var narrow = SharedViewReach.Around(25, 46, 12);

        Assert.Equal((wide.East - wide.West) / 2, narrow.East - narrow.West, 9);
        Assert.True(narrow.West > wide.West && narrow.East < wide.East);
        Assert.True(narrow.South > wide.South && narrow.North < wide.North);
    }

    [Fact]
    public void A_view_saved_far_out_reaches_the_whole_world_and_stops_at_its_edges()
    {
        var reach = SharedViewReach.Around(179, 80, 1.5);

        Assert.Equal(-180, reach.West);
        Assert.Equal(180, reach.East);
        Assert.True(reach.North < 90);
        Assert.Equal(0, reach.LowestZoom);
    }

    [Fact]
    public void A_window_is_clipped_to_the_reach_and_one_outside_it_is_nothing()
    {
        var reach = new SharedViewReach(10, 40, 12, 42, 8);

        Assert.Equal((10d, 40d, 11d, 41d), reach.Clip(-180, -90, 11, 41)!.Value);
        Assert.Equal((10d, 40d, 12d, 42d), reach.Clip(-180, -90, 180, 90)!.Value);
        Assert.Null(reach.Clip(12, 40, 13, 42));
        Assert.Null(reach.Clip(20, 50, 21, 51));
    }
}
