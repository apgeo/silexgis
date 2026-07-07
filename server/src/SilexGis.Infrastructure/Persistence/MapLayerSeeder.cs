// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Seeds the default layer catalog — attribution-compliant public
/// layers. Idempotent by name; never overwrites admin edits.
/// </summary>
public static class MapLayerSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        (string Name, string Url, string Attribution, bool IsDefault)[] baseLayers =
        [
            ("OpenStreetMap",
                "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                "© OpenStreetMap contributors",
                true),
            ("OpenTopoMap",
                "https://tile.opentopomap.org/{z}/{x}/{y}.png",
                "© OpenStreetMap contributors, SRTM | © OpenTopoMap (CC-BY-SA)",
                false),
            ("Esri World Imagery",
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                "Esri, Maxar, Earthstar Geographics, and the GIS User Community",
                false),
        ];

        var existing = await db.MapLayers.Select(x => x.Name).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var (name, url, attribution, isDefault) in baseLayers)
        {
            sort += 10;
            if (!existing.Contains(name))
            {
                db.MapLayers.Add(new MapLayer
                {
                    Name = name,
                    LayerKind = MapLayerKind.Xyz,
                    UrlTemplate = url,
                    Attribution = attribution,
                    IsBase = true,
                    IsDefault = isDefault,
                    SortOrder = sort,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
