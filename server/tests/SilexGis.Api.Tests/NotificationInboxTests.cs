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
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The inbox: a person reading what happened to them, in the language they are reading the site
/// in, with what each notification is about decided again at the moment they read it.
/// </summary>
/// <remarks>
/// Everybody here is a Viewer. The seeded Editors group reads past visibility across the whole
/// installation by design, so an Editor would still be able to open a record after a grant was
/// withdrawn — and every test below that turns on somebody having lost access would pass without
/// the code under test doing anything at all.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class NotificationInboxTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;      // owns the record and does the granting
    private HttpClient reader = null!;     // whose inbox this is
    private HttpClient stranger = null!;   // proves an id is not a key
    private Guid ownerId;
    private Guid readerId;
    private Guid strangerId;

    public NotificationInboxTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "500" });

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, OwnerEmail);
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, ReaderEmail);
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, StrangerEmail);

        owner = await AuthHelper.BearerClientAsync(factory, OwnerEmail);
        reader = await AuthHelper.BearerClientAsync(factory, ReaderEmail);
        stranger = await AuthHelper.BearerClientAsync(factory, StrangerEmail);
    }

    private string OwnerEmail => $"inbox-own-{suffix}@t.local";

    private string ReaderEmail => $"inbox-rdr-{suffix}@t.local";

    private string StrangerEmail => $"inbox-str-{suffix}@t.local";

    [Fact]
    public async Task A_grant_is_read_back_as_a_line_saying_what_it_was_about()
    {
        var title = $"Owned trip {suffix}";
        var tripId = await CreateTripAsync(title);
        await GrantReadAsync(tripId, readerId);

        var page = await InboxAsync(reader);
        page.GetProperty("totalItems").GetInt32().ShouldBe(1);

        var row = page.GetProperty("items")[0];
        row.GetProperty("category").GetString().ShouldBe("permissionGranted");
        row.GetProperty("templateKey").GetString().ShouldBe(MessageTemplateCatalog.NotifyPermissionGranted);
        row.GetProperty("title").GetString().ShouldNotBeNull().ShouldContain(title);
        row.GetProperty("url").GetString().ShouldBe($"/trip-logs/{tripId}");
        row.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();
        row.GetProperty("readAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_notification_about_something_the_reader_has_lost_keeps_only_its_category_and_its_date()
    {
        // The protection the whole target reference exists for. A producer freezes the record's
        // name and a path to it when it queues the message; by the time it is read the reader may
        // have lost the right to open it, and nothing about the stored row moves when that
        // happens. So what it is about is decided again here, and a row that does not survive that
        // is still listed — that something happened is not the secret — with neither the name nor
        // a link.
        //
        // The same rule already runs once, where a producer decides who to tell at all: nobody is
        // told about something they could not open, decided from their own access as it stands
        // rather than assumed from their being named on the row. This is that rule at the only
        // other moment it can go stale.
        var title = $"Withdrawn trip {suffix}";
        var tripId = await CreateTripAsync(title);
        await GrantReadAsync(tripId, readerId);

        // The positive half, in the same test: while the grant stands the reader may open the
        // trip, and the line names it and links to it. Without this, everything below would pass
        // just as well against a page that never says anything.
        (await reader.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var granted = (await InboxAsync(reader)).GetProperty("items")[0];
        granted.GetProperty("title").GetString().ShouldNotBeNull().ShouldContain(title);
        granted.GetProperty("url").GetString().ShouldBe($"/trip-logs/{tripId}");
        granted.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();

        await RevokeAsync(tripId);

        // The unreadable state, constructed rather than assumed: the trip is private, this reader
        // does not own it, no rule names them any more, and their role reads past nothing. The
        // ordinary read path is what proves it.
        (await reader.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await RawInboxAsync(reader);
        var withheld = JsonDocument.Parse(body).RootElement.GetProperty("items")[0];

        withheld.GetProperty("category").GetString().ShouldBe("permissionGranted");
        withheld.GetProperty("createdAt").ValueKind.ShouldBe(JsonValueKind.String);
        withheld.GetProperty("targetWithheld").GetBoolean().ShouldBeTrue();
        withheld.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);

        // Asserted against the literal strings the producer wrote into the row, not against the
        // shape of the answer: a page that stopped saying them for some other reason, or that
        // said them somewhere this test did not think to look, is not a page that has protected
        // anything.
        body.ShouldNotContain(title);
        body.ShouldNotContain($"/trip-logs/{tripId}");
    }

    [Fact]
    public async Task Opening_one_of_them_says_exactly_what_the_list_said()
    {
        var title = $"Single trip {suffix}";
        var tripId = await CreateTripAsync(title);
        await GrantReadAsync(tripId, readerId);
        var id = (await InboxAsync(reader)).GetProperty("items")[0].GetProperty("id").GetInt64();

        var whole = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/{id}");
        whole.GetProperty("title").GetString().ShouldNotBeNull().ShouldContain(title);
        whole.GetProperty("url").GetString().ShouldBe($"/trip-logs/{tripId}");

        await RevokeAsync(tripId);

        // A row does not become readable by being asked for on its own. Withholding happens in
        // one place, so both endpoints answer the same way, and this is what pins that.
        var raw = await (await reader.GetAsync($"/api/v1/notifications/{id}")).Content.ReadAsStringAsync();
        var degraded = JsonDocument.Parse(raw).RootElement;
        degraded.GetProperty("targetWithheld").GetBoolean().ShouldBeTrue();
        degraded.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
        degraded.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldNotContain(title);
        raw.ShouldNotContain($"/trip-logs/{tripId}");
    }

    [Fact]
    public async Task Somebody_elses_notification_reads_as_one_that_was_never_written()
    {
        var tripId = await CreateTripAsync($"Private trip {suffix}");
        await GrantReadAsync(tripId, readerId);
        var id = (await InboxAsync(reader)).GetProperty("items")[0].GetProperty("id").GetInt64();

        // Whose it is, is part of the lookup rather than a check after it: 404 and not 403, so an
        // id cannot be used to find out whether a particular person was told a particular thing.
        var refused = await stranger.GetAsync($"/api/v1/notifications/{id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("notification.not_found");

        // Marking it read is the same question asked with a different verb.
        (await stranger.PostAsync($"/api/v1/notifications/{id}/read", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And it is genuinely there for the person it belongs to, so the refusal above is about
        // who asked rather than about the row.
        (await reader.GetAsync($"/api/v1/notifications/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InboxAsync(stranger)).GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Reading_one_moves_the_count_and_reading_it_again_leaves_the_stamp_alone()
    {
        await GrantReadAsync(await CreateTripAsync($"Counted trip {suffix}"), readerId);
        var id = (await InboxAsync(reader)).GetProperty("items")[0].GetProperty("id").GetInt64();

        (await UnreadAsync(reader)).ShouldBe(1);

        (await reader.PostAsync($"/api/v1/notifications/{id}/read", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync(reader)).ShouldBe(0);

        var first = (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/{id}"))
            .GetProperty("readAt").GetDateTimeOffset();

        (await reader.PostAsync($"/api/v1/notifications/{id}/read", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // When it was first opened, not most recently: a client that marks on render would
        // otherwise rewrite the stamp every time the row scrolled past.
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/{id}"))
            .GetProperty("readAt").GetDateTimeOffset().ShouldBe(first);
    }

    [Fact]
    public async Task Reading_everything_clears_the_callers_own_and_nobody_elses()
    {
        await GrantReadAsync(await CreateTripAsync($"Bulk trip A {suffix}"), readerId);
        await GrantReadAsync(await CreateTripAsync($"Bulk trip B {suffix}"), readerId);
        await GrantReadAsync(await CreateTripAsync($"Bulk trip C {suffix}"), strangerId);

        (await UnreadAsync(reader)).ShouldBe(2);
        (await UnreadAsync(stranger)).ShouldBe(1);

        (await reader.PostAsync("/api/v1/notifications/read-all", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await UnreadAsync(reader)).ShouldBe(0);
        (await UnreadAsync(stranger)).ShouldBe(1, "one person's clearing their own inbox is not everyone's");
    }

    [Fact]
    public async Task The_list_narrows_by_category_and_by_what_is_still_unread()
    {
        await GrantReadAsync(await CreateTripAsync($"Filtered trip {suffix}"), readerId);
        await QueueSecurityAlertAsync();

        (await InboxAsync(reader)).GetProperty("totalItems").GetInt32().ShouldBe(2);
        (await InboxAsync(reader, "?category=securityAlerts")).GetProperty("totalItems").GetInt32().ShouldBe(1);

        var alert = (await InboxAsync(reader, "?category=securityAlerts"))
            .GetProperty("items")[0].GetProperty("id").GetInt64();
        (await reader.PostAsync($"/api/v1/notifications/{alert}/read", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var unread = await InboxAsync(reader, "?unreadOnly=true");
        unread.GetProperty("totalItems").GetInt32().ShouldBe(1);
        unread.GetProperty("items")[0].GetProperty("category").GetString().ShouldBe("permissionGranted");

        // A category is named the way the answers name it, and something that is not a category
        // at all is an ordinary refusal rather than a failure.
        var nonsense = await reader.GetAsync("/api/v1/notifications/?category=notACategory");
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await nonsense.Content.ReadAsStringAsync()).ShouldContain("notification.category_unknown");
    }

    [Fact]
    public async Task Nobody_reads_an_inbox_without_signing_in()
    {
        using var anonymous = factory.CreateClient();

        foreach (var route in new[] { "/api/v1/notifications/", "/api/v1/notifications/unread-count", "/api/v1/notifications/1" })
        {
            (await anonymous.GetAsync(route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);
        }

        (await anonymous.PostAsync("/api/v1/notifications/1/read", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync("/api/v1/notifications/read-all", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reader_is_written_to_in_the_language_they_are_reading_the_site_in()
    {
        var title = $"Bilingual trip {suffix}";
        await GrantReadAsync(await CreateTripAsync(title), readerId);

        // The account still says English — this is the case the header exists for. Somebody who
        // switched the interface to Romanian on a borrowed machine reads a Romanian list without
        // having to change what their profile remembers.
        (await InboxAsync(reader)).GetProperty("items")[0]
            .GetProperty("title").GetString().ShouldBe($"You were given access to {title}");

        (await InboxAsync(reader, acceptLanguage: "ro-RO,ro;q=0.9,en;q=0.8")).GetProperty("items")[0]
            .GetProperty("title").GetString().ShouldBe($"Ați primit acces la {title}");

        // A language this installation has no wording in asks for nothing, and the account's own
        // choice decides instead of a default overruling it.
        await SetLocaleAsync("ro");
        (await InboxAsync(reader, acceptLanguage: "de-DE,de;q=0.9")).GetProperty("items")[0]
            .GetProperty("title").GetString().ShouldBe($"Ați primit acces la {title}");
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trip = new TripLog
        {
            Title = title,
            TripDate = new DateOnly(2026, 1, 1),
            OwnerUserId = ownerId,
            Visibility = Visibility.Private,
        };
        db.TripLogs.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    private async Task GrantReadAsync(Guid tripId, Guid subjectId)
    {
        var granted = await owner.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
    }

    private async Task RevokeAsync(Guid tripId)
    {
        var revoked = await owner.PutAsJsonAsync(
            $"/api/v1/objects/tripLog/{tripId}/access", new { entries = Array.Empty<object>() });
        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A notification about the account itself, which names no object and so has nothing to
    /// re-decide. Queued directly: its producer is a password change, and what is under test here
    /// is the listing rather than who wrote the row.
    /// </summary>
    private async Task QueueSecurityAlertAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        NotificationQueue.Enqueue(
            db,
            readerId,
            NotificationCategory.SecurityAlerts,
            MessageTemplateCatalog.NotifySecurityPasswordChanged,
            new Dictionary<string, string>());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_category_whose_inbox_is_switched_off_is_neither_listed_nor_counted()
    {
        // The inbox is a cell of the matrix like any other. Switching it off has to mean
        // something, because the settings page tells somebody who switches every channel off that
        // the category now reaches them nowhere — and a row that still appeared here, and still
        // lit the count in the header, would make that statement false.
        await SetInboxAsync("permissionGranted", "off");

        await GrantReadAsync(await CreateTripAsync($"Muted trip {suffix}"), readerId);
        await QueueSecurityAlertAsync();

        var page = await InboxAsync(reader);
        var categories = page.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("category").GetString()).ToList();

        categories.ShouldNotContain("permissionGranted");
        page.GetProperty("totalItems").GetInt32().ShouldBe(1);
        (await UnreadAsync(reader)).ShouldBe(1);

        // The positive half, twice over: a category nobody may switch off is still here — the
        // rule is read from the vocabulary, so a stored "off" for one is worth nothing — and
        // switching the inbox back on brings back what happened while it was off, because the
        // choice is applied when the row is read and never when it is written.
        categories.ShouldContain("securityAlerts");

        await SetInboxAsync("permissionGranted", "immediate");

        (await InboxAsync(reader)).GetProperty("totalItems").GetInt32().ShouldBe(2);
        (await UnreadAsync(reader)).ShouldBe(2);
    }

    private async Task SetInboxAsync(string category, string choice)
    {
        var saved = await reader.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            categories = new[]
            {
                new { category, channels = new[] { new { channel = "inApp", choice } } },
            },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private Task SetLocaleAsync(string language) =>
        reader.PutAsJsonAsync("/api/v1/me/locale", new { language, timeZone = (string?)null });

    private async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count"))
            .GetProperty("unread").GetInt32();

    private async Task<JsonElement> InboxAsync(
        HttpClient client, string? query = null, string? acceptLanguage = null) =>
        JsonDocument.Parse(await RawInboxAsync(client, query, acceptLanguage)).RootElement.Clone();

    private async Task<string> RawInboxAsync(
        HttpClient client, string? query = null, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/notifications/{query}");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    public async Task DisposeAsync()
    {
        // Notifications are one table for the whole database, so a class takes its own rows away
        // rather than the table.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications
            .Where(n => n.RecipientUserId == readerId || n.RecipientUserId == strangerId)
            .ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        stranger?.Dispose();
        factory.Dispose();
    }
}
