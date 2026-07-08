// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Idempotent seed of the lookup taxonomies. Matches by Code — never
/// overwrites admin edits; inserts missing rows and backfills fields
/// that are still null (e.g. feature-type properties schemas).
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
        // Typed-properties JSON schemas (minimal subset: object with string/number/
        // integer/boolean/enum properties). The client renders these as extra form
        // fields; the values live in surface_features.properties (jsonb).
        const string sinkholeSchema =
            """
            {"type":"object","properties":{
              "depth_m":{"type":"number","title":"Depth (m)","minimum":0},
              "diameter_m":{"type":"number","title":"Diameter (m)","minimum":0}
            }}
            """;
        const string waterFlowSchema =
            """
            {"type":"object","properties":{
              "flow_kind":{"type":"string","title":"Flow kind","enum":["spring","sink","resurgence","intermittent"]},
              "temperature_c":{"type":"number","title":"Water temperature (°C)"}
            }}
            """;

        // Symbol files reference the bundled legacy symbol set.
        (string Code, string Name, GeometryKind Kind, string Symbol, string? Schema)[] items =
        [
            ("sinkhole", "Sinkhole / Doline", GeometryKind.Point, "sinkhole.png", sinkholeSchema),
            ("pit", "Pit", GeometryKind.Point, "pit.png", null),
            ("pitch", "Pitch", GeometryKind.Point, "pitch.png", null),
            ("chimney", "Chimney", GeometryKind.Point, "chimney.png", null),
            ("tunnel", "Tunnel", GeometryKind.Point, "tunnel.png", null),
            ("lake", "Lake / Pond", GeometryKind.Any, "lake.png", null),
            ("water_flow", "Spring / Water flow", GeometryKind.Point, "water_flow.png", waterFlowSchema),
            ("fracture_line", "Fracture line / Fault", GeometryKind.Line, "fracture_line.png", null),
            ("peak", "Peak", GeometryKind.Point, "peak.png", null),
            ("bivouac", "Bivouac", GeometryKind.Point, "bivouac.png", null),
            ("exploration_point", "Exploration point", GeometryKind.Point, "exploration_point.png", null),
            ("desobstruction", "Desobstruction", GeometryKind.Point, "desobstruction.png", null),
            ("continuation", "Continuation", GeometryKind.Point, "continuation.png", null),
            ("calm", "Calm", GeometryKind.Point, "calm.png", null),
            ("detritus", "Detritus", GeometryKind.Point, "dedritus.png", null),
            ("driller", "Drilling point", GeometryKind.Point, "driller.png", null),
            ("flag", "Flag / Marker", GeometryKind.Point, "flag.png", null),
            ("generic", "Generic feature", GeometryKind.Any, "generic_feature.png", null),
            ("arrow", "Arrow / Direction", GeometryKind.Line, "arrows.png", null),
        ];

        var existing = await db.FeatureTypes.ToDictionaryAsync(x => x.Code, ct);
        var sort = 0;
        foreach (var (code, name, kind, symbol, schema) in items)
        {
            sort += 10;
            if (existing.TryGetValue(code, out var row))
            {
                // Backfill a schema only where none was ever set — a non-null value
                // may be an admin edit and is never overwritten.
                row.PropertiesSchema ??= schema;
            }
            else
            {
                db.FeatureTypes.Add(new FeatureType
                {
                    Code = code,
                    Name = name,
                    GeometryKind = kind,
                    SymbolFile = symbol,
                    SortOrder = sort,
                    PropertiesSchema = schema,
                });
            }
        }
    }
}
