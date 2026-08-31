// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// Where this installation's tile-source catalogue is, and the access keys the sources in it need.
/// </summary>
public sealed class MapLayerOptions
{
    public const string SectionName = "MapLayers";

    /// <summary>
    /// The catalogue file, absolute or relative to the application's content root. Empty means the
    /// one shipped in the image.
    /// </summary>
    /// <remarks>
    /// Worth overriding for exactly one reason: a catalogue kept outside the image survives an
    /// upgrade. Left at the default, an operator who adds their national mapping agency's tile
    /// server loses the edit the next time the image is rebuilt, and loses it silently — the map
    /// still works, it just quietly has one fewer source on it than it did yesterday.
    /// </remarks>
    public string? CatalogPath { get; set; }

    /// <summary>
    /// Access keys by the name a catalogue entry's <c>apiKey</c> attribute uses, e.g.
    /// <c>SILEXGIS__MapLayers__ApiKeys__Thunderforest</c>.
    /// </summary>
    /// <remarks>
    /// Here rather than in the catalogue file so that the file is a thing an operator can commit,
    /// paste into a support thread or share with the group next door without checking it for
    /// secrets first. A key does still reach the browser — it has to, since the browser is what
    /// fetches the tiles — but that is the provider's model, not a leak this introduces.
    /// </remarks>
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The catalogue file's full path, resolved against <paramref name="contentRoot"/>.</summary>
    public string ResolvedCatalogPath(string contentRoot) =>
        string.IsNullOrWhiteSpace(CatalogPath)
            ? Path.Combine(contentRoot, "map-layers.xml")
            : Path.IsPathRooted(CatalogPath)
                ? CatalogPath
                // Relative and combined rather than assumed to be relative to the process's working
                // directory, which under a container is not the content root and under `dotnet run`
                // is whichever directory somebody happened to be standing in.
                : Path.Combine(contentRoot, CatalogPath);
}
