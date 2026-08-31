// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The catalogue reader, exercised as text in and records out.
/// </summary>
/// <remarks>
/// Every case here is one an operator's own file will eventually be: half-edited, copied from a
/// QGIS export, or carrying a source whose key nobody set yet. What they have in common is that
/// the wrong answer is silent — a source that quietly does not appear, or one that appears and
/// draws nothing — so what is asserted throughout is that the reader SAYS something, not merely
/// that it survives.
/// </remarks>
public class MapLayerCatalogTests
{
    private static string Doc(string inner) =>
        $"<silexgis-map-layers version=\"1\">{inner}</silexgis-map-layers>";

    [Fact]
    public void Reads_a_plain_entry_with_the_documented_defaults()
    {
        var result = MapLayerCatalog.Read(Doc(
            """<layer name="OpenStreetMap" url="https://tile.example/{z}/{x}/{y}.png" />"""));

        Assert.Empty(result.Problems);
        var layer = Assert.Single(result.Layers);
        Assert.Equal("OpenStreetMap", layer.Name);
        // A bare entry is an enabled basemap that is not the default and covers the ordinary
        // zoom range. Asserted because every one of these is a decision somebody relies on by
        // NOT writing the attribute.
        Assert.True(layer.IsBase);
        Assert.False(layer.IsDefault);
        Assert.True(layer.Enabled);
        Assert.Equal(0, layer.MinZoom);
        Assert.Equal(19, layer.MaxZoom);
        Assert.Null(layer.ApiKeyName);
    }

    [Fact]
    public void Orders_layers_by_their_position_in_the_file()
    {
        var result = MapLayerCatalog.Read(Doc(
            """
            <layer name="First" url="https://a.example/{z}/{x}/{y}.png" />
            <layer name="Second" url="https://b.example/{z}/{x}/{y}.png" />
            <layer name="Third" url="https://c.example/{z}/{x}/{y}.png" />
            """));

        Assert.Equal(["First", "Second", "Third"], result.Layers.Select(l => l.Name));
        // Spread rather than consecutive, so a source can be slotted between two others by hand
        // without renumbering anything.
        Assert.Equal([10, 20, 30], result.Layers.Select(l => l.SortOrder));
    }

    [Fact]
    public void Keeps_the_good_entries_when_one_is_malformed()
    {
        // The case this whole design turns on: a file is edited by hand, so one entry will
        // eventually be wrong, and losing the other thirty-nine over it would take an
        // installation's entire basemap list away for a missing quote.
        var result = MapLayerCatalog.Read(Doc(
            """
            <layer name="Good" url="https://a.example/{z}/{x}/{y}.png" />
            <layer name="No url at all" />
            <layer name="Also good" url="https://b.example/{z}/{x}/{y}.png" />
            """));

        Assert.Equal(["Good", "Also good"], result.Layers.Select(l => l.Name));
        Assert.Contains(result.Problems, p => p.Message.Contains("No url at all"));
    }

    [Fact]
    public void Refuses_an_entry_naming_a_key_its_url_has_nowhere_to_put()
    {
        // Would otherwise publish a url that fetches tiles anonymously and works right up until
        // the provider starts refusing them, which for a rate-limited service is under load.
        var result = MapLayerCatalog.Read(Doc(
            """<layer name="Keyed" url="https://tile.example/{z}/{x}/{y}.png" apiKey="Thunderforest" />"""));

        Assert.Empty(result.Layers);
        Assert.Contains(result.Problems, p => p.Message.Contains("{apikey}"));
    }

    [Fact]
    public void Refuses_an_entry_whose_url_wants_a_key_it_never_names()
    {
        // The mirror image, and the worse of the two: the placeholder would be sent literally,
        // every tile would answer 401, and the map would simply be blank.
        var result = MapLayerCatalog.Read(Doc(
            """<layer name="Keyed" url="https://tile.example/{z}/{x}/{y}.png?apikey={apikey}" />"""));

        Assert.Empty(result.Layers);
        Assert.Contains(result.Problems, p => p.Message.Contains("apiKey"));
    }

    [Fact]
    public void Refuses_a_zoom_range_the_wrong_way_round()
    {
        var result = MapLayerCatalog.Read(Doc(
            """<layer name="Backwards" url="https://a.example/{z}/{x}/{y}.png" minZoom="12" maxZoom="8" />"""));

        Assert.Empty(result.Layers);
        Assert.Contains(result.Problems, p => p.Message.Contains("maxZoom"));
    }

    [Fact]
    public void Takes_the_last_of_two_entries_sharing_a_name_and_says_so()
    {
        var result = MapLayerCatalog.Read(Doc(
            """
            <layer name="Twice" url="https://old.example/{z}/{x}/{y}.png" />
            <layer name="Twice" url="https://new.example/{z}/{x}/{y}.png" />
            """));

        var layer = Assert.Single(result.Layers);
        Assert.Equal("https://new.example/{z}/{x}/{y}.png", layer.UrlTemplate);
        Assert.Contains(result.Problems, p => p.Message.Contains("more than once"));
    }

    [Fact]
    public void Reports_a_file_that_is_not_xml_at_all_rather_than_throwing()
    {
        // What a truncated write, or a half-pasted file, actually looks like. Startup must survive
        // it: the stored catalogue from the previous start is still perfectly good.
        var result = MapLayerCatalog.Read("<silexgis-map-layers><layer name=\"Cut off\"");

        Assert.Empty(result.Layers);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void Substitutes_a_configured_key_into_the_url()
    {
        var entry = Assert.Single(MapLayerCatalog.Read(Doc(
            """<layer name="Keyed" url="https://tile.example/{z}/{x}/{y}.png?apikey={apikey}" apiKey="Thunderforest" />""")).Layers);

        var url = MapLayerCatalog.ResolveUrl(entry, new Dictionary<string, string> { ["Thunderforest"] = "abc123" });

        Assert.Equal("https://tile.example/{z}/{x}/{y}.png?apikey=abc123", url);
        // The z/x/y placeholders are the tile client's and must survive untouched — substituting
        // them here would produce a url that fetches one tile forever.
        Assert.Contains("{z}/{x}/{y}", url);
    }

    [Fact]
    public void Withholds_a_url_whose_key_is_not_configured()
    {
        var entry = Assert.Single(MapLayerCatalog.Read(Doc(
            """<layer name="Keyed" url="https://tile.example/{z}/{x}/{y}.png?apikey={apikey}" apiKey="Thunderforest" />""")).Layers);

        Assert.Null(MapLayerCatalog.ResolveUrl(entry, new Dictionary<string, string>()));
        // Whitespace is not a key. A .env line left as `SILEXGIS__MapLayers__ApiKeys__X=` is the
        // ordinary way a key ends up empty, and treating it as set publishes a broken layer.
        Assert.Null(MapLayerCatalog.ResolveUrl(entry, new Dictionary<string, string> { ["Thunderforest"] = "   " }));
    }

    [Fact]
    public void Reads_an_overlay_and_a_disabled_entry_as_declared()
    {
        var result = MapLayerCatalog.Read(Doc(
            """
            <layer name="Hiking" url="https://a.example/{z}/{x}/{y}.png" base="false" group="Overlays" />
            <layer name="Restricted" url="https://b.example/{z}/{x}/{y}.png" enabled="false" />
            """));

        var hiking = result.Layers.Single(l => l.Name == "Hiking");
        Assert.False(hiking.IsBase);
        Assert.Equal("Overlays", hiking.GroupName);

        // Declared and off, not absent: an entry somebody can see and switch on deliberately is
        // the whole reason the restricted sources ship at all.
        Assert.False(result.Layers.Single(l => l.Name == "Restricted").Enabled);
    }
}
