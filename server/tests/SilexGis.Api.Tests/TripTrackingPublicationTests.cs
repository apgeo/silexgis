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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
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

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public TripTrackingPublicationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"trkpub-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the fake survey file
            // below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pub-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pub-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pub-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"pub-read-{suffix}@t.local");
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

        var (trip, cavers) = await CreateTripAsync("Guided", guests: 1, client: guide);
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
    /// A follower is told a place in the party and whatever an administrator typed, and nothing
    /// else about who anybody is. The signed-in read of the same trip is the positive half: it
    /// names the cavers, which is what makes the absence in the public envelope a fact about this
    /// surface rather than about an empty trip.
    /// </summary>
    [Fact]
    public async Task The_published_party_carries_no_caver_identity_beyond_what_the_admin_typed()
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
        before.ShouldAllBe(p => p.GetProperty("label").ValueKind == JsonValueKind.Null);

        // The signed-in read of the same trip does name them — so the envelope above is
        // withholding an identity that exists, not describing a trip that has none.
        var signedIn = await StateAsync(owner, trip.Trip);
        signedIn.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid())
            .Order()
            .ShouldBe(trip.Cavers.Order());

        // Naming somebody is a deliberate act, and it is the only thing that puts a name on the
        // page. The person nobody named stays a number.
        var named = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "Ana P." });
        named.StatusCode.ShouldBe(HttpStatusCode.OK, await named.Content.ReadAsStringAsync());

        var after = (await FollowAsync(token)).GetProperty("participants").EnumerateArray().ToList();
        after.Count(p => p.GetProperty("label").GetString() == "Ana P.").ShouldBe(1);
        after.Count(p => p.GetProperty("label").ValueKind == JsonValueKind.Null).ShouldBe(1);

        // Clearing it takes the name back off the page.
        (await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip.Trip}/tracking/participants/{trip.Cavers[0]}",
            new { label = "   " })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FollowAsync(token)).GetProperty("participants").EnumerateArray()
            .ShouldAllBe(p => p.GetProperty("label").ValueKind == JsonValueKind.Null);
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

        // What is not withheld is who is still underground: a follower keeps the fact the page
        // exists for even when a position cannot be shown.
        publishedAfter.GetProperty("participants").EnumerateArray().Single()
            .GetProperty("in").GetBoolean().ShouldBeTrue();
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
        var (hidden, _) = await CreateTripAsync("Private", guests: 1, visibility: "private");
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

    // ---- plumbing --------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Follow(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    /// <summary>A trip, armed against a survey model of its own cave, with one caver placed.</summary>
    private async Task<(Guid Trip, Guid Cave, Guid Model, List<Guid> Cavers)> TrackedTripAsync(
        string title, bool locationProtected, int guests = 1)
    {
        var (trip, cavers) = await CreateTripAsync(title, guests);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", new
        {
            caverIds = new[] { cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (trip, cave, model, cavers);
    }

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

    private async Task<JsonElement> FollowAsync(string token)
    {
        var response = await anonymous.GetAsync(Follow(token));
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

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(
        string title, int guests, string visibility = "authenticated", HttpClient? client = null)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
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
        cavers.Count.ShouldBe(guests);
        return (trip, cavers);
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
}
