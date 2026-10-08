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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Entry and exit times taken from a trip's tracking log onto its roster: read as a proposal that
/// writes nothing, then written as exactly what was reviewed. What these hold to is that nothing
/// reaches a roster that a person did not tick, that a time somebody typed is never replaced
/// without being pointed out first, and that a trip form opened before the write cannot put the
/// old times back afterwards.
/// </summary>
public sealed class TripRosterTimesFromTrackingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Bucharest = "Europe/Bucharest";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;

    public TripRosterTimesFromTrackingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rtt-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rtt-read-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"rtt-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"rtt-read-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    // ---- the proposal -------------------------------------------------------------------------

    /// <summary>
    /// Per person: what the log says on the zone named, what the roster holds, whether taking one
    /// for the other would overwrite a typed time, and why not where it cannot be taken. A report
    /// taken off the log says nothing — shown beside the same report while it was still on it.
    /// And reading the proposal changes no roster row.
    /// </summary>
    [Fact]
    public async Task The_proposal_is_read_on_the_zone_named_ignores_removed_reports_and_writes_nothing()
    {
        var (trip, cavers) = await CreateTripAsync("Times proposed", guests: 5);
        var (plain, stillIn, corrected, typed, leaves) = (cavers[0], cavers[1], cavers[2], cavers[3], cavers[4]);
        // One person's times were typed on the trip's form before anybody looked at the log.
        (await PutRosterAsync(trip, cavers.Select(id => id == typed
            ? Row(id, entry: "08:15:00", exit: "16:30:00")
            : Row(id)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await FollowedAsync(trip);

        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), [plain, typed, leaves]);
        await ReportAsync(trip, "exited", At(12, 0), [corrected]);
        var mistaken = await ReportAsync(trip, "exited", At(15, 0), [corrected]);
        // Somebody who went is taken off the roster afterwards: their reports stay.
        (await PutRosterAsync(trip, cavers.Where(id => id != leaves).Select(id => id == typed
            ? Row(id, entry: "08:15:00", exit: "16:30:00")
            : Row(id)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await RosterRowsAsync(trip);

        // While the mistaken report is on the log it is the last word on when they came out.
        Person(await TimesAsync(owner, trip, Bucharest), corrected).GetProperty("exit").GetString().ShouldBe("18:00:00");
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{mistaken}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var times = await TimesAsync(owner, trip, Bucharest);
        times.GetProperty("timeZone").GetString().ShouldBe(Bucharest);
        var people = times.GetProperty("people");
        people.GetArrayLength().ShouldBe(5);

        var first = Person(times, plain);
        first.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
        first.GetProperty("enteredAt").GetDateTimeOffset().ShouldBe(At(6, 0));
        first.GetProperty("exitedAt").GetDateTimeOffset().ShouldBe(At(14, 0));
        first.GetProperty("entry").GetString().ShouldBe("09:00:00");
        first.GetProperty("exit").GetString().ShouldBe("17:00:00");
        first.GetProperty("currentEntry").ValueKind.ShouldBe(JsonValueKind.Null);
        first.GetProperty("changes").GetBoolean().ShouldBeTrue();
        first.GetProperty("overwrites").GetBoolean().ShouldBeFalse();
        first.GetProperty("problem").ValueKind.ShouldBe(JsonValueKind.Null);
        first.GetProperty("stays").GetInt32().ShouldBe(1);

        var open = Person(times, stillIn);
        open.GetProperty("problem").GetString().ShouldBe("noExit");
        open.GetProperty("entry").GetString().ShouldBe("09:00:00");
        open.GetProperty("exit").ValueKind.ShouldBe(JsonValueKind.Null);
        open.GetProperty("changes").GetBoolean().ShouldBeFalse();

        // The report taken off the log no longer speaks: noon it is.
        Person(times, corrected).GetProperty("exit").GetString().ShouldBe("15:00:00");

        var overwritten = Person(times, typed);
        overwritten.GetProperty("currentEntry").GetString().ShouldBe("08:15:00");
        overwritten.GetProperty("currentExit").GetString().ShouldBe("16:30:00");
        overwritten.GetProperty("changes").GetBoolean().ShouldBeTrue();
        overwritten.GetProperty("overwrites").GetBoolean().ShouldBeTrue();

        // Listed, and said to be off the roster — after everybody who is on it.
        var gone = Person(times, leaves);
        gone.GetProperty("onRoster").GetBoolean().ShouldBeFalse();
        gone.GetProperty("problem").GetString().ShouldBe("notOnRoster");
        gone.GetProperty("entry").GetString().ShouldBe("09:00:00");
        people[4].GetProperty("caverId").GetGuid().ShouldBe(leaves);

        // The same moments on another zone's clocks are other readings.
        var utc = await TimesAsync(owner, trip, "utc");
        utc.GetProperty("timeZone").GetString().ShouldBe("UTC");
        Person(utc, plain).GetProperty("entry").GetString().ShouldBe("06:00:00");

        // A zone has to be named, and has to be one the server knows.
        foreach (var asked in new[] { "", "?timeZone=Mars/Olympus", "?timeZone=EET" })
        {
            var refused = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/roster-times{asked}");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, asked);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.roster_times_zone_unknown");
        }

        (await RosterRowsAsync(trip)).ShouldBe(before);
    }

    // ---- the write ----------------------------------------------------------------------------

    /// <summary>
    /// A note about the cave is about nobody: it went in with nobody and came out with nobody.
    /// With one on the log the proposal lists exactly the people it would list without it — the
    /// note is not somebody the log speaks of who has no row — and each of them is read the
    /// times the reports about them give.
    /// </summary>
    [Fact]
    public async Task A_note_about_the_cave_on_the_log_adds_nobody_to_the_proposal_and_moves_no_time()
    {
        var (trip, cavers) = await CreateTripAsync("Times beside a hazard", guests: 2);
        await FollowedAsync(trip);
        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), cavers);

        // Said after everybody was out: read as anybody's report it would be the last word of the day.
        var note = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            kind = "caveNote", note = "Loose rock above the pitch", recordedAt = At(15, 0),
        });
        note.StatusCode.ShouldBe(HttpStatusCode.OK, await note.Content.ReadAsStringAsync());

        var times = await TimesAsync(owner, trip, Bucharest);
        times.GetProperty("people").GetArrayLength().ShouldBe(2);
        foreach (var caver in cavers)
        {
            var person = Person(times, caver);
            person.GetProperty("onRoster").GetBoolean().ShouldBeTrue();
            person.GetProperty("enteredAt").GetDateTimeOffset().ShouldBe(At(6, 0));
            person.GetProperty("exitedAt").GetDateTimeOffset().ShouldBe(At(14, 0));
            person.GetProperty("problem").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    /// <summary>
    /// The people ticked get the reviewed times on every roster row they have — a person with two
    /// jobs has two — nobody else's row moves, the trip's history says where the times came from,
    /// and a second press writes nothing.
    /// </summary>
    [Fact]
    public async Task Taking_writes_every_row_of_each_person_ticked_and_nobody_elses()
    {
        var (trip, cavers) = await CreateTripAsync("Times taken", guests: 3);
        var (twoJobs, unticked, typed) = (cavers[0], cavers[1], cavers[2]);
        var roles = await RoleIdsAsync();
        (await PutRosterAsync(trip,
        [
            Row(twoJobs, role: roles[0]),
            Row(twoJobs, role: roles[1]),
            Row(unticked),
            Row(typed, entry: "08:15:00", exit: "16:30:00"),
        ])).StatusCode.ShouldBe(HttpStatusCode.OK);
        await FollowedAsync(trip);
        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), cavers);

        var (times, version) = await ReviewAsync(trip, Bucharest);
        Person(times, twoJobs).GetProperty("changes").GetBoolean().ShouldBeTrue();
        var historyBefore = await HistoryAsync(trip);

        var taken = await TakeAsync(owner, trip, version, Bucharest, [(twoJobs, "09:00:00", "17:00:00")]);
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
        var answer = await BodyAsync(taken);
        answer.GetProperty("people").GetInt32().ShouldBe(1);
        answer.GetProperty("rows").GetInt32().ShouldBe(2);
        Person(answer.GetProperty("times"), twoJobs).GetProperty("changes").GetBoolean().ShouldBeFalse();
        Person(answer.GetProperty("times"), unticked).GetProperty("changes").GetBoolean().ShouldBeTrue();

        var rows = await RosterRowsAsync(trip);
        rows.Where(r => r.Caver == twoJobs).ShouldAllBe(r => r.Entry == new TimeOnly(9, 0) && r.Exit == new TimeOnly(17, 0));
        rows.Count(r => r.Caver == twoJobs).ShouldBe(2);
        // Nobody else: the person left unticked still has no time of their own, and the one whose
        // times were typed still has what was typed.
        rows.Single(r => r.Caver == unticked).ShouldBe(new RosterRow(unticked, null, null));
        rows.Single(r => r.Caver == typed).ShouldBe(new RosterRow(typed, new TimeOnly(8, 15), new TimeOnly(16, 30)));

        // On the trip's history: the two rows, each with its change, and one line for the act.
        var history = await HistoryAsync(trip);
        (history.RosterRows - historyBefore.RosterRows).ShouldBe(2);
        (history.Acts - historyBefore.Acts).ShouldBe(1);

        // The typed times are replaced only once that person is ticked too.
        var (_, next) = await ReviewAsync(trip, Bucharest);
        // Sent as an overwrite nobody was shown, it is refused; as the overwrite it was reviewed
        // as, it is written.
        var unshown = await TakeAsync(owner, trip, next, Bucharest, [(typed, "09:00:00", "17:00:00")]);
        unshown.StatusCode.ShouldBe(HttpStatusCode.Conflict, await unshown.Content.ReadAsStringAsync());
        (await RosterRowsAsync(trip)).Single(r => r.Caver == typed)
            .ShouldBe(new RosterRow(typed, new TimeOnly(8, 15), new TimeOnly(16, 30)));
        var second = await TakeAsync(owner, trip, next, Bucharest,
            [new Tick(typed, "09:00:00", "17:00:00", "08:15:00", "16:30:00", Overwrites: true)]);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        (await RosterRowsAsync(trip)).Single(r => r.Caver == typed)
            .ShouldBe(new RosterRow(typed, new TimeOnly(9, 0), new TimeOnly(17, 0)));

        // Asked again for what is already there: nothing written, nothing added to the history.
        var (_, again) = await ReviewAsync(trip, Bucharest);
        var repeat = await TakeAsync(owner, trip, again, Bucharest,
            [new Tick(twoJobs, "09:00:00", "17:00:00", "09:00:00", "17:00:00")]);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(repeat)).GetProperty("rows").GetInt32().ShouldBe(0);
        (await HistoryAsync(trip)).Acts.ShouldBe(historyBefore.Acts + 2);
    }

    /// <summary>
    /// The write needs the trip's version, refuses an old one, and moves it: a trip form read
    /// before the times were taken is refused when it is saved afterwards, where it would have
    /// put the old roster back — and saves once it has read the trip again.
    /// </summary>
    [Fact]
    public async Task The_write_rides_the_trips_version_and_a_form_held_open_cannot_save_over_it()
    {
        var (trip, cavers) = await CreateTripAsync("Times and versions", guests: 2);
        await FollowedAsync(trip);
        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), cavers);
        (string Caver, string Entry, string Exit)[] ticked = [(cavers[0].ToString(), "09:00:00", "17:00:00")];
        var body = new
        {
            timeZone = Bucharest,
            people = ticked.Select(p => new { caverId = p.Caver, entry = p.Entry, exit = p.Exit, overwrites = false }).ToArray(),
        };
        var url = $"/api/v1/trip-logs/{trip}/tracking/roster-times";

        // No version at all.
        var missing = await owner.PostAsJsonAsync(url, body);
        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await missing.Content.ReadAsStringAsync());

        // A version from before somebody else saved the trip — the trip itself, under another
        // title, with the roster as it was.
        var (_, reviewed) = await ReviewAsync(trip, Bucharest);
        (await PutRosterAsync(trip, cavers.Select(id => Row(id)), title: "Renamed since"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await owner.PostWithIfMatchAsync(url, body, reviewed);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await stale.Content.ReadAsStringAsync());
        (await RosterRowsAsync(trip)).ShouldAllBe(r => r.Entry == null && r.Exit == null);

        // The form somebody opened now, before the times are taken.
        var formVersion = await TripVersionAsync(trip);
        var (_, current) = await ReviewAsync(trip, Bucharest);
        current.ShouldBe(formVersion);
        var taken = await owner.PostWithIfMatchAsync(url, body, current);
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());

        // Saved afterwards, carrying the roster it was opened with: refused, and the times stand.
        var overwrite = await PutRosterAsync(trip, cavers.Select(id => Row(id)), formVersion);
        overwrite.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await overwrite.Content.ReadAsStringAsync());
        (await RosterRowsAsync(trip)).Single(r => r.Caver == cavers[0])
            .ShouldBe(new RosterRow(cavers[0], new TimeOnly(9, 0), new TimeOnly(17, 0)));

        // The same save once the trip has been read again goes through: the refusal was about
        // the version, not about the request.
        (await PutRosterAsync(trip, cavers.Select(id => Row(id)), await TripVersionAsync(trip)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Two people have the trip's form open and each changes only a participant's times. The
    /// first save moves the trip's version, so the second — which carries the roster as it was
    /// opened, and would put the first one's times back to nothing — is refused the way any stale
    /// save of that form is, and goes through once the trip has been read again. A save that
    /// changes no roster row moves nothing, as before.
    /// </summary>
    [Fact]
    public async Task Two_forms_open_on_one_trip_cannot_save_one_roster_over_the_other()
    {
        var (trip, cavers) = await CreateTripAsync("Two forms", guests: 2);
        var (first, second) = (cavers[0], cavers[1]);
        // The form's own save, once, so that the saves below differ from it in the roster alone.
        (await PutRosterAsync(trip, cavers.Select(id => Row(id)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Both forms are opened here.
        var opened = await TripVersionAsync(trip);

        // The first form types one person's times and saves: same title, same date, same people.
        var saved = await PutRosterAsync(trip, cavers.Select(id => id == first
            ? Row(id, entry: "08:15:00", exit: "16:30:00")
            : Row(id)), opened);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var afterFirst = await TripVersionAsync(trip);
        afterFirst.ShouldNotBe(opened, "a save that changed a roster row must move the trip's version");
        saved.Headers.ETag!.ToString().ShouldBe(afterFirst, "the save hands over the version it produced");

        // The second form types the other person's times, over the roster it was opened with.
        var stale = await PutRosterAsync(trip, cavers.Select(id => id == second
            ? Row(id, entry: "09:00:00", exit: "17:00:00")
            : Row(id)), opened);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await stale.Content.ReadAsStringAsync());
        var rows = await RosterRowsAsync(trip);
        rows.Single(r => r.Caver == first).ShouldBe(new RosterRow(first, new TimeOnly(8, 15), new TimeOnly(16, 30)));
        rows.Single(r => r.Caver == second).ShouldBe(new RosterRow(second, null, null));

        // Read again, it carries both people's times and saves: the refusal was about the
        // version, not about the request.
        IEnumerable<object> both =
        [
            Row(first, entry: "08:15:00", exit: "16:30:00"),
            Row(second, entry: "09:00:00", exit: "17:00:00"),
        ];
        (await PutRosterAsync(trip, both, afterFirst)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterSecond = await TripVersionAsync(trip);
        afterSecond.ShouldNotBe(afterFirst);

        // The same roster saved again changes no row: the version stays, and nothing more lands
        // on the trip's history — the version follows what was written, not that a save was made.
        var historyBefore = await HistoryAsync(trip);
        (await PutRosterAsync(trip, both, afterSecond)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TripVersionAsync(trip)).ShouldBe(afterSecond);
        (await HistoryAsync(trip)).RosterRows.ShouldBe(historyBefore.RosterRows);

        // Taking somebody off is a change of the roster like any other.
        (await PutRosterAsync(trip, [Row(first, entry: "08:15:00", exit: "16:30:00")], afterSecond))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TripVersionAsync(trip)).ShouldNotBe(afterSecond);
        (await RosterRowsAsync(trip)).Select(r => r.Caver).ShouldBe([first]);
    }

    /// <summary>
    /// A time typed on the trip's form after the review, and saved with nothing else changed, is
    /// not replaced by a write that was never shown it. The form's save moved the trip's version,
    /// so the write as reviewed is refused as stale before it is looked at. And a writer who has
    /// the new version but still says the roster held nothing — a version fetched without reading
    /// the proposal again — is refused on what it says the roster held. Read again, the typed time
    /// is marked as an overwrite, and only a write that says so replaces it.
    /// </summary>
    [Fact]
    public async Task A_time_typed_after_the_review_is_not_replaced_by_a_write_that_never_saw_it()
    {
        var (trip, cavers) = await CreateTripAsync("Times typed meanwhile", guests: 2);
        var (typedMeanwhile, untouched) = (cavers[0], cavers[1]);
        // The form's own save, once, so that the saves below differ from it in the roster alone.
        (await PutRosterAsync(trip, cavers.Select(id => Row(id)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await FollowedAsync(trip);
        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), cavers);

        var (times, version) = await ReviewAsync(trip, Bucharest);
        Person(times, typedMeanwhile).GetProperty("overwrites").GetBoolean().ShouldBeFalse();

        // Somebody else types one person's times on the trip's form and saves: same title, same
        // date, same people. This used to leave the trip's version where the review read it, and
        // the assertion here said so; a save that changes a roster row now moves it, because two
        // such saves from two open forms otherwise overwrite each other with nobody told.
        (await PutRosterAsync(trip, cavers.Select(id => id == typedMeanwhile
            ? Row(id, entry: "08:15:00", exit: "16:30:00")
            : Row(id)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = await TripVersionAsync(trip);
        moved.ShouldNotBe(version, "a save of the form that changed a participant's times moves the trip's version");

        // The write as reviewed, under the version it was reviewed at: stale, and nothing written.
        var stale = await TakeAsync(owner, trip, version, Bucharest,
            [(typedMeanwhile, "09:00:00", "17:00:00"), (untouched, "09:00:00", "17:00:00")]);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await stale.Content.ReadAsStringAsync());

        // The same write under the version the trip has now — as a writer would send that picked
        // the version up without reading the proposal again. The version no longer refuses it;
        // what it says the roster held does.
        var refused = await TakeAsync(owner, trip, moved, Bucharest,
            [(typedMeanwhile, "09:00:00", "17:00:00"), (untouched, "09:00:00", "17:00:00")]);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.roster_times_changed");
        var rows = await RosterRowsAsync(trip);
        rows.Single(r => r.Caver == typedMeanwhile)
            .ShouldBe(new RosterRow(typedMeanwhile, new TimeOnly(8, 15), new TimeOnly(16, 30)));
        // Whole or nothing: the person beside them was not written either.
        rows.Single(r => r.Caver == untouched).ShouldBe(new RosterRow(untouched, null, null));

        // Saying what the roster holds now, while still claiming nothing typed is replaced, is
        // no better.
        var unmarked = await TakeAsync(owner, trip, moved, Bucharest,
            [new Tick(typedMeanwhile, "09:00:00", "17:00:00", "08:15:00", "16:30:00")]);
        unmarked.StatusCode.ShouldBe(HttpStatusCode.Conflict, await unmarked.Content.ReadAsStringAsync());

        // Read again, the typed time is pointed out under the version the form's save produced,
        // and the write that repeats that goes through — so the refusals above were about the
        // version and the roster, not about the people or the trip.
        var (again, current) = await ReviewAsync(trip, Bucharest);
        current.ShouldBe(moved);
        var shown = Person(again, typedMeanwhile);
        shown.GetProperty("overwrites").GetBoolean().ShouldBeTrue();
        shown.GetProperty("currentEntry").GetString().ShouldBe("08:15:00");
        var taken = await TakeAsync(owner, trip, current, Bucharest,
        [
            new Tick(typedMeanwhile, "09:00:00", "17:00:00", "08:15:00", "16:30:00", Overwrites: true),
            new Tick(untouched, "09:00:00", "17:00:00"),
        ]);
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
        (await RosterRowsAsync(trip)).ShouldAllBe(r => r.Entry == new TimeOnly(9, 0) && r.Exit == new TimeOnly(17, 0));
    }

    /// <summary>
    /// What is written is what was reviewed, or nothing: a log that changed after the review, a
    /// person the roster cannot hold times for, and somebody not on the roster each refuse the
    /// whole write — the people beside them included.
    /// </summary>
    [Fact]
    public async Task A_write_that_is_no_longer_what_was_reviewed_writes_nothing()
    {
        var (trip, cavers) = await CreateTripAsync("Times that moved", guests: 4);
        var (fine, moved, stillIn, leaves) = (cavers[0], cavers[1], cavers[2], cavers[3]);
        await FollowedAsync(trip);
        await ReportAsync(trip, "entered", At(6, 0), cavers);
        await ReportAsync(trip, "exited", At(14, 0), [fine, leaves]);
        var movedExit = await ReportAsync(trip, "exited", At(14, 0), [moved]);
        (await PutRosterAsync(trip, cavers.Where(id => id != leaves).Select(id => Row(id))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, version) = await ReviewAsync(trip, Bucharest);
        // After the review, the moment one of them came out is corrected on the log. The trip's
        // own version does not move for that.
        var correction = await owner.PutAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events/{movedExit}", new
        {
            kind = "exited", recordedAt = At(14, 30),
        });
        correction.StatusCode.ShouldBe(HttpStatusCode.OK, await correction.Content.ReadAsStringAsync());
        (await ReviewAsync(trip, Bucharest)).Version.ShouldBe(version);

        (Guid, string, string)[][] refusedSets =
        [
            [(fine, "09:00:00", "17:00:00"), (moved, "09:00:00", "17:00:00")],
            [(fine, "09:00:00", "17:00:00"), (stillIn, "09:00:00", "17:00:00")],
            [(fine, "09:00:00", "17:00:00"), (leaves, "09:00:00", "17:00:00")],
            [(fine, "09:00:00", "17:00:00"), (Guid.NewGuid(), "09:00:00", "17:00:00")],
        ];
        foreach (var set in refusedSets)
        {
            var refused = await TakeAsync(owner, trip, version, Bucharest, set);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
            (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.roster_times_changed");
        }

        // Requests that say too little, or name a zone nobody knows.
        var url = $"/api/v1/trip-logs/{trip}/tracking/roster-times";
        (await owner.PostWithIfMatchAsync(url, new { timeZone = Bucharest, people = Array.Empty<object>() }, version))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostWithIfMatchAsync(url, new { people = new[] { new { caverId = fine, entry = "09:00:00", exit = "17:00:00" } } }, version))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostWithIfMatchAsync(url, new { timeZone = Bucharest, people = new[] { new { caverId = fine, entry = "09:00:00", overwrites = false } } }, version))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Whether a typed time is being replaced has to be said, not left to be assumed.
        (await owner.PostWithIfMatchAsync(url, new { timeZone = Bucharest, people = new[] { new { caverId = fine, entry = "09:00:00", exit = "17:00:00" } } }, version))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // An element that is no person is a refused request, not a failure of the server.
        var nobody = await owner.PostWithIfMatchAsync(url, new { timeZone = Bucharest, people = new object?[] { null } }, version);
        nobody.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nobody.Content.ReadAsStringAsync());
        (await nobody.Content.ReadAsStringAsync()).ShouldContain("validation.failed");
        var unknown = await TakeAsync(owner, trip, version, "Mars/Olympus", [(fine, "09:00:00", "17:00:00")]);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("tracking.roster_times_zone_unknown");

        (await RosterRowsAsync(trip)).ShouldAllBe(r => r.Entry == null && r.Exit == null);

        // The same people as reviewed again, corrected moment and all, are written.
        var allowed = await TakeAsync(owner, trip, version, Bucharest,
            [(fine, "09:00:00", "17:00:00"), (moved, "09:00:00", "17:30:00")]);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await BodyAsync(allowed)).GetProperty("rows").GetInt32().ShouldBe(2);
    }

    // ---- who may -----------------------------------------------------------------------------

    /// <summary>
    /// The proposal and the write are for whoever may write the trip. Somebody who reads it is
    /// told they may not; somebody who cannot read it is told there is no such trip; and a
    /// deleted trip is no trip for its own writer either. Each refusal stands beside the owner
    /// being answered on the same trip.
    /// </summary>
    [Fact]
    public async Task Only_somebody_who_may_write_the_trip_reads_or_takes_the_times()
    {
        var (trip, cavers) = await CreateTripAsync("Times of a readable trip", guests: 1);
        var (hidden, hiddenCavers) = await CreateTripAsync("Times of a private trip", guests: 1, visibility: "private");
        foreach (var (id, people) in new[] { (trip, cavers), (hidden, hiddenCavers) })
        {
            await FollowedAsync(id);
            await ReportAsync(id, "entered", At(6, 0), people);
            await ReportAsync(id, "exited", At(14, 0), people);
        }

        string Url(Guid id) => $"/api/v1/trip-logs/{id}/tracking/roster-times";
        (Guid, string, string)[] ticked = [(cavers[0], "09:00:00", "17:00:00")];
        (Guid, string, string)[] hiddenTicked = [(hiddenCavers[0], "09:00:00", "17:00:00")];

        (await anonymous.GetAsync($"{Url(trip)}?timeZone={Bucharest}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TakeAsync(anonymous, trip, "*", Bucharest, ticked)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The reader does read this trip — what they are refused is what only a writer is given.
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"{Url(trip)}?timeZone={Bucharest}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TakeAsync(reader, trip, "*", Bucharest, ticked)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And does not read the private one, which answers as a trip that is not there.
        (await reader.GetAsync($"/api/v1/trip-logs/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var masked = await reader.GetAsync($"{Url(hidden)}?timeZone={Bucharest}");
        masked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await masked.Content.ReadAsStringAsync()).ShouldContain("trip_log.not_found");
        (await TakeAsync(reader, hidden, "*", Bucharest, hiddenTicked)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await RosterRowsAsync(trip)).ShouldAllBe(r => r.Entry == null && r.Exit == null);
        (await RosterRowsAsync(hidden)).ShouldAllBe(r => r.Entry == null && r.Exit == null);

        // The owner is answered on both, so none of the refusals above was about the trips.
        (await owner.GetAsync($"{Url(hidden)}?timeZone={Bucharest}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TakeAsync(owner, trip, "*", Bucharest, ticked)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A deleted trip has no roster to propose times for, for anybody.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Url(hidden)}?timeZone={Bucharest}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TakeAsync(owner, hidden, "*", Bucharest, hiddenTicked)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private sealed record RosterRow(Guid Caver, TimeOnly? Entry, TimeOnly? Exit);

    private static object Row(Guid caver, string? entry = null, string? exit = null, long? role = null) =>
        new { caverId = caver, roleId = role, entryTime = entry, exitTime = exit };

    private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    private static JsonElement Person(JsonElement times, Guid caver) =>
        times.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("caverId").GetGuid() == caver);

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> TimesAsync(HttpClient client, Guid trip, string zone)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking/roster-times?timeZone={Uri.EscapeDataString(zone)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>The proposal as its writer reads it, with the version of the trip it was read under.</summary>
    private async Task<(JsonElement Times, string Version)> ReviewAsync(Guid trip, string zone)
    {
        var response = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/roster-times?timeZone={Uri.EscapeDataString(zone)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.ETag.ShouldNotBeNull();
        return (await BodyAsync(response), response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// One ticked person as the write names them: the pair the review proposed, and what the
    /// review showed the roster holding — nothing, and nothing replaced, unless said otherwise.
    /// </summary>
    private sealed record Tick(
        Guid Caver, string Entry, string Exit, string? CurrentEntry = null, string? CurrentExit = null, bool Overwrites = false);

    /// <summary>The write for people reviewed on a roster that held no time for them.</summary>
    private static Task<HttpResponseMessage> TakeAsync(
        HttpClient client, Guid trip, string version, string zone, (Guid Caver, string Entry, string Exit)[] ticked) =>
        TakeAsync(client, trip, version, zone, ticked.Select(p => new Tick(p.Caver, p.Entry, p.Exit)).ToArray());

    private static Task<HttpResponseMessage> TakeAsync(
        HttpClient client, Guid trip, string version, string zone, Tick[] ticked) =>
        client.PostWithIfMatchAsync($"/api/v1/trip-logs/{trip}/tracking/roster-times", new
        {
            timeZone = zone,
            people = ticked.Select(p => new
            {
                caverId = p.Caver,
                entry = p.Entry,
                exit = p.Exit,
                currentEntry = p.CurrentEntry,
                currentExit = p.CurrentExit,
                overwrites = p.Overwrites,
            }).ToArray(),
        }, version);

    private async Task<string> TripVersionAsync(Guid trip)
    {
        var response = await owner.GetAsync($"/api/v1/trip-logs/{trip}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.ToString();
    }

    /// <summary>
    /// Rewrites a trip's roster the way its form does: the list sent is the whole of it. The
    /// title is the same on every save of one trip unless another is asked for, so that a save
    /// which changes only the roster changes only the roster — a title that moved each time
    /// would move the trip's version each time, and every case about that version would pass
    /// for the title's sake.
    /// </summary>
    private Task<HttpResponseMessage> PutRosterAsync(
        Guid trip, IEnumerable<object> participants, string version = "*", string? title = null) =>
        owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = title ?? $"Roster {trip:N}"[..28],
            tripDate = "2026-09-12",
            participants = participants.ToArray(),
            visibility = "authenticated",
        }, version);

    private async Task<List<RosterRow>> RosterRowsAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking().Where(p => p.TripLogId == trip)
            .OrderBy(p => p.Id).Select(p => new RosterRow(p.CaverId, p.EntryTime, p.ExitTime)).ToListAsync();
    }

    private async Task<List<long>> RoleIdsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var roles = await db.TripParticipantRoles.OrderBy(r => r.Id).Select(r => r.Id).Take(2).ToListAsync();
        roles.Count.ShouldBe(2, "a person with two jobs needs two roles in the vocabulary");
        return roles;
    }

    /// <summary>
    /// How much of the trip's history is about roster rows, and how many lines say that times
    /// were taken from the log.
    /// </summary>
    private async Task<(int RosterRows, int Acts)> HistoryAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = trip.ToString();
        var rows = await db.AuditEntries.CountAsync(a =>
            a.RootEntityId == id && a.EntityType == nameof(TripLogParticipant) && a.Action == AuditActions.Updated);
        // The trip's own lines are read and looked through here: what a line says is a document
        // column, and "contains this word" is not a question the database is asked of one.
        var ofTheTrip = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == id && a.EntityType == nameof(TripLog) && a.Changes != null)
            .Select(a => a.Changes!).ToListAsync();
        var acts = ofTheTrip.Count(changes => changes.Contains("fromTracking") && changes.Contains(Bucharest));
        return (rows, acts);
    }

    /// <summary>
    /// A watch that was kept and has been closed, written straight to the table: these tests are
    /// about what is read off a log, and a closed log takes reports without a survey to place
    /// them in.
    /// </summary>
    private async Task FollowedAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripTrackings.Add(new TripTracking { TripLogId = trip, State = TripTrackingState.Closed });
        await db.SaveChangesAsync();
    }

    /// <summary>One report about each of the people named, at the moment given; answers the first one's id.</summary>
    private async Task<Guid> ReportAsync(Guid trip, string kind, DateTimeOffset at, IEnumerable<Guid> cavers)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = cavers.ToArray(), kind, recordedAt = at,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await BodyAsync(response))[0].GetProperty("id").GetGuid();
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(
        string title, int guests, string visibility = "authenticated")
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .OrderBy(p => p.Id).Select(p => p.CaverId).ToListAsync();
        cavers.Count.ShouldBe(guests);
        return (trip, cavers);
    }
}
