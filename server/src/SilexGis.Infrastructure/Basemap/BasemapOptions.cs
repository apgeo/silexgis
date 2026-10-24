// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Basemap;

/// <summary>The basemap this installation serves itself: whether it does, where it keeps it, and where it cuts it from.</summary>
public sealed class BasemapOptions
{
    public const string SectionName = "Basemap";

    /// <summary>
    /// Whether this installation serves a vector basemap of its own. On by default: the packaged
    /// stack runs the tile server for it, and until an administrator downloads a region there is
    /// simply nothing to serve — the entry naming it is not offered, and the administration page
    /// says why. Off, the entry is never offered and no region can be downloaded.
    /// </summary>
    public bool SelfHosted { get; set; } = true;

    /// <summary>
    /// The directory the extract is kept in — the one the tile server reads. Relative paths resolve
    /// against the application's own directory, the way the file store's root does.
    /// </summary>
    public string Directory { get; set; } = Path.Combine("data", "basemap");

    /// <summary>
    /// Where a region is cut from: a planet build's address, with <c>{build}</c> where its date
    /// goes. Fixed here, by whoever runs the installation; the administration page chooses a date
    /// and a box, never an address.
    /// </summary>
    public string SourceUrlTemplate { get; set; } = "https://build.protomaps.com/{build}.pmtiles";

    /// <summary>The list of the builds that source publishes, read to offer them and to find the latest.</summary>
    public string BuildsUrl { get; set; } = "https://build-metadata.protomaps.dev/builds.json";

    /// <summary>The program that cuts a region out of a build: the <c>pmtiles</c> command-line tool.</summary>
    public string ExtractorPath { get; set; } = "pmtiles";

    /// <summary>
    /// The largest extract this installation accepts, in bytes. Romania to the last level is about
    /// 1.3 GB and Europe about 49 GB; the default, 5 GB, takes a country and refuses a continent by
    /// accident. A download is also refused when the disk has no room for it beside the current one.
    /// </summary>
    public long MaxBytes { get; set; } = 5_000_000_000;

    /// <summary>
    /// A region downloaded by itself on the first start that finds no extract and no download ever
    /// asked for: <c>romania</c>, or <c>apuseni</c> for a karst region; empty, the default, for
    /// none. For an installation that should come up with a basemap of its own without anybody
    /// pressing a button — a test or showcase installation — and done once: after that the
    /// administration page decides.
    /// </summary>
    public string? InitialRegion { get; set; }
}
