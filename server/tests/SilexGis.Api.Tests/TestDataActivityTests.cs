// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a test-data run's people did, as the application stores it: trips written by the trip
/// writer (rosters, party numbers, links to the caves they visited), the survey trips on the days
/// and with the lengths of the surveys, invitations and teams on planned trips, a summer camp with
/// its roster and trips, club events in a series — readable through the trip routes, and removed
/// to the last row.
/// </summary>
public sealed class TestDataActivityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/admin/test-data";

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;

    public TestDataActivityTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tda-admin-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tda-admin-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_run_writes_trips_camps_and_events_as_the_application_keeps_them_and_removal_takes_them_back()
    {
        var profile = new
        {
            seed = 6262,
            regions = new[] { "bihor-vladeasa" },
            caves = new { count = 15 },
            people = new { clubs = 2, organisations = 1, cavers = 20, accounts = 10 },
            timeline = new { trips = 50, expeditionYears = 2 },
        };
        var plan = TestDataPlanner.Plan(TestDataProfile.FromJson(JsonSerializer.Serialize(profile, TestDataProfile.JsonOptions)));
        var start = await admin.PostAsJsonAsync($"{Base}/runs", new { profile, confirmAlongsideExistingData = true });
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted, await start.Content.ReadAsStringAsync());
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var job = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TestDataGenerate))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await QueuedJob.RunAsync(factory.Services, job.Id);
        var run = await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
        run.GetProperty("status").GetString().ShouldBe("succeeded", run.ToString());
        run.GetProperty("counts").GetProperty("tripLogs").GetInt32().ShouldBe(50);

        List<Guid> trips;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listed = await db.TestDataRows.Where(r => r.RunId == runId).ToListAsync();
            trips = listed.Where(r => r.TableName == "trip_logs").Select(r => Guid.Parse(r.RowId)).ToList();
            var stored = await db.TripLogs.IgnoreQueryFilters().Where(t => trips.Contains(t.Id)).ToListAsync();

            // Every planned trip, in the state it reached; the deleted ones in the bin.
            foreach (var planned in plan.Activity.Trips)
            {
                var trip = stored.Single(t => t.Title == planned.Title && t.TripDate == planned.Date);
                trip.State.ShouldBe(planned.State, planned.Title);
                trip.IsDeleted.ShouldBe(planned.Deleted, planned.Title);
                trip.LengthSurveyedM.ShouldBe(planned.LengthSurveyedM);
                (await db.TripLogParticipants.CountAsync(p => p.TripLogId == trip.Id)).ShouldBe(planned.Members.Count + planned.Proposers.Count, planned.Title);
            }

            // The trip writer kept what it keeps: party numbers, and the trips' links to the caves they visited.
            (await db.TripPartyNumbers.CountAsync(n => trips.Contains(n.TripLogId))).ShouldBeGreaterThan(0);
            var links = listed.Where(r => r.TableName == "res_links").Select(r => Guid.Parse(r.RowId)).ToList();
            links.Count.ShouldBe(plan.Activity.Trips.Count);
            (await db.ResLinkMembers.CountAsync(m => links.Contains(m.ResLinkId) && m.FeatureId != null)).ShouldBe(plan.Activity.Trips.Sum(t => t.CaveIndices.Count));

            (await db.TripInvitations.CountAsync(i => i.TripLogId != null && trips.Contains(i.TripLogId.Value))).ShouldBe(plan.Activity.Trips.Sum(t => t.Invitations.Count));
            (await db.TripTeams.CountAsync(t => trips.Contains(t.TripLogId))).ShouldBe(plan.Activity.Trips.Sum(t => t.Teams.Count));

            var camps = listed.Where(r => r.TableName == "expeditions").Select(r => Guid.Parse(r.RowId)).ToList();
            camps.Count.ShouldBe(plan.Activity.Expeditions.Count);
            (await db.ExpeditionRoster.CountAsync(r => camps.Contains(r.ExpeditionId))).ShouldBe(plan.Activity.Expeditions.Sum(e => e.Roster.Count));
            (await db.ExpeditionTrips.CountAsync(e => camps.Contains(e.ExpeditionId))).ShouldBe(plan.Activity.Trips.Count(t => t.ExpeditionIndex != null));

            var events = listed.Where(r => r.TableName == "events").Select(r => Guid.Parse(r.RowId)).ToList();
            events.Count.ShouldBe(plan.Activity.Events.Sum(e => e.Count));
            (await db.Events.Where(e => events.Contains(e.Id) && e.SeriesId != null).Select(e => e.SeriesId).Distinct().CountAsync())
                .ShouldBe(plan.Activity.Events.Count(e => e.Frequency != null));
        }

        // The trips are trips like any other: the list answers them to the administrator.
        var listing = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-logs?pageSize=100");
        listing.GetProperty("items").EnumerateArray().Count(i => trips.Contains(i.GetProperty("id").GetGuid()))
            .ShouldBe(plan.Activity.Trips.Count(t => !t.Deleted));

        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var removal = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TestDataRemove))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await QueuedJob.RunAsync(factory.Services, removal.Id);
        (await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}")).GetProperty("status").GetString().ShouldBe("removed");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await TestDataCoverageSql.MeasureAsync(db, runId, CancellationToken.None)).ShouldAllBe(t => t.Rows == 0);
            (await db.TripLogs.IgnoreQueryFilters().CountAsync(t => trips.Contains(t.Id))).ShouldBe(0);
            (await db.TripTypes.CountAsync(t => t.Code == TestDataActivityPlanner.AddedTripTypeCode)).ShouldBe(0);
        }
    }
}
