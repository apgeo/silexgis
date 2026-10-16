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
/// <param name="InDocuments">
/// Whether this source's tiles may be copied into a document the application produces. False
/// unless the entry says otherwise: showing a tile on a screen and putting it into a file that is
/// then circulated are different uses, most providers' terms treat them differently, and nothing
/// about a tile address says which kind of terms stand behind it. Only somebody who has read
/// them can.
/// </param>
/// <param name="Kind">
/// How the source is drawn: tiles on a z/x/y template (<c>xyz</c>), a Web Map Service, or a Web Map
/// Tile Service. Only kinds this version can draw are ever read into an entry.
/// </param>
/// <param name="Wms">The service options of a <c>wms</c> entry; null for every other kind.</param>
/// <param name="Wmts">The service options of a <c>wmts</c> entry; null for every other kind.</param>
/// <param name="OnSharedLinks">
/// Whether a shared view's link may draw this source for somebody with no account. True unless the
/// entry says otherwise or sits in <see cref="MapLayerCatalog.RestrictedTermsGroup"/>: a source
/// whose terms bind the installation's own users — or that was switched on for them by an override
/// — is not thereby offered to whoever is sent a link.
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
    int SortOrder,
    bool InDocuments = false,
    MapLayerKind Kind = MapLayerKind.Xyz,
    MapLayerWmsOptions? Wms = null,
    MapLayerWmtsOptions? Wmts = null,
    bool OnSharedLinks = true);

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

    /// <summary>
    /// The heading the shipped catalogue files sources under whose terms do not permit the way
    /// this application fetches them. An entry under it is kept off shared links unless it says
    /// <c>sharedLinks="true"</c> itself.
    /// </summary>
    public const string RestrictedTermsGroup = "Restricted terms of use";

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

            var isBase = Bool(element.Attribute("base"), true);

            // A kind this version cannot draw is refused here, where an operator reads the reason,
            // rather than accepted and then left off every map with nothing anywhere saying why.
            var kindWord = Trimmed(element.Attribute("kind"));
            var kind = MapLayerKinds.Parse(kindWord);
            if (kind is null)
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' has kind '{kindWord}', which is not a kind of map layer (xyz, wms or wmts); skipped"));
                continue;
            }

            if (!MapLayerKinds.IsDrawable(kind.Value))
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' has kind '{MapLayerKinds.NameOf(kind.Value)}', which this version cannot draw yet "
                    + "(it draws xyz, wms and wmts); not supported yet, skipped"));
                continue;
            }

            MapLayerWmsOptions? wms = null;
            MapLayerWmtsOptions? wmts = null;
            if (kind == MapLayerKind.Wms)
            {
                wms = ReadWms(element, name, url, isBase, problems);
                if (wms is null)
                {
                    continue;
                }
            }
            else if (kind == MapLayerKind.Wmts)
            {
                wmts = ReadWmts(element, name, url, problems);
                if (wmts is null)
                {
                    continue;
                }
            }

            var minZoom = Int(element.Attribute("minZoom"), 0);
            var maxZoom = Int(element.Attribute("maxZoom"), 19);
            if (maxZoom < minZoom)
            {
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' has maxZoom {maxZoom} below minZoom {minZoom}; skipped"));
                continue;
            }

            var attribution = Trimmed(element.Attribute("attribution"));
            var inDocuments = Bool(element.Attribute("inDocuments"), false);

            // The mark is taken off rather than the entry dropped: in both cases below the source
            // is still a perfectly good thing to look at, and only the claim that it may be put
            // into a file cannot be honoured. Said out loud, because the alternative reading of a
            // picture that comes out with no background is that the application is broken.
            var obstacle = inDocuments ? MapLayerDocumentRules.ObstacleTo(isBase, attribution, kind.Value) : null;
            if (obstacle == MapLayerDocumentObstacle.NoAttribution)
            {
                // Terms that allow a copy allow it with the credit beside the picture, and a
                // credit nobody wrote down cannot be put there.
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' is marked inDocuments but has no attribution to write under a picture; kept, without the mark"));
                inDocuments = false;
            }

            if (obstacle == MapLayerDocumentObstacle.NotABackground)
            {
                // A document's picture is drawn over one background. An overlay is not one.
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' is marked inDocuments but is an overlay, and a document's picture takes one basemap; kept, without the mark"));
                inDocuments = false;
            }

            if (obstacle == MapLayerDocumentObstacle.NotPlainTiles)
            {
                // Whether a service's terms allow its pictures into a circulated file is a
                // question of its own, and the picture is drawn from z/x/y tiles only.
                problems.Add(new MapLayerCatalogProblem(
                    $"'{name}' is marked inDocuments but is a {MapLayerKinds.NameOf(kind.Value)} layer, and a document's picture is drawn from xyz tiles only; kept, without the mark"));
                inDocuments = false;
            }

            var group = Trimmed(element.Attribute("group"));
            var onSharedLinks = Bool(
                element.Attribute("sharedLinks"),
                !string.Equals(group, RestrictedTermsGroup, StringComparison.OrdinalIgnoreCase));

            layers.Add(new MapLayerEntry(
                name.Trim(),
                url.Trim(),
                attribution,
                group,
                apiKeyName,
                isBase,
                Bool(element.Attribute("default"), false),
                Bool(element.Attribute("enabled"), true),
                minZoom,
                maxZoom,
                index,
                inDocuments,
                kind.Value,
                wms,
                wmts,
                onSharedLinks));
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

    private static MapLayerWmsOptions? ReadWms(
        XElement element, string name, string url, bool isBase, List<MapLayerCatalogProblem> problems)
    {
        var layers = Trimmed(element.Attribute("layers"));
        if (layers is null)
        {
            problems.Add(new MapLayerCatalogProblem($"'{name}' is a wms layer with no layers attribute; skipped"));
            return null;
        }

        if (url.Contains('{', StringComparison.Ordinal) && !url.Contains(ApiKeyPlaceholder, StringComparison.Ordinal))
        {
            // A WMS url is the service's address; the map adds the request itself.
            problems.Add(new MapLayerCatalogProblem(
                $"'{name}' is a wms layer whose url holds a {{placeholder}}; a wms url is the service address alone; skipped"));
            return null;
        }

        var version = Trimmed(element.Attribute("version")) ?? MapLayerKinds.SupportedWmsVersions[0];
        if (!MapLayerKinds.SupportedWmsVersions.Contains(version))
        {
            problems.Add(new MapLayerCatalogProblem(
                $"'{name}' asks for wms version {version}; the map speaks {string.Join(" and ", MapLayerKinds.SupportedWmsVersions)}; skipped"));
            return null;
        }

        var crs = Crs(element, name, "wms", problems);
        if (crs is null)
        {
            return null;
        }

        return new MapLayerWmsOptions(
            layers,
            Trimmed(element.Attribute("styles")) ?? string.Empty,
            Trimmed(element.Attribute("format")) ?? "image/png",
            version,
            // An overlay asks for a transparent picture unless told otherwise — an opaque one
            // would hide the basemap under it entirely.
            Bool(element.Attribute("transparent"), !isBase),
            crs);
    }

    private static MapLayerWmtsOptions? ReadWmts(
        XElement element, string name, string url, List<MapLayerCatalogProblem> problems)
    {
        var layer = Trimmed(element.Attribute("layer"));
        var matrixSet = Trimmed(element.Attribute("matrixSet"));
        if (layer is null || matrixSet is null)
        {
            problems.Add(new MapLayerCatalogProblem(
                $"'{name}' is a wmts layer and needs both a layer and a matrixSet attribute; skipped"));
            return null;
        }

        // A url naming the tile matrix is the service's REST template and must name the row and
        // the column too; one naming none of the three is its key-value endpoint.
        var rest = url.Contains("{TileMatrix}", StringComparison.Ordinal);
        if (rest && !(url.Contains("{TileRow}", StringComparison.Ordinal) && url.Contains("{TileCol}", StringComparison.Ordinal)))
        {
            problems.Add(new MapLayerCatalogProblem(
                $"'{name}' is a wmts template with {{TileMatrix}} but without both {{TileRow}} and {{TileCol}}; skipped"));
            return null;
        }

        var crs = Crs(element, name, "wmts", problems);
        if (crs is null)
        {
            return null;
        }

        return new MapLayerWmtsOptions(
            layer,
            matrixSet,
            Trimmed(element.Attribute("format")) ?? "image/png",
            Trimmed(element.Attribute("style")) ?? "default",
            crs,
            Trimmed(element.Attribute("matrixIdPrefix")) ?? string.Empty);
    }

    private static string? Crs(XElement element, string name, string kindName, List<MapLayerCatalogProblem> problems)
    {
        var crs = Trimmed(element.Attribute("crs"))?.ToUpperInvariant() ?? MapLayerKinds.WebMercator;
        if (MapLayerKinds.SupportedCrs.Contains(crs))
        {
            return crs;
        }

        problems.Add(new MapLayerCatalogProblem(
            $"'{name}' is a {kindName} layer in {crs}; the map draws {string.Join(" and ", MapLayerKinds.SupportedCrs)}; skipped"));
        return null;
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
