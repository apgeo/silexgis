// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Xml.Linq;

namespace SilexGis.Domain.Map;

/// <summary>
/// One tile source as an installation's catalogue file declares it, before any key is resolved.
/// </summary>
/// <param name="Name">
/// The catalogue's key for this source. Two entries sharing a name are one entry declared twice,
/// and the later wins — see <see cref="MapLayerCatalog.Read"/>.
/// </param>
/// <param name="ApiKeyName">
/// Which configured key <paramref name="UrlTemplate"/> needs, or null. Never the key itself.
/// </param>
/// <param name="Enabled">
/// Whether the operator has switched this source on. Sources whose terms of use forbid the way
/// this application would consume them ship declared and switched off, so that turning one on is
/// a deliberate act by somebody who can read the licence note beside it.
/// </param>
public sealed record MapLayerEntry(
    string Name,
    string UrlTemplate,
    string? Attribution,
    string? GroupName,
    string? ApiKeyName,
    bool IsBase,
    bool IsDefault,
    bool Enabled,
    int MinZoom,
    int MaxZoom,
    int SortOrder);

/// <summary>Why a catalogue file could not be read.</summary>
public sealed record MapLayerCatalogProblem(string Message);

/// <summary>The result of reading a catalogue file: what it declared, and what was wrong with it.</summary>
/// <remarks>
/// Both, not one or the other. A file with one malformed entry among forty is a file whose other
/// thirty-nine are perfectly good, and refusing all of them over one typo means an installation
/// loses its whole basemap list to a missing quote. Equally, dropping the bad entry silently means
/// nobody ever finds out the source they added is not there. So the good ones are returned and the
/// problems are returned beside them, for the caller to log.
/// </remarks>
public sealed record MapLayerCatalogResult(
    IReadOnlyList<MapLayerEntry> Layers,
    IReadOnlyList<MapLayerCatalogProblem> Problems);

/// <summary>
/// Reads the XML file in which an installation declares which tile sources it offers.
/// </summary>
/// <remarks>
/// <para>
/// A file rather than a table, and read on start rather than edited in the application, because
/// this is deployment configuration: it is the same kind of thing as which port to listen on. An
/// operator adding their national mapping agency's tile server wants to do it beside the rest of
/// their configuration, keep it in whatever holds their deployment, and have a fresh installation
/// come up with it already there — none of which an admin screen gives them.
/// </para>
/// <para>
/// Deliberately engine-free and I/O-free: it takes text and returns records. That is what lets the
/// awkward cases — a key that is not configured, a zoom range the wrong way round, an entry with
/// no URL — be tested as arithmetic rather than by standing up a database and a web server.
/// </para>
/// </remarks>
public static class MapLayerCatalog
{
    /// <summary>The placeholder an entry's URL uses where its configured key belongs.</summary>
    public const string ApiKeyPlaceholder = "{apikey}";

    /// <summary>Reads a catalogue document, returning what it declared and what was wrong with it.</summary>
    public static MapLayerCatalogResult Read(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            return new MapLayerCatalogResult([], [new MapLayerCatalogProblem($"not valid XML: {ex.Message}")]);
        }

        var layers = new List<MapLayerEntry>();
        var problems = new List<MapLayerCatalogProblem>();
        // Position in the file is the order in the panel. It is the only ordering an operator can
        // express by editing the thing they are already editing, and an explicit sort attribute
        // that has to be renumbered to insert a row in the middle is the kind of chore that ends
        // with everything at 100.
        var index = 0;

        foreach (var element in doc.Root?.Elements("layer") ?? [])
        {
            index += 10;
            var name = (string?)element.Attribute("name");
            var url = (string?)element.Attribute("url");

            if (string.IsNullOrWhiteSpace(name))
            {
                problems.Add(new MapLayerCatalogProblem($"entry {index / 10} has no name; skipped"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                problems.Add(new MapLayerCatalogProblem($"'{name}' has no url; skipped"));
                continue;
            }

            var apiKeyName = Trimmed(element.Attribute("apiKey"));
            // The two halves of a keyed source have to agree, and each half alone is a source that
            // silently does not work: a URL with a placeholder and no key named substitutes
            // nothing, and a key named with no placeholder to put it in resolves and is discarded.
            if (apiKeyName is not null && !url.Contains(ApiKeyPlaceholder, StringComparison.Ordinal))
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' names the access key '{apiKeyName}' but its url has no {ApiKeyPlaceholder} to put it in; skipped"));
                continue;
            }

            if (apiKeyName is null && url.Contains(ApiKeyPlaceholder, StringComparison.Ordinal))
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' has {ApiKeyPlaceholder} in its url but names no apiKey; skipped"));
                continue;
            }

            var minZoom = Int(element.Attribute("minZoom"), 0);
            var maxZoom = Int(element.Attribute("maxZoom"), 19);
            if (maxZoom < minZoom)
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' has maxZoom {maxZoom} below minZoom {minZoom}; skipped"));
                continue;
            }

            layers.Add(new MapLayerEntry(
                name.Trim(),
                url.Trim(),
                Trimmed(element.Attribute("attribution")),
                Trimmed(element.Attribute("group")),
                apiKeyName,
                Bool(element.Attribute("base"), true),
                Bool(element.Attribute("default"), false),
                Bool(element.Attribute("enabled"), true),
                minZoom,
                maxZoom,
                index));
        }

        // A name declared twice is one source the operator edited into the file twice, so the last
        // spelling wins — the same rule an editor applies to a repeated setting. Reported, because
        // the alternative reading is that they meant two sources and gave them one name by mistake,
        // and only they can tell the difference.
        var deduped = new Dictionary<string, MapLayerEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
        {
            if (deduped.ContainsKey(layer.Name))
            {
                problems.Add(new MapLayerCatalogProblem($"'{layer.Name}' is declared more than once; the last one wins"));
            }

            deduped[layer.Name] = layer;
        }

        return new MapLayerCatalogResult([.. deduped.Values], problems);
    }

    /// <summary>
    /// The URL to publish for an entry, or null when it names a key this installation has not set.
    /// </summary>
    /// <remarks>
    /// Null rather than the template: a tile URL still carrying <c>{apikey}</c> answers 401 or 404
    /// for every tile in the viewport, which on screen is a blank map — indistinguishable from a
    /// source that is down, and from a bad network. Withholding the layer at least leaves a list
    /// somebody can compare against the file.
    /// </remarks>
    public static string? ResolveUrl(MapLayerEntry entry, IReadOnlyDictionary<string, string> apiKeys)
    {
        if (entry.ApiKeyName is null)
        {
            return entry.UrlTemplate;
        }

        return apiKeys.TryGetValue(entry.ApiKeyName, out var key) && !string.IsNullOrWhiteSpace(key)
            ? entry.UrlTemplate.Replace(ApiKeyPlaceholder, key.Trim(), StringComparison.Ordinal)
            : null;
    }

    private static string? Trimmed(XAttribute? attribute)
    {
        var value = attribute?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool Bool(XAttribute? attribute, bool fallback) =>
        bool.TryParse(attribute?.Value, out var parsed) ? parsed : fallback;

    private static int Int(XAttribute? attribute, int fallback) =>
        int.TryParse(attribute?.Value, out var parsed) ? parsed : fallback;
}
