// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Taking published links back in bulk, and exchanging one for a fresh one.
/// </summary>
/// <remarks>
/// <para>
/// <b>What "the old address stops answering" has to mean.</b> Not only a 404: an address that was
/// taken back or replaced must be indistinguishable from one that never existed, on every route a
/// stranger can ask — the same status, the same body, and the same number of database commands,
/// because a refusal that took longer for a once-real token would say with its timing what its
/// body keeps back. Each test here therefore asks all four anonymous routes about the withdrawn
/// address and an invented one side by side, after first seeing each of them answer for the
/// address while it stood; a route that was refusing all along would otherwise pass.
/// </para>
/// <para>
/// Every test of this class shares one database, and one test withdraws everything in it. Each
/// test makes the links it asserts about, and nothing counts links it did not make except the one
/// test whose claim is about all of them.
/// </para>
/// </remarks>
public sealed class PublishedLinkWithdrawalTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Admin = "/api/v1/admin/published-trips";
    private const string Everything = Admin + "/revoke-everything";
    private const string Invented = "not-a-token-at-all";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient admin = null!;
    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private string adminEmail = null!;
    private string ownerEmail = null!;
    private Guid adminId;
    private Guid ownerId;
    private long caveTypeId;

    public PublishedLinkWithdrawalTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"pubwithdraw-{Guid.NewGuid():N}");
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
        adminEmail = $"pubwd-adm-{suffix}@t.local";
        ownerEmail = $"pubwd-own-{suffix}@t.local";
        var viewerEmail = $"pubwd-view-{suffix}@t.local";
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, viewerEmail);
        admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);
        viewer = await AuthHelper.BearerClientAsync(factory, viewerEmail);
        anonymous = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- replacing one link ------------------------------------------------------------------

    [Fact]
    public async Task Replacing_a_link_shuts_the_old_address_like_an_invented_one_and_opens_a_fresh_one()
    {
        // Somebody on the trip holds an account, so that "nobody is told" below is said about a
        // trip where somebody could have been.
        var memberEmail = $"pubwd-member-{Guid.NewGuid():N}"[..26] + "@t.local";
        var memberUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, memberEmail);
        var memberCaver = await CaverOfUserAsync(memberUserId);

        var scene = await SceneAsync(memberCaver);
        var current = scene.Current;
        (await PublishedNoticesAsync(current.Trip)).ShouldBe([memberUserId]);

        // The address answers on all four routes while it stands.
        foreach (var route in scene.Routes)
        {
            (await anonymous.GetAsync(route(current.Token))).StatusCode.ShouldBe(HttpStatusCode.OK, route("…"));
        }

        var replaced = await owner.PostAsync(Replace(current.Trip, current.ShareId), null);
        var payload = await replaced.Content.ReadAsStringAsync();
        replaced.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fresh = JsonDocument.Parse(payload).RootElement;
        var freshId = fresh.GetProperty("id").GetGuid();
        var freshToken = fresh.GetProperty("token").GetString()!;
        freshId.ShouldNotBe(current.ShareId);
        freshToken.ShouldNotBe(current.Token);
        freshToken.Length.ShouldBe(current.Token.Length);
        // The secret changed and nothing else did: the fresh link runs out when the old one would.
        fresh.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(current.ExpiresAt);
        replaced.Headers.Location!.ToString().ShouldBe($"{Shares(current.Trip)}/{freshId}");

        // The old address is now an unknown one, and the fresh one does what the old one did.
        await ShouldAnswerLikeAnInventedTokenAsync(scene.Routes, current.Token);
        foreach (var route in scene.Routes)
        {
            (await anonymous.GetAsync(route(freshToken))).StatusCode.ShouldBe(HttpStatusCode.OK, route("…"));
        }

        // The trip's own list tells it as one link taken back and one standing in its place.
        var listed = (await owner.GetFromJsonAsync<JsonElement>(Shares(current.Trip))).EnumerateArray().ToList();
        listed.Count.ShouldBe(2);
        var oldRow = listed.Single(r => r.GetProperty("id").GetGuid() == current.ShareId);
        var freshRow = listed.Single(r => r.GetProperty("id").GetGuid() == freshId);
        oldRow.GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        freshRow.GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        freshRow.GetProperty("createdBy").GetGuid().ShouldBe(ownerId);
        freshRow.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(current.ExpiresAt);

        // Both halves left the trail a revocation and a publication each leave, in the trip's
        // timeline and under the name of whoever did it.
        (await AuditAsync(current.ShareId, AuditActions.Updated, ownerId, current.Trip)).ShouldBe(1);
        (await AuditAsync(freshId, AuditActions.Created, ownerId, current.Trip)).ShouldBe(1);

        // And nobody was told a second time: nothing new was published.
        (await PublishedNoticesAsync(current.Trip)).ShouldBe([memberUserId]);
    }

    [Fact]
    public async Task A_finished_trips_link_is_replaced_without_restarting_its_watch_and_a_withdrawn_one_never()
    {
        var cave = await CaveAsync();
        var model = await ModelAsync(cave);
        var finished = await PublishedTripAsync(owner, "Long over", cave, model);
        await CloseAsync(finished.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var elsewhere = await PublishedTripAsync(owner, "Another trip");

        // In the archive: the live page is gone, the cave's history opens and holds this trip.
        (await anonymous.GetAsync(Follow(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await anonymous.GetFromJsonAsync<JsonElement>(PastList(finished.Token)))
            .ShouldContain(finished.Trip);

        // Publishing afresh is refused for a watch that is over — which is exactly why the address
        // of a finished trip could not be changed before without restarting its watch.
        var mint = await owner.PostAsync(Shares(finished.Trip), null);
        mint.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Code(await mint.Content.ReadAsStringAsync()).ShouldBe("tracking.publication_refused_not_armed");

        var replaced = await owner.PostAsync(Replace(finished.Trip, finished.ShareId), null);
        replaced.StatusCode.ShouldBe(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var fresh = await replaced.Content.ReadFromJsonAsync<JsonElement>();
        var freshId = fresh.GetProperty("id").GetGuid();
        var freshToken = fresh.GetProperty("token").GetString()!;

        (await anonymous.GetAsync(PastList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await anonymous.GetFromJsonAsync<JsonElement>(PastList(freshToken)))
            .ShouldContain(finished.Trip);

        // The link that was just taken back cannot be exchanged again ...
        var again = await owner.PostAsync(Replace(finished.Trip, finished.ShareId), null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Code(await again.Content.ReadAsStringAsync()).ShouldBe("tracking.share_revoked");

        // ... nor can one that does not exist, nor another trip's link asked for under this trip,
        // which is left standing by the attempt.
        var unknown = await owner.PostAsync(Replace(finished.Trip, Guid.NewGuid()), null);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Code(await unknown.Content.ReadAsStringAsync()).ShouldBe("tracking.share_not_found");
        var foreign = await owner.PostAsync(Replace(finished.Trip, elsewhere.ShareId), null);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Code(await foreign.Content.ReadAsStringAsync()).ShouldBe("tracking.share_not_found");
        (await anonymous.GetAsync(Follow(elsewhere.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // None of the refusals minted anything: one standing link, the fresh one.
        (await StandingAsync(finished.Trip)).ShouldBe([freshId]);

        // A cave that has since been protected is not handed out under a new address either — and
        // the refusal takes nothing back, so lifting the protection finds the link as it was.
        await SetLocationProtectedAsync(cave, true);
        var withheld = await owner.PostAsync(Replace(finished.Trip, freshId), null);
        withheld.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Code(await withheld.Content.ReadAsStringAsync()).ShouldBe("tracking.publication_refused_protected");
        await SetLocationProtectedAsync(cave, false);
        (await StandingAsync(finished.Trip)).ShouldBe([freshId]);
        (await anonymous.GetAsync(PastList(freshToken))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A link that has run out by itself is still the thing that keeps a finished trip in its
    /// cave's history, so it is still a link somebody may need to exchange.
    /// </summary>
    [Fact]
    public async Task A_link_past_its_own_expiry_is_replaced_and_the_fresh_one_runs_out_when_it_did()
    {
        var finished = await PublishedTripAsync(owner, "Ran out long ago");
        await CloseAsync(finished.Trip, DateTimeOffset.UtcNow.AddDays(-30));

        // Backdated straight into the column, like the closing above: a plain recorded instant
        // with nothing derived from it, read against the clock on every request.
        var ranOutAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-16), TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var link = await db.TripTrackingShares.SingleAsync(s => s.Id == finished.ShareId);
            link.ExpiresAt = ranOutAt;
            await db.SaveChangesAsync();
        }

        // Run out, taken back by nobody, and still opening the cave's history with this trip in it.
        (await anonymous.GetAsync(Follow(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await anonymous.GetFromJsonAsync<JsonElement>(PastList(finished.Token)))
            .ShouldContain(finished.Trip);

        var replaced = await owner.PostAsync(Replace(finished.Trip, finished.ShareId), null);
        replaced.StatusCode.ShouldBe(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var fresh = await replaced.Content.ReadFromJsonAsync<JsonElement>();
        var freshId = fresh.GetProperty("id").GetGuid();
        var freshToken = fresh.GetProperty("token").GetString()!;

        // A replacement extends nothing: the fresh link carries the instant the old one ran out at.
        fresh.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(ranOutAt);

        // The old address is an unknown one; the fresh one opens exactly what the old one opened
        // — the history, and not a live page that had already shut.
        (await anonymous.GetAsync(PastList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(LiveList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await anonymous.GetFromJsonAsync<JsonElement>(PastList(freshToken)))
            .ShouldContain(finished.Trip);
        (await anonymous.GetAsync(Follow(freshToken))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StandingAsync(finished.Trip)).ShouldBe([freshId]);

        var rows = (await admin.GetFromJsonAsync<JsonElement>($"{Admin}?pageSize=500"))
            .GetProperty("items").EnumerateArray().ToList();
        rows.Single(r => r.GetProperty("id").GetGuid() == finished.ShareId)
            .GetProperty("status").GetString().ShouldBe("revoked");
        rows.Single(r => r.GetProperty("id").GetGuid() == freshId)
            .GetProperty("status").GetString().ShouldBe("inArchive");

        // And a link that has run out can simply be taken back, which takes the trip out of the
        // cave's history.
        (await owner.DeleteAsync($"{Shares(finished.Trip)}/{freshId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(PastList(freshToken))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Whoever_may_publish_the_trip_may_replace_its_link_and_nobody_else()
    {
        var trip = await PublishedTripAsync(owner, "Whose to replace");

        (await anonymous.PostAsync(Replace(trip.Trip, trip.ShareId), null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer reads this trip — the same account opens it below — and may not write it.
        (await viewer.GetAsync($"/api/v1/trip-logs/{trip.Trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostAsync(Replace(trip.Trip, trip.ShareId), null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The trip's own coordinator with one right taken away, the right to share its cave: still
        // able to run the trip and to take the link back, not to hand the cave out again.
        await SetCaveShareDenyAsync(ownerId, trip.Cave, denied: true);
        var refused = await owner.PostAsync(Replace(trip.Trip, trip.ShareId), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Code(await refused.Content.ReadAsStringAsync()).ShouldBe("tracking.publication_refused_cave");

        // Nothing of the three refusals reached the link.
        (await StandingAsync(trip.Trip)).ShouldBe([trip.ShareId]);
        (await anonymous.GetAsync(Follow(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The right given back, the same request from the same account goes through.
        await SetCaveShareDenyAsync(ownerId, trip.Cave, denied: false);
        var byOwner = await owner.PostAsync(Replace(trip.Trip, trip.ShareId), null);
        byOwner.StatusCode.ShouldBe(HttpStatusCode.Created, await byOwner.Content.ReadAsStringAsync());
        var second = await byOwner.Content.ReadFromJsonAsync<JsonElement>();

        // And a full administrator, who neither made this trip nor published it, replaces the link
        // from the same route — which is the act the installation's own list offers.
        var byAdmin = await admin.PostAsync(Replace(trip.Trip, second.GetProperty("id").GetGuid()), null);
        byAdmin.StatusCode.ShouldBe(HttpStatusCode.Created, await byAdmin.Content.ReadAsStringAsync());
        var third = await byAdmin.Content.ReadFromJsonAsync<JsonElement>();
        (await anonymous.GetAsync(Follow(second.GetProperty("token").GetString()!)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(Follow(third.GetProperty("token").GetString()!)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditAsync(third.GetProperty("id").GetGuid(), AuditActions.Created, adminId, trip.Trip)).ShouldBe(1);
    }

    /// <summary>
    /// Two people press Replace on the same link at the same moment: one address comes out of it.
    /// </summary>
    /// <remarks>
    /// The two requests are held at the save until both have arrived there, so both have read the
    /// link as standing before either writes — the interleaving that would leave a trip with two
    /// working addresses if nothing guarded it. Fired at once without the hold they would almost
    /// always run one after the other, and the test would pass on the ordinary "already taken
    /// back" answer whether or not the guard existed; the arrival count below is what says the
    /// race was really run.
    /// </remarks>
    [Fact]
    public async Task Two_replacements_racing_leave_exactly_one_working_address()
    {
        var trip = await PublishedTripAsync(owner, "Pressed twice");

        var hold = new HoldUntilBothSave();
        using var host = new SilexGisApiFactory(connectionString, HostSettings(), services =>
        {
            JobWorkers.RemoveFrom(services);
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(hold));
        });
        var first = await AuthHelper.BearerClientAsync(host, ownerEmail);
        var second = await AuthHelper.BearerClientAsync(host, adminEmail);
        using var visitor = host.CreateClient();

        hold.Armed = true;
        var answers = await Task.WhenAll(
            first.PostAsync(Replace(trip.Trip, trip.ShareId), null),
            second.PostAsync(Replace(trip.Trip, trip.ShareId), null));
        hold.Armed = false;

        hold.Arrived.ShouldBe(2, "both requests must have read the link before either wrote");
        answers.Select(a => a.StatusCode).OrderBy(s => (int)s)
            .ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var lost = answers.Single(a => a.StatusCode == HttpStatusCode.Conflict);
        Code(await lost.Content.ReadAsStringAsync()).ShouldBe("tracking.share_revoked");
        var won = await answers.Single(a => a.StatusCode == HttpStatusCode.Created)
            .Content.ReadFromJsonAsync<JsonElement>();

        // One standing link, the winner's; its address opens the page and the old one does not.
        (await StandingAsync(trip.Trip)).ShouldBe([won.GetProperty("id").GetGuid()]);
        (await visitor.GetAsync(Follow(won.GetProperty("token").GetString()!)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await visitor.GetAsync(Follow(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The loser wrote nothing at all: two links exist for this trip, not three.
        (await LinkCountAsync(trip.Trip)).ShouldBe(2);
    }

    // ---- taking back every link of a trip ----------------------------------------------------

    [Fact]
    public async Task Every_link_of_one_trip_is_taken_back_in_one_act_and_no_other_trips()
    {
        var scene = await SceneAsync();
        var current = scene.Current;
        var extra = await MintAsync(owner, current.Trip);
        var earlier = await MintAsync(owner, current.Trip);
        (await owner.DeleteAsync($"{Shares(current.Trip)}/{earlier.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var earlierStamp = await RevokedAtAsync(earlier.ShareId);
        earlierStamp.ShouldNotBeNull();
        var elsewhere = await PublishedTripAsync(owner, "Not this one");

        foreach (var route in scene.Routes)
        {
            (await anonymous.GetAsync(route(current.Token))).StatusCode.ShouldBe(HttpStatusCode.OK, route("…"));
            (await anonymous.GetAsync(route(extra.Token))).StatusCode.ShouldBe(HttpStatusCode.OK, route("…"));
        }

        // The account that published every one of these links, and may take each back from its
        // own trip, is an editor and not a full administrator: this act is not theirs.
        var byEditor = await owner.PostAsync(RevokeTrip(current.Trip), null);
        byEditor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Code(await byEditor.Content.ReadAsStringAsync()).ShouldBe("access.forbidden");
        (await viewer.PostAsync(RevokeTrip(current.Trip), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PostAsync(RevokeTrip(current.Trip), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await StandingAsync(current.Trip)).ShouldBe([current.ShareId, extra.ShareId], ignoreOrder: true);

        var missing = await admin.PostAsync(RevokeTrip(Guid.NewGuid()), null);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Code(await missing.Content.ReadAsStringAsync()).ShouldBe("trip_log.not_found");

        var withdrawn = await WithdrawnAsync(await admin.PostAsync(RevokeTrip(current.Trip), null));
        withdrawn.ShouldBe((2, 1));

        // Both addresses are unknown ones now, on every route.
        await ShouldAnswerLikeAnInventedTokenAsync(scene.Routes, current.Token);
        await ShouldAnswerLikeAnInventedTokenAsync(scene.Routes, extra.Token);
        (await StandingAsync(current.Trip)).ShouldBeEmpty();

        // The other trip was not reached, and neither was the earlier trip of the same cave.
        (await anonymous.GetAsync(Follow(elsewhere.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(PastList(scene.Earlier.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A link taken back before keeps the instant it was taken back at, and leaves no new trail;
        // each of the two withdrawn here left one, under the administrator's name.
        (await RevokedAtAsync(earlier.ShareId)).ShouldBe(earlierStamp);
        (await AuditAsync(earlier.ShareId, AuditActions.Updated, adminId, current.Trip)).ShouldBe(0);
        (await AuditAsync(current.ShareId, AuditActions.Updated, adminId, current.Trip)).ShouldBe(1);
        (await AuditAsync(extra.ShareId, AuditActions.Updated, adminId, current.Trip)).ShouldBe(1);

        // The installation's list says the same of both.
        var rows = (await admin.GetFromJsonAsync<JsonElement>($"{Admin}?pageSize=500"))
            .GetProperty("items").EnumerateArray().ToList();
        foreach (var id in new[] { current.ShareId, extra.ShareId })
        {
            rows.Single(r => r.GetProperty("id").GetGuid() == id)
                .GetProperty("status").GetString().ShouldBe("revoked");
        }

        // Asked again, there is nothing left to take back.
        (await WithdrawnAsync(await admin.PostAsync(RevokeTrip(current.Trip), null))).ShouldBe((0, 0));
    }

    /// <summary>
    /// A link somebody else takes back while a withdrawal is running is theirs, not the
    /// withdrawal's: it keeps their instant, leaves no trail under the administrator and is not
    /// counted.
    /// </summary>
    /// <remarks>
    /// The interleaving is forced rather than hoped for. The other party's revocation is written
    /// in a transaction this test holds open, so the withdrawal reads the link as standing and
    /// then waits on its row; the test sees that wait in the database before it lets the other
    /// party commit. A withdrawal that never reached the row fails the test instead of passing it.
    /// </remarks>
    [Fact]
    public async Task A_link_somebody_else_takes_back_during_a_withdrawal_keeps_their_instant_and_is_not_counted()
    {
        var current = await PublishedTripAsync(owner, "Taken back twice");
        var theirs = await MintAsync(owner, current.Trip);
        var takenBackAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        Task<HttpResponseMessage> withdrawal;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await using var held = await db.Database.BeginTransactionAsync();
            var link = await db.TripTrackingShares.SingleAsync(s => s.Id == theirs.ShareId);
            link.RevokedAt = takenBackAt;
            await db.SaveChangesAsync();

            withdrawal = admin.PostAsync(RevokeTrip(current.Trip), null);

            var waiting = false;
            for (var i = 0; i < 300 && !waiting && !withdrawal.IsCompleted; i++)
            {
                await Task.Delay(100);
                waiting = await WaitingOnARowAsync();
            }

            waiting.ShouldBeTrue("the withdrawal never came to wait on the link held by the other transaction");
            await held.CommitAsync();
        }

        // One link was this act's to take back: the other was already somebody else's.
        (await WithdrawnAsync(await withdrawal)).ShouldBe((1, 1));
        (await StandingAsync(current.Trip)).ShouldBeEmpty();

        (await RevokedAtAsync(theirs.ShareId)).ShouldBe(takenBackAt);
        (await AuditAsync(theirs.ShareId, AuditActions.Updated, adminId, current.Trip)).ShouldBe(0);
        (await RevokedAtAsync(current.ShareId)).ShouldNotBe(takenBackAt);
        (await AuditAsync(current.ShareId, AuditActions.Updated, adminId, current.Trip)).ShouldBe(1);
    }

    /// <summary>Whether some session of this test's database is waiting for a lock right now.</summary>
    /// <remarks>
    /// The tests of this class take turns and the database is this class's alone, so a session
    /// waiting on a lock here is the request this test started.
    /// </remarks>
    private async Task<bool> WaitingOnARowAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'",
            connection);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    // ---- taking back everything --------------------------------------------------------------

    [Fact]
    public async Task Everything_is_taken_back_only_by_a_full_administrator_who_says_the_word()
    {
        var one = await PublishedTripAsync(owner, "Everything one");
        var two = await PublishedTripAsync(owner, "Everything two");
        var extra = await MintAsync(owner, one.Trip);
        var tokens = new[] { one.Token, two.Token, extra.Token };

        var (standingBefore, tripsBefore) = await StandingEverywhereAsync();
        standingBefore.ShouldBeGreaterThanOrEqualTo(3);
        tripsBefore.ShouldBeGreaterThanOrEqualTo(2);

        // Without the word, with another word, with the word nearly: refused, naming the member.
        foreach (var body in new object[]
                 {
                     new { },
                     new { confirm = "yes" },
                     new { confirm = "Revoke-Everything" },
                     new { confirm = "revoke-all" },
                 })
        {
            var refused = await admin.PostAsJsonAsync(Everything, body);
            var text = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
            Code(text).ShouldBe("validation.failed");
            JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty("confirm", out _)
                .ShouldBeTrue(text);
        }

        // With no body at all the request is refused before it reaches anything.
        ((int)(await admin.PostAsync(Everything, null)).StatusCode).ShouldBeOneOf(400, 415);

        // With the word, from accounts that are not full administrators.
        var word = new { confirm = "revoke-everything" };
        var byEditor = await owner.PostAsJsonAsync(Everything, word);
        byEditor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Code(await byEditor.Content.ReadAsStringAsync()).ShouldBe("access.forbidden");
        (await viewer.PostAsJsonAsync(Everything, word)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PostAsJsonAsync(Everything, word)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Not one of those eight requests took anything back.
        (await StandingEverywhereAsync()).ShouldBe((standingBefore, tripsBefore));
        foreach (var token in tokens)
        {
            (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The act itself reports what it withdrew: every standing link, over every trip that had one.
        (await WithdrawnAsync(await admin.PostAsJsonAsync(Everything, word)))
            .ShouldBe((standingBefore, tripsBefore));

        (await StandingEverywhereAsync()).ShouldBe((0, 0));
        foreach (var token in tokens)
        {
            (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await anonymous.GetAsync(LiveList(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await anonymous.GetAsync(PastList(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The list agrees: every link it holds is a withdrawn one.
        var list = await admin.GetFromJsonAsync<JsonElement>($"{Admin}?pageSize=500");
        var counts = list.GetProperty("counts").EnumerateArray()
            .ToDictionary(c => c.GetProperty("status").GetString()!, c => c.GetProperty("count").GetInt32());
        counts["revoked"].ShouldBe(list.GetProperty("totalItems").GetInt32());
        counts["revoked"].ShouldBeGreaterThanOrEqualTo(standingBefore);
        counts.Where(c => c.Key != "revoked").ShouldAllBe(c => c.Value == 0);
        (await AuditAsync(two.ShareId, AuditActions.Updated, adminId, two.Trip)).ShouldBe(1);

        (await WithdrawnAsync(await admin.PostAsJsonAsync(Everything, word))).ShouldBe((0, 0));

        // It is a withdrawal and not a switch: a trip whose watch is still running is published
        // again the ordinary way, under a new address, the moment somebody chooses to.
        var again = await MintAsync(owner, one.Trip);
        (await anonymous.GetAsync(Follow(again.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(Follow(one.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Everything includes the links of a deleted trip. A deleted trip is hidden from every reader
    /// and its links with it, but it can be put back, and its links come back exactly as they
    /// stood — so a link the act passed over would open again on the day its trip was restored,
    /// after an administrator had been told that every link was taken back.
    /// </summary>
    /// <remarks>
    /// The two readings are held apart on purpose. What a reader can reach is asked through the
    /// routes and the administrators' list, which go on hiding the deleted trip's link throughout;
    /// what the act did is asked of the table past that hiding, of the number it answered and of
    /// the trail. The list showing fewer rows than the act withdrew is the expected state here, not
    /// a discrepancy.
    /// </remarks>
    [Fact]
    public async Task Everything_takes_back_a_deleted_trips_links_too_so_restoring_the_trip_reopens_nothing()
    {
        var scene = await SceneAsync();
        var deleted = scene.Current;
        var elsewhere = await PublishedTripAsync(owner, "Still there");
        var word = new { confirm = "revoke-everything" };

        // The address answers on every route while the trip is there, so every refusal below is
        // about what was done to it and not about an arrangement that never opened.
        foreach (var route in scene.Routes)
        {
            (await anonymous.GetAsync(route(deleted.Token))).StatusCode.ShouldBe(HttpStatusCode.OK, route("…"));
        }

        (await owner.DeleteAsync($"/api/v1/trip-logs/{deleted.Trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Deleted, its address is an unknown one — to the same number of questions asked of the
        // database as an invented token costs — and no reading an administrator has shows the
        // link: not the list, and not the withdrawal of one trip, which finds no such trip.
        await ShouldAnswerLikeAnInventedTokenAsync(scene.Routes, deleted.Token);
        (await StandingAsync(deleted.Trip)).ShouldBeEmpty();
        (await ListedLinkIdsAsync()).ShouldNotContain(deleted.ShareId);
        var oneTrip = await admin.PostAsync(RevokeTrip(deleted.Trip), null);
        oneTrip.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Code(await oneTrip.Content.ReadAsStringAsync()).ShouldBe("trip_log.not_found");

        // And in storage it stands, which is the whole of the risk.
        (await StoredRevokedAtAsync(deleted.ShareId)).ShouldBeNull();

        // What the ordinary reading counts as standing leaves the deleted trip's link out; the act
        // answers with one link and one trip more than that.
        var (standingBefore, tripsBefore) = await StandingEverywhereAsync();
        standingBefore.ShouldBeGreaterThanOrEqualTo(2);
        (await WithdrawnAsync(await admin.PostAsJsonAsync(Everything, word)))
            .ShouldBe((standingBefore + 1, tripsBefore + 1));

        // Stamped, and on the trail under the administrator's name like any other link.
        (await StoredRevokedAtAsync(deleted.ShareId)).ShouldNotBeNull();
        (await AuditAsync(deleted.ShareId, AuditActions.Updated, adminId, deleted.Trip)).ShouldBe(1);
        (await AuditAsync(elsewhere.ShareId, AuditActions.Updated, adminId, elsewhere.Trip)).ShouldBe(1);

        // The list still hides it: fewer rows than the act withdrew.
        var listed = await ListedLinkIdsAsync();
        listed.ShouldContain(elsewhere.ShareId);
        listed.ShouldNotContain(deleted.ShareId);

        (await owner.PostAsync($"/api/v1/trip-logs/{deleted.Trip}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The trip is back, and the address it was published under is still one nobody ever made.
        (await owner.GetAsync($"/api/v1/trip-logs/{deleted.Trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldAnswerLikeAnInventedTokenAsync(scene.Routes, deleted.Token);
        (await StandingAsync(deleted.Trip)).ShouldBeEmpty();

        // With its trip back the link is on the list again, as a withdrawn one.
        var rows = (await admin.GetFromJsonAsync<JsonElement>($"{Admin}?pageSize=500"))
            .GetProperty("items").EnumerateArray().ToList();
        rows.Single(r => r.GetProperty("id").GetGuid() == deleted.ShareId)
            .GetProperty("status").GetString().ShouldBe("revoked");

        // And nothing was left for a second asking.
        (await WithdrawnAsync(await admin.PostAsJsonAsync(Everything, word))).ShouldBe((0, 0));
    }

    // ---- the comparison with an invented token -----------------------------------------------

    /// <summary>
    /// Asks every route about <paramref name="withdrawn"/> and about an invented token, on a host
    /// that counts database commands, and requires the two to be answered alike in every respect a
    /// caller could observe.
    /// </summary>
    private async Task ShouldAnswerLikeAnInventedTokenAsync(
        IReadOnlyList<Func<string, string>> routes, string withdrawn)
    {
        var counter = new CommandCounter();
        using var counted = new SilexGisApiFactory(connectionString, HostSettings(), services =>
        {
            JobWorkers.RemoveFrom(services);
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(counter));
        });
        using var client = counted.CreateClient();

        foreach (var route in routes)
        {
            counter.Reset();
            var real = await client.GetAsync(route(withdrawn));
            var realCommands = counter.Count;
            counter.Reset();
            var invented = await client.GetAsync(route(Invented));
            var inventedCommands = counter.Count;

            real.StatusCode.ShouldBe(HttpStatusCode.NotFound, route("…"));
            (await RefusalShapeAsync(real)).ShouldBe(await RefusalShapeAsync(invented), route("…"));
            // The lookup of the link is itself a command, so zero on both sides would mean the
            // counter was never attached and the equality proved nothing.
            inventedCommands.ShouldBeGreaterThan(0, route("…"));
            realCommands.ShouldBe(inventedCommands, route("…"));
        }
    }

    /// <summary>A refusal as a caller sees it, without the trace identifier, which differs per request.</summary>
    private static async Task<string> RefusalShapeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var members = body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");
        return $"{(int)response.StatusCode} {string.Join('&', members)}";
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

    /// <summary>
    /// Holds every save that is about to add a link until two such saves are waiting, then lets
    /// both go — so two requests that each mean to replace a link have both read it first.
    /// </summary>
    /// <remarks>
    /// Bounded, so a request that never gets company fails the test on its arrival count instead
    /// of hanging the run.
    /// </remarks>
    private sealed class HoldUntilBothSave : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;
        private volatile bool armed;

        public bool Armed
        {
            get => armed;
            set => armed = value;
        }

        public int Arrived => Volatile.Read(ref arrived);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (armed
                && eventData.Context is { } context
                && context.ChangeTracker.Entries<TripTrackingShare>().Any(e => e.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrived) >= 2)
                {
                    both.TrySetResult();
                }

                await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));
            }

            return result;
        }
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Replace(Guid trip, Guid share) => $"{Shares(trip)}/{share}/replace";

    private static string RevokeTrip(Guid trip) => $"{Admin}/{trip}/revoke-all";

    private static string Follow(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string PastList(string token) => $"{Follow(token)}/past";

    private static string LiveList(string token) => $"{Follow(token)}/live";

    private static string PastTrack(string token, Guid trip) => $"{Follow(token)}/past/{trip}";

    private sealed record Published(Guid Trip, Guid Cave, Guid ShareId, string Token, DateTimeOffset ExpiresAt);

    /// <summary>
    /// A cave with a finished, published trip in its history and a party underground now — the
    /// arrangement in which the address of the running trip answers on all four anonymous routes.
    /// </summary>
    private sealed record Scene(Published Earlier, Published Current, IReadOnlyList<Func<string, string>> Routes);

    private async Task<Scene> SceneAsync(Guid? memberCaver = null)
    {
        var cave = await CaveAsync();
        var model = await ModelAsync(cave);
        var earlier = await PublishedTripAsync(owner, "Earlier", cave, model);
        await CloseAsync(earlier.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var current = await PublishedTripAsync(owner, "Underground now", cave, model, memberCaver);
        return new Scene(
            earlier,
            current,
            [Follow, LiveList, PastList, token => PastTrack(token, earlier.Trip)]);
    }

    /// <summary>
    /// A trip armed against a model of a cave, one caver placed, and published — all through
    /// <paramref name="coordinator"/>. With no cave named it gets one of its own.
    /// </summary>
    private async Task<Published> PublishedTripAsync(
        HttpClient coordinator, string title, Guid? cave = null, Guid? model = null, Guid? memberCaver = null)
    {
        var caveId = cave ?? await CaveAsync();
        var modelId = model ?? await ModelAsync(caveId);

        var participants = new List<object> { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..24] } };
        if (memberCaver is { } member)
        {
            participants.Add(new { caverId = member });
        }

        var created = await coordinator.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility = "authenticated",
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        Guid caver;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caver = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
                .OrderBy(p => p.CaverId).Select(p => p.CaverId).FirstAsync();
        }

        (await PutConfigAsync(coordinator, trip, new { state = "armed", surveyModelId = modelId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await coordinator.PostAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/tracking/events",
                new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var minted = await MintAsync(coordinator, trip);
        return new Published(trip, caveId, minted.ShareId, minted.Token, minted.ExpiresAt);
    }

    private static async Task<(Guid ShareId, string Token, DateTimeOffset ExpiresAt)> MintAsync(
        HttpClient client, Guid trip)
    {
        var minted = await client.PostAsync(Shares(trip), null);
        var payload = await minted.Content.ReadAsStringAsync();
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var body = JsonDocument.Parse(payload).RootElement;
        return (
            body.GetProperty("id").GetGuid(),
            body.GetProperty("token").GetString()!,
            body.GetProperty("expiresAt").GetDateTimeOffset());
    }

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    /// <remarks>
    /// Backdated straight into the column because it is a plain recorded fact with nothing derived
    /// from it, read against the clock on every request.
    /// </remarks>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    /// <summary>Through the write service that maintains the derived column, never the column itself.</summary>
    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    /// <summary>An explicit deny of the right to share one cave, for one account, and its removal.</summary>
    private async Task SetCaveShareDenyAsync(Guid userId, Guid caveFeatureId, bool denied)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var existing = await db.AccessEntries.FirstOrDefaultAsync(e =>
            e.SubjectKind == AccessSubjectKind.User && e.SubjectId == userId
            && e.Domain == AccessDomain.Features && e.ScopeFeatureId == caveFeatureId);

        if (denied && existing is null)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = userId,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Share,
                Effect = AccessEffect.Deny,
                ScopeKind = AccessScopeKind.Object,
                ScopeFeatureId = caveFeatureId,
            });
        }
        else if (!denied && existing is not null)
        {
            db.AccessEntries.Remove(existing);
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> CaverOfUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.Id).SingleAsync();
    }

    private async Task<Guid> CaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Withdrawn Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A survey model created the real way, then stations seeded into the graph tables.</summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "pubwithdraw.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(
        Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

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

    // ---- reading the state -------------------------------------------------------------------

    /// <summary>The ids of one trip's links that nobody has taken back, read off the table.</summary>
    private async Task<List<Guid>> StandingAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripTrackingShares.AsNoTracking()
            .Where(s => s.TripLogId == trip && s.RevokedAt == null)
            .Select(s => s.Id).ToListAsync();
    }

    private async Task<int> LinkCountAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripTrackingShares.AsNoTracking().CountAsync(s => s.TripLogId == trip);
    }

    /// <summary>How many links stand across the installation, and over how many trips.</summary>
    private async Task<(int Links, int Trips)> StandingEverywhereAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var standing = db.TripTrackingShares.AsNoTracking().Where(s => s.RevokedAt == null);
        return (await standing.CountAsync(), await standing.Select(s => s.TripLogId).Distinct().CountAsync());
    }

    private async Task<DateTimeOffset?> RevokedAtAsync(Guid share)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripTrackingShares.AsNoTracking()
            .Where(s => s.Id == share).Select(s => s.RevokedAt).SingleAsync();
    }

    /// <summary>
    /// When a link was taken back, read past the hiding of a deleted trip's rows: the one reading
    /// in which a link that is kept and hidden can be told from one that was withdrawn.
    /// </summary>
    private async Task<DateTimeOffset?> StoredRevokedAtAsync(Guid share)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripTrackingShares.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.Id == share).Select(s => s.RevokedAt).SingleAsync();
    }

    /// <summary>The links the administrators' list holds, whatever their status.</summary>
    private async Task<List<Guid>> ListedLinkIdsAsync() =>
        [.. (await admin.GetFromJsonAsync<JsonElement>($"{Admin}?pageSize=500"))
            .GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];

    /// <summary>
    /// How many trail rows one link has for one action by one account, filed under its trip.
    /// </summary>
    private async Task<int> AuditAsync(Guid share, string action, Guid by, Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AuditEntries.AsNoTracking().CountAsync(a =>
            a.EntityType == nameof(TripTrackingShare) && a.EntityId == share.ToString()
            && a.Action == action && a.UserId == by
            && a.RootEntityType == nameof(TripLog) && a.RootEntityId == trip.ToString());
    }

    /// <summary>Who has been told that a trip was published, read off the notification rows.</summary>
    private async Task<List<Guid>> PublishedNoticesAsync(Guid tripLogId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateKey == MessageTemplateCatalog.NotifyTripPublished
                && n.TargetKind == NotificationTargetKind.TripLog
                && n.TargetId == tripLogId)
            .Select(n => n.RecipientUserId)
            .ToListAsync();
    }

    private static async Task<(int Links, int Trips)> WithdrawnAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonDocument.Parse(payload).RootElement;
        return (body.GetProperty("revokedLinks").GetInt32(), body.GetProperty("trips").GetInt32());
    }

    private static List<Guid> ListedIds(JsonElement list) =>
        [.. list.GetProperty("trips").EnumerateArray().Select(t => t.GetProperty("tripLogId").GetGuid())];

    private static string? Code(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();
}
