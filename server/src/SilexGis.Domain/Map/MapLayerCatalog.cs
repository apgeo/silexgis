// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
/// How the source is drawn: tiles on a z/x/y template (<c>xyz</c>), a Web Map Service, a Web Map
/// Tile Service, a cloud-optimised GeoTIFF, or vector shapes drawn in a style.
/// </param>
/// <param name="Wms">The service options of a <c>wms</c> entry; null for every other kind.</param>
/// <param name="Wmts">The service options of a <c>wmts</c> entry; null for every other kind.</param>
/// <param name="Cog">The file options of a <c>cog</c> entry; null for every other kind.</param>
/// <param name="Vector">The data and style options of a <c>vector</c> entry; null for every other kind.</param>
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
    bool OnSharedLinks = true,
    MapLayerCogOptions? Cog = null,
    MapLayerVectorOptions? Vector = null);

/// <summary>Something wrong with a catalogue file, and what was done about it.</summary>
/// <param name="Code">Which kind of problem it is — one of <see cref="MapLayerCatalogCodes"/>, or a document rule's own code.</param>
/// <param name="Message">What an operator reads: the entry, what is wrong, and whether it was skipped or kept.</param>
public sealed record MapLayerCatalogProblem(string Code, string Message);

/// <summary>The result of reading a catalogue file: what it declared, and what was wrong with it.</summary>
/// <remarks>
/// Both, not one or the other. A file with one malformed entry among forty is a file whose other
/// thirty-nine are perfectly good, and refusing all of them over one typo means an installation
/// loses its whole basemap list to a missing quote. Equally, dropping the bad entry silently means
/// nobody ever finds out the source they added is not there. So the good ones are returned and the
/// problems are returned beside them, for the caller to log.
/// </remarks>
/// <param name="RefusedNames">
/// Names the file declares that it gives no usable entry for: every entry of that name was
/// skipped. A source stored from an earlier start under one of these names is still in the table,
/// so the seeder switches it off — otherwise an entry edited into a shape the reader refuses
/// would go on drawing as it was before the edit, with the report saying it had been skipped.
/// </param>
public sealed record MapLayerCatalogResult(
    IReadOnlyList<MapLayerEntry> Layers,
    IReadOnlyList<MapLayerCatalogProblem> Problems,
    IReadOnlyList<string> RefusedNames);

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

    /// <summary>The most bands a cloud-optimised GeoTIFF entry may draw: red, green, blue and transparency.</summary>
    public const int MaxCogBands = 4;

    /// <summary>The highest code a projection may name: EPSG codes are small positive integers.</summary>
    private const int MaxEpsgCode = 999_999;

    /// <summary>
    /// Reads a catalogue document, returning what it declared and what was wrong with it.
    /// </summary>
    /// <param name="xml">The file's text.</param>
    /// <param name="projectionDefinition">
    /// The PROJ.4 definition of an EPSG code, or null when there is none — the installation's own
    /// coordinate-system registry. Without one, a cloud-optimised GeoTIFF in anything but the two
    /// systems every view knows is refused, as one in an unknown system is.
    /// </param>
    public static MapLayerCatalogResult Read(string xml, Func<int, string?>? projectionDefinition = null)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            return new MapLayerCatalogResult(
                [], [new MapLayerCatalogProblem(MapLayerCatalogCodes.NotXml, $"not valid XML: {ex.Message}")], []);
        }

        var layers = new List<MapLayerEntry>();
        var problems = new List<MapLayerCatalogProblem>();
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                problems.Add(new MapLayerCatalogProblem(MapLayerCatalogCodes.NoName, $"entry {index / 10} has no name; skipped"));
                continue;
            }

            declaredNames.Add(name.Trim());

            if (string.IsNullOrWhiteSpace(url))
            {
                problems.Add(new MapLayerCatalogProblem(MapLayerCatalogCodes.NoUrl, $"'{name}' has no url; skipped"));
                continue;
            }

            var apiKeyName = Trimmed(element.Attribute("apiKey"));
            // The two halves of a keyed source have to agree, and each half alone is a source that
            // silently does not work: a URL with a placeholder and no key named substitutes
            // nothing, and a key named with no placeholder to put it in resolves and is discarded.
            if (apiKeyName is not null && !url.Contains(ApiKeyPlaceholder, StringComparison.Ordinal))
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.KeyWithoutPlaceholder,
                    $"'{name}' names the access key '{apiKeyName}' but its url has no {ApiKeyPlaceholder} to put it in; skipped"));
                continue;
            }

            if (apiKeyName is null && url.Contains(ApiKeyPlaceholder, StringComparison.Ordinal))
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.PlaceholderWithoutKey,
                    $"'{name}' has {ApiKeyPlaceholder} in its url but names no apiKey; skipped"));
                continue;
            }

            var isBase = Bool(element.Attribute("base"), true);

            // A kind that names nothing is refused here, where an operator reads the reason, rather
            // than accepted and then left off every map with nothing anywhere saying why.
            var kindWord = Trimmed(element.Attribute("kind"));
            var kind = MapLayerKinds.Parse(kindWord);
            if (kind is null)
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.UnknownKind,
                    $"'{name}' has kind '{kindWord}', which is not a kind of map layer (xyz, wms, wmts, cog or vector); skipped"));
                continue;
            }

            var wms = kind == MapLayerKind.Wms ? ReadWms(element, name, url, isBase, problems) : null;
            var wmts = kind == MapLayerKind.Wmts ? ReadWmts(element, name, url, problems) : null;
            var cog = kind == MapLayerKind.Cog ? ReadCog(element, name, url, projectionDefinition, problems) : null;
            var vector = kind == MapLayerKind.Vector ? ReadVector(element, name, url, problems) : null;
            if (kind != MapLayerKind.Xyz && wms is null && wmts is null && cog is null && vector is null)
            {
                // The kind's reader has already said why.
                continue;
            }

            var minZoom = Int(element.Attribute("minZoom"), 0);
            var maxZoom = Int(
                element.Attribute("maxZoom"),
                kind == MapLayerKind.Vector ? MapLayerKinds.VectorDefaultMaxZoom : 19);
            if (maxZoom < minZoom)
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.ZoomRangeInverted,
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
                    MapLayerDocumentRules.AttributionRequiredCode,
                    $"'{name}' is marked inDocuments but has no attribution to write under a picture; kept, without the mark"));
                inDocuments = false;
            }

            if (obstacle == MapLayerDocumentObstacle.NotABackground)
            {
                // A document's picture is drawn over one background. An overlay is not one.
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerDocumentRules.NotABackgroundCode,
                    $"'{name}' is marked inDocuments but is an overlay, and a document's picture takes one basemap; kept, without the mark"));
                inDocuments = false;
            }

            if (obstacle == MapLayerDocumentObstacle.NotPlainTiles)
            {
                // Whether a service's or a file's terms allow its pictures into a circulated file
                // is a question of its own, and the picture is drawn from z/x/y tiles only.
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerDocumentRules.NotPlainTilesCode,
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
                onSharedLinks,
                cog,
                vector));
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
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.DeclaredTwice, $"'{layer.Name}' is declared more than once; the last one wins"));
            }

            deduped[layer.Name] = layer;
        }

        CheckStandIns(deduped, problems);

        var refused = declaredNames.Where(n => !deduped.ContainsKey(n)).Order(StringComparer.OrdinalIgnoreCase);
        return new MapLayerCatalogResult([.. deduped.Values], problems, [.. refused]);
    }

    /// <summary>The number in a projection written <c>EPSG:</c> and its code, or null for anything else.</summary>
    public static int? EpsgCode(string? projection)
    {
        const string Prefix = "EPSG:";
        var text = projection?.Trim();
        if (text is null || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = text[Prefix.Length..];
        return digits.Length > 0
            && digits.All(char.IsAsciiDigit)
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            && code is > 0 and <= MaxEpsgCode
                ? code
                : null;
    }

    /// <summary>A projection's one spelling: <c>EPSG:</c> and its code.</summary>
    public static string ProjectionName(int code) => $"EPSG:{code.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Holds every vector entry's 3D stand-in to one the 3D scene can drape: another entry of this
    /// file, drawn as pictures, on the same side of the list. A stand-in that is not is dropped
    /// and the entry kept — the flat map draws it all the same — and one written in another case
    /// is spelt as the entry it names.
    /// </summary>
    private static void CheckStandIns(Dictionary<string, MapLayerEntry> entries, List<MapLayerCatalogProblem> problems)
    {
        foreach (var (key, entry) in entries.ToList())
        {
            if (entry.Vector?.Scene3dStandIn is not { } wanted)
            {
                continue;
            }

            var reason = !entries.TryGetValue(wanted, out var target)
                ? $"names '{wanted}' as its 3D stand-in, which this file does not declare or skips"
                : !MapLayerKinds.CanStandIn3d(target.Kind, target.IsBase, entry.IsBase)
                    ? target.Kind is MapLayerKind.Xyz or MapLayerKind.Wms or MapLayerKind.Wmts
                        ? $"names '{target.Name}' as its 3D stand-in, which is {(target.IsBase ? "a basemap" : "an overlay")} where this entry is {(entry.IsBase ? "a basemap" : "an overlay")}"
                        : $"names '{target.Name}' as its 3D stand-in, which is a {MapLayerKinds.NameOf(target.Kind)} layer; the 3D scene drapes xyz, wms and wmts sources in a vector layer's place"
                    : null;

            if (reason is not null)
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.StandInUnusable, $"'{entry.Name}' {reason}; kept, without a 3D stand-in"));
                entries[key] = entry with { Vector = entry.Vector with { Scene3dStandIn = null } };
            }
            else if (!string.Equals(target!.Name, wanted, StringComparison.Ordinal))
            {
                entries[key] = entry with { Vector = entry.Vector with { Scene3dStandIn = target.Name } };
            }
        }
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
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.WmsNoLayers, $"'{name}' is a wms layer with no layers attribute; skipped"));
            return null;
        }

        if (StrayPlaceholder(url, []) is { } stray)
        {
            // A WMS url is the service's address; the map adds the request itself, and a
            // placeholder beside the key's would be sent to the service as it stands.
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.StrayPlaceholder,
                $"'{name}' is a wms layer whose url holds {stray}; a wms url is the service address alone; skipped"));
            return null;
        }

        var version = Trimmed(element.Attribute("version")) ?? MapLayerKinds.SupportedWmsVersions[0];
        if (!MapLayerKinds.SupportedWmsVersions.Contains(version))
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.WmsVersionUnsupported,
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
                MapLayerCatalogCodes.WmtsIncomplete,
                $"'{name}' is a wmts layer and needs both a layer and a matrixSet attribute; skipped"));
            return null;
        }

        // A url naming the tile matrix is the service's REST template and must name the row and
        // the column too; one naming none of the three is its key-value endpoint.
        var rest = url.Contains("{TileMatrix}", StringComparison.Ordinal);
        if (rest && !(url.Contains("{TileRow}", StringComparison.Ordinal) && url.Contains("{TileCol}", StringComparison.Ordinal)))
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.WmtsTemplateIncomplete,
                $"'{name}' is a wmts template with {{TileMatrix}} but without both {{TileRow}} and {{TileCol}}; skipped"));
            return null;
        }

        // Every placeholder has to be one both maps fill. The flat map and the 3D view each fill a
        // REST template's tile coordinates, its matrix set, its style and its layer; anything else
        // is sent to the service as it stands, and a key-value address is filled by neither.
        if (StrayPlaceholder(url, rest ? WmtsRestPlaceholders : []) is { } stray)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.StrayPlaceholder,
                rest
                    ? $"'{name}' is a wmts template holding {stray}, which the map does not fill (it fills {string.Join(", ", WmtsRestPlaceholders)}); skipped"
                    : $"'{name}' is a wmts key-value address holding {stray}; a key-value address is the service address alone; skipped"));
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

    private static MapLayerCogOptions? ReadCog(
        XElement element,
        string name,
        string url,
        Func<int, string?>? projectionDefinition,
        List<MapLayerCatalogProblem> problems)
    {
        // A file's address is used as it stands; only the access key is put into it.
        if (StrayPlaceholder(url, []) is { } stray)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.StrayPlaceholder,
                $"'{name}' is a cog layer whose url holds {stray}; a cog url is the file's address alone; skipped"));
            return null;
        }

        // Named rather than read out of the file by each view: both views must place the picture
        // alike, and a system neither of them knows would put it in the wrong place silently.
        var projectionWord = Trimmed(element.Attribute("projection"));
        if (projectionWord is null)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.CogProjectionMissing,
                $"'{name}' is a cog layer with no projection attribute; name the file's coordinate system, e.g. projection=\"EPSG:3857\"; skipped"));
            return null;
        }

        if (EpsgCode(projectionWord) is not { } code)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.CogProjectionUnknown,
                $"'{name}' has projection '{projectionWord}'; a projection is written EPSG: and its code; skipped"));
            return null;
        }

        var projection = ProjectionName(code);
        string? definition = null;
        if (!MapLayerKinds.IsBuiltInProjection(projection))
        {
            definition = projectionDefinition?.Invoke(code)?.Trim();
            if (string.IsNullOrEmpty(definition))
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.CogProjectionUnknown,
                    $"'{name}' is a cog layer in {projection}, which this installation has no definition for; skipped"));
                return null;
            }
        }

        var bandsWord = Trimmed(element.Attribute("bands"));
        IReadOnlyList<int>? bands = null;
        if (bandsWord is not null)
        {
            bands = Bands(bandsWord);
            if (bands is null)
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.CogBandsInvalid,
                    $"'{name}' has bands '{bandsWord}'; bands are one to {MaxCogBands} band numbers counted from 1, comma-separated; skipped"));
                return null;
            }
        }

        if (!Number(element, "nodata", name, problems, out var nodata)
            || !Number(element, "min", name, problems, out var min)
            || !Number(element, "max", name, problems, out var max))
        {
            return null;
        }

        // One end of the range without the other would be scaled against a guess, which for a
        // 16-bit file is a black or a white picture.
        if ((min is null) != (max is null))
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.CogRangeInvalid,
                $"'{name}' names only one end of its value range; min and max go together; skipped"));
            return null;
        }

        if (min >= max)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.CogRangeInvalid,
                $"'{name}' has min {min.Value.ToString(CultureInfo.InvariantCulture)} not below max {max!.Value.ToString(CultureInfo.InvariantCulture)}; skipped"));
            return null;
        }

        return new MapLayerCogOptions(bands, nodata, min, max, projection, definition);
    }

    private static MapLayerVectorOptions? ReadVector(
        XElement element, string name, string url, List<MapLayerCatalogProblem> problems)
    {
        var formatWord = Trimmed(element.Attribute("format"));
        if (MapLayerKinds.ParseVectorFormat(formatWord) is not { } format)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.VectorFormatInvalid,
                formatWord is null
                    ? $"'{name}' is a vector layer with no format attribute (mvt, pmtiles or geojson); skipped"
                    : $"'{name}' has vector format '{formatWord}', which is none of mvt, pmtiles and geojson; skipped"));
            return null;
        }

        // A tile template names all three of the tile's coordinates; an mvt address naming none of
        // them is the service's TileJSON, which says where the tiles are. One naming some would ask
        // every tile at the same address.
        IReadOnlyList<string> filled = [];
        if (format == MapLayerVectorFormat.Mvt)
        {
            var named = TileCoordinates.Count(t => url.Contains(t, StringComparison.Ordinal));
            if (named is > 0 and < 3)
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.VectorTemplateIncomplete,
                    $"'{name}' is a vector tile template naming only some of {{z}}, {{x}} and {{y}}; a template names all three, and an address naming none is the service's TileJSON; skipped"));
                return null;
            }

            filled = named == 3 ? TileCoordinates : [];
        }

        if (StrayPlaceholder(url, filled) is { } stray)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.StrayPlaceholder,
                filled.Count > 0
                    ? $"'{name}' is a vector tile template holding {stray}, which the map does not fill (it fills {{z}}, {{x}} and {{y}}); skipped"
                    : $"'{name}' is a {MapLayerKinds.NameOf(format)} vector layer whose url holds {stray}; that url is the address alone; skipped"));
            return null;
        }

        var style = Trimmed(element.Attribute("style"));
        var styleSource = Trimmed(element.Attribute("styleSource"));
        var labelField = Trimmed(element.Attribute("labelField"));
        if (style is null && (styleSource is not null || labelField is not null))
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.VectorStyleRequired,
                $"'{name}' names a {(styleSource is not null ? "styleSource" : "labelField")} but no style for it to belong to; skipped"));
            return null;
        }

        // A style's address is fetched as it stands: no key is put into it, and nothing else is.
        if (style is not null && StrayPlaceholder(style, [], keyAllowed: false) is { } styleStray)
        {
            problems.Add(new MapLayerCatalogProblem(
                MapLayerCatalogCodes.StrayPlaceholder,
                $"'{name}' has a style address holding {styleStray}; a style address is used as it stands; skipped"));
            return null;
        }

        if (labelField is not null)
        {
            var property = labelField.Replace(MapLayerKinds.LanguagePlaceholder, string.Empty, StringComparison.Ordinal);
            if (property.Length == 0 || property.Contains('{', StringComparison.Ordinal) || property.Contains('}', StringComparison.Ordinal)
                || property.Any(char.IsWhiteSpace))
            {
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.VectorLabelFieldInvalid,
                    $"'{name}' has labelField '{labelField}'; it names one property of the shapes, with {MapLayerKinds.LanguagePlaceholder} where the reader's language goes, e.g. name:{MapLayerKinds.LanguagePlaceholder}; skipped"));
                return null;
            }
        }

        return new MapLayerVectorOptions(format, style, styleSource, labelField, Trimmed(element.Attribute("scene3d")));
    }

    private static readonly string[] TileCoordinates = ["{z}", "{x}", "{y}"];

    /// <summary>The band numbers a list names, or null when it is not one to four numbers counted from 1.</summary>
    private static int[]? Bands(string word)
    {
        var parts = word.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > MaxCogBands)
        {
            return null;
        }

        var bands = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bands[i]) || bands[i] < 1)
            {
                return null;
            }
        }

        return bands;
    }

    /// <summary>
    /// Reads an optional number attribute into <paramref name="value"/>; false, with the problem
    /// said, when it is there and not a finite number.
    /// </summary>
    private static bool Number(
        XElement element, string attribute, string name, List<MapLayerCatalogProblem> problems, out double? value)
    {
        value = null;
        var word = Trimmed(element.Attribute(attribute));
        if (word is null)
        {
            return true;
        }

        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        problems.Add(new MapLayerCatalogProblem(
            MapLayerCatalogCodes.CogNumberInvalid,
            $"'{name}' has {attribute} '{word}', which is not a number; skipped"));
        return false;
    }

    /// <summary>
    /// The placeholders both the flat map and the 3D view fill in a WMTS REST template. The tile
    /// coordinates must be spelt as here; the 3D engine fills the matrix set in either case and the
    /// style and layer in the spellings listed, which the flat map also fills.
    /// </summary>
    private static readonly string[] WmtsRestPlaceholders =
        ["{TileMatrix}", "{TileRow}", "{TileCol}", "{TileMatrixSet}", "{Style}", "{style}", "{layer}"];

    /// <summary>
    /// The first placeholder in a url that is not one of <paramref name="allowed"/> — nor the access
    /// key's, where a key may be put into it — or null.
    /// </summary>
    private static string? StrayPlaceholder(string url, IReadOnlyList<string> allowed, bool keyAllowed = true)
    {
        var rest = keyAllowed ? url.Replace(ApiKeyPlaceholder, string.Empty, StringComparison.Ordinal) : url;
        foreach (var placeholder in allowed)
        {
            rest = rest.Replace(placeholder, string.Empty, StringComparison.Ordinal);
        }

        var open = rest.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var close = rest.IndexOf('}', open);
        return close < 0 ? rest[open..] : rest[open..(close + 1)];
    }

    private static string? Crs(XElement element, string name, string kindName, List<MapLayerCatalogProblem> problems)
    {
        var crs = Trimmed(element.Attribute("crs"))?.ToUpperInvariant() ?? MapLayerKinds.WebMercator;
        if (MapLayerKinds.SupportedCrs.Contains(crs))
        {
            return crs;
        }

        problems.Add(new MapLayerCatalogProblem(
            MapLayerCatalogCodes.CrsUnsupported,
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
