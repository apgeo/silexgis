// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a saved view's document says a shared link may draw — read so that anything unreadable
/// is something the link does not show.
/// </summary>
public class SharedViewConfigTests
{
    [Fact]
    public void Reads_the_background_and_the_two_overlays_a_link_can_draw()
    {
        var config = SharedViewConfig.Read(
            """{"configVersion":1,"center":[25,45],"zoom":9,"baseLayerId":7,"entrancesVisible":true,"surfaceFeaturesVisible":false,"photosVisible":true}""");

        Assert.Equal(new SharedViewConfig(7, true, false), config);
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
}
