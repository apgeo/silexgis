// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A deleted trip is gone from every surface that shows trips and keeps every row it had, and a
/// restored one is back on all of them with all of it.
/// </summary>
/// <remarks>
/// <para>
/// The risk this class exists for is a read that forgot. So each surface is asked three times
/// with the same question — before the delete, after it, and after the restore — and the first
/// answer is asserted as firmly as the second: a listing that never showed the trip proves
/// nothing by not showing it once it is deleted.
/// </para>
/// <para>
/// The second half is that nothing was taken down. A restore is exact only because the delete
/// removed no row, so the rows are counted past the filter that hides them while the trip is
/// deleted, and the restored trip is read back through the ordinary routes to show it is the
/// same trip.
/// </para>
/// </remarks>
public sealed class TripSoftDeleteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string WorldBbox = "-180,-90,180,90";

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private Guid readerId;
    private Guid ownerCaver;
    private long caveTypeId;

    public TripSoftDeleteTests(PostgresFixture postgres) =>
        // The calendar feed is an installation's choice and off as shipped; it is one of the
        // surfaces a deleted trip has to leave, so this class runs with it on.
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Protection:CalendarFeedEnabled"] = "true",
        });

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tsd-own-{suffix}@t.local");
        // A plain reader, and it has to be: the seeded editors hold every content domain at the
        // widest reach, so an editor reading a trip proves nothing about the rule that let them.
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsd-rdr-{suffix}@t.local");
        ownerCaver = await RosterHelper.CaverIdForAsync(factory, ownerId);

        owner = await AuthHelper.BearerClientAsync(factory, $"tsd-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tsd-rdr-{suffix}@t.local");
        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        reader.Dispose();
        anonymous.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task A_deleted_trip_leaves_every_surface_keeps_every_row_and_comes_back_whole()
    {
        var marker = $"zq{Guid.NewGuid():N}"[..14];
        var cave = await CreateCaveAsync($"Cave {marker}");
        var camp = await CreateCampAsync($"Camp {marker}");
        var trip = await CreateTripAsync($"Trip {marker}", [cave], withGuest: true);
        await JoinAsync(camp, trip);
        await MoveAsync(trip, "done");
        var picture = await UploadPictureAsync($"{marker}.jpg");
        await AttachAsync(picture, trip, "photoInterior");
        // A rule of the trip's own: the private-by-default alternative would leave the reader
        // nothing to lose, and what this row proves is that it survives.
        await GrantAsync(trip, readerId, AccessAction.Read);
        var feed = await MintFeedAsync();

        var sightings = Sightings(marker, trip, cave, camp, feed);

        // Before: every surface shows it. Without this half the two below prove nothing.
        foreach (var (name, sees) in sightings)
        {
            (await sees()).ShouldBeTrue($"the fixture never put the trip on: {name}");
        }

        var before = await ReadTripAsync(owner, trip);
        var rowsBefore = await RowCountsAsync(trip);
        rowsBefore.Participants.ShouldBeGreaterThanOrEqualTo(2);
        rowsBefore.LinkMembers.ShouldBeGreaterThan(0);
        rowsBefore.Attachments.ShouldBe(1);
        rowsBefore.AccessEntries.ShouldBe(1);
        rowsBefore.CampMemberships.ShouldBe(1);

        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{trip}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // After: no surface shows it, to its owner or to anybody.
        foreach (var (name, sees) in sightings)
        {
            (await sees()).ShouldBeFalse($"a deleted trip is still on: {name}");
        }

        // The row is marked, not removed, and says who and when.
        var row = await DeletedTrips.RowAsync(factory, trip);
        row.ShouldNotBeNull();
        row.DeletedAt.ShouldNotBeNull();
        row.DeletedByUserId.ShouldBe(ownerId);

        // And nothing hung on it was taken down: counted past the filter that hides them.
        (await RowCountsAsync(trip)).ShouldBe(rowsBefore);

        var restored = await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null);
        var restoredBody = await restored.Content.ReadAsStringAsync();
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, restoredBody);
        restored.Headers.ETag.ShouldNotBeNull("a restore answers the version it produced");

        // Back: every surface shows it again.
        foreach (var (name, sees) in sightings)
        {
            (await sees()).ShouldBeTrue($"a restored trip is missing from: {name}");
        }

        // The same trip, read through its own route: its roster, its caves and its camp are what
        // they were, because the delete never touched them.
        var answer = JsonDocument.Parse(restoredBody).RootElement;
        var after = await ReadTripAsync(owner, trip);
        foreach (var body in new[] { answer, after })
        {
            Names(body.GetProperty("participants")).ShouldBe(Names(before.GetProperty("participants")), ignoreOrder: true);
            Ids(body.GetProperty("caveIds")).ShouldBe([cave]);
            body.GetProperty("expeditionId").GetGuid().ShouldBe(camp);
            body.GetProperty("state").GetString().ShouldBe("done");
        }

        (await DeletedTrips.RowAsync(factory, trip))!.DeletedAt.ShouldBeNull();
        (await DeletedTrips.RowAsync(factory, trip))!.DeletedByUserId.ShouldBeNull();
        (await RowCountsAsync(trip)).ShouldBe(rowsBefore);

        // The version the restore answered is the one the page will edit against.
        var read = await owner.GetAsync($"/api/v1/trip-logs/{trip}");
        read.Headers.ETag.ShouldBe(restored.Headers.ETag);
    }

    /// <summary>
    /// Every route that reaches one trip answers not found once the trip is deleted, to its owner
    /// as to anybody. Each is asked before the delete as well, where it must reach the trip: a
    /// route that was already answering not found — a misspelt address, a missing right — would
    /// pass the second half for a reason that has nothing to do with deletion.
    /// </summary>
    [Fact]
    public async Task Every_route_onto_a_deleted_trip_answers_not_found()
    {
        var camp = await CreateCampAsync("A camp to try joining");
        var trip = await CreateTripAsync("A trip with every door tried", [], withGuest: false);

        var probes = new (string Name, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("read", () => owner.GetAsync($"/api/v1/trip-logs/{trip}")),
            // No precondition on either write: before the delete that is a refusal which says
            // the trip was found and the header was missing, and it changes nothing.
            ("update", () => owner.PutAsJsonAsync($"/api/v1/trip-logs/{trip}", TripBody("Renamed", []))),
            ("state", () => owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/state", new { state = "done" })),
            ("callout", () => owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/callout", new { })),
            ("stand down", () => owner.PostAsync($"/api/v1/trip-logs/{trip}/callout/stand-down", null)),
            ("write-up", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/report")),
            // The same document asked for with a picture of a map to go in it. Sent with no
            // picture, which the route answers with the plain document — so before the delete it
            // reaches the trip, and after it there is no trip for a picture to be put beside.
            ("write-up with a picture", () => owner.PostAsync($"/api/v1/trip-logs/{trip}/report/download", null)),
            ("invitations", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/invitations/")),
            ("checklist", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/checklist/")),
            ("tracking", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/")),
            ("tracking log", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/events")),
            ("follow links", () => owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking/shares/")),
            ("leave camp", () => owner.PutAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = (Guid?)null })),
            ("join camp", () => owner.PostAsJsonAsync(
                $"/api/v1/expeditions/{camp}/trips", new { tripLogId = trip })),
            ("files", () => owner.GetAsync($"/api/v1/attachments/?entityType=tripLog&entityId={trip}")),
            ("tags", () => owner.GetAsync($"/api/v1/taggings/?entityType=tripLog&entityId={trip}")),
            ("links", () => owner.GetAsync($"/api/v1/reslinks/for-target?type=tripLog&id={trip}")),
            ("history", () => owner.GetAsync($"/api/v1/history?entityType=TripLog&entityId={trip}")),
            ("rules", () => owner.GetAsync($"/api/v1/objects/tripLog/{trip}/access")),
            ("own rights", () => owner.GetAsync($"/api/v1/objects/tripLog/{trip}/effective-access")),
        };

        foreach (var (name, send) in probes)
        {
            var response = await send();
            response.StatusCode.ShouldNotBe(
                HttpStatusCode.NotFound,
                $"'{name}' never reached the trip: {await response.Content.ReadAsStringAsync()}");
        }

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var (name, send) in probes)
        {
            var response = await send();
            response.StatusCode.ShouldBe(
                HttpStatusCode.NotFound,
                $"'{name}' still reaches a deleted trip: {await response.Content.ReadAsStringAsync()}");
        }

        // A second delete, and a write carrying the wildcard precondition: neither finds a trip
        // to act on. The wildcard matters — it is the header that would match a row that is
        // merely hidden, if the version were read past the filter.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", TripBody("Renamed", [])))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The version a precondition is compared against is read by hand-written SQL, which the
    /// model's filter does not reach. A deleted trip has none, exactly as a trip that never
    /// existed has none, and gets one back when it is restored.
    /// </summary>
    [Fact]
    public async Task A_deleted_trip_has_no_version_until_it_is_restored()
    {
        var trip = await CreateTripAsync("A trip with a version", [], withGuest: false);
        (await VersionAsync(trip)).ShouldNotBeNull();
        (await VersionAsync(Guid.NewGuid())).ShouldBeNull();

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await VersionAsync(trip)).ShouldBeNull();

        (await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await VersionAsync(trip)).ShouldNotBeNull();
    }

    /// <summary>
    /// A link the trip merely joined keeps what it still relates and stops naming the trip; one
    /// that would be left with a single end is not shown at all; and both are as they were once
    /// the trip is back. The trip's own role link — what it did at a cave — is the third kind,
    /// and is covered where every surface is.
    /// </summary>
    [Fact]
    public async Task A_deleted_trips_links_read_as_though_the_trip_had_gone()
    {
        var first = await CreateCaveAsync("First cave of a link");
        var second = await CreateCaveAsync("Second cave of a link");
        var trip = await CreateTripAsync("A trip that joined two links", [], withGuest: false);

        var three = await CreateLinkAsync(("feature", first), ("feature", second), ("tripLog", trip));
        var two = await CreateLinkAsync(("feature", first), ("tripLog", trip));

        (await LinkIdsOnAsync(first)).ShouldBe([three, two], ignoreOrder: true);
        (await MemberTargetsAsync(three)).ShouldBe([first, second, trip], ignoreOrder: true);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The three-ended link still relates the two caves, and says nothing of the trip — not
        // even a bare row admitting something is there. The two-ended one has one end left and
        // is not a link any more, on the panel or by its own address.
        (await LinkIdsOnAsync(first)).ShouldBe([three]);
        (await MemberTargetsAsync(three)).ShouldBe([first, second], ignoreOrder: true);
        (await owner.GetAsync($"/api/v1/reslinks/{two}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/reslinks/{two}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await LinkIdsOnAsync(first)).ShouldBe([three, two], ignoreOrder: true);
        (await MemberTargetsAsync(three)).ShouldBe([first, second, trip], ignoreOrder: true);
        (await MemberTargetsAsync(two)).ShouldBe([first, trip], ignoreOrder: true);
    }

    /// <summary>
    /// What a restored trip's links mean: what they meant. A cave it named that was deleted while
    /// the trip was away is not shown on it, exactly as on a trip that was never deleted; and
    /// putting the cave back puts it back on the trip, because neither delete removed the link.
    /// </summary>
    [Fact]
    public async Task A_restored_trip_names_a_cave_deleted_meanwhile_exactly_as_a_live_trip_would()
    {
        var kept = await CreateCaveAsync("A cave that stays");
        var goes = await CreateCaveAsync("A cave deleted meanwhile");
        var restoredTrip = await CreateTripAsync("The trip that was away", [kept, goes], withGuest: false);
        var liveTrip = await CreateTripAsync("The trip that never left", [kept, goes], withGuest: false);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{restoredTrip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var caveGone = await owner.DeleteAsync($"/api/v1/caves/{goes}");
        caveGone.StatusCode.ShouldBe(HttpStatusCode.NoContent, await caveGone.Content.ReadAsStringAsync());
        (await owner.PostAsync($"/api/v1/trip-logs/{restoredTrip}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The two trips agree, which is the whole claim: being deleted and restored changed
        // nothing about how a missing cave reads.
        foreach (var trip in new[] { restoredTrip, liveTrip })
        {
            var body = await ReadTripAsync(owner, trip);
            Ids(body.GetProperty("caveIds")).ShouldBe([kept]);
        }

        // Nothing was removed from either: the membership naming the deleted cave is still
        // there, on both, waiting for the cave.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.FeatureId == goes)).ShouldBe(2);
    }

    /// <summary>
    /// The other half of what a restored trip's links mean. A photograph the trip held — pinned
    /// to it, and related to it and to a cave by a link — that was deleted and then removed for
    /// good while the trip was away took its own pin and its own end of the link with it: that
    /// removal cleans up after itself whether or not the trip it touches is showing. So the trip
    /// comes back without the photograph and with nothing left naming a document that is not
    /// there, exactly as a trip that was never deleted reads after the same removal.
    /// </summary>
    [Fact]
    public async Task A_restored_trip_has_lost_what_was_removed_for_good_meanwhile_and_nothing_still_names_it()
    {
        var cave = await CreateCaveAsync("A cave two trips photographed");
        var restoredTrip = await CreateTripAsync("The trip that was away", [], withGuest: false);
        var liveTrip = await CreateTripAsync("The trip that never left", [], withGuest: false);
        var file = await UploadPictureAsync($"removed-{Guid.NewGuid():N}.jpg");
        var document = await DocumentOfAsync(file);

        var links = new Dictionary<Guid, Guid>();
        foreach (var trip in new[] { restoredTrip, liveTrip })
        {
            await AttachAsync(file, trip, "photoInterior");
            links[trip] = await CreateLinkAsync(("document", document), ("feature", cave), ("tripLog", trip));
            (await PinCountAsync(trip)).ShouldBe(1);
            (await MemberTargetsAsync(links[trip])).ShouldBe([document, cave, trip], ignoreOrder: true);
        }

        (await owner.DeleteAsync($"/api/v1/trip-logs/{restoredTrip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The photograph is deleted, and its own window runs out, while the trip is deleted.
        var binned = await owner.PostAsJsonAsync(
            "/api/v1/photos/bulk", new { documentIds = new[] { document }, delete = true });
        var binnedBody = await binned.Content.ReadAsStringAsync();
        binned.StatusCode.ShouldBe(HttpStatusCode.OK, binnedBody);
        JsonDocument.Parse(binnedBody).RootElement.GetProperty("changed").EnumerateArray()
            .Select(id => id.GetGuid()).ShouldBe([document]);
        await RemoveDocumentForGoodAsync(document);

        (await owner.PostAsync($"/api/v1/trip-logs/{restoredTrip}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The two trips agree again: no pin, and a link that still relates the trip to the cave
        // and says nothing of a photograph.
        foreach (var trip in new[] { restoredTrip, liveTrip })
        {
            (await PinCountAsync(trip)).ShouldBe(0);
            (await MemberTargetsAsync(links[trip])).ShouldBe([cave, trip], ignoreOrder: true);
        }

        // And nothing in storage names the document any more, on either of them.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ResLinkMembers.AsNoTracking()
            .CountAsync(m => m.EntityType == AttachedEntityType.Document && m.EntityId == document)).ShouldBe(0);
        (await db.Attachments.AsNoTracking().CountAsync(a => a.FileId == file)).ShouldBe(0);
    }

    /// <summary>
    /// A picture filed under a camp is placed by the journeys the camp gathers: a guarded cave
    /// one of its trips names withholds the picture's own position from a reader who may not
    /// place that cave. Deleting that trip must not lift it. The camera was where it was, and a
    /// delete is something a person takes back — a position handed over while the trip happened
    /// to be deleted cannot be taken back with it.
    /// </summary>
    /// <remarks>
    /// The hiding of a deleted trip's place in its camp is right for every reader of the camp and
    /// wrong for this one question, which is why it is asked here from both sides: the same
    /// picture is handed over whole before the trip joins the camp, so what refuses it afterwards
    /// is the trip and nothing else about the arrangement.
    /// </remarks>
    [Fact]
    public async Task A_picture_filed_under_a_camp_stays_guarded_while_the_trip_that_guards_it_is_deleted()
    {
        var guarded = await CreateCaveAsync("A cave nobody may place", locationProtected: true);
        var camp = await CreateCampAsync("A camp with a picture filed under it");
        await GrantAsync(camp, readerId, AccessAction.Read, AccessDomain.Expeditions);
        var trip = await CreateTripAsync("The trip that went to the guarded cave", [guarded], withGuest: false);
        var picture = await UploadGeotaggedPictureAsync($"camp-{Guid.NewGuid():N}.jpg");
        await AttachAsync(picture, camp, "other", entityType: "expedition");

        // The fixture, both halves: the upload read a position out of the picture, and with no
        // trip in the camp nothing guards it — the reader is handed the bytes the camera wrote.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == picture)).Geom.ShouldNotBeNull();
        }

        (await IsHandedTheOriginalAsync(reader, picture)).ShouldBeTrue(
            "nothing places the picture yet, so the refusals below would prove nothing");

        await JoinAsync(camp, trip);
        (await IsHandedTheOriginalAsync(reader, picture)).ShouldBeFalse(
            "a trip of the camp names a guarded cave, and the picture is placed by it");
        (await IsHandedTheOriginalAsync(owner, picture)).ShouldBeTrue();

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The trip is in no camp as far as the camp's own page is concerned…
        (await JsonAsync(owner, $"/api/v1/trip-logs/?pageSize=500&expeditionId={camp}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
        // …and the picture is exactly as guarded as it was.
        (await IsHandedTheOriginalAsync(reader, picture)).ShouldBeFalse(
            "deleting the trip handed over the position it was guarding");
        (await IsHandedTheOriginalAsync(owner, picture)).ShouldBeTrue();

        (await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IsHandedTheOriginalAsync(reader, picture)).ShouldBeFalse();
    }

    // ---- what each surface is asked

    /// <summary>
    /// One question per surface, answered the same three times. Each asks whether this trip is
    /// there, by its identifier, by a word only its title holds, or by a count that is one with it
    /// and none without.
    /// </summary>
    private List<(string Name, Func<Task<bool>> Sees)> Sightings(
        string marker, Guid trip, Guid cave, Guid camp, string feedUrl) =>
    [
        ("its own page", async () =>
            (await owner.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode == HttpStatusCode.OK),
        ("its own page, for a reader let in by a rule on it", async () =>
            (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode == HttpStatusCode.OK),
        ("the list", () => MentionsAsync(owner, "/api/v1/trip-logs/?pageSize=500", marker)),
        ("the list of a reader let in by a rule on it", () =>
            MentionsAsync(reader, "/api/v1/trip-logs/?pageSize=500", marker)),
        ("the list narrowed to its cave", () =>
            MentionsAsync(owner, $"/api/v1/trip-logs/?pageSize=500&caveId={cave}", marker)),
        ("the list narrowed to its camp", () =>
            MentionsAsync(owner, $"/api/v1/trip-logs/?pageSize=500&expeditionId={camp}", marker)),
        ("the option counts", async () =>
            (await JsonAsync(owner, $"/api/v1/trip-logs/facets?search={marker}"))
                .GetProperty("matching").GetInt32() == 1),
        ("the grouped list", async () =>
            (await JsonAsync(owner, $"/api/v1/trip-logs/grouping?groupBy=year&search={marker}"))
                .GetProperty("matching").GetInt32() == 1),
        ("the list's totals", async () =>
            (await JsonAsync(owner, $"/api/v1/trip-logs/stats?search={marker}"))
                .GetProperty("matching").GetInt32() == 1),
        ("the exported sheet", () => ArchiveMentionsAsync($"/api/v1/trip-logs/export?search={marker}", marker)),
        ("my trips", () => MentionsAsync(owner, "/api/v1/trip-logs/mine?from=2000-01-01&pageSize=500", marker)),
        ("the map", () => MentionsAsync(owner, $"/api/v1/map/trip-logs?bbox={WorldBbox}", trip.ToString())),
        ("the calendar", () =>
            MentionsAsync(owner, "/api/v1/calendar?from=2026-07-01&to=2026-07-31", trip.ToString())),
        // The address is minted against the installation's public origin; the path is what this
        // host answers, and it answers it to a caller carrying nothing.
        ("the calendar feed", () =>
            MentionsAsync(anonymous, new Uri(feedUrl).PathAndQuery, trip.ToString())),
        ("search", () => MentionsAsync(owner, $"/api/v1/search?q={marker}", trip.ToString())),
        ("the dashboard", () => MentionsAsync(owner, "/api/v1/dashboard/summary", trip.ToString())),
        ("the cave's trip count", async () =>
            (await JsonAsync(owner, $"/api/v1/caves/{cave}/summary")).GetProperty("tripLogCount").GetInt32() == 1),
        ("the cave's links", async () =>
            (await JsonAsync(owner, $"/api/v1/reslinks/for-target?type=feature&id={cave}"))
                .GetProperty("totalItems").GetInt32() == 1),
        ("the cave's statistics", async () =>
            (await JsonAsync(owner, $"/api/v1/stats/caves/{cave}")).GetProperty("trips").GetInt32() == 1),
        ("a person's statistics", async () =>
            (await JsonAsync(owner, $"/api/v1/stats/cavers/{ownerCaver}")).GetProperty("trips").GetInt32() == 1),
        ("the camp's statistics", async () =>
            (await JsonAsync(owner, $"/api/v1/stats/expeditions/{camp}")).GetProperty("trips").GetInt32() == 1),
        ("the camp's map", () => MentionsAsync(owner, $"/api/v1/expeditions/{camp}/map", trip.ToString())),
        // By the trip's own title: the camp and the cave carry the marker too, and the write-up
        // names both whether or not the trip is in it.
        ("the camp's write-up", () => ArchiveMentionsAsync($"/api/v1/expeditions/{camp}/report", $"Trip {marker}")),
        ("the photographs of the trip", async () =>
            (await JsonAsync(owner, $"/api/v1/photos?pageSize=100&tripLogId={trip}"))
                .GetProperty("totalItems").GetInt32() == 1),
        ("the photographs of the camp", async () =>
            (await JsonAsync(owner, $"/api/v1/photos?pageSize=100&expeditionId={camp}"))
                .GetProperty("totalItems").GetInt32() == 1),
        ("its history", async () =>
            (await owner.GetAsync($"/api/v1/history?entityType=TripLog&entityId={trip}")).StatusCode
                == HttpStatusCode.OK),
    ];

    private static async Task<bool> MentionsAsync(HttpClient client, string url, string needle)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return body.Contains(needle, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    /// Whether a generated file names the trip — the exported workbook, or a camp's write-up.
    /// Both are archives of markup, so the words are looked for in each part rather than in the
    /// bytes as they travel.
    /// </summary>
    private async Task<bool> ArchiveMentionsAsync(string url, string needle)
    {
        var response = await owner.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var part = new StreamReader(entry.Open(), Encoding.UTF8);
            if ((await part.ReadToEndAsync()).Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ---- fixtures

    private async Task<Guid> CreateCaveAsync(string name, bool locationProtected = false)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..Math.Min(60, name.Length + 33)],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateCampAsync(string name)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = "2026-07-18",
            endDate = "2026-08-01",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(created);
    }

    private object TripBody(string title, Guid[] caveIds, bool withGuest = false) => new
    {
        title,
        tripDate = "2026-07-20",
        geom = new { type = "Point", coordinates = new[] { 25.61, 45.55 } },
        caveIds,
        participants = withGuest
            ? new object[] { new { caverId = ownerCaver }, new { newCaverName = $"Guest {suffix} {Guid.NewGuid():N}"[..30] } }
            : [new { caverId = ownerCaver }],
        visibility = "private",
        hadIncident = false,
    };

    private async Task<Guid> CreateTripAsync(string title, Guid[] caveIds, bool withGuest)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody(title, caveIds, withGuest));
        return await CreatedIdAsync(created);
    }

    private async Task JoinAsync(Guid camp, Guid trip)
    {
        var joined = await owner.PostAsJsonAsync($"/api/v1/expeditions/{camp}/trips", new { tripLogId = trip });
        joined.StatusCode.ShouldBe(HttpStatusCode.OK, await joined.Content.ReadAsStringAsync());
    }

    private async Task MoveAsync(Guid trip, string state)
    {
        var moved = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{trip}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task<Guid> UploadPictureAsync(string name)
    {
        using var picture = new MagickImage(MagickColors.SlateGray, 320, 240);
        return await UploadJpegAsync(picture, name);
    }

    /// <summary>A picture whose camera wrote down where it was, which is what a guard withholds.</summary>
    private async Task<Guid> UploadGeotaggedPictureAsync(string name)
    {
        using var picture = new MagickImage(MagickColors.SlateGray, 320, 240);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, new Rational[] { new(45u), new(31u), new(52u) });
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, new Rational[] { new(25u), new(26u), new(49u) });
        picture.SetProfile(exif);
        return await UploadJpegAsync(picture, name);
    }

    private async Task<Guid> UploadJpegAsync(MagickImage picture, string name)
    {
        var content = new ByteArrayContent(picture.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", name } };
        var uploaded = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        return await CreatedIdAsync(uploaded);
    }

    private async Task AttachAsync(Guid fileId, Guid target, string role, string entityType = "tripLog")
    {
        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId = target,
            role,
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Whether this caller is handed the bytes the camera wrote, position and all. Asked of the
    /// answer that says so and of the address it points at, which must agree: a control that
    /// says no over a link that still serves the file would be the refusal in name only.
    /// </summary>
    private static async Task<bool> IsHandedTheOriginalAsync(HttpClient client, Guid fileId)
    {
        var file = await JsonAsync(client, $"/api/v1/files/{fileId}");
        var offered = file.GetProperty("mayDownloadOriginal").GetBoolean();
        var served = await client.GetAsync(file.GetProperty("contentUrl").GetString());
        served.StatusCode.ShouldBe(
            offered ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            "what the file says of its original and what its address answers disagree");
        return offered;
    }

    /// <summary>
    /// A rule on one row, written straight to the table: what is under test is that the row
    /// survives a delete and still decides after a restore, not the route that authors it.
    /// </summary>
    private async Task GrantAsync(
        Guid target, Guid userId, AccessAction actions, AccessDomain domain = AccessDomain.TripLogs)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = target,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>How many files the trip's own page lists as pinned to it.</summary>
    private async Task<int> PinCountAsync(Guid trip) =>
        (await JsonAsync(owner, $"/api/v1/attachments/?entityType=tripLog&entityId={trip}")).GetArrayLength();

    private async Task<Guid> DocumentOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        return (await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId)).DocumentId;
    }

    /// <summary>
    /// Takes a deleted document past its own window and runs the pass that removes such
    /// documents — backdated rather than waited for, as the trips' own window is.
    /// </summary>
    private async Task RemoveDocumentForGoodAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var longAgo = DateTimeOffset.UtcNow - SoftDeleteRules.DefaultRetention - TimeSpan.FromDays(1);
        (await db.Documents.IgnoreQueryFilters()
            .Where(d => d.Id == documentId && d.DeletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAt, longAgo))).ShouldBe(1);

        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.DocumentPurge);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.DocumentPurge }, CancellationToken.None);

        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == documentId)).ShouldBeFalse();
    }

    private async Task<string> MintFeedAsync()
    {
        var minted = await owner.PostAsJsonAsync("/api/v1/me/calendar-feeds", new { label = "Phone" });
        var payload = await minted.Content.ReadAsStringAsync();
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("url").GetString()!;
    }

    private async Task<Guid> CreateLinkAsync(params (string Type, Guid Id)[] ends)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = "A link somebody made by hand",
            members = ends.Select((end, index) => new
            {
                targetType = end.Type,
                targetId = end.Id,
                isMain = false,
                sortOrder = index,
                note = (string?)null,
                anchorKind = "whole",
                anchor = (object?)null,
                anchorFileId = (Guid?)null,
            }).ToArray(),
        });
        return await CreatedIdAsync(created);
    }

    private async Task<List<Guid>> LinkIdsOnAsync(Guid cave) =>
        [.. (await JsonAsync(owner, $"/api/v1/reslinks/for-target?type=feature&id={cave}&pageSize=100"))
            .GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())];

    private async Task<List<Guid>> MemberTargetsAsync(Guid link) =>
        [.. (await JsonAsync(owner, $"/api/v1/reslinks/{link}"))
            .GetProperty("members").EnumerateArray().Select(m => m.GetProperty("targetId").GetGuid())];

    private static async Task<JsonElement> ReadTripAsync(HttpClient client, Guid trip) =>
        await JsonAsync(client, $"/api/v1/trip-logs/{trip}");

    private static List<string?> Names(JsonElement people) =>
        [.. people.EnumerateArray().Select(p => p.GetProperty("name").GetString())];

    private static List<Guid> Ids(JsonElement ids) => [.. ids.EnumerateArray().Select(i => i.GetGuid())];

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<long?> VersionAsync(Guid trip)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await ConcurrencySql.VersionAsync(db, VersionedTable.TripLogs, trip, CancellationToken.None);
    }

    /// <summary>
    /// How many rows hang on a trip, counted past the filter that hides a deleted trip's rows —
    /// the only reading in which "nothing was removed" can be seen at all.
    /// </summary>
    private async Task<TripRows> RowCountsAsync(Guid trip)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return new TripRows(
            await db.TripLogParticipants.IgnoreQueryFilters().CountAsync(p => p.TripLogId == trip),
            await db.ResLinkMembers.CountAsync(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == trip),
            await db.Attachments.CountAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == trip),
            await db.AccessEntries.CountAsync(e => e.Domain == AccessDomain.TripLogs && e.ScopeId == trip),
            await db.ExpeditionTrips.IgnoreQueryFilters().CountAsync(m => m.TripLogId == trip));
    }

    private sealed record TripRows(
        int Participants, int LinkMembers, int Attachments, int AccessEntries, int CampMemberships);
}
