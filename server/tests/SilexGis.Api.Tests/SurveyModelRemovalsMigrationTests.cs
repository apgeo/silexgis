// SPDX-License-Identifier: AGPL-3.0-or-later
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

namespace SilexGis.Api.Tests;

/// <summary>
/// The update that starts recording removed survey models and the lines of phones' place stations
/// carries every row a database already holds: the models stay as they were, an upload's lines stay
/// as they were, and from then on every model removed — by any path — leaves a record a phone is told
/// from.
/// <para>
/// On a database of its own, migrated to the step before, holding invented rows written the way that
/// step stored them, then migrated on.
/// </para>
/// </summary>
public sealed class SurveyModelRemovalsMigrationTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Before = "20261031045722_TerrainBuildAchievedDepth";

    private readonly string adminConnectionString;
    private readonly List<string> databases = [];

    public SurveyModelRemovalsMigrationTests(PostgresFixture postgres) => adminConnectionString = postgres.ConnectionString;

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
    public async Task Existing_rows_survive_and_every_model_removed_afterwards_is_recorded()
    {
        var connectionString = await DatabaseAtAsync(Before);

        // Invented throughout: one account, one cave with two models, an upload with one line.
        var owner = Guid.CreateVersion7();
        var cave = Guid.CreateVersion7();
        var kept = Guid.CreateVersion7();
        var removed = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();
        // Written by SQL against the step's own columns, not through today's model: a later step
        // that adds a column to one of these tables would otherwise break the insert.
        var caveType = $"invented-{Guid.NewGuid():N}"[..20];
        var sql = new System.Text.StringBuilder();
        sql.AppendLine(MigrationFixtureSql.User(owner, "migration-owner"));
        sql.AppendLine($$"""
            INSERT INTO cave_types (code, name, sort_order, created_at, updated_at)
            VALUES ('{{caveType}}', 'invented', 990, now(), now());
            INSERT INTO features (id, kind, category, name, owner_user_id, visibility, location_protected,
                is_protected_effective, created_at, updated_at)
            VALUES ('{{cave}}', {{(short)FeatureKind.Cave}}, 0, 'Pestera 1', '{{owner}}', 0, false, false, now(), now());
            INSERT INTO caves (id, cave_type_id, exploration_status, is_show_cave, entrance_count)
            SELECT '{{cave}}', id, 0, false, 0 FROM cave_types WHERE code = '{{caveType}}';
            """);
        foreach (var (id, name, format, current) in new[]
                 {
                     (kept, "Sistemul", SurveyModelFormat.Lox, true),
                     (removed, "Varianta", SurveyModelFormat.Survex3d, false),
                 })
        {
            var file = Guid.CreateVersion7();
            sql.AppendLine(MigrationFixtureSql.DocumentWithFile(
                owner, Guid.CreateVersion7(), Guid.CreateVersion7(), file, name, (short)FileKind.Survey, $"{name}.lox"));
            sql.AppendLine($"""
                INSERT INTO survey_models (id, cave_feature_id, name, file_id, format, is_current, created_at, updated_at)
                VALUES ('{id}', '{cave}', '{name}', '{file}', {(short)format}, {(current ? "true" : "false")}, now(), now());
                """);
        }

        sql.AppendLine($"""
            INSERT INTO import_batches (id, confirmed_by_user_id, source, mode, created_count, attached_count,
                skipped_count, created_at, updated_at)
            VALUES ('{batch}', '{owner}', {(short)ImportSource.DeviceSync}, 0, 0, 0, 0, now(), now());
            """);

        // A line of an upload, written the way the step before stored lines.
        sql.AppendLine($$"""
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids)
            VALUES ('{{batch}}', {{(short)ImportDecisionAction.Create}}, '{}', '[]');
            """);
        await ExecuteAsync(connectionString, sql.ToString());

        await using (var db = Context(connectionString))
        {
            await db.GetService<IMigrator>().MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

            // Every row held before is still there, as it was.
            var models = await db.SurveyModels.AsNoTracking().Where(m => m.CaveFeatureId == cave).ToListAsync();
            models.Select(m => m.Id).ShouldBe([kept, removed], ignoreOrder: true);
            models.Single(m => m.Id == kept).IsCurrent.ShouldBeTrue();
            var line = await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ImportBatchId == batch);
            line.PlaceStationId.ShouldBeNull();
            line.Action.ShouldBe(ImportDecisionAction.Create);

            // Nothing is recorded for what was removed before the update; a removal from now on is,
            // by a statement that loads no row, with its cave and the database's moment.
            (await db.SyncSurveyModelRemovals.CountAsync()).ShouldBe(0);
            var at = DateTimeOffset.UtcNow;
            await db.SurveyModels.Where(m => m.Id == removed).ExecuteDeleteAsync();
            var record = await db.SyncSurveyModelRemovals.AsNoTracking().SingleAsync();
            record.SurveyModelId.ShouldBe(removed);
            record.CaveFeatureId.ShouldBe(cave);
            record.RemovedAt.ShouldBeGreaterThan(at.AddMinutes(-5));
            (await db.SurveyModels.AsNoTracking().CountAsync(m => m.Id == kept)).ShouldBe(1);
        }
    }

    private async Task<string> DatabaseAtAsync(string target)
    {
        var name = $"silexgis_models_{Guid.NewGuid():N}";
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
