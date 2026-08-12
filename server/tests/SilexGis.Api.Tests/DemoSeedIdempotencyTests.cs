// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The demonstration dataset can be laid down twice without doubling.
///
/// It is offered as a command anyone can run against an installation they already seeded, and
/// each of its sections guards itself so that a re-run tops up what a later version added
/// rather than starting over. A guard that stops guarding is invisible: the second run simply
/// writes a second copy of everything it covers, and the only symptom is a dataset that reads
/// oddly. So the check here is not "the trips are still five" — it counts <em>every</em> table
/// in the schema either side of the second run and demands that none of them moved. A section
/// nobody thought about is covered by that as surely as the one being changed.
///
/// It runs on a database of its own so the dataset does not sit underneath other tests, whose
/// counts are their own business.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DemoSeedIdempotencyTests : IAsyncLifetime
{
    private static readonly Guid Owner = new("00000000-0000-0000-0000-0000000000d1");

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_demo_{Guid.NewGuid():N}";
    private string connectionString = null!;

    public DemoSeedIdempotencyTests(PostgresFixture postgres) =>
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

        // The account the dataset is owned by. Written as SQL because the identity rows belong
        // to a layer no feature slice may reach into, and this needs one row, not a sign-up.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (
                id, locale, two_factor_authenticator_enabled, two_factor_email_enabled,
                two_factor_sms_enabled, real_name_visibility, bio_visibility, email_visibility,
                phone_visibility, caving_club_visibility, address_visibility,
                address_point_visibility, notify_digest, created_at, updated_at, email_confirmed,
                phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ('00000000-0000-0000-0000-0000000000d1', 'en', false, false, false,
                    0, 0, 0, 0, 0, 0, 0, 0, now(), now(), true, false, false, true, 0);
            """);
    }

    [Fact]
    public async Task Seeding_the_demonstration_dataset_twice_writes_it_once()
    {
        await using (var first = CreateContext())
        {
            await DemoSeeder.SeedAsync(first, Owner);
        }

        Dictionary<string, long> afterFirst;
        await using (var counting = CreateContext())
        {
            afterFirst = await RowCountsAsync(counting);

            // The run has to have done something, or "nothing changed the second time" would be
            // true of a seeder that does nothing at all.
            afterFirst["caves"].ShouldBeGreaterThan(0);
            afterFirst["trip_logs"].ShouldBeGreaterThan(0);
        }

        await using (var second = CreateContext())
        {
            await DemoSeeder.SeedAsync(second, Owner);
        }

        await using var after = CreateContext();
        (await RowCountsAsync(after)).ShouldBe(afterFirst);
    }

    /// <summary>
    /// Which caves the seeded trips are about, and what they did there, now that the pairing is
    /// a link like any other. Several roles rather than one: a reader that answers over all of
    /// them cannot be told apart from one that only ever looks at the first unless the data has
    /// more than one in it, so the dataset itself is what keeps that check honest.
    /// </summary>
    [Fact]
    public async Task The_seeded_trips_name_their_caves_through_more_than_one_role()
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        await using var read = CreateContext();
        var tripIds = await read.TripLogs.Where(t => t.Title.StartsWith("Demo:")).Select(t => t.Id).ToListAsync();
        tripIds.ShouldNotBeEmpty();

        var roleCodes = await (
            from member in read.ResLinkMembers
            where member.EntityType == AttachedEntityType.TripLog
                && member.EntityId != null && tripIds.Contains(member.EntityId.Value)
            join link in read.ResLinks on member.ResLinkId equals link.Id
            join role in read.ResLinkRelationTypes on link.RelationTypeId equals role.Id
            select role.Code).Distinct().ToListAsync();

        roleCodes.Count.ShouldBeGreaterThan(1);
        roleCodes.ShouldAllBe(code => ResLinkRelationTypeSeeds.TripRoleCodes.Contains(code));

        // Every one of them is a trip naming a feature, not a trip on its own: a link holding
        // only the trip would show in the demonstration as an association with nothing at
        // the other end.
        foreach (var tripId in tripIds)
        {
            (await TripRoleLinks.FeatureIdsNamedBy(read, tripId).Distinct().ToListAsync())
                .ShouldNotBeEmpty();
        }
    }

    /// <summary>
    /// Every table the schema has, by name, with its row count. Read from the catalogue rather
    /// than from a list somebody maintains, so a table added tomorrow is covered without anyone
    /// remembering to add it here.
    /// </summary>
    private static async Task<Dictionary<string, long>> RowCountsAsync(SilexGisDbContext db)
    {
        var names = new List<string>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using (var list = new NpgsqlCommand(
                """
                SELECT table_name FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """, connection))
            await using (var reader = await list.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
            }

            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                // The name comes from the catalogue, not from a caller, and is quoted; a count
                // cannot be expressed as a parameter.
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{name}\"", connection);
                counts[name] = (long)(await count.ExecuteScalarAsync())!;
            }

            return counts;
        }
        finally
        {
            await connection.CloseAsync();
        }
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
