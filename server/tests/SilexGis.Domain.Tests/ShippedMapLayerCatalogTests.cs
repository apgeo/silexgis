// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The catalogue this project actually ships, read through the reader that will read it at
/// startup.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MapLayerCatalogTests"/> covers the reader against documents written for each case.
/// Nothing covered the shipped file, and it is edited by hand more often than the reader is: a
/// typo in it compiles, passes every test, and is first noticed when an installation starts and a
/// source is silently missing from the panel — or, for a bad url template, when a viewer picks a
/// basemap and the map goes blank. That is a long way from the edit that caused it.
/// </para>
/// <para>
/// So the assertions below are deliberately about the properties an edit breaks by accident,
/// not about which sources are present: naming a source twice, forgetting the {z}/{x}/{y} the
/// url is addressed by, leaving two basemaps claiming to be the default, or naming an api key
/// without leaving anywhere to put it. Adding, removing or switching a source is an ordinary
/// edit and must not have to come here for permission.
/// </para>
/// </remarks>
public class ShippedMapLayerCatalogTests
{
    private static MapLayerCatalogResult Shipped() =>
        MapLayerCatalog.Read(File.ReadAllText(
            Path.Combine(RepositoryRoot(), "server", "src", "SilexGis.Api", "map-layers.xml")));

    [Fact]
    public void The_shipped_catalogue_reads_without_complaint()
    {
        var result = Shipped();

        // Problems carry the reason; surfacing them beats asserting an empty collection and
        // printing nothing but a count when this fails.
        Assert.True(
            result.Problems.Count == 0,
            string.Join("; ", result.Problems.Select(p => p.Message)));
        Assert.NotEmpty(result.Layers);
    }

    [Fact]
    public void Every_shipped_source_is_addressable()
    {
        foreach (var layer in Shipped().Layers)
        {
            Assert.False(string.IsNullOrWhiteSpace(layer.Name));
            foreach (var token in new[] { "{z}", "{x}", "{y}" })
            {
                Assert.True(
                    layer.UrlTemplate.Contains(token, StringComparison.Ordinal),
                    $"{layer.Name}: the url has no {token}, so every tile request builds the same address");
            }

            // A key named but never substituted publishes a layer that answers 401 for every
            // tile, which on screen is indistinguishable from a source that is simply down.
            if (layer.ApiKeyName is not null)
            {
                Assert.True(
                    layer.UrlTemplate.Contains("{apikey}", StringComparison.Ordinal),
                    $"{layer.Name}: names the key {layer.ApiKeyName} but the url has nowhere to put it");
            }
        }
    }

    [Fact]
    public void Exactly_one_shipped_basemap_is_the_default()
    {
        var defaults = Shipped().Layers.Where(l => l.IsDefault).Select(l => l.Name).ToList();

        Assert.True(defaults.Count == 1, $"expected one default basemap, found: {string.Join(", ", defaults)}");
        Assert.True(
            Shipped().Layers.Single(l => l.IsDefault).Enabled,
            "the default basemap ships switched off, so a viewer who has chosen nothing gets nothing");
    }

    // There is deliberately no test here that no source is named twice. One was written, and it
    // could not fail: the reader collapses duplicates by name before returning them, so the list
    // a test reads has already had the second copy removed. The duplicate is caught instead by
    // the problems assertion above, because the reader records "'X' is declared more than once"
    // rather than swallowing it — which was confirmed by duplicating an entry and watching that
    // test, and only that test, go red.

    /// <summary>
    /// The checkout both halves of the application sit in, found by walking up from the test
    /// assembly rather than assumed, so the path holds wherever the build output lands.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "client", "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "server", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"no checkout containing both halves of the application was found above {AppContext.BaseDirectory}");
    }
}
