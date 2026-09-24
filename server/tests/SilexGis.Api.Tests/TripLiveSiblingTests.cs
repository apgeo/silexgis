// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The parties in one cave right now, read by somebody holding one published link and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> A share token names exactly one trip, so before this route a club
/// running two parties into one cave could publish both and show neither beside the other. Nearly
/// every test here therefore asserts something about a trip the token was <em>not</em> minted for —
/// a test that only ever saw the token's own trip could not tell this route from the followed one.
/// </para>
/// <para>
/// Every test that asserts something is invisible builds the state that would disclose it and
/// asserts the disclosure in the same test: the absent trip beside the present one, the dead token
/// beside the live one. A 404 proves nothing on its own — a route that refuses a mistyped token
/// refuses everything.
/// </para>
/// <para>
/// Protection is switched on through the write service that maintains the derived columns, never by
/// writing <c>location_protected</c> into the row: written by hand it leaves
/// <c>is_protected_effective</c> stale, and the test then passes whether or not the rule works.
/// </para>
/// </remarks>
public sealed class TripLiveSiblingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripLiveSiblingTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"livesib-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(connectionString, HostSettings(), JobWorkers.RemoveFrom);
    }

    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    /// <summary>
    /// A second host over the same database and file store, differently configured — how one link
    /// gets two answers with the trip, the watch and the publication being the very same rows.
    /// </summary>
    private SilexGisApiFactory HostWith(params (string Key, string Value)[] settings)
    {
        var host = HostSettings();
        foreach (var (key, value) in settings) host[key] = value;
        return new SilexGisApiFactory(connectionString, host, JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sib-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sib-own-{suffix}@t.local");
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

    /// <summary>
    /// <b>The load-bearing one.</b> One link shows both parties underground in the same cave, with
    /// each party's own positions — which is the thing that was impossible before.
    /// </summary>
    [Fact]
    public async Task One_link_shows_every_party_in_the_cave_including_one_it_was_not_minted_for()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);

        var mine = await PublishedTripAsync("Mine", cave, model, station: "cave.upper.2");
        var theirs = await PublishedTripAsync("Theirs", cave, model, station: "cave.deep.3");

        var list = await LiveListAsync(mine.Token);

        // Both trips, and the other party's position with them — the whole point.
        ListedIds(list).ShouldBe([mine.Trip, theirs.Trip], ignoreOrder: true);
        StationOf(list, theirs.Trip, ordinal: 1).ShouldBe("cave.deep.3");
        StationOf(list, mine.Trip, ordinal: 1).ShouldBe("cave.upper.2");

        // Asserted beside it, because it is what makes the list necessary: the followed route
        // answers for one trip only, and the archive deliberately refuses a trip still being
        // followed. Without this pair, the list above could be the followed route under a new name.
        var followed = await Json(anonymous.GetAsync(Follow(mine.Token)));
        followed.GetProperty("tripLogId").GetGuid().ShouldBe(mine.Trip);
        ListedIds(await Json(anonymous.GetAsync(PastList(mine.Token)))).ShouldBeEmpty();
    }

    /// <summary>
    /// The identity a page matches rows by, proved across the handover rather than asserted once:
    /// the id the followed envelope reports is the id this trip carries in the live list, and the
    /// same id it carries in the archive after its watch closes and the grace runs out.
    /// </summary>
    [Fact]
    public async Task One_trip_keeps_one_identity_from_the_followed_page_to_the_live_list_to_the_archive()
    {
        var published = await PublishedTripAsync("Identity");

        var followed = await Json(anonymous.GetAsync(Follow(published.Token)));
        var reported = followed.GetProperty("tripLogId").GetGuid();
        reported.ShouldBe(published.Trip);
        ListedIds(await LiveListAsync(published.Token)).ShouldBe([reported]);

        // Now it is over: out of the live list, into the archive, same id.
        await CloseAsync(published.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        ListedIds(await LiveListAsync(published.Token)).ShouldBeEmpty();
        ListedIds(await Json(anonymous.GetAsync(PastList(published.Token)))).ShouldBe([reported]);

        // And the playback answers to it, which is what a page follows the id to.
        var track = await Json(anonymous.GetAsync($"{PastList(published.Token)}/{reported}"));
        track.GetProperty("tripLogId").GetGuid().ShouldBe(reported);
    }

    /// <summary>
    /// The grace window after a watch closes belongs to this list, not the archive — and the trip
    /// says which case it is, so a page never draws somebody as underground who came out.
    /// </summary>
    [Fact]
    public async Task A_watch_closed_moments_ago_is_still_here_saying_it_is_closed_and_is_not_yet_archived()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var armed = await PublishedTripAsync("Still in", cave, model);
        var justOut = await PublishedTripAsync("Just out", cave, model);
        await CloseAsync(justOut.Trip, DateTimeOffset.UtcNow.AddMinutes(-5));

        var list = await LiveListAsync(armed.Token);

        // Both present, and distinguishable — the state is what a page must read.
        StateOf(list, armed.Trip).ShouldBe("armed");
        StateOf(list, justOut.Trip).ShouldBe("closed");

        // The archive does not have it yet. Both halves matter: this is the boundary the two
        // windows share, and asserting only one side could not tell a handover from an overlap.
        ListedIds(await Json(anonymous.GetAsync(PastList(armed.Token)))).ShouldBeEmpty();

        // Past the grace it crosses over, and is gone from here.
        await CloseAsync(justOut.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        ListedIds(await LiveListAsync(armed.Token)).ShouldBe([armed.Trip]);
        ListedIds(await Json(anonymous.GetAsync(PastList(armed.Token)))).ShouldBe([justOut.Trip]);
    }

    /// <summary>
    /// <b>The design's core claim.</b> Every party in this list is placed against the survey the
    /// token's own page hands over. A trip measured on another survey of the same cave is reported
    /// as placed and not drawn — never given a station name from one survey to be drawn on another.
    /// </summary>
    [Fact]
    public async Task A_party_measured_on_another_survey_is_said_to_be_elsewhere_rather_than_misplaced()
    {
        var cave = await CaveAsync(locationProtected: false);
        var mineModel = await ModelAsync(cave);
        // A second model of the same cave whose stations are spelled identically, which is exactly
        // the collision this rule exists for: the name would resolve, and to the wrong place.
        var otherModel = await ModelAsync(cave);

        var mine = await PublishedTripAsync("Mine", cave, mineModel, station: "cave.upper.2");
        var theirs = await PublishedTripAsync("Theirs", cave, otherModel, station: "cave.deep.3");

        var list = await LiveListAsync(mine.Token);
        var them = TripIn(list, theirs.Trip);
        var participant = them.GetProperty("participants").EnumerateArray().Single();

        participant.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        participant.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeTrue();
        // Still underground: the point of the flag is that "known, not drawable here" is not the
        // same as "nobody has said where they are".
        participant.GetProperty("in").GetBoolean().ShouldBeTrue();
        // And not confused with a withholding, which is a different absence with a different cause.
        them.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();

        // Read through the other trip's own link the very same report draws, which is what proves
        // the difference is the survey being handed over rather than anything about the report.
        var throughTheirs = await LiveListAsync(theirs.Token);
        StationOf(throughTheirs, theirs.Trip, ordinal: 1).ShouldBe("cave.deep.3");
    }

    [Fact]
    public async Task A_party_in_another_cave_is_absent_however_published_it_is()
    {
        var here = await PublishedTripAsync("Here");
        var elsewhere = await PublishedTripAsync("Elsewhere");

        // Both are live and published; they differ only in the cave. Asserted from both links, so
        // this cannot pass by one of them being broken.
        ListedIds(await LiveListAsync(here.Token)).ShouldBe([here.Trip]);
        ListedIds(await LiveListAsync(elsewhere.Token)).ShouldBe([elsewhere.Trip]);
    }

    [Fact]
    public async Task A_trip_nobody_published_is_absent_and_withdrawing_every_link_removes_one()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var published = await PublishedTripAsync("Published", cave, model);

        // Armed and placed in the same cave, never published: a page must not learn of it.
        var unpublished = await CreateTripAsync("Unpublished");
        await ArmAsync(unpublished.Trip, model);
        await ReportAsync(unpublished.Trip, unpublished.Cavers[0], "cave.upper.2");

        var second = await PublishedTripAsync("Withdrawn", cave, model);

        ListedIds(await LiveListAsync(published.Token))
            .ShouldBe([published.Trip, second.Trip], ignoreOrder: true);

        // Withdrawing the only link unpublishes the trip, and it leaves — while the trip, the watch
        // and the party all stay exactly as they were.
        (await owner.DeleteAsync($"{Shares(second.Trip)}/{second.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        ListedIds(await LiveListAsync(published.Token)).ShouldBe([published.Trip]);
    }

    [Fact]
    public async Task A_watch_that_was_never_armed_is_absent_even_with_a_link_to_it()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var armed = await PublishedTripAsync("Armed", cave, model);

        // Publishing refuses an unarmed watch, and the state machine refuses armed -> off outright
        // (409), so a trip holding a standing link and an Off watch cannot be built through the API
        // at all. It is written directly, because that is the only way this row exists — and because
        // the state is a plain recorded fact with nothing derived from it, unlike protection, which
        // is never written this way. What is being tested is that the rule refuses it: the SQL
        // narrowing above it is an optimisation, and a test that only exercised the narrowing would
        // pass with the rule deleted.
        var standDown = await PublishedTripAsync("Stood down", cave, model);
        await SetStateAsync(standDown.Trip, TripTrackingState.Off);

        ListedIds(await LiveListAsync(armed.Token)).ShouldBe([armed.Trip]);
    }

    [Fact]
    public async Task Protection_switched_on_after_the_link_was_minted_closes_this_list_too()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var published = await PublishedTripAsync("Guarded later", cave, model);

        // Open first, so the closure below is a change of answer rather than a route that never
        // worked.
        ListedIds(await LiveListAsync(published.Token)).ShouldBe([published.Trip]);

        await SetLocationProtectedAsync(cave, true);

        (await anonymous.GetAsync(LiveList(published.Token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_revoked_an_invented_and_an_over_long_token_answer_what_an_unknown_one_answers()
    {
        var published = await PublishedTripAsync("Refusals");
        ListedIds(await LiveListAsync(published.Token)).ShouldBe([published.Trip]);

        (await owner.DeleteAsync($"{Shares(published.Trip)}/{published.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var revoked = await RefusalShapeAsync(await anonymous.GetAsync(LiveList(published.Token)));
        var invented = await RefusalShapeAsync(await anonymous.GetAsync(LiveList("not-a-token-at-all")));
        var overLong = await RefusalShapeAsync(await anonymous.GetAsync(LiveList(new string('t', 600))));

        // One answer, with no branch a caller can read: anything else would tell somebody holding a
        // guess that their guess was once real.
        revoked.ShouldBe(invented);
        overLong.ShouldBe(invented);
        invented.ShouldStartWith("404");
    }

    /// <summary>
    /// The party is named by the one resolver both published surfaces use, so somebody kept off the
    /// followed page by a caption is kept off this list — including in a trip the token was not
    /// minted for, which is the case a second copy of the rule would get wrong.
    /// </summary>
    [Fact]
    public async Task A_caption_on_another_trips_participant_is_what_this_list_calls_them()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var mine = await PublishedTripAsync("Mine", cave, model);

        var theirs = await CreateTripAsync("Theirs");
        await ArmAsync(theirs.Trip, model);
        await ReportAsync(theirs.Trip, theirs.Cavers[0], "cave.upper.2");
        await PublishAsync(theirs.Trip);

        var rosterName = await RosterNameAsync(theirs.Cavers[0]);
        LabelsOf(await LiveListAsync(mine.Token), theirs.Trip).ShouldBe([rosterName]);

        (await owner.PutAsJsonAsync(
                $"/api/v1/trip-logs/{theirs.Trip}/tracking/participants/{theirs.Cavers[0]}",
                new { label = "a third of the party" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        LabelsOf(await LiveListAsync(mine.Token), theirs.Trip).ShouldBe(["a third of the party"]);
    }

    /// <summary>
    /// An installation that publishes no names publishes none here either — asked of a trip the
    /// token was not minted for, and asked of the very same rows through a differently configured
    /// host, so this cannot pass by the roster having been empty.
    /// </summary>
    [Fact]
    public async Task An_installation_that_publishes_no_names_publishes_none_on_this_list()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var mine = await PublishedTripAsync("Mine", cave, model);
        var theirs = await PublishedTripAsync("Theirs", cave, model);

        LabelsOf(await LiveListAsync(mine.Token), theirs.Trip)
            .ShouldBe([await RosterNameAsync(await FirstCaverAsync(theirs.Trip))]);

        using var quiet = HostWith(("TripTracking:PublishRealNames", "false"));
        using var quietAnonymous = quiet.CreateClient();
        var list = await Json(quietAnonymous.GetAsync(LiveList(mine.Token)));
        LabelsOf(list, theirs.Trip).ShouldBe([null]);
        // Their place in the party is still there — it is what the page calls them instead.
        TripIn(list, theirs.Trip).GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("ordinal").GetInt32()).ShouldBe([1]);
    }

    /// <summary>
    /// The list is bounded, and says only that there is more — never how much, because how busy a
    /// club is, is itself a disclosure.
    /// </summary>
    [Fact]
    public async Task The_list_is_bounded_and_says_only_that_there_is_more()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var first = await PublishedTripAsync("One", cave, model);
        await PublishedTripAsync("Two", cave, model);
        await PublishedTripAsync("Three", cave, model);

        var whole = await LiveListAsync(first.Token);
        ListedIds(whole).Count.ShouldBe(3);
        whole.GetProperty("more").GetBoolean().ShouldBeFalse();

        using var narrow = HostWith(("TripTracking:FollowedListSize", "2"));
        using var narrowAnonymous = narrow.CreateClient();
        var bounded = await Json(narrowAnonymous.GetAsync(LiveList(first.Token)));
        ListedIds(bounded).Count.ShouldBe(2);
        bounded.GetProperty("more").GetBoolean().ShouldBeTrue();
        // A count would be the disclosure the bit avoids.
        bounded.TryGetProperty("total", out _).ShouldBeFalse();
    }

    /// <summary>
    /// The consequence of sharing the archive's gate, asserted rather than left in a comment: a link
    /// whose own trip is over keeps reporting who is in the cave now — and the archive's retention
    /// is the one setting that closes both.
    /// </summary>
    [Fact]
    public async Task A_link_whose_own_trip_is_over_still_reports_the_parties_now_until_retention_ends_it()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var old = await PublishedTripAsync("Mine, over", cave, model, tripDate: "2026-01-04");
        await CloseAsync(old.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var now = await PublishedTripAsync("Theirs, underground", cave, model);

        // The old link's own page is finished, and it still names today's party. This is the
        // behaviour the journal page needs and the widening that has to be bounded.
        (await anonymous.GetAsync(Follow(old.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ListedIds(await LiveListAsync(old.Token)).ShouldBe([now.Trip]);

        // Retention shuts it, because the gate is the archive's gate.
        using var bounded = HostWith(("TripPastTracks:Retention", "30.00:00:00"));
        using var boundedAnonymous = bounded.CreateClient();
        (await boundedAnonymous.GetAsync(LiveList(old.Token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // And the link of a trip still being followed is unaffected by that setting.
        (await boundedAnonymous.GetAsync(LiveList(now.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_archive_switched_off_closes_this_list_for_a_link_whose_own_trip_is_over()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var old = await PublishedTripAsync("Mine, over", cave, model);
        await CloseAsync(old.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var now = await PublishedTripAsync("Theirs, underground", cave, model);

        using var off = HostWith(("TripPastTracks:Enabled", "false"));
        using var offAnonymous = off.CreateClient();

        (await offAnonymous.GetAsync(LiveList(old.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // A link whose own watch is running is not reaching through the archive's gate at all.
        (await offAnonymous.GetAsync(LiveList(now.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A report anchored to a cave that may not be published is withheld here exactly as it is on
    /// the followed page — the per-row rule, asked of a trip the token was not minted for.
    /// </summary>
    [Fact]
    public async Task A_report_anchored_to_a_guarded_cave_is_withheld_on_another_trips_row()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var mine = await PublishedTripAsync("Mine", cave, model);
        var theirs = await PublishedTripAsync("Theirs", cave, model, station: "cave.deep.3");

        StationOf(await LiveListAsync(mine.Token), theirs.Trip, ordinal: 1).ShouldBe("cave.deep.3");

        // Re-anchor their report to a guarded cave. The trip's own publication is decided about the
        // watch's cave, which is untouched; this is the narrower per-row question.
        var guarded = await CaveAsync(locationProtected: true);
        await ReanchorReportsAsync(theirs.Trip, guarded);

        var list = await LiveListAsync(mine.Token);
        var them = TripIn(list, theirs.Trip);
        var participant = them.GetProperty("participants").EnumerateArray().Single();
        participant.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        them.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        // Never both: a second bit explaining the absence would give back what the withholding kept.
        participant.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
    }

    // ---- addresses ---------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Follow(string token) =>
        $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string LiveList(string token) => $"{Follow(token)}/live";

    private static string PastList(string token) => $"{Follow(token)}/past";

    // ---- seeding -----------------------------------------------------------------------------

    private sealed record Published(Guid Trip, Guid Cave, Guid Model, Guid ShareId, string Token);

    private sealed record NewTrip(Guid Trip, List<Guid> Cavers);

    /// <summary>A trip armed on a model of its cave, one caver placed, and published.</summary>
    private async Task<Published> PublishedTripAsync(
        string title, Guid? cave = null, Guid? model = null, string tripDate = "2026-09-12",
        string station = "cave.upper.2")
    {
        var caveId = cave ?? await CaveAsync(locationProtected: false);
        var modelId = model ?? await ModelAsync(caveId);
        var trip = await CreateTripAsync(title, tripDate: tripDate);
        await ArmAsync(trip.Trip, modelId);
        await ReportAsync(trip.Trip, trip.Cavers[0], station);
        var (shareId, token) = await PublishAsync(trip.Trip);
        return new Published(trip.Trip, caveId, modelId, shareId, token);
    }

    private async Task<NewTrip> CreateTripAsync(
        string title, int guests = 1, string tripDate = "2026-09-12")
    {
        var invented = Enumerable.Range(1, guests)
            .Select(i => $"Guest {i} {Guid.NewGuid():N}"[..24])
            .ToList();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate,
            participants = invented.Select(name => new { newCaverName = name }).ToArray(),
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        cavers.Count.ShouldBe(guests);
        return new NewTrip(trip, cavers);
    }

    private async Task<Guid> FirstCaverAsync(Guid trip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking().Where(p => p.TripLogId == trip)
            .OrderBy(p => p.Id).Select(p => p.CaverId).FirstAsync();
    }

    private async Task<string> RosterNameAsync(Guid caver)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.AsNoTracking().Where(c => c.Id == caver)
            .Select(c => c.FullName).SingleAsync();
    }

    private async Task ArmAsync(Guid trip, Guid model) =>
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    private Task ReportAsync(Guid trip, Guid caver, string station) =>
        EventAsync(trip, new { caverIds = new[] { caver }, kind = "atStation", stationName = station });

    private async Task EventAsync(Guid trip, object body) =>
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", body))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    private async Task<(Guid Id, string Token)> PublishAsync(Guid trip)
    {
        var minted = await owner.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        var current = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = JsonDocument.Parse(await current.Content.ReadAsStringAsync())
            .RootElement.GetProperty("state").GetString();
        if (state != "closed")
        {
            (await PutConfigAsync(owner, trip, new { state = "closed" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Points every one of a trip's reports at another cave — the per-row anchor, which is a
    /// different question from the watch's own cave and is what the withholding rule asks about.
    /// </summary>
    private async Task ReanchorReportsAsync(Guid trip, Guid caveFeatureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TripPositionEvents.Where(e => e.TripLogId == trip)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, caveFeatureId));
    }

    /// <summary>
    /// Writes a watch's state directly, for the one state the API will not move a published watch
    /// into. A plain recorded fact with nothing derived from it — the same justification the
    /// backdated close instant carries, and the opposite of protection, which must go through the
    /// service that maintains its derived columns.
    /// </summary>
    private async Task SetStateAsync(Guid trip, TripTrackingState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.State = state;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Sib Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey model created the real way, then stations seeded straight into the graph tables.
    /// Two models of one cave spell their stations identically, which is the collision the
    /// other-survey rule exists for.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "livesib.3d");
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
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
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

    // ---- reading the responses ---------------------------------------------------------------

    private Task<JsonElement> LiveListAsync(string token) => Json(anonymous.GetAsync(LiveList(token)));

    private static async Task<JsonElement> Json(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static List<Guid> ListedIds(JsonElement list) =>
        [.. list.GetProperty("trips").EnumerateArray().Select(t => t.GetProperty("tripLogId").GetGuid())];

    private static JsonElement TripIn(JsonElement list, Guid tripLogId) =>
        list.GetProperty("trips").EnumerateArray()
            .Single(t => t.GetProperty("tripLogId").GetGuid() == tripLogId);

    private static string? StateOf(JsonElement list, Guid tripLogId) =>
        TripIn(list, tripLogId).GetProperty("state").GetString();

    private static string? StationOf(JsonElement list, Guid tripLogId, int ordinal)
    {
        var participant = TripIn(list, tripLogId).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("ordinal").GetInt32() == ordinal);
        var station = participant.GetProperty("stationName");
        return station.ValueKind == JsonValueKind.Null ? null : station.GetString();
    }

    private static List<string?> LabelsOf(JsonElement list, Guid tripLogId) =>
        [.. TripIn(list, tripLogId).GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("label").ValueKind == JsonValueKind.Null
                ? null
                : p.GetProperty("label").GetString())];

    /// <summary>
    /// A refusal as a caller can see it, with the trace identifier left out: that one differs per
    /// request by design, while every other member has to be the same whichever way the token was
    /// wrong.
    /// </summary>
    private static async Task<string> RefusalShapeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var members = body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");
        return $"{(int)response.StatusCode} {string.Join('&', members)}";
    }
}
