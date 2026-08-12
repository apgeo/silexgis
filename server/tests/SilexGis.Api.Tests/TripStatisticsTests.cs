// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.UserModel;
using NPOI.XSSF.Extractor;
using NPOI.XSSF.UserModel;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The totals a person, a cave and a club add up to. Two properties are pinned here and they pull
/// in opposite directions, which is why each case asserts both halves over one fixture.
///
/// The first is that a total is counted over the trips the caller may read and no further. Two
/// people therefore see different figures for the same person, and that is the design rather than
/// a fault: an unfiltered per-person total is a summary of where a named person has been, and
/// asked repeatedly it reports the existence of rows that are never returned. Every refusal below
/// is paired with the same request succeeding for somebody entitled to it, so a zero is a
/// withholding rather than an empty fixture. The withheld side is always a Viewer — the seeded
/// Editors group reads past visibility by design, so an Editor seeing nothing would prove nothing.
///
/// The second is that the figures are of the things they name. The roster holds one row per person
/// per job, so a person who led a trip and surveyed it is two rows and one person; and a role link
/// names a cave once per link, so a trip that named a cave twice is one trip. Neither mistake fails
/// a build. Both make a club's page report more people underground than were there.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripStatisticsTests : IAsyncLifetime
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;      // Editor — creates everything below
    private HttpClient reader = null!;     // Viewer — reads only what visibility opens to them
    private HttpClient anonymous = null!;
    private string tag = null!;
    private long caveTypeId;
    private long participantRoleId;
    private long surveyorRoleId;

    // Nothing here queues work, so this host does not run the job drain. Every test class shares
    // one PostGIS container and the queue lives in it, so a drain started here would claim a file
    // reading or an import queued by another class — and fail it, because the file sits under that
    // class's own storage root and not under this one's.
    public TripStatisticsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                var worker = services.Single(s => s.ImplementationType == typeof(ProcessingJobWorker));
                services.Remove(worker);
            });

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        var suffix = tag;
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"stat-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"stat-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"stat-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"stat-read-{suffix}@t.local");
        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        participantRoleId = await db.TripParticipantRoles
            .Where(r => r.Code == TripParticipantRoleSeeds.ParticipantCode).Select(r => r.Id).SingleAsync();
        surveyorRoleId = await db.TripParticipantRoles
            .Where(r => r.Code == "surveyor").Select(r => r.Id).SingleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Statistics_are_refused_to_a_caller_who_is_not_signed_in()
    {
        var response = await anonymous.GetAsync($"/api/v1/stats/cavers/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_subject_that_does_not_exist_is_not_answered_for()
    {
        (await owner.GetAsync($"/api/v1/stats/cavers/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/stats/caves/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/stats/caving-groups/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A statistic is a reading of its subject, so it is refused wherever reading the subject is.
    /// There is no right of its own to hold or to lack, which is precisely why this has to be
    /// pinned: the refusal rests entirely on the domain check, and a change that admitted everybody
    /// to it would look like a simplification.
    ///
    /// The default installation grants every signed-in account the right to read people and clubs,
    /// so the entry that does so is parked for the length of the case — otherwise the branch is
    /// unreachable and the test would pass over an endpoint that had stopped checking. A Manager
    /// holds both rights through a group of their own and is answered normally throughout, so the
    /// refusal below is this caller rather than a fixture that saved nothing.
    /// </summary>
    [Fact]
    public async Task The_figures_are_refused_to_a_caller_who_may_not_read_the_subject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"stat-man-{suffix}@t.local");
        using var manager = await AuthHelper.BearerClientAsync(factory, $"stat-man-{suffix}@t.local");

        var clubId = await CreateCavingGroupAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Refused",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(trip);

        // Fixture proof: the reader is answered normally while they still hold the baseline right.
        (await StatsAsync(reader, $"cavers/{anaId}")).GetProperty("trips").GetInt32().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var baselineId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.AllUsersSlug)
            .Select(g => g.Id)
            .SingleAsync();
        var parked = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == baselineId
                && (e.Domain == AccessDomain.Cavers || e.Domain == AccessDomain.CavingGroups))
            .Select(e => new { e.Id, e.Domain, e.Actions, e.Effect, e.ScopeKind })
            .ToListAsync();
        parked.ShouldNotBeEmpty();

        try
        {
            await db.AccessEntries
                .Where(e => parked.Select(p => p.Id).Contains(e.Id))
                .ExecuteDeleteAsync();

            (await reader.GetAsync($"/api/v1/stats/cavers/{anaId}"))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await reader.GetAsync($"/api/v1/stats/caving-groups/{clubId}"))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // The file is refused on the same terms. A saved copy reachable where the page is not
            // would be the export answering what the screen declined to.
            (await reader.GetAsync($"/api/v1/stats/cavers/{anaId}/export"))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await reader.GetAsync($"/api/v1/stats/caving-groups/{clubId}/export"))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // And somebody who holds the rights through a group of their own still is answered,
            // so the refusal is the missing right rather than the endpoint having stopped working.
            (await StatsAsync(manager, $"cavers/{anaId}")).GetProperty("trips").GetInt32().ShouldBe(1);
            (await StatsAsync(manager, $"caving-groups/{clubId}")).GetProperty("trips").GetInt32().ShouldBe(1);
        }
        finally
        {
            // Put the baseline back as it was. Every other class shares this database, so a right
            // borrowed here has to be returned whatever the assertions above did.
            foreach (var entry in parked)
            {
                db.AccessEntries.Add(new SilexGis.Domain.Entities.AccessEntry
                {
                    PermissionGroupId = baselineId,
                    Effect = entry.Effect,
                    Domain = entry.Domain,
                    Actions = entry.Actions,
                    ScopeKind = entry.ScopeKind,
                });
            }

            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The property the whole surface rests on. One person, two trips, one of which the reader may
    /// not see: the same request answers two different numbers, and both of them are right.
    ///
    /// The trip the reader may see is what makes the smaller figure a filter rather than a caller
    /// who simply cannot reach statistics at all, and the owner's larger figure over the same
    /// person is what makes the private trip real rather than a fixture that never saved it.
    /// </summary>
    [Fact]
    public async Task A_persons_total_counts_the_trips_the_caller_may_read_and_no_others()
    {
        var caveId = await CreateCaveAsync();
        var openTrip = await CreateTripAsync(new TripSpec
        {
            Title = "Open",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            CaveIds = [caveId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var caverId = SoleCaverId(openTrip);

        await CreateTripAsync(new TripSpec
        {
            Title = "Private",
            TripDate = "2026-05-20",
            Visibility = "private",
            CaveIds = [caveId],
            People = [new PersonSpec(caverId, participantRoleId)],
        });

        var held = await StatsAsync(owner, $"cavers/{caverId}");
        held.GetProperty("trips").GetInt32().ShouldBe(2);

        var seen = await StatsAsync(reader, $"cavers/{caverId}");
        seen.GetProperty("trips").GetInt32().ShouldBe(1);
        seen.GetProperty("earliestTripDate").GetString().ShouldBe("2026-05-14");

        // And the cave the two trips name is answered the same way from its own side, so the rule
        // belongs to the totals rather than to one endpoint.
        (await StatsAsync(owner, $"caves/{caveId}")).GetProperty("trips").GetInt32().ShouldBe(2);
        (await StatsAsync(reader, $"caves/{caveId}")).GetProperty("trips").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The count that breaks quietly. One person doing two jobs on one trip is two roster rows, and
    /// every figure over people has to reduce them: the club went once with two people, not twice
    /// with three. The third person, holding one job, is what keeps the assertion from passing on a
    /// build that simply lost a row.
    /// </summary>
    [Fact]
    public async Task Somebody_who_did_two_jobs_on_one_trip_is_one_person_who_went_once()
    {
        var clubId = await CreateCavingGroupAsync();
        var caveId = await CreateCaveAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Two hats",
            TripDate = "2026-05-14",
            EntryTime = "09:00",
            ExitTime = "17:00",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            CaveIds = [caveId],
            People = [new PersonSpec("Ana", participantRoleId), new PersonSpec("Bogdan", participantRoleId)],
        });

        var anaId = CaverIdByName(trip, "Ana");
        // Fixture proof: Ana really does hold two jobs on the one trip, so a count of three below
        // would be the roster being counted rather than the people on it.
        await AddRoleAsync(trip, anaId, surveyorRoleId);
        (await RosterRowCountAsync(TripId(trip))).ShouldBe(3);

        var club = await StatsAsync(owner, $"caving-groups/{clubId}");
        club.GetProperty("trips").GetInt32().ShouldBe(1);
        club.GetProperty("people").GetInt32().ShouldBe(2);
        club.GetProperty("personTrips").GetInt32().ShouldBe(2);
        // Two people, eight hours each, and Ana's second job does not send her underground twice.
        club.GetProperty("undergroundMinutes").GetInt32().ShouldBe(960);

        var ana = await StatsAsync(owner, $"cavers/{anaId}");
        ana.GetProperty("trips").GetInt32().ShouldBe(1);
        ana.GetProperty("personTrips").GetInt32().ShouldBe(1);
        ana.GetProperty("undergroundMinutes").GetInt32().ShouldBe(480);

        // The cave the trip named twice is one cave, once — the same reduction on the other side.
        var cave = await StatsAsync(owner, $"caves/{caveId}");
        cave.GetProperty("trips").GetInt32().ShouldBe(1);
        cave.GetProperty("places").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// Hours over a trip that ran across days rather than across one midnight. A rule written when
    /// a trip was a single day turns a thirty-hour push into six, and a thirty-hour push is exactly
    /// the trip somebody wants counted.
    ///
    /// The second person is on the same trip with times of their own, so the case also pins that a
    /// row's times stand for that person and the trip's stand for everybody else.
    /// </summary>
    [Fact]
    public async Task A_trip_that_ran_across_days_counts_the_hours_it_actually_ran()
    {
        var clubId = await CreateCavingGroupAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Long push",
            TripDate = "2026-05-14",
            TripDateEnd = "2026-05-15",
            EntryTime = "09:00",
            ExitTime = "15:00",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People =
            [
                new PersonSpec("Ana", participantRoleId),
                new PersonSpec("Bogdan", participantRoleId) { EntryTime = "22:00", ExitTime = "06:00" },
            ],
        });

        var anaId = CaverIdByName(trip, "Ana");
        var bogdanId = CaverIdByName(trip, "Bogdan");

        // Ana takes the trip's own times: 09:00 on the 14th to 15:00 on the 15th is thirty hours.
        var ana = await StatsAsync(owner, $"cavers/{anaId}");
        ana.GetProperty("undergroundMinutes").GetInt32().ShouldBe(1800);
        ana.GetProperty("timedPersonTrips").GetInt32().ShouldBe(1);

        // Bogdan gave his own, and 22:00 to 06:00 across a range that already says which day it
        // ended is one night, not a night plus the day the range spans.
        (await StatsAsync(owner, $"cavers/{bogdanId}")).GetProperty("undergroundMinutes")
            .GetInt32().ShouldBe(480);

        var club = await StatsAsync(owner, $"caving-groups/{clubId}");
        club.GetProperty("undergroundMinutes").GetInt32().ShouldBe(2280);
        club.GetProperty("personTrips").GetInt32().ShouldBe(2);
        club.GetProperty("timedPersonTrips").GetInt32().ShouldBe(2);

        // A trip nobody timed contributes no hours and says so, rather than contributing a zero
        // that would read as a trip that took no time at all.
        await CreateTripAsync(new TripSpec
        {
            Title = "Untimed",
            TripDate = "2026-06-01",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People = [new PersonSpec(anaId, participantRoleId)],
        });
        var after = await StatsAsync(owner, $"caving-groups/{clubId}");
        after.GetProperty("personTrips").GetInt32().ShouldBe(3);
        after.GetProperty("timedPersonTrips").GetInt32().ShouldBe(2);
        after.GetProperty("undergroundMinutes").GetInt32().ShouldBe(2280);
    }

    /// <summary>
    /// Why a first visit can never be a stored flag. The club's trip is somebody's first time in
    /// the cave right up until an older trip is typed up — which is the ordinary case in a club
    /// digitising thirty years of archive, not an edge one — and at that moment the credit moves
    /// to the older trip and the club's figure has to give it up.
    ///
    /// The person's own figure is asserted on either side of the same edit: they have been to one
    /// cave throughout, so a build that moved the number for everybody rather than for the club
    /// would be caught here rather than looking like the same answer.
    /// </summary>
    [Fact]
    public async Task A_first_visit_moves_to_the_older_trip_when_one_is_entered_afterwards()
    {
        var clubId = await CreateCavingGroupAsync();
        var caveId = await CreateCaveAsync();
        var clubTrip = await CreateTripAsync(new TripSpec
        {
            Title = "Club outing",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            CaveIds = [caveId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(clubTrip);

        var before = await StatsAsync(owner, $"caving-groups/{clubId}");
        before.GetProperty("places").GetInt32().ShouldBe(1);
        before.GetProperty("firstVisits").GetInt32().ShouldBe(1);
        (await StatsAsync(owner, $"cavers/{anaId}")).GetProperty("firstVisits").GetInt32().ShouldBe(1);

        // The same person, the same cave, years earlier, with somebody else entirely.
        await CreateTripAsync(new TripSpec
        {
            Title = "Years earlier",
            TripDate = "2019-03-02",
            Visibility = "authenticated",
            CaveIds = [caveId],
            People = [new PersonSpec(anaId, participantRoleId)],
        });

        var after = await StatsAsync(owner, $"caving-groups/{clubId}");
        after.GetProperty("trips").GetInt32().ShouldBe(1);
        after.GetProperty("places").GetInt32().ShouldBe(1);
        after.GetProperty("firstVisits").GetInt32().ShouldBe(0);

        // The person has still been to exactly one cave, and the first time is still one first
        // time — what moved is which trip it belongs to, not how many there were.
        var ana = await StatsAsync(owner, $"cavers/{anaId}");
        ana.GetProperty("trips").GetInt32().ShouldBe(2);
        ana.GetProperty("places").GetInt32().ShouldBe(1);
        ana.GetProperty("firstVisits").GetInt32().ShouldBe(1);
        ana.GetProperty("earliestTripDate").GetString().ShouldBe("2019-03-02");
    }

    /// <summary>
    /// A first visit is judged against every trip the caller may read, so a reader who cannot see
    /// the older trip is told the club's outing was the first — which is true of the record they
    /// hold, and is the same shape of answer every other figure here gives. The owner, over the
    /// same two trips, is told otherwise.
    /// </summary>
    [Fact]
    public async Task A_first_visit_is_judged_against_the_trips_the_caller_can_see()
    {
        var clubId = await CreateCavingGroupAsync();
        var caveId = await CreateCaveAsync();
        var clubTrip = await CreateTripAsync(new TripSpec
        {
            Title = "Club outing",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            CaveIds = [caveId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(clubTrip);

        await CreateTripAsync(new TripSpec
        {
            Title = "Years earlier, kept private",
            TripDate = "2019-03-02",
            Visibility = "private",
            CaveIds = [caveId],
            People = [new PersonSpec(anaId, participantRoleId)],
        });

        (await StatsAsync(owner, $"caving-groups/{clubId}")).GetProperty("firstVisits").GetInt32().ShouldBe(0);
        (await StatsAsync(reader, $"caving-groups/{clubId}")).GetProperty("firstVisits").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// A cave whose position is kept from this caller is answered with nothing, exactly as the list
    /// of its trips is: the trips carry their own geometry, so a count beside the cave's name
    /// places it, and a number can be asked for again and again and compared. The same caller is
    /// answered normally about a cave nobody guards, and the owner is answered normally about the
    /// guarded one — so the zeros are this caller and this cave, not a broken query.
    /// </summary>
    [Fact]
    public async Task A_cave_the_caller_may_not_place_is_counted_for_nothing()
    {
        var guardedId = await CreateCaveAsync(locationProtected: true);
        var openId = await CreateCaveAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Both caves",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            CaveIds = [guardedId, openId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(trip);

        // Fixture proof: the reader really can read the guarded cave, and is told it is only
        // approximately placed for them.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{guardedId}"));
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        (await StatsAsync(reader, $"caves/{guardedId}")).GetProperty("trips").GetInt32().ShouldBe(0);
        (await StatsAsync(owner, $"caves/{guardedId}")).GetProperty("trips").GetInt32().ShouldBe(1);
        (await StatsAsync(reader, $"caves/{openId}")).GetProperty("trips").GetInt32().ShouldBe(1);

        // And the person's page counts the places it can account for: the reader is shown the one
        // cave whose link the trip hands them, so a count of two would be a number their own trip
        // page contradicts.
        (await StatsAsync(reader, $"cavers/{anaId}")).GetProperty("places").GetInt32().ShouldBe(1);
        (await StatsAsync(owner, $"cavers/{anaId}")).GetProperty("places").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// A place is a cave. A trip role names anything that can be linked, and a club that records
    /// the work area it was in names one on every trip — so counting whatever a role happens to
    /// point at reports two places and two first visits for one first visit to one cave, and
    /// contradicts the trip's own page, which lists caves and nothing else.
    ///
    /// The cave on the same trip is what keeps this from passing on a build that simply counted
    /// nothing: the figure has to be one, not zero.
    /// </summary>
    [Fact]
    public async Task An_area_a_trip_worked_in_is_not_one_of_the_places_it_reached()
    {
        var caveId = await CreateCaveAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Area and cave",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            CaveIds = [caveId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(trip);

        var areaId = await CreateGenericFeatureAsync();
        await NameFeatureAsync(TripId(trip), areaId, "trip-work-area");

        // Fixture proof: the work area really is named by one of the trip's roles, so a figure of
        // one below is the kind being narrowed rather than the link never having been written.
        (await NamedFeatureCountAsync(TripId(trip))).ShouldBe(2);

        var ana = await StatsAsync(owner, $"cavers/{anaId}");
        ana.GetProperty("places").GetInt32().ShouldBe(1);
        ana.GetProperty("firstVisits").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// A cave's figures are about that cave. One trip into two caves is a perfectly ordinary day,
    /// and counting every place its trips reached would print "two places, two first visits" under
    /// the name of one of them — a page stating something true of the trip and false of its subject.
    ///
    /// The person's page over the same fixture still counts two, which is what says this is the
    /// cave subject being narrowed rather than the place count having been broken for everybody.
    /// </summary>
    [Fact]
    public async Task A_caves_figures_are_about_that_cave_and_not_the_others_on_the_same_trip()
    {
        var firstId = await CreateCaveAsync();
        var secondId = await CreateCaveAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Two caves in a day",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            CaveIds = [firstId, secondId],
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        var anaId = SoleCaverId(trip);

        var cave = await StatsAsync(owner, $"caves/{firstId}");
        cave.GetProperty("trips").GetInt32().ShouldBe(1);
        cave.GetProperty("places").GetInt32().ShouldBe(1);
        cave.GetProperty("firstVisits").GetInt32().ShouldBe(1);

        // The other cave answers for itself in the same terms, so neither is being counted for the
        // other and neither has simply been dropped.
        (await StatsAsync(owner, $"caves/{secondId}")).GetProperty("places").GetInt32().ShouldBe(1);

        // And Ana reached two places on the one trip, which is the figure her page is for.
        var ana = await StatsAsync(owner, $"cavers/{anaId}");
        ana.GetProperty("places").GetInt32().ShouldBe(2);
        ana.GetProperty("firstVisits").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// The saved file withholds what the page withholds. How many people were on somebody's trips
    /// is a fact about their company rather than about them, and the screen leaves it off a
    /// person's own panel — a file that states it anyway is the copy that outlives the page and
    /// gets forwarded, which is exactly the wrong one to be more generous.
    /// </summary>
    [Fact]
    public async Task A_persons_saved_file_withholds_the_figure_their_own_page_does()
    {
        var clubId = await CreateCavingGroupAsync();
        var trip = await CreateTripAsync(new TripSpec
        {
            Title = "Company",
            TripDate = "2026-06-05",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People = [new PersonSpec("Ana", participantRoleId), new PersonSpec("Bogdan", participantRoleId)],
        });
        var anaId = CaverIdByName(trip, "Ana");

        var person = await SheetAsync(owner, $"cavers/{anaId}");
        person.ShouldContainKey("Trips");
        person.ShouldNotContainKey("People");

        // The club's own sheet states it, so the absence above is this subject rather than a row
        // that stopped being written at all.
        (await SheetAsync(owner, $"caving-groups/{clubId}"))["People"].ShouldBe(2d);
    }

    /// <summary>
    /// The measured facts a trip records add up over the trips the caller may read, and a trip
    /// recording none of them contributes nothing rather than dragging the total anywhere.
    /// </summary>
    [Fact]
    public async Task The_measured_facts_of_a_trip_add_up_over_what_the_caller_may_read()
    {
        var clubId = await CreateCavingGroupAsync();
        await CreateTripAsync(new TripSpec
        {
            Title = "Surveying",
            TripDate = "2026-05-14",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            LengthSurveyedM = 412.5m,
            RopeMetres = 60m,
            SurveyStations = 47,
            HadIncident = true,
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        await CreateTripAsync(new TripSpec
        {
            Title = "Nothing measured",
            TripDate = "2026-05-16",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People = [new PersonSpec("Bogdan", participantRoleId)],
        });
        await CreateTripAsync(new TripSpec
        {
            Title = "Kept private",
            TripDate = "2026-05-18",
            Visibility = "private",
            OrganizingCavingGroupId = clubId,
            LengthSurveyedM = 1000m,
            People = [new PersonSpec("Ana", participantRoleId)],
        });

        var held = await StatsAsync(owner, $"caving-groups/{clubId}");
        held.GetProperty("trips").GetInt32().ShouldBe(3);
        held.GetProperty("lengthSurveyedM").GetDecimal().ShouldBe(1412.5m);
        held.GetProperty("incidents").GetInt32().ShouldBe(1);
        held.GetProperty("latestTripDate").GetString().ShouldBe("2026-05-18");

        var seen = await StatsAsync(reader, $"caving-groups/{clubId}");
        seen.GetProperty("trips").GetInt32().ShouldBe(2);
        seen.GetProperty("lengthSurveyedM").GetDecimal().ShouldBe(412.5m);
        seen.GetProperty("ropeMetresM").GetDecimal().ShouldBe(60m);
        seen.GetProperty("surveyStations").GetInt32().ShouldBe(47);
        seen.GetProperty("incidents").GetInt32().ShouldBe(1);
        seen.GetProperty("latestTripDate").GetString().ShouldBe("2026-05-16");
    }

    [Fact]
    public async Task The_saved_file_states_the_same_figures_the_screen_does_for_the_same_caller()
    {
        var clubId = await CreateCavingGroupAsync();
        await CreateTripAsync(new TripSpec
        {
            Title = "Saved openly",
            TripDate = "2026-06-01",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            LengthSurveyedM = 120.5m,
            People = [new PersonSpec("Ana", participantRoleId)],
        });
        await CreateTripAsync(new TripSpec
        {
            Title = "Saved privately",
            TripDate = "2026-06-02",
            Visibility = "private",
            OrganizingCavingGroupId = clubId,
            LengthSurveyedM = 900m,
            People = [new PersonSpec("Bogdan", participantRoleId)],
        });

        var held = await SheetAsync(owner, $"caving-groups/{clubId}");
        held["Trips"].ShouldBe(2d);
        held["Metres surveyed"].ShouldBe(1020.5d);

        // Same file, same route, a caller who may read one of the two trips. The withheld half is
        // absent from the file exactly as it is from the page, and nothing in the file counts it.
        var seen = await SheetAsync(reader, $"caving-groups/{clubId}");
        seen["Trips"].ShouldBe(1d);
        seen["Metres surveyed"].ShouldBe(120.5d);

        var onScreen = await StatsAsync(reader, $"caving-groups/{clubId}");
        onScreen.GetProperty("trips").GetInt32().ShouldBe((int)seen["Trips"]);
        onScreen.GetProperty("lengthSurveyedM").GetDecimal().ShouldBe((decimal)seen["Metres surveyed"]);
    }

    [Fact]
    public async Task The_saved_file_says_whose_figures_it_holds()
    {
        var clubId = await CreateCavingGroupAsync();
        await CreateTripAsync(new TripSpec
        {
            Title = "Stated",
            TripDate = "2026-06-03",
            Visibility = "authenticated",
            OrganizingCavingGroupId = clubId,
            People = [new PersonSpec("Ana", participantRoleId)],
        });

        var response = await owner.GetAsync($"/api/v1/stats/caving-groups/{clubId}/export");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName
            .ShouldNotBeNull()
            .ShouldContain("trip-statistics-caving-group");

        var text = await SheetTextAsync(response);
        text.ShouldContain("Counted over the trips you may read");
    }

    [Fact]
    public async Task The_saved_file_is_refused_wherever_the_figures_are()
    {
        (await anonymous.GetAsync($"/api/v1/stats/cavers/{Guid.NewGuid()}/export"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync($"/api/v1/stats/caving-groups/{Guid.NewGuid()}/export"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/stats/caves/{Guid.NewGuid()}/export"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    /// <summary>The figure rows of a saved workbook, by the label written beside them.</summary>
    private static async Task<Dictionary<string, double>> SheetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync($"/api/v1/stats/{path}/export");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XSSFWorkbook(stream);
        var sheet = workbook.GetSheetAt(0);
        var figures = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var r = 0; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row?.GetCell(0) is { CellType: CellType.String } label
                && row.GetCell(1) is { CellType: CellType.Numeric } value)
            {
                figures[label.StringCellValue] = value.NumericCellValue;
            }
        }

        return figures;
    }

    private static async Task<string> SheetTextAsync(HttpResponseMessage response)
    {
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XSSFWorkbook(stream);
        return new XSSFExcelExtractor(workbook).Text;
    }

    private sealed class TripSpec
    {
        public required string Title { get; init; }
        public required string TripDate { get; init; }
        public string? TripDateEnd { get; init; }
        public string? EntryTime { get; init; }
        public string? ExitTime { get; init; }
        public required string Visibility { get; init; }
        public Guid? OrganizingCavingGroupId { get; init; }
        public Guid[] CaveIds { get; init; } = [];
        public PersonSpec[] People { get; init; } = [];
        public decimal? LengthSurveyedM { get; init; }
        public decimal? RopeMetres { get; init; }
        public int? SurveyStations { get; init; }
        public bool HadIncident { get; init; }
    }

    private sealed class PersonSpec
    {
        public PersonSpec(string newName, long roleId)
        {
            NewCaverName = newName;
            RoleId = roleId;
        }

        public PersonSpec(Guid caverId, long roleId)
        {
            CaverId = caverId;
            RoleId = roleId;
        }

        public Guid? CaverId { get; }

        public string? NewCaverName { get; }

        public long RoleId { get; }

        public string? EntryTime { get; init; }

        public string? ExitTime { get; init; }
    }

    private async Task<JsonElement> CreateTripAsync(TripSpec spec)
    {
        var body = new
        {
            title = $"{spec.Title} {Guid.NewGuid():N}",
            tripDate = spec.TripDate,
            tripDateEnd = spec.TripDateEnd,
            entryTime = spec.EntryTime,
            exitTime = spec.ExitTime,
            organizingCavingGroupId = spec.OrganizingCavingGroupId,
            caveIds = spec.CaveIds,
            participants = spec.People.Select(p => new
            {
                caverId = p.CaverId,
                // Woven with this instance's own token: the whole suite shares one database, and a
                // person's name resolves to whoever already holds it — a plain "Ana" would join
                // another class's Ana and be credited with her trips.
                newCaverName = p.NewCaverName is null ? null : $"{p.NewCaverName} {tag}",
                roleId = p.RoleId,
                entryTime = p.EntryTime,
                exitTime = p.ExitTime,
                note = (string?)null,
            }).ToArray(),
            visibility = spec.Visibility,
            lengthSurveyedM = spec.LengthSurveyedM,
            ropeMetres = spec.RopeMetres,
            surveyStations = spec.SurveyStations,
            hadIncident = spec.HadIncident,
        };

        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>
    /// Gives somebody a second job on a trip they are already on, which is the roster shape the
    /// counts here have to survive. Written straight into the roster rather than through the write
    /// path, so the fixture cannot be quietly reshaped by a change to how a roster is reconciled.
    /// </summary>
    private async Task AddRoleAsync(JsonElement trip, Guid caverId, long roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripLogParticipants.Add(new SilexGis.Domain.Entities.TripLogParticipant
        {
            TripLogId = TripId(trip),
            CaverId = caverId,
            RoleId = roleId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A feature that is not a cave, for the trip roles that name one. Written straight in, because
    /// what it is matters here and nothing else about it does.
    /// </summary>
    private async Task<Guid> CreateGenericFeatureAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var ownerId = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync();
        var id = Guid.NewGuid();
        db.Features.Add(new SilexGis.Domain.Entities.Feature
        {
            Id = id,
            Name = $"Area {Guid.NewGuid():N}"[..30],
            Kind = SilexGis.Domain.Entities.FeatureKind.Generic,
            FeatureTypeId = typeId,
            OwnerUserId = ownerId,
            Visibility = Visibility.Public,
            AncestorIds = [id],
        });
        db.FeatureAncestors.Add(new SilexGis.Domain.Entities.FeatureAncestor
        {
            FeatureId = id,
            AncestorId = id,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Has one of the trip's roles name a feature, as the role fields do.</summary>
    private async Task NameFeatureAsync(Guid tripId, Guid featureId, string roleCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await TripRoleLinks.NameFeatureAsync(db, tripId, featureId, roleCode, null, default))
            .ShouldBeTrue();
        await db.SaveChangesAsync();
    }

    /// <summary>How many features the trip's roles name between them, of whatever kind.</summary>
    private async Task<int> NamedFeatureCountAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await TripRoleLinks.FeatureIdsNamedBy(db, tripId).Distinct().CountAsync();
    }

    private async Task<int> RosterRowCountAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.CountAsync(p => p.TripLogId == tripId);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Stat {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCavingGroupAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = $"Club {Guid.NewGuid():N}"[..30],
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static Guid TripId(JsonElement trip) => trip.GetProperty("id").GetGuid();

    private static Guid SoleCaverId(JsonElement trip) =>
        trip.GetProperty("participants").EnumerateArray().Single().GetProperty("caverId").GetGuid();

    private static Guid CaverIdByName(JsonElement trip, string name) =>
        trip.GetProperty("participants").EnumerateArray()
            .First(p => p.GetProperty("name").GetString()!.Contains(name, StringComparison.Ordinal))
            .GetProperty("caverId").GetGuid();

    private static async Task<JsonElement> StatsAsync(HttpClient client, string path) =>
        await ReadJsonAsync(await client.GetAsync($"/api/v1/stats/{path}"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }
}
