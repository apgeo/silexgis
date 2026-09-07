// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two acts that reach past one occurrence of a repeating event: "this and following", and
/// calling off the rest of the run.
/// </summary>
/// <remarks>
/// <para>
/// Both are all-or-nothing, and that is what most of these tests are about. A bulk act that
/// half-applies is worse than one that refuses: the caller is told it worked, half the calendar
/// says otherwise, and nobody finds out until somebody turns up to an evening that was supposed to
/// have moved. So every refusal below is asserted twice — once for the answer, once for the rows,
/// which must be exactly as they were.
/// </para>
/// <para>
/// The account that may act on some occurrences and not others is a plain reader holding only the
/// rules these tests write on it. The seeded editing membership reads and writes past visibility at
/// the widest scope by design, so a refusal aimed at an editor would prove nothing.
/// </para>
/// </remarks>
public sealed class EventSeriesBulkTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient clubmate = null!;
    private HttpClient anonymous = null!;

    private Guid clubmateId;
    private Guid cavingGroupId;
    private Guid clubmateCaver;

    public EventSeriesBulkTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"evb-own-{suffix}@t.local");
        clubmateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"evb-club-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Bulk Club {suffix}", Slug = $"bulk-club-{suffix}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
            clubmateCaver = (await db.Cavers.FirstAsync(c => c.UserId == clubmateId)).Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"evb-own-{suffix}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"evb-club-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// Neither act aimed at a run is reachable signed out. Asserted beside the same two routes
    /// answering a signed-in caller, so this is about being signed out and not about the routes
    /// being unreachable — which is what a route accidentally mapped outside the group that
    /// requires an account would otherwise look like.
    /// </summary>
    [Fact]
    public async Task Nobody_signed_out_reaches_either_act_aimed_at_a_run()
    {
        var day = new DateOnly(2056, 2, 1);
        var (seriesId, ids) = await SeriesAsync("Signed out", day, count: 3);

        var edit = await anonymous.PutAsJsonAsync(
            $"/api/v1/events/{ids[0]}/series/following", Body("Signed out", day));
        edit.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await anonymous.DeleteAsync($"/api/v1/events/{ids[0]}/series/following"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Nothing moved and nothing went.
        (await BySeriesAsync(owner, seriesId)).Count.ShouldBe(3);

        (await EditFollowingAsync(owner, ids[0], Body("Signed in", day)))
            .GetProperty("changed").GetInt32().ShouldBe(3);
        (await DeleteFollowingAsync(owner, ids[0])).GetProperty("deleted").GetInt32().ShouldBe(3);
    }

    /// <summary>
    /// The number of occurrences left standing is a number of occurrences this caller could have
    /// seen anyway.
    /// </summary>
    /// <remarks>
    /// Somebody handed the future of a run — the shape a club uses when one member takes over
    /// running the evenings from a date on — must not learn from the answer how many earlier
    /// evenings of it exist that they may not open. The kept half is the informative half of this
    /// answer, and computing it as "the whole series minus what went" would count rows that never
    /// went through the visibility walk.
    /// </remarks>
    [Fact]
    public async Task What_was_kept_is_counted_over_the_occurrences_the_caller_may_read()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = await CreateAsync(owner, PrivateBody("Handover", today.AddDays(-14)));
        var seriesId = first.GetProperty("seriesId").GetGuid();
        var ids = (await BySeriesAsync(owner, seriesId))
            .OrderBy(x => x.GetProperty("startDate").GetString())
            .Select(Id)
            .ToList();
        ids.Count.ShouldBe(4);

        // The run is the owner's alone, and the last two evenings are handed over by name.
        await GrantAsync(ids[2], AccessAction.Read | AccessAction.Delete);
        await GrantAsync(ids[3], AccessAction.Read | AccessAction.Delete);

        // The two earlier evenings really are out of reach, so what follows is about the count
        // and not about a grant that turned out to be wider than it looked.
        (await clubmate.GetAsync($"/api/v1/events/{ids[0]}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await clubmate.GetAsync($"/api/v1/events/{ids[1]}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var result = await DeleteFollowingAsync(clubmate, ids[2]);
        result.GetProperty("deleted").GetInt32().ShouldBe(2);
        result.GetProperty("kept").GetInt32().ShouldBe(0);

        // The two that were kept are still there, and the owner — who may read them — is told so.
        (await BySeriesAsync(owner, seriesId)).Select(Id).ShouldBe([ids[0], ids[1]], ignoreOrder: true);
    }

    /// <summary>
    /// A move so large that a later occurrence would fall off the end of the calendar is refused,
    /// and refused entire, rather than failing part-way through the run.
    /// </summary>
    [Fact]
    public async Task A_move_that_would_carry_the_run_off_the_calendar_is_refused_and_moves_nothing()
    {
        var day = new DateOnly(2056, 5, 2);
        var (seriesId, ids) = await SeriesAsync("Off the end", day, count: 3);

        var response = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{ids[0]}/series/following",
            Body("Off the end", DateOnly.MaxValue));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        payload.ShouldContain("event.series_move_out_of_range");

        var after = await BySeriesAsync(owner, seriesId);
        after.Count.ShouldBe(3);
        after.Select(x => x.GetProperty("startDate").GetString()).OrderBy(x => x)
            .ShouldBe(["2056-05-02", "2056-05-09", "2056-05-16"]);
    }

    /// <summary>
    /// The pair the whole surface exists to distinguish: an edit aimed at one evening, and an edit
    /// aimed at that evening and the rest of the run. Asserted together, because either one alone
    /// would pass against an implementation that did the other.
    /// </summary>
    [Fact]
    public async Task This_occurrence_changes_one_row_and_this_and_following_changes_the_rest_with_it()
    {
        var (seriesId, ids) = await SeriesAsync("Club night", new DateOnly(2055, 3, 2), count: 5);

        // One occurrence, by the ordinary edit: the third evening moves to the Wednesday and is
        // held somewhere else, and its four siblings do not notice.
        var single = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{ids[2]}",
            Body("Club night", new DateOnly(2055, 3, 17), place: "The other pub"));
        single.StatusCode.ShouldBe(HttpStatusCode.OK, await single.Content.ReadAsStringAsync());

        var afterOne = await BySeriesAsync(owner, seriesId);
        afterOne.Count.ShouldBe(5);
        Day(afterOne, ids[2]).ShouldBe("2055-03-17");
        afterOne.Where(x => Id(x) != ids[2])
            .Select(x => x.GetProperty("place").ValueKind)
            .ShouldAllBe(kind => kind == JsonValueKind.Null);

        // Now the same evening and every later one, in one act. The days keep the spacing they
        // were generated with: the anchor moves by one day and so does everything after it.
        var result = await EditFollowingAsync(
            owner, ids[2], Body("Club night, upstairs", new DateOnly(2055, 3, 18), place: "The Bell"));
        result.GetProperty("changed").GetInt32().ShouldBe(3);
        result.GetProperty("seriesId").GetGuid().ShouldBe(seriesId);
        result.GetProperty("anchor").GetProperty("startDate").GetString().ShouldBe("2055-03-18");

        var after = await BySeriesAsync(owner, seriesId);
        after.Count.ShouldBe(5);

        // The two evenings before the anchor are untouched — the title they had, the day they had,
        // and no place, which is the field the bulk edit wrote on the others.
        foreach (var earlier in new[] { ids[0], ids[1] })
        {
            Title(after, earlier).ShouldBe($"Club night {suffix}");
            after.Single(x => Id(x) == earlier).GetProperty("place").ValueKind
                .ShouldBe(JsonValueKind.Null);
        }

        Day(after, ids[0]).ShouldBe("2055-03-02");
        Day(after, ids[1]).ShouldBe("2055-03-09");

        // The anchor and the two after it carry the edit, each on its own shifted day.
        foreach (var later in new[] { ids[2], ids[3], ids[4] })
        {
            Title(after, later).ShouldBe($"Club night, upstairs {suffix}");
            after.Single(x => Id(x) == later).GetProperty("place").GetString().ShouldBe("The Bell");
            after.Single(x => Id(x) == later).GetProperty("seriesId").GetGuid().ShouldBe(seriesId);
        }

        Day(after, ids[2]).ShouldBe("2055-03-18");
        Day(after, ids[3]).ShouldBe("2055-03-24");
        Day(after, ids[4]).ShouldBe("2055-03-31");
    }

    /// <summary>
    /// A caller who may act on the occurrence they addressed but not on every occurrence the act
    /// would reach changes nothing at all.
    /// </summary>
    [Fact]
    public async Task An_edit_reaching_an_occurrence_the_caller_may_not_write_writes_none_of_them()
    {
        var (seriesId, ids) = await SeriesAsync("Training", new DateOnly(2055, 4, 6), count: 4);

        // A plain reader given the right to change three of the four evenings by name, and only
        // to read the fourth. Written as rules on the rows because that is what "this evening is
        // yours to run" is in this application.
        foreach (var writable in new[] { ids[0], ids[1], ids[2] })
        {
            await GrantAsync(writable, AccessAction.Read | AccessAction.Write);
        }

        await GrantAsync(ids[3], AccessAction.Read);

        var refused = await owner.GetAsync($"/api/v1/events/{ids[0]}");
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The positive half: they really may change the evening they addressed, one at a time.
        var single = await clubmate.PutWithIfMatchAsync(
            $"/api/v1/events/{ids[0]}", Body("Training moved", new DateOnly(2055, 4, 6)));
        single.StatusCode.ShouldBe(HttpStatusCode.OK, await single.Content.ReadAsStringAsync());

        // And the negative half, which is the point: the same edit aimed at the run reaches the
        // evening they may only read, so it is refused entire.
        var response = await clubmate.PutWithIfMatchAsync(
            $"/api/v1/events/{ids[0]}/series/following",
            Body("Training for everyone", new DateOnly(2055, 4, 13)));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, payload);
        payload.ShouldContain("event.series_partly_forbidden");

        // Nothing was written. Not the occurrence they may write, not the day of any of them —
        // a bulk act that had applied to the three it was allowed and stopped at the fourth would
        // pass every assertion above and leave the series in a shape nobody asked for.
        var after = await BySeriesAsync(owner, seriesId);
        after.Count.ShouldBe(4);
        Title(after, ids[0]).ShouldBe($"Training moved {suffix}");
        Title(after, ids[1]).ShouldBe($"Training {suffix}");
        Title(after, ids[2]).ShouldBe($"Training {suffix}");
        Title(after, ids[3]).ShouldBe($"Training {suffix}");
        after.Select(x => x.GetProperty("startDate").GetString()).OrderBy(x => x)
            .ShouldBe(["2055-04-06", "2055-04-13", "2055-04-20", "2055-04-27"]);

        // The owner, who may write all four, does exactly what was refused — so the refusal was
        // about the rights over the set and not about the route.
        (await EditFollowingAsync(owner, ids[0], Body("Training for everyone", new DateOnly(2055, 4, 6))))
            .GetProperty("changed").GetInt32().ShouldBe(4);
    }

    /// <summary>
    /// Calling off the rest of a repeating event leaves what has already happened, and leaves the
    /// answers people gave about it reachable.
    /// </summary>
    [Fact]
    public async Task Calling_off_the_rest_of_a_series_keeps_the_evenings_that_already_happened()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (seriesId, ids) = await SeriesAsync("Committee", today.AddDays(-14), count: 4);

        // Somebody said they were coming to the first evening, which has been and gone. That
        // answer is the record of who was there, and it is what a delete aimed at the future must
        // not quietly take with it.
        var asked = await owner.PostAsJsonAsync(
            $"/api/v1/events/{ids[0]}/invitations", new { caverId = clubmateCaver });
        asked.StatusCode.ShouldBe(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());

        // Addressed to the first occurrence — the earliest thing anybody could aim "the rest of
        // this series" at — and it still stops at today.
        var result = await DeleteFollowingAsync(owner, ids[0]);
        result.GetProperty("seriesId").GetGuid().ShouldBe(seriesId);
        result.GetProperty("deleted").GetInt32().ShouldBe(2);
        result.GetProperty("kept").GetInt32().ShouldBe(2);

        var after = await BySeriesAsync(owner, seriesId);
        after.Select(Id).OrderBy(x => x).ShouldBe([ids[0], ids[1]], ignoreOrder: true);

        // The kept evenings are whole events still: openable, and still carrying what was said
        // about them.
        (await owner.GetAsync($"/api/v1/events/{ids[0]}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(owner, $"/api/v1/events/{ids[0]}/invitations"))
            .GetProperty("invitations").GetArrayLength().ShouldBe(1);

        // And the same act aimed at a run that is entirely ahead of us takes all of it, so what
        // was kept above was kept for having happened and not for anything else.
        var (futureSeries, futureIds) = await SeriesAsync("Rescue practice", today.AddDays(30), count: 3);
        var future = await DeleteFollowingAsync(owner, futureIds[1]);
        future.GetProperty("deleted").GetInt32().ShouldBe(2);
        future.GetProperty("kept").GetInt32().ShouldBe(1);
        (await BySeriesAsync(owner, futureSeries)).Select(Id).ShouldBe([futureIds[0]]);
    }

    /// <summary>
    /// Neither act reaches an event outside the series it was addressed through, and neither is
    /// available at all on an event that is part of no series.
    /// </summary>
    [Fact]
    public async Task Nothing_reaches_out_of_the_series_it_was_addressed_through()
    {
        var day = new DateOnly(2055, 6, 1);
        var (_, first) = await SeriesAsync("Tuesday night", day, count: 3);
        var (secondSeries, second) = await SeriesAsync("Tuesday night", day, count: 3);

        // A one-off on the same day, to catch a query that narrowed by date and forgot the key.
        var alone = Id(await CreateAsync(owner, Body("Tuesday night", day)));

        (await EditFollowingAsync(owner, first[0], Body("Renamed", day)))
            .GetProperty("changed").GetInt32().ShouldBe(3);

        var others = await BySeriesAsync(owner, secondSeries);
        others.Count.ShouldBe(3);
        others.Select(x => x.GetProperty("title").GetString()).Distinct()
            .ShouldBe([$"Tuesday night {suffix}"]);
        (await ReadJsonAsync(owner, $"/api/v1/events/{alone}"))
            .GetProperty("title").GetString().ShouldBe($"Tuesday night {suffix}");

        (await DeleteFollowingAsync(owner, second[0])).GetProperty("deleted").GetInt32().ShouldBe(3);
        (await ReadJsonAsync(owner, $"/api/v1/events/{first[0]}"))
            .GetProperty("title").GetString().ShouldBe($"Renamed {suffix}");
        (await owner.GetAsync($"/api/v1/events/{alone}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // An event belonging to no series has nothing following it, and is told so rather than
        // being quietly treated as a series of one.
        var edit = await owner.PutWithIfMatchAsync(
            $"/api/v1/events/{alone}/series/following", Body("Tuesday night", day));
        edit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await edit.Content.ReadAsStringAsync()).ShouldContain("event.not_in_series");

        var delete = await owner.DeleteAsync($"/api/v1/events/{alone}/series/following");
        delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await delete.Content.ReadAsStringAsync()).ShouldContain("event.not_in_series");
        (await owner.GetAsync($"/api/v1/events/{alone}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private object Body(
        string title,
        DateOnly startDate,
        DateOnly? endDate = null,
        string? place = null,
        object? recurrence = null) => new
        {
            title = $"{title} {suffix}",
            kind = "clubMeeting",
            startDate = startDate.ToString("yyyy-MM-dd"),
            endDate = endDate?.ToString("yyyy-MM-dd"),
            place,
            visibility = "cavingGroup",
            cavingGroupId,
            recurrence,
        };

    /// <summary>A weekly run of the given length, and the identifiers of its occurrences in order.</summary>
    private async Task<(Guid SeriesId, List<Guid> Ids)> SeriesAsync(string title, DateOnly start, int count)
    {
        var first = await CreateAsync(owner, Body(
            title, start,
            recurrence: new { frequency = "weekly", count, rule = "Every week" }));
        var seriesId = first.GetProperty("seriesId").GetGuid();
        var ids = (await BySeriesAsync(owner, seriesId))
            .OrderBy(x => x.GetProperty("startDate").GetString())
            .Select(Id)
            .ToList();
        ids.Count.ShouldBe(count);
        return (seriesId, ids);
    }

    private async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/events", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<JsonElement> EditFollowingAsync(HttpClient client, Guid anchor, object body)
    {
        var response = await client.PutWithIfMatchAsync(
            $"/api/v1/events/{anchor}/series/following", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> DeleteFollowingAsync(HttpClient client, Guid anchor)
    {
        var response = await client.DeleteAsync($"/api/v1/events/{anchor}/series/following");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>A rule on one row, naming one account: what "this evening is yours" is here.</summary>
    private async Task GrantAsync(Guid eventId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = clubmateId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Events,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = eventId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>
    /// The occurrences of one series this caller reaches. Narrowed by the suffix as well as the
    /// key, because the fixture shares one database with every other suite.
    /// </summary>
    private async Task<List<JsonElement>> BySeriesAsync(HttpClient client, Guid seriesId)
    {
        var page = await ReadJsonAsync(
            client, $"/api/v1/events?pageSize=200&search={suffix}&seriesId={seriesId}");
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    /// <summary>A run only its author may read, so a grant on one occurrence is the whole of
    /// what anybody else has.</summary>
    private object PrivateBody(string title, DateOnly start) => new
    {
        title = $"{title} {suffix}",
        kind = "clubMeeting",
        startDate = start.ToString("yyyy-MM-dd"),
        visibility = "private",
        recurrence = new { frequency = "weekly", count = 4, rule = "Every week" },
    };

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    private static string? Day(IEnumerable<JsonElement> rows, Guid id) =>
        rows.Single(x => Id(x) == id).GetProperty("startDate").GetString();

    private static string? Title(IEnumerable<JsonElement> rows, Guid id) =>
        rows.Single(x => Id(x) == id).GetProperty("title").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
