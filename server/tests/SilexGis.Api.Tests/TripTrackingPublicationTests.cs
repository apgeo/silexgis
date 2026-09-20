// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Publishing a tracked trip: a link an administrator hands out, and the page somebody with no
/// account follows the party on.
/// </summary>
/// <remarks>
/// <para>
/// Every test that asserts something is invisible builds the state that would disclose it and
/// asserts the disclosure in the same test — the refused publication beside the granted one, the
/// dead token beside the live one, the anonymous envelope beside the signed-in read of the same
/// trip. A negative that passes for any reason is not a protection test: a page that 404s because
/// the token was typed wrongly proves nothing about protection, and only its twin says which of
/// the two happened.
/// </para>
/// <para>
/// Protection is switched on here through the write service that maintains the derived columns,
/// never by writing <c>location_protected</c> straight into the row. Writing the column by hand
/// leaves <c>is_protected_effective</c> stale, and a test doing that would pass whether or not
/// the rule under test works.
/// </para>
/// </remarks>
public sealed class TripTrackingPublicationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    /// <summary>Putting a photograph in the public gallery is an administrator's act and nobody else's.</summary>
    private HttpClient publisher = null!;
    private string ownerEmail = null!;
    private long caveTypeId;

    public TripTrackingPublicationTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"trkpub-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            connectionString,
            HostSettings(),
            // Workers off: the graph-extraction job would otherwise pick up the fake survey file
            // below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    /// <summary>
    /// What every host this class builds is configured with. Shared so a second host reads the
    /// same file store and signs delivery URLs with the same keys as the first.
    /// </summary>
    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    /// <summary>
    /// A second host over the same database, run by an operator who has turned published names off.
    /// </summary>
    /// <remarks>
    /// The setting is read where the envelope is built rather than stored anywhere, so this is how
    /// a test compares the two answers to <em>one</em> published link: the trip, the share and the
    /// reports are the same rows, and the only difference between the two envelopes is the setting.
    /// </remarks>
    private SilexGisApiFactory NamesOffFactory()
    {
        var settings = HostSettings();
        settings["TripTracking:PublishRealNames"] = "false";
        return new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
    }

    /// <summary>What a followed page calls each member of the party, in the envelope's order.</summary>
    private static List<string?> Labels(JsonElement envelope) =>
        [.. envelope.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("label").ValueKind == JsonValueKind.Null
                ? null
                : p.GetProperty("label").GetString())];

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"pub-own-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pub-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pub-read-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pub-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pub-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"pub-read-{suffix}@t.local");
        publisher = await AuthHelper.BearerClientAsync(factory, $"pub-adm-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The load-bearing one. A trip in a protected cave cannot be published at all — not
    /// blurred, not partial — and the identical trip in an open cave publishes and hands a
    /// follower the party, the stations and the survey model to draw them in. Without the second
    /// half the first proves only that minting can fail.
    /// </summary>
    [Fact]
    public async Task A_protected_caves_trip_is_refused_publication_and_an_open_ones_is_followed()
    {
        var guarded = await TrackedTripAsync("Guarded", locationProtected: true);
        var refused = await owner.PostAsync(Shares(guarded.Trip), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.publication_refused_protected");
        // Nothing was published, so nothing is listed — a refusal that had written a row would
        // leave a link somebody could later revoke and believe had been live.
        (await BodyAsync(await owner.GetAsync(Shares(guarded.Trip)))).GetArrayLength().ShouldBe(0);

        var open = await TrackedTripAsync("Open", locationProtected: false);
        var (_, token) = await PublishAsync(open.Trip);

        // The token is in the mint response and nowhere else — the managing list carries
        // metadata only, because the plaintext is not stored to be listed.
        var listed = await owner.GetAsync(Shares(open.Trip));
        var listedRaw = await listed.Content.ReadAsStringAsync();
        listedRaw.ShouldNotContain(token);
        JsonDocument.Parse(listedRaw).RootElement.GetArrayLength().ShouldBe(1);

        var page = await FollowAsync(token);
        page.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        page.GetProperty("tripDate").GetString().ShouldBe("2026-09-12");
        page.GetProperty("state").GetString().ShouldBe("armed");
        page.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        var follower = page.GetProperty("participants").EnumerateArray().Single();
        follower.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        follower.GetProperty("in").GetBoolean().ShouldBeTrue();
        follower.GetProperty("out").GetBoolean().ShouldBeFalse();
        follower.GetProperty("lastRecordedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // The model travels as a short-lived signed URL the viewer can fetch with no account,
        // and the coordinate system travels as a string beside it rather than as a second
        // anonymous route to go and ask.
        var model = page.GetProperty("model");
        model.GetProperty("sourceEpsg").GetInt32().ShouldBe(31700);
        model.GetProperty("proj4").GetString().ShouldNotBeNullOrWhiteSpace();
        var modelUrl = model.GetProperty("modelUrl").GetString()!;
        modelUrl.ShouldContain("token=");
        var delivered = await anonymous.GetAsync(modelUrl);
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await delivered.Content.ReadAsByteArrayAsync()).ShouldBe(new byte[] { 1, 2, 3, 4 });
    }

    /// <summary>
    /// Publishing hands over the cave's survey drawing and its station names, so it takes the
    /// right to share that cave and not only the right to run the trip. The same person, the
    /// same trip and the same cave publish the moment that one right is there — which is what
    /// says the refusal was about the right and not about anything else in the arrangement.
    /// </summary>
    [Fact]
    public async Task Publishing_takes_the_right_to_share_the_trips_cave()
    {
        // The cave and its model belong to one account; another runs a trip in it. Both are
        // editors, so the trip is unambiguously theirs to write — the only thing in question is
        // whether the cave may leave the installation.
        var cave = await CreateCaveAsync(locationProtected: false);
        var model = await SeedModelWithStationsAsync(cave);

        var email = $"pub-guide-{Guid.NewGuid():N}"[..20] + "@t.local";
        var guideId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        var guide = await AuthHelper.BearerClientAsync(factory, email);

        var (trip, cavers, _) = await CreateTripAsync("Guided", guests: 1, client: guide);
        (await PutConfigAsync(guide, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guide.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = new[] { cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // One right taken away and nothing else: they still read the cave, still place it, still
        // write the trip — the tracking configuration above proves all three.
        await SetCaveShareDenyAsync(guideId, cave, denied: true);

        var refused = await guide.PostAsync(Shares(trip), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.publication_refused_cave");
        // A refusal that had written a row would leave a link somebody could later revoke and
        // believe had been live.
        (await BodyAsync(await guide.GetAsync(Shares(trip)))).GetArrayLength().ShouldBe(0);

        // The positive twin: one right restored, everything else untouched.
        await SetCaveShareDenyAsync(guideId, cave, denied: false);
        var (_, token) = await PublishAsync(trip, guide);
        (await FollowAsync(token)).GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
    }

    /// <summary>
    /// The publication refusal does not rest on the derived protection column alone. A cave
    /// whose root flag is set while that column still says otherwise is refused — and the same
    /// trip through the same link was being followed a moment earlier, which is what says the
    /// disagreement closed it rather than anything else about the fixture.
    /// </summary>
    /// <remarks>
    /// The one place in this file that writes <c>location_protected</c> by hand, and the reason
    /// is the opposite of the usual one: the state under test IS the state where the flag and
    /// the derived column disagree, and only a write that skips the service can produce it. The
    /// disagreement is asserted below rather than assumed, so a future write path that did
    /// recompute the column would fail this test instead of quietly making it prove nothing.
    /// </remarks>
    [Fact]
    public async Task A_cave_whose_protection_column_has_gone_stale_is_still_refused()
    {
        var trip = await TrackedTripAsync("Drift", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        var open = await FollowAsync(token);
        open.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == trip.Cave)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.LocationProtected, true));
        }

        (await ProtectionColumnsAsync(trip.Cave)).ShouldBe((Root: true, Derived: false),
            "this test is only about a disagreement, so the disagreement has to exist");

        (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var refused = await owner.PostAsync(Shares(trip.Trip), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.publication_refused_protected");
    }

    /// <summary>
    /// What a follower is actually handed: a capability for one file. The same token against
    /// another file — one belonging to the very same cave — opens nothing, so the envelope is a
    /// drawing rather than a way into the file store.
    /// </summary>
    [Fact]
    public async Task The_delivery_url_a_follower_is_handed_reaches_that_file_alone()
    {
        var trip = await TrackedTripAsync("Delivery", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        var modelUrl = (await FollowAsync(token)).GetProperty("model").GetProperty("modelUrl").GetString()!;
        (await anonymous.GetAsync(modelUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var neighbour = await FileIdOfModelAsync(await SeedModelWithStationsAsync(trip.Cave));
        var pointedElsewhere = modelUrl.Replace(
            (await FileIdOfModelAsync(trip.Model)).ToString(), neighbour.ToString(), StringComparison.OrdinalIgnoreCase);
        pointedElsewhere.ShouldNotBe(modelUrl, "the two files must differ for this to prove anything");
        (await anonymous.GetAsync(pointedElsewhere)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The address a follower is handed names the file it delivers, so a re-read of the same trip
    /// is recognisably the same survey and a survey that has been swapped is recognisably not.
    /// </summary>
    /// <remarks>
    /// <b>A page keeps the address it first loaded, and it has to know when not to.</b> The
    /// envelope is re-read every minute and re-signs the same file each time, so a viewer handed
    /// the newest string would re-download the survey and reset its camera once a minute. But a
    /// coordinator may re-point an armed watch at a corrected survey while the party is
    /// underground, and a page that kept the first address forever would then draw the old
    /// geometry under the new survey's station names. The followed page tells the two apart by the
    /// address's path — everything before the signature — and this is the property of the server
    /// that makes that reading sound. There is deliberately no survey identifier on the envelope
    /// to compare instead.
    /// </remarks>
    [Fact]
    public async Task The_delivery_url_keeps_its_path_across_re_reads_and_changes_it_when_the_survey_does()
    {
        var trip = await TrackedTripAsync("Same survey, fresh signature", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        static string PathOf(JsonElement envelope)
        {
            var url = envelope.GetProperty("model").GetProperty("modelUrl").GetString()!;
            var query = url.IndexOf('?', StringComparison.Ordinal);
            return query < 0 ? url : url[..query];
        }

        var first = PathOf(await FollowAsync(token));
        // A minute later, the same trip and the same survey: whatever the signature says, the file
        // being delivered is the one already in the browser.
        PathOf(await FollowAsync(token)).ShouldBe(first);

        // And the corrected survey, uploaded and pointed at mid-trip: a different drawing, said by
        // a different address, which is the only thing telling the page to take it up.
        var corrected = await SeedModelWithStationsAsync(trip.Cave);
        (await PutConfigAsync(owner, trip.Trip, new { state = "armed", surveyModelId = corrected }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        PathOf(await FollowAsync(token)).ShouldNotBe(first);
    }

    /// <summary>
    /// Revoked, unknown, malformed and oversized tokens are one answer, and the live link beside
    /// them is the half that says the answer means something.
    /// </summary>
    [Fact]
    public async Task A_revoked_link_answers_exactly_as_an_invented_one_while_a_live_link_opens()
    {
        var trip = await TrackedTripAsync("Two links", locationProtected: false);
        var doomed = await PublishAsync(trip.Trip);
        var kept = await PublishAsync(trip.Trip);

        // Positive half, before anything is revoked: both open.
        (await anonymous.GetAsync(Follow(doomed.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(Follow(kept.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await RevokeAsync(trip.Trip, doomed.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var revoked = await anonymous.GetAsync(Follow(doomed.Token));
        var unknown = await anonymous.GetAsync(Follow("Zm9yZ2VkLXRva2VuLXRoYXQtd2FzLW5ldmVyLW1pbnRlZA"));
        var malformed = await anonymous.GetAsync(Follow("not a token"));
        var oversized = await anonymous.GetAsync(Follow(new string('a', 400)));

        var shapes = new List<string>();
        foreach (var answer in new[] { revoked, unknown, malformed, oversized })
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            shapes.Add(await RefusalShapeAsync(answer));
        }

        shapes[0].ShouldContain("tracking.share_not_found");
        shapes.Distinct().Count().ShouldBe(1, "a follower must not be able to tell why a token failed");

        // And the other link is untouched: revoking is per link, not per trip.
        (await anonymous.GetAsync(Follow(kept.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revoking twice is the same fact said twice.
        (await RevokeAsync(trip.Trip, doomed.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A link with an end, and the end is not a distinguishable answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until this existed a follow link ended in exactly one way — somebody revoking it — and that
    /// is the one remedy the workflow does not actually cover: the paste-in block puts the token
    /// into a club's own article, which is indexed and archived, so an address handed over in March
    /// is still fetchable in November from a page nobody has edited since. The expiry needs nobody
    /// to remember anything.
    /// </para>
    /// <para>
    /// The live link beside the lapsed one is what makes this a test of the window rather than of
    /// the route 404ing: the two rows differ in one column. And the refusal is compared against an
    /// invented token's, because an answer that said "expired" would tell a stranger holding a
    /// guess that their guess was once real.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_lapsed_link_answers_exactly_as_an_invented_one_while_a_live_link_opens()
    {
        var trip = await TrackedTripAsync("A link that ends", locationProtected: false);
        var doomed = await PublishAsync(trip.Trip);
        var kept = await PublishAsync(trip.Trip);

        // Both open while both are inside their window. Without this the assertion below proves
        // only that the route can 404.
        (await anonymous.GetAsync(Follow(doomed.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(Follow(kept.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ExpireAsync(doomed.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var lapsed = await anonymous.GetAsync(Follow(doomed.Token));
        var unknown = await anonymous.GetAsync(Follow("Zm9yZ2VkLXRva2VuLXRoYXQtd2FzLW5ldmVyLW1pbnRlZA"));

        lapsed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RefusalShapeAsync(lapsed)).ShouldBe(
            await RefusalShapeAsync(unknown),
            "a lapsed link must be indistinguishable from one that never existed");

        // Per link, not per trip: the other one is inside its own window and untouched.
        (await anonymous.GetAsync(Follow(kept.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The mint says when the link ends, and the managing list goes on saying it.
    /// </summary>
    /// <remarks>
    /// The token exists in one response and can never be shown again, so the mint is the only
    /// moment somebody pasting an address into an article can be told how long it will answer — and
    /// the list is the only place they can come back to and find out afterwards. Both are asserted
    /// against the same instant, because two surfaces disagreeing about when a publication ends
    /// would be worse than neither saying.
    /// </remarks>
    [Fact]
    public async Task The_mint_and_the_list_say_the_same_end()
    {
        var trip = await TrackedTripAsync("Published until", locationProtected: false);

        var minted = await owner.PostAsync(Shares(trip.Trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var expiresAt = (await BodyAsync(minted)).GetProperty("expiresAt").GetDateTimeOffset();

        // A real end rather than a placeholder, and inside the configured window rather than at
        // some far horizon: an expiry ten years out is not an expiry.
        expiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        expiresAt.ShouldBeLessThan(DateTimeOffset.UtcNow.AddDays(60));

        var listed = await owner.GetAsync(Shares(trip.Trip));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Single();
        row.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(expiresAt);
        // And the list still carries no token, which is the thing it must never start doing.
        row.EnumerateObject().Select(m => m.Name).ShouldNotContain("token");
    }

    /// <summary>
    /// Closing the watch closes the page, after a window in which it can still say the party is out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two hosts over one database, and only the grace setting differs between them — so what is
    /// being read is the window rather than the fact that the watch was closed. The same token, the
    /// same rows, two answers.
    /// </para>
    /// <para>
    /// The grace window is the half that is easy to argue away: the instant a coordinator closes
    /// the watch is the instant the page has its most important thing to say, to the people who
    /// have been refreshing it all evening. A page that went dark then would look exactly like the
    /// party having stopped being reported.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Closing_the_watch_leaves_a_grace_window_and_an_installation_may_have_none()
    {
        var trip = await TrackedTripAsync("Everybody out", locationProtected: false);
        var share = await PublishAsync(trip.Trip);

        (await PutConfigAsync(owner, trip.Trip, new { state = "closed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Still answering, and still answering with the party — which is the whole point of the
        // window rather than merely of the status code.
        var final = await FollowAsync(share.Token);
        final.GetProperty("state").GetString().ShouldBe("closed");
        final.GetProperty("participants").GetArrayLength().ShouldBeGreaterThan(0);

        using var noGrace = NoGraceFactory();
        using var strangerElsewhere = noGrace.CreateClient();
        var refused = await strangerElsewhere.GetAsync(Follow(share.Token));
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RefusalShapeAsync(refused)).ShouldBe(
            await RefusalShapeAsync(
                await strangerElsewhere.GetAsync(Follow("Zm9yZ2VkLXRva2VuLXRoYXQtd2FzLW5ldmVyLW1pbnRlZA"))),
            "a link whose watch has closed must be indistinguishable from one that never existed");
    }

    /// <summary>
    /// A watch that is not running is not published, and minting says so rather than handing over
    /// an address that opens nothing.
    /// </summary>
    [Fact]
    public async Task Publishing_takes_a_running_watch_and_refuses_one_that_is_not()
    {
        var trip = await TrackedTripAsync("Stood down", locationProtected: false);

        // The positive half first, so the refusal below cannot be the trip simply being
        // unpublishable for some other reason.
        (await owner.PostAsync(Shares(trip.Trip), null)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PutConfigAsync(owner, trip.Trip, new { state = "closed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await owner.PostAsync(Shares(trip.Trip), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RefusalShapeAsync(refused)).ShouldContain("tracking.publication_refused_not_armed");
    }

    /// <summary>
    /// A member of the party who cannot publish can still learn that the trip is published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap this closes: the list of links takes write access to the trip, the tracking state
    /// carried no fact about publication, and nothing told anybody — so somebody whose real name
    /// was on a public page, by this installation's default, had no way at all to find out.
    /// </para>
    /// <para>
    /// Asserted through a reader who cannot write the trip, and paired in both directions: before
    /// anything is published the same reader is told nothing, and after the link is taken back they
    /// are told nothing again. A field that always answered would be no better than the silence it
    /// replaced. And the read is checked for carrying no token, because the fact must not become a
    /// second copy of the capability.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_trips_own_read_says_it_is_published_to_somebody_who_cannot_publish_it()
    {
        var trip = await TrackedTripAsync("Told at last", locationProtected: false);

        var before = await StateAsync(reader, trip.Trip);
        before.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        before.GetProperty("publishedUntil").ValueKind.ShouldBe(JsonValueKind.Null);

        var share = await PublishAsync(trip.Trip);

        // The reader may not even list the links, which is the surface this one exists beside.
        (await reader.GetAsync(Shares(trip.Trip))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var published = await StateAsync(reader, trip.Trip);
        published.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.String);
        published.GetProperty("publishedUntil").GetDateTimeOffset()
            .ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        published.ToString().ShouldNotContain(share.Token);

        (await RevokeAsync(trip.Trip, share.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await StateAsync(reader, trip.Trip);
        after.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The trip's own read tells each participant what the published page calls them, in the same
    /// words the page uses.
    /// </summary>
    /// <remarks>
    /// Read off both surfaces in one test and compared, because the value of the field is that it
    /// is the page's answer and not an approximation of it: a signed-in surface that said "a place
    /// in the party" about somebody the page names would be worse than saying nothing. The captions
    /// are what let the two be lined up at all — the published envelope carries no caver id, by
    /// design.
    /// </remarks>
    [Fact]
    public async Task The_trips_own_read_says_what_the_published_page_calls_each_person()
    {
        var trip = await TrackedTripAsync("Named where they can see it", locationProtected: false, guests: 2);
        await CaptionAsync(trip.Trip, trip.Cavers[0], "The trip leader");
        var share = await PublishAsync(trip.Trip);

        var signedIn = await StateAsync(owner, trip.Trip);
        var page = await FollowAsync(share.Token);

        var told = signedIn.GetProperty("participants").EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("caverId").GetGuid(),
                p => p.GetProperty("publishedAs").ValueKind == JsonValueKind.Null
                    ? null
                    : p.GetProperty("publishedAs").GetString());

        // The captioned one, by the id that was captioned: the caption is what the page prints, and
        // it is what the reader is told.
        told[trip.Cavers[0]].ShouldBe("The trip leader");
        Member(page, "The trip leader").GetProperty("label").GetString().ShouldBe("The trip leader");

        // And the whole answer, compared as a set against the page's own. Deliberately not by
        // position: the helper hands back cavers ordered by id and the names it invented in the
        // order it invented them, so a test that lined the two lists up would be right about half
        // the time and would fail on the other half with nothing to say why.
        //
        // This is also the stronger claim. What matters is not that some name arrived but that the
        // two surfaces say the same thing about the same party — the signed-in read cannot be
        // approximating the page, because a follower is looking at the second list and a member is
        // reading the first.
        told.Values.Order(StringComparer.Ordinal).ShouldBe(Labels(page).Order(StringComparer.Ordinal));

        // And the uncaptioned half really is the roster's own name rather than an echo of the
        // caption column: of the two people on this trip the page names one by the caption and the
        // other by a name the trip was built with.
        var named = told.Values.OfType<string>().ToList();
        named.Count.ShouldBe(2);
        named.ShouldContain("The trip leader");
        named.ShouldContain(name => trip.Names.Contains(name));
    }

    /// <summary>
    /// An installation that publishes nobody's name tells them that too.
    /// </summary>
    /// <remarks>
    /// The twin of the test above, and the one that proves the field runs the published page's rule
    /// rather than reading the roster: same rows, same trip, one setting different, and the answer
    /// on the signed-in read changes with it.
    /// </remarks>
    [Fact]
    public async Task Where_no_names_are_published_the_read_says_the_page_will_name_nobody()
    {
        var trip = await TrackedTripAsync("Numbered party", locationProtected: false);
        (await StateAsync(owner, trip.Trip)).GetProperty("participants").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("publishedAs").ValueKind == JsonValueKind.String);

        using var namesOff = NamesOffFactory();
        using var quiet = await AuthHelper.BearerClientAsync(namesOff, ownerEmail);

        (await StateAsync(quiet, trip.Trip)).GetProperty("participants").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("publishedAs").ValueKind == JsonValueKind.Null);
    }

    /// <summary>
    /// The people a published page names are told that it exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trip's own page says a trip is published and cannot be muted, but it only helps somebody
    /// who goes and looks — and nobody opens a trip they were on last week to check whether
    /// something changed about it. A publication is an event with a moment, so the thing that
    /// matches it is a message sent at that moment.
    /// </para>
    /// <para>
    /// Both directions in one test. Nothing is queued at all for a trip nobody published — which is
    /// what keeps this from passing against a build that notified on every trip write — and after
    /// the link is minted the recipients are exactly the one roster member who holds an account.
    /// Asserted as an equality rather than as "contains", because the two ways this goes wrong are
    /// telling nobody and telling everybody, and only an equality catches the second: the trip's
    /// other participant is a guest with no account and must be reached by nothing, and no account
    /// off the roster may be reached at all.
    /// </para>
    /// <para>
    /// That the person who published is not told is the shared recipient rule's doing and is tested
    /// where that rule lives; it is deliberately not claimed here, because the account that
    /// publishes in this test is not on the trip's roster and an assertion about it would read as a
    /// proof while being true for the wrong reason.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Publishing_tells_the_party_and_reaches_nobody_else()
    {
        var memberEmail = $"pub-member-{Guid.NewGuid():N}"[..24] + "@t.local";
        var memberUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, memberEmail);
        var memberCaver = await CaverOfUserAsync(memberUserId);

        var trip = await TrackedTripAsync(
            "Somebody should know", locationProtected: false, memberCaver: memberCaver);

        // Nothing has been published, so nothing has been said. Without this the assertion below
        // would pass against a server that queued a notice on every trip write.
        (await PublishedNoticesAsync(trip.Trip)).ShouldBeEmpty();

        _ = await PublishAsync(trip.Trip);

        (await PublishedNoticesAsync(trip.Trip)).ShouldBe([memberUserId]);
    }

    /// <summary>
    /// A follower is told the party's names — the roster's own, which is what this installation
    /// publishes by default — and still nothing that identifies anybody beyond that.
    /// </summary>
    /// <remarks>
    /// The two halves are one test on purpose. That names arrive says nothing on its own unless
    /// the same body is also shown to carry no caver identity: a page that published the names by
    /// publishing the people would satisfy the first sentence and defeat the point of the second.
    /// The signed-in read of the same trip is here for the other direction — it keys by
    /// <c>caverId</c> and it keeps doing so, so publishing names widened the public envelope and
    /// changed nothing about what a caller with an account is told.
    /// </remarks>
    [Fact]
    public async Task The_published_party_is_named_from_the_roster_and_still_carries_no_caver_identity()
    {
        var trip = await TrackedTripAsync("Naming", locationProtected: false, guests: 2);
        var (_, token) = await PublishAsync(trip.Trip);

        var raw = await (await anonymous.GetAsync(Follow(token))).Content.ReadAsStringAsync();
        foreach (var caver in trip.Cavers)
        {
            raw.Contains(caver.ToString(), StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                "the published envelope is keyed by a place in the party, never by a person");
        }

        var before = JsonDocument.Parse(raw).RootElement.GetProperty("participants").EnumerateArray().ToList();
        before.Count.ShouldBe(2);
        before.Select(p => p.GetProperty("ordinal").GetInt32()).ShouldBe(new[] { 1, 2 });
        // The names the roster holds, and exactly those: not a rendering of the ordinal, and not
        // somebody else's name — which is why the trip's own names are compared and not merely
        // the fact that some string arrived.
        before.Select(p => p.GetProperty("label").GetString()).Order()
            .ShouldBe(trip.Names.Order());

        // The signed-in read of the same trip is unchanged by any of this: it names the cavers by
        // identity, as it always has, and the caption field stays empty because nobody typed one.
        var signedIn = await StateAsync(owner, trip.Trip);
        signedIn.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid())
            .Order()
            .ShouldBe(trip.Cavers.Order());
        signedIn.GetProperty("participants").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("label").ValueKind == JsonValueKind.Null);
        signedIn.GetProperty("publishesRealNames").GetBoolean().ShouldBeTrue(
            "the panel that mints a link words itself from this, so it has to say what the page does");

        // Naming somebody is still a deliberate act and still wins: the caption is what the page
        // shows, and the person nobody captioned keeps the roster's name.
        var named = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "Ana P." });
        named.StatusCode.ShouldBe(HttpStatusCode.OK, await named.Content.ReadAsStringAsync());

        var after = (await FollowAsync(token)).GetProperty("participants").EnumerateArray().ToList();
        after.Count(p => p.GetProperty("label").GetString() == "Ana P.").ShouldBe(1);
        after.Count(p => trip.Names.Contains(p.GetProperty("label").GetString() ?? "")).ShouldBe(1);

        // Clearing the caption puts that person back to the roster's name rather than to nothing:
        // the caption overrode a name, it did not create the only one there was.
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "   " })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FollowAsync(token)).GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("label").GetString()).Order()
            .ShouldBe(trip.Names.Order());
    }

    /// <summary>
    /// The installation-wide switch, and the one thing that outranks it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both settings are exercised against <b>one trip and one live token</b>, which is the only
    /// way the negative means anything: the same link that answers a party of names here answers
    /// places in the party to a server told not to publish names, so what changed is the setting
    /// and not the fixture. A second host over the same database is what makes that comparison
    /// possible — the setting is read where the envelope is built, not stored on the trip.
    /// </para>
    /// <para>
    /// And the caption an administrator typed wins on both, because that field is how one person
    /// who does not want to appear is kept off a public page without the whole installation
    /// turning the feature off. If the switch could override it, somebody who asked not to be
    /// named would be named the moment a setting somewhere else moved.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_installation_can_publish_no_names_and_a_caption_still_outranks_the_setting()
    {
        var trip = await TrackedTripAsync("Quiet", locationProtected: false, guests: 2);
        var (_, token) = await PublishAsync(trip.Trip);

        using var quiet = NamesOffFactory();
        using var visitor = quiet.CreateClient();

        // As this installation is configured, the link names the party.
        Labels(await FollowAsync(token)).Order().ShouldBe(trip.Names.Order());
        // The same link, read by a server whose operator has turned names off, names nobody.
        var quietBefore = await FollowAsync(token, visitor);
        Labels(quietBefore).ShouldAllBe(label => label == null);
        quietBefore.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("ordinal").GetInt32()).ShouldBe(new[] { 1, 2 });
        // What the page is still for does not depend on the setting: where the party is, is there.
        quietBefore.GetProperty("participants").EnumerateArray()
            .Count(p => p.GetProperty("stationName").GetString() == "cave.upper.2").ShouldBe(1);

        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "Ana P." })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The caption is shown whichever way the setting is set; only what an uncaptioned
        // participant is called differs between the two.
        var quietAfter = Labels(await FollowAsync(token, visitor));
        quietAfter.Count(label => label == "Ana P.").ShouldBe(1);
        quietAfter.Count(label => label == null).ShouldBe(1);

        var loudAfter = Labels(await FollowAsync(token));
        loudAfter.Count(label => label == "Ana P.").ShouldBe(1);
        loudAfter.Count(label => trip.Names.Contains(label ?? "")).ShouldBe(1);

        // And each server's own trip read says which of the two a link minted there would be —
        // which is what the panel that mints one words itself from. Both halves, because a flag
        // that answered the same thing everywhere would tell an administrator nothing.
        (await StateAsync(owner, trip.Trip)).GetProperty("publishesRealNames").GetBoolean().ShouldBeTrue();
        using var quietOwner = await AuthHelper.BearerClientAsync(quiet, ownerEmail);
        (await StateAsync(quietOwner, trip.Trip)).GetProperty("publishesRealNames").GetBoolean()
            .ShouldBeFalse();
    }

    /// <summary>
    /// A member who holds an account is published under the <b>roster's</b> name for them, not under
    /// the display name their account carries — a decision, pinned here so it cannot drift silently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two names are asserted against each other in one test because either alone proves
    /// nothing. That the page says "Ana Popescu" is unremarkable until the same person is shown to
    /// be called something else everywhere a caller is signed in; that the signed-in page says
    /// "Ana P." is unremarkable until the published page is shown not to follow it. Together they
    /// say what is actually true: this application calls one person by two names, on purpose, and
    /// which surface uses which is fixed.
    /// </para>
    /// <para>
    /// Why the roster's, when the shared resolver would have preferred the account's: that resolver
    /// composes "display name, or user name, or <c>user-</c> and eight hex digits", and accounts are
    /// created with the address as the user name — so on a page whose whole point is naming people,
    /// deferring to it would publish <c>user-1a2b3c4d</c> for every member who never chose a display
    /// name. The second half of this test is that demonstration, not a decoration: the account made
    /// the ordinary way resolves to exactly that string, and the published page does not use it.
    /// </para>
    /// <para>
    /// What a member does have is the caption, so the last assertion is that it reaches them like
    /// anybody else — the person who wants a different name on a public page is not without a way
    /// to get one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_member_is_published_under_the_roster_name_and_not_under_their_account_label()
    {
        var member = await MemberWithOwnDisplayNameAsync();
        var trip = await TrackedTripAsync("Member", locationProtected: false, guests: 1,
            memberCaver: member.Caver);
        var (_, token) = await PublishAsync(trip.Trip);

        // The published page: the roster's name for them, and not the one their account carries.
        var published = Labels(await FollowAsync(token));
        published.ShouldContain(member.RosterName);
        published.ShouldNotContain(member.AccountLabel);

        // The signed-in page, which is where the account's own name is honoured — so the line above
        // is this application preferring one existing name over another, not a page that had only
        // one to choose from.
        var detail = await BodyAsync(await owner.GetAsync($"/api/v1/trip-logs/{trip.Trip}"));
        // Both lists: the roster is split by role on that surface, and which side a row lands on is
        // not what this test is about.
        var onTrip = detail.GetProperty("participants").EnumerateArray()
            .Concat(detail.GetProperty("proposers").EnumerateArray())
            .Single(p => p.GetProperty("caverId").GetGuid() == member.Caver);
        onTrip.GetProperty("name").GetString().ShouldBe(member.AccountLabel);

        // And the alternative is worse rather than merely different. The trip's own owner holds an
        // account made the ordinary way — nobody chose a display name for it, and the address is its
        // user name — and the resolver the published page deliberately does not use renders exactly
        // that account as a placeholder. Publishing through it would put that string on the page in
        // place of a person's name, for every member who never chose one.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var ordinary = await db.Users.AsNoTracking().FirstAsync(u => u.Email == ownerEmail);
            ordinary.DisplayName.ShouldBeNull("this account is the ordinary case: nobody named it");
            ProfileProtection.Label(ordinary.Id, ordinary.DisplayName, ordinary.UserName, ordinary.Email)
                .ShouldStartWith(ProfileProtection.AnonymousLabelPrefix);
        }

        // Turned off, a member is a place in the party exactly as a guest is: holding an account
        // neither exempts somebody from the setting nor subjects them to a different rule.
        using var quiet = NamesOffFactory();
        using var visitor = quiet.CreateClient();
        Labels(await FollowAsync(token, visitor)).ShouldAllBe(label => label == null);

        // The caption reaches a member like anybody else, which is the way somebody who holds an
        // account gets a different name onto a public page.
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{member.Caver}",
            new { label = "A club member" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var captioned = Labels(await FollowAsync(token));
        captioned.ShouldContain("A club member");
        captioned.ShouldNotContain(member.RosterName);
    }

    /// <summary>
    /// The re-check that makes the refusal mean something after the fact: a link handed out last
    /// week stops opening the moment somebody decides the cave needs protecting. The page working
    /// beforehand, and the trip still being readable by its owner afterwards, are what say the
    /// protection closed the publication rather than the test breaking the trip.
    /// </summary>
    [Fact]
    public async Task Protection_switched_on_after_the_link_was_handed_out_closes_the_page()
    {
        var trip = await TrackedTripAsync("Late protection", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        var open = await FollowAsync(token);
        open.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        await SetLocationProtectedAsync(trip.Cave, true);

        var closed = await anonymous.GetAsync(Follow(token));
        closed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unknown = await anonymous.GetAsync(Follow("Zm9yZ2VkLXRva2VuLXRoYXQtd2FzLW5ldmVyLW1pbnRlZA"));
        (await RefusalShapeAsync(closed)).ShouldBe(await RefusalShapeAsync(unknown),
            "a link the cave has closed must not be distinguishable from one that never existed");

        // The trip itself is untouched — this closed a publication, not a record.
        (await StateAsync(owner, trip.Trip)).GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        // And a fresh link cannot be minted while the protection stands.
        var refused = await owner.PostAsync(Shares(trip.Trip), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("tracking.publication_refused_protected");

        // Taking the protection back off re-opens the link that was never revoked: the page reads
        // the cave's state now, and remembers no verdict of its own.
        await SetLocationProtectedAsync(trip.Cave, false);
        (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The trip's own read stops calling the trip published at the moment the cave does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two answers about one page, which have to move together — and for a while only one of
    /// them moved.</b> The published read refuses on three grounds: the link's own window, the
    /// watch, and the cave. The trip's read consulted the window alone, so a cave protected after a
    /// link had been handed out left every follower with a 404 while the tab the trip is
    /// administered from went on drawing "this trip is published … anybody holding the link can
    /// open a page about this trip". A participant who read that banner and asked for a caption
    /// would have been acting on a page that no longer existed.
    /// </para>
    /// <para>
    /// Read from both surfaces at the same instant, so what is pinned is that they agree rather
    /// than that either says something in particular. Both directions, because the refusal is taken
    /// again on every read and written down nowhere: published, then not, then published again once
    /// the protection comes off, with the link never revoked and never re-minted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_trips_own_read_stops_saying_published_when_the_cave_refuses()
    {
        var trip = await TrackedTripAsync("Published until protected", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = await StateAsync(reader, trip.Trip);
        published.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.String);
        published.GetProperty("publishedUntil").ValueKind.ShouldBe(JsonValueKind.String);

        await SetLocationProtectedAsync(trip.Cave, true);

        (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var refused = await StateAsync(reader, trip.Trip);
        refused.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null,
            "the page answers nothing, so the trip's own read must not call the trip published");
        // Together, because "published" and "until" are one answer: an until with no since would
        // put a date on a publication this read has just denied.
        refused.GetProperty("publishedUntil").ValueKind.ShouldBe(JsonValueKind.Null);

        await SetLocationProtectedAsync(trip.Cave, false);

        (await anonymous.GetAsync(Follow(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(reader, trip.Trip)).GetProperty("publishedAt").ValueKind
            .ShouldBe(JsonValueKind.String,
                "the refusal is re-decided on every read, so lifting it publishes the trip again");
    }

    /// <summary>
    /// The published page withholds a position exactly where the signed-in read does. Both halves
    /// of the same trip, before and after the anchor is severed: with the anchor in place both
    /// name the station, without it neither does and both say so.
    /// </summary>
    [Fact]
    public async Task The_published_envelope_withholds_what_the_signed_in_read_withholds()
    {
        var trip = await TrackedTripAsync("Anchors", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        var publishedBefore = await FollowAsync(token);
        var signedInBefore = await StateAsync(owner, trip.Trip);
        publishedBefore.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        signedInBefore.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        publishedBefore.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        signedInBefore.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();
        // The hour a position was reported at travels with the station on both surfaces, so the
        // negative below is a withholding rather than a field neither read ever fills in.
        TimeOf(publishedBefore.GetProperty("participants").EnumerateArray().Single(), "positionRecordedAt")
            .ShouldNotBeNull();
        TimeOf(signedInBefore.GetProperty("participants").EnumerateArray().Single(), "positionRecordedAt")
            .ShouldNotBeNull();

        // Sever the row's own anchor — the cave the protection rule would be evaluated against.
        // The configuration keeps its own, so the page still opens; the row, with nothing left to
        // decide about, is withheld from everyone.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TripPositionEvents
                .Where(e => e.TripLogId == trip.Trip)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CaveFeatureId, (Guid?)null));
        }

        var publishedAfter = await FollowAsync(token);
        var signedInAfter = await StateAsync(owner, trip.Trip);
        publishedAfter.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        signedInAfter.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        publishedAfter.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        signedInAfter.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        // And the hour goes with it, on both, so that no surface can put an age on a place it was
        // refused. What it does not do is make the hour a secret — the last-heard time is still
        // sent to a follower who cannot be shown a position, because a page whose whole purpose is
        // that somebody is still being heard from has to keep saying when. Asserted beside the
        // null so the pair is read together rather than as a protection it is not.
        TimeOf(publishedAfter.GetProperty("participants").EnumerateArray().Single(), "positionRecordedAt")
            .ShouldBeNull();
        TimeOf(signedInAfter.GetProperty("participants").EnumerateArray().Single(), "positionRecordedAt")
            .ShouldBeNull();
        TimeOf(publishedAfter.GetProperty("participants").EnumerateArray().Single(), "lastRecordedAt")
            .ShouldNotBeNull();

        // What is not withheld is who is still underground: a follower keeps the fact the page
        // exists for even when a position cannot be shown. That this one caver — placed by a
        // station report and never by an `entered` — reads as underground is the presence rule
        // itself: a station of the cave's own model is a place inside the cave.
        publishedAfter.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("in").GetBoolean().ShouldBeTrue();
        signedInAfter.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("in").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// The page a family is watching does not move somebody between the counts because a note was
    /// written about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three people, one report apiece to tell them apart, and each is the twin of another: the
    /// note after an exit against the note after an entry, and the person nothing has been said
    /// about against the same person once they go in. Somebody reading only the first would not
    /// know whether the page had stopped folding reports altogether.
    /// </para>
    /// <para>
    /// This is the published half of a rule that now lives in one place in Domain and is asked for
    /// by both reads. The signed-in half of it is asserted in the tracking tests; what this one
    /// adds is that the envelope a stranger is handed agrees, since the two used to compute
    /// standing from hand-written copies that could differ.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_note_about_somebody_already_out_leaves_them_out_on_the_followed_page()
    {
        var trip = await ArmedTripAsync("Standing", locationProtected: false, guests: 3);
        await CaptionAsync(trip.Trip, trip.Cavers[0], "Waiting");
        await CaptionAsync(trip.Trip, trip.Cavers[1], "Inside");
        await CaptionAsync(trip.Trip, trip.Cavers[2], "Home");
        var (_, token) = await PublishAsync(trip.Trip);

        // Word about the first arrives before the party sets off; the other two go in.
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[0] }, kind = "note", note = "running late" }, At(8, 0));
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[1], trip.Cavers[2] }, kind = "entered" }, At(9, 0));
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[1] }, kind = "note", note = "asked for rope" }, At(9, 5));
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[2] }, kind = "exited" }, At(17, 0));
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[2] }, kind = "note", note = "got a lift home" }, At(17, 5));

        var page = await FollowAsync(token);

        // Neither in nor out, and it has to stay that way: to somebody waiting at home the
        // difference between "they have not gone in yet" and "they are safely back" is the page.
        var waiting = Member(page, "Waiting");
        waiting.GetProperty("in").GetBoolean().ShouldBeFalse();
        waiting.GetProperty("out").GetBoolean().ShouldBeFalse();
        TimeOf(waiting, "lastRecordedAt").ShouldBe(At(8, 0), "the note was heard, it just said nothing about presence");

        // The twin: the same kind of report about somebody underground leaves them underground.
        var inside = Member(page, "Inside");
        inside.GetProperty("in").GetBoolean().ShouldBeTrue();
        inside.GetProperty("out").GetBoolean().ShouldBeFalse();

        // And the defect this test is named for: a note about somebody already out.
        var home = Member(page, "Home");
        home.GetProperty("out").GetBoolean().ShouldBeTrue();
        home.GetProperty("in").GetBoolean().ShouldBeFalse();
        TimeOf(home, "lastRecordedAt").ShouldBe(At(17, 5), "the note landed after the exit, and still did not undo it");

        // The other twin: the page does still move when something that speaks to presence arrives.
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[0] }, kind = "entered" }, At(18, 0));
        var arrived = Member(await FollowAsync(token), "Waiting");
        arrived.GetProperty("in").GetBoolean().ShouldBeTrue();
        arrived.GetProperty("out").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A place reported after somebody came out does not put them back underground on the page
    /// their family is watching; a recorded entry does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The published half of the rule, and the half that matters most, because this page has no
    /// author present when it changes. A report's time is the clock at the moment it was written
    /// unless somebody set it, and rows also arrive from a device-export import carrying a
    /// recorder's own clock — a cave affords no fix to correct that clock against, and an archive
    /// from Saturday is routinely imported on Monday. A page gated on the share token and on its
    /// cave still being publishable, never on whether the trip is over, will re-render either of
    /// those. Under the rule this replaces, that re-render moved somebody who is at home back into
    /// the underground count with nothing on the page to explain it.
    /// </para>
    /// <para>
    /// The second caver is the twin: the same exit, then the one report that is meant to undo it.
    /// Without them this would pass on a page whose standings had simply stopped changing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_place_reported_after_an_exit_does_not_return_a_follower_to_the_underground_count()
    {
        var trip = await ArmedTripAsync("Backfilled", locationProtected: false, guests: 2);
        await CaptionAsync(trip.Trip, trip.Cavers[0], "Home");
        await CaptionAsync(trip.Trip, trip.Cavers[1], "Back inside");
        var (_, token) = await PublishAsync(trip.Trip);

        await ReportAsync(trip.Trip, new { caverIds = trip.Cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(trip.Trip, new { caverIds = trip.Cavers, kind = "exited" }, At(17, 0));

        var home = Member(await FollowAsync(token), "Home");
        home.GetProperty("out").GetBoolean().ShouldBeTrue();

        // Word about where one of them was, stamped after the exit it describes the run-up to.
        await ReportAsync(
            trip.Trip,
            new { caverIds = new[] { trip.Cavers[0] }, kind = "atStation", stationName = "cave.deep.3" },
            At(17, 20));
        // And the other really did go back in, with somebody saying so.
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[1] }, kind = "entered" }, At(17, 20));

        var page = await FollowAsync(token);

        // The place lands and is shown with its own hour — this is not a rule that drops the
        // report — and the person it is about is still out.
        var stillHome = Member(page, "Home");
        stillHome.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        TimeOf(stillHome, "positionRecordedAt").ShouldBe(At(17, 20));
        stillHome.GetProperty("out").GetBoolean().ShouldBeTrue();
        stillHome.GetProperty("in").GetBoolean().ShouldBeFalse();

        var backInside = Member(page, "Back inside");
        backInside.GetProperty("in").GetBoolean().ShouldBeTrue();
        backInside.GetProperty("out").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A published position carries the hour it was reported at, and a later note does not age it.
    /// </summary>
    /// <remarks>
    /// The twin is the first read: while the station <em>is</em> the latest word the two times
    /// agree, so the second read's disagreement is the note arriving and nothing else. This page is
    /// read by people judging how old a place is, and it used to carry only the time of the last
    /// report of any kind — beside a station that could be hours older.
    /// </remarks>
    [Fact]
    public async Task A_published_positions_time_is_its_own_report_and_a_later_note_does_not_age_it()
    {
        var trip = await ArmedTripAsync("Ages", locationProtected: false, guests: 2);
        await CaptionAsync(trip.Trip, trip.Cavers[0], "Placed");
        await CaptionAsync(trip.Trip, trip.Cavers[1], "Unplaced");
        var (_, token) = await PublishAsync(trip.Trip);

        await ReportAsync(trip.Trip, new { caverIds = trip.Cavers, kind = "entered" }, At(9, 0));
        await ReportAsync(
            trip.Trip,
            new { caverIds = new[] { trip.Cavers[0] }, kind = "atStation", stationName = "cave.upper.2" },
            At(9, 30));

        var fresh = await FollowAsync(token);
        var placed = Member(fresh, "Placed");
        placed.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        TimeOf(placed, "lastRecordedAt").ShouldBe(At(9, 30));
        TimeOf(placed, "positionRecordedAt").ShouldBe(At(9, 30));

        // Nobody has placed the other one; they still have a last word.
        var unplaced = Member(fresh, "Unplaced");
        unplaced.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        TimeOf(unplaced, "positionRecordedAt").ShouldBeNull();
        TimeOf(unplaced, "lastRecordedAt").ShouldBe(At(9, 0));

        // Four hours later a note lands. The station is four hours old and has to keep saying so.
        await ReportAsync(trip.Trip, new { caverIds = new[] { trip.Cavers[0] }, kind = "note", note = "asked for rope" }, At(13, 30));

        var aged = Member(await FollowAsync(token), "Placed");
        aged.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        TimeOf(aged, "lastRecordedAt").ShouldBe(At(13, 30));
        TimeOf(aged, "positionRecordedAt").ShouldBe(At(9, 30));
    }

    /// <summary>
    /// Who may publish, and what each refusal looks like. Publishing is a write on the trip, so a
    /// caller who may only read it is told so, and one who was never shown the trip is told
    /// nothing at all.
    /// </summary>
    [Fact]
    public async Task Publishing_is_a_trip_write_and_each_refusal_keeps_its_own_shape()
    {
        var trip = await TrackedTripAsync("Authority", locationProtected: false);

        // No account: the route is behind the default-deny group, so it never reaches a handler.
        (await anonymous.PostAsync(Shares(trip.Trip), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Shares(trip.Trip))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}", new { label = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Readable but not writable: forbidden, and the same for naming somebody.
        (await reader.PostAsync(Shares(trip.Trip), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync(Shares(trip.Trip))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}", new { label = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Never shown the trip at all: indistinguishable from a trip that does not exist.
        var (hidden, _, _) = await CreateTripAsync("Private", guests: 1, visibility: "private");
        var unseen = await reader.PostAsync(Shares(hidden), null);
        unseen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unseen.Content.ReadAsStringAsync()).ShouldContain("trip_log.not_found");
        var invented = await owner.PostAsync(Shares(Guid.NewGuid()), null);
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A trip nobody is tracking has nothing to follow.
        var untracked = await owner.PostAsync(Shares(hidden), null);
        untracked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await untracked.Content.ReadAsStringAsync()).ShouldContain("tracking.model_missing");

        // A link that is not this trip's is not found, and neither is one that never existed.
        (await RevokeAsync(trip.Trip, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Naming somebody the trip does not name is refused rather than invented.
        var stranger = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{Guid.NewGuid()}", new { label = "Nobody" });
        stranger.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await stranger.Content.ReadAsStringAsync()).ShouldContain("tracking.caver_not_participant");

        // And a label longer than the column is a validation refusal, not a truncation.
        var tooLong = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = new string('x', 400) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("validation.failed");
    }

    // ---- the pictures on a followed page -----------------------------------------------------

    /// <summary>
    /// A photograph an administrator published is shown at the station it was taken at, and the
    /// identical photograph that nobody published is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves in one test because either alone proves nothing. A page that showed no pictures
    /// at all would pass the absence; a page that showed everything linked to the model would pass
    /// the presence. The two photographs here are alike in every way that could matter — same
    /// upload path, same link shape, same station, same trip — and differ only in the flag that
    /// says a person decided this one may be seen by anybody.
    /// </para>
    /// <para>
    /// What the URL opens is asserted by spending it rather than by reading it, and spent by the
    /// caller it was actually minted for: somebody with no account at all. The rendering is served
    /// and the upload is refused, which is the whole difference between the reach a photograph gets
    /// here and the reach the survey model gets.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_published_photograph_is_shown_at_its_station_and_an_unpublished_one_is_not()
    {
        var trip = await TrackedTripAsync("Pictures", locationProtected: false);

        var shown = await PhotographAsync("shown");
        var kept = await PhotographAsync("kept");
        await LinkAsync(trip.Model, "cave.upper.2", shown.Document);
        await LinkAsync(trip.Model, "cave.upper.2", kept.Document);
        await PublishPhotographAsync(shown.Document);

        var (_, token) = await PublishAsync(trip.Trip);
        var page = await FollowAsync(token);
        var pictures = Pictures(page);

        var picture = pictures.ShouldHaveSingleItem();
        picture.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        var url = picture.GetProperty("thumbnailUrl").GetString()!;
        url.ShouldStartWith($"/api/v1/files/{shown.File}/thumbnail?");
        url.ShouldContain("token=");

        // The absence, said against the fixture rather than against the count alone: nothing
        // anywhere in this envelope names the photograph nobody published.
        var whole = page.GetRawText();
        whole.ShouldNotContain(kept.File.ToString());
        whole.ShouldNotContain(kept.Document.ToString());

        // The rendering is served to a caller carrying nothing...
        (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...and the same token, moved to the route that hands over the upload, opens nothing.
        // That is what a renderings-only reach is for: a photograph's own bytes carry the fix its
        // camera wrote, and a rendering is drawn here with every metadata profile stripped.
        (await anonymous.GetAsync(ContentInsteadOf(url))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And nothing on this surface offers the upload in the first place — the model is the one
        // thing handed over whole, because a survey is only useful as its own bytes.
        whole.ShouldNotContain($"/api/v1/files/{shown.File}/content");
    }

    /// <summary>
    /// A photograph's caption travels; where there is none, its title does. Both are already
    /// published for this very picture by the gallery the flag above put it in.
    /// </summary>
    [Fact]
    public async Task A_published_photographs_caption_travels_and_its_title_stands_in_for_one()
    {
        var trip = await TrackedTripAsync("Captions", locationProtected: false);

        var captioned = await PhotographAsync("captioned");
        var bare = await PhotographAsync("bare");
        await LinkAsync(trip.Model, "cave.upper.2", captioned.Document);
        await LinkAsync(trip.Model, "cave.deep.3", bare.Document);
        await PublishPhotographAsync(captioned.Document);
        await PublishPhotographAsync(bare.Document);
        var credited = await owner.PutAsJsonAsync($"/api/v1/photos/{captioned.Document}/credit", new
        {
            photographerCaverId = (Guid?)null,
            photographerName = (string?)null,
            caption = "Head of the second pitch",
            licenceCode = (string?)null,
            placeName = (string?)null,
        });
        credited.StatusCode.ShouldBe(HttpStatusCode.OK, await credited.Content.ReadAsStringAsync());

        var (_, token) = await PublishAsync(trip.Trip);
        var pictures = Pictures(await FollowAsync(token));

        Picture(pictures, "cave.upper.2").GetProperty("caption").GetString()
            .ShouldBe("Head of the second pitch");
        // Not null, and not the empty string: a nameless thumbnail is worse than a plainly-named one.
        Picture(pictures, "cave.deep.3").GetProperty("caption").GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Protecting the cave after the link was handed out closes the page, pictures and all.
    /// </summary>
    /// <remarks>
    /// The decision that matters is not a picture decision at all: a trip in a protected cave is
    /// refused the whole envelope, re-decided on every read, so there is no answer for a picture to
    /// ride out on. Asserted in three moves over one unchanged token — published and shown, guarded
    /// and gone, unguarded and shown again — because a 404 on its own is what a mistyped token also
    /// answers, and only the recovery says which of the two happened.
    /// </remarks>
    [Fact]
    public async Task Protecting_the_cave_after_minting_takes_the_pictures_with_the_page()
    {
        var trip = await TrackedTripAsync("Guarded later", locationProtected: false);
        var photograph = await PhotographAsync("later");
        await LinkAsync(trip.Model, "cave.upper.2", photograph.Document);
        await PublishPhotographAsync(photograph.Document);

        var (_, token) = await PublishAsync(trip.Trip);
        Pictures(await FollowAsync(token)).Count.ShouldBe(1);

        await SetLocationProtectedAsync(trip.Cave, true);
        var refused = await anonymous.GetAsync(Follow(token));
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The refusal carries nothing of the picture either — not a file id, not a URL.
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain(photograph.File.ToString());

        await SetLocationProtectedAsync(trip.Cave, false);
        Pictures(await FollowAsync(token)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A published, geotagged photograph whose link also names a guarded cave is withheld, and the
    /// same photograph on a link that names no guarded thing is shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the case the gallery flag cannot answer. Marking a picture public is a decision
    /// about whether it may be <em>seen</em>; it says nothing about where a cave is. A link is an
    /// assertion that its members belong together, so publishing one of them at a point in the
    /// drawing gives the whole assertion a position — and a link that also names a cave whose
    /// coordinates are guarded would then stand that cave beside a position. That is the same
    /// pairing the association rule withholds elsewhere, spelled for a surface that names no
    /// feature at all.
    /// </para>
    /// <para>
    /// The twin is the same rows with the flag turned off, so what is being proved is the
    /// protection and not some accident of the fixture: one picture is withheld while its
    /// neighbour on the clean link is shown, and the withheld one appears the moment the second
    /// cave stops being guarded.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_published_photograph_whose_link_names_a_guarded_cave_is_withheld()
    {
        var trip = await TrackedTripAsync("Entangled", locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);

        // Both are geotagged: what decides between them is the company their link keeps, never
        // whether the file happens to carry a fix — the reach already answers that one.
        var clean = await PhotographAsync("clean", GeotaggedJpeg(45.6, 25.6));
        var entangled = await PhotographAsync("entangled", GeotaggedJpeg(45.6, 25.6));
        await PublishPhotographAsync(clean.Document);
        await PublishPhotographAsync(entangled.Document);

        await LinkAsync(trip.Model, "cave.upper.2", clean.Document);
        await LinkAsync(trip.Model, "cave.deep.3", entangled.Document, alsoNaming: guarded);

        var (_, token) = await PublishAsync(trip.Trip);
        var page = await FollowAsync(token);

        Pictures(page).ShouldHaveSingleItem()
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        page.GetRawText().ShouldNotContain(entangled.File.ToString());
        // And the guarded cave is not named anywhere either — it never could be, which is the
        // structural half of why this surface is safe at all.
        page.GetRawText().ShouldNotContain(guarded.ToString());

        // The twin: one flag, and the withheld picture arrives.
        await SetLocationProtectedAsync(guarded, false);
        var reopened = Pictures(await FollowAsync(token));
        reopened.Count.ShouldBe(2);
        Picture(reopened, "cave.deep.3").GetProperty("thumbnailUrl").GetString()
            .ShouldStartWith($"/api/v1/files/{entangled.File}/thumbnail?");
    }

    /// <summary>
    /// A published trip whose stations nobody has linked a published photograph to carries an
    /// empty list — never a null, and never a station with a URL behind nothing.
    /// </summary>
    /// <remarks>
    /// The ordinary answer for most installations, and it has to be a shape the page can draw: an
    /// absent field and a list of one broken URL are the two ways this goes wrong quietly.
    /// </remarks>
    [Fact]
    public async Task A_trip_with_no_published_photographs_carries_an_empty_list()
    {
        var trip = await TrackedTripAsync("Nothing published", locationProtected: false);
        var (_, token) = await PublishAsync(trip.Trip);

        var model = (await FollowAsync(token)).GetProperty("model");
        model.GetProperty("pictures").ValueKind.ShouldBe(JsonValueKind.Array);
        model.GetProperty("pictures").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// A photograph published on a model that already carries a full window of older station
    /// links still reaches the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number of links one model is read for is bounded, and a bound is only half of a window:
    /// which end it is taken from decides what a club with a long-worked cave actually sees. The
    /// signed-in strip pages a model's links newest first, so a photograph linked last week is the
    /// first thing on it. A published page reading the other end would answer with the oldest links
    /// on the model and nothing else — and the failure is silent in the worst way, because "no
    /// photograph of the new pitch appears" is exactly what a correctly-working page answers for an
    /// installation that has published nothing.
    /// </para>
    /// <para>
    /// The filler links are backdated in both the ways a window could be taken from: their creation
    /// stamps and their time-ordered identifiers. Two hundred of them is the window the server
    /// reads; if that number ever rises, this one has to rise with it or the test stops asking
    /// anything. Its twin is every other test in this section, each of which publishes a photograph
    /// on a model carrying only its own handful of links.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_photograph_linked_after_a_full_window_of_older_links_is_still_published()
    {
        var trip = await TrackedTripAsync("Long-worked cave", locationProtected: false);
        await OlderStationLinksAsync(trip.Model, "cave.deep.3", 200);

        var photograph = await PhotographAsync("newest");
        await LinkAsync(trip.Model, "cave.upper.2", photograph.Document);
        await PublishPhotographAsync(photograph.Document);

        var (_, token) = await PublishAsync(trip.Trip);
        var picture = Pictures(await FollowAsync(token)).ShouldHaveSingleItem();

        picture.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        picture.GetProperty("thumbnailUrl").GetString()
            .ShouldStartWith($"/api/v1/files/{photograph.File}/thumbnail?");
    }

    /// <summary>
    /// A place reported before the trip's survey was changed is not handed to a follower as a
    /// place on the survey the page draws — it is dropped, and said to have been dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This page is a drawing, and a station name printed beside it reads as a place on it. The
    /// two surveys here spell their stations identically, so the collision is exact: publishing
    /// <c>cave.upper.2</c> of the earlier survey next to the later survey's drawing would put a
    /// person at a named place in a cave on a page their family is reading.
    /// </para>
    /// <para>
    /// What is kept is that somebody <em>is</em> placed. The envelope carries one bit for it, not
    /// the survey's id — a follower is handed no survey identifiers at all — and the bit exists
    /// because "known, not shown here" and "nobody has reported where they are" are the two
    /// readings this page must never merge.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_place_reported_on_the_earlier_survey_is_not_published_beside_the_later_ones_drawing()
    {
        var trip = await TrackedTripAsync("Re-surveyed mid-trip", locationProtected: false);

        // The twin first, while the watch is still on the survey that place was measured in: the
        // station is published and the page says nothing about another survey.
        var (_, before) = await PublishAsync(trip.Trip);
        var published = (await FollowAsync(before)).GetProperty("participants").EnumerateArray().Single();
        published.GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        published.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
        TimeOf(published, "positionRecordedAt").ShouldNotBeNull();

        // A corrected survey of the same cave arrives and the watch is moved onto it.
        var corrected = await SeedModelWithStationsAsync(trip.Cave);
        (await PutConfigAsync(owner, trip.Trip, new { state = "armed", surveyModelId = corrected }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var envelope = await FollowAsync(before);
        var moved = envelope.GetProperty("participants").EnumerateArray().Single();
        moved.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        moved.GetProperty("depthM").ValueKind.ShouldBe(JsonValueKind.Null);
        moved.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeTrue();
        // The hour goes with the place: a time beside no place is dated from whatever is nearest,
        // and on this page the nearest thing is the drawing.
        TimeOf(moved, "positionRecordedAt").ShouldBeNull();
        // What is not lost: the page still says somebody is underground and still says when they
        // were last heard from, which is the whole reason a family opens it.
        moved.GetProperty("in").GetBoolean().ShouldBeTrue();
        TimeOf(moved, "lastRecordedAt").ShouldNotBeNull();
        // Nothing here is a withholding, and the flag that means that must not have moved.
        envelope.GetProperty("positionsWithheld").GetBoolean().ShouldBeFalse();

        // And the second twin: a report made against the survey now in use is published again, so
        // the rule is about which survey a place was measured in and not about a page that has
        // stopped showing places.
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip.Trip}/tracking/events", new
        {
            caverIds = new[] { trip.Cavers[0] },
            kind = "atStation",
            stationName = "cave.deep.3",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = (await FollowAsync(before)).GetProperty("participants").EnumerateArray().Single();
        again.GetProperty("stationName").GetString().ShouldBe("cave.deep.3");
        again.GetProperty("positionOnOtherModel").GetBoolean().ShouldBeFalse();
    }

    // ---- plumbing --------------------------------------------------------------------------

    /// <summary>
    /// Backdates one link's own end, which is the only part of the window a test cannot reach
    /// through the API: the expiry is fixed at mint and deliberately never extended or shortened
    /// by anything a caller can do.
    /// </summary>
    /// <remarks>
    /// Written straight into the column because it is a plain recorded fact with nothing derived
    /// from it — unlike protection, where writing the root by hand leaves the effective column
    /// stale and makes a test pass whether or not the rule works. Here the value is read on every
    /// request and compared against the clock, so a row written this way is exactly a row that was
    /// minted that long ago.
    /// </remarks>
    private async Task ExpireAsync(Guid shareId, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var share = await db.TripTrackingShares.SingleAsync(s => s.Id == shareId);
        share.ExpiresAt = at;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A second host over the same database, run by an operator who has chosen no grace window
    /// after a watch closes.
    /// </summary>
    /// <remarks>
    /// The same device as the names-off host above, and for the same reason: the window is read
    /// where the request is answered rather than stored anywhere, so this is how one token gets two
    /// answers with the trip, the share and the watch being the very same rows. The alternative —
    /// backdating the close — would also work and would prove less, because it could not tell a
    /// window being applied from a window of zero.
    /// </remarks>
    private SilexGisApiFactory NoGraceFactory()
    {
        var settings = HostSettings();
        settings["TripTracking:ShareGraceAfterClose"] = "00:00:00";
        return new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
    }

    /// <summary>The caver row an account was given when it was created.</summary>
    private async Task<Guid> CaverOfUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.Id).SingleAsync();
    }

    /// <summary>
    /// Who has been told that this trip was published, read off the notification rows themselves.
    /// </summary>
    /// <remarks>
    /// The row's existence is the in-app notice and is what the delivery pass later reads, so this
    /// is the whole of what "somebody was told" means at the moment the mint commits. Narrowed by
    /// template key as well as by target, because a trip collects notices for several reasons and a
    /// test that counted all of them would pass on the strength of the roster announcement.
    /// </remarks>
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

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Follow(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    /// <summary>A trip, armed against a survey model of its own cave, with nothing reported yet.</summary>
    private async Task<(Guid Trip, Guid Cave, Guid Model, List<Guid> Cavers, List<string> Names)> ArmedTripAsync(
        string title, bool locationProtected, int guests = 1, Guid? memberCaver = null)
    {
        var (trip, cavers, names) = await CreateTripAsync(title, guests, existingCaver: memberCaver);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return (trip, cave, model, cavers, names);
    }

    /// <summary>A trip, armed against a survey model of its own cave, with one caver placed.</summary>
    private async Task<(Guid Trip, Guid Cave, Guid Model, List<Guid> Cavers, List<string> Names)> TrackedTripAsync(
        string title, bool locationProtected, int guests = 1, Guid? memberCaver = null)
    {
        var armed = await ArmedTripAsync(title, locationProtected, guests, memberCaver);
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{armed.Trip}/tracking/events", new
        {
            caverIds = new[] { armed.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        return armed;
    }

    /// <summary>
    /// The trip's own day. Reports are folded in the order the <em>reporter</em> gave, not the
    /// order they were typed, so a test about that order has to state its own times.
    /// </summary>
    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    /// <summary>One report, stamped with the hour the reporter gave, asserted to have landed.</summary>
    private async Task ReportAsync(Guid trip, object body, DateTimeOffset recordedAt)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["recordedAt"] = recordedAt.ToString("O");
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Captions the party so that a test can name who it is asserting about.
    /// </summary>
    /// <remarks>
    /// The envelope is keyed by a place in the party and carries no caver id, which is the whole
    /// point of it — so a test cannot look somebody up by identity and must not be given a way to.
    /// A caption is the one label this page shows whatever else is configured, so captioning the
    /// party is how a test says "this row is that person" without the response carrying a person.
    /// </remarks>
    private async Task CaptionAsync(Guid trip, Guid caver, string caption)
    {
        var response = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/participants/{caver}", new { label = caption });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Member(JsonElement envelope, string caption) =>
        envelope.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("label").GetString() == caption);

    /// <summary>A nullable instant off the wire — null is a real answer on every time this page sends.</summary>
    private static DateTimeOffset? TimeOf(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).GetDateTimeOffset();

    private async Task<(Guid Id, string Token)> PublishAsync(Guid trip, HttpClient? client = null)
    {
        var minted = await (client ?? owner).PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var body = await BodyAsync(minted);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    /// <summary>
    /// Denies (or stops denying) one account the right to share one cave, written straight into
    /// storage: a rule naming a person on an object is exactly what the permissions tab writes,
    /// and the deny is scoped to <c>Share</c> alone so everything else that account holds over
    /// the cave — reading it, placing it — is demonstrably untouched.
    /// </summary>
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

    /// <summary>The root flag an operator sets, and the column derived from it.</summary>
    private async Task<(bool Root, bool Derived)> ProtectionColumnsAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Features.AsNoTracking()
            .Where(f => f.Id == featureId)
            .Select(f => new { f.LocationProtected, f.IsProtectedEffective })
            .SingleAsync();
        return (row.LocationProtected, row.IsProtectedEffective);
    }

    private async Task<Guid> FileIdOfModelAsync(Guid modelId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking().Where(m => m.Id == modelId).Select(m => m.FileId).SingleAsync();
    }

    /// <summary>
    /// A refusal as a follower can see it, with the trace identifier left out: that one differs
    /// per request by design and says nothing about the token, while every other member of the
    /// answer has to be the same whichever way the token was wrong.
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

    private Task<HttpResponseMessage> RevokeAsync(Guid trip, Guid shareId) =>
        owner.DeleteAsync($"{Shares(trip)}/{shareId}");

    /// <summary>
    /// The page a follower sees. A client may be named so that one link can be read through more
    /// than one host — the same token, answered by a differently-configured server.
    /// </summary>
    private async Task<JsonElement> FollowAsync(string token, HttpClient? client = null)
    {
        var response = await (client ?? anonymous).GetAsync(Follow(token));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>
    /// Flips the protection root through the service that owns the derived columns, so
    /// <c>is_protected_effective</c> is recomputed exactly as a real edit recomputes it.
    /// </summary>
    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    /// <summary>
    /// A trip with a roster, created through the real API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The names it invents are returned as well as the ids, because the roster's name is what a
    /// published page now shows: a test that asserted only "some string arrived" would pass on a
    /// server that published the wrong person's name, or a rendering of the ordinal.
    /// </para>
    /// <para>
    /// <paramref name="existingCaver"/> puts somebody already in the roster on the trip instead of
    /// inventing them — the only way to build a participant who holds an account, since a guest
    /// added by name never has one. Their name is not in <c>Names</c>: whoever asked for them knows
    /// it, and the point of the ones returned is that they were invented here.
    /// </para>
    /// </remarks>
    private async Task<(Guid Trip, List<Guid> Cavers, List<string> Names)> CreateTripAsync(
        string title, int guests, string visibility = "authenticated", HttpClient? client = null,
        Guid? existingCaver = null)
    {
        var invented = Enumerable.Range(1, guests)
            .Select(i => $"Guest {i} {Guid.NewGuid():N}"[..24])
            .ToList();
        var participants = invented
            .Select(name => (object)new { newCaverName = name })
            .Concat(existingCaver is { } already ? [new { caverId = already }] : [])
            .ToArray();
        var response = await (client ?? owner).PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        cavers.Count.ShouldBe(guests + (existingCaver is null ? 0 : 1));
        return (trip, cavers, invented);
    }

    /// <summary>
    /// A club member: an account, its roster entry renamed to what a roster-keeper would have typed,
    /// and a display name of their own that differs from it.
    /// </summary>
    /// <remarks>
    /// Written straight to the database rather than through the profile routes because what is being
    /// set up is a <em>disagreement</em> between two names — the state every account reaches the
    /// moment somebody edits either one — and the fastest way to it is to state both.
    /// </remarks>
    private async Task<(Guid Caver, string RosterName, string AccountLabel)> MemberWithOwnDisplayNameAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"pub-member-{suffix}@t.local";
        var rosterName = $"Ana Popescu {suffix}";
        var accountLabel = $"Ana P. {suffix}";

        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.DisplayName = accountLabel;
        var caver = await db.Cavers.FirstAsync(c => c.UserId == userId);
        caver.FullName = rosterName;
        await db.SaveChangesAsync();
        return (caver.Id, rosterName, accountLabel);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pub Cave {Guid.NewGuid():N}"[..30],
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
    /// A survey model created the real way (upload — a file row cannot exist outside the
    /// documents chain), then stations seeded straight into the graph tables. The declared
    /// coordinate system is Stereo70, which is the case the CRS answer exists for.
    /// </summary>
    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "publication.3d");
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

    private static SurveyStation Station(Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
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

    private static async Task<JsonElement> StateAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // ---- pictures ----------------------------------------------------------------------------

    private static List<JsonElement> Pictures(JsonElement envelope) =>
        [.. envelope.GetProperty("model").GetProperty("pictures").EnumerateArray()];

    private static JsonElement Picture(IEnumerable<JsonElement> pictures, string station) =>
        pictures.Single(p => p.GetProperty("stationName").GetString() == station);

    /// <summary>
    /// The same delivery URL aimed at the route that hands over the upload, token and all.
    /// </summary>
    /// <remarks>
    /// The reach a token was signed with is the whole of what it opens and the delivery routes
    /// re-decide nothing, so moving one between routes is exactly the attempt the reach exists to
    /// refuse — and the only honest way to assert which reach was minted.
    /// </remarks>
    private static string ContentInsteadOf(string thumbnailUrl)
    {
        var query = thumbnailUrl[(thumbnailUrl.IndexOf('?') + 1)..]
            .Split('&')
            .Single(part => part.StartsWith("token=", StringComparison.Ordinal));
        return thumbnailUrl[..thumbnailUrl.IndexOf('?')].Replace("/thumbnail", "/content") + "?" + query;
    }

    /// <summary>
    /// A photograph uploaded the real way — a file row cannot exist outside the documents chain —
    /// returned as both identities, because the envelope names a file and the gallery flag names a
    /// document.
    /// </summary>
    private async Task<(Guid Document, Guid File)> PhotographAsync(string label, byte[]? bytes = null)
    {
        var content = new ByteArrayContent(bytes ?? PlainJpeg());
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", $"{label}-{Guid.NewGuid():N}.jpg" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var fileId = (await BodyAsync(response)).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return (version.DocumentId, fileId);
    }

    /// <summary>
    /// Puts a photograph in the installation's public gallery — the act of publication this page
    /// takes as consent, performed by the only kind of account allowed to perform it.
    /// </summary>
    private async Task PublishPhotographAsync(Guid documentId)
    {
        var response = await publisher.PutAsync($"/api/v1/photos/{documentId}/public?published=true", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// "This photograph was taken at that station", authored exactly as the viewer authors it: a
    /// link whose one member anchors to a station of the model and whose other is the picture.
    /// <paramref name="alsoNaming"/> adds a third member, which is how a link comes to touch a
    /// feature the trip is not about.
    /// </summary>
    private async Task<Guid> LinkAsync(
        Guid surveyModelId, string station, Guid documentId, Guid? alsoNaming = null)
    {
        List<object> members =
        [
            new
            {
                targetType = "surveyModel",
                targetId = surveyModelId,
                isMain = false,
                sortOrder = 0,
                anchorKind = "modelStation",
                anchor = new { station },
            },
            new { targetType = "document", targetId = documentId, isMain = false, sortOrder = 1 },
        ];
        if (alsoNaming is { } feature)
        {
            members.Add(new { targetType = "feature", targetId = feature, isMain = false, sortOrder = 2 });
        }

        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Older links anchored to a station of one model, enough of them to fill the window a
    /// published page reads — and carrying no photograph, so what they contribute is their number
    /// and their age and nothing else.
    /// </summary>
    /// <remarks>
    /// Written straight into storage rather than authored through the route: what a test needs of
    /// these is that they are old, and nothing minted this second can be. Both of the things a
    /// window could be taken from are made old — the creation stamp, and the time-ordered
    /// identifier — so a query reading either end by either key is answered honestly.
    /// </remarks>
    private async Task OlderStationLinksAsync(Guid surveyModelId, string station, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var earlier = DateTimeOffset.UtcNow.AddYears(-1);
        var ids = new List<Guid>(count);
        for (var i = 0; i < count; i++)
        {
            var id = Guid.CreateVersion7(earlier.AddSeconds(i));
            ids.Add(id);
            db.ResLinks.Add(new ResLink { Id = id, ShortCode = Guid.NewGuid().ToString("N")[..8] });
            db.ResLinkMembers.Add(new ResLinkMember
            {
                Id = Guid.CreateVersion7(earlier.AddSeconds(i)),
                ResLinkId = id,
                EntityType = AttachedEntityType.SurveyModel,
                EntityId = surveyModelId,
                AnchorKind = AnchorKind.ModelStation,
                Anchor = JsonSerializer.Serialize(new { station }),
            });
        }
        await db.SaveChangesAsync();

        // Every insert is stamped with the moment it was inserted, so the ages these rows exist
        // for are written afterwards, by a statement that does not go through that stamping.
        (await db.ResLinks.Where(l => ids.Contains(l.Id))
            .ExecuteUpdateAsync(rows => rows.SetProperty(l => l.CreatedAt, earlier)))
            .ShouldBe(count);
    }

    private static byte[] PlainJpeg()
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A JPEG stamped with a capture point — a photograph that can place what it shows.</summary>
    private static byte[] GeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 64);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>Degrees → EXIF degrees/minutes/seconds rationals.</summary>
    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }
}
