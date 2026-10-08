// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// One day is stored one way per kind of record, and every surface that reads the days reads it
/// that way.
/// </summary>
/// <remarks>
/// <para>
/// A trip is written down as a finished thing, so for it — as for a camp — no last day means one
/// day, and a last day equal to the first is folded into that wherever the trip is written. A stay
/// at a camp is recorded while the camp is still going, so for it no last day means the person has
/// not left, and one day keeps its day. The two rules are opposite on purpose, and the tests here
/// put them side by side on the same surfaces so that neither can be "tidied" into the other.
/// </para>
/// <para>
/// The readers are asserted over rows written through the ordinary routes rather than over rows
/// placed in the table, because the question is what somebody who typed the same day twice then
/// sees on the calendar, in the feed their phone polls and in the documents the club files.
/// </para>
/// </remarks>
public sealed class OneDaySpanTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient ana = null!; // Editor; writes every row and is the person the stays name
    private Guid anaId;
    private Guid anaCaver;
    private long memberRoleId;
    private long cookRoleId;

    public OneDaySpanTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-oneday-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Protection:CalendarFeedEnabled"] = "true",
        });
    }

    public async Task InitializeAsync()
    {
        anaId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"oneday-ana-{suffix}@t.local");
        anaCaver = await RosterHelper.CaverIdForAsync(factory, anaId);
        ana = await AuthHelper.BearerClientAsync(factory, $"oneday-ana-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        memberRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == ExpeditionRosterRoleSeeds.MemberCode).Select(r => r.Id).SingleAsync();
        cookRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == "cook").Select(r => r.Id).SingleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        ana.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    // ---- a trip: written ---------------------------------------------------------------------

    [Fact]
    public async Task A_trip_ending_the_day_it_starts_is_stored_with_no_end_when_created_and_when_edited()
    {
        // A form that offers a range has no way to say "one day" other than by picking the same
        // day twice, so the request is taken and the equal end is folded away on the server. The
        // client used to be the only place that did this, which is why the table could hold both
        // spellings of one day.
        var created = await ana.PostAsJsonAsync("/api/v1/trip-logs/", TripBody("Same day twice", "2031-05-10", "2031-05-10"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trip = await created.Content.ReadFromJsonAsync<JsonElement>();
        var tripId = trip.GetProperty("id").GetGuid();
        trip.GetProperty("tripDateEnd").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StoredEndAsync(tripId)).ShouldBeNull();

        // The control, so the assertion above is about the equal end and not about every end: a
        // trip that ran on keeps the day it ran on to.
        var ranOn = await ana.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Same day twice", "2031-05-10", "2031-05-12"));
        ranOn.StatusCode.ShouldBe(HttpStatusCode.OK, await ranOn.Content.ReadAsStringAsync());
        (await StoredEndAsync(tripId)).ShouldBe(new DateOnly(2031, 5, 12));

        // And the edit door folds it exactly as the create door does — cut back to its first day,
        // the trip is one day again and not a range of itself.
        var cutBack = await ana.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}", TripBody("Same day twice", "2031-05-10", "2031-05-10"));
        cutBack.StatusCode.ShouldBe(HttpStatusCode.OK, await cutBack.Content.ReadAsStringAsync());
        (await cutBack.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("tripDateEnd").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StoredEndAsync(tripId)).ShouldBeNull();
    }

    [Fact]
    public async Task A_trip_ending_before_it_starts_is_refused_and_never_turned_into_one_day()
    {
        var backwards = await ana.PostAsJsonAsync(
            "/api/v1/trip-logs/", TripBody("Mistyped", "2031-05-10", "2031-05-08"));

        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await backwards.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_database_refuses_a_trip_whose_end_is_not_after_its_start()
    {
        // The rule is held where no writer can miss it, as a camp's is. Both halves: the equal end
        // a writer that skipped the fold would leave, and the backwards one a writer that skipped
        // the refusal would.
        foreach (var end in new[] { new DateOnly(2031, 5, 10), new DateOnly(2031, 5, 9) })
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.TripLogs.Add(new TripLog
            {
                Title = $"Past the write path {suffix}",
                OwnerUserId = anaId,
                TripDate = new DateOnly(2031, 5, 10),
                TripDateEnd = end,
            });

            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // And it takes what it should: no end, and an end the day after.
        using var taking = factory.Services.CreateScope();
        var store = taking.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        store.TripLogs.Add(new TripLog
        {
            Title = $"One day {suffix}", OwnerUserId = anaId, TripDate = new DateOnly(2031, 5, 10),
        });
        store.TripLogs.Add(new TripLog
        {
            Title = $"Two days {suffix}",
            OwnerUserId = anaId,
            TripDate = new DateOnly(2031, 5, 10),
            TripDateEnd = new DateOnly(2031, 5, 11),
        });
        await store.SaveChangesAsync();
    }

    // ---- the calendar and the feed -----------------------------------------------------------

    [Fact]
    public async Task The_calendar_reads_a_one_day_trip_as_one_day_and_a_stay_of_any_length_as_being_on_the_camp()
    {
        const string window = "from=2062-03-01&to=2062-03-31";

        var trip = await TripAsync("Day trip", "2062-03-04", "2062-03-04", onIt: true);
        await MoveAsync("trip-logs", trip, "planned");

        var oneDayCamp = await CampAsync("One-day stay camp", "2062-03-10", "2062-03-20");
        var openCamp = await CampAsync("Open stay camp", "2062-03-11", "2062-03-21");
        var notHerCamp = await CampAsync("Not her camp", "2062-03-12", "2062-03-22");
        foreach (var camp in new[] { oneDayCamp, openCamp, notHerCamp })
        {
            await MoveAsync("expeditions", camp, "planned");
        }

        await StayAsync(oneDayCamp, anaCaver, memberRoleId, "2062-03-12", "2062-03-12");
        await StayAsync(openCamp, anaCaver, memberRoleId, "2062-03-11", null);

        var all = await EntriesAsync(window);
        all.Select(x => x.GetProperty("id").GetGuid())
            .ShouldBe([trip, oneDayCamp, openCamp, notHerCamp], ignoreOrder: true);

        // The trip was typed as the same day twice and is one day on the grid: no end at all,
        // which is what the grid draws a single cell from.
        var onTheGrid = all.Single(x => x.GetProperty("id").GetGuid() == trip);
        onTheGrid.GetProperty("start").GetString().ShouldBe("2062-03-04");
        onTheGrid.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);

        // "Mine" asks whether she is recorded at the camp, not for which days: there for one day
        // and there without having left are both there. The camp she is on nobody's roster of is
        // the control. Each camp keeps its own days — a stay never redraws the camp.
        var mine = await EntriesAsync($"{window}&mine=true");
        mine.Select(x => x.GetProperty("id").GetGuid())
            .ShouldBe([trip, oneDayCamp, openCamp], ignoreOrder: true);
        var drawn = mine.Single(x => x.GetProperty("id").GetGuid() == oneDayCamp);
        drawn.GetProperty("start").GetString().ShouldBe("2062-03-10");
        drawn.GetProperty("end").GetString().ShouldBe("2062-03-20");
    }

    [Fact]
    public async Task The_feed_carries_a_one_day_trip_as_one_whole_day_and_the_camps_of_both_kinds_of_stay()
    {
        var trip = await TripAsync("Feed day trip", "2063-04-04", "2063-04-04", onIt: true);
        await MoveAsync("trip-logs", trip, "planned");

        var oneDayCamp = await CampAsync("Feed one-day stay camp", "2063-04-10", "2063-04-20");
        var openCamp = await CampAsync("Feed open stay camp", "2063-04-11", "2063-04-21");
        var notHerCamp = await CampAsync("Feed not her camp", "2063-04-12", "2063-04-22");
        foreach (var camp in new[] { oneDayCamp, openCamp, notHerCamp })
        {
            await MoveAsync("expeditions", camp, "planned");
        }

        await StayAsync(oneDayCamp, anaCaver, memberRoleId, "2063-04-12", "2063-04-12");
        await StayAsync(openCamp, anaCaver, memberRoleId, "2063-04-11", null);

        var feed = await FeedAsync();

        // One whole day in the format is its day and the day after, exclusive — and not the two
        // days a stored end equal to the start would have had to be defended against.
        var tripBlock = EventBlock(feed, $"trip-{trip}@");
        tripBlock.ShouldContain("DTSTART;VALUE=DATE:20630404");
        tripBlock.ShouldContain("DTEND;VALUE=DATE:20630405");

        // Both camps, each over its own days whatever the stay's were; and not the camp she is on
        // no roster of.
        EventBlock(feed, $"camp-{oneDayCamp}@").ShouldContain("DTSTART;VALUE=DATE:20630410");
        EventBlock(feed, $"camp-{oneDayCamp}@").ShouldContain("DTEND;VALUE=DATE:20630421");
        EventBlock(feed, $"camp-{openCamp}@").ShouldContain("DTSTART;VALUE=DATE:20630411");
        feed.ShouldNotContain(notHerCamp.ToString());
    }

    // ---- the documents -----------------------------------------------------------------------

    [Fact]
    public async Task A_camp_s_write_up_tells_one_day_from_still_there_and_prints_a_one_day_trip_as_its_day()
    {
        var camp = await CampAsync("Written-up camp", "2064-07-01", "2064-07-14");
        var trip = await TripAsync("Day shaft", "2064-07-04", "2064-07-04", onIt: false);
        using (var joined = await ana.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/trips", new { tripLogId = trip }))
        {
            joined.IsSuccessStatusCode.ShouldBeTrue(await joined.Content.ReadAsStringAsync());
        }

        var visitor = await CaverAsync($"Day Visitor {suffix}");
        var stayer = await CaverAsync($"Long Stayer {suffix}");
        await StayAsync(camp, visitor, memberRoleId, "2064-07-05", "2064-07-05");
        await StayAsync(camp, stayer, cookRoleId, "2064-07-02", "2064-07-09");
        await StayAsync(camp, anaCaver, memberRoleId, "2064-07-03", null);

        var text = await DocumentTextAsync($"/api/v1/expeditions/{camp}/report");

        // One day: its date, once, and never a range of itself.
        text.ShouldContain("2064-07-05");
        text.ShouldNotContain("2064-07-05 – 2064-07-05");

        // A stay that ran on: both of its days.
        text.ShouldContain("2064-07-02 – 2064-07-09");

        // Somebody who has not left is said in words. Printed as the bare day they arrived, the
        // document would file that they went home that evening.
        text.ShouldContain("from 2064-07-03, still there");

        // The trip was typed as the same day twice and is one line under one date.
        text.ShouldContain("Day shaft");
        text.ShouldNotContain("2064-07-04 – 2064-07-04");
    }

    [Fact]
    public async Task A_trip_s_write_up_prints_a_trip_typed_as_the_same_day_twice_as_one_day()
    {
        var trip = await TripAsync("Written-up day", "2065-08-09", "2065-08-09", onIt: true);

        var text = await DocumentTextAsync($"/api/v1/trip-logs/{trip}/report");

        text.ShouldContain("2065-08-09");
        text.ShouldNotContain("2065-08-09 – 2065-08-09");
    }

    // ---- helpers -----------------------------------------------------------------------------

    private object TripBody(string title, string date, string? end) => new
    {
        title = $"{title} {suffix}",
        tripDate = date,
        tripDateEnd = end,
        caveIds = Array.Empty<Guid>(),
        participants = Array.Empty<object>(),
        visibility = "private",
    };

    private async Task<Guid> TripAsync(string title, string date, string? end, bool onIt)
    {
        var response = await ana.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {suffix}",
            tripDate = date,
            tripDateEnd = end,
            caveIds = Array.Empty<Guid>(),
            participants = onIt ? new object[] { new { caverId = anaCaver } } : [],
            visibility = "private",
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CampAsync(string name, string start, string end)
    {
        var response = await ana.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {suffix}",
            description = "A camp.",
            startDate = start,
            endDate = end,
            geom = (object?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(response);
    }

    private async Task StayAsync(Guid campId, Guid caverId, long roleId, string from, string? to)
    {
        var response = await ana.PostAsJsonAsync($"/api/v1/expeditions/{campId}/roster/", new
        {
            caverId,
            roleId,
            fromDate = from,
            toDate = to,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CaverAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = name };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    private async Task MoveAsync(string route, Guid id, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/{route}/{id}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task<DateOnly?> StoredEndAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogs.AsNoTracking()
            .Where(t => t.Id == tripId).Select(t => t.TripDateEnd).SingleAsync();
    }

    private async Task<List<JsonElement>> EntriesAsync(string query)
    {
        var response = await ana.GetAsync($"/api/v1/calendar?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("entries").EnumerateArray()
            .Select(x => x.Clone())];
    }

    /// <summary>What a calendar application polling her address is handed.</summary>
    private async Task<string> FeedAsync()
    {
        var minted = await ana.PostAsJsonAsync("/api/v1/me/calendar-feeds", new { label = "Phone" });
        var payload = await minted.Content.ReadAsStringAsync();
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var url = JsonDocument.Parse(payload).RootElement.GetProperty("url").GetString()!;

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(new Uri(url).PathAndQuery);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return text;
    }

    /// <summary>The one event of a feed whose identifier starts as given, from its opening line to its closing one.</summary>
    private static string EventBlock(string feed, string uidStart)
    {
        var at = feed.IndexOf("UID:" + uidStart, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"the feed carries no event for {uidStart}");
        var begin = feed.LastIndexOf("BEGIN:VEVENT", at, StringComparison.Ordinal);
        var end = feed.IndexOf("END:VEVENT", at, StringComparison.Ordinal);
        return feed[begin..end];
    }

    /// <summary>The words of a generated document, as a word processor would read them.</summary>
    private async Task<string> DocumentTextAsync(string url)
    {
        using var response = await ana.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return document.MainDocumentPart!.Document!.InnerText;
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
