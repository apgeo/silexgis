// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
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
public sealed class DemoSeedIdempotencyTests : IAsyncLifetime, IClassFixture<PostgresFixture>
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
                address_point_visibility, created_at, updated_at, email_confirmed,
                phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ('00000000-0000-0000-0000-0000000000d1', 'en', false, false, false,
                    0, 0, 0, 0, 0, 0, 0, now(), now(), true, false, false, true, 0);
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
    /// A row lost from the dataset comes back on the next run, whichever block it belongs to, and
    /// nothing else moves. The guard every block used to carry — stop at the first sign of the
    /// block — made a loss permanent: delete one camp and no run ever wrote it again, and on the
    /// page that read as the feature being empty rather than as the seed being stale.
    /// </summary>
    [Theory]
    [InlineData("features", "DELETE FROM features WHERE name = 'Falia Demo'")]
    [InlineData("features", "DELETE FROM features WHERE name = 'Avenul Demo Vântului entrance'")]
    [InlineData("features", "DELETE FROM features WHERE name = 'Demo: choked crawl'")]
    [InlineData("expeditions", "DELETE FROM expeditions WHERE name = 'Demo: winter recce'")]
    [InlineData("expedition_roster", "DELETE FROM expedition_roster WHERE id = (SELECT id FROM expedition_roster ORDER BY from_date, id LIMIT 1)")]
    [InlineData("map_views", "DELETE FROM map_views WHERE name = 'Demo: Bihor caves'")]
    [InlineData("events", "DELETE FROM events WHERE title = 'Demo: Tuesday rope training' AND start_date = (SELECT MIN(start_date) FROM events WHERE title = 'Demo: Tuesday rope training')")]
    [InlineData("trip_invitations", "DELETE FROM trip_invitations WHERE id = (SELECT id FROM trip_invitations WHERE event_id IS NOT NULL ORDER BY id LIMIT 1)")]
    public async Task A_row_lost_from_the_dataset_is_put_back_on_the_next_run(string table, string loss)
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        long seeded;
        await using (var strip = CreateContext())
        {
            seeded = await CountAsync(strip, table);
            seeded.ShouldBeGreaterThan(0);
            await strip.Database.ExecuteSqlRawAsync(loss);
            (await CountAsync(strip, table)).ShouldBe(seeded - 1);
        }

        await using (var again = CreateContext())
        {
            await DemoSeeder.SeedAsync(again, Owner);
        }

        await using var read = CreateContext();
        (await CountAsync(read, table)).ShouldBe(seeded);

        // And a series row that came back is in its series, not alone beside it.
        if (table == "events")
        {
            (await read.Events.Where(e => e.Title == "Demo: Tuesday rope training")
                .Select(e => e.SeriesId).Distinct().CountAsync()).ShouldBe(1);
        }
    }

    /// <summary>
    /// The dataset shows the surfaces built since the demo was first written: a camp's leads
    /// board has leads, the calendar has club dates, and one of them has answers.
    /// </summary>
    [Fact]
    public async Task The_dataset_has_leads_for_the_camp_board_and_club_dates_with_answers()
    {
        await using (var db = CreateContext())
        {
            await DemoSeeder.SeedAsync(db, Owner);
        }

        await using var read = CreateContext();
        var continuationTypeId = await read.FeatureTypes
            .Where(t => t.Code == FeatureTypeSeeds.Continuation).Select(t => t.Id).SingleAsync();
        (await read.Features.CountAsync(f => f.FeatureTypeId == continuationTypeId && f.Name != null && f.Name.StartsWith("Demo:")))
            .ShouldBe(2);

        (await read.Events.CountAsync(e => e.Title == "Demo: Tuesday rope training")).ShouldBe(6);
        (await read.Events.Where(e => e.Title == "Demo: Tuesday rope training").Select(e => e.SeriesId).Distinct().CountAsync()).ShouldBe(1);
        (await read.Events.CountAsync(e => e.Title == "Demo: autumn general meeting")).ShouldBe(1);
        var gearCheckId = await read.Events.Where(e => e.Title == "Demo: gear check evening").Select(e => e.Id).SingleAsync();
        (await read.TripInvitations.CountAsync(i => i.EventId == gearCheckId)).ShouldBe(4);
    }

    /// <summary>Rows in a table named by this file's own constants — never by anything a caller typed.</summary>
#pragma warning disable EF1003 // the table name is one of this file's literals, not input
    private static Task<long> CountAsync(SilexGisDbContext db, string table) =>
        db.Database.SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM " + table).SingleAsync();
#pragma warning restore EF1003

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
    /// Everybody the demonstration data puts on a trip holds a number in that trip's party, and
    /// seeding again gives nobody a second one.
    /// </summary>
    /// <remarks>
    /// The seeder writes roster rows itself rather than through the service that saves a trip, so
    /// it is one of the paths that has to remember to number its people. A read would not show a
    /// path that forgot — it lists such a person anyway, after the numbered ones — which is why the
    /// rows are counted here.
    /// </remarks>
    [Fact]
    public async Task Everybody_on_a_seeded_trip_holds_one_number_in_its_party()
    {
        for (var run = 0; run < 2; run++)
        {
            await using var db = CreateContext();
            await DemoSeeder.SeedAsync(db, Owner);
        }

        await using var read = CreateContext();
        var tripIds = await read.TripLogs.Where(t => t.Title.StartsWith("Demo:")).Select(t => t.Id).ToListAsync();
        tripIds.ShouldNotBeEmpty();

        var named = await read.TripLogParticipants.Where(p => tripIds.Contains(p.TripLogId))
            .Select(p => new { p.TripLogId, p.CaverId }).Distinct().ToListAsync();
        var numbered = await read.TripPartyNumbers.Where(n => tripIds.Contains(n.TripLogId))
            .Select(n => new { n.TripLogId, n.CaverId, n.Number }).ToListAsync();

        named.Count.ShouldBeGreaterThan(tripIds.Count);
        numbered.Select(n => (n.TripLogId, n.CaverId)).ShouldBe(
            named.Select(p => (p.TripLogId, (Guid?)p.CaverId)), ignoreOrder: true);
        // From one, without a gap, on a trip nobody has left.
        foreach (var trip in numbered.GroupBy(n => n.TripLogId))
        {
            trip.Select(n => n.Number).Order().ShouldBe(Enumerable.Range(1, trip.Count()));
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

    // ---- the trip that was followed underground ----------------------------------------------
    //
    // It needs a survey, a survey is a stored file, and so it is written only by a caller that can
    // keep bytes — which every test above is not. These stand an application up over this class's
    // database to borrow what the seeding command itself is given: the document writer and the
    // file store. Its job workers are taken out, so the reading the seeder queues stays queued
    // until a test runs it by hand, and nothing moves a row between two counts but the seeder.

    /// <summary>What the block lays down, counted: its reports, and the ones that place somebody.</summary>
    private const int SeededReports = 10;

    private const int SeededPlacedReports = 5;

    private const string LoseTheTrackedTrip =
        "DELETE FROM trip_logs WHERE title = '" + DemoSeeder.TrackedTripTitle + "'";

    [Fact]
    public async Task Seeding_twice_where_files_can_be_kept_writes_the_tracked_trip_once()
    {
        using var host = Host();
        await SeedWithFilesAsync(host);

        Dictionary<string, long> afterFirst;
        await using (var counting = CreateContext())
        {
            afterFirst = await RowCountsAsync(counting);

            // What the block is there to write, by number — or "the second run changed nothing"
            // would be true of a block that never ran.
            afterFirst["survey_models"].ShouldBe(1);
            afterFirst["trip_tracking"].ShouldBe(1);
            afterFirst["trip_position_events"].ShouldBe(SeededReports);
            afterFirst["trip_teams"].ShouldBe(1);
            afterFirst["cave_depth_places"].ShouldBe(1);
            (await counting.TripLogs.CountAsync(t => t.Title == DemoSeeder.TrackedTripTitle)).ShouldBe(1);

            // The survey is waiting to be read, by one job, exactly as an uploaded one waits.
            (await counting.SurveyModels.Select(m => m.Status).SingleAsync()).ShouldBe(SurveyModelStatus.Pending);
            (await counting.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.SurveyGraph)).ShouldBe(1);
            afterFirst["survey_stations"].ShouldBe(0);

            // And nobody without an account was handed a page: publishing is a person's act.
            afterFirst["trip_tracking_shares"].ShouldBe(0);
        }

        await SeedWithFilesAsync(host);

        await using var after = CreateContext();
        (await RowCountsAsync(after)).ShouldBe(afterFirst);
    }

    /// <summary>
    /// An installation that seeded before this trip was part of the dataset already holds the
    /// documents and everything else, so a block gated on any of those would never run there. The
    /// state is built the way such an installation looks: seeded whole, then without this block's
    /// rows.
    /// </summary>
    [Fact]
    public async Task A_database_seeded_before_the_tracked_trip_existed_gains_it_on_the_next_run()
    {
        using var host = Host();
        await SeedWithFilesAsync(host);

        await using (var strip = CreateContext())
        {
            (await strip.TripPositionEvents.CountAsync()).ShouldBe(SeededReports);
            await strip.Database.ExecuteSqlRawAsync(LoseTheTrackedTrip);
            await strip.Database.ExecuteSqlRawAsync("DELETE FROM survey_models");
            await strip.Database.ExecuteSqlRawAsync("DELETE FROM cave_depth_places");

            // Gone with their trip, which is what makes this the earlier installation and not
            // one with a trip that merely lost its title.
            (await strip.TripPositionEvents.CountAsync()).ShouldBe(0);
            (await strip.TripTrackings.CountAsync()).ShouldBe(0);
            (await strip.TripTeams.CountAsync()).ShouldBe(0);
        }

        await SeedWithFilesAsync(host);

        await using var read = CreateContext();
        var trip = await read.TripLogs.SingleAsync(t => t.Title == DemoSeeder.TrackedTripTitle);
        var model = await read.SurveyModels.SingleAsync();
        var watch = await read.TripTrackings.SingleAsync();
        watch.TripLogId.ShouldBe(trip.Id);
        watch.State.ShouldBe(TripTrackingState.Closed);
        watch.SurveyModelId.ShouldBe(model.Id);
        (await read.TripPositionEvents.CountAsync(e => e.TripLogId == trip.Id)).ShouldBe(SeededReports);
        (await read.TripPositionEvents.CountAsync(e => e.SurveyModelId == model.Id)).ShouldBe(SeededPlacedReports);
        (await read.CaveDepthPlaces.CountAsync(p => p.CaveFeatureId == model.CaveFeatureId)).ShouldBe(1);

        // Numbered by the rule a real trip is, like every other seeded party.
        (await read.TripPartyNumbers.Where(n => n.TripLogId == trip.Id).Select(n => n.Number).OrderBy(n => n).ToListAsync())
            .ShouldBe([1, 2]);
    }

    /// <summary>
    /// One report lost from the log comes back, the same report and no other; a team or a
    /// declared place lost on its own comes back the same way.
    /// </summary>
    [Theory]
    [InlineData("trip_position_events", "DELETE FROM trip_position_events WHERE id = (SELECT id FROM trip_position_events WHERE kind = 2 LIMIT 1)")]
    [InlineData("trip_position_events", "DELETE FROM trip_position_events WHERE id = (SELECT id FROM trip_position_events WHERE kind = 4 ORDER BY recorded_at LIMIT 1)")]
    [InlineData("cave_depth_places", "DELETE FROM cave_depth_places")]
    [InlineData("trip_tracking", "DELETE FROM trip_tracking")]
    public async Task A_row_lost_from_the_tracked_trip_is_put_back_on_the_next_run(string table, string loss)
    {
        using var host = Host();
        await SeedWithFilesAsync(host);

        Dictionary<string, long> seeded;
        await using (var strip = CreateContext())
        {
            seeded = await RowCountsAsync(strip);
            seeded[table].ShouldBeGreaterThan(0);
            await strip.Database.ExecuteSqlRawAsync(loss);
            (await CountAsync(strip, table)).ShouldBe(seeded[table] - 1);
        }

        await SeedWithFilesAsync(host);

        await using var read = CreateContext();
        var after = await RowCountsAsync(read);

        // The history is where a write says it happened, so it is the one table a repair is
        // allowed — and expected — to add to. Everything else is exactly as it was.
        after.Where(t => t.Key != "audit_log").ShouldBe(seeded.Where(t => t.Key != "audit_log"), ignoreOrder: true);

        // The reports are the ones the dataset names: each person went in once and came out once,
        // and one of them was placed by a depth.
        var kinds = await read.TripPositionEvents.GroupBy(e => e.Kind)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        kinds[TripPositionEventKind.Entered].ShouldBe(2);
        kinds[TripPositionEventKind.Exited].ShouldBe(2);
        kinds[TripPositionEventKind.AtDepth].ShouldBe(1);
        (await read.TripTrackings.Select(t => t.State).SingleAsync()).ShouldBe(TripTrackingState.Closed);
    }

    /// <summary>
    /// The seeded trip, read the two ways an account reads a followed trip: on its tracking tab,
    /// and in a write-up whose layout asks for the journal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader is an ordinary member — no right over the trip or the cave beyond reading them —
    /// because the point of putting the trip on the unprotected cave is that the demonstration
    /// shows places rather than a column of withheld ones, and an administrator would be shown
    /// them wherever the trip was.
    /// </para>
    /// <para>
    /// The survey is then read by the job the seeder queued, with the ordinary reader, and every
    /// station a seeded report or the declared place names is looked up among the stations it
    /// found. The reports were written before any station existed, by name alone, so nothing else
    /// would notice a name that is not in the file: the log would read correctly and the marker
    /// would simply never be drawn.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_seeded_tracked_trip_is_read_on_its_tracking_tab_and_printed_by_a_layout_that_asks_for_the_journal()
    {
        using var host = Host();
        await SeedWithFilesAsync(host);

        await AuthHelper.CreateUserAsync(host, GlobalRoles.Admin, "demo-seed-admin@t.local");
        await AuthHelper.CreateUserAsync(host, GlobalRoles.Viewer, "demo-seed-member@t.local");
        using var admin = await AuthHelper.BearerClientAsync(host, "demo-seed-admin@t.local");
        using var member = await AuthHelper.BearerClientAsync(host, "demo-seed-member@t.local");

        Guid tripId;
        Guid modelId;
        List<string> named;
        await using (var db = CreateContext())
        {
            tripId = await db.TripLogs.Where(t => t.Title == DemoSeeder.TrackedTripTitle).Select(t => t.Id).SingleAsync();
            modelId = await db.SurveyModels.Select(m => m.Id).SingleAsync();
            named = await db.TripPositionEvents.Where(e => e.ViewerStationName != null)
                .Select(e => e.ViewerStationName!).ToListAsync();
            named.AddRange(await db.CaveDepthPlaces.Select(p => p.ViewerStationName).ToListAsync());
            named.Count.ShouldBe(SeededPlacedReports + 1);
        }

        // ---- the tracking tab ---------------------------------------------------------------
        var state = await member.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}/tracking/");
        state.GetProperty("state").GetString().ShouldBe("closed");
        state.GetProperty("surveyModelId").GetGuid().ShouldBe(modelId);
        state.GetProperty("surveyModelMissing").GetBoolean().ShouldBeFalse();
        state.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        state.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        state.GetProperty("teams").GetArrayLength().ShouldBe(1);

        var party = state.GetProperty("participants").EnumerateArray().ToList();
        party.Count.ShouldBe(2);
        party.Select(p => p.GetProperty("ordinal").GetInt32()).Order().ShouldBe([1, 2]);
        party.ShouldAllBe(p => p.GetProperty("lastKind").GetString() == "exited");
        // Where each was last placed is said, to a member: the head of the pit for the one who
        // stayed, its foot for the one who went down.
        party.Select(p => p.GetProperty("stationName").GetString()).Order()
            .ShouldBe(["demo.put.0", "demo.put.3"]);

        var log = await member.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}/tracking/events");
        var rows = log.GetProperty("items").EnumerateArray().ToList();
        rows.Count.ShouldBe(SeededReports);
        rows.Count(r => r.GetProperty("stationName").ValueKind == JsonValueKind.String).ShouldBe(SeededPlacedReports);
        rows.Single(r => r.GetProperty("kind").GetString() == "atDepth")
            .GetProperty("depthEnteredM").GetDecimal().ShouldBe(60m);

        // ---- the write-up -------------------------------------------------------------------
        using var stored = await admin.PostAsJsonAsync("/api/v1/report-templates/", new
        {
            name = "With the journal",
            body = "title: {title}\nheading: Followed underground\ntracking\n",
            isDefault = false,
            kind = "trip",
            tripTypeId = (long?)null,
        });
        var layout = await stored.Content.ReadAsStringAsync();
        stored.StatusCode.ShouldBe(HttpStatusCode.Created, layout);
        var layoutId = JsonDocument.Parse(layout).RootElement.GetProperty("id").GetGuid();

        var journal = await WriteUpWordsAsync(member, $"/api/v1/trip-logs/{tripId}/report?templateId={layoutId}");
        journal.ShouldContain("Followed underground");
        journal.ShouldContain("not a callout record");
        journal.ShouldContain("raised no alarm");
        journal.ShouldContain("2026-08-29 07:10 UTC");
        journal.ShouldContain("Went in");
        journal.ShouldContain("At a station · demo.galerie.3");
        journal.ShouldContain("At a station · demo.put.0");
        journal.ShouldContain("At a depth · demo.put.3, 60 m");
        journal.ShouldContain("Rope checked and holding");
        journal.ShouldContain("Came out");
        journal.ShouldNotContain("place withheld");

        // The layout every installation starts with prints none of it: the journal is something
        // a club's own layout asks for.
        var standard = await WriteUpWordsAsync(member, $"/api/v1/trip-logs/{tripId}/report");
        standard.ShouldContain(DemoSeeder.TrackedTripTitle);
        standard.ShouldNotContain("not a callout record");
        standard.ShouldNotContain("demo.put");

        // ---- the survey, read as an uploaded one is ------------------------------------------
        var queued = (await QueuedJob.OfKindAsync(host.Services, ProcessingJobKinds.SurveyGraph)).ShouldHaveSingleItem();
        await QueuedJob.RunAsync(host.Services, queued.Id);

        await using var read = CreateContext();
        var model = await read.SurveyModels.SingleAsync();
        model.Status.ShouldBe(SurveyModelStatus.Ready, model.ProcessingError);
        model.IsCurrent.ShouldBeTrue();

        var stations = await read.SurveyStations.Where(x => x.SurveyModelId == modelId).ToListAsync();
        stations.Count.ShouldBe(12);
        stations.Count(x => x.Flags.HasFlag(SurveyStationFlags.Entrance)).ShouldBe(1);
        var found = stations.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        named.Where(found.Contains).Count().ShouldBe(named.Count);

        // The way in is where the cave's main entrance is, so the survey is drawn on its cave.
        var entrance = await read.Features
            .Where(f => f.Kind == FeatureKind.CaveEntrance
                && f.Entrance!.CaveFeatureId == model.CaveFeatureId
                && f.Name == "Main entrance")
            .Select(f => f.Geom)
            .SingleAsync();
        stations.Single(x => x.Flags.HasFlag(SurveyStationFlags.Entrance)).Position
            .Distance(entrance!).ShouldBeLessThan(1e-6);

        // The foot of the pit is as far below the way in as the declared place says it is.
        var wayIn = stations.Single(x => x.Flags.HasFlag(SurveyStationFlags.Entrance));
        var foot = stations.Single(x => x.Name == "demo.put.3");
        (wayIn.Position.Z - foot.Position.Z).ShouldBe(60, tolerance: 0.01);
    }

    /// <summary>
    /// An application over this class's database, for the two things the seeder needs handed to
    /// it and for reading the result as an account does. Made inside the test, so it is the
    /// test's own and is disposed with it.
    /// </summary>
    private SilexGisApiFactory Host()
    {
        var filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-demoseed-{Guid.NewGuid():N}");
        return new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    /// <summary>The dataset as the seeding command lays it down: with somewhere to keep bytes.</summary>
    private static async Task SeedWithFilesAsync(SilexGisApiFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await DemoSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
            Owner,
            scope.ServiceProvider.GetRequiredService<DocumentWriteService>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>());
    }

    /// <summary>The words of a downloaded write-up, one paragraph to a line.</summary>
    private static async Task<string> WriteUpWordsAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return string.Join(
            "\n",
            document.MainDocumentPart!.Document!.Body!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
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
