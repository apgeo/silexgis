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
/// The past tracks of one cave, read by somebody holding a published link and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim these exist to hold down is that a published link has two windows.</b> The live
/// one decides whether a party can be followed right now; the past one decides whether trips of
/// the same cave that are already over can be read, and it deliberately outlives the first. So
/// nearly every test here asserts the live route and the past routes about the <em>same token</em>
/// — a past assertion on its own could not tell "the archive outlives the page" apart from "both
/// windows are the same window", which is exactly the thing that was got wrong once already.
/// </para>
/// <para>
/// Every test that asserts something is invisible builds the state that would disclose it and
/// asserts the disclosure in the same test: the absent trip beside the present one, the dead token
/// beside the live one. A negative that passes for any reason is not a protection test — a route
/// that 404s because a token was mistyped proves nothing at all.
/// </para>
/// <para>
/// Protection is switched on through the write service that maintains the derived columns, never
/// by writing <c>location_protected</c> into the row: writing it by hand leaves
/// <c>is_protected_effective</c> stale and makes a test pass whether or not the rule works.
/// </para>
/// </remarks>
public sealed class TripPastTrackTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripPastTrackTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"pasttrack-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(connectionString, HostSettings(), JobWorkers.RemoveFrom);
    }

    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    /// <summary>
    /// A second host over the same database and the same file store, differently configured.
    /// </summary>
    /// <remarks>
    /// Every window here is read where the request is answered and stored nowhere, so this is how
    /// one link gets two answers with the trip, the watch and the publication being the very same
    /// rows — which is the only way to tell a setting being applied from a setting that happens to
    /// agree with the state somebody built.
    /// </remarks>
    private SilexGisApiFactory HostWith(params (string Key, string Value)[] settings)
    {
        var host = HostSettings();
        foreach (var (key, value) in settings) host[key] = value;
        return new SilexGisApiFactory(connectionString, host, JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"past-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"past-own-{suffix}@t.local");
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
    /// <b>The load-bearing one.</b> A link whose live page has ended goes on opening the archive,
    /// and the live route refuses the very same token at the very same moment.
    /// </summary>
    /// <remarks>
    /// This is the whole correction. Gating the archive on the live window would have made a club's
    /// article show its history only while some party of that cave happened to be underground —
    /// almost never — so the two halves are asserted together on one token: the page is over, the
    /// history is readable.
    /// </remarks>
    [Fact]
    public async Task A_link_whose_live_page_has_ended_still_opens_the_past()
    {
        var trip = await PastTripAsync("Evening trip");

        // Window one is shut: the page a follower had is gone, exactly as it was before this
        // feature existed.
        (await anonymous.GetAsync(Live(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Window two is open, and the trip is in its own cave's archive.
        var list = await ListAsync(trip.Token);
        list.GetProperty("more").GetBoolean().ShouldBeFalse();
        var row = list.GetProperty("trips").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("tripLogId").GetGuid().ShouldBe(trip.Trip);
        row.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
        row.GetProperty("participantCount").GetInt32().ShouldBe(1);
        row.GetProperty("playable").GetBoolean().ShouldBeTrue();
        row.GetProperty("closedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var track = await TrackAsync(trip.Token, trip.Trip);
        track.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        track.GetProperty("trackTruncated").GetBoolean().ShouldBeFalse();
        track.GetProperty("model").GetProperty("sourceEpsg").GetInt32().ShouldBe(31700);

        var fix = track.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("track").EnumerateArray().ShouldHaveSingleItem();
        fix.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        fix.GetProperty("in").GetBoolean().ShouldBeTrue();
        fix.GetProperty("out").GetBoolean().ShouldBeFalse();
        fix.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// Outside both windows, and having never existed, are the same answer — byte for byte, on all
    /// three routes.
    /// </summary>
    /// <remarks>
    /// The property the live window was protecting and the one the second window must not cost: a
    /// stranger who finds a token in an archived article learns nothing from trying it. Asserted as
    /// an equality of the whole refusal rather than of the status, because a code or a detail that
    /// differed would say which of the two a caller was holding.
    /// </remarks>
    [Fact]
    public async Task A_revoked_an_invented_and_an_over_long_token_answer_what_an_unknown_one_answers()
    {
        var alive = await PastTripAsync("Still readable");
        var revoked = await PastTripAsync("Withdrawn");
        (await owner.DeleteAsync($"{Shares(revoked.Trip)}/{revoked.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var invented = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('+', '-').Replace('/', '_');
        var overLong = new string('a', 400);

        var baseline = await RefusalShapeAsync(await anonymous.GetAsync(PastList(invented)));
        foreach (var dead in new[] { revoked.Token, overLong, string.Empty })
        {
            (await RefusalShapeAsync(await anonymous.GetAsync(PastList(dead)))).ShouldBe(baseline);
            (await RefusalShapeAsync(await anonymous.GetAsync(PastTrack(dead, revoked.Trip))))
                .ShouldBe(baseline);
        }

        // The same answer the live route gives, so the new routes add no distinguishable refusal
        // to the surface.
        (await RefusalShapeAsync(await anonymous.GetAsync(Live(invented)))).ShouldBe(baseline);

        // And the twin, without which every line above would pass on a server that answered 404 to
        // everything: a usable token opens the archive.
        (await anonymous.GetAsync(PastList(alive.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A trip that is still being followed is not in the archive — including the one whose link has
    /// lapsed while its party are still underground.
    /// </summary>
    /// <remarks>
    /// The second of those is the side door and the reason the "watch is not armed" clause is not
    /// redundant. A link that has run out closes the live page on its own, so without that clause a
    /// party still underground would drop straight into an anonymous archive, readable by anybody
    /// holding a token to any other trip of the same cave.
    /// </remarks>
    [Fact]
    public async Task A_party_still_underground_is_absent_from_the_archive_however_dead_their_link_is()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var reader = await PastTripAsync("History", cave, model);

        var live = await PublishedTripAsync("Underground now", cave, model);
        var lapsed = await PublishedTripAsync("Underground on a dead link", cave, model);
        await ExpireAsync(lapsed.ShareId, DateTimeOffset.UtcNow.AddDays(-1));

        // Their own links say the same thing from both ends: one still opens a live page, the other
        // opens nothing at all. Neither is history.
        (await anonymous.GetAsync(Live(live.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(Live(lapsed.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(PastList(lapsed.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = ListedIds(await ListAsync(reader.Token));
        listed.ShouldContain(reader.Trip);
        listed.ShouldNotContain(live.Trip);
        listed.ShouldNotContain(lapsed.Trip);

        (await anonymous.GetAsync(PastTrack(reader.Token, live.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(PastTrack(reader.Token, lapsed.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A trip nobody published is not in the archive, and revoking every link of one takes it back
    /// out.
    /// </summary>
    /// <remarks>
    /// Publication is the act, read off the link rows that already exist — there is no archive flag
    /// and no second act. Which is why revocation is the withdrawal path: it already means "end
    /// this publication, now", and it is read as meaning exactly that.
    /// </remarks>
    [Fact]
    public async Task A_trip_nobody_published_is_absent_and_revoking_every_link_removes_one()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var reader = await PastTripAsync("The one with a link", cave, model);
        var withdrawn = await PastTripAsync("The one withdrawn", cave, model);

        // A trip of the same cave that was tracked and closed but never published at all.
        var unpublished = await CreateTripAsync("Never published");
        await ArmAsync(unpublished.Trip, model);
        await ReportAsync(unpublished.Trip, unpublished.Cavers[0], "cave.deep.3");
        await CloseAsync(unpublished.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        ListedIds(await ListAsync(reader.Token)).ShouldBe(
            new[] { reader.Trip, withdrawn.Trip }, ignoreOrder: true);
        (await anonymous.GetAsync(PastTrack(reader.Token, unpublished.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.DeleteAsync($"{Shares(withdrawn.Trip)}/{withdrawn.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        ListedIds(await ListAsync(reader.Token)).ShouldBe(new[] { reader.Trip });
        (await anonymous.GetAsync(PastTrack(reader.Token, withdrawn.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A link to one cave reads that cave's past and no other's.
    /// </summary>
    /// <remarks>
    /// The scope check, asserted with its twin so that the 404 is about the cave rather than about
    /// the trip being unreadable for some other reason: the identical trip answers 200 to a link of
    /// its own cave.
    /// </remarks>
    [Fact]
    public async Task A_past_trip_of_another_cave_is_refused()
    {
        var here = await PastTripAsync("This cave");
        var elsewhere = await PastTripAsync("Another cave");

        ListedIds(await ListAsync(here.Token)).ShouldBe(new[] { here.Trip });
        (await anonymous.GetAsync(PastTrack(here.Token, elsewhere.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(PastTrack(elsewhere.Token, elsewhere.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A cave protected after the link was handed out has no archive either, and lifting the
    /// protection gives it back.
    /// </summary>
    /// <remarks>
    /// The refusal is re-decided on every read and remembered nowhere, which is what the third
    /// assertion says: a verdict cached at mint would go on being true after somebody had just
    /// decided the cave needed protecting.
    /// </remarks>
    [Fact]
    public async Task Protection_switched_on_after_the_link_was_minted_closes_the_archive_too()
    {
        var trip = await PastTripAsync("Guarded later");

        (await anonymous.GetAsync(PastList(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetLocationProtectedAsync(trip.Cave, true);
        (await anonymous.GetAsync(PastList(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(PastTrack(trip.Token, trip.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetLocationProtectedAsync(trip.Cave, false);
        (await anonymous.GetAsync(PastTrack(trip.Token, trip.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A caption keeps somebody off the past page exactly as it keeps them off the live one — and
    /// one typed after the trip was published takes effect immediately.
    /// </summary>
    /// <remarks>
    /// The caption is the only individual opt-out there is, now that there is no per-trip archive
    /// act, and this makes it retroactive: nothing is cached and the response is rebuilt on every
    /// read. Both surfaces are asserted about the same person on the same trip, because there is
    /// one resolver and the point is that there is one.
    /// </remarks>
    [Fact]
    public async Task A_caption_typed_after_the_trip_renames_the_party_on_the_past_page()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var trip = await CreateTripAsync("Captioned late", guests: 2);
        await ArmAsync(trip.Trip, model);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");
        var (_, token) = await PublishAsync(trip.Trip);

        // While it is still live, the page names both of them off the roster.
        Labels(await Json(anonymous.GetAsync(Live(token))), "participants")
            .ShouldBe(trip.Names, ignoreOrder: true);

        await CloseAsync(trip.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        Labels(await TrackAsync(token, trip.Trip), "participants")
            .ShouldBe(trip.Names, ignoreOrder: true);

        // Typed after the trip is over and after the link was minted.
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "A caver" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = Labels(await TrackAsync(token, trip.Trip), "participants");
        renamed.ShouldContain("A caver");
        renamed.ShouldNotContain(await RosterNameAsync(trip.Cavers[0]));
        // And the other member of the party is untouched, so the caption renamed one person rather
        // than emptying the page.
        renamed.ShouldContain(await RosterNameAsync(trip.Cavers[1]));
    }

    /// <summary>
    /// An installation that does not publish real names does not publish them here either, and a
    /// caption still wins over the setting.
    /// </summary>
    /// <remarks>
    /// One link, two hosts, the same rows: the only difference between the two responses is the
    /// setting, which is what says the setting is being read rather than the state being different.
    /// </remarks>
    [Fact]
    public async Task An_installation_that_publishes_no_names_publishes_none_on_the_past_page()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var trip = await CreateTripAsync("Nameless", guests: 2);
        await ArmAsync(trip.Trip, model);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");
        var (_, token) = await PublishAsync(trip.Trip);
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[1]}",
            new { label = "Kept" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await CloseAsync(trip.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        Labels(await TrackAsync(token, trip.Trip), "participants")
            .ShouldBe(new List<string?> { await RosterNameAsync(trip.Cavers[0]), "Kept" },
                ignoreOrder: true);

        using var quiet = HostWith(("TripTracking:PublishRealNames", "false"));
        using var quietClient = quiet.CreateClient();
        Labels(await Json(quietClient.GetAsync(PastTrack(token, trip.Trip))), "participants")
            .ShouldBe(new List<string?> { null, "Kept" }, ignoreOrder: true);
    }

    /// <summary>
    /// The party is numbered on the past page exactly as it was numbered on the live one.
    /// </summary>
    /// <remarks>
    /// There is one derivation of a participant's place in the party and both public surfaces call
    /// it. A second copy is how "Caver 3" comes to mean two different people on two pages about the
    /// same trip, with nothing on either page able to say which is meant — so the agreement is
    /// asserted rather than assumed, on a roster deliberately more than one person long.
    /// </remarks>
    [Fact]
    public async Task The_past_page_numbers_the_party_the_way_the_live_page_did()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var trip = await CreateTripAsync("Four of them", guests: 4);
        await ArmAsync(trip.Trip, model);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");

        // Captioned so a row can be named without the response carrying a person: both surfaces are
        // keyed by a place in the party and deliberately carry no caver id, so a caption is the only
        // way a test can say "this row is that person" — and comparing the numbers alone would
        // compare 1,2,3,4 with 1,2,3,4 and prove nothing.
        foreach (var (caver, index) in trip.Cavers.Select((c, i) => (c, i)))
        {
            (await owner.PutAsJsonAsync(
                $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{caver}",
                new { label = $"Member {index}" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var (_, token) = await PublishAsync(trip.Trip);
        var liveOrder = NumberedParty(await Json(anonymous.GetAsync(Live(token))));
        liveOrder.Count.ShouldBe(4);
        liveOrder.ShouldContain("1=Member 0");

        await CloseAsync(trip.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        NumberedParty(await TrackAsync(token, trip.Trip)).ShouldBe(liveOrder);
    }

    /// <summary>
    /// A report whose own cave anchor is gone loses its place on the past track and keeps its hour,
    /// and the response says a place was withheld.
    /// </summary>
    /// <remarks>
    /// The per-row rule, which is not the page-level refusal above it: the trip's own cave is open,
    /// so the archive answers, and the row with nothing left to evaluate protection against is
    /// withheld from everyone. Its twin is the identical response taken before the anchor was
    /// severed.
    /// </remarks>
    [Fact]
    public async Task A_report_whose_anchor_is_gone_is_withheld_on_the_past_track_and_keeps_its_hour()
    {
        var trip = await PastTripAsync("Severed");

        var before = Track(await TrackAsync(trip.Token, trip.Trip)).ShouldHaveSingleItem();
        before.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        (await TrackAsync(trip.Token, trip.Trip)).GetProperty("positionsWithheld").GetBoolean()
            .ShouldBeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TripPositionEvents
                .Where(e => e.TripLogId == trip.Trip)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)null));
        }

        var page = await TrackAsync(trip.Token, trip.Trip);
        page.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        var after = Track(page).ShouldHaveSingleItem();
        after.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("depthM").ValueKind.ShouldBe(JsonValueKind.Null);
        // Never beside a withheld place: a second bit explaining the absence would give back
        // exactly what the withholding kept.
        after.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
        // What survives is that somebody was heard from and when — the thing a playback needs in
        // order not to read as a party who vanished.
        after.GetProperty("recordedAt").ValueKind.ShouldBe(JsonValueKind.String);
        after.GetProperty("in").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// A place measured on a survey that has since been superseded is said to be elsewhere rather
    /// than drawn on the survey handed over — and the survey handed over is the past trip's own.
    /// </summary>
    /// <remarks>
    /// A station path is a name inside one survey, so drawing one survey's path on another is a
    /// confident statement about where somebody was, assembled out of a collision of names. The two
    /// models here spell their stations identically, so the collision is exact.
    /// </remarks>
    [Fact]
    public async Task A_place_measured_on_a_superseded_survey_is_said_to_be_elsewhere()
    {
        var cave = await CaveAsync(locationProtected: false);
        var first = await ModelAsync(cave);
        var trip = await CreateTripAsync("Re-surveyed");
        await ArmAsync(trip.Trip, first);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");
        var (_, token) = await PublishAsync(trip.Trip);

        var corrected = await ModelAsync(cave);
        (await PutConfigAsync(owner, trip.Trip, new { state = "armed", surveyModelId = corrected }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.deep.3");
        await CloseAsync(trip.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        var page = await TrackAsync(token, trip.Trip);
        // The trip's own model, which is the one its watch ended on — not some other trip's.
        page.GetProperty("model").GetProperty("modelUrl").GetString()!
            .ShouldContain((await FileOfModelAsync(corrected)).ToString());

        var fixes = Track(page);
        fixes.Count.ShouldBe(2);
        fixes[0].GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        fixes[0].GetProperty("positionOnOtherModel").GetBoolean().ShouldBeTrue();
        // Nothing here is a withholding, and the flag that means that must not have moved.
        page.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();

        fixes[1].GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        fixes[1].GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A trip whose every place was measured on a survey that was then replaced under it is not
    /// offered as something to play, because its playback draws nothing.
    /// </summary>
    /// <remarks>
    /// The list's "there is something to play" bit has to survive both ways a playback comes back
    /// empty — the place withheld, and the place known but not on the survey handed over. Counting
    /// reports without asking which survey they were measured in promises the second kind, and the
    /// promise is only visible by opening the playback it offered. The drawable trip beside it is
    /// the control: a bit that were always true would otherwise pass this test.
    /// </remarks>
    [Fact]
    public async Task A_trip_whose_places_were_all_measured_on_a_replaced_survey_is_not_playable()
    {
        var cave = await CaveAsync(locationProtected: false);
        var first = await ModelAsync(cave);

        var stale = await CreateTripAsync("Re-pointed then closed", tripDate: "2026-09-05");
        await ArmAsync(stale.Trip, first);
        await ReportAsync(stale.Trip, stale.Cavers[0], "cave.upper.2");
        var corrected = await ModelAsync(cave);
        (await PutConfigAsync(owner, stale.Trip, new { state = "armed", surveyModelId = corrected }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var (_, staleToken) = await PublishAsync(stale.Trip);
        await CloseAsync(stale.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        var drawn = await PastTripAsync("Drawn on its own survey", cave, corrected);

        var offered = (await ListAsync(staleToken)).GetProperty("trips").EnumerateArray()
            .ToDictionary(
                t => t.GetProperty("tripLogId").GetGuid(),
                t => t.GetProperty("playable").GetBoolean());
        offered[drawn.Trip].ShouldBeTrue();
        offered[stale.Trip].ShouldBeFalse();

        // The playback the list declined to promise, opened: known, and nowhere it can be drawn.
        var only = Track(await TrackAsync(staleToken, stale.Trip)).ShouldHaveSingleItem();
        only.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        only.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// An installation that has switched the archive off answers what an unknown token answers,
    /// and the same link on the default host opens it.
    /// </summary>
    [Fact]
    public async Task The_archive_switched_off_answers_what_an_unknown_token_answers()
    {
        var trip = await PastTripAsync("Switched off");
        (await anonymous.GetAsync(PastList(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var off = HostWith(("TripPastTracks:Enabled", "false"));
        using var client = off.CreateClient();
        var invented = await RefusalShapeAsync(await client.GetAsync(PastList("nothing-at-all")));
        (await RefusalShapeAsync(await client.GetAsync(PastList(trip.Token)))).ShouldBe(invented);
        (await RefusalShapeAsync(await client.GetAsync(PastTrack(trip.Token, trip.Trip))))
            .ShouldBe(invented);
    }

    /// <summary>
    /// Retention ends the archive, counted from the end of the trip, and the same link inside a
    /// longer window still opens it.
    /// </summary>
    /// <remarks>
    /// The default is no limit, so the pair here is two hosts over one set of rows — which also
    /// proves the setting is bound at all. A settings class nobody binds ignores the operator
    /// silently, and the only way to see that is to change one and watch the answer change.
    /// </remarks>
    [Fact]
    public async Task Retention_ends_the_archive_and_a_longer_window_keeps_it()
    {
        var trip = await PastTripAsync("Aged out");

        using var brief = HostWith(("TripPastTracks:Retention", "00:00:01"));
        using var briefClient = brief.CreateClient();
        (await briefClient.GetAsync(PastList(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await briefClient.GetAsync(PastTrack(trip.Token, trip.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var long_ = HostWith(("TripPastTracks:Retention", "3650.00:00:00"));
        using var longClient = long_.CreateClient();
        (await longClient.GetAsync(PastList(trip.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await longClient.GetAsync(PastTrack(trip.Token, trip.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The list is bounded, says that something older exists, and does not say how much.
    /// </summary>
    /// <remarks>
    /// A count of past trips is a disclosure that a bit saying "there is more" is not — "this club
    /// has been into this cave 412 times" is a fact about the club. The bound is read from the same
    /// end as the list, so what a long register loses is its oldest rows on both.
    /// </remarks>
    [Fact]
    public async Task The_list_is_bounded_and_says_only_that_something_older_exists()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var newer = await PastTripAsync("Newer", cave, model, tripDate: "2026-09-12");
        var older = await PastTripAsync("Older", cave, model, tripDate: "2026-09-05");

        var whole = await ListAsync(newer.Token);
        whole.GetProperty("more").GetBoolean().ShouldBeFalse();
        // Newest first, which is the order a visitor reads a club's recent history in.
        ListedIds(whole).ShouldBe(new[] { newer.Trip, older.Trip });
        whole.EnumerateObject().Select(p => p.Name).ShouldNotContain("total");

        using var small = HostWith(("TripPastTracks:ListSize", "1"));
        using var smallClient = small.CreateClient();
        var page = await Json(smallClient.GetAsync(PastList(newer.Token)));
        page.GetProperty("more").GetBoolean().ShouldBeTrue();
        ListedIds(page).ShouldBe(new[] { newer.Trip });
    }

    /// <summary>
    /// A note about somebody is not on the past track, and it moves nobody between standings.
    /// </summary>
    /// <remarks>
    /// A note is a coordinator's free text about a person during a callout and has never been on a
    /// public surface. Its twin is the exit beside it: one report is emitted and the other is not,
    /// from one log, so the absence is the note being dropped rather than the track being empty.
    /// </remarks>
    [Fact]
    public async Task A_note_is_not_on_the_past_track_and_moves_nobody()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var trip = await CreateTripAsync("With a note");
        await ArmAsync(trip.Trip, model);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");
        await EventAsync(trip.Trip, new
        {
            caverIds = new[] { trip.Cavers[0] },
            kind = "note",
            note = "radio check at the pitch head",
        });
        await EventAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[0] }, kind = "exited" });
        var (_, token) = await PublishAsync(trip.Trip);
        await CloseAsync(trip.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        var page = await TrackAsync(token, trip.Trip);
        page.GetRawText().ShouldNotContain("radio check");

        var fixes = Track(page);
        fixes.Count.ShouldBe(2);
        fixes[0].GetProperty("in").GetBoolean().ShouldBeTrue();
        fixes[1].GetProperty("out").GetBoolean().ShouldBeTrue();
        fixes[1].GetProperty("in").GetBoolean().ShouldBeFalse();
    }

    // ---- plumbing ----------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Live(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string PastList(string token) => $"{Live(token)}/past";

    private static string PastTrack(string token, Guid tripLogId) => $"{Live(token)}/past/{tripLogId}";

    private sealed record PastTrip(Guid Trip, Guid Cave, Guid Model, Guid ShareId, string Token);

    private sealed record NewTrip(Guid Trip, List<Guid> Cavers, List<string> Names);

    /// <summary>
    /// A trip that is over: armed against a model of its cave, one caver placed, published, and its
    /// watch closed long enough ago that the live window has run out.
    /// </summary>
    /// <remarks>
    /// The close instant is backdated straight into the column because it is a plain recorded fact
    /// with nothing derived from it, and it is read against the clock on every request — so a row
    /// written this way is exactly a watch that was closed that long ago. Protection is never
    /// written this way, for the opposite reason.
    /// </remarks>
    private async Task<PastTrip> PastTripAsync(
        string title, Guid? cave = null, Guid? model = null, string tripDate = "2026-09-12")
    {
        var published = await PublishedTripAsync(title, cave, model, tripDate);
        await CloseAsync(published.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        return published;
    }

    /// <summary>A trip armed, placed and published, with its watch still running.</summary>
    private async Task<PastTrip> PublishedTripAsync(
        string title, Guid? cave = null, Guid? model = null, string tripDate = "2026-09-12")
    {
        var caveId = cave ?? await CaveAsync(locationProtected: false);
        var modelId = model ?? await ModelAsync(caveId);
        var trip = await CreateTripAsync(title, tripDate: tripDate);
        await ArmAsync(trip.Trip, modelId);
        await ReportAsync(trip.Trip, trip.Cavers[0], "cave.upper.2");
        var (shareId, token) = await PublishAsync(trip.Trip);
        return new PastTrip(trip.Trip, caveId, modelId, shareId, token);
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
        return new NewTrip(trip, cavers, invented);
    }

    /// <summary>
    /// The roster's own name for one caver, read off the row.
    /// </summary>
    /// <remarks>
    /// Read rather than taken from the list of invented names beside it: that list is in the order
    /// the guests were written and the caver ids come back sorted, so pairing them by position
    /// would be right about half the time and wrong silently the other half.
    /// </remarks>
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
        (await PutConfigAsync(owner, trip, new { state = "closed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    /// <summary>Backdates one link's own end — the part of the window no caller can reach.</summary>
    private async Task ExpireAsync(Guid shareId, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var share = await db.TripTrackingShares.SingleAsync(s => s.Id == shareId);
        share.ExpiresAt = at;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<Guid> FileOfModelAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking().Where(m => m.Id == modelId)
            .Select(m => m.FileId).SingleAsync();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Past Cave {Guid.NewGuid():N}"[..30],
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
    /// superseded-survey rule exists for.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "pasttrack.3d");
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

    private Task<JsonElement> ListAsync(string token) => Json(anonymous.GetAsync(PastList(token)));

    private Task<JsonElement> TrackAsync(string token, Guid tripLogId) =>
        Json(anonymous.GetAsync(PastTrack(token, tripLogId)));

    private static async Task<JsonElement> Json(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static List<Guid> ListedIds(JsonElement list) =>
        [.. list.GetProperty("trips").EnumerateArray().Select(t => t.GetProperty("tripLogId").GetGuid())];

    private static List<JsonElement> Track(JsonElement page) =>
        [.. page.GetProperty("participants").EnumerateArray()
            .SelectMany(p => p.GetProperty("track").EnumerateArray())];

    private static List<string?> Labels(JsonElement envelope, string property) =>
        [.. envelope.GetProperty(property).EnumerateArray()
            .Select(p => p.GetProperty("label").ValueKind == JsonValueKind.Null
                ? null
                : p.GetProperty("label").GetString())];

    /// <summary>Who this page calls the party, against the number it gives each of them.</summary>
    private static List<string> NumberedParty(JsonElement envelope) =>
        [.. envelope.GetProperty("participants").EnumerateArray()
            .Select(p => $"{p.GetProperty("ordinal").GetInt32()}={p.GetProperty("label").GetString()}")];

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
