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
using SilexGis.Domain.Events;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A repeating event, which is a run of ordinary events written in one act.
/// </summary>
/// <remarks>
/// <para>
/// The claim these tests exist to hold is that <em>nothing else knows a series exists</em>. An
/// occurrence is opened, edited, deleted, filtered, put on the calendar and answered by exactly
/// the mechanisms a one-off event goes through, with no arm in any of them asking which occurrence
/// it is looking at — so the assertions below drive those mechanisms rather than assuming them.
/// </para>
/// <para>
/// The accounts every negative case is about are plain readers holding nothing. The seeded editing
/// membership reads past visibility at the widest scope by design, so "an editor could not see it"
/// would prove nothing about the audience; each refusal sits beside the reading it is the shadow
/// of.
/// </para>
/// </remarks>
public sealed class EventSeriesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient clubmate = null!;
    private HttpClient outsider = null!;

    private Guid ownerId;
    private Guid cavingGroupId;
    private Guid clubmateCaver;

    public EventSeriesTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"evs-own-{suffix}@t.local");
        var clubmateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"evs-club-{suffix}@t.local");

        // Holds no membership and no grant of any kind, which is what makes every "cannot reach
        // it" assertion below about the audience rather than about a missing editing right.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"evs-out-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Series Club {suffix}", Slug = $"series-club-{suffix}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
            clubmateCaver = (await db.Cavers.FirstAsync(c => c.UserId == clubmateId)).Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"evs-own-{suffix}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"evs-club-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"evs-out-{suffix}@t.local");
    }

    [Fact]
    public async Task A_series_is_written_as_exactly_the_rows_its_bounds_allow()
    {
        var first = await CreateAsync(owner, Body(
            $"Club night {suffix}",
            new DateOnly(2054, 3, 3),
            recurrence: Repeat("weekly", count: 6, rule: "Every Tuesday")));

        // The create answers with the first occurrence, because that is the row somebody is taken
        // to — and it carries the key its siblings share and the words describing the pattern.
        var seriesId = first.GetProperty("seriesId").GetGuid();
        first.GetProperty("seriesRule").GetString().ShouldBe("Every Tuesday");
        first.GetProperty("startDate").GetString().ShouldBe("2054-03-03");

        var occurrences = await ListAsync(owner, $"seriesId={seriesId}");
        occurrences.Count.ShouldBe(6);
        occurrences.Select(x => x.GetProperty("startDate").GetString())
            .OrderBy(x => x)
            .ShouldBe(["2054-03-03", "2054-03-10", "2054-03-17", "2054-03-24", "2054-03-31", "2054-04-07"]);

        // Every occurrence is a whole event and not a shadow of the first: its own identifier, the
        // same audience, the same kind, and nothing marking it as derived.
        occurrences.Select(Id).Distinct().Count().ShouldBe(6);
        foreach (var row in occurrences)
        {
            row.GetProperty("seriesId").GetGuid().ShouldBe(seriesId);
            row.GetProperty("seriesRule").GetString().ShouldBe("Every Tuesday");
            row.GetProperty("title").GetString().ShouldBe($"Club night {suffix}");
            row.GetProperty("kind").GetString().ShouldBe("clubMeeting");
            row.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);
            row.GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        // And an event nobody asked to repeat belongs to no series, so the key above means
        // something rather than being written on everything.
        var oneOff = await CreateAsync(owner, Body($"One-off {suffix}", new DateOnly(2054, 5, 5)));
        oneOff.GetProperty("seriesId").ValueKind.ShouldBe(JsonValueKind.Null);
        oneOff.GetProperty("seriesRule").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_series_with_no_bound_or_past_a_bound_is_refused_and_writes_nothing()
    {
        var day = new DateOnly(2054, 6, 2);

        // Nothing at all says where it stops. This is the refusal the whole generator exists for:
        // "every week, for ever" is four words to type and there is nothing in a table that
        // pushes back on it.
        await RefusedAsync(
            Body($"Unbounded {suffix}", day, recurrence: Repeat("weekly", rule: "Every week")),
            EventRecurrence.UnboundedCode);

        await RefusedAsync(
            Body($"Too many {suffix}", day,
                recurrence: Repeat("weekly", count: EventRecurrence.MaxOccurrences + 1, rule: "Weekly")),
            EventRecurrence.TooManyCode);

        // Inside the horizon and still far too many rows, so the count ceiling holds even when
        // the caller bounded the series by a date instead.
        await RefusedAsync(
            Body($"Too many days {suffix}", day,
                recurrence: Repeat("daily", until: day.AddDays(300), rule: "Every day")),
            EventRecurrence.TooManyCode);

        await RefusedAsync(
            Body($"Too far {suffix}", day,
                recurrence: Repeat(
                    "monthly",
                    until: day.AddDays(EventRecurrence.MaxHorizonDays + 1),
                    rule: "Monthly")),
            EventRecurrence.HorizonTooFarCode);

        await RefusedAsync(
            Body($"Once {suffix}", day, recurrence: Repeat("weekly", count: 1, rule: "Once")),
            EventRecurrence.NotRepeatingCode);

        // Not one row of any of them was written. A refusal that had already saved the first
        // occurrence would leave somebody an event they never agreed to and no sign of the rest.
        (await ListAsync(owner, null)).ShouldBeEmpty();

        // The same request one step inside every limit is written, so what was refused above is
        // the bounds and not the repeating.
        var allowed = await CreateAsync(owner, Body(
            $"Allowed {suffix}", day,
            recurrence: Repeat("weekly", count: EventRecurrence.MaxOccurrences, rule: "Weekly")));
        (await ListAsync(owner, $"seriesId={allowed.GetProperty("seriesId").GetGuid()}"))
            .Count.ShouldBe(EventRecurrence.MaxOccurrences);
    }

    [Fact]
    public async Task The_words_describing_a_series_are_carried_back_and_read_by_nothing()
    {
        // The sentence deliberately contradicts the repetition and is dressed up as a machine
        // rule. If anything anywhere parsed it, these rows would be a day apart; they are a week
        // apart, because the days came from the repetition the author picked and the sentence is
        // for a person to read.
        const string Words = "FREQ=DAILY;BYDAY=MO — every single day, honestly (ședința săptămânală)";

        var first = await CreateAsync(owner, Body(
            $"Rule text {suffix}", new DateOnly(2054, 7, 7),
            recurrence: Repeat("weekly", count: 3, rule: Words)));

        var occurrences = await ListAsync(owner, $"seriesId={first.GetProperty("seriesId").GetGuid()}");
        occurrences.Select(x => x.GetProperty("startDate").GetString())
            .OrderBy(x => x)
            .ShouldBe(["2054-07-07", "2054-07-14", "2054-07-21"]);

        // Byte for byte, accents and punctuation included, on every occurrence.
        foreach (var row in occurrences)
        {
            row.GetProperty("seriesRule").GetString().ShouldBe(Words);
        }
    }

    [Fact]
    public async Task An_occurrence_that_runs_more_than_a_day_keeps_its_length()
    {
        var first = await CreateAsync(owner, Body(
            $"Course {suffix}", new DateOnly(2054, 8, 3),
            endDate: new DateOnly(2054, 8, 5),
            recurrence: Repeat("weekly", count: 3, rule: "Three days, weekly")));

        var occurrences = await ListAsync(owner, $"seriesId={first.GetProperty("seriesId").GetGuid()}");
        occurrences
            .Select(x => (x.GetProperty("startDate").GetString(), x.GetProperty("endDate").GetString()))
            .OrderBy(x => x.Item1)
            .ShouldBe([("2054-08-03", "2054-08-05"), ("2054-08-10", "2054-08-12"), ("2054-08-17", "2054-08-19")]);
    }

    [Fact]
    public async Task A_repetition_sent_to_the_edit_of_one_event_is_refused()
    {
        var id = Id(await CreateAsync(owner, Body($"Single {suffix}", new DateOnly(2054, 9, 1))));

        var response = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{id}",
            Body($"Single {suffix}", new DateOnly(2054, 9, 1),
                recurrence: Repeat("weekly", count: 4, rule: "Weekly")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("event.recurrence_create_only");

        // The row is untouched, and the same edit without the repetition is accepted — so the
        // refusal is about the repetition and not about the edit.
        var edited = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{id}", Body($"Single renamed {suffix}", new DateOnly(2054, 9, 1)));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The batch's central claim, driven rather than assumed: each occurrence is reached, read,
    /// edited, answered, filtered, put on the calendar and deleted by the mechanisms that were
    /// already there, none of which was told a series exists.
    /// </summary>
    [Fact]
    public async Task Every_occurrence_is_an_ordinary_event_to_everything_that_was_already_there()
    {
        var first = await CreateAsync(owner, Body(
            $"Ordinary {suffix}", new DateOnly(2054, 10, 6),
            recurrence: Repeat("weekly", count: 4, rule: "Every Tuesday")));
        var seriesId = first.GetProperty("seriesId").GetGuid();
        var ids = (await ListAsync(owner, $"seriesId={seriesId}"))
            .OrderBy(x => x.GetProperty("startDate").GetString()).Select(Id).ToList();

        // Read: its own page, its own version token — the precondition every edit of it carries.
        var read = await owner.GetAsync($"/api/v1/events/{ids[1]}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = read.Headers.ETag!.Tag;
        etag.ShouldNotBeNullOrWhiteSpace();

        // Edit: one occurrence, on its own version, leaving its siblings exactly as they were.
        // This is what "a per-occurrence exception" means here — there is no exception mechanism,
        // only a row somebody changed.
        var edited = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{ids[1]}",
            Body($"Moved this week {suffix}", new DateOnly(2054, 10, 14), place: "The other pub"),
            etag);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        var afterEdit = await ListAsync(owner, $"seriesId={seriesId}");
        afterEdit.Count.ShouldBe(4);
        afterEdit.Single(x => Id(x) == ids[1]).GetProperty("startDate").GetString().ShouldBe("2054-10-14");
        afterEdit.Single(x => Id(x) == ids[2]).GetProperty("title").GetString().ShouldBe($"Ordinary {suffix}");

        // The edited occurrence is still in the series: changing one evening does not remove it
        // from the run it belongs to.
        afterEdit.Single(x => Id(x) == ids[1]).GetProperty("seriesId").GetGuid().ShouldBe(seriesId);

        // Answered: the response mechanism keys on one event's identifier, and an occurrence is
        // one event, so somebody says yes to a single evening rather than to the whole year.
        var asked = await owner.PostAsJsonAsync(
            $"/api/v1/events/{ids[0]}/invitations", new { caverId = clubmateCaver });
        asked.StatusCode.ShouldBe(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());

        var answers = await ReadJsonAsync(owner, $"/api/v1/events/{ids[0]}/invitations");
        answers.GetProperty("invitations").GetArrayLength().ShouldBe(1);

        // And the next occurrence has nobody on it, which is the whole reason occurrences are
        // rows: one standing answer per person per event, and a different evening is a different
        // event.
        (await ReadJsonAsync(owner, $"/api/v1/events/{ids[1]}/invitations"))
            .GetProperty("invitations").GetArrayLength().ShouldBe(0);

        // The visibility walk: the audience is the club, so a clubmate reads every occurrence and
        // a plain reader holding nothing reads none of them — asserted together, so a walk that
        // hid everything would not pass either half.
        (await ListAsync(clubmate, $"seriesId={seriesId}")).Count.ShouldBe(4);
        (await ListAsync(outsider, $"seriesId={seriesId}")).ShouldBeEmpty();
        (await outsider.GetAsync($"/api/v1/events/{ids[0]}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The calendar: once the occurrences say there is a date, each falls in its own day cell
        // with no idea that it is one of four.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Events.Where(x => x.SeriesId == seriesId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, ActivityState.Published));
        }

        var entries = (await ReadJsonAsync(owner, "/api/v1/calendar?from=2054-10-01&to=2054-10-31"))
            .GetProperty("entries").EnumerateArray()
            .Where(x => ids.Contains(x.GetProperty("id").GetGuid()))
            .ToList();
        entries.Count.ShouldBe(4);
        entries.Select(x => x.GetProperty("start").GetString()).OrderBy(x => x)
            .ShouldBe(["2054-10-06", "2054-10-14", "2054-10-20", "2054-10-27"]);

        // Deleted: one occurrence goes and the rest of the series stands, because there is no
        // parent to take with it.
        (await owner.DeleteAsync($"/api/v1/events/{ids[3]}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(owner, $"seriesId={seriesId}")).Count.ShouldBe(3);
    }

    private object Body(
        string title,
        DateOnly startDate,
        DateOnly? endDate = null,
        string kind = "clubMeeting",
        string? place = null,
        object? recurrence = null) => new
        {
            title,
            kind,
            startDate = startDate.ToString("yyyy-MM-dd"),
            endDate = endDate?.ToString("yyyy-MM-dd"),
            place,
            visibility = "cavingGroup",
            cavingGroupId,
            recurrence,
        };

    private static object Repeat(string frequency, int? count = null, DateOnly? until = null, string? rule = null) =>
        new { frequency, count, until = until?.ToString("yyyy-MM-dd"), rule };

    private async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/events", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task RefusedAsync(object body, string code)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/events", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        payload.ShouldContain(code);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>
    /// The events this caller reaches, narrowed to the ones this class made — the fixture shares
    /// one database with every other suite, so an unnarrowed list would be the afternoon's other
    /// tests.
    /// </summary>
    private async Task<List<JsonElement>> ListAsync(HttpClient client, string? query)
    {
        var url = $"/api/v1/events?pageSize=200&search={suffix}"
            + (query is null ? string.Empty : $"&{query}");
        var page = await ReadJsonAsync(client, url);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
