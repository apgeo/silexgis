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
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The list of deleted trips and putting one back: who is shown what, who is refused how, and
/// what the window does to both.
/// </summary>
/// <remarks>
/// <para>
/// The right that deletes is the right that restores, asked of the trip as it stands — its owner,
/// its audience and the rules written on it all survive the delete, so the ordinary decision is
/// still there to ask. Four accounts hold four different answers to it, and each is shown beside
/// one that differs, so that no refusal here passes for a reason other than the one under test.
/// </para>
/// </remarks>
public sealed class TripRestoreTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;     // Editor; writes and deletes the trips
    private HttpClient reader = null!;    // Viewer; reads what its audience admits, deletes nothing
    private HttpClient deputy = null!;    // Viewer handed read and delete on one trip
    private HttpClient admin = null!;
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private Guid deputyId;

    public TripRestoreTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trs-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trs-rdr-{suffix}@t.local");
        deputyId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trs-dep-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"trs-adm-{suffix}@t.local");

        owner = await AuthHelper.BearerClientAsync(factory, $"trs-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"trs-rdr-{suffix}@t.local");
        deputy = await AuthHelper.BearerClientAsync(factory, $"trs-dep-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"trs-adm-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        reader.Dispose();
        deputy.Dispose();
        admin.Dispose();
        anonymous.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task The_deleted_list_holds_what_the_caller_may_put_back_and_nothing_else()
    {
        var open = await CreateTripAsync("Open to everybody", "authenticated");
        var shut = await CreateTripAsync("Private to its owner", "private");
        var handed = await CreateTripAsync("Handed to a deputy", "private");
        await GrantAsync(handed, deputyId, AccessAction.Read | AccessAction.Delete);
        var live = await CreateTripAsync("Never deleted", "authenticated");

        // The reader really can open the open one while it is live. Without this the empty list
        // below would be the list of somebody who could never see any of them.
        (await reader.GetAsync($"/api/v1/trip-logs/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var trip in new[] { open, shut, handed })
        {
            (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // The owner: all three, most recently deleted first, and not the one still live.
        var mine = await DeletedAsync(owner);
        mine.Select(t => t.Id).ShouldBe([handed, shut, open]);
        mine.ShouldNotContain(t => t.Id == live);

        // Each row says what it is, when it went, when it goes for good and who deleted it.
        var row = mine.Single(t => t.Id == open);
        row.Title.ShouldStartWith("Open to everybody");
        row.TripDate.ShouldBe("2026-07-20");
        row.DeletedByUserId.ShouldBe(ownerId);
        row.DeletedByName.ShouldNotBeNullOrWhiteSpace();
        row.RestorableUntil.ShouldBe(row.DeletedAt.AddDays(TripDeletionRules.DefaultRetentionDays));

        // The reader could read one of them and could delete none: nothing is theirs to put back,
        // so nothing is listed — not even the one they used to open.
        (await DeletedAsync(reader)).ShouldBeEmpty();

        // The deputy holds read and delete on exactly one, by a rule on the trip that the delete
        // left standing.
        (await DeletedAsync(deputy)).Select(t => t.Id).ShouldBe([handed]);

        // A full administrator may delete anything, and so may restore anything — which, in a
        // database the tests of this class share, includes what the others left deleted. So it
        // is this test's three that are looked for, in the order the list gives them.
        var ours = new[] { open, shut, handed };
        (await DeletedAsync(admin)).Select(t => t.Id).Where(id => ours.Contains(id)).ShouldBe([handed, shut, open]);

        // Paged, with a total that counts what this caller may put back rather than what exists.
        var page = await JsonAsync(deputy, "/api/v1/trip-logs/deleted?pageSize=1");
        page.GetProperty("totalItems").GetInt32().ShouldBe(1);
        var secondOfThree = await JsonAsync(owner, "/api/v1/trip-logs/deleted?page=2&pageSize=1");
        secondOfThree.GetProperty("totalItems").GetInt32().ShouldBe(3);
        secondOfThree.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(shut);

        (await anonymous.GetAsync("/api/v1/trip-logs/deleted")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restoring_is_refused_the_way_reading_and_deleting_are()
    {
        var open = await CreateTripAsync("Readable, deleted", "authenticated");
        var shut = await CreateTripAsync("Unreadable, deleted", "private");
        var handed = await CreateTripAsync("Deputy's to restore", "private");
        await GrantAsync(handed, deputyId, AccessAction.Read | AccessAction.Delete);
        var deleteOnly = await CreateTripAsync("Deletable, not readable", "private");
        await GrantAsync(deleteOnly, deputyId, AccessAction.Delete);
        var live = await CreateTripAsync("Readable, live", "authenticated");

        foreach (var trip in new[] { open, shut, handed, deleteOnly })
        {
            (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Nobody signed in, and a trip that does not exist.
        (await RestoreAsync(anonymous, open)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldBeProblemAsync(await RestoreAsync(owner, Guid.NewGuid()), HttpStatusCode.NotFound, "trip_log.not_found");

        // A trip the caller could not read answers as one that does not exist, deleted or not —
        // and the same answer, so nothing here says which of the two it is.
        await ShouldBeProblemAsync(await RestoreAsync(reader, shut), HttpStatusCode.NotFound, "trip_log.not_found");

        // The right to delete without the right to read is not enough: this route answers with
        // the whole trip, and must not be how somebody comes to read one.
        await ShouldBeProblemAsync(await RestoreAsync(deputy, deleteOnly), HttpStatusCode.NotFound, "trip_log.not_found");
        (await DeletedTrips.RowAsync(factory, deleteOnly))!.DeletedAt.ShouldNotBeNull();

        // Readable but not theirs to delete: a refusal, whether the trip is deleted or live.
        (await RestoreAsync(reader, open)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RestoreAsync(reader, live)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DeletedTrips.RowAsync(factory, open))!.DeletedAt.ShouldNotBeNull();

        // Theirs to delete, and not deleted: there is nothing to restore.
        await ShouldBeProblemAsync(
            await RestoreAsync(owner, live), HttpStatusCode.Conflict, TripDeletionRules.NotDeletedCode);

        // The deputy restores the one they were handed, by the rule the delete left on it, and
        // is answered with the trip.
        var byDeputy = await RestoreAsync(deputy, handed);
        var body = await byDeputy.Content.ReadAsStringAsync();
        byDeputy.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid().ShouldBe(handed);
        (await deputy.GetAsync($"/api/v1/trip-logs/{handed}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The owner restores theirs, and a second time finds nothing left to do.
        (await RestoreAsync(owner, open)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeProblemAsync(
            await RestoreAsync(owner, open), HttpStatusCode.Conflict, TripDeletionRules.NotDeletedCode);
        (await reader.GetAsync($"/api/v1/trip-logs/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Past the window a trip is neither offered nor accepted back, whether or not the pass that
    /// removes it has reached it yet: the moment a list said it would go is the moment it stops
    /// being restorable, and the two must not disagree for however long the pass is late.
    /// </summary>
    [Fact]
    public async Task A_trip_past_its_window_is_neither_listed_nor_restored()
    {
        var late = await CreateTripAsync("Deleted too long ago", "private");
        var recent = await CreateTripAsync("Deleted just now", "private");
        foreach (var trip in new[] { late, recent })
        {
            (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await DeletedTrips.AgePastTheWindowAsync(factory, late);

        // Still in the table — no pass has run — and already not on the list.
        (await DeletedTrips.RowAsync(factory, late)).ShouldNotBeNull();
        var listed = (await DeletedAsync(owner)).Select(t => t.Id).ToList();
        listed.ShouldContain(recent);
        listed.ShouldNotContain(late);

        await ShouldBeProblemAsync(
            await RestoreAsync(owner, late), HttpStatusCode.Conflict, TripDeletionRules.RestoreWindowPassedCode);
        (await DeletedTrips.RowAsync(factory, late))!.DeletedAt.ShouldNotBeNull();
        (await RestoreAsync(owner, recent)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// An installation that sets no window keeps deleted trips until it says otherwise: nothing
    /// is scheduled to go, so nothing has a date to go by, and a trip deleted long ago is still
    /// there to put back. The same trip on the same database, read by a host with the shipped
    /// window, is past it — which is what shows the setting is what decided.
    /// </summary>
    [Fact]
    public async Task An_installation_with_no_window_keeps_deleted_trips_restorable()
    {
        var trip = await CreateTripAsync("Kept until somebody says otherwise", "private");
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DeletedTrips.AgePastTheWindowAsync(factory, trip);

        (await JsonAsync(owner, "/api/v1/trip-logs/config"))
            .GetProperty("deletedRetentionDays").GetInt32().ShouldBe(TripDeletionRules.DefaultRetentionDays);
        (await DeletedAsync(owner)).ShouldNotContain(t => t.Id == trip);
        (await anonymous.GetAsync("/api/v1/trip-logs/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var keeping = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Trips:DeletedRetentionDays"] = "0",
        });
        using var keeper = await AuthHelper.BearerClientAsync(keeping, $"trs-own-{suffix}@t.local");

        (await JsonAsync(keeper, "/api/v1/trip-logs/config"))
            .GetProperty("deletedRetentionDays").ValueKind.ShouldBe(JsonValueKind.Null);

        var kept = (await DeletedAsync(keeper)).Single(t => t.Id == trip);
        kept.RestorableUntil.ShouldBeNull();

        // The pass has nothing to measure against and removes nothing, however old the stamp.
        await DeletedTrips.RunPurgeAsync(keeping);
        (await DeletedTrips.RowAsync(keeping, trip)).ShouldNotBeNull();

        (await RestoreAsync(keeper, trip)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await keeper.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Neither act tells anybody. The people on a trip were never told it had gone, so a notice
    /// that it is back would be the first they heard of either — and a delete is not something a
    /// roster needs waking for.
    /// </summary>
    [Fact]
    public async Task Deleting_and_restoring_a_trip_people_are_expecting_notifies_nobody()
    {
        var deputyCaver = await RosterHelper.CaverIdForAsync(factory, deputyId);
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"A plan people are on {suffix}",
            tripDate = "2026-07-20",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = deputyCaver } },
            visibility = "authenticated",
            hadIncident = false,
        });
        var trip = await CreatedIdAsync(created);
        var planned = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{trip}/state", new { state = "planned" });
        planned.StatusCode.ShouldBe(HttpStatusCode.OK, await planned.Content.ReadAsStringAsync());

        // A plan in this state does tell its roster when it changes, which is what makes the
        // silence below a property of these two acts and not of the fixture: an edit is shown to
        // reach the deputy, and the count taken after it is what the delete must not move.
        var edited = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = $"A plan people are on {suffix}",
            tripDate = "2026-07-21",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = deputyCaver } },
            visibility = "authenticated",
            hadIncident = false,
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var told = await NotificationCountAsync(deputyId);
        told.ShouldBeGreaterThan(0);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NotificationCountAsync(deputyId)).ShouldBe(told);

        (await RestoreAsync(owner, trip)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NotificationCountAsync(deputyId)).ShouldBe(told);
    }

    // ---- fixtures

    private sealed record DeletedTrip(
        Guid Id,
        string Title,
        string TripDate,
        string? TripDateEnd,
        DateTimeOffset DeletedAt,
        DateTimeOffset? RestorableUntil,
        Guid? DeletedByUserId,
        string? DeletedByName);

    private static async Task<List<DeletedTrip>> DeletedAsync(HttpClient client)
    {
        var page = await JsonAsync(client, "/api/v1/trip-logs/deleted?pageSize=500");
        var items = page.GetProperty("items").Deserialize<List<DeletedTrip>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        page.GetProperty("totalItems").GetInt32().ShouldBe(items.Count);
        return items;
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid trip) =>
        client.PostAsync($"/api/v1/trip-logs/{trip}/restore", null);

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private async Task<Guid> CreateTripAsync(string title, string visibility)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {suffix} {Guid.NewGuid():N}"[..Math.Min(80, title.Length + 42)],
            tripDate = "2026-07-20",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility,
            hadIncident = false,
        });
        return await CreatedIdAsync(created);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    /// A rule on one trip, written straight to the table: what is under test is that the row
    /// survives a delete and still decides, not the route that authors it.
    /// </summary>
    private async Task GrantAsync(Guid trip, Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = trip,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> NotificationCountAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == userId);
    }
}
