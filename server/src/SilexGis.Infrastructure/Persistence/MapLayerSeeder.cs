// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>What applying a catalogue changed, and what was wrong with it.</summary>
/// <remarks>
/// Returned rather than logged from in here, because the thing that knows how this installation
/// logs is the application, and a seeder that reaches for a logger is a seeder that cannot be
/// tested without one.
/// </remarks>
/// <param name="SwitchedOffAsRefused">
/// Stored sources switched off because the file still names them but now refuses every entry of
/// that name — an entry edited to a kind this version cannot draw, say.
/// </param>
public sealed record MapLayerSeedReport(
    int Added,
    int Updated,
    int WithheldForMissingKey,
    IReadOnlyList<MapLayerCatalogProblem> Problems,
    int SwitchedOffAsRefused = 0);

/// <summary>
/// Brings the stored layer catalogue into line with the tile sources this installation declares.
/// </summary>
/// <remarks>
/// <para>
/// Matched by name, and the file wins for the entries it names. A stored source the file does not
/// name is left exactly as it is — that is what lets an installation keep a source somebody added
/// by hand while still taking the shipped list's corrections.
/// </para>
/// <para>
/// One thing about a named entry the file does not win over, because the file does not declare
/// it: what an administrator of this installation decided about copying the source into
/// documents (<see cref="MapLayer.InDocumentsChoice"/>). The file's own answer is written to its
/// own column on every start; the decision beside it is never read or written here.
/// </para>
/// <para>
/// A name the file still declares but refuses every entry of is the file's word too: the stored
/// source of that name is switched off rather than left drawing as it was before the edit that
/// made it unreadable. It is not deleted — the row is what the operator compares against the
/// report line saying why — and correcting the entry and restarting switches it back on.
/// </para>
/// <para>
/// Idempotent, and it has to be: this runs on every start, so applying it twice must equal
/// applying it once, or a restart would renumber, re-default or duplicate the list.
/// </para>
/// <para>
/// A cloud-optimised GeoTIFF's coordinate system is resolved here, from the installation's own
/// registry, and its definition stored with the entry's options — so the views that place the
/// file are handed it with the layer, and a code the registry does not know refuses the entry on
/// start rather than misplacing the picture on screen.
/// </para>
/// </remarks>
public static class MapLayerSeeder
{
    public static async Task<MapLayerSeedReport> SeedAsync(
        string catalogXml,
        IReadOnlyDictionary<string, string> apiKeys,
        ICrsRegistry crs,
        SilexGisDbContext db,
        CancellationToken ct = default)
    {
        var catalog = MapLayerCatalog.Read(catalogXml, crs.Proj4);
        var problems = catalog.Problems.ToList();

        var stored = await db.MapLayers.ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase, ct);
        int added = 0, updated = 0, withheld = 0;

        foreach (var entry in catalog.Layers)
        {
            var url = MapLayerCatalog.ResolveUrl(entry, apiKeys);
            if (url is null)
            {
                // Declared, and not published. The row is still written so that the source shows up
                // as switched off rather than not existing: "the key is missing" and "somebody
                // deleted the entry" are different problems and look identical from an empty list.
                withheld++;
            }

            var enabled = entry.Enabled && url is not null;

            if (!stored.TryGetValue(entry.Name, out var row))
            {
                db.MapLayers.Add(Apply(new MapLayer { Name = entry.Name, UrlTemplate = string.Empty }, entry, url, enabled));
                added++;
                continue;
            }

            // Written back unconditionally rather than compared field by field: EF's change tracker
            // already decides whether anything actually differs, so an equality check here would be
            // a second, hand-maintained copy of that decision — and the copy is what goes stale
            // when a field is added.
            Apply(row, entry, url, enabled);
            if (db.Entry(row).State == EntityState.Modified)
            {
                updated++;
            }
        }

        var switchedOff = 0;
        foreach (var name in catalog.RefusedNames)
        {
            if (stored.TryGetValue(name, out var row) && row.Enabled)
            {
                row.Enabled = false;
                switchedOff++;
                problems.Add(new MapLayerCatalogProblem(
                    MapLayerCatalogCodes.SwitchedOffAsRefused,
                    $"'{row.Name}' was stored from an earlier start and is switched off until its entry can be read again"));
            }
        }

        await db.SaveChangesAsync(ct);
        return new MapLayerSeedReport(added, updated, withheld, problems, switchedOff);
    }

    private static MapLayer Apply(MapLayer row, MapLayerEntry entry, string? url, bool enabled)
    {
        // The template is kept when the key is missing, so that setting the key and restarting is
        // all it takes. Publishing is what the Enabled flag above withholds.
        row.UrlTemplate = url ?? entry.UrlTemplate;
        row.LayerKind = entry.Kind;
        // Kept when it holds the same options in the database's own spelling, so that a start
        // which changes nothing writes nothing.
        var options = MapLayerOptionsJson.Write(entry);
        if (!MapLayerOptionsJson.SameOptions(row.Options, options))
        {
            row.Options = options;
        }

        row.Attribution = entry.Attribution;
        row.GroupName = entry.GroupName;
        row.ApiKeyName = entry.ApiKeyName;
        row.IsBase = entry.IsBase;
        row.IsDefault = entry.IsDefault;
        row.MinZoom = entry.MinZoom;
        row.MaxZoom = entry.MaxZoom;
        row.SortOrder = entry.SortOrder;
        row.Enabled = enabled;
        row.InDocuments = entry.InDocuments;
        row.OnSharedLinks = entry.OnSharedLinks;
        return row;
    }
}
