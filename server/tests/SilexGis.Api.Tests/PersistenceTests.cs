// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests : IDisposable
{
    private readonly SilexGisApiFactory factory;

    public PersistenceTests(PostgresFixture postgres) => factory = new SilexGisApiFactory(postgres.ConnectionString);

    [Fact]
    public async Task Postgis_extension_is_installed_and_answers_spatial_sql()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var version = await db.Database
            .SqlQuery<string>($"SELECT postgis_full_version() AS \"Value\"")
            .SingleAsync();
        version.ShouldContain("POSTGIS=");

        var srid = await db.Database
            .SqlQuery<int>($"SELECT ST_SRID(ST_SetSRID(ST_MakePoint(25.58, 45.65), 4326)) AS \"Value\"")
            .SingleAsync();
        srid.ShouldBe(4326);
    }

    [Fact]
    public async Task Taxonomies_are_seeded_idempotently()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        (await db.CaveTypes.AnyAsync(x => x.Code == "cave")).ShouldBeTrue();
        (await db.EntranceTypes.AnyAsync(x => x.Code == "natural")).ShouldBeTrue();
        (await db.RockTypes.AnyAsync(x => x.Code == "limestone")).ShouldBeTrue();
        (await db.FeatureTypes.CountAsync()).ShouldBeGreaterThanOrEqualTo(27);

        // Feature types drive the data-driven kinds: category, accepted geometry and whether
        // a kind may exist outside a containing feature.
        var area = await db.FeatureTypes.SingleAsync(x => x.Code == "karst_area");
        area.Category.ShouldBe(FeatureCategory.Area);
        area.AcceptedGeometryClasses.ShouldContain(GeometryClass.Polygon);
        area.RequiresParent.ShouldBeFalse();
        (await db.FeatureTypes.SingleAsync(x => x.Code == "cave_sector")).RequiresParent.ShouldBeTrue();

        // Typed-properties schemas ship (and backfill) for selected feature types.
        var sinkhole = await db.FeatureTypes.SingleAsync(x => x.Code == "sinkhole");
        sinkhole.PropertiesSchema.ShouldNotBeNull();
        sinkhole.PropertiesSchema.ShouldContain("depth_m");

        // A doline is drawn as a rim as often as it is dropped as a marker, and its area,
        // circularity and long axis are only measurable from the rim — so the kind carries
        // both, and it is the outline half that this pins, the marker half being the one it
        // shipped with.
        sinkhole.AcceptedGeometryClasses.ShouldContain(GeometryClass.Point);
        sinkhole.AcceptedGeometryClasses.ShouldContain(GeometryClass.Polygon);
        sinkhole.AcceptedGeometryClasses.ShouldContain(GeometryClass.MultiPolygon);

        // Link kinds are security-bearing: a locating link to a protected feature is redacted.
        var associatedCave = await db.LinkKinds.SingleAsync(x => x.Code == "associated_cave");
        associatedCave.Locating.ShouldBeTrue();

        // Resource-link relation vocabulary: the seeded codes are the exchange contract,
        // and Directed is semantics-bearing (it decides whether a link must carry a main
        // member or must not), so each seeded row's flag is pinned here.
        (string Code, bool Directed)[] relations =
        [
            ("same-object", false),
            ("related-to", false),
            ("contains", true),
            ("documents", true),
            ("derived-from", true),
            ("adjacent-to", false),
            ("duplicate-of", true),
            ("needs-clarification", false),

            // The trip roles. Every one is directed with the trip as the main member, which is
            // what makes a role-filtered field read one way from the trip and the other way
            // from what the trip named; an undirected role would be ambiguous the moment two
            // trips shared a link. Directedness also cannot be changed once any installation
            // has linked with the code, so it is pinned per row here rather than in bulk.
            ("trip-work-area", true),
            ("trip-objective", true),
            ("trip-visited", true),
            ("trip-surveyed", true),
            ("trip-discovered", true),
            ("trip-dug", true),
            ("trip-photographed", true),
            ("trip-searched-not-found", true),
            ("trip-lead", true),
            ("trip-follows-on-from", true),
        ];
        foreach (var (code, directed) in relations)
        {
            var row = await db.ResLinkRelationTypes.SingleAsync(x => x.Code == code);
            row.Directed.ShouldBe(directed, code);
            (row.InverseName is not null).ShouldBe(directed, code);
        }

        // Re-running the seeder must not duplicate rows.
        var before = await db.FeatureTypes.CountAsync();
        var linkKindsBefore = await db.LinkKinds.CountAsync();
        var relationTypesBefore = await db.ResLinkRelationTypes.CountAsync();

        // Sort order too, not only the count: the seeder derives it from a row's position in
        // the shipped list, so a second pass that moved one would mean the vocabulary's display
        // order depends on how many times the application has started.
        var relationOrderBefore = await db.ResLinkRelationTypes.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.SortOrder })
            .ToListAsync();

        await TaxonomySeeder.SeedAsync(db);
        (await db.FeatureTypes.CountAsync()).ShouldBe(before);
        (await db.LinkKinds.CountAsync()).ShouldBe(linkKindsBefore);
        (await db.ResLinkRelationTypes.CountAsync()).ShouldBe(relationTypesBefore);

        var relationOrderAfter = await db.ResLinkRelationTypes.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.SortOrder })
            .ToListAsync();
        relationOrderAfter.ShouldBe(relationOrderBefore);

        // The trip roles were appended, so they sort after every code that shipped before them
        // and their own order matches the list. Appending is the whole reason they do: the
        // seeder never re-sorts a row it already inserted, so a code added in the middle would
        // take one sort order on a fresh database and a different one on an existing install.
        var byCode = relationOrderAfter.ToDictionary(x => x.Code, x => x.SortOrder);
        var lastShippedBefore = byCode["needs-clarification"];
        var tripRoles = relations.Where(r => r.Code.StartsWith("trip-", StringComparison.Ordinal)).ToList();
        tripRoles.Count.ShouldBe(10);
        var expected = lastShippedBefore;
        foreach (var (code, _) in tripRoles)
        {
            expected += 10;
            byCode[code].ShouldBe(expected, code);
        }
    }

    [Fact]
    public async Task Seeding_backfills_a_schema_nobody_set_and_leaves_one_an_administrator_removed_alone()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var types = scope.ServiceProvider.GetRequiredService<DocumentTypeWriteService>();

        var permit = await db.DocumentTypes.SingleAsync(t => t.Code == "permit");
        permit.MetadataSchema.ShouldNotBeNull();
        var shipped = permit.MetadataSchema;

        // The administrator empties the schema box, which is how a kind is told it has none.
        // Going through the write service rather than writing the columns directly is the
        // point: this is the state a real edit leaves behind, version move included.
        await types.UpdateAsync(
            permit.Id,
            new DocumentTypeInput(permit.Code, permit.Name, permit.Description, permit.SortOrder, null));
        permit.MetadataSchema.ShouldBeNull();
        permit.MetadataSchemaVersion.ShouldBeGreaterThan(DocumentType.FirstSchemaVersion);

        // Restarting the API re-runs the seeder. The removal has to survive it — otherwise the
        // change is undone silently, and the shipped text is then published as the very
        // version that was meant to mean "this kind has no schema".
        await TaxonomySeeder.SeedAsync(db);

        var afterRestart = await db.DocumentTypes.AsNoTracking().SingleAsync(t => t.Id == permit.Id);
        afterRestart.MetadataSchema.ShouldBeNull();
        (await db.DocumentTypeSchemas.AsNoTracking()
            .AnyAsync(s => s.DocumentTypeId == permit.Id && s.Version == afterRestart.MetadataSchemaVersion))
            .ShouldBeFalse();

        // The positive twin: a kind whose schema was never set — no edit, so still on its
        // first version — is backfilled, which is what the backfill exists for. Putting the
        // row back the way it shipped is the same act, so the test also leaves nothing behind.
        permit.MetadataSchemaVersion = DocumentType.FirstSchemaVersion;
        await db.SaveChangesAsync();

        await TaxonomySeeder.SeedAsync(db);

        var restored = await db.DocumentTypes.AsNoTracking().SingleAsync(t => t.Id == permit.Id);
        restored.MetadataSchema.ShouldBe(shipped);
        restored.MetadataSchemaVersion.ShouldBe(DocumentType.FirstSchemaVersion);
    }

    [Fact]
    public async Task Insert_generates_uuid_v7_sets_timestamps_and_audits()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var cavingGroup = new CavingGroup { Name = $"Test CavingGroup {Guid.NewGuid():N}", Slug = $"test-{Guid.NewGuid():N}" };
        db.CavingGroups.Add(cavingGroup);
        await db.SaveChangesAsync();

        cavingGroup.Id.ShouldNotBe(Guid.Empty);
        cavingGroup.Id.ToString()[14].ShouldBe('7'); // uuid version nibble — must stay v7
        cavingGroup.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        cavingGroup.UpdatedAt.ShouldBe(cavingGroup.CreatedAt);

        var audit = await db.AuditEntries
            .Where(a => a.EntityType == nameof(CavingGroup) && a.EntityId == cavingGroup.Id.ToString())
            .ToListAsync();
        audit.ShouldContain(a => a.Action == AuditActions.Created);

        cavingGroup.Description = "updated";
        await db.SaveChangesAsync();
        cavingGroup.UpdatedAt.ShouldBeGreaterThan(cavingGroup.CreatedAt);

        var updateAudit = await db.AuditEntries
            .Where(a => a.EntityType == nameof(CavingGroup) && a.EntityId == cavingGroup.Id.ToString() && a.Action == AuditActions.Updated)
            .SingleAsync();
        updateAudit.Changes.ShouldNotBeNull();
        updateAudit.Changes.ShouldContain("Description");
    }

    [Fact]
    public async Task Feature_aggregate_persists_with_its_derived_state_and_one_audit_row()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"persist-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();

        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
        var feature = new Feature { Name = $"Persisted Cave {suffix}", OwnerUserId = userId };
        var cave = new Cave { CaveTypeId = caveTypeId, Region = "Bihor" };
        feature.Cave = cave;
        await writer.CreateCaveAsync(feature, cave, [], CancellationToken.None);
        await db.SaveChangesAsync();

        feature.Id.ToString()[14].ShouldBe('7'); // uuid version nibble — must stay v7
        feature.Kind.ShouldBe(FeatureKind.Cave);
        feature.Category.ShouldBe(FeatureCategory.Underground);
        cave.Id.ShouldBe(feature.Id); // shared primary key

        // Derived state the write service owns: the ancestor array is self-inclusive and the
        // closure carries the matching row, even for a root feature with no parents.
        var stored = await db.Features.AsNoTracking().SingleAsync(f => f.Id == feature.Id);
        stored.AncestorIds.ShouldBe(new[] { feature.Id });
        stored.IsProtectedEffective.ShouldBeFalse();
        stored.Properties.ShouldBe("{}");
        (await db.FeatureAncestors.AnyAsync(a => a.FeatureId == feature.Id && a.AncestorId == feature.Id))
            .ShouldBeTrue();

        // Supertype and subtype rows are edited together, so the aggregate audits as ONE row
        // under its kind-qualified type name.
        var key = feature.Id.ToString();
        var audit = await db.AuditEntries.Where(a => a.EntityId == key).ToListAsync();
        audit.Count.ShouldBe(1);
        var entry = audit[0];
        entry.Action.ShouldBe(AuditActions.Created);
        entry.EntityType.ShouldBe(FeatureAudit.TypeName(FeatureKind.Cave));
        entry.Changes.ShouldNotBeNull();
        entry.Changes.ShouldContain("Region"); // the subtype's columns are merged in
    }

    public void Dispose() => factory.Dispose();
}
