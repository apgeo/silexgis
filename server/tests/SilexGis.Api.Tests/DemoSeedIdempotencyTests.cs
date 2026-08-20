// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;
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
    /// A database seeded before a section existed picks that section up on the next run.
    ///
    /// The trap is a section guarded on somebody else's evidence: a block that skips when the
    /// camps are already there never runs again on any installation that has camps, which is
    /// every installation the earlier version was run on — and it fails silently, as an empty
    /// list nobody can distinguish from a broken route. The camp's roster is the section this is
    /// written against, and the state is built by removing its rows from a seeded database, which
    /// is exactly what such an installation looks like.
    /// </summary>
    [Fact]
    public async Task A_database_seeded_before_the_camp_roster_existed_gains_it_on_the_next_run()
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        int seeded;
        await using (var strip = CreateContext())
        {
            seeded = await strip.ExpeditionRoster.CountAsync();
            seeded.ShouldBeGreaterThan(0);
            await strip.Database.ExecuteSqlRawAsync("DELETE FROM expedition_roster");
        }

        await using (var again = CreateContext())
        {
            await DemoSeeder.SeedAsync(again, Owner);
        }

        await using var read = CreateContext();
        (await read.ExpeditionRoster.CountAsync()).ShouldBe(seeded);

        // And it is the numbers the block exists to show: more rows than people, so a surface
        // counting rows where it meant people is visibly wrong against this dataset rather than
        // merely slightly high.
        var rows = await read.ExpeditionRoster.Select(r => r.CaverId).ToListAsync();
        rows.Count.ShouldBe(6);
        rows.Distinct().Count().ShouldBe(4);
    }

    /// <summary>
    /// The same, for the trips a camp gathered — and for the trips themselves.
    ///
    /// A camp holding no trips is the state every surface built over the membership reads as
    /// empty in, and an installation seeded before those trips existed is exactly where that would
    /// be permanent: the trips block used to stop at the first demo trip it found, so a trip added
    /// to the demo later reached no machine that had already run it. The state is built by
    /// removing both the memberships and the trips they point at.
    /// </summary>
    [Fact]
    public async Task A_database_seeded_before_the_camps_trips_existed_gains_them_on_the_next_run()
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        int members;
        await using (var strip = CreateContext())
        {
            members = await strip.ExpeditionTrips.CountAsync();
            members.ShouldBeGreaterThan(0);

            // A camp whose trips were never seeded, on a database that has everything else.
            await strip.Database.ExecuteSqlRawAsync("DELETE FROM expedition_trips");
            await strip.Database.ExecuteSqlRawAsync(
                "DELETE FROM trip_logs WHERE title LIKE 'Demo: camp %'");
        }

        await using (var again = CreateContext())
        {
            await DemoSeeder.SeedAsync(again, Owner);
        }

        await using var read = CreateContext();
        (await read.ExpeditionTrips.CountAsync()).ShouldBe(members);

        // And the trips are back with what makes them worth gathering: the figures the camp's
        // totals are made of, and a sketch for its map to draw.
        var trips = await read.TripLogs
            .Where(t => t.Title.StartsWith("Demo: camp "))
            .Select(t => new { t.Id, t.Geom, t.DepthReachedM })
            .ToListAsync();
        trips.Count.ShouldBe(members);
        trips.ShouldContain(t => t.Geom != null);
        trips.ShouldContain(t => t.DepthReachedM != null);
    }

    /// <summary>
    /// The same, for who was asked on the trip still being planned and what each of them said.
    ///
    /// The one shape this dataset exists to show is a trip with more yeses than places, so that a
    /// surface which ignored the limit shows everybody on a trip that has room for three. A block
    /// that stopped topping up — widened to "any answer on any trip", or gated on the limit
    /// already being set — would leave every installation that ran the earlier seeder showing an
    /// empty list, which is indistinguishable from a broken route. The state is built by removing
    /// the answers and the limit together, because they are one fact about one trip.
    /// </summary>
    [Fact]
    public async Task A_database_seeded_before_the_trips_answers_existed_gains_them_on_the_next_run()
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        int answers;
        await using (var strip = CreateContext())
        {
            answers = await strip.TripInvitations.CountAsync();
            answers.ShouldBeGreaterThan(0);

            await strip.Database.ExecuteSqlRawAsync("DELETE FROM trip_invitations");
            await strip.Database.ExecuteSqlRawAsync(
                "UPDATE trip_logs SET max_participants = NULL");
        }

        await using (var again = CreateContext())
        {
            await DemoSeeder.SeedAsync(again, Owner);
        }

        await using var read = CreateContext();
        (await read.TripInvitations.CountAsync()).ShouldBe(answers);

        // And they are back in the shape the block exists to show: one trip, a stated limit, and
        // more people saying they are coming than there is room for, so somebody is waiting.
        var trip = await read.TripLogs
            .Where(t => t.MaxParticipants != null)
            .Select(t => new { t.Id, t.MaxParticipants })
            .SingleAsync();
        trip.MaxParticipants.ShouldNotBeNull();

        var rows = await read.TripInvitations.Where(x => x.TripLogId == trip.Id).ToListAsync();
        rows.Count(x => x.Response == TripInvitationResponse.Yes)
            .ShouldBeGreaterThan(trip.MaxParticipants!.Value);

        var places = TripAttendance.Rank(rows, trip.MaxParticipants);
        places.Values.ShouldContain(p => !p.Attending);

        // Somebody was picked out of the order, which is the part of the feature a list of yeses
        // in order alone would show as working when it was not.
        rows.ShouldContain(x => x.SelectedAt != null);
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
