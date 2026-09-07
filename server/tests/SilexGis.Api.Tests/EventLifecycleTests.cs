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
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Where an event has got to: the moves its own table admits, the refusals for the ones it does
/// not, who is allowed to make them, the version precondition they are made against, and what a
/// state does to whether the event reaches a calendar at all.
/// </summary>
/// <remarks>
/// The accounts the negative cases are about are plain readers. The seeded editing membership
/// reads and writes past visibility at the widest scope, so "an editor could not do it" would
/// prove nothing about the rule under test — it would prove only that the account held no
/// editing rights. Every negative case here sits beside the positive one it is the shadow of.
/// </remarks>
public sealed class EventLifecycleTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;

    // In the club the events below are bound to, so she may read them and nothing more.
    private HttpClient clubmate = null!;

    // In no club and granted nothing, so the club's events are not there for her at all.
    private HttpClient outsider = null!;

    private Guid ownerId;
    private Guid cavingGroupId;

    public EventLifecycleTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"evs-own-{suffix}@t.local");
        var clubmateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"evs-club-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"evs-out-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"State Club {suffix}", Slug = $"state-club-{suffix}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"evs-own-{suffix}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"evs-club-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"evs-out-{suffix}@t.local");
    }

    /// <summary>
    /// The table is asked once and answers both questions: a move it admits is made, and one it
    /// does not is refused under the code that names the kind that refused it — so a caller can
    /// tell an event's refusal from a trip's or a camp's.
    /// </summary>
    [Fact]
    public async Task An_event_makes_the_moves_its_own_table_admits_and_no_others()
    {
        var id = await NewEventAsync($"Club night {suffix}", new DateOnly(2054, 3, 4));

        (await MoveAsync(id, "planned")).GetProperty("state").GetString().ShouldBe("planned");

        // The ladder is climbed one rung at a time: announcing something that has not happened
        // yet skips two decisions, so the table has no such pair and the refusal is a conflict
        // rather than a bad request — the request was well formed and the event was not there.
        var skipped = await owner.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "published" });
        skipped.StatusCode.ShouldBe(HttpStatusCode.Conflict, await skipped.Content.ReadAsStringAsync());
        (await CodeAsync(skipped)).ShouldBe("event.state_transition_invalid");

        (await MoveAsync(id, "confirmed")).GetProperty("state").GetString().ShouldBe("confirmed");
        (await MoveAsync(id, "done")).GetProperty("state").GetString().ShouldBe("done");

        (await MoveAsync(id, "published")).GetProperty("state").GetString().ShouldBe("published");

        // Read back from the row rather than taken from the answer, so the two readings compared
        // below have been through the same storage and differ in nothing but the stamp itself.
        var firstAnnouncement = (await ReadAsync(owner, id)).GetProperty("publishedAt").GetString();
        firstAnnouncement.ShouldNotBeNull();

        // Un-announcing and announcing again does not rewrite the day it was first made known.
        (await MoveAsync(id, "draft")).GetProperty("state").GetString().ShouldBe("draft");
        (await MoveAsync(id, "published")).GetProperty("state").GetString().ShouldBe("published");
        (await ReadAsync(owner, id)).GetProperty("publishedAt").GetString()
            .ShouldBe(firstAnnouncement);

        // An event that happened cannot be made not to have happened.
        var uncancellable = await owner.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "cancelled" });
        uncancellable.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(uncancellable)).ShouldBe("event.state_transition_invalid");

        // A value outside the vocabulary altogether is the request's shape refusing it rather
        // than the table: the table answers about moves between states this application has, and
        // a number that names no state is not a move it should ever be asked about.
        var nonsense = await owner.PostWithIfMatchAsync($"/api/v1/events/{id}/state", new { state = 99 });
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nonsense.Content.ReadAsStringAsync());
        (await CodeAsync(nonsense)).ShouldBe("validation.failed");
    }

    /// <summary>
    /// A body that names no state at all. The vocabulary's first member is the zero value and
    /// every live state has a legal move back to it, so a request specifying nothing would move
    /// an event to the workshop and answer 200 — un-announcing it on a body that asked for
    /// nothing. The refusal has to come from the request's shape, because the transition table
    /// cannot tell an absent field from a deliberate one.
    /// </summary>
    [Fact]
    public async Task A_transition_that_names_no_state_is_refused_rather_than_read_as_the_first_one()
    {
        var id = await NewEventAsync($"Stateless move {suffix}", new DateOnly(2054, 4, 1));
        (await MoveAsync(id, "planned")).GetProperty("state").GetString().ShouldBe("planned");

        var empty = await owner.PostWithIfMatchAsync($"/api/v1/events/{id}/state", new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync());
        (await CodeAsync(empty)).ShouldBe("validation.failed");

        // An explicit null is the same request said another way, and is refused the same.
        var nulled = await owner.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = (string?)null });
        nulled.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nulled.Content.ReadAsStringAsync());
        (await CodeAsync(nulled)).ShouldBe("validation.failed");

        // And neither of them moved it, which is the whole point: the row is where it was.
        (await ReadAsync(owner, id)).GetProperty("state").GetString().ShouldBe("planned");
    }

    /// <summary>
    /// Moving an event is a change to it, so it takes the right to change it and not the right
    /// to read it. Somebody who may read it is told so; somebody who may not is told nothing
    /// beyond that there is nothing there.
    /// </summary>
    [Fact]
    public async Task Only_somebody_who_may_change_the_event_moves_it()
    {
        var id = await NewEventAsync($"Guarded night {suffix}", new DateOnly(2054, 5, 6));

        using var anonymous = factory.CreateClient();
        (await anonymous.PostWithIfMatchAsync($"/api/v1/events/{id}/state", new { state = "planned" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // She may read it — the assertion beside the refusal, so that "she cannot move it" is
        // about the right to change and not about her being unable to find the row.
        (await ReadAsync(clubmate, id)).GetProperty("id").GetGuid().ShouldBe(id);
        var refused = await clubmate.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "planned" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        // She is in no club and holds no entry, so the event is not there for her at all and the
        // refusal says so — a forbidden would tell her the row exists.
        (await outsider.GetAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var invisible = await outsider.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "planned" });
        invisible.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(invisible)).ShouldBe("event.not_found");

        // The positive: the account that owns it moves it, so none of the refusals above is the
        // route being shut to everybody.
        (await MoveAsync(id, "planned")).GetProperty("state").GetString().ShouldBe("planned");
    }

    /// <summary>
    /// The move is made against the version the caller was shown. Two people announcing and
    /// un-announcing the same evening otherwise land in whichever order the database happened to
    /// see, and the second one never learns that the first happened.
    /// </summary>
    [Fact]
    public async Task The_version_the_caller_last_read_is_required_and_checked()
    {
        var id = await NewEventAsync($"Precondition {suffix}", new DateOnly(2054, 6, 7));

        var bare = await owner.PostAsJsonAsync($"/api/v1/events/{id}/state", new { state = "planned" });
        bare.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await bare.Content.ReadAsStringAsync());
        (await CodeAsync(bare)).ShouldBe("concurrency.if_match_required");

        var stale = await owner.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "planned" }, "\"1\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // The version the read handed over is accepted, which is what makes the header something
        // a client can actually satisfy rather than a wall.
        var read = await owner.GetAsync($"/api/v1/events/{id}");
        var etag = read.Headers.ETag!.Tag;
        var moved = await owner.PostWithIfMatchAsync(
            $"/api/v1/events/{id}/state", new { state = "planned" }, etag);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// What a state does to the calendar, and what it does not do to who may read the row. An
    /// event in the workshop is kept off the grid and stays exactly as readable as it was; one
    /// that has reached a state with a date behind it appears, carrying its own wall-clock times
    /// and no position of any kind.
    /// </summary>
    [Fact]
    public async Task The_calendar_carries_an_event_once_its_state_says_there_is_a_date()
    {
        var window = "from=2054-09-01&to=2054-09-30";

        var drafted = await NewEventAsync($"Unfinished {suffix}", new DateOnly(2054, 9, 10));
        var meeting = await NewEventAsync(
            $"Club night {suffix}", new DateOnly(2054, 9, 17),
            startTime: new TimeOnly(19, 0), endTime: new TimeOnly(22, 30));
        (await MoveAsync(meeting, "planned")).GetProperty("state").GetString().ShouldBe("planned");

        var rows = await EntriesAsync(owner, window);
        rows.Select(Id).ShouldNotContain(drafted);

        // The draft is off the grid and is not one bit less readable for it: the same account, in
        // the same test, still opens its own page. Keeping a row off a calendar is a display
        // rule; deciding who may read it is a permission, and only one of those is answered by
        // what state a row is in.
        (await ReadAsync(owner, drafted)).GetProperty("state").GetString().ShouldBe("draft");

        var row = rows.Single(x => Id(x) == meeting);
        row.GetProperty("source").GetString().ShouldBe("event");
        row.GetProperty("title").GetString().ShouldBe($"Club night {suffix}");
        row.GetProperty("start").GetString().ShouldBe("2054-09-17");
        row.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("placement").GetString().ShouldBe("ahead");
        row.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        // The times travel as they were written, with nothing having read either as an instant
        // in a zone: a 19:00 club night is 19:00 on the grid wherever it is drawn.
        row.GetProperty("startTime").GetString().ShouldStartWith("19:00");
        row.GetProperty("endTime").GetString().ShouldStartWith("22:30");

        // An event has no geometry at all, so the flag that says whether a row carries one is
        // false rather than absent — and no coordinate reaches the calendar from here.
        row.GetProperty("hasPosition").GetBoolean().ShouldBeFalse();

        // The row says which of the six kinds it is. A source word alone would make a permit
        // deadline and a social evening the same row, which is the one thing a reader scanning a
        // month is trying to tell apart.
        row.GetProperty("kind").GetString().ShouldBe("clubMeeting");

        // The source filter narrows to it, and the narrowing is a real one: asking for the trips
        // in the same window does not answer with the event.
        (await EntriesAsync(owner, $"{window}&source=event")).Select(Id).ShouldContain(meeting);
        (await EntriesAsync(owner, $"{window}&source=tripLog")).Select(Id).ShouldNotContain(meeting);

        // Several families at once, which is how "everything except the trips" is asked for now
        // that there are three of them. A single-valued narrowing could only ever name one, and
        // the family it left out would vanish from a record that says nothing is missing.
        var withoutTrips = await EntriesAsync(owner, $"{window}&source=expedition,event");
        withoutTrips.Select(Id).ShouldContain(meeting);
        (await EntriesAsync(owner, $"{window}&source=tripLog,expedition")).Select(Id)
            .ShouldNotContain(meeting);

        // One bad word in a list is the whole list refused, and named, rather than quietly
        // dropped — a narrowing that silently ignored half of what it was handed answers a
        // question nobody asked.
        var refused = await owner.GetAsync($"/api/v1/calendar?{window}&source=event,banana");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("calendar.source_invalid");

        // Visibility is walked before the state rule and is never widened by it: the club sees
        // the club's event, and an account outside the club does not — while the same request
        // does answer, so the empty half is about this row and not about a refused window.
        (await EntriesAsync(clubmate, window)).Select(Id).ShouldContain(meeting);
        (await EntriesAsync(outsider, window)).Select(Id).ShouldNotContain(meeting);
    }

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    /// <summary>
    /// An event written straight into the table rather than through the create endpoint. What is
    /// under test here is the state route and the version token it is checked against, and a
    /// fixture built through the write path would make every lifecycle assertion depend on that
    /// path's validation as well — so a refusal there would read as a lifecycle failure.
    /// </summary>
    private async Task<Guid> NewEventAsync(
        string title, DateOnly startDate, TimeOnly? startTime = null, TimeOnly? endTime = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = new Event
        {
            Title = title,
            OwnerUserId = ownerId,
            CavingGroupId = cavingGroupId,
            Visibility = Visibility.CavingGroup,
            Kind = EventKind.ClubMeeting,
            StartDate = startDate,
            StartTime = startTime,
            EndTime = endTime,
        };
        db.Events.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<JsonElement> MoveAsync(Guid id, string state)
    {
        var response = await owner.PostWithIfMatchAsync($"/api/v1/events/{id}/state", new { state });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/events/{id}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<List<JsonElement>> EntriesAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/calendar?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.Clone()
            .GetProperty("entries").EnumerateArray()];
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        clubmate?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }
}
