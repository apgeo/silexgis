// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The installation's own basemap: which boxes, levels and builds a download may ask for, what its
/// extract is called and served under, and how the extracting tool's output is read.
/// </summary>
public class SelfHostedBasemapTests
{
    [Fact]
    public void Offers_romania_and_a_karst_region_and_accepts_any_box_of_the_world()
    {
        SelfHostedBasemap.RegionProblem(SelfHostedBasemap.Romania).ShouldBeNull();
        SelfHostedBasemap.RegionProblem(SelfHostedBasemap.Apuseni).ShouldBeNull();
        SelfHostedBasemap.Presets.Keys.ShouldBe(["romania", "apuseni"], ignoreOrder: true);
        SelfHostedBasemap.RegionProblem(new BasemapRegion(-180, -85, 180, 85)).ShouldBeNull();
    }

    [Theory]
    [InlineData(23.4, 46.2, 22.2, 47.0)] // east of nothing
    [InlineData(22.2, 47.0, 23.4, 46.2)] // north below south
    [InlineData(22.2, 46.2, 22.2, 47.0)] // no width
    [InlineData(-181, 46.2, 23.4, 47.0)]
    [InlineData(22.2, 46.2, 23.4, 86)] // past where web-mercator tiles end
    [InlineData(double.NaN, 46.2, 23.4, 47.0)]
    public void Refuses_a_box_that_is_not_one_of_the_world(double west, double south, double east, double north)
    {
        SelfHostedBasemap.RegionProblem(new BasemapRegion(west, south, east, north)).ShouldNotBeNull();
    }

    [Fact]
    public void Takes_levels_up_to_the_last_a_build_holds_and_builds_written_as_their_date()
    {
        SelfHostedBasemap.IsZoom(0).ShouldBeTrue();
        SelfHostedBasemap.IsZoom(15).ShouldBeTrue();
        SelfHostedBasemap.IsZoom(16).ShouldBeFalse();
        SelfHostedBasemap.IsZoom(-1).ShouldBeFalse();

        SelfHostedBasemap.IsBuild("20261024").ShouldBeTrue();
        SelfHostedBasemap.IsBuild("20261324").ShouldBeFalse();
        SelfHostedBasemap.IsBuild("2026-10-24").ShouldBeFalse();
        SelfHostedBasemap.IsBuild("../../etc").ShouldBeFalse();
        SelfHostedBasemap.IsBuild(null).ShouldBeFalse();
        SelfHostedBasemap.Latest(["20261022", "20261024", "junk", "20261023"]).ShouldBe("20261024");
        SelfHostedBasemap.Latest([]).ShouldBeNull();
    }

    /// <summary>
    /// An extract's name is its address, and an address is never reused: two extracts of one build
    /// cut a second apart are two names, and the one being served is the one cut last.
    /// </summary>
    [Fact]
    public void Names_each_extract_by_its_build_and_the_moment_it_was_cut_and_serves_the_latest()
    {
        var at = new DateTimeOffset(2026, 10, 24, 14, 24, 44, TimeSpan.FromHours(3));
        var name = SelfHostedBasemap.FileName("20261024", at);

        name.ShouldBe("basemap-20261024-20261024112444.pmtiles");
        SelfHostedBasemap.SourceId(name).ShouldBe("basemap-20261024-20261024112444");
        SelfHostedBasemap.ParseFileName(name).ShouldBe(("20261024", at.ToUniversalTime()));
        SelfHostedBasemap.ParseFileName("basemap-20261024-20261024112444.pmtiles.partial").ShouldBeNull();
        SelfHostedBasemap.ParseFileName("romania.pmtiles").ShouldBeNull();

        SelfHostedBasemap.Current(
            [
                "basemap-20261024-20261024112444.pmtiles",
                "basemap-20261023-20261024120000.pmtiles", // an older build cut later is the newer extract
                "basemap-20261025-20261025000000.pmtiles.partial",
                "notes.txt",
            ]).ShouldBe("basemap-20261023-20261024120000.pmtiles");
        SelfHostedBasemap.Current([]).ShouldBeNull();
    }

    [Fact]
    public void Publishes_a_self_hosted_entry_only_with_an_extract_and_under_its_name()
    {
        const string template = "/basemap/tiles/{basemap}/{z}/{x}/{y}";

        SelfHostedBasemap.IsSelfHosted(template).ShouldBeTrue();
        SelfHostedBasemap.Published(template, "basemap-20261024-20261024112444")
            .ShouldBe("/basemap/tiles/basemap-20261024-20261024112444/{z}/{x}/{y}");
        SelfHostedBasemap.Published(template, null).ShouldBeNull();
        // Any other source is published as it stands, extract or none.
        SelfHostedBasemap.Published("https://tile.example/{z}/{x}/{y}.png", null).ShouldBe("https://tile.example/{z}/{x}/{y}.png");
    }

    [Theory]
    [InlineData("2026/10/24 14:24:15 extract.go:612: Extract transferred 38 MB (overfetch 0.05) for an archive size of 36 MB", 36_000_000L)]
    [InlineData("… for an archive size of 1.3 GB\n", 1_300_000_000L)]
    [InlineData("… for an archive size of 640 kB", 640_000L)]
    [InlineData("nothing of the kind", null)]
    public void Reads_the_size_an_extraction_reports(string output, long? bytes)
    {
        SelfHostedBasemap.ArchiveSize(output).ShouldBe(bytes);
    }

    [Theory]
    [InlineData("fetching chunks   0% |    | ( 0 B/36 MB) [0s:0s]\rfetching chunks  45% |███ | (16/36 MB, 4.9 MB/s)", 45)]
    [InlineData("fetching chunks 100% |██████| (36/36 MB, 2.9 MB/s)", 100)]
    [InlineData("2026/10/24 14:24:09 extract.go:401: fetching 12 dirs, 12 chunks, 9 requests", null)]
    public void Reads_how_far_an_extraction_has_got(string output, int? percent)
    {
        SelfHostedBasemap.ProgressPercent(output).ShouldBe(percent);
    }
}
