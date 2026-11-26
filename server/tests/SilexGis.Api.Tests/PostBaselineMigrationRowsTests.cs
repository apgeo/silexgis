// SPDX-License-Identifier: AGPL-3.0-or-later
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
using SilexGis.Infrastructure.Persistence;
using static SilexGis.Api.Tests.Support.MigrationFixtureSql;

namespace SilexGis.Api.Tests;

/// <summary>
/// The updates since the baseline that carry or rewrite rows, each run over a database that already
/// holds some, and each checked for what it did to them.
/// </summary>
/// <remarks>
/// <para>
/// Each of these updates holds statements that act on stored rows — a backfill, a conditional
/// delete, a seeded entry, a copy from one column into another — and on a fresh database every one
/// of them touches nothing. An installation that is updated rather than installed meets them with
/// its rows in place, so a statement that is wrong about those rows is only seen there.
/// </para>
/// <para>
/// Each test has a database of its own, migrated to the step before the update, filled with
/// invented rows written by SQL against that step's columns (today's model would name columns that
/// step does not have), and then migrated to the update itself and no further, so that what is
/// asserted is what that update did.
/// </para>
/// </remarks>
public sealed class PostBaselineMigrationRowsTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly string adminConnectionString;
    private readonly List<string> databases = [];

    public PostBaselineMigrationRowsTests(PostgresFixture postgres) => adminConnectionString = postgres.ConnectionString;

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

    // ---- caves carry a feature type -------------------------------------------------------

    private const string BeforeCaveTypes = "20261021060552_FeatureUpdatedAtIndex";
    private const string CaveTypes = "20261023202150_CavesCarryAFeatureType";

    /// <summary>
    /// Every cave, deleted ones included, takes the shipped cave type; a type an installation made
    /// under the code that type takes keeps its rows under a new code; and the old surface "pit"
    /// goes only where nothing uses it.
    /// </summary>
    [Fact]
    public async Task Caves_take_the_cave_type_and_the_installations_own_types_keep_their_rows()
    {
        var connectionString = await DatabaseAtAsync(BeforeCaveTypes);
        var owner = Guid.CreateVersion7();
        var liveCave = Guid.CreateVersion7();
        var deletedCave = Guid.CreateVersion7();
        var ownTyped = Guid.CreateVersion7();
        var sql = new StringBuilder();
        sql.AppendLine(User(owner, "cave-type-owner"));
        sql.AppendLine(FeatureType("cave", "A kind the installation made"));
        sql.AppendLine(FeatureType("pit", "Pit"));
        sql.AppendLine($"""
            INSERT INTO cave_types (code, name, sort_order, created_at, updated_at)
            VALUES ('invented_cave_class', 'Invented class', 990, now(), now());
            INSERT INTO features (id, kind, category, feature_type_id, name, owner_user_id, visibility,
                location_protected, is_protected_effective, created_at, updated_at, deleted_at)
            SELECT '{ownTyped}', {(short)FeatureKind.Generic}, 0, id, 'Uses the installation''s own kind', '{owner}', 0,
                false, false, now(), now(), NULL
            FROM feature_types WHERE code = 'cave';
            INSERT INTO features (id, kind, category, name, owner_user_id, visibility, location_protected,
                is_protected_effective, created_at, updated_at, deleted_at)
            VALUES ('{liveCave}', {(short)FeatureKind.Cave}, 1, 'Pestera 1', '{owner}', 0, false, false, now(), now(), NULL),
                   ('{deletedCave}', {(short)FeatureKind.Cave}, 1, 'Pestera 2', '{owner}', 0, false, false, now(), now(), now());
            INSERT INTO caves (id, cave_type_id, exploration_status, is_show_cave, entrance_count)
            SELECT v.id, t.id, 0, false, 0
            FROM cave_types t, (VALUES ('{liveCave}'::uuid), ('{deletedCave}'::uuid)) AS v(id)
            WHERE t.code = 'invented_cave_class';
            """);
        await ExecuteAsync(connectionString, sql.ToString());
        var ownTypeId = await CountAsync(connectionString, "SELECT id FROM feature_types WHERE code = 'cave'");

        await MigrateToAsync(connectionString, CaveTypes);

        // The shipped type: one, bound to caves, at the seeder's place.
        (await CountAsync(connectionString, "SELECT count(*) FROM feature_types WHERE code = 'cave'")).ShouldBe(1);
        var caveTypeId = await CountAsync(connectionString, "SELECT id FROM feature_types WHERE code = 'cave' AND bound_kind = 1");
        caveTypeId.ShouldNotBe(ownTypeId);
        (await CountAsync(connectionString, $"SELECT sort_order FROM feature_types WHERE id = {caveTypeId}")).ShouldBe(320);

        // Both caves carry it, the deleted one too: a restored cave comes back with its type.
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM features WHERE id IN ('{liveCave}', '{deletedCave}') AND feature_type_id = {caveTypeId}"))
            .ShouldBe(2);
        (await CountAsync(connectionString, $"SELECT count(*) FROM features WHERE id = '{deletedCave}' AND deleted_at IS NOT NULL"))
            .ShouldBe(1);

        // The installation's own kind keeps its id, its row and its feature, under a code of its own.
        (await TextAsync(connectionString, $"SELECT code FROM feature_types WHERE id = {ownTypeId}"))
            .ShouldBe($"cave_{ownTypeId}");
        (await CountAsync(connectionString, $"SELECT feature_type_id FROM features WHERE id = '{ownTyped}'"))
            .ShouldBe(ownTypeId);

        // Nothing used the old surface pit, so it went.
        (await CountAsync(connectionString, "SELECT count(*) FROM feature_types WHERE code = 'pit'")).ShouldBe(0);

        // And the rule that ties a feature's type to its kind holds over every row carried across.
        (await CountAsync(connectionString,
                "SELECT count(*) FROM pg_constraint WHERE conname = 'fk_features_feature_types_feature_type_id_kind'"))
            .ShouldBe(1);
    }

    /// <summary>The old surface "pit" stays where a feature uses it, even one in the bin.</summary>
    [Fact]
    public async Task A_pit_kind_something_uses_is_kept()
    {
        var connectionString = await DatabaseAtAsync(BeforeCaveTypes);
        var owner = Guid.CreateVersion7();
        var binned = Guid.CreateVersion7();
        var sql = new StringBuilder();
        sql.AppendLine(User(owner, "pit-owner"));
        sql.AppendLine(FeatureType("pit", "Pit"));
        sql.AppendLine($"""
            INSERT INTO features (id, kind, category, feature_type_id, name, owner_user_id, visibility,
                location_protected, is_protected_effective, created_at, updated_at, deleted_at)
            SELECT '{binned}', {(short)FeatureKind.Generic}, 0, id, 'A pit in the bin', '{owner}', 0,
                false, false, now(), now(), now()
            FROM feature_types WHERE code = 'pit';
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        await MigrateToAsync(connectionString, CaveTypes);

        (await CountAsync(connectionString, "SELECT count(*) FROM feature_types WHERE code = 'pit'")).ShouldBe(1);
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM features f JOIN feature_types t ON t.id = f.feature_type_id WHERE f.id = '{binned}' AND t.code = 'pit'"))
            .ShouldBe(1);
    }

    // ---- removed links are recorded, and the device default set --------------------------

    private const string BeforeLinkRemovals = "20261024170954_ImportBatchLinesRecordTheirUndo";
    private const string LinkRemovals = "20261024180631_SyncLinkRemovalsAndDeviceDefaultSet";

    /// <summary>
    /// The links already held are kept and left out of the default set; nothing is recorded for
    /// them; and from then on every link that leaves the table is recorded, whichever way it goes.
    /// </summary>
    [Fact]
    public async Task Existing_links_are_kept_and_every_later_removal_is_recorded()
    {
        var connectionString = await DatabaseAtAsync(BeforeLinkRemovals);
        var owner = Guid.CreateVersion7();
        var feature = Guid.CreateVersion7();
        var (document, firstFile, secondFile) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (detached, cascaded) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sql = new StringBuilder();
        sql.AppendLine(User(owner, "link-owner"));
        sql.AppendLine($"""
            INSERT INTO features (id, kind, category, name, owner_user_id, visibility, location_protected,
                is_protected_effective, created_at, updated_at)
            VALUES ('{feature}', {(short)FeatureKind.Cave}, 1, 'Pestera 1', '{owner}', 0, false, false, now(), now());
            """);
        sql.AppendLine(DocumentWithFile(owner, document, Guid.CreateVersion7(), firstFile, "plan", (short)FileKind.Image, "plan.png"));
        sql.AppendLine(DocumentWithFile(
            owner, Guid.CreateVersion7(), Guid.CreateVersion7(), secondFile, "photo", (short)FileKind.Image, "photo.jpg"));
        sql.AppendLine($"""
            INSERT INTO attachments (id, file_id, feature_id, role, sort_order, created_at, updated_at)
            VALUES ('{detached}', '{firstFile}', '{feature}', 0, 0, now(), now()),
                   ('{cascaded}', '{secondFile}', '{feature}', 0, 1, now(), now());
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        await MigrateToAsync(connectionString, LinkRemovals);

        (await CountAsync(connectionString,
                $"SELECT count(*) FROM attachments WHERE feature_id = '{feature}' AND NOT in_device_default_set"))
            .ShouldBe(2);
        (await CountAsync(connectionString, "SELECT count(*) FROM sync_link_removals")).ShouldBe(0);

        // A link detached by a statement that loads no row is recorded with its file and document.
        await ExecuteAsync(connectionString, $"DELETE FROM attachments WHERE id = '{detached}'");
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM sync_link_removals WHERE attachment_id = '{detached}' AND file_id = '{firstFile}' "
                + $"AND document_id = '{document}' AND feature_id = '{feature}'"))
            .ShouldBe(1);

        // And one that goes with its file, by the cascade, is recorded all the same.
        await ExecuteAsync(connectionString, $"DELETE FROM files WHERE id = '{secondFile}'");
        (await CountAsync(connectionString, $"SELECT count(*) FROM attachments WHERE id = '{cascaded}'")).ShouldBe(0);
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM sync_link_removals WHERE attachment_id = '{cascaded}' AND file_id = '{secondFile}'"))
            .ShouldBe(1);
    }

    // ---- uploads can be held for review ---------------------------------------------------

    private const string BeforeReviewHold = "20261025231142_RasterMapsAndPins";
    private const string ReviewHold = "20261025231346_UploadReviewHold";

    /// <summary>
    /// The administrators group that already exists gains the review right once; another group
    /// gains nothing; and the accounts, uploads and lines held before are not held for review.
    /// </summary>
    [Fact]
    public async Task The_administrators_gain_the_review_right_once_and_nothing_held_before_is_held()
    {
        var connectionString = await DatabaseAtAsync(BeforeReviewHold);
        var owner = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();
        var (administrators, others) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sql = new StringBuilder();
        sql.AppendLine(User(owner, "review-owner"));
        sql.AppendLine(Group(administrators, "administrators"));
        sql.AppendLine(Group(others, "invented-club"));
        sql.AppendLine(Batch(batch, owner));
        sql.AppendLine($$"""
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids)
            VALUES ('{{batch}}', {{(short)ImportDecisionAction.Create}}, '{}', '[]');
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        await MigrateToAsync(connectionString, ReviewHold);

        (await CountAsync(connectionString,
                $"SELECT count(*) FROM access_entries WHERE permission_group_id = '{administrators}' AND domain = 24 "
                + "AND effect = 0 AND actions = 255 AND scope_kind = 0"))
            .ShouldBe(1);
        (await CountAsync(connectionString, $"SELECT count(*) FROM access_entries WHERE permission_group_id = '{others}'"))
            .ShouldBe(0);

        (await CountAsync(connectionString, $"SELECT count(*) FROM users WHERE id = '{owner}' AND NOT uploads_need_review"))
            .ShouldBe(1);
        (await CountAsync(connectionString, $"SELECT count(*) FROM import_batches WHERE id = '{batch}' AND review_state = 0 AND held_at IS NULL"))
            .ShouldBe(1);
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM import_batch_items WHERE import_batch_id = '{batch}' AND review_decision IS NULL "
                + "AND depends_on_item_id IS NULL AND review_id IS NULL"))
            .ShouldBe(1);
    }

    /// <summary>An administrators group that already holds the review right keeps the one it holds.</summary>
    [Fact]
    public async Task An_administrators_group_holding_the_review_right_is_left_as_it_is()
    {
        var connectionString = await DatabaseAtAsync(BeforeReviewHold);
        var administrators = Guid.CreateVersion7();
        await ExecuteAsync(connectionString, Group(administrators, "administrators") + $"""
            INSERT INTO access_entries (permission_group_id, effect, domain, actions, scope_kind, created_at, updated_at)
            VALUES ('{administrators}', 0, 24, 1, 0, now(), now());
            """);

        await MigrateToAsync(connectionString, ReviewHold);

        (await CountAsync(connectionString,
                $"SELECT count(*) FROM access_entries WHERE permission_group_id = '{administrators}' AND domain = 24"))
            .ShouldBe(1);
        (await CountAsync(connectionString,
                $"SELECT actions FROM access_entries WHERE permission_group_id = '{administrators}' AND domain = 24"))
            .ShouldBe(1);
    }

    // ---- a held line depends on a list of lines -------------------------------------------

    private const string BeforeDependencyList = "20261026002013_UploadReviewHeldBytes";
    private const string DependencyList = "20261026020357_UploadReviewDependencyList";

    /// <summary>
    /// A line that waited on another keeps waiting on it, now as a list of one; a line that waited
    /// on nothing still waits on nothing.
    /// </summary>
    [Fact]
    public async Task A_line_that_depended_on_another_keeps_that_dependency_as_a_list()
    {
        var connectionString = await DatabaseAtAsync(BeforeDependencyList);
        var owner = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();
        await ExecuteAsync(connectionString, User(owner, "dependency-owner") + Batch(batch, owner) + $$"""
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids)
            VALUES ('{{batch}}', {{(short)ImportDecisionAction.Create}}, '{"line": "first"}', '[]');
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids, depends_on_item_id)
            SELECT '{{batch}}', {{(short)ImportDecisionAction.Create}}, '{"line": "second"}', '[]', id
            FROM import_batch_items WHERE import_batch_id = '{{batch}}' AND source_properties ->> 'line' = 'first';
            """);
        var first = await CountAsync(connectionString,
            $"SELECT id FROM import_batch_items WHERE import_batch_id = '{batch}' AND source_properties ->> 'line' = 'first'");

        await MigrateToAsync(connectionString, DependencyList);

        (await TextAsync(connectionString,
                $"SELECT depends_on_item_ids::text FROM import_batch_items WHERE import_batch_id = '{batch}' AND source_properties ->> 'line' = 'second'"))
            .ShouldBe($"{{{first}}}");
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM import_batch_items WHERE import_batch_id = '{batch}' AND source_properties ->> 'line' = 'first' "
                + "AND depends_on_item_ids IS NULL"))
            .ShouldBe(1);
        (await CountAsync(connectionString,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'import_batch_items' AND column_name = 'depends_on_item_id'"))
            .ShouldBe(0);
        (await CountAsync(connectionString,
                "SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_import_batch_items_depends_on_item_ids' AND indexdef LIKE '%USING gin%'"))
            .ShouldBe(1);
    }

    // ---- a survey leg carries the day it was surveyed ------------------------------------------

    private const string BeforeLegDates = "20261119090754_TerrainDerivativeToolVersion";
    private const string LegDates = "20261126074551_SurveyShotSurveyedOn";

    /// <summary>
    /// The legs already held keep every column and gain an empty day — unknown, until their survey
    /// is read again — and stepping back takes the day away and keeps the legs.
    /// </summary>
    [Fact]
    public async Task Existing_legs_are_kept_with_no_day_and_stepping_back_keeps_them()
    {
        var connectionString = await DatabaseAtAsync(BeforeLegDates);
        var owner = Guid.CreateVersion7();
        var cave = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();
        var sql = new StringBuilder();
        sql.AppendLine(User(owner, "leg-dates-owner"));
        sql.AppendLine($"""
            INSERT INTO features (id, kind, category, name, owner_user_id, visibility, location_protected,
                is_protected_effective, created_at, updated_at)
            VALUES ('{cave}', {(short)FeatureKind.Cave}, 1, 'Pestera 1', '{owner}', 0, false, false, now(), now());
            INSERT INTO cave_types (code, name, sort_order, created_at, updated_at)
            VALUES ('invented_leg_dates', 'Invented', 990, now(), now());
            INSERT INTO caves (id, cave_type_id, exploration_status, is_show_cave, entrance_count)
            SELECT '{cave}', id, 0, false, 0 FROM cave_types WHERE code = 'invented_leg_dates';
            """);
        sql.AppendLine(DocumentWithFile(owner, Guid.CreateVersion7(), Guid.CreateVersion7(), file, "invented", (short)FileKind.Survey, "invented.3d"));
        sql.AppendLine($"""
            INSERT INTO survey_models (id, cave_feature_id, name, file_id, format, is_current, created_at, updated_at)
            VALUES ('{model}', '{cave}', 'Invented', '{file}', {(short)SurveyModelFormat.Survex3d}, true, now(), now());
            INSERT INTO survey_shots (survey_model_id, from_station_name, to_station_name, geom, length_m, flags, raw_flags)
            VALUES ('{model}', 'a', 'b', ST_GeomFromText('LINESTRING Z (7.5 41.25 800, 7.5001 41.25 790)', 4326), 12.5, 0, 0),
                   ('{model}', 'b', 'c', ST_GeomFromText('LINESTRING Z (7.5001 41.25 790, 7.5002 41.25 785)', 4326), 9.25, 2, 2);
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        await MigrateToAsync(connectionString, LegDates);

        (await CountAsync(connectionString,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'survey_shots' AND column_name = 'surveyed_on' "
                + "AND data_type = 'date' AND is_nullable = 'YES'"))
            .ShouldBe(1);
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM survey_shots WHERE survey_model_id = '{model}' AND surveyed_on IS NULL"))
            .ShouldBe(2);
        (await CountAsync(connectionString,
                $"SELECT count(*) FROM survey_shots WHERE survey_model_id = '{model}' AND from_station_name = 'b' AND to_station_name = 'c' "
                + "AND length_m = 9.25 AND flags = 2 AND ST_NPoints(geom) = 2"))
            .ShouldBe(1);

        // A day can be held; stepping back takes it away with the column, and keeps the legs.
        await ExecuteAsync(connectionString, "UPDATE survey_shots SET surveyed_on = DATE '2003-08-14' WHERE from_station_name = 'a'");
        await MigrateToAsync(connectionString, BeforeLegDates);
        (await CountAsync(connectionString,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'survey_shots' AND column_name = 'surveyed_on'"))
            .ShouldBe(0);
        (await CountAsync(connectionString, $"SELECT count(*) FROM survey_shots WHERE survey_model_id = '{model}'")).ShouldBe(2);
    }

    // ---- rows of the older steps --------------------------------------------------------------

    private static string FeatureType(string code, string name) => $$"""
        INSERT INTO feature_types (code, name, sort_order, category, accepted_geometry_classes,
            protected_display, requires_parent, properties_schema_version, created_at, updated_at)
        VALUES ('{{code}}', '{{name}}', 990, 0, '{0}', 0, false, 1, now(), now());
        """;

    private static string Group(Guid id, string slug) => $"""
        INSERT INTO permission_groups (id, name, slug, is_protected, is_seeded, created_at, updated_at)
        VALUES ('{id}', '{slug}', '{slug}', false, false, now(), now());
        """;

    private static string Batch(Guid id, Guid owner) => $"""
        INSERT INTO import_batches (id, confirmed_by_user_id, source, mode, created_count, attached_count,
            skipped_count, created_at, updated_at)
        VALUES ('{id}', '{owner}', {(short)ImportSource.DeviceSync}, 0, 0, 0, 0, now(), now());
        """;

    private async Task<string> DatabaseAtAsync(string target)
    {
        var name = $"silexgis_rows_{Guid.NewGuid():N}";
        databases.Add(name);
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
        await MigrateToAsync(connectionString, target);
        return connectionString;
    }

    private static async Task MigrateToAsync(string connectionString, string target)
    {
        await using var db = new SilexGisDbContext(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }
}
