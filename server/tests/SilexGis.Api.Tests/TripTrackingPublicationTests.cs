// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
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

    // ---- plumbing --------------------------------------------------------------------------

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
}
