// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Access;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The shipped permission groups are given to an installation once. A non-protected one an
/// operator deletes stays deleted across restarts — the seeder runs on every start, so "create
/// it when missing" would quietly reverse the operator's decision and re-enrol every legacy role
/// holder. The protected groups, which the application will not delete, are still put back.
/// Each test runs on a database of its own, because it deletes groups others rely on.
/// </summary>
public sealed class PermissionGroupSeedOnceTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string MigrationBeforeTheRecord = "20261111210040_AddTestDataRuns";

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_groups_{Guid.NewGuid():N}";
    private string connectionString = null!;

    public PermissionGroupSeedOnceTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        connectionString =
            new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
    }

    [Fact]
    public async Task A_deleted_shipped_group_stays_deleted_and_a_deleted_protected_one_comes_back()
    {
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            await PermissionGroupSeeder.SeedAsync(db);
            (await db.PermissionGroups.CountAsync(g => g.IsSeeded)).ShouldBe(6);
            await DeleteGroupAsync(db, SeededPermissionGroups.EditorsSlug);
            await DeleteGroupAsync(db, SeededPermissionGroups.AllUsersSlug);
        }

        await using (var db = CreateContext())
        {
            await PermissionGroupSeeder.SeedAsync(db);
            (await db.PermissionGroups.AnyAsync(g => g.Slug == SeededPermissionGroups.EditorsSlug))
                .ShouldBeFalse("a shipped group an operator deleted was created again on the next start");
            (await db.PermissionGroups.AnyAsync(g => g.Slug == SeededPermissionGroups.AllUsersSlug))
                .ShouldBeTrue("a protected group cannot be deleted through the application and is put back");
            (await db.PermissionGroups.AnyAsync(g => g.Slug == SeededPermissionGroups.ReviewersSlug)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task An_installation_seeded_before_the_record_existed_keeps_a_group_it_had_deleted()
    {
        // An installation from before the record: seeded on every start, the record never written,
        // and Editors deleted by its operator. The update's migration has to mark it as given.
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeTheRecord);
            await PermissionGroupSeeder.SeedAsync(db);
            await db.AppSettings.Where(s => s.Key == AppSettingSections.SeededPermissionGroups).ExecuteDeleteAsync();
            await DeleteGroupAsync(db, SeededPermissionGroups.EditorsSlug);
        }

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            await PermissionGroupSeeder.SeedAsync(db);
            (await db.PermissionGroups.AnyAsync(g => g.Slug == SeededPermissionGroups.EditorsSlug))
                .ShouldBeFalse("the update brought back a group the operator had deleted before it");
            (await db.PermissionGroups.CountAsync(g => g.IsSeeded)).ShouldBe(5);
        }
    }

    [Fact]
    public async Task A_lost_record_is_written_again_without_doubling_any_group()
    {
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            await PermissionGroupSeeder.SeedAsync(db);
            await db.AppSettings.Where(s => s.Key == AppSettingSections.SeededPermissionGroups).ExecuteDeleteAsync();
        }

        await using (var db = CreateContext())
        {
            await PermissionGroupSeeder.SeedAsync(db);
            (await db.PermissionGroups.CountAsync(g => g.IsSeeded)).ShouldBe(6);
            (await db.AppSettings.AnyAsync(s => s.Key == AppSettingSections.SeededPermissionGroups)).ShouldBeTrue();
        }
    }

    private static async Task DeleteGroupAsync(SilexGisDbContext db, string slug)
    {
        var id = await db.PermissionGroups.Where(g => g.Slug == slug).Select(g => g.Id).SingleAsync();
        await db.PermissionGroupMembers.Where(m => m.PermissionGroupId == id).ExecuteDeleteAsync();
        await db.AccessEntries.Where(e => e.PermissionGroupId == id).ExecuteDeleteAsync();
        await db.PermissionGroups.Where(g => g.Id == id).ExecuteDeleteAsync();
    }

    private SilexGisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
