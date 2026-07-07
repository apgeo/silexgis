// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Idempotent seed of the lookup taxonomies. Matches by Code — never
/// overwrites admin edits; only inserts missing rows.
/// </summary>
public static class TaxonomySeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        await SeedSetAsync(db.CaveTypes, static (c, n) => new CaveType { Code = c, Name = n }, ct,
            ("cave", "Cave"),
            ("pit", "Pit / Aven"),
            ("spring_cave", "Spring cave"),
            ("mine", "Mine / Artificial cavity"),
            ("rock_shelter", "Rock shelter"));

        await SeedSetAsync(db.EntranceTypes, static (c, n) => new EntranceType { Code = c, Name = n }, ct,
            ("natural", "Natural"),
            ("excavated", "Excavated / Dug open"),
            ("artificial", "Artificial / Adit"),
            ("collapsed", "Collapsed"));

        await SeedSetAsync(db.RockTypes, static (c, n) => new RockType { Code = c, Name = n }, ct,
            ("limestone", "Limestone"),
            ("dolomite", "Dolomite"),
            ("marble", "Marble"),
            ("gypsum", "Gypsum"),
            ("salt", "Salt"),
            ("conglomerate", "Conglomerate"),
            ("sandstone", "Sandstone"),
            ("volcanic", "Volcanic rock"),
            ("other", "Other"));

        await SeedFeatureTypesAsync(db, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSetAsync<T>(
        DbSet<T> set, Func<string, string, T> create, CancellationToken ct, params (string Code, string Name)[] items)
        where T : TaxonomyBase
    {
        var existing = await set.Select(x => x.Code).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var (code, name) in items)
        {
            sort += 10;
            if (!existing.Contains(code))
            {
                var entity = create(code, name);
                entity.SortOrder = sort;
                set.Add(entity);
            }
        }
    }

    private static async Task SeedFeatureTypesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        // Symbol files reference the bundled legacy symbol set.
        (string Code, string Name, GeometryKind Kind, string Symbol)[] items =
        [
            ("sinkhole", "Sinkhole / Doline", GeometryKind.Point, "sinkhole.png"),
            ("pit", "Pit", GeometryKind.Point, "pit.png"),
            ("pitch", "Pitch", GeometryKind.Point, "pitch.png"),
            ("chimney", "Chimney", GeometryKind.Point, "chimney.png"),
            ("tunnel", "Tunnel", GeometryKind.Point, "tunnel.png"),
            ("lake", "Lake / Pond", GeometryKind.Any, "lake.png"),
            ("water_flow", "Spring / Water flow", GeometryKind.Point, "water_flow.png"),
            ("fracture_line", "Fracture line / Fault", GeometryKind.Line, "fracture_line.png"),
            ("peak", "Peak", GeometryKind.Point, "peak.png"),
            ("bivouac", "Bivouac", GeometryKind.Point, "bivouac.png"),
            ("exploration_point", "Exploration point", GeometryKind.Point, "exploration_point.png"),
            ("desobstruction", "Desobstruction", GeometryKind.Point, "desobstruction.png"),
            ("continuation", "Continuation", GeometryKind.Point, "continuation.png"),
            ("calm", "Calm", GeometryKind.Point, "calm.png"),
            ("detritus", "Detritus", GeometryKind.Point, "dedritus.png"),
            ("driller", "Drilling point", GeometryKind.Point, "driller.png"),
            ("flag", "Flag / Marker", GeometryKind.Point, "flag.png"),
            ("generic", "Generic feature", GeometryKind.Any, "generic_feature.png"),
            ("arrow", "Arrow / Direction", GeometryKind.Line, "arrows.png"),
        ];

        var existing = await db.FeatureTypes.Select(x => x.Code).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var (code, name, kind, symbol) in items)
        {
            sort += 10;
            if (!existing.Contains(code))
            {
                db.FeatureTypes.Add(new FeatureType
                {
                    Code = code,
                    Name = name,
                    GeometryKind = kind,
                    SymbolFile = symbol,
                    SortOrder = sort,
                });
            }
        }
    }
}
