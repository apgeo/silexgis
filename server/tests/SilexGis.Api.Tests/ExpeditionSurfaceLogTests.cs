// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A camp's surface log: who is underground on the camp's trips, in one answer, with no place in
/// it.
/// </summary>
/// <remarks>
/// <para>
/// Every test that asserts something is absent builds the state that would show it and asserts
/// the showing in the same test — the trip one reader does not get beside the reader who does,
/// the closed watch one second before it leaves beside the instant it has left. An absence proves
/// nothing alone: a route that listed nothing would pass all of them.
/// </para>
/// <para>
/// The reader is a plain Viewer, on purpose. The seeded Editors read past a row's visibility, so
/// an Editor who "cannot see" a trip would prove nothing; and a Viewer holds no exact-location
/// right over anybody's cave, which makes them the reader the protection test needs.
/// </para>
/// </remarks>
public sealed class ExpeditionSurfaceLogTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private string ownerEmail = null!;
    private string readerEmail = null!;
    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public ExpeditionSurfaceLogTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        // Workers off: the survey file below is four bytes, and a worker reading it would fail
        // the model and rewrite the stations seeded beside it.
        factory = new SilexGisApiFactory(connectionString, HostSettings(), JobWorkers.RemoveFrom);
    }

    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"slog-own-{suffix}@t.local";
        readerEmail = $"slog-read-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, readerEmail);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);
        reader = await AuthHelper.BearerClientAsync(factory, readerEmail);
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- who may ask ---------------------------------------------------------------------

    [Fact]
    public async Task The_log_takes_an_account()
    {
        var camp = await CampAsync(owner, "Needs an account");

        (await anonymous.GetAsync(Log(camp))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.GetAsync(Log(camp))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_camp_the_caller_may_not_read_answers_as_a_camp_that_is_not_there()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "Private camp", visibility: "private");
        var (trip, _) = await TripAsync(owner, "In a private camp", guests: 1);
        await JoinAsync(owner, camp, trip);
        await ArmAsync(owner, trip, model);

        // A Viewer holds nothing over a private camp, so this is a camp they genuinely may not
        // read — although the trip in it is one they may, which is the case worth refusing: the
        // camp's own rule is asked first and a readable trip does not open the camp.
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await reader.GetAsync(Log(camp));
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        var missing = await owner.GetAsync(Log(Guid.NewGuid()));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        // The positive half: the very same camp answers, with its trip, for somebody who may
        // read it.
        TripIds(await LogAsync(owner, camp)).ShouldBe([trip]);
    }

    // ---- which trips are on it -----------------------------------------------------------

    /// <summary>
    /// The four ways a trip is not on a camp's log, each beside the answer that shows the trip
    /// was there to be listed: not readable by this caller, in another camp, in no camp, never
    /// followed.
    /// </summary>
    [Fact]
    public async Task Only_the_camps_own_followed_trips_that_the_caller_may_read_are_listed()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "The camp");
        var otherCamp = await CampAsync(owner, "Another camp");

        var (open, _) = await TripAsync(owner, "Open", guests: 1);
        var (hidden, _) = await TripAsync(owner, "Hidden", guests: 1, visibility: "private");
        var (elsewhere, _) = await TripAsync(owner, "Elsewhere", guests: 1);
        var (loose, _) = await TripAsync(owner, "In no camp", guests: 1);
        var (unfollowed, _) = await TripAsync(owner, "Never followed", guests: 1);

        await JoinAsync(owner, camp, open);
        await JoinAsync(owner, camp, hidden);
        await JoinAsync(owner, camp, unfollowed);
        await JoinAsync(owner, otherCamp, elsewhere);
        foreach (var trip in new[] { open, hidden, elsewhere, loose })
        {
            await ArmAsync(owner, trip, model);
        }

        // The reader may read the camp. The private trip is not theirs, and it is simply not
        // there — no row, no gap, no count.
        var theirs = await LogAsync(reader, camp);
        TripIds(theirs).ShouldBe([open]);
        theirs.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // The same camp, asked by somebody who may read the private trip: it is there.
        TripIds(await LogAsync(owner, camp)).ShouldBe([open, hidden], ignoreOrder: true);

        // The trip of the other camp is on the other camp's log, for the same reader — so it is
        // absent above because of the camp it is in, not because nothing lists it.
        TripIds(await LogAsync(reader, otherCamp)).ShouldBe([elsewhere]);

        // The never-followed trip is a member the reader can see in the camp's own trip list,
        // and it joins the log the moment its watch is armed.
        var members = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/trip-logs?expeditionId={camp}&page=1&pageSize=50");
        members.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())
            .ShouldContain(unfollowed);
        await ArmAsync(owner, unfollowed, model);
        TripIds(await LogAsync(reader, camp)).ShouldBe([open, unfollowed], ignoreOrder: true);
    }

    /// <summary>
    /// A closed watch stays on the log for the configured window and leaves at the instant it
    /// ends — asked to the second on a clock the test holds, with a running watch beside it that
    /// never leaves.
    /// </summary>
    /// <remarks>
    /// The window is two minutes because the clock may only move by minutes: the client is signed
    /// in while this host's clock and the machine's still agree, and a session does not outlive
    /// a large jump.
    /// </remarks>
    [Fact]
    public async Task A_closed_watch_leaves_the_log_when_the_window_has_passed_and_a_running_one_never_does()
    {
        var window = TimeSpan.FromMinutes(2);
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var settings = HostSettings();
        settings["ExpeditionSurfaceLog:RecentlyClosed"] = window.ToString("c");
        using var host = new SilexGisApiFactory(connectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });
        using var coordinator = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(coordinator, "On a held clock");
        var (finished, _) = await TripAsync(coordinator, "Came out", guests: 1);
        var (running, _) = await TripAsync(coordinator, "Still in", guests: 1);
        await JoinAsync(coordinator, camp, finished);
        await JoinAsync(coordinator, camp, running);
        await ArmAsync(coordinator, finished, model);
        await ArmAsync(coordinator, running, model);

        clock.Now += TimeSpan.FromSeconds(30);
        (await PutConfigAsync(coordinator, finished, new { state = "closed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Just closed: listed, and saying so. The closing instant is read back from the answer
        // so that the edges below are the row's own and not this test's idea of them.
        var justClosed = Row(await LogAsync(coordinator, camp), finished);
        justClosed.GetProperty("state").GetString().ShouldBe("closed");
        var closedAt = justClosed.GetProperty("closedAt").GetDateTimeOffset();
        closedAt.ShouldBe(clock.Now, TimeSpan.FromMilliseconds(1));

        // The last second of the window: both trips, the running one first.
        clock.Now = closedAt + window - TimeSpan.FromSeconds(1);
        TripIds(await LogAsync(coordinator, camp)).ShouldBe([running, finished]);

        // The instant it ends: the finished trip has left and the running one has not.
        clock.Now = closedAt + window;
        TripIds(await LogAsync(coordinator, camp)).ShouldBe([running]);

        // And a running watch does not age off: far past any window, still there.
        clock.Now = closedAt + window + window + window;
        var later = await LogAsync(coordinator, camp);
        TripIds(later).ShouldBe([running]);
        Row(later, running).GetProperty("state").GetString().ShouldBe("armed");
    }

    // ---- what a row says -----------------------------------------------------------------

    /// <summary>
    /// The count: in, out and never heard, by the rule the trip's own watch counts by — a note
    /// is word from somebody and still places them nowhere.
    /// </summary>
    [Fact]
    public async Task A_row_counts_its_party_in_out_and_never_heard_and_a_note_alone_places_nobody()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "Counting");
        var (trip, party) = await TripAsync(owner, "Four people", guests: 4);
        await JoinAsync(owner, camp, trip);
        await ArmAsync(owner, trip, model);
        var plannedOut = new DateTimeOffset(2026, 9, 12, 19, 30, 0, TimeSpan.Zero);
        await SetExpectedReturnAsync(trip, plannedOut);

        var (inside, backOut, noteOnly, silent) = (party[0], party[1], party[2], party[3]);
        var t0 = Whole(DateTimeOffset.UtcNow.AddHours(-3));
        await ReportAsync(owner, trip, new { caverIds = new[] { inside.Id, backOut.Id }, kind = "entered" }, t0);
        await ReportAsync(owner, trip, new { caverIds = new[] { backOut.Id }, kind = "exited" }, t0.AddMinutes(50));
        await ReportAsync(owner, trip, new { caverIds = new[] { noteOnly.Id }, kind = "note", note = "at the tents" }, t0.AddMinutes(60));
        // A note after an entry is word from them and leaves them inside.
        await ReportAsync(owner, trip, new { caverIds = new[] { inside.Id }, kind = "note", note = "all well" }, t0.AddMinutes(70));

        var row = Row(await LogAsync(reader, camp), trip);
        row.GetProperty("state").GetString().ShouldBe("armed");
        row.GetProperty("armedAt").ValueKind.ShouldBe(JsonValueKind.String);
        row.GetProperty("closedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
        row.GetProperty("title").GetString()!.ShouldStartWith("Four people");
        // The hour the party planned to be out by, as a time and nothing else.
        row.GetProperty("expectedReturnAt").GetDateTimeOffset().ShouldBe(plannedOut);

        row.GetProperty("underground").GetInt32().ShouldBe(1);
        row.GetProperty("out").GetInt32().ShouldBe(1);
        row.GetProperty("unheard").GetInt32().ShouldBe(2);
        row.GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(70));

        var people = row.GetProperty("party").EnumerateArray()
            .ToDictionary(p => p.GetProperty("caverId").GetGuid());
        people.Count.ShouldBe(4);
        Standing(people[inside.Id]).ShouldBe((true, false));
        people[inside.Id].GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(70));
        Standing(people[backOut.Id]).ShouldBe((false, true));
        people[backOut.Id].GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(50));
        // The note is when they were last heard, and it puts them neither in nor out.
        Standing(people[noteOnly.Id]).ShouldBe((false, false));
        people[noteOnly.Id].GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(60));
        Standing(people[silent.Id]).ShouldBe((false, false));
        people[silent.Id].GetProperty("lastRecordedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Names are the ones the trip's own page gives the same reader.
        var page = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{trip}");
        var named = page.GetProperty("participants").EnumerateArray()
            .ToDictionary(p => p.GetProperty("caverId").GetGuid(), p => p.GetProperty("name").GetString());
        foreach (var (caverId, person) in people)
        {
            person.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
            person.GetProperty("name").GetString().ShouldBe(named[caverId]);
        }

        // And the two screens agree on who is inside: the trip's own watch, for the same reader.
        var watch = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{trip}/tracking");
        foreach (var participant in watch.GetProperty("participants").EnumerateArray())
        {
            Standing(people[participant.GetProperty("caverId").GetGuid()]).ShouldBe(Standing(participant));
        }
    }

    /// <summary>
    /// Somebody the log speaks of and the trip no longer names is still on the head count, as on
    /// the trip's own watch: the two screens count one party.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state is built the way the product lets it arise. While a watch runs, a roster edit
    /// that takes off somebody it has reports about is refused — asserted here, so that the
    /// removal that follows is known to be the allowed one. Once the watch is closed the trip's
    /// list may be corrected, and the trip stays on the camp's log for the recently-closed
    /// window: that is the first reading. Starting the watch again leaves it running with that
    /// person off its list and still inside the cave, which is the second and the one that
    /// matters most.
    /// </para>
    /// <para>
    /// Both controls are in the test. Before anybody leaves, the head count and the watch agree
    /// about the same four people. And somebody whose only report was taken off the log, and who
    /// was then taken off the trip, is on neither screen — a report taken off the log speaks of
    /// nobody — beside the person who is on both.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Somebody_the_log_speaks_of_stays_on_the_head_count_after_leaving_the_trips_list_as_on_the_trips_own_watch()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "Off the list");
        var (trip, party) = await TripAsync(owner, "Taken off the list", guests: 4);
        await JoinAsync(owner, camp, trip);
        await ArmAsync(owner, trip, model);

        var (stays, leaves, withdrawn, silent) = (party[0].Id, party[1].Id, party[2].Id, party[3].Id);
        var t0 = Whole(DateTimeOffset.UtcNow.AddHours(-3));
        await ReportAsync(owner, trip, new { caverIds = new[] { stays, leaves }, kind = "entered" }, t0);
        await ReportAsync(owner, trip, new { caverIds = new[] { withdrawn }, kind = "entered" }, t0.AddMinutes(5));
        await ReportAsync(owner, trip, new { caverIds = new[] { leaves }, kind = "note", note = "all well" }, t0.AddMinutes(40));

        // The control: everybody is on the trip's list, and the two screens tell one party.
        var everybody = await RowAgreeingWithTheWatchAsync(reader, camp, trip);
        PartyIds(everybody).ShouldBe([stays, leaves, withdrawn, silent], ignoreOrder: true);
        everybody.GetProperty("underground").GetInt32().ShouldBe(3);
        everybody.GetProperty("unheard").GetInt32().ShouldBe(1);

        // A report that never happened is taken off the log, and the person it was about may then
        // leave even a running watch: nothing on the log speaks of them any more.
        var mistaken = (await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/trip-logs/{trip}/tracking/events?caverId={withdrawn}"))
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{mistaken}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var trimmed = await PutRosterAsync(owner, trip, [stays, leaves, silent]);
        trimmed.StatusCode.ShouldBe(HttpStatusCode.OK, await trimmed.Content.ReadAsStringAsync());

        // Somebody the log does speak of may not, while the watch runs.
        var refused = await PutRosterAsync(owner, trip, [stays, silent]);
        var refusal = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusal);
        refusal.ShouldContain("trip_log.participant_tracked");

        // Closed: the trip's list is corrected, and the trip is still on the camp's log.
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var removed = await PutRosterAsync(owner, trip, [stays, silent]);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());

        foreach (var state in new[] { "closed", "armed" })
        {
            if (state == "armed")
            {
                // Started again, for a party that turned out to be still underground: a running
                // watch with somebody inside whom the trip no longer lists.
                await ArmAsync(owner, trip, model);
            }

            // The trip's own watch lists them, marked, and still inside.
            var watch = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{trip}/tracking");
            var watched = watch.GetProperty("participants").EnumerateArray()
                .Single(p => p.GetProperty("caverId").GetGuid() == leaves);
            watched.GetProperty("onRoster").GetBoolean().ShouldBeFalse();
            Standing(watched).ShouldBe((true, false));

            // And so does the camp's head count, for the same reader.
            var row = await RowAgreeingWithTheWatchAsync(reader, camp, trip);
            row.GetProperty("state").GetString().ShouldBe(state);
            // The people the trip names first, then the one only its log speaks of — and not the
            // one whose report was taken off it.
            PartyIds(row).Count.ShouldBe(3);
            PartyIds(row).Take(2).ShouldBe([stays, silent], ignoreOrder: true);
            PartyIds(row)[2].ShouldBe(leaves);
            row.GetProperty("underground").GetInt32().ShouldBe(2);
            row.GetProperty("out").GetInt32().ShouldBe(0);
            row.GetProperty("unheard").GetInt32().ShouldBe(1);
            // Their note is the party's latest word, and it is told as such.
            row.GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(40));

            var counted = row.GetProperty("party").EnumerateArray()
                .Single(p => p.GetProperty("caverId").GetGuid() == leaves);
            Standing(counted).ShouldBe((true, false));
            counted.GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(t0.AddMinutes(40));
            // Named as the watch names them for this reader, since the trip no longer does.
            counted.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
            counted.GetProperty("name").GetString().ShouldBe(watched.GetProperty("name").GetString());
        }
    }

    /// <summary>
    /// The load-bearing one. A party in a cave whose position the reader may not be told is
    /// counted on the log like any other, and the answer holds no place — for that reader or for
    /// the one who may place the cave.
    /// </summary>
    /// <remarks>
    /// Both controls are in the test, because either half alone proves little: the place exists
    /// and reaches the person entitled to it through the trip's own watch, and the reader is
    /// genuinely somebody that same watch withholds it from.
    /// </remarks>
    [Fact]
    public async Task A_party_in_a_protected_cave_is_counted_for_a_reader_who_may_not_place_it_and_no_place_is_in_the_answer()
    {
        var cave = await CaveAsync(locationProtected: true);
        var model = await ModelAsync(cave);
        var camp = await CampAsync(owner, "Protected ground");
        var (trip, party) = await TripAsync(owner, "Protected", guests: 2);
        await JoinAsync(owner, camp, trip);
        (await PutConfigAsync(owner, trip, new
        {
            state = "armed",
            surveyModelId = model,
            referenceStationName = "cave.ent.0",
            depthFilter = new[] { "cave.upper" },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(owner, trip, new { caverIds = new[] { party[0].Id }, kind = "atStation", stationName = "cave.upper.2" });

        // Control one: the place is real, and the person who may place the cave reads it on the
        // trip's own watch.
        var ownersWatch = await (await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking")).Content.ReadAsStringAsync();
        ownersWatch.ShouldContain("cave.upper.2");
        ownersWatch.ShouldContain(model.ToString());
        // Control two: the reader is somebody that watch withholds it from.
        var readersWatch = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{trip}/tracking");
        readersWatch.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();

        foreach (var client in new[] { reader, owner })
        {
            var response = await client.GetAsync(Log(camp));
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

            // The standing is told: one inside on the strength of a reported place, one unheard.
            var row = Row(JsonDocument.Parse(body).RootElement, trip);
            row.GetProperty("underground").GetInt32().ShouldBe(1);
            row.GetProperty("out").GetInt32().ShouldBe(0);
            row.GetProperty("unheard").GetInt32().ShouldBe(1);
            var placed = row.GetProperty("party").EnumerateArray()
                .Single(p => p.GetProperty("caverId").GetGuid() == party[0].Id);
            Standing(placed).ShouldBe((true, false));
            placed.GetProperty("lastRecordedAt").ValueKind.ShouldBe(JsonValueKind.String);

            // And nothing of where: no station of the report or of the watch's configuration, no
            // survey, no cave — by value, wherever in the answer it might have been put.
            body.ShouldNotContain("cave.upper");
            body.ShouldNotContain("cave.ent");
            body.ShouldNotContain(model.ToString());
            body.ShouldNotContain(cave.ToString());
        }
    }

    /// <summary>
    /// The answer's members, by name, exactly. A member added later fails here until somebody has
    /// decided it belongs on a screen that is a head count and not a map — and a station, a depth,
    /// a survey, a cave, a team or anything of the callout does not.
    /// </summary>
    [Fact]
    public async Task The_answer_carries_exactly_the_members_a_head_count_needs()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "Shape");
        var (trip, party) = await TripAsync(owner, "Shape", guests: 1);
        await JoinAsync(owner, camp, trip);
        await ArmAsync(owner, trip, model);
        await ReportAsync(owner, trip, new { caverIds = new[] { party[0].Id }, kind = "atStation", stationName = "cave.deep.3" });

        var log = await LogAsync(owner, camp);
        log.EnumerateObject().Select(p => p.Name).ShouldBe(["trips", "truncated"], ignoreOrder: true);

        var row = Row(log, trip);
        row.EnumerateObject().Select(p => p.Name).ShouldBe(
            [
                "tripLogId", "title", "tripDate", "tripDateEnd",
                "state", "armedAt", "closedAt", "expectedReturnAt",
                "underground", "out", "unheard", "lastRecordedAt", "party",
            ],
            ignoreOrder: true,
            "a row of the surface log carries a head count and the watch's own times, and nothing else");

        var person = row.GetProperty("party").EnumerateArray().Single();
        person.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["caverId", "name", "in", "out", "lastRecordedAt"],
            ignoreOrder: true,
            "a person on the surface log is somebody, in or out, and when they were last heard");
    }

    // ---- how much of it ------------------------------------------------------------------

    /// <summary>
    /// Past the cap the answer says it was cut short, and what it keeps is the running watch —
    /// never a finished trip in place of one still underground.
    /// </summary>
    [Fact]
    public async Task Past_the_cap_the_log_says_so_and_keeps_the_running_watch_over_the_finished_one()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "Capped");
        // The finished trip is made and armed first, so that neither the order the trips were
        // created in nor the order they were armed in would put the running one on top.
        var (finished, _) = await TripAsync(owner, "Finished", guests: 1);
        var (running, _) = await TripAsync(owner, "Running", guests: 1);
        await JoinAsync(owner, camp, finished);
        await JoinAsync(owner, camp, running);
        await ArmAsync(owner, running, model);
        await ArmAsync(owner, finished, model);
        (await PutConfigAsync(owner, finished, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Uncapped: both, the running one first, and nothing cut.
        var whole = await LogAsync(owner, camp);
        TripIds(whole).ShouldBe([running, finished]);
        whole.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        var settings = HostSettings();
        settings["ExpeditionSurfaceLog:MaxRows"] = "1";
        using var capped = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
        using var cappedOwner = await AuthHelper.BearerClientAsync(capped, ownerEmail);
        var cut = await LogAsync(cappedOwner, camp);
        TripIds(cut).ShouldBe([running]);
        cut.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// A watch filed as closed that never ran — what importing a device's recording leaves on the
    /// trip it creates — is not a party that has just come out: it is off the log, and it takes
    /// no place from one that has.
    /// </summary>
    /// <remarks>
    /// The row is written the way the import writes it: closed at this instant, never armed. It
    /// is therefore the most recently closed watch of the camp, which is the position from which
    /// it would crowd a genuinely closed one out under a cap of one. The last step arms the same
    /// row in the record and reads again, so the absence above is owed to the missing arming and
    /// not to a row the log could not have listed anyway.
    /// </remarks>
    [Fact]
    public async Task A_watch_filed_as_closed_that_never_ran_is_off_the_log_and_takes_no_place_on_it()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var camp = await CampAsync(owner, "With an import");
        var (followed, _) = await TripAsync(owner, "Followed and out", guests: 1);
        var (filed, _) = await TripAsync(owner, "Filed afterwards", guests: 2);
        await JoinAsync(owner, camp, followed);
        await JoinAsync(owner, camp, filed);
        await ArmAsync(owner, followed, model);
        (await PutConfigAsync(owner, followed, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.TripTrackings.Add(new TripTracking
            {
                TripLogId = filed,
                State = TripTrackingState.Closed,
                SurveyModelId = model,
                ClosedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            });
            await db.SaveChangesAsync();
        }

        var whole = await LogAsync(owner, camp);
        TripIds(whole).ShouldBe([followed]);
        whole.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // Under a cap of one the followed trip keeps its row, and nothing is said to be cut:
        // the filed one is neither ahead of it nor counted as a row that did not fit.
        var settings = HostSettings();
        settings["ExpeditionSurfaceLog:MaxRows"] = "1";
        using var capped = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
        using var cappedOwner = await AuthHelper.BearerClientAsync(capped, ownerEmail);
        var cut = await LogAsync(cappedOwner, camp);
        TripIds(cut).ShouldBe([followed]);
        cut.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // The same row, had it once been armed: listed, and first as the most recently closed.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.TripTrackings.SingleAsync(t => t.TripLogId == filed);
            row.ArmedAt = DateTimeOffset.UtcNow.AddHours(-6);
            await db.SaveChangesAsync();
        }

        TripIds(await LogAsync(owner, camp)).ShouldBe([filed, followed]);
        var cutAgain = await LogAsync(cappedOwner, camp);
        TripIds(cutAgain).ShouldBe([filed]);
        cutAgain.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// The log costs the database the same number of commands for a camp with four followed trips
    /// as for a camp with one.
    /// </summary>
    /// <remarks>
    /// Commands are counted through an interceptor on a host of this test's own. The assertion is
    /// equality between the two camps rather than a number, because how many commands the answer
    /// takes is not the promise — that it does not grow with the camp is.
    /// </remarks>
    [Fact]
    public async Task The_log_costs_the_database_the_same_for_four_trips_as_for_one()
    {
        var model = await ModelAsync(await CaveAsync(locationProtected: false));
        var small = await CampAsync(owner, "One trip");
        var large = await CampAsync(owner, "Four trips");
        foreach (var (camp, count) in new[] { (small, 1), (large, 4) })
        {
            for (var i = 0; i < count; i++)
            {
                var (trip, party) = await TripAsync(owner, $"Counted {i}", guests: 2);
                await JoinAsync(owner, camp, trip);
                await ArmAsync(owner, trip, model);
                await ReportAsync(owner, trip, new { caverIds = party.Select(p => p.Id).ToArray(), kind = "entered" });
            }
        }

        var counter = new CommandCounter();
        using var counted = new SilexGisApiFactory(connectionString, HostSettings(), services =>
        {
            JobWorkers.RemoveFrom(services);
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(counter));
        });
        // The Viewer, so that the trips are narrowed by the visibility rule rather than read past
        // it. Asked once before counting, so that whatever a host reads once and remembers is
        // not charged to whichever camp happens to be asked first.
        using var client = await AuthHelper.BearerClientAsync(counted, readerEmail);
        TripIds(await LogAsync(client, small)).Count.ShouldBe(1);

        var one = await CountedAsync(counter, client, small);
        var four = await CountedAsync(counter, client, large);
        one.Trips.ShouldBe(1);
        four.Trips.ShouldBe(4);
        // The camp's own lookup is a command, so zero on both sides would mean the interceptor
        // was never attached and the equality proved nothing.
        one.Commands.ShouldBeGreaterThan(0);
        four.Commands.ShouldBe(one.Commands);
    }

    // ---- the settings --------------------------------------------------------------------

    /// <summary>
    /// A setting that cannot be served stops the server from starting, naming the setting —
    /// rather than serving an empty log that reads as "nobody is underground".
    /// </summary>
    [Theory]
    [InlineData("ExpeditionSurfaceLog:MaxRows", "0")]
    [InlineData("ExpeditionSurfaceLog:MaxRows", "501")]
    [InlineData("ExpeditionSurfaceLog:RecentlyClosed", "-00:00:01")]
    public void A_setting_that_cannot_be_served_refuses_the_start_and_names_itself(string key, string value)
    {
        var settings = HostSettings();
        settings[key] = value;
        using var refused = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom, ownHost: true);

        var failure = Should.Throw<Exception>(() => refused.CreateClient());
        Messages(failure).ShouldContain(m => m.Contains(key, StringComparison.Ordinal));

        // The positive half is every other test in this class: the same host, with the settings
        // left alone or set inside their range, starts and answers.
    }

    // ---- arranging -----------------------------------------------------------------------

    private static string Log(Guid camp) => $"/api/v1/expeditions/{camp}/surface-log";

    private static async Task<JsonElement> LogAsync(HttpClient client, Guid camp)
    {
        var response = await client.GetAsync(Log(camp));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static List<Guid> TripIds(JsonElement log) =>
        [.. log.GetProperty("trips").EnumerateArray().Select(t => t.GetProperty("tripLogId").GetGuid())];

    private static JsonElement Row(JsonElement log, Guid trip) =>
        log.GetProperty("trips").EnumerateArray().Single(t => t.GetProperty("tripLogId").GetGuid() == trip);

    private static (bool In, bool Out) Standing(JsonElement person) =>
        (person.GetProperty("in").GetBoolean(), person.GetProperty("out").GetBoolean());

    /// <summary>The people on one row of the log, in the order the row lists them.</summary>
    private static List<Guid> PartyIds(JsonElement row) =>
        [.. row.GetProperty("party").EnumerateArray().Select(p => p.GetProperty("caverId").GetGuid())];

    /// <summary>
    /// One trip's row on the camp's log, read beside that trip's own watch by the same reader and
    /// held to it: the same people, each standing as the watch says and last heard when it says,
    /// and the row's three numbers the watch's party counted.
    /// </summary>
    private static async Task<JsonElement> RowAgreeingWithTheWatchAsync(HttpClient client, Guid camp, Guid trip)
    {
        var watch = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{trip}/tracking");
        var watched = watch.GetProperty("participants").EnumerateArray()
            .ToDictionary(p => p.GetProperty("caverId").GetGuid());
        var row = Row(await LogAsync(client, camp), trip);
        var counted = row.GetProperty("party").EnumerateArray()
            .ToDictionary(p => p.GetProperty("caverId").GetGuid());

        counted.Keys.ShouldBe(
            watched.Keys, ignoreOrder: true,
            "the camp's head count and the trip's own watch list the same people");
        foreach (var (caverId, person) in counted)
        {
            Standing(person).ShouldBe(Standing(watched[caverId]));
            person.GetProperty("lastRecordedAt").ToString()
                .ShouldBe(watched[caverId].GetProperty("lastRecordedAt").ToString());
        }

        var standings = watched.Values.Select(Standing).ToList();
        row.GetProperty("underground").GetInt32().ShouldBe(standings.Count(s => s.In));
        row.GetProperty("out").GetInt32().ShouldBe(standings.Count(s => s.Out));
        row.GetProperty("unheard").GetInt32().ShouldBe(standings.Count(s => !s.In && !s.Out));
        return row;
    }

    private static DateTimeOffset Whole(DateTimeOffset at) => at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond));

    private static IEnumerable<string> Messages(Exception? failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private static async Task<(int Trips, int Commands)> CountedAsync(
        CommandCounter counter, HttpClient client, Guid camp)
    {
        counter.Reset();
        var response = await client.GetAsync(Log(camp));
        var commands = counter.Count;
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return (TripIds(JsonDocument.Parse(payload).RootElement).Count, commands);
    }

    private static async Task<Guid> CampAsync(HttpClient client, string name, string visibility = "authenticated")
    {
        var response = await client.PostAsJsonAsync("/api/v1/expeditions", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..Math.Min(40, name.Length + 33)],
            startDate = "2026-09-10",
            endDate = "2026-09-20",
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task JoinAsync(HttpClient client, Guid camp, Guid trip)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/expeditions/{camp}/trips", new { tripLogId = trip });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A trip with invented guests, and those guests as the trip itself names them.</summary>
    private static async Task<(Guid Trip, List<(Guid Id, string Name)> Party)> TripAsync(
        HttpClient client, string title, int guests, string visibility = "authenticated")
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var body = JsonDocument.Parse(payload).RootElement;
        var party = body.GetProperty("participants").EnumerateArray()
            .Select(p => (p.GetProperty("caverId").GetGuid(), p.GetProperty("name").GetString()!))
            .Distinct()
            .ToList();
        party.Count.ShouldBe(guests);
        return (body.GetProperty("id").GetGuid(), party);
    }

    /// <summary>
    /// The trip saved with these people as its whole list — the trip's form sends the roster back
    /// entire, so leaving somebody out is how a person is taken off a trip.
    /// </summary>
    private static Task<HttpResponseMessage> PutRosterAsync(HttpClient client, Guid trip, IEnumerable<Guid> participants) =>
        client.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = $"Roster {Guid.NewGuid():N}"[..28],
            tripDate = "2026-09-12",
            participants = participants.Select(id => new { caverId = id }).ToArray(),
            visibility = "authenticated",
        });

    /// <summary>
    /// The hour the party planned to be out by, written straight onto the trip: a plain recorded
    /// fact with nothing derived from it on this read, and the callout that normally travels with
    /// it is exactly what this log does not look at.
    /// </summary>
    private async Task SetExpectedReturnAsync(Guid trip, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.TripLogs.SingleAsync(t => t.Id == trip);
        row.ExpectedReturnAt = at;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Log Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey model created the real way (an upload — a file row cannot exist outside the
    /// documents chain), then marked ready with a handful of stations seeded into the graph
    /// tables. The extraction job never runs here; workers are off.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "surface-log.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private static async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, Guid trip, object body)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    private static async Task ArmAsync(HttpClient client, Guid trip, Guid model)
    {
        var response = await PutConfigAsync(client, trip, new { state = "armed", surveyModelId = model });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>One report, asserted to have landed; stamped with a stated hour when one is given.</summary>
    private static async Task ReportAsync(HttpClient client, Guid trip, object body, DateTimeOffset? recordedAt = null)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        if (recordedAt is { } at) json["recordedAt"] = at.ToString("O");
        var response = await client.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Every command the host sends to the database, counted and nothing else.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Reset() => Interlocked.Exchange(ref count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }
    }
}
