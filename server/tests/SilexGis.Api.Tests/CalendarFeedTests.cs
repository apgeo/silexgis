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
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The calendar subscription feed: minted, listed and withdrawn by its holder, read by a calendar
/// application holding nothing but the address.
/// </summary>
/// <remarks>
/// <para>
/// The case this file exists for is the one about grants. The feed's token is the first anonymous
/// token here that resolves to a person rather than to one named thing, and what makes that
/// acceptable is that nothing about the person's rights is stored with it: the grant set is
/// assembled on every poll. So a right withdrawn after the address was handed out has to stop
/// its rows on the very next poll, and that is asserted directly rather than inferred from the
/// code's shape.
/// </para>
/// <para>
/// The gate is switched on for the whole class through configuration, which is the installation
/// default the settings service falls back to; the one test about the gate being off saves the
/// section the way an administrator would, and puts it back.
/// </para>
/// </remarks>
public sealed class CalendarFeedTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;

    private HttpClient ana = null!;    // Editor; writes the rows
    private HttpClient reader = null!; // Viewer holding nothing except what a test grants
    private HttpClient admin = null!;
    private Guid anaId;
    private Guid readerId;
    private Guid anaCaver;
    private Guid readerCaver;

    public CalendarFeedTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        // The gate on, as the installation default. The address an installation lives at is the
        // test server's own, which the factory fixes for every host of a class — the sign-in
        // client registration is seeded from it — so the addresses asserted below are on it.
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Protection:CalendarFeedEnabled"] = "true",
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        anaId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"feed-ana-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"feed-rdr-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"feed-adm-{suffix}@t.local");
        anaCaver = await RosterHelper.CaverIdForAsync(factory, anaId);
        readerCaver = await RosterHelper.CaverIdForAsync(factory, readerId);

        ana = await AuthHelper.BearerClientAsync(factory, $"feed-ana-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"feed-rdr-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"feed-adm-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        ana.Dispose();
        reader.Dispose();
        admin.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Minting_listing_and_revoking_need_an_account_and_the_address_is_shown_once()
    {
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/me/calendar-feeds")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.PostAsJsonAsync("/api/v1/me/calendar-feeds", new { label = "Phone" }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var minted = await MintAsync(reader, "Phone");
        var id = minted.GetProperty("id").GetGuid();
        var url = minted.GetProperty("url").GetString()!;
        minted.GetProperty("label").GetString().ShouldBe("Phone");

        // Composed from where the installation says it lives, ending the way calendar
        // applications recognise, and carrying a 43-character token.
        url.ShouldStartWith("http://localhost/api/v1/calendar/feed/");
        url.ShouldEndWith(".ics");
        TokenOf(url).Length.ShouldBe(43);

        // The list says the installation offers feeds, names the row, and never the address.
        var listed = await ListAsync(reader);
        listed.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        var rows = listed.GetProperty("feeds").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("id").GetGuid()).ShouldContain(id);
        listed.GetRawText().ShouldNotContain(TokenOf(url));
        rows.Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("revokedAt").ValueKind
            .ShouldBe(JsonValueKind.Null);

        // Only the holder can withdraw it: another account's row answers as an invented one does.
        (await ana.DeleteAsync($"/api/v1/me/calendar-feeds/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.DeleteAsync($"/api/v1/me/calendar-feeds/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await reader.DeleteAsync($"/api/v1/me/calendar-feeds/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Idempotent, and the stamp stays.
        (await reader.DeleteAsync($"/api/v1/me/calendar-feeds/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterwards = (await ListAsync(reader)).GetProperty("feeds").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == id);
        afterwards.GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task The_feed_is_read_by_a_caller_carrying_nothing_and_says_text_calendar()
    {
        var url = (await MintAsync(reader)).GetProperty("url").GetString()!;

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(PathOf(url));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
        response.Content.Headers.ContentType.CharSet.ShouldBe("utf-8");
        var feed = await response.Content.ReadAsStringAsync();
        feed.ShouldStartWith("BEGIN:VCALENDAR\r\n");
        feed.ShouldContain("METHOD:PUBLISH\r\n");
        feed.ShouldEndWith("END:VCALENDAR\r\n");
    }

    /// <summary>
    /// The scope: what the person is on, under their own visibility, and nothing about any row
    /// beyond its title, its days and where to read it.
    /// </summary>
    [Fact]
    public async Task The_feed_carries_the_rows_the_person_is_on_and_nothing_else_about_them()
    {
        var onIt = await TripAsync("Feed trip on", "2062-03-04", onIt: true, place: "Șura Mare; car park");
        var notOnIt = await TripAsync("Feed trip off", "2062-03-05", onIt: false);
        var draft = await TripAsync("Feed trip draft", "2062-03-06", onIt: true);
        var calledOff = await TripAsync("Feed trip off-called", "2062-03-07", onIt: true);
        await MoveTripAsync(onIt, "planned");
        await MoveTripAsync(notOnIt, "planned");
        await MoveTripAsync(calledOff, "planned");
        await MoveTripAsync(calledOff, "cancelled");

        var campOn = await CampAsync("Feed camp on", "2062-03-10", "2062-03-14");
        var campOff = await CampAsync("Feed camp off", "2062-03-15", "2062-03-16");
        await StayAsync(campOn, anaCaver, "2062-03-11");
        await MoveCampAsync(campOn, "planned");
        await MoveCampAsync(campOff, "planned");

        var said = await EventAsync("Feed evening yes", "2062-03-20");
        var declined = await EventAsync("Feed evening no", "2062-03-21");
        var silent = await EventAsync("Feed evening asked", "2062-03-22");
        var unasked = await EventAsync("Feed evening unasked", "2062-03-23");
        await AnswerAsync(said, "yes");
        await AnswerAsync(declined, "no");
        await InviteAsync(silent);
        foreach (var evening in new[] { said, declined, silent, unasked })
        {
            await MoveEventAsync(evening, "planned");
        }

        var feed = await FeedOfAsync(ana);

        // On it: named, staying, said yes, asked and silent, and the one called off — marked.
        feed.ShouldContain($"UID:trip-{onIt}@localhost");
        feed.ShouldContain($"UID:trip-{calledOff}@localhost");
        feed.ShouldContain($"UID:camp-{campOn}@localhost");
        feed.ShouldContain($"UID:event-{said}@localhost");
        feed.ShouldContain($"UID:event-{silent}@localhost");
        feed.ShouldContain("STATUS:CANCELLED");

        // Not on it, readable though every one of them is to her: she wrote them.
        feed.ShouldNotContain(notOnIt.ToString());
        feed.ShouldNotContain(campOff.ToString());
        feed.ShouldNotContain(declined.ToString());
        feed.ShouldNotContain(unasked.ToString());
        // A draft is kept off the feed exactly as it is kept off the calendar page.
        feed.ShouldNotContain(draft.ToString());

        // The link back, composed from the installation's address.
        feed.ShouldContain($"URL:http://localhost/trip-logs/{onIt}");
        feed.ShouldContain($"URL:http://localhost/expeditions/{campOn}");
        feed.ShouldContain($"URL:http://localhost/events/{said}");

        // And nothing else: the place she typed on the trip is not here, nor any property that
        // could carry a position, a party or a narrative.
        feed.ShouldNotContain("Șura Mare");
        feed.ShouldNotContain("LOCATION");
        feed.ShouldNotContain("GEO:");
        feed.ShouldNotContain("ATTENDEE");
        feed.ShouldNotContain("DESCRIPTION");
        feed.ShouldNotContain("RRULE");
    }

    /// <summary>
    /// The one genuinely new thing the feed does — resolve a token to a person's grants — and
    /// the property that makes it safe: the grants are resolved on every poll, never stored.
    /// </summary>
    [Fact]
    public async Task A_grant_withdrawn_after_the_mint_stops_the_row_on_the_next_poll()
    {
        // A private trip of Ana's with the reader on its roster. Being on a trip is not reading
        // it: the reader is named but holds no right to the row, so the feed cannot carry it yet.
        var trip = await TripAsync("Feed grant trip", "2063-05-05", onIt: false, alsoOn: readerCaver);
        await MoveTripAsync(trip, "planned");

        var url = (await MintAsync(reader)).GetProperty("url").GetString()!;
        (await FeedAtAsync(url)).ShouldNotContain(trip.ToString());

        // Granted after the address was handed out, and the next poll carries it.
        var grant = await GrantTripReadAsync(trip);
        (await FeedAtAsync(url)).ShouldContain($"UID:trip-{trip}@localhost");

        // Withdrawn, and the next poll does not. Nothing about the mint is consulted.
        await WithdrawAsync(grant);
        (await FeedAtAsync(url)).ShouldNotContain(trip.ToString());
    }

    [Fact]
    public async Task A_revoked_an_unknown_and_a_malformed_token_answer_the_one_404()
    {
        var minted = await MintAsync(reader);
        var url = minted.GetProperty("url").GetString()!;
        (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await reader.DeleteAsync($"/api/v1/me/calendar-feeds/{minted.GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var revoked = await ReadAsync(url);
        var unknown = await ReadAsync("http://localhost/api/v1/calendar/feed/" + new string('a', 43) + ".ics");
        var oversized = await ReadAsync("http://localhost/api/v1/calendar/feed/" + new string('a', 150) + ".ics");

        foreach (var refusal in new[] { revoked, unknown, oversized })
        {
            refusal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await refusal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("calendar_feed.not_found");
        }
    }

    [Fact]
    public async Task With_feeds_switched_off_the_mint_refuses_and_every_address_already_out_stops()
    {
        var url = (await MintAsync(reader)).GetProperty("url").GetString()!;
        (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SaveGateAsync(false);
        try
        {
            // The settings page is told, so it can stop drawing the section.
            (await ListAsync(reader)).GetProperty("enabled").GetBoolean().ShouldBeFalse();

            var refused = await reader.PostAsJsonAsync("/api/v1/me/calendar-feeds", new { label = "Laptop" });
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("calendar_feed.disabled");

            // An address minted while it was on answers exactly as an invented one does.
            var stopped = await ReadAsync(url);
            stopped.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await stopped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("calendar_feed.not_found");
        }
        finally
        {
            await SaveGateAsync(true);
        }

        (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_locked_account_s_feed_stops_answering()
    {
        var url = (await MintAsync(reader)).GetProperty("url").GetString()!;
        (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await LockAsync(readerId, true);
        try
        {
            (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await LockAsync(readerId, false);
        }

        (await ReadAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_window_is_per_token_so_one_address_cannot_spend_another_s()
    {
        // A host of its own so the window is small enough to reach, and so tripping it cannot
        // starve the other tests of this class, which poll the same route from the same address.
        using var tight = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Protection:CalendarFeedEnabled"] = "true",
            ["CalendarFeed:RateLimitPerMinute"] = "3",
        });
        using var client = tight.CreateClient();

        var first = PathOf((await MintAsync(reader)).GetProperty("url").GetString()!);
        var second = PathOf((await MintAsync(reader)).GetProperty("url").GetString()!);

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK, $"poll {i + 1}");
        }

        (await client.GetAsync(first)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        // The same caller, the other address: its own budget, untouched.
        (await client.GetAsync(second)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static string TokenOf(string url) =>
        url[(url.LastIndexOf('/') + 1)..^".ics".Length];

    /// <summary>The address as the test host answers it — the installation's host replaced by the test server's.</summary>
    private static string PathOf(string url) => new Uri(url).PathAndQuery;

    /// <summary>A poll by a caller carrying nothing, with the body read before the client goes.</summary>
    private async Task<HttpResponseMessage> ReadAsync(string url)
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(PathOf(url));
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    private async Task<string> FeedAtAsync(string url)
    {
        var response = await ReadAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return text;
    }

    private async Task<string> FeedOfAsync(HttpClient holder) =>
        await FeedAtAsync((await MintAsync(holder)).GetProperty("url").GetString()!);

    private static async Task<JsonElement> MintAsync(HttpClient client, string? label = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/calendar-feeds", new { label });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me/calendar-feeds");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task SaveGateAsync(bool enabled)
    {
        var response = await admin.PutAsJsonAsync("/api/v1/admin/settings/protection", new
        {
            revealProtectedAssociations = false,
            calendarFeedEnabled = enabled,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task LockAsync(Guid userId, bool locked)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(
            set => set.SetProperty(u => u.LockoutEnd, locked ? DateTimeOffset.UtcNow.AddYears(100) : null));
    }

    private async Task<Guid> TripAsync(
        string title, string date, bool onIt, Guid? alsoOn = null, string? place = null)
    {
        var participants = new List<object>();
        if (onIt)
        {
            participants.Add(new { caverId = anaCaver });
        }

        if (alsoOn is { } other)
        {
            participants.Add(new { caverId = other });
        }

        var response = await ana.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = date,
            caveIds = Array.Empty<Guid>(),
            participants,
            locationText = place,
            description = place,
            visibility = "private",
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CampAsync(string name, string start, string end)
    {
        var response = await ana.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = start,
            endDate = end,
            geom = (object?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(response);
    }

    private async Task StayAsync(Guid campId, Guid caverId, string from)
    {
        long roleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            roleId = await db.ExpeditionRosterRoles
                .Where(r => r.Code == ExpeditionRosterRoleSeeds.MemberCode).Select(r => r.Id).SingleAsync();
        }

        var response = await ana.PostAsJsonAsync($"/api/v1/expeditions/{campId}/roster/", new
        {
            caverId,
            roleId,
            fromDate = from,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> EventAsync(string title, string date)
    {
        var response = await ana.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            kind = "clubMeeting",
            startDate = date,
            startTime = "19:00",
            visibility = "private",
        });
        return await CreatedIdAsync(response);
    }

    private async Task InviteAsync(Guid eventId)
    {
        var response = await ana.PostAsJsonAsync($"/api/v1/events/{eventId}/invitations/", new { caverId = anaCaver });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task AnswerAsync(Guid eventId, string answer)
    {
        await InviteAsync(eventId);
        var response = await ana.PutAsJsonAsync(
            $"/api/v1/events/{eventId}/invitations/{anaCaver}/response", new { response = answer });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task MoveTripAsync(Guid tripId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task MoveCampAsync(Guid campId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/expeditions/{campId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task MoveEventAsync(Guid eventId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/events/{eventId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task<long> GrantTripReadAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = readerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = tripId,
        };
        db.AccessEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task WithdrawAsync(long entryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AccessEntries.Where(e => e.Id == entryId).ExecuteDeleteAsync();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
