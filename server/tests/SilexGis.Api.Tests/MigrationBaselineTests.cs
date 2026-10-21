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
/// A database the old migration chain built is moved onto the single baseline without the baseline
/// being run against it; one standing too far back is refused; a fresh one is left to the migrator.
/// <para>
/// Each case runs on a database of its own, because it rewrites the migration history and takes
/// parts of the schema away — neither of which a database shared with other tests could survive.
/// The old chain's state is written directly: its migrations no longer exist to be run, and the
/// schema the baseline builds is the one the chain left (compared, dump against dump, when the
/// chain was squashed).
/// </para>
/// </summary>
public sealed class MigrationBaselineTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly string adminConnectionString;
    private readonly List<string> databases = [];

    public MigrationBaselineTests(PostgresFixture postgres) => adminConnectionString = postgres.ConnectionString;

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

    /// <summary>
    /// A new database of this class's own, migrated by today's migrations — or, given
    /// <paramref name="target"/>, only as far as that one. The old chain's head left exactly the
    /// schema the baseline builds, so a database standing for one is migrated to the baseline and no
    /// further: the migrations added after it are what the move must still leave to the migrator.
    /// </summary>
    private async Task<string> MigratedDatabaseAsync(string? target = null)
    {
        var name = $"silexgis_baseline_{Guid.NewGuid():N}";
        databases.Add(name);
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
        await using var db = Context(connectionString);
        if (target is null)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.GetService<IMigrator>().MigrateAsync(target);
        }

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

    /// <summary>Writes a history as the old chain would have left it, ending at <paramref name="last"/>.</summary>
    private static Task OldHistoryAsync(string connectionString, string last) => ExecuteAsync(connectionString, $"""
        DELETE FROM "__EFMigrationsHistory";
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES
            ('20260801214814_InitialSchema', '10.0.9'),
            ('20260804160557_ResLinks', '10.0.9'),
            ('{last}', '10.0.9');
        """);

    private static async Task<List<string>> HistoryAsync(string connectionString)
    {
        await using var db = Context(connectionString);
        return [.. await db.Database.GetAppliedMigrationsAsync()];
    }

    [Fact]
    public async Task A_database_at_the_old_chains_head_keeps_its_tables_and_takes_the_baseline_as_its_history()
    {
        var connectionString = await MigratedDatabaseAsync(MigrationBaseline.BaselineId);
        await OldHistoryAsync(connectionString, MigrationBaseline.ChainHead);
        // A row written before the move survives it: nothing is recreated.
        await ExecuteAsync(connectionString,
            "INSERT INTO app_settings (key, value, created_at, updated_at) VALUES ('kept', '{\"a\":1}', now(), now())");

        await using (var db = Context(connectionString))
        {
            (await MigrationBaseline.AdoptAsync(db)).ShouldBe(MigrationBaseline.Outcome.Adopted);
            (await HistoryAsync(connectionString)).ShouldBe([MigrationBaseline.BaselineId]);

            // What came after the baseline is then the migrator's, as on any database.
            await db.Database.MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            (await db.Database.SqlQueryRaw<string>("SELECT key AS \"Value\" FROM app_settings WHERE key = 'kept'").ToListAsync())
                .ShouldBe(["kept"]);
            (await HistoryAsync(connectionString)).ShouldBe(db.Database.GetMigrations().ToList());
        }

        // Running it again changes nothing.
        await using var again = Context(connectionString);
        (await MigrationBaseline.AdoptAsync(again)).ShouldBe(MigrationBaseline.Outcome.NothingToDo);
    }

    [Fact]
    public async Task A_database_a_few_steps_short_of_the_head_is_brought_up_before_it_is_moved()
    {
        // Where the installations running at the squash stood: before saved filters and before the
        // map-layer flag for shared links.
        var connectionString = await MigratedDatabaseAsync(MigrationBaseline.BaselineId);
        await ExecuteAsync(connectionString, """
            DROP TABLE saved_filters;
            ALTER TABLE map_layers DROP COLUMN on_shared_links;
            """);
        await OldHistoryAsync(connectionString, "20261016021043_JobRetryAndProgress");

        await using var db = Context(connectionString);
        (await MigrationBaseline.AdoptAsync(db)).ShouldBe(MigrationBaseline.Outcome.Adopted);

        (await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.saved_filters') IS NOT NULL AS \"Value\"").SingleAsync())
            .ShouldBeTrue();
        (await db.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'map_layers' AND column_name = 'on_shared_links') AS \"Value\"")
            .SingleAsync()).ShouldBeTrue();
        (await HistoryAsync(connectionString)).ShouldBe([MigrationBaseline.BaselineId]);
    }

    [Fact]
    public async Task A_database_further_back_is_refused_and_left_as_it_was()
    {
        var connectionString = await MigratedDatabaseAsync(MigrationBaseline.BaselineId);
        await OldHistoryAsync(connectionString, "20260930120000_SomethingOlder");

        await using var db = Context(connectionString);
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => MigrationBaseline.AdoptAsync(db));
        refused.Message.ShouldStartWith(MigrationBaseline.RefusedCode);

        (await HistoryAsync(connectionString)).ShouldContain("20260930120000_SomethingOlder");
        (await HistoryAsync(connectionString)).ShouldNotContain(MigrationBaseline.BaselineId);
    }

    [Fact]
    public async Task A_fresh_database_is_left_to_the_migrator_which_applies_the_baseline()
    {
        var connectionString = await MigratedDatabaseAsync();

        await using var db = Context(connectionString);
        (await MigrationBaseline.AdoptAsync(db)).ShouldBe(MigrationBaseline.Outcome.NothingToDo);
        var history = await HistoryAsync(connectionString);
        history[0].ShouldBe(MigrationBaseline.BaselineId);
        history.ShouldBe(db.Database.GetMigrations().ToList());
    }
}
