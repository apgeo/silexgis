// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Tests;

/// <summary>
/// The pins phones sent are carried from their own table onto place-pin links when a database is
/// updated, every row with its identifier, pixel, times and removal — the times are the revisions
/// the phones hold, so a phone that read its maps before the update writes against them after it.
/// <para>
/// On a database of its own, migrated to the step before, holding invented maps and pins written
/// the way that step stored them, then migrated on.
/// </para>
/// </summary>
public sealed class RasterPinsAsLinksMigrationTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Before = "20261028082415_StoredFilePixelOrientation";

    private readonly string adminConnectionString;
    private readonly List<string> databases = [];

    public RasterPinsAsLinksMigrationTests(PostgresFixture postgres) => adminConnectionString = postgres.ConnectionString;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        foreach (var name in databases)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Every_pin_becomes_a_link_with_its_identifier_pixel_times_and_removal()
    {
        var connectionString = await DatabaseAtAsync(Before);

        // Invented throughout: one account, a cave with two places, a map whose picture has two
        // revisions — the current one turned a quarter by its orientation code — and three pins:
        // one live, one removed, one of a place off the picture's edge.
        var owner = Guid.CreateVersion7();
        var cave = Guid.CreateVersion7();
        var placeA = Guid.CreateVersion7();
        var placeB = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        var oldFile = Guid.CreateVersion7();
        var original = Guid.CreateVersion7();
        var copy = Guid.CreateVersion7();
        var map = Guid.CreateVersion7();
        var removedMap = Guid.CreateVersion7();
        var removedDocument = Guid.CreateVersion7();
        var removedFile = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();
        // Written by SQL against the step's own columns, not through today's model: a later step
        // that adds a column to one of these tables would otherwise break the insert.
        var sql = new StringBuilder();
        sql.AppendLine(MigrationFixtureSql.User(owner, "migration-owner"));
        foreach (var (id, name) in new[] { (cave, "Pestera 1"), (placeA, "punct A"), (placeB, "punct B") })
        {
            var ancestors = id == cave ? "'{}'" : $"ARRAY['{cave}']::uuid[]";
            sql.AppendLine($"""
                INSERT INTO features (id, kind, category, name, owner_user_id, visibility, location_protected,
                    is_protected_effective, ancestor_ids, created_at, updated_at)
                VALUES ('{id}', {(short)FeatureKind.Cave}, 0, '{name}', '{owner}', 0, false, false, {ancestors}, now(), now());
                """);
        }

        Picture(sql, owner, document, (oldFile, 1, false, null), (original, 2, true, 6), (copy, 2, true, null));
        Picture(sql, owner, removedDocument, (removedFile, 1, true, null));
        sql.AppendLine($"""
            INSERT INTO raster_maps (id, cave_feature_id, title, map_type, sort_order, document_id, owner_user_id,
                created_at, updated_at, deleted_at)
            VALUES ('{map}', '{cave}', 'plan', 0, 0, '{document}', '{owner}', now(), now(), NULL),
                   ('{removedMap}', '{cave}', 'removed', 0, 0, '{removedDocument}', '{owner}', now(), now(), now());
            INSERT INTO import_batches (id, confirmed_by_user_id, source, mode, created_count, attached_count,
                skipped_count, created_at, updated_at)
            VALUES ('{batch}', '{owner}', {(short)ImportSource.DeviceSync}, 0, 0, 0, 0, now(), now());
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        var live = Guid.CreateVersion7();
        var removed = Guid.CreateVersion7();
        var offPicture = Guid.CreateVersion7();
        var onRemovedMap = Guid.CreateVersion7();
        var created = new DateTimeOffset(2026, 9, 30, 8, 1, 2, TimeSpan.Zero).AddTicks(1_234_560);
        var updated = new DateTimeOffset(2026, 10, 2, 9, 3, 4, TimeSpan.Zero).AddTicks(6_543_210);
        var gone = new DateTimeOffset(2026, 10, 3, 10, 5, 6, TimeSpan.Zero).AddTicks(1_111_110);
        await ExecuteAsync(connectionString, $"""
            INSERT INTO raster_pins (id, raster_map_id, place_feature_id, x, y, created_at, updated_at, deleted_at) VALUES
                ('{live}', '{map}', '{placeA}', 12, 3, {Stamp(created)}, {Stamp(updated)}, NULL),
                ('{removed}', '{map}', '{placeB}', 0, 0, {Stamp(created)}, {Stamp(gone)}, {Stamp(gone)}),
                ('{offPicture}', '{map}', '{placeB}', 400, 3, {Stamp(created)}, {Stamp(updated)}, NULL),
                ('{onRemovedMap}', '{removedMap}', '{placeA}', 1, 1, {Stamp(created)}, {Stamp(updated)}, NULL);
            -- The upload's line, written by SQL as that step stored it: a later step adds columns to
            -- this table, which the model of today would write and the database of then lacks.
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids, raster_pin_id)
                VALUES ('{batch}', {(short)ImportDecisionAction.Create}, '{"{}"}', '[]', '{live}');
            """);

        await using (var db = Context(connectionString))
        {
            await db.GetService<IMigrator>().MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            await new PlacePointStore(db).RefreshFramesAsync(null);
        }

        await using (var db = Context(connectionString))
        {
            // The relation was made by the migration, at the place the seeder would give it.
            var relation = await db.ResLinkRelationTypes.AsNoTracking().SingleAsync(r => r.Code == ResLinkRelationTypeSeeds.MapPlacePointCode);
            var index = ResLinkRelationTypeSeeds.All.Select(s => s.Code).ToList().IndexOf(ResLinkRelationTypeSeeds.MapPlacePointCode);
            relation.SortOrder.ShouldBe(ShippedVocabularyOrder.Of(index));
            relation.Directed.ShouldBeTrue();

            var links = await db.ResLinks.AsNoTracking().Where(l => l.RelationTypeId == relation.Id).ToListAsync();
            links.Select(l => l.Id).OrderBy(id => id).ShouldBe(new[] { live, removed, offPicture, onRemovedMap }.OrderBy(id => id));
            links.Select(l => l.ShortCode).Distinct().Count().ShouldBe(4);
            links.ShouldAllBe(l => ResLinkRules.IsShortCode(l.ShortCode) && l.CreatedBy == owner);

            var byId = links.ToDictionary(l => l.Id);
            byId[live].CreatedAt.ShouldBe(created);
            byId[live].UpdatedAt.ShouldBe(updated);
            byId[live].DeletedAt.ShouldBeNull();
            byId[removed].UpdatedAt.ShouldBe(gone);
            byId[removed].DeletedAt.ShouldBe(gone);

            var members = await db.ResLinkMembers.AsNoTracking().Where(m => byId.Keys.Contains(m.ResLinkId)).ToListAsync();
            members.Count.ShouldBe(8);
            foreach (var group in members.GroupBy(m => m.ResLinkId))
            {
                var picture = group.Single(m => m.IsMain);
                picture.EntityType.ShouldBe(AttachedEntityType.Document);
                picture.AnchorKind.ShouldBe(AnchorKind.ImageRegion);
                var place = group.Single(m => !m.IsMain);
                place.FeatureId.ShouldNotBeNull();
                place.AnchorKind.ShouldBe(AnchorKind.Whole);
            }

            // Measured on the original file of the current revision — not the earlier revision's, not
            // the converted copy beside it.
            var livePicture = members.Single(m => m.ResLinkId == live && m.IsMain);
            livePicture.EntityId.ShouldBe(document);
            livePicture.AnchorFileId.ShouldBe(original);

            // The pixel is the one stored, and the fraction is the one conversion's, for the code the
            // file carries: a quarter turn, on a 40 × 30 picture.
            var frame = PictureFrame.Of(40, 30, 6, 0);
            var anchor = PlacePointAnchor.Read(livePicture.Anchor)!.Value;
            (anchor.Px, anchor.Py).ShouldBe((12, 3));
            var expected = PlacePointFrame.ToFraction(frame, 12, 3)!.Value;
            anchor.X!.Value.ShouldBe(expected.X);
            anchor.Y!.Value.ShouldBe(expected.Y);

            // Off the picture: the pixel is kept, and nothing says where to draw it.
            var off = PlacePointAnchor.Read(members.Single(m => m.ResLinkId == offPicture && m.IsMain).Anchor)!.Value;
            (off.Px, off.Py, off.X, off.Y).ShouldBe((400, 3, (double?)null, (double?)null));

            // The line that says an upload sent the pin still finds it.
            (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ImportBatchId == batch)).RasterPinId.ShouldBe(live);
            (await db.ResLinks.AnyAsync(l => db.ImportBatchItems.Any(i => i.RasterPinId == l.Id && i.ImportBatchId == batch)))
                .ShouldBeTrue();

            // The pins read back as the stream reads them: same identifier, pixel and revision, so a
            // phone's held revision still matches; the removed one's removal is its tombstone.
            var rows = await PlacePointQueries.Rows(db).AsNoTracking().Where(p => p.MapId == map).ToListAsync();
            var liveRow = rows.Single(r => r.Id == live);
            PlacePointQueries.Pixel(liveRow).ShouldBe((12, 3));
            liveRow.PlaceId.ShouldBe(placeA);
            (liveRow.UpdatedAt.UtcTicks / 10).ShouldBe(updated.UtcTicks / 10);
            rows.Single(r => r.Id == removed).DeletedAt.ShouldBe(gone);
            (await PlacePointQueries.Rows(db).AsNoTracking().AnyAsync(p => p.Id == onRemovedMap)).ShouldBeFalse("hidden with its map");
            (await PlacePointQueries.Rows(db).AsNoTracking().IgnoreQueryFilters().AnyAsync(p => p.Id == onRemovedMap)).ShouldBeTrue();

            // The old table is kept as it was, read-only, for one release.
            (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM raster_pins_moved_to_links").SingleAsync())
                .ShouldBe(4);
            (await db.Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM raster_pins_moved_to_links WHERE id = {removed} AND deleted_at IS NOT NULL")
                .SingleAsync())
                .ShouldBe(1);
        }

        // Starting again changes nothing: the fractions are filled once.
        await using (var db = Context(connectionString))
        {
            var before = await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == live && m.IsMain).Select(m => m.Anchor).SingleAsync();
            await new PlacePointStore(db).RefreshFramesAsync(null);
            (await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == live && m.IsMain).Select(m => m.Anchor).SingleAsync())
                .ShouldBe(before);
        }
    }

    /// <summary>
    /// A picture document with its revisions, each a list of files: (id, revision, current, code).
    /// </summary>
    private static void Picture(StringBuilder sql, Guid owner, Guid document, params (Guid File, int Revision, bool Current, int? Code)[] files)
    {
        sql.AppendLine($"""
            INSERT INTO documents (id, title, owner_user_id, visibility, created_at, updated_at)
            VALUES ('{document}', 'plan', '{owner}', 0, now(), now());
            """);
        var stamp = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var revision in files.GroupBy(f => (f.Revision, f.Current)))
        {
            var version = Guid.CreateVersion7();
            sql.AppendLine($"""
                INSERT INTO document_versions (id, document_id, version_number, is_current, created_at, updated_at)
                VALUES ('{version}', '{document}', {revision.Key.Revision}, {(revision.Key.Current ? "true" : "false")}, now(), now());
                """);
            foreach (var file in revision)
            {
                stamp = stamp.AddMinutes(1);
                var code = file.Code is { } c ? c.ToString(CultureInfo.InvariantCulture) : "NULL";
                sql.AppendLine($"""
                    INSERT INTO files (id, storage_path, original_name, mime_type, size_bytes, sha256,
                        document_version_id, kind, pixel_width, pixel_height, pixel_orientation, created_at, updated_at)
                    VALUES ('{file.File}', 'invented/{file.File:N}.png', 'plan.png', 'image/png', 0, '{new string('a', 64)}',
                        '{version}', {(short)FileKind.Image}, 40, 30, {code}, {Stamp(stamp)}, {Stamp(stamp)});
                    """);
            }
        }
    }

    private static string Stamp(DateTimeOffset at) =>
        "'" + at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "+00'";

    private async Task<string> DatabaseAtAsync(string target)
    {
        var name = $"silexgis_pins_{Guid.NewGuid():N}";
        databases.Add(name);
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
        await using var db = Context(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
        return connectionString;
    }

    private static SilexGisDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
