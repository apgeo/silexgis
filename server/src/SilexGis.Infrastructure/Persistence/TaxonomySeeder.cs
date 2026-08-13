// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Trips;

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

        await SeedTripTypesAsync(db, ct);
        await SeedTripParticipantRolesAsync(db, ct);
        await SeedLinkKindsAsync(db, ct);
        await SeedResLinkRelationTypesAsync(db, ct);
        await SeedFeatureTypesAsync(db, ct);
        await SeedDocumentTypesAsync(db, ct);

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

    // The purposes a trip may be recorded under. The rows come from the shared seed list so the
    // admin surface refusing to re-code or delete a shipped row and this insert can never
    // disagree about which codes those are.
    private static async Task SeedTripTypesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var existing = await db.TripTypes.ToDictionaryAsync(x => x.Code, ct);
        var sort = 0;
        foreach (var seed in Domain.Trips.TripTypeSeeds.All)
        {
            sort += 10;
            var sections = TripTypeStarterSchemas.For(seed.Code);
            if (existing.TryGetValue(seed.Code, out var row))
            {
                foreach (var section in TripType.Sections)
                {
                    // Backfill a shipped schema only onto a section nobody has edited. A null
                    // schema is not proof that none was ever set: emptying the schema box is how
                    // an administrator says this section has none, and writing the shipped text
                    // back over that on the next restart would silently undo their change — and
                    // then publish the shipped text as the very version that was meant to mean
                    // "no schema". Every edit moves the version, so the version is what tells
                    // the two nulls apart.
                    if (row.SchemaOf(section) is null
                        && row.SchemaVersionOf(section) == TripType.FirstSchemaVersion)
                    {
                        row.SetInitialSchema(section, sections.Of(section));
                    }
                }
            }
            else
            {
                var added = new TripType { Code = seed.Code, Name = seed.Name, SortOrder = sort };
                foreach (var section in TripType.Sections)
                {
                    added.SetInitialSchema(section, sections.Of(section));
                }

                db.TripTypes.Add(added);
            }
        }

        // Identities are assigned by the database, so the history rows that reference them
        // cannot be built in the same pass.
        await db.SaveChangesAsync(ct);
        await PublishUnpublishedTripSchemasAsync(db, ct);
    }

    /// <summary>
    /// Makes sure every trip purpose's current section schemas exist in the schema history. A
    /// trip stamps the version it was validated against and is re-checked against that
    /// version's text, so a current schema missing from the history would leave those trips
    /// measured against a schema nobody can produce.
    /// </summary>
    private static async Task PublishUnpublishedTripSchemasAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var types = await db.TripTypes.AsNoTracking().ToListAsync(ct);
        if (types.Count == 0)
        {
            return;
        }

        var typeIds = types.Select(t => t.Id).ToList();
        var published = await db.TripTypeSchemas.AsNoTracking()
            .Where(s => typeIds.Contains(s.TripTypeId))
            .Select(s => new { s.TripTypeId, s.Section, s.Version })
            .ToListAsync(ct);
        var known = published.Select(p => (p.TripTypeId, p.Section, p.Version)).ToHashSet();

        foreach (var type in types)
        {
            foreach (var section in TripType.Sections)
            {
                if (type.SchemaOf(section) is not { } schema)
                {
                    continue;
                }

                if (known.Add((type.Id, section, type.SchemaVersionOf(section))))
                {
                    db.TripTypeSchemas.Add(new TripTypeSchema
                    {
                        TripTypeId = type.Id,
                        Section = section,
                        Version = type.SchemaVersionOf(section),
                        Schema = schema,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Locating is security-bearing (a locating link to a protected feature is redacted).
    // Everything defaults to locating = true; only kinds that provably carry no positional
    // information may ever be relaxed, by an administrator, audited.
    private static async Task SeedLinkKindsAsync(SilexGisDbContext db, CancellationToken ct)
    {
        (string Code, string Name, bool Locating)[] items =
        [
            ("associated_cave", "Associated cave", true),
            ("hydro_connection", "Hydrological connection", true),
            ("same_system", "Same cave system", true),
            ("related", "Related feature", true),
        ];

        var existing = await db.LinkKinds.Select(x => x.Code).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var (code, name, locating) in items)
        {
            sort += 10;
            if (!existing.Contains(code))
            {
                db.LinkKinds.Add(new LinkKind { Code = code, Name = name, Locating = locating, SortOrder = sort });
            }
        }
    }

    // What somebody did on a trip. The rows come from the shared seed list so the admin surface
    // refusing to re-code or delete a shipped row and this insert can never disagree about which
    // codes those are — and two of them are load-bearing rather than decorative: a roster with no
    // "participant" row could not record attendance at all, and "proposer" is what the right to
    // edit a proposed trip is about to be decided by.
    private static async Task SeedTripParticipantRolesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var existing = await db.TripParticipantRoles.Select(x => x.Code).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var seed in Domain.Trips.TripParticipantRoleSeeds.All)
        {
            sort += 10;
            if (!existing.Contains(seed.Code))
            {
                db.TripParticipantRoles.Add(new TripParticipantRole
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    SortOrder = sort,
                });
            }
        }
    }

    // Directed is semantics-bearing: a directed relation requires exactly one main member
    // once a link has two or more, an undirected one forbids the marker. The rows come
    // from the shared seed list so the admin surface refusing to touch seeded codes and
    // this insert can never disagree about which codes those are.
    private static async Task SeedResLinkRelationTypesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var existing = await db.ResLinkRelationTypes.Select(x => x.Code).ToHashSetAsync(ct);
        var sort = 0;
        foreach (var seed in Domain.ResLinks.ResLinkRelationTypeSeeds.All)
        {
            sort += 10;
            if (!existing.Contains(seed.Code))
            {
                db.ResLinkRelationTypes.Add(new ResLinkRelationType
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    Directed = seed.Directed,
                    InverseName = seed.InverseName,
                    SortOrder = sort,
                });
            }
        }
    }

    /// <summary>
    /// The document kinds a club archive starts with, and the metadata schemas they carry.
    /// Same mechanism as feature kinds: a JSON schema over a jsonb bag, versioned, with the
    /// document stamping the version it validated against.
    /// </summary>
    private static async Task SeedDocumentTypesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        const string surveyReportSchema =
            """
            {"type":"object","properties":{
              "cave_name":{"type":"string","title":"Cave"},
              "surveyed_length_m":{"type":"number","title":"Surveyed length (m)","minimum":0},
              "grade":{"type":"string","title":"Survey grade","enum":["1","2","3","4","5","6","X"]}
            }}
            """;
        const string tripReportSchema =
            """
            {"type":"object","properties":{
              "participants":{"type":"integer","title":"Participants","minimum":1},
              "duration_hours":{"type":"number","title":"Duration (h)","minimum":0},
              "objective_reached":{"type":"boolean","title":"Objective reached"}
            }}
            """;
        const string permitSchema =
            """
            {"type":"object","properties":{
              "authority":{"type":"string","title":"Issuing authority"},
              "reference":{"type":"string","title":"Reference number"}
            }}
            """;

        (string Code, string Name, string? Schema)[] items =
        [
            ("survey_report", "Survey report", surveyReportSchema),
            ("trip_report", "Trip report", tripReportSchema),
            ("map", "Map / plan", null),
            ("photo", "Photograph", null),
            ("permit", "Permit / authorization", permitSchema),
            ("article", "Article / publication", null),
            ("correspondence", "Correspondence", null),
            ("other", "Other document", null),
        ];

        var existing = await db.DocumentTypes.ToDictionaryAsync(x => x.Code, ct);
        var sort = 0;
        foreach (var (code, name, schema) in items)
        {
            sort += 10;
            if (existing.TryGetValue(code, out var row))
            {
                // Backfill a shipped schema only onto a kind nobody has edited. A null schema
                // is not proof that none was ever set: emptying the schema box is how an
                // administrator says this kind has none, and writing the shipped text back
                // over that on the next restart would silently undo their change — and then
                // publish the shipped text as the very schema version that was meant to mean
                // "no schema". Every edit moves the version, so the version is what tells the
                // two nulls apart.
                if (row.MetadataSchema is null && row.MetadataSchemaVersion == DocumentType.FirstSchemaVersion)
                {
                    row.MetadataSchema = schema;
                }
            }
            else
            {
                db.DocumentTypes.Add(new DocumentType
                {
                    Code = code,
                    Name = name,
                    SortOrder = sort,
                    MetadataSchema = schema,
                });
            }
        }

        // Identities are assigned by the database, so the history rows that reference them
        // cannot be built in the same pass.
        await db.SaveChangesAsync(ct);
        await PublishUnpublishedSchemasAsync(db, ct);
    }

    /// <summary>
    /// Makes sure every document type's current schema exists in the schema history. A
    /// document stamps the version it was validated against and is re-checked against that
    /// version's text, so a current schema missing from the history would leave those
    /// documents measured against a schema nobody can produce.
    /// </summary>
    private static async Task PublishUnpublishedSchemasAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var types = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.MetadataSchema != null)
            .Select(t => new { t.Id, t.MetadataSchema, t.MetadataSchemaVersion })
            .ToListAsync(ct);
        if (types.Count == 0)
        {
            return;
        }

        var typeIds = types.Select(t => t.Id).ToList();
        var published = await db.DocumentTypeSchemas.AsNoTracking()
            .Where(s => typeIds.Contains(s.DocumentTypeId))
            .Select(s => new { s.DocumentTypeId, s.Version })
            .ToListAsync(ct);
        var known = published.Select(p => (p.DocumentTypeId, p.Version)).ToHashSet();

        foreach (var type in types)
        {
            if (known.Add((type.Id, type.MetadataSchemaVersion)))
            {
                db.DocumentTypeSchemas.Add(new DocumentTypeSchema
                {
                    DocumentTypeId = type.Id,
                    Version = type.MetadataSchemaVersion,
                    Schema = type.MetadataSchema!,
                });
            }
        }
    }

    private static async Task SeedFeatureTypesAsync(SilexGisDbContext db, CancellationToken ct)
    {
        // Typed-properties JSON schemas (minimal subset: object with string/number/
        // integer/boolean/enum properties). The client renders these as extra form
        // fields; the values live in features.properties (jsonb).
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

        // A continuation is an open way on: the thing an exploring club chases across years, and
        // the reason a trip's account of it is not enough on its own.
        //
        // The state belongs here, on the place, and not on the trip that named it. A continuation
        // outlives the trip that found it and is answered by a different trip, often years later;
        // held per trip, "is this one still open" would have as many answers as visits and no way
        // to tell which is current. A trip's link to it records what that trip saw — the state
        // records what is true now.
        //
        // The grade is how promising it looked, on the scale exploring clubs already keep their
        // question marks by, so a season can be planned from the register rather than from memory.
        const string continuationSchema =
            """
            {"type":"object","properties":{
              "state":{"type":"string","title":"State","enum":["open","checked","dead-end","continues"]},
              "grade":{"type":"string","title":"Promise","enum":["A","B","C","D"]},
              "note":{"type":"string","title":"What is left to do"}
            }}
            """;

        // Accepted geometry classes follow the legacy loose semantics: a kind accepts its
        // class plus the matching Multi* (imported multi-part features round-trip).
        GeometryClass[] point = [GeometryClass.Point, GeometryClass.MultiPoint];
        GeometryClass[] line = [GeometryClass.LineString, GeometryClass.MultiLineString];
        GeometryClass[] area = [GeometryClass.Polygon, GeometryClass.MultiPolygon];
        GeometryClass[] any =
        [
            GeometryClass.Point, GeometryClass.LineString, GeometryClass.Polygon,
            GeometryClass.MultiPoint, GeometryClass.MultiLineString, GeometryClass.MultiPolygon,
        ];

        // Symbol files reference the bundled legacy symbol set. Categories drive map-layer
        // separation and index partitioning; RequiresParent marks kinds meaningless outside
        // a containing feature (in-cave palette).
        (string Code, string Name, FeatureCategory Category, GeometryClass[] Classes,
            bool RequiresParent, string? Symbol, string? Schema)[] items =
        [
            // Surface palette (v1/v2 heritage)
            ("sinkhole", "Sinkhole / Doline", FeatureCategory.Surface, point, false, "sinkhole.png", sinkholeSchema),
            ("pit", "Pit", FeatureCategory.Surface, point, false, "pit.png", null),
            ("pitch", "Pitch", FeatureCategory.Surface, point, false, "pitch.png", null),
            ("chimney", "Chimney", FeatureCategory.Surface, point, false, "chimney.png", null),
            ("tunnel", "Tunnel", FeatureCategory.Surface, point, false, "tunnel.png", null),
            ("lake", "Lake / Pond", FeatureCategory.Surface, any, false, "lake.png", null),
            ("water_flow", "Spring / Water flow", FeatureCategory.Surface, point, false, "water_flow.png", waterFlowSchema),
            ("fracture_line", "Fracture line / Fault", FeatureCategory.Surface, line, false, "fracture_line.png", null),
            ("peak", "Peak", FeatureCategory.Surface, point, false, "peak.png", null),
            ("wall", "Wall / Crag", FeatureCategory.Surface, line, false, "fracture_line.png", null),
            ("bivouac", "Bivouac", FeatureCategory.Surface, point, false, "bivouac.png", null),
            ("exploration_point", "Exploration point", FeatureCategory.Surface, point, false, "exploration_point.png", null),
            ("desobstruction", "Desobstruction", FeatureCategory.Surface, point, false, "desobstruction.png", null),
            ("continuation", "Continuation", FeatureCategory.Surface, point, false, "continuation.png", continuationSchema),
            ("calm", "Calm", FeatureCategory.Surface, point, false, "calm.png", null),
            ("detritus", "Detritus", FeatureCategory.Surface, point, false, "dedritus.png", null),
            ("driller", "Drilling point", FeatureCategory.Surface, point, false, "driller.png", null),
            ("flag", "Flag / Marker", FeatureCategory.Surface, point, false, "flag.png", null),
            ("generic", "Generic feature", FeatureCategory.Surface, any, false, "generic_feature.png", null),
            ("arrow", "Arrow / Direction", FeatureCategory.Surface, line, false, "arrows.png", null),

            // Underground palette (inside a cave — parent required)
            ("stalactite", "Stalactite / Speleothem", FeatureCategory.Underground, point, true, "generic_feature.png", null),
            ("calcite_dome", "Calcite dome", FeatureCategory.Underground, point, true, "generic_feature.png", null),
            ("cave_sector", "Cave sector", FeatureCategory.Underground, area, true, "generic_feature.png", null),

            // Areas & groupings
            ("karst_area", "Karst area", FeatureCategory.Area, area, false, null, null),
            ("massif", "Massif / Mountain", FeatureCategory.Area, area, false, null, null),
            ("cave_system", "Cave system", FeatureCategory.Area, area, false, null, null),

            // Structures
            ("building", "Building", FeatureCategory.Structure, [.. point, .. area], false, null, null),
        ];

        var existing = await db.FeatureTypes.ToDictionaryAsync(x => x.Code, ct);
        var sort = 0;
        foreach (var (code, name, category, classes, requiresParent, symbol, schema) in items)
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
                    Category = category,
                    AcceptedGeometryClasses = classes,
                    RequiresParent = requiresParent,
                    SymbolFile = symbol,
                    SortOrder = sort,
                    PropertiesSchema = schema,
                });
            }
        }
    }
}
