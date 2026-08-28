// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The sync protocol played from the device's side against a real database: a first download,
/// a resume from the middle of the stream, the record of what has gone, and the boundary of
/// what one selection may reach at all.
/// </summary>
/// <remarks>
/// Everything here is asserted with a positive on the same call as the negative. A download
/// that hands over nothing satisfies every "must not appear" assertion ever written, so each
/// exclusion test also names a row that must arrive — otherwise a fixture that silently built
/// nothing would read as a working refusal.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SyncProtocolTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private Guid readerId;
    private long caveTypeId;
    private long cavePlaceTypeId;
    private string marker = null!;

    public SyncProtocolTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sp-own-{marker}@t.local");

        // A Viewer, deliberately. The seeded Editors group reads past visibility across the whole
        // feature domain, so an Editor would read the rows the protection tests below need refused
        // and every one of them would pass for the wrong reason. This account owns none of the rows
        // either: a row's own owner always sees it exactly, whatever its protection says.
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sp-read-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            cavePlaceTypeId = await db.FeatureTypes
                .Where(t => t.Code == "cave_place").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sp-own-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sp-read-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// No route in this slice is on the anonymous allow-list, and this asks the application
    /// itself rather than a list written by hand: a route added later is covered the day it is
    /// added, which a hand-kept list cannot promise.
    /// </summary>
    [Fact]
    public async Task Every_sync_route_refuses_a_caller_with_no_token()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/sync", StringComparison.Ordinal) == true)
            .ToList();

        // If this ever reads zero the test below proves nothing, so it is stated as a fact.
        routes.Count.ShouldBeGreaterThanOrEqualTo(7);

        foreach (var route in routes)
        {
            var method = route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0];
            var path = FillRoute(route.RoutePattern.RawText!);
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT")
            {
                request.Content = JsonContent.Create(new { });
            }

            var response = await anonymous.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
        }
    }

    [Fact]
    public async Task A_first_download_hands_over_the_set_its_settings_and_everything_under_its_roots()
    {
        var cave = await CreateCaveAsync($"Sync cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Sync place {marker}", 25.441, 45.531);
        var set = await CreateSetAsync([cave], new { pciStrategy = "ro-default", digits = 4 });

        var page = await DownloadAsync(set);

        page.GetProperty("setRevision").GetInt64().ShouldBe(1);
        page.GetProperty("settings").GetProperty("pciStrategy").GetString().ShouldBe("ro-default");
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        page.GetProperty("nextCursor").GetString().ShouldNotBeNullOrWhiteSpace();
        page.GetProperty("tombstones").EnumerateArray().ShouldBeEmpty();

        var features = Features(page);
        features.Keys.ShouldBe([cave, place], ignoreOrder: true);

        // The root arrives as a cave; the place arrives under it, named by the kind's stable code
        // rather than by an identifier that means something different on the next installation.
        features[cave].GetProperty("kind").GetString().ShouldBe("cave");
        features[place].GetProperty("featureTypeCode").GetString().ShouldBe("cave_place");
        features[place].GetProperty("parents").EnumerateArray().Single()
            .GetProperty("parentId").GetGuid().ShouldBe(cave);
        features[place].GetProperty("geometry").GetProperty("type").GetString().ShouldBe("Point");

        // The row's revision on this server is its update stamp, and it is what a device sends
        // back when it writes the row. The device's own clock travels beside it as provenance and
        // is null here, because this row was made through the web interface: it is carried so two
        // devices editing offline can compare their versions with each other, and never so that a
        // phone's clock can decide which version this server keeps.
        features[place].GetProperty("updatedAt").GetDateTimeOffset().ShouldBeGreaterThan(default);
        features[place].GetProperty("clientUpdatedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        await RecordAsync("07-download-first-page", owner, set, [(cave, "cave"), (place, "place")]);
    }

    [Fact]
    public async Task A_download_resumed_from_its_cursor_repeats_no_row_and_skips_none()
    {
        var cave = await CreateCaveAsync($"Cursor cave {marker}");
        var first = await CreatePlaceAsync(cave, $"Cursor one {marker}", 25.442, 45.532);
        var second = await CreatePlaceAsync(cave, $"Cursor two {marker}", 25.443, 45.533);
        var third = await CreatePlaceAsync(cave, $"Cursor three {marker}", 25.444, 45.534);
        var set = await CreateSetAsync([cave]);

        // One row at a time, which is the shape a flaky connection produces anyway: every page
        // but the last must say there is more, and the walk must terminate.
        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var body = await DownloadAsync(set, cursor, pageSize: 1);
            seen.AddRange(Features(body).Keys);
            cursor = body.GetProperty("nextCursor").GetString();
            if (!body.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        seen.ShouldBe([cave, first, second, third], ignoreOrder: true);
        seen.Distinct().Count().ShouldBe(seen.Count);

        // Asking again from the end is not a re-download: the device is level with the server.
        var caughtUp = await DownloadAsync(set, cursor);
        Features(caughtUp).ShouldBeEmpty();
        caughtUp.GetProperty("hasMore").GetBoolean().ShouldBeFalse();

        // A resume position this server never issued is refused rather than read as "start
        // again", which would hand a device a full re-download it could not tell from an
        // incremental one.
        var bad = await owner.GetAsync($"/api/v1/sync/sets/{set}/download?cursor=not-a-cursor");
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.cursor_invalid");

        // Recorded from a fresh first page and the page that resumes from it, so the file shows a
        // resume doing its work rather than whichever page happened to come second above.
        var opening = await DownloadAsync(set, pageSize: 1);
        await RecordAsync(
            "08-download-cursor-restart",
            owner,
            set,
            [(cave, "cave"), (first, "place-one"), (second, "place-two"), (third, "place-three")],
            cursor: opening.GetProperty("nextCursor").GetString(),
            pageSize: 1);
    }

    /// <summary>
    /// A resume position is a position within one selection, and it stops meaning anything when
    /// the selection moves. Adding a cave to a set touches no feature row, so nothing under the
    /// new cave is ever after a caught-up device's watermark: without this refusal the device
    /// would ask with its stored cursor, be told there is nothing new, and never see the cave it
    /// had just asked to carry.
    /// </summary>
    [Fact]
    public async Task A_cursor_issued_before_the_selection_changed_is_refused_rather_than_answered_short()
    {
        var carried = await CreateCaveAsync($"Revision cave {marker}");
        var added = await CreateCaveAsync($"Added cave {marker}");
        var addedPlace = await CreatePlaceAsync(added, $"Added place {marker}", 25.471, 45.561);
        var set = await CreateSetAsync([carried]);

        // Level with the server, and holding the watermark that says so.
        var caughtUp = await DownloadAsync(set);
        Features(caughtUp).Keys.ShouldBe([carried], ignoreOrder: true);
        caughtUp.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        var cursor = caughtUp.GetProperty("nextCursor").GetString();

        // The same cursor still works while the selection has not moved, so the refusal below is
        // about the change and not about the cursor being unusable in the first place.
        (await DownloadAsync(set, cursor)).GetProperty("hasMore").GetBoolean().ShouldBeFalse();

        await ReplaceRootsAsync(set, [carried, added]);

        var stale = await owner.GetAsync(
            $"/api/v1/sync/sets/{set}/download?cursor={Uri.EscapeDataString(cursor!)}");
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.cursor_stale");

        // And the answer the device is told to give: drop the position, read the set again, and
        // the cave it added is there — along with what is inside it.
        var restarted = await DownloadAsync(set);
        Features(restarted).Keys.ShouldBe([carried, added, addedPlace], ignoreOrder: true);

        // The position that restart hands back is good against the new revision, so a device is
        // not left refusing its own cursor for ever.
        var resumed = await DownloadAsync(set, restarted.GetProperty("nextCursor").GetString());
        Features(resumed).ShouldBeEmpty();
    }

    /// <summary>
    /// A sync set has exactly one reader. The download route is the only one in this slice that
    /// was added after the rule was written down, so its ownership arm is asserted here rather
    /// than assumed to be covered by the tests on the set's own routes.
    /// </summary>
    [Fact]
    public async Task A_download_of_somebody_elses_set_is_answered_like_one_that_is_not_there()
    {
        var cave = await CreateCaveAsync($"Owned cave {marker}", visibility: "authenticated");
        var readersOwn = await CreateSetAsync(reader, [cave]);

        var refused = await owner.GetAsync($"/api/v1/sync/sets/{readersOwn}/download");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // The set is real and downloadable by the account that owns it, so the refusal above
        // refused something rather than describing a set that was never created.
        Features(await DownloadAsync(reader, readersOwn)).Keys.ShouldBe([cave], ignoreOrder: true);
    }

    [Fact]
    public async Task A_cave_the_selection_does_not_name_is_never_handed_over()
    {
        var carried = await CreateCaveAsync($"Carried cave {marker}");
        var left = await CreateCaveAsync($"Left cave {marker}");
        var leftPlace = await CreatePlaceAsync(left, $"Left place {marker}", 25.445, 45.535);
        var set = await CreateSetAsync([carried]);

        var features = Features(await DownloadAsync(set));

        // The positive first: the fixture really did produce readable rows, so the absences
        // below are a boundary and not an empty answer.
        features.Keys.ShouldContain(carried);
        features.Keys.ShouldNotContain(left);
        features.Keys.ShouldNotContain(leftPlace);

        // Both caves are the same caller's and both are readable — membership is what separates
        // them, so widening the set reaches the second one without any other change.
        await ReplaceRootsAsync(set, [carried, left]);
        Features(await DownloadAsync(set)).Keys.ShouldBe([carried, left, leftPlace], ignoreOrder: true);
    }

    [Fact]
    public async Task A_deleted_row_arrives_as_a_stub_carrying_its_identifier_and_the_moment_it_went()
    {
        var cave = await CreateCaveAsync($"Tombstone cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Tombstone place {marker}", 25.446, 45.536);
        var set = await CreateSetAsync([cave]);

        var beforeDelete = await DownloadAsync(set);
        Features(beforeDelete).Keys.ShouldContain(place);
        var cursor = beforeDelete.GetProperty("nextCursor").GetString();

        (await owner.DeleteAsync($"/api/v1/features/{place}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // The delete reaches a device that had already caught up. A cursor keyed on the update
        // time alone would not: a delete is stamped without touching it, so the row would sit
        // behind the watermark for ever and the device would keep the place it should drop.
        var after = await DownloadAsync(set, cursor);
        var tombstone = after.GetProperty("tombstones").EnumerateArray().Single();
        tombstone.GetProperty("id").GetGuid().ShouldBe(place);
        tombstone.GetProperty("deletedAt").GetDateTimeOffset().ShouldBeGreaterThan(default);
        Features(after).Keys.ShouldNotContain(place);

        // The stub says nothing beyond that. Asserted as the whole set of keys, because the
        // failure worth catching is a field added to the record later and nobody noticing that
        // a name or a position now travels with every delete.
        tombstone.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "deletedAt"], ignoreOrder: true);

        await RecordAsync(
            "09-download-tombstones", owner, set, [(cave, "cave"), (place, "place")], cursor: cursor);

        // And a device starting fresh still gets the cave, so the tombstone rode a working
        // stream rather than an empty one.
        Features(await DownloadAsync(set)).Keys.ShouldBe([cave], ignoreOrder: true);
    }

    [Fact]
    public async Task The_settings_document_travels_with_a_revision_that_moves_only_on_a_change()
    {
        var cave = await CreateCaveAsync($"Settings cave {marker}");
        var set = await CreateSetAsync([cave], new { pciStrategy = "ro-default", digits = 4 });

        (await DownloadAsync(set)).GetProperty("setRevision").GetInt64().ShouldBe(1);

        // Re-sending the same document is not an edit — the device's copy has not gone stale.
        await ReplaceRootsAsync(set, [cave], new { pciStrategy = "ro-default", digits = 4 });
        (await DownloadAsync(set)).GetProperty("setRevision").GetInt64().ShouldBe(1);

        // A different document is, and the revision the download carries moves with it. The
        // server stores it verbatim and forms no opinion about what any of it means.
        await ReplaceRootsAsync(set, [cave], new { pciStrategy = "custom", digits = 6, qcri = new { mode = "salted" } });
        var changed = await DownloadAsync(set);
        changed.GetProperty("setRevision").GetInt64().ShouldBe(2);
        changed.GetProperty("settings").GetProperty("digits").GetInt32().ShouldBe(6);
        changed.GetProperty("settings").GetProperty("qcri").GetProperty("mode").GetString().ShouldBe("salted");
    }

    /// <summary>
    /// The shape the per-row rule exists for, and the one a per-cave filter would ship: a cave
    /// this caller may place exactly, with an independently protected point inside it. A filter
    /// that asked "may this caller see the cave?" would answer yes and hand the place over.
    /// </summary>
    [Fact]
    public async Task A_place_that_is_its_own_protection_root_is_absent_while_the_cave_around_it_arrives()
    {
        var cave = await CreateCaveAsync($"Withhold cave {marker}", visibility: "authenticated");
        var open = await CreatePlaceAsync(
            cave, $"Open place {marker}", 25.451, 45.541, "authenticated");
        var guarded = await CreatePlaceAsync(
            cave, $"Guarded place {marker}", 25.452, 45.542, "authenticated", locationProtected: true);

        var set = await CreateSetAsync(reader, [cave]);
        var page = await DownloadAsync(reader, set);
        var features = Features(page);

        // The refusal, and on the same page the proof that this fixture produces rows at all:
        // an empty answer would satisfy the exclusion below without refusing anything.
        features.Keys.ShouldBe([cave, open], ignoreOrder: true);
        features.Keys.ShouldNotContain(guarded);

        // What did arrive is at its surveyed position rather than a snapped one. This channel
        // has no approximate geometry in it at all, so a coordinate that arrives is the real one.
        features[open].GetProperty("geometry").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(25.451, 1e-9);
        features[open].GetProperty("locationProtected").GetBoolean().ShouldBeFalse();

        // The withheld row is absent, not reported gone: a device must never read a refusal as
        // a deletion and drop a row it is still entitled to hold from an earlier grant.
        page.GetProperty("tombstones").EnumerateArray().ShouldBeEmpty();

        // Recorded before the grant below, so the committed file is the refusal itself: two rows
        // where the database holds three, and no third entry anywhere in it saying one was kept
        // back. Whoever writes the device reads this file and sees that a withheld row is simply
        // not there, rather than inferring it from prose.
        await RecordAsync(
            "10-download-protected-withheld",
            reader,
            set,
            [(cave, "cave"), (open, "place-readable"), (guarded, "place-withheld")]);

        // And the refusal is alive: a grant on the place — the protection root itself, not the
        // cave, which never guarded anything — hands the same row over at full precision.
        await GrantExactViewAsync(guarded);
        var after = Features(await DownloadAsync(reader, set));
        after.Keys.ShouldBe([cave, open, guarded], ignoreOrder: true);
        after[guarded].GetProperty("geometry").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(25.452, 1e-9);
        after[guarded].GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// Whether a position is guarded is carried on the row, not left for the device to rebuild
    /// from the containment edges it was given — because those are filtered to parents the caller
    /// may read and are therefore not always a complete ancestry.
    /// </summary>
    /// <remarks>
    /// The two flags are different questions and this is the shape where they part: a place that
    /// is not itself a protection root, inside a cave that is. A device writing this row to
    /// cleartext storage and re-sharing it would otherwise have nothing anywhere in the payload
    /// telling it the position is guarded at all.
    /// </remarks>
    [Fact]
    public async Task A_row_inside_a_protected_cave_says_its_position_is_guarded_without_being_a_root_itself()
    {
        var cave = await CreateCaveAsync($"Effective cave {marker}", locationProtected: true);
        var inside = await CreatePlaceAsync(cave, $"Effective place {marker}", 25.491, 45.581);
        var elsewhere = await CreateCaveAsync($"Unguarded cave {marker}");
        var set = await CreateSetAsync([cave, elsewhere]);

        var features = Features(await DownloadAsync(set));
        features.Keys.ShouldBe([cave, inside, elsewhere], ignoreOrder: true);

        // The place is not a root and says so, and still reports the position as guarded.
        features[inside].GetProperty("locationProtected").GetBoolean().ShouldBeFalse();
        features[inside].GetProperty("protectedEffective").GetBoolean().ShouldBeTrue();

        // The root itself is both.
        features[cave].GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        features[cave].GetProperty("protectedEffective").GetBoolean().ShouldBeTrue();

        // And a row with nothing guarding it is neither, so the field is answering the question
        // rather than being true of everything this channel emits.
        features[elsewhere].GetProperty("protectedEffective").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The withheld row is taken out of the query before the page is ordered, cut or counted, and
    /// this is what says so: a walk one row at a time, with a guarded row sitting in the middle of
    /// the change order. Every page carries a full row, the walk ends, and nothing is skipped.
    /// </summary>
    /// <remarks>
    /// Excluding after the cut instead would still hand over the same rows in the end, and every
    /// other test here would still pass — but the page the guarded row fell on would come back
    /// empty, telling the device exactly how many rows it was not allowed to have, and a device
    /// stepping its cursor over that page would step over the row behind it too.
    /// </remarks>
    [Fact]
    public async Task A_page_at_a_time_walk_past_a_withheld_row_keeps_its_pages_full_and_skips_nothing()
    {
        var cave = await CreateCaveAsync($"Paged cave {marker}", visibility: "authenticated");
        var before = await CreatePlaceAsync(
            cave, $"Paged before {marker}", 25.481, 45.571, "authenticated");
        var guarded = await CreatePlaceAsync(
            cave, $"Paged guarded {marker}", 25.482, 45.572, "authenticated", locationProtected: true);
        var after = await CreatePlaceAsync(
            cave, $"Paged after {marker}", 25.483, 45.573, "authenticated");

        var set = await CreateSetAsync(reader, [cave]);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        for (; pages < 10; pages++)
        {
            var body = await DownloadAsync(reader, set, cursor, pageSize: 1);
            var rows = Features(body);

            // A full page, not a shortened one. This is the assertion the ordering of the
            // exclusion actually turns on.
            rows.Count.ShouldBe(1, $"page {pages} came back short");
            seen.AddRange(rows.Keys);
            cursor = body.GetProperty("nextCursor").GetString();
            if (!body.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        // Three readable rows, each once, and the walk terminated rather than running out of
        // attempts — an exclusion applied after the cut would produce neither.
        pages.ShouldBeLessThan(9);
        seen.ShouldBe([cave, before, after], ignoreOrder: true);
        seen.Distinct().Count().ShouldBe(seen.Count);
        seen.ShouldNotContain(guarded);
    }

    /// <summary>
    /// Protection and visibility are inherited along containment and along nothing else, so a
    /// place with nothing containing it inherits from nothing: it would be unprotected however
    /// the cave it belongs to is guarded, and handed to every reader at full precision. The
    /// server refuses to store that shape at all, which is why the rule above only ever has to
    /// reason about places that have a cave above them.
    /// </summary>
    [Fact]
    public async Task A_place_with_nothing_containing_it_is_refused_and_the_contained_one_is_withheld()
    {
        var rootless = await CreatePlaceResponseAsync(
            [], $"Rootless place {marker}", 25.461, 45.551, "authenticated", locationProtected: true);
        rootless.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await rootless.Content.ReadAsStringAsync());
        (await rootless.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("feature.parent_required");

        // The representable equivalent — the same place, with a cave containing it — is withheld
        // from a caller who may not place it, while the cave it hangs under still arrives.
        var cave = await CreateCaveAsync($"Contained cave {marker}", visibility: "authenticated");
        var attached = await CreatePlaceAsync(
            cave, $"Contained place {marker}", 25.461, 45.551, "authenticated", locationProtected: true);

        var readerSet = await CreateSetAsync(reader, [cave]);
        Features(await DownloadAsync(reader, readerSet)).Keys.ShouldBe([cave], ignoreOrder: true);

        // The row exists and is downloadable — the account entitled to it gets it on its own
        // selection — so the line above refused something rather than describing an empty tree.
        var ownerSet = await CreateSetAsync([cave]);
        Features(await DownloadAsync(ownerSet)).Keys.ShouldBe([cave, attached], ignoreOrder: true);
    }

    /// <summary>
    /// A protected row's deletion still reaches a device, and can, because a stub is an
    /// identifier and a time and those locate nothing.
    /// </summary>
    [Fact]
    public async Task A_deleted_protected_cave_leaves_a_stub_carrying_nothing_but_identifier_and_time()
    {
        var guarded = await CreateCaveAsync(
            $"Guarded cave {marker}", visibility: "authenticated", locationProtected: true);
        var open = await CreateCaveAsync($"Open cave {marker}", visibility: "authenticated");
        var set = await CreateSetAsync(reader, [guarded, open]);

        // The live half refuses the guarded cave, and says so by handing over the other one.
        var first = await DownloadAsync(reader, set);
        Features(first).Keys.ShouldBe([open], ignoreOrder: true);
        var cursor = first.GetProperty("nextCursor").GetString();

        (await owner.DeleteAsync($"/api/v1/caves/{guarded}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // The delete reaches the device even though the row itself never could. A device that
        // held this cave from before it was guarded would otherwise keep it for ever and put it
        // back on its next upload, which is the one failure a tombstone exists to prevent.
        var after = await DownloadAsync(reader, set, cursor);
        var stub = after.GetProperty("tombstones").EnumerateArray().Single();
        stub.GetProperty("id").GetGuid().ShouldBe(guarded);
        stub.GetProperty("deletedAt").GetDateTimeOffset().ShouldBeGreaterThan(default);
        Features(after).Keys.ShouldNotContain(guarded);

        // The whole of the stub, asserted as its set of keys rather than field by field: the
        // failure worth catching is a field added to the record later, so that a name or a
        // position starts travelling with every delete and nobody notices.
        stub.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "deletedAt"], ignoreOrder: true);

        // A device starting fresh still gets the cave it may read, so the stub rode a working
        // stream rather than one that had stopped producing anything.
        Features(await DownloadAsync(reader, set)).Keys.ShouldBe([open], ignoreOrder: true);
    }

    private static string FillRoute(string pattern)
    {
        var segments = pattern.Split('/')
            .Select(s => s.StartsWith('{') ? Guid.NewGuid().ToString() : s);
        return string.Join('/', segments);
    }

    private static Dictionary<Guid, JsonElement> Features(JsonElement page) =>
        page.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid(), f => f);

    private Task<JsonElement> DownloadAsync(Guid set, string? cursor = null, int? pageSize = null) =>
        DownloadAsync(owner, set, cursor, pageSize);

    private static async Task<JsonElement> DownloadAsync(
        HttpClient client, Guid set, string? cursor = null, int? pageSize = null)
    {
        var (response, _) = await DownloadResponseAsync(client, set, cursor, pageSize);
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    /// <summary>
    /// The same request, kept whole so a recording can be taken from it. The recorded files are the
    /// specification the application on the other side of this protocol is written against, so they
    /// are taken from a real exchange this suite already asserts about rather than composed by hand.
    /// </summary>
    private static async Task<(HttpResponseMessage Response, string Url)> DownloadResponseAsync(
        HttpClient client, Guid set, string? cursor = null, int? pageSize = null)
    {
        var query = new List<string>();
        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (pageSize is { } size)
        {
            query.Add($"pageSize={size}");
        }

        var url = $"/api/v1/sync/sets/{set}/download"
            + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (response, url);
    }

    /// <summary>
    /// Repeats one exchange and holds the result against the committed contract files. Named
    /// identifiers read as themselves in the recording; everything the server stamps from its own
    /// clock, its own counters or the position of the data is replaced before the comparison, or
    /// the files would differ on every run and guard nothing.
    /// </summary>
    private async Task RecordAsync(
        string caseName,
        HttpClient client,
        Guid set,
        (Guid Id, string Label)[] names,
        string? cursor = null,
        int? pageSize = null)
    {
        var fixture = new ContractFixture().Literal(marker, "marker").Name(set, "set");
        if (cursor is not null)
        {
            // In the request line as well as in the body: it is the same opaque token, and a
            // recording that pinned its bytes would be re-written by any change to the data.
            fixture.Literal(Uri.EscapeDataString(cursor), "cursor");
        }

        foreach (var (id, label) in names)
        {
            fixture.Name(id, label);
        }

        var (response, url) = await DownloadResponseAsync(client, set, cursor, pageSize);
        using (response)
        {
            await fixture.AssertAsync(caseName, $"GET {url}", response);
        }
    }

    private Task<Guid> CreateSetAsync(IReadOnlyList<Guid> roots, object? settings = null) =>
        CreateSetAsync(owner, roots, settings);

    private async Task<Guid> CreateSetAsync(
        HttpClient client, IReadOnlyList<Guid> roots, object? settings = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = settings ?? new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task ReplaceRootsAsync(Guid set, IReadOnlyList<Guid> roots, object? settings = null)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/sync/sets/{set}", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = settings ?? new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(
        string name, string visibility = "private", bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePlaceAsync(
        Guid parent,
        string name,
        double lon,
        double lat,
        string visibility = "private",
        bool locationProtected = false)
    {
        var response = await CreatePlaceResponseAsync(
            [new { parentId = parent, isPrimary = true }], name, lon, lat, visibility, locationProtected);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CreatePlaceResponseAsync(
        object[] parents,
        string name,
        double lon,
        double lat,
        string visibility = "private",
        bool locationProtected = false) =>
        owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = cavePlaceTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility,
            locationProtected,
            parents,
        });

    /// <summary>
    /// Lets the reader see one protection root exactly. Every refusal below is re-asked after a
    /// grant like this one, so a fixture that produced no row at all cannot pass as a refusal.
    /// </summary>
    private async Task GrantExactViewAsync(Guid featureId)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
