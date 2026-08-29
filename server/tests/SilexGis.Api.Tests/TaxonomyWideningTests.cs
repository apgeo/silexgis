// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Seeding reaches a database that was seeded before.
///
/// <para>
/// The shapes a kind accepts are part of what the application is, not a starting suggestion:
/// widening one is how a doline gains an outline, and every test in this suite builds its
/// database from empty, so a seeder that only wrote the new set into fresh installs would leave
/// every check green while every existing install went on refusing the drawing. That is the
/// failure this file exists for, and it needs a database of its own — the shared one is already
/// current, so nothing could be observed on it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TaxonomyWideningTests : IAsyncLifetime
{
    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_taxonomy_{Guid.NewGuid():N}";
    private string connectionString = null!;

    public TaxonomyWideningTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        connectionString =
            new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }
                .ConnectionString;

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await TaxonomySeeder.SeedAsync(db);
    }

    [Fact]
    public async Task A_kind_that_gained_a_shape_gains_it_on_a_database_that_was_already_seeded()
    {
        // The state an installation seeded before the doline outline arrived is in: markers
        // only. Written directly, because there is no path through the application that
        // narrows a kind.
        await using (var before = CreateContext())
        {
            var sinkhole = await before.FeatureTypes.SingleAsync(x => x.Code == "sinkhole");
            sinkhole.AcceptedGeometryClasses = [GeometryClass.Point, GeometryClass.MultiPoint];
            await before.SaveChangesAsync();
        }

        await using (var reseed = CreateContext())
        {
            await TaxonomySeeder.SeedAsync(reseed);
        }

        await using var after = CreateContext();
        var widened = await after.FeatureTypes.SingleAsync(x => x.Code == "sinkhole");
        widened.AcceptedGeometryClasses.ShouldContain(GeometryClass.Polygon);
        widened.AcceptedGeometryClasses.ShouldContain(GeometryClass.MultiPolygon);
        widened.AcceptedGeometryClasses.ShouldContain(GeometryClass.Point);
    }

    [Fact]
    public async Task A_shape_an_installation_added_is_not_taken_away_by_the_next_seeding()
    {
        // Widening only. A class already in use would otherwise start refusing its own stored
        // rows the next time one of them was edited, and the seeding that did it would say
        // nothing.
        await using (var before = CreateContext())
        {
            var narrow = await before.FeatureTypes.SingleAsync(x => x.Code == "peak");
            narrow.AcceptedGeometryClasses =
                [.. narrow.AcceptedGeometryClasses, GeometryClass.LineString];
            await before.SaveChangesAsync();
        }

        await using (var reseed = CreateContext())
        {
            await TaxonomySeeder.SeedAsync(reseed);
        }

        await using var after = CreateContext();
        var peak = await after.FeatureTypes.SingleAsync(x => x.Code == "peak");
        peak.AcceptedGeometryClasses.ShouldContain(GeometryClass.LineString);
        peak.AcceptedGeometryClasses.ShouldContain(GeometryClass.Point);
    }

    private SilexGisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
