// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The update that gives foreign keys and the bin the lookup indexes they were missing carries every
/// row a database already holds, adds the plain lookups beside the partial unique indexes without
/// renaming or replacing those, and leaves each lookup usable by the question it exists for.
/// <para>
/// On a database of its own, migrated to the step before, holding invented rows written by SQL the
/// way that step stored them (not through today's model, which a later column would break), then
/// migrated on.
/// </para>
/// </summary>
public sealed class UsableLookupIndexesMigrationTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Before = "20261101085640_SurveyModelRemovalsAndPlaceStations";

    private readonly string adminConnectionString;
    private readonly List<string> databases = [];

    public UsableLookupIndexesMigrationTests(PostgresFixture postgres) => adminConnectionString = postgres.ConnectionString;

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
    public async Task Existing_rows_survive_and_each_new_lookup_answers_its_question()
    {
        var connectionString = await DatabaseAtAsync(Before);

        // The fixture half: the step before really lacks the lookups and still has the inverted
        // index, so the assertions after the update measure what the update did.
        (await IndexesAsync(connectionString)).Keys.ShouldNotContain("ix_attachments_feature_id");
        (await IndexesAsync(connectionString)).ShouldContainKey("ix_features_deleted_at");

        // Invented throughout: one account, a feature type of its own, a parent and a child feature
        // joined by an edge, a third feature in the bin, and one tag on the child.
        var owner = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var binned = Guid.CreateVersion7();
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO users (id, access_failed_count, address_point_visibility, address_visibility,
                bio_visibility, caving_club_visibility, created_at, email_confirmed, email_visibility,
                locale, lockout_enabled, phone_number_confirmed, phone_visibility, real_name_visibility,
                two_factor_authenticator_enabled, two_factor_email_enabled, two_factor_enabled,
                two_factor_sms_enabled, updated_at, uploads_need_review, user_name, normalized_user_name)
            VALUES ('{{owner}}', 0, 0, 0, 0, 0, now(), true, 0, 'en', false, false, 0, 0,
                false, false, false, false, now(), false, 'index-fixture', 'INDEX-FIXTURE');

            INSERT INTO feature_types (code, name, sort_order, category, accepted_geometry_classes,
                protected_display, requires_parent, properties_schema_version, bound_kind, created_at, updated_at)
            VALUES ('index_fixture_spring', 'Invented spring', 990, 0, '{0}', 0, false, 1, 0, now(), now());

            INSERT INTO features (id, kind, category, feature_type_id, name, owner_user_id, visibility,
                location_protected, is_protected_effective, created_at, updated_at, deleted_at)
            SELECT v.id, 0, 0, t.id, v.name, '{{owner}}', 0, false, false, now(), now(), v.deleted_at
            FROM feature_types t,
                (VALUES ('{{parent}}'::uuid, 'Invented parent', NULL::timestamptz),
                        ('{{child}}'::uuid, 'Invented child', NULL::timestamptz),
                        ('{{binned}}'::uuid, 'Invented binned', now())) AS v(id, name, deleted_at)
            WHERE t.code = 'index_fixture_spring';

            INSERT INTO feature_hierarchy_edges (parent_id, child_id, is_primary, created_at, updated_at)
            VALUES ('{{parent}}', '{{child}}', true, now(), now());

            INSERT INTO tags (name, slug, created_at, updated_at) VALUES ('Invented tag', 'invented-tag', now(), now());
            INSERT INTO taggings (tag_id, feature_id) SELECT id, '{{child}}' FROM tags WHERE slug = 'invented-tag';
            """);

        await using (var db = Context(connectionString))
        {
            await db.GetService<IMigrator>().MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }

        (await CountAsync(connectionString, $"SELECT count(*) FROM features WHERE id IN ('{parent}', '{child}', '{binned}')")).ShouldBe(3);
        (await CountAsync(connectionString, $"SELECT count(*) FROM features WHERE id = '{binned}' AND deleted_at IS NOT NULL")).ShouldBe(1);
        (await CountAsync(connectionString, $"SELECT count(*) FROM feature_hierarchy_edges WHERE child_id = '{child}' AND is_primary")).ShouldBe(1);
        (await CountAsync(connectionString, $"SELECT count(*) FROM taggings WHERE feature_id = '{child}'")).ShouldBe(1);

        var indexes = await IndexesAsync(connectionString);

        // The plain lookups exist, unfiltered and not unique.
        foreach (var lookup in new[]
                 {
                     "ix_attachments_feature_id", "ix_attachments_entity_type_entity_id",
                     "ix_centerlines_cave_feature_id", "ix_feature_hierarchy_edges_child_id",
                     "ix_taggings_tag_id", "ix_survey_upload_public_links_survey_upload_id",
                 })
        {
            indexes.ShouldContainKey(lookup);
            indexes[lookup].ShouldNotContain("UNIQUE", Case.Sensitive);
            indexes[lookup].ShouldNotContain(" WHERE ", Case.Sensitive);
        }

        // The partial unique indexes they used to be swallowed by keep their names and their rules.
        indexes["ux_attachments_primary_feature"].ShouldContain("UNIQUE");
        indexes["ux_attachments_primary_feature"].ShouldContain("is_primary");
        indexes["ux_attachments_primary_entity"].ShouldContain("UNIQUE");
        indexes["ix_centerlines_default_per_cave"].ShouldContain("is_default");
        indexes["ix_feature_hierarchy_edges_primary"].ShouldContain("is_primary");

        // The bin's index now holds the deleted rows rather than every live one.
        indexes.Keys.ShouldNotContain("ix_features_deleted_at");
        indexes["ix_features_deleted_at_not_null"].ShouldContain("deleted_at IS NOT NULL");

        // And each answers the lookup it exists for. Sequential scans are switched off because a
        // table of three rows would be scanned whatever indexes it had; what is asked here is only
        // whether an index exists that the planner is allowed to use for this predicate.
        (await PlanAsync(connectionString, $"SELECT id FROM feature_hierarchy_edges WHERE child_id = '{child}'"))
            .ShouldContain("ix_feature_hierarchy_edges_child_id");
        (await PlanAsync(connectionString, "SELECT id FROM taggings WHERE tag_id = 1"))
            .ShouldContain("ix_taggings_tag_id");
        (await PlanAsync(connectionString, $"SELECT id FROM attachments WHERE feature_id = '{child}'"))
            .ShouldContain("ix_attachments_feature_id");
        (await PlanAsync(connectionString, "SELECT id FROM features WHERE deleted_at IS NOT NULL"))
            .ShouldContain("ix_features_deleted_at_not_null");
    }

    private static async Task<Dictionary<string, string>> IndexesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(0)] = reader.GetString(1);
        }

        return indexes;
    }

    private static async Task<string> PlanAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand($"EXPLAIN {sql}", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join("\n", lines);
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> DatabaseAtAsync(string target)
    {
        var name = $"silexgis_indexes_{Guid.NewGuid():N}";
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
