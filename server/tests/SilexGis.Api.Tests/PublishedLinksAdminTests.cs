// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Diagnostics;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation-wide list of published links, read by a full administrator.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim these hold down is that the list tells the truth about the pages.</b> A status is
/// only worth showing if the link it describes answers accordingly, so the walk below asks the
/// list and the anonymous routes about the same token at the same instant of one fake clock, at
/// every stage of a link's life — a status asserted on its own could not tell "computed by the
/// routes' rules" from "computed by rules that happen to agree today".
/// </para>
/// <para>
/// Every test of this class shares one database, so the list always holds other tests' links as
/// well. Nothing here counts the whole list; each assertion is about links this test made, found
/// by id, or about the list's own arithmetic.
/// </para>
/// <para>
/// Protection is switched on through the write service that maintains the derived columns, never
/// by writing the column: written by hand it leaves the effective flag stale and the assertion
/// passes whether or not the rule works.
/// </para>
/// </remarks>
public sealed class PublishedLinksAdminTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string List = "/api/v1/admin/published-trips";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient admin = null!;
    private HttpClient owner = null!;
    private string adminEmail = null!;
    private string ownerEmail = null!;
    private Guid ownerId;
    private long caveTypeId;

    public PublishedLinksAdminTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"publinks-{Guid.NewGuid():N}");
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
        adminEmail = $"publinks-adm-{suffix}@t.local";
        ownerEmail = $"publinks-own-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);

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

    [Fact]
    public async Task A_full_administrator_reads_links_across_trips_and_nobody_else_reads_any()
    {
        var first = await PublishedTripAsync(owner, "Across one");
        var second = await PublishedTripAsync(owner, "Across two");

        var answer = await ListAsync(admin, "?pageSize=500");
        var rows = Rows(answer);

        // Two trips the administrator neither made nor published, each with what the page needs.
        foreach (var trip in new[] { first, second })
        {
            var row = rows.Single(r => r.GetProperty("id").GetGuid() == trip.ShareId);
            row.GetProperty("tripLogId").GetGuid().ShouldBe(trip.Trip);
            row.GetProperty("tripTitle").GetString().ShouldBe(trip.Title);
            row.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
            row.GetProperty("cave").GetProperty("id").GetGuid().ShouldBe(trip.Cave);
            row.GetProperty("cave").GetProperty("name").GetString().ShouldBe(trip.CaveName);
            row.GetProperty("watchState").GetString().ShouldBe("armed");
            row.GetProperty("createdBy").GetGuid().ShouldBe(ownerId);
            row.GetProperty("createdByLabel").GetString().ShouldNotBeNullOrWhiteSpace();
            row.GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
            row.GetProperty("createdAt").GetDateTimeOffset()
                .ShouldBeLessThan(row.GetProperty("expiresAt").GetDateTimeOffset());
            row.GetProperty("status").GetString().ShouldBe("followable");
        }

        // Said once for the installation, from its settings as shipped.
        answer.GetProperty("publishesRealNames").GetBoolean().ShouldBeTrue();
        answer.GetProperty("archiveEnabled").GetBoolean().ShouldBeTrue();

        // The same request from the very account that published both links, and may still manage
        // each of them from its own trip: an editor, who reads past every visibility setting, is
        // not a full administrator and gets none of the installation's list.
        (await owner.GetAsync(Shares(first.Trip))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await owner.GetAsync(List);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Code(await refused.Content.ReadAsStringAsync()).ShouldBe("access.forbidden");

        var viewerEmail = $"publinks-view-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, viewerEmail);
        var viewer = await AuthHelper.BearerClientAsync(factory, viewerEmail);
        (await viewer.GetAsync(List)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await factory.CreateClient().GetAsync(List)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The list says what its statuses rest on: the installation's own periods and its read
    /// limit, as shipped on one host and as set on another — and says it to a full administrator
    /// only.
    /// </summary>
    /// <remarks>
    /// Two hosts over one database, so that what differs between the two answers is the settings
    /// and nothing else: an answer that printed constants would pass on the first and fail on the
    /// second.
    /// </remarks>
    [Fact]
    public async Task The_list_states_the_settings_its_statuses_rest_on_to_an_administrator_only()
    {
        // As shipped: two weeks, two days, an archive with no limit, 120 reads a minute, and no
        // limit on how long an old link lists today's parties.
        var shipped = (await ListAsync(admin, "?pageSize=1")).GetProperty("settings");
        shipped.GetProperty("shareLifetimeSeconds").GetInt64().ShouldBe(14 * 86_400);
        shipped.GetProperty("shareGraceAfterCloseSeconds").GetInt64().ShouldBe(2 * 86_400);
        shipped.GetProperty("archiveRetentionSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
        shipped.GetProperty("publicReadsPerMinute").GetInt32().ShouldBe(120);
        shipped.GetProperty("siblingWindowAfterLapseSeconds").ValueKind.ShouldBe(JsonValueKind.Null);

        var settings = HostSettings();
        settings["TripTracking:ShareLifetime"] = "21.00:00:00";
        settings["TripTracking:ShareGraceAfterClose"] = "00:00:00";
        settings["TripPastTracks:Retention"] = "365.00:00:00";
        settings["TripTracking:PublicRateLimitPerMinute"] = "45";
        settings["TripTracking:SiblingWindowAfterLapse"] = "1.12:00:00";
        using var host = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
        var hostAdmin = await AuthHelper.BearerClientAsync(host, adminEmail);

        var set = (await ListAsync(hostAdmin, "?pageSize=1")).GetProperty("settings");
        set.GetProperty("shareLifetimeSeconds").GetInt64().ShouldBe(21 * 86_400);
        // No grace is a period of nothing, said as zero: it is a setting an installation chose,
        // not an absent one.
        set.GetProperty("shareGraceAfterCloseSeconds").GetInt64().ShouldBe(0);
        set.GetProperty("archiveRetentionSeconds").GetInt64().ShouldBe(365 * 86_400);
        set.GetProperty("publicReadsPerMinute").GetInt32().ShouldBe(45);
        set.GetProperty("siblingWindowAfterLapseSeconds").GetInt64().ShouldBe(36 * 3_600);

        // The settings of an installation are not for whoever publishes on it. An editor reads
        // past every visibility setting and is refused the whole answer, on the host with the
        // settings changed as on the other; so is somebody with no account.
        var hostEditor = await AuthHelper.BearerClientAsync(host, ownerEmail);
        var refused = await hostEditor.GetAsync(List);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var refusal = await refused.Content.ReadAsStringAsync();
        Code(refusal).ShouldBe("access.forbidden");
        refusal.ShouldNotContain("shareLifetimeSeconds");
        refusal.ShouldNotContain("publicReadsPerMinute");
        (await host.CreateClient().GetAsync(List)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// One link through its whole life on one clock, the list and the pages asked together.
    /// </summary>
    /// <remarks>
    /// The windows are set to minutes because the clock may only move by minutes: every client here
    /// is signed in while this host's clock and the machine's still agree, and a session outlives a
    /// quarter of an hour by neither.
    /// </remarks>
    [Fact]
    public async Task Each_status_is_what_the_link_itself_answers_at_the_same_instant()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var settings = HostSettings();
        settings["TripTracking:ShareGraceAfterClose"] = "00:01:00";
        settings["TripTracking:ShareLifetime"] = "00:06:00";
        using var host = new SilexGisApiFactory(connectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });
        var hostAdmin = await AuthHelper.BearerClientAsync(host, adminEmail);
        var coordinator = await AuthHelper.BearerClientAsync(host, ownerEmail);
        var visitor = host.CreateClient();
        var start = clock.Now;

        // Two parties in two caves. One comes out; the other's coordinator forgets the watch.
        var finished = await PublishedTripAsync(coordinator, "Walked out");
        var forgotten = await PublishedTripAsync(coordinator, "Left running");

        async Task<string> StatusAsync(Guid shareId)
        {
            var answer = await ListAsync(hostAdmin, "?pageSize=500");
            // Every status in the answer was decided at the one instant the clock shows.
            answer.GetProperty("asOf").GetDateTimeOffset().ShouldBe(clock.Now);
            return Rows(answer).Single(r => r.GetProperty("id").GetGuid() == shareId)
                .GetProperty("status").GetString()!;
        }

        async Task<HttpStatusCode> AnswersAsync(string url) => (await visitor.GetAsync(url)).StatusCode;

        // Followable: the page follows the party.
        (await StatusAsync(finished.ShareId)).ShouldBe("followable");
        (await AnswersAsync(Live(finished.Token))).ShouldBe(HttpStatusCode.OK);

        // In grace: closed a moment ago, and the page still answers to say so.
        clock.Now = start.AddMinutes(1);
        (await PutConfigAsync(coordinator, finished.Trip, new { state = "closed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(finished.ShareId)).ShouldBe("inGrace");
        (await AnswersAsync(Live(finished.Token))).ShouldBe(HttpStatusCode.OK);
        ListedIds(await visitor.GetFromJsonAsync<JsonElement>(PastList(finished.Token)))
            .ShouldNotContain(finished.Trip);

        // In the archive: the grace has run out, the live page is gone, the history opens.
        clock.Now = start.AddMinutes(3);
        (await StatusAsync(finished.ShareId)).ShouldBe("inArchive");
        (await AnswersAsync(Live(finished.Token))).ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await visitor.GetFromJsonAsync<JsonElement>(PastList(finished.Token)))
            .ShouldContain(finished.Trip);
        // The other party is still underground and still followed.
        (await StatusAsync(forgotten.ShareId)).ShouldBe("followable");
        (await AnswersAsync(Live(forgotten.Token))).ShouldBe(HttpStatusCode.OK);

        // Both links have now run out by themselves. The finished trip stays in the archive — a
        // link nobody took back is exactly what keeps it there — and the forgotten watch has
        // lapsed: neither followable on a dead link nor history while it is still armed.
        clock.Now = start.AddMinutes(8);
        (await StatusAsync(finished.ShareId)).ShouldBe("inArchive");
        (await AnswersAsync(PastList(finished.Token))).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(forgotten.ShareId)).ShouldBe("lapsed");
        (await AnswersAsync(Live(forgotten.Token))).ShouldBe(HttpStatusCode.NotFound);
        (await AnswersAsync(PastList(forgotten.Token))).ShouldBe(HttpStatusCode.NotFound);
        (await AnswersAsync(LiveList(forgotten.Token))).ShouldBe(HttpStatusCode.NotFound);

        // Withheld: the finished trip's cave is protected, and the archive shuts for as long as it
        // is. The lapsed link next to it stays lapsed — it was opening nothing anyway.
        await SetLocationProtectedAsync(finished.Cave, true);
        (await StatusAsync(finished.ShareId)).ShouldBe("withheld");
        (await AnswersAsync(PastList(finished.Token))).ShouldBe(HttpStatusCode.NotFound);
        (await StatusAsync(forgotten.ShareId)).ShouldBe("lapsed");

        await SetLocationProtectedAsync(finished.Cave, false);
        (await StatusAsync(finished.ShareId)).ShouldBe("inArchive");
        (await AnswersAsync(PastList(finished.Token))).ShouldBe(HttpStatusCode.OK);

        // Revoked: taken back by the person who handed it out, and it opens nothing again.
        (await coordinator.DeleteAsync($"{Shares(finished.Trip)}/{finished.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var revoked = Rows(await ListAsync(hostAdmin, "?pageSize=500"))
            .Single(r => r.GetProperty("id").GetGuid() == finished.ShareId);
        revoked.GetProperty("status").GetString().ShouldBe("revoked");
        // Stamped from the same clock; the column keeps microseconds, the clock carries finer.
        revoked.GetProperty("revokedAt").GetDateTimeOffset().ShouldBe(clock.Now, TimeSpan.FromMilliseconds(1));
        (await AnswersAsync(PastList(finished.Token))).ShouldBe(HttpStatusCode.NotFound);
        (await AnswersAsync(Live(finished.Token))).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_filter_the_counts_the_order_and_the_pages_describe_one_list()
    {
        var followed = await PublishedTripAsync(owner, "bravo filter");
        var withdrawn = await PublishedTripAsync(owner, "Alpha filter");
        var archived = await PublishedTripAsync(owner, "charlie filter");
        (await owner.DeleteAsync($"{Shares(withdrawn.Trip)}/{withdrawn.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await CloseAsync(archived.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        var everything = await ListAsync(admin, "?pageSize=500");
        var all = Rows(everything);
        string StatusOf(Guid shareId) =>
            all.Single(r => r.GetProperty("id").GetGuid() == shareId).GetProperty("status").GetString()!;
        StatusOf(followed.ShareId).ShouldBe("followable");
        StatusOf(withdrawn.ShareId).ShouldBe("revoked");
        StatusOf(archived.ShareId).ShouldBe("inArchive");

        // The filter keeps one status and loses the rest — asserted beside the unfiltered answer
        // above, which holds all three, so an empty or unfiltered list could not pass this.
        var onlyRevoked = await ListAsync(admin, "?status=revoked&pageSize=500");
        var revokedIds = Ids(onlyRevoked);
        revokedIds.ShouldContain(withdrawn.ShareId);
        revokedIds.ShouldNotContain(followed.ShareId);
        revokedIds.ShouldNotContain(archived.ShareId);
        Rows(onlyRevoked).ShouldAllBe(r => r.GetProperty("status").GetString() == "revoked");
        Ids(await ListAsync(admin, "?status=inArchive&pageSize=500")).ShouldContain(archived.ShareId);

        // The counts name every status, add up to the whole list, agree with what each filter
        // returns, and do not move when a filter is applied.
        var counts = Counts(everything);
        counts.Keys.ShouldBe(
            ["followable", "inGrace", "inArchive", "withheld", "lapsed", "revoked"], ignoreOrder: true);
        counts.Values.Sum().ShouldBe(everything.GetProperty("totalItems").GetInt32());
        counts["revoked"].ShouldBe(onlyRevoked.GetProperty("totalItems").GetInt32());
        counts["revoked"].ShouldBe(revokedIds.Count);
        Counts(onlyRevoked).ShouldBe(counts);

        // Newest first when nothing is asked, and the pages are that same list cut up: walked two
        // at a time they give every link once, in order.
        var newestFirst = Ids(everything);
        newestFirst.IndexOf(archived.ShareId).ShouldBeLessThan(newestFirst.IndexOf(followed.ShareId));
        var walked = new List<Guid>();
        for (var page = 1; walked.Count < newestFirst.Count; page++)
        {
            var slice = await ListAsync(admin, $"?pageSize=2&page={page}");
            slice.GetProperty("totalItems").GetInt32().ShouldBe(newestFirst.Count);
            var ids = Ids(slice);
            ids.Count.ShouldBeInRange(1, 2);
            walked.AddRange(ids);
        }
        walked.ShouldBe(newestFirst);
        Ids(await ListAsync(admin, $"?pageSize=2&page={(newestFirst.Count / 2) + 2}")).ShouldBeEmpty();

        // A named order, in both directions, without regard to the titles' case.
        var byTitle = Rows(await ListAsync(admin, "?sort=tripTitle&pageSize=500"))
            .Select(r => r.GetProperty("tripTitle").GetString()!).ToList();
        byTitle.ShouldBe(byTitle.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList());
        byTitle.IndexOf(withdrawn.Title).ShouldBeLessThan(byTitle.IndexOf(followed.Title));
        var byTitleDown = Rows(await ListAsync(admin, "?sort=tripTitle&descending=true&pageSize=500"))
            .Select(r => r.GetProperty("tripTitle").GetString()!).ToList();
        byTitleDown.IndexOf(followed.Title).ShouldBeLessThan(byTitleDown.IndexOf(withdrawn.Title));

        // Ordered by status, the links doing the most come first.
        var byStatus = Ids(await ListAsync(admin, "?sort=status&pageSize=500"));
        byStatus.IndexOf(followed.ShareId).ShouldBeLessThan(byStatus.IndexOf(archived.ShareId));
        byStatus.IndexOf(archived.ShareId).ShouldBeLessThan(byStatus.IndexOf(withdrawn.ShareId));

        // What the list cannot be asked: each is a refusal naming the parameter, not a guess. A
        // word that is not one of a closed list — a number and a list of two among them — is
        // refused under the code every such list is refused by; a figure out of range under the
        // validators' own.
        foreach (var (query, parameter, code) in new[]
                 {
                     ("?status=published", "status", "validation.invalid_enum"),
                     ("?status=5", "status", "validation.invalid_enum"),
                     ("?status=revoked,lapsed", "status", "validation.invalid_enum"),
                     ("?sort=handle", "sort", "validation.invalid_enum"),
                     ("?sort=1", "sort", "validation.invalid_enum"),
                     ("?page=0", "page", "validation.failed"),
                     ("?pageSize=0", "pageSize", "validation.failed"),
                     ("?pageSize=501", "pageSize", "validation.failed"),
                 })
        {
            var refused = await admin.GetAsync(List + query);
            var body = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            Code(body).ShouldBe(code, query);
            JsonDocument.Parse(body).RootElement.GetProperty("errors").TryGetProperty(parameter, out _)
                .ShouldBeTrue($"{query}: {body}");
        }

        // Who may ask is settled before what was asked: a malformed question from somebody who
        // may not read the list is refused as theirs to ask, not corrected.
        (await owner.GetAsync(List + "?status=published")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// With past trips switched off, a finished trip's link is in no archive: it opens nothing.
    /// </summary>
    /// <remarks>
    /// The same link is asked on two hosts over one database that differ in that one setting, so
    /// what changes between the two answers is the switch and nothing else — and on each host the
    /// list is held against what the address itself answers there.
    /// </remarks>
    [Fact]
    public async Task With_past_trips_switched_off_a_finished_trips_link_has_lapsed_and_opens_nothing()
    {
        var settings = HostSettings();
        settings["TripPastTracks:Enabled"] = "false";
        using var host = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
        var hostAdmin = await AuthHelper.BearerClientAsync(host, adminEmail);
        var visitor = host.CreateClient();
        var elsewhere = factory.CreateClient();

        var finished = await PublishedTripAsync(owner, "Over, archive off");
        await CloseAsync(finished.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var running = await PublishedTripAsync(owner, "Running, archive off");

        // Where past trips are served, this is a link in the archive, and its address opens it.
        var served = await ListAsync(admin, "?pageSize=500");
        served.GetProperty("archiveEnabled").GetBoolean().ShouldBeTrue();
        Rows(served).Single(r => r.GetProperty("id").GetGuid() == finished.ShareId)
            .GetProperty("status").GetString().ShouldBe("inArchive");
        (await elsewhere.GetAsync(PastList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await elsewhere.GetAsync(LiveList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Where they are not, the same link has lapsed, and every route answers it with a 404.
        var answer = await ListAsync(hostAdmin, "?pageSize=500");
        answer.GetProperty("archiveEnabled").GetBoolean().ShouldBeFalse();
        Rows(answer).Single(r => r.GetProperty("id").GetGuid() == finished.ShareId)
            .GetProperty("status").GetString().ShouldBe("lapsed");
        Counts(answer)["inArchive"].ShouldBe(0);
        (await visitor.GetAsync(Live(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync(LiveList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync(PastList(finished.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The switch is over history only: a party underground is followed as before.
        Rows(answer).Single(r => r.GetProperty("id").GetGuid() == running.ShareId)
            .GetProperty("status").GetString().ShouldBe("followable");
        (await visitor.GetAsync(Live(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await visitor.GetAsync(LiveList(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_answer_carries_no_token_and_no_stored_hash_only_the_handle_the_log_writes()
    {
        var trip = await PublishedTripAsync(owner, "No secret");
        var storedHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(trip.Token)));

        // The row really is stored under that hash, so the absence asserted below is the absence
        // of the thing a token is looked up by and not of a string nobody ever wrote.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripTrackingShares.AsNoTracking().SingleAsync(s => s.Id == trip.ShareId))
                .TokenHash.ShouldBe(storedHash);
        }

        var response = await admin.GetAsync(List + "?pageSize=500");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        // The link is in the answer, named by its handle ...
        var row = Rows(JsonDocument.Parse(body).RootElement)
            .Single(r => r.GetProperty("id").GetGuid() == trip.ShareId);
        var handle = row.GetProperty("handle").GetString()!;
        handle.ShouldBe(storedHash[..8]);

        // ... which is the handle a request for the published page leaves in the log, so the two
        // can be matched by eye ...
        CredentialUrlScrubber.Scrub(Live(trip.Token)).ShouldBe($"/api/v1/public/trips/[token:{handle}]");

        // ... and neither the token nor the hash it is looked up by is anywhere in the body.
        body.ShouldNotContain(trip.Token);
        body.ShouldNotContain(storedHash);
        row.EnumerateObject().Select(p => p.Name).ShouldNotContain("token");
        row.EnumerateObject().Select(p => p.Name).ShouldNotContain("tokenHash");
    }

    /// <summary>
    /// A watch nobody closed is found by how long it has run, whatever its link is doing.
    /// </summary>
    /// <remarks>
    /// The moments are written straight into their columns because each is a plain recorded fact
    /// with nothing derived from it, read against the clock on every request — so a watch written
    /// as started ten days ago is exactly a watch that was started ten days ago. The three watches
    /// differ in one fact each, so what the filter keeps and what it loses are told apart by that
    /// fact and nothing else.
    /// </remarks>
    [Fact]
    public async Task A_watch_left_running_is_found_by_how_long_it_has_run_and_a_closed_one_is_not()
    {
        var forgotten = await PublishedTripAsync(owner, "Forgotten watch");
        var fresh = await PublishedTripAsync(owner, "Fresh watch");
        var closed = await PublishedTripAsync(owner, "Closed watch");
        var tenDaysAgo = DateTimeOffset.UtcNow.AddDays(-10);
        await SetArmedAtAsync(forgotten.Trip, tenDaysAgo);
        await SetArmedAtAsync(closed.Trip, tenDaysAgo);
        (await PutConfigAsync(owner, closed.Trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Every row says when its watch was started, the closed one included.
        var everything = await ListAsync(admin, "?pageSize=500");
        JsonElement RowOf(JsonElement answer, Published trip) =>
            Rows(answer).Single(r => r.GetProperty("id").GetGuid() == trip.ShareId);
        RowOf(everything, forgotten).GetProperty("watchArmedAt").GetDateTimeOffset()
            .ShouldBe(tenDaysAgo, TimeSpan.FromMilliseconds(1));
        RowOf(everything, closed).GetProperty("watchArmedAt").GetDateTimeOffset()
            .ShouldBe(tenDaysAgo, TimeSpan.FromMilliseconds(1));
        RowOf(everything, fresh).GetProperty("watchArmedAt").GetDateTimeOffset()
            .ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(-1));

        // Longer than a week: the forgotten one, and neither the one started today nor the one
        // started just as long ago that somebody closed.
        var overAWeek = await ListAsync(admin, "?armedLongerThanDays=7&pageSize=500");
        Ids(overAWeek).ShouldContain(forgotten.ShareId);
        Ids(overAWeek).ShouldNotContain(fresh.ShareId);
        Ids(overAWeek).ShouldNotContain(closed.ShareId);
        Rows(overAWeek).ShouldAllBe(r => r.GetProperty("watchState").GetString() == "armed");
        overAWeek.GetProperty("totalItems").GetInt32().ShouldBe(Rows(overAWeek).Count);
        // The figures above the list describe the installation and do not move with the question.
        Counts(overAWeek).ShouldBe(Counts(everything));

        // Zero days is "every watch that is running": both running ones, still not the closed one.
        var running = Ids(await ListAsync(admin, "?armedLongerThanDays=0&pageSize=500"));
        running.ShouldContain(forgotten.ShareId);
        running.ShouldContain(fresh.ShareId);
        running.ShouldNotContain(closed.ShareId);

        // Longer than the watch has run: not found.
        Ids(await ListAsync(admin, "?armedLongerThanDays=30&pageSize=500")).ShouldNotContain(forgotten.ShareId);

        // Longest-running first when asked in that order.
        var byStart = Ids(await ListAsync(admin, "?sort=watchArmedAt&armedLongerThanDays=0&pageSize=500"));
        byStart.IndexOf(forgotten.ShareId).ShouldBeLessThan(byStart.IndexOf(fresh.ShareId));

        // The case nobody notices: the forgotten watch's link runs out. It follows nothing and is
        // in no public list — and it is still found here, because the question is asked of the
        // watch. With the status it now has it is found; with the one it had it is not.
        //
        // First, what is true before it runs out: the link opens, both of its lists answer, and it
        // is found as followable. Without this, every "not" below would also hold for a link that
        // had never worked.
        using var reader = factory.CreateClient();
        (await reader.GetAsync(Live(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync(LiveList(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync(PastList(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var followableAndRunning = await ListAsync(admin, "?status=followable&armedLongerThanDays=7&pageSize=500");
        RowOf(followableAndRunning, forgotten).GetProperty("status").GetString().ShouldBe("followable");
        Ids(await ListAsync(admin, "?status=lapsed&armedLongerThanDays=7&pageSize=500"))
            .ShouldNotContain(forgotten.ShareId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var link = await db.TripTrackingShares.SingleAsync(l => l.Id == forgotten.ShareId);
            link.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        (await reader.GetAsync(Live(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync(LiveList(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync(PastList(forgotten.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var lapsedAndRunning = await ListAsync(admin, "?status=lapsed&armedLongerThanDays=7&pageSize=500");
        RowOf(lapsedAndRunning, forgotten).GetProperty("status").GetString().ShouldBe("lapsed");
        Ids(await ListAsync(admin, "?status=followable&armedLongerThanDays=7&pageSize=500"))
            .ShouldNotContain(forgotten.ShareId);

        // A number that cannot be a number of days is refused by name, not read as "no filter".
        foreach (var query in new[] { "?armedLongerThanDays=-1", "?armedLongerThanDays=3651" })
        {
            var refused = await admin.GetAsync(List + query);
            var body = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            Code(body).ShouldBe("validation.failed", query);
            JsonDocument.Parse(body).RootElement.GetProperty("errors").TryGetProperty("armedLongerThanDays", out _)
                .ShouldBeTrue($"{query}: {body}");
        }

        // And the question is still only an administrator's to ask.
        (await owner.GetAsync(List + "?armedLongerThanDays=7")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The list says which address the request was counted under, so a wrong proxy count shows.
    /// </summary>
    /// <remarks>
    /// The test server has no peer address of its own, so the last entry of the forwarded header
    /// stands in for the proxy that would be the peer in a real deployment — and a request with no
    /// header at all has no address to report.
    /// </remarks>
    [Fact]
    public async Task The_answer_names_the_address_the_request_was_counted_under_after_the_proxies()
    {
        async Task<JsonElement> SeenFromAsync(HttpClient client, string? forwardedFor)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, List + "?pageSize=1");
            if (forwardedFor is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("seenFrom").Clone();
        }

        // Nothing to report is said as nothing, not as a made-up address.
        (await SeenFromAsync(admin, null)).ValueKind.ShouldBe(JsonValueKind.Null);

        // The packaged stack's one proxy: the reader's own address.
        (await SeenFromAsync(admin, "203.0.113.10")).GetString().ShouldBe("203.0.113.10");

        // Two proxies in front and the count left at one: the address shown is the outer proxy's.
        // That is the whole use of the line — the reader does not recognise it as their own.
        (await SeenFromAsync(admin, "203.0.113.10, 10.9.9.9")).GetString().ShouldBe("10.9.9.9");

        // The same request to an application told there are two: the reader's own again.
        var settings = HostSettings();
        settings["Proxy:Hops"] = "2";
        using var behindTwo = new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
        var behindTwoAdmin = await AuthHelper.BearerClientAsync(behindTwo, adminEmail);
        (await SeenFromAsync(behindTwoAdmin, "203.0.113.10, 10.9.9.9")).GetString().ShouldBe("203.0.113.10");

        // Somebody who may not read the list is told nothing about addresses either.
        var asOwner = new HttpRequestMessage(HttpMethod.Get, List);
        asOwner.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        var refused = await owner.SendAsync(asOwner);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("203.0.113.10");
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Live(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string PastList(string token) => $"{Live(token)}/past";

    private static string LiveList(string token) => $"{Live(token)}/live";

    private sealed record Published(
        Guid Trip, string Title, Guid Cave, string CaveName, Guid ShareId, string Token);

    /// <summary>
    /// A trip armed against a model of its own cave, one caver placed, and published — all through
    /// <paramref name="coordinator"/>, so that the instants stamped and the windows applied are
    /// those of the host that client talks to.
    /// </summary>
    private async Task<Published> PublishedTripAsync(HttpClient coordinator, string title)
    {
        var (cave, caveName) = await CaveAsync();
        var model = await ModelAsync(cave);

        var fullTitle = $"{title} {Guid.NewGuid():N}";
        var created = await coordinator.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = fullTitle,
            tripDate = "2026-09-12",
            participants = new[] { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..24] } },
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
                .Select(p => p.CaverId).SingleAsync();
        }

        (await PutConfigAsync(coordinator, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await coordinator.PostAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/tracking/events",
                new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var minted = await coordinator.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return new Published(
            trip, fullTitle, cave, caveName, body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    /// <remarks>
    /// Backdated straight into the column because it is a plain recorded fact with nothing derived
    /// from it, read against the clock on every request — so a row written this way is exactly a
    /// watch that was closed that long ago.
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

    /// <summary>Backdates the moment a running watch was started at. See the test that uses it.</summary>
    private async Task SetArmedAtAsync(Guid trip, DateTimeOffset armedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ArmedAt = armedAt;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<(Guid Id, string Name)> CaveAsync()
    {
        var name = $"Listed Cave {Guid.NewGuid():N}"[..30];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), name);
    }

    /// <summary>A survey model created the real way, then stations seeded into the graph tables.</summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "publinks.3d");
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

    // ---- reading the answers -----------------------------------------------------------------

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(List + query);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static List<JsonElement> Rows(JsonElement answer) =>
        [.. answer.GetProperty("items").EnumerateArray()];

    private static List<Guid> Ids(JsonElement answer) =>
        [.. Rows(answer).Select(r => r.GetProperty("id").GetGuid())];

    private static Dictionary<string, int> Counts(JsonElement answer) =>
        answer.GetProperty("counts").EnumerateArray()
            .ToDictionary(c => c.GetProperty("status").GetString()!, c => c.GetProperty("count").GetInt32());

    private static List<Guid> ListedIds(JsonElement list) =>
        [.. list.GetProperty("trips").EnumerateArray().Select(t => t.GetProperty("tripLogId").GetGuid())];

    private static string? Code(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();
}
