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
using SilexGis.Infrastructure.Identity;
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
        await using (var db = Context(connectionString))
        {
            db.Users.Add(new SilexGisUser { Id = owner, UserName = "migration-owner", Email = "migration-owner@example.invalid" });
            var caveType = new CaveType { Code = $"invented-{Guid.NewGuid():N}"[..20], Name = "invented" };
            db.CaveTypes.Add(caveType);
            await db.SaveChangesAsync();
            db.Features.Add(new Feature
            {
                Id = cave, Kind = FeatureKind.Cave, Name = "Pestera 1", OwnerUserId = owner,
                Cave = new Cave { Id = cave, CaveTypeId = caveType.Id },
            });
            foreach (var (id, name, format, current) in new[]
                     {
                         (kept, "Sistemul", SurveyModelFormat.Lox, true),
                         (removed, "Varianta", SurveyModelFormat.Survex3d, false),
                     })
            {
                var file = Guid.CreateVersion7();
                var document = Guid.CreateVersion7();
                var version = Guid.CreateVersion7();
                db.Documents.Add(new Document { Id = document, Title = name, OwnerUserId = owner });
                db.DocumentVersions.Add(new DocumentVersion { Id = version, DocumentId = document, VersionNumber = 1, IsCurrent = true });
                db.StoredFiles.Add(new StoredFile
                {
                    Id = file, DocumentVersionId = version, StoragePath = $"invented/{file:N}.lox", OriginalName = $"{name}.lox",
                    MimeType = "application/octet-stream", Sha256 = new string('b', 64), Kind = FileKind.Survey, SizeBytes = 12,
                });
                db.SurveyModels.Add(new SurveyModel
                {
                    Id = id, CaveFeatureId = cave, Name = name, FileId = file, Format = format, IsCurrent = current,
                });
            }

            db.ImportBatches.Add(new ImportBatch { Id = batch, ConfirmedByUserId = owner, Source = ImportSource.DeviceSync });
            await db.SaveChangesAsync();
        }

        // A line of an upload, written the way the step before stored lines.
        await ExecuteAsync(connectionString, $$"""
            INSERT INTO import_batch_items (import_batch_id, action, source_properties, attachment_ids)
            VALUES ('{{batch}}', {{(short)ImportDecisionAction.Create}}, '{}', '[]');
            """);

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
