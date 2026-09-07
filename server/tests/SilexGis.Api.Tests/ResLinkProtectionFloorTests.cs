// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SilexGis.Api.Tests;

/// <summary>
/// The disclosure floor of resource links, stated over any number of members:
/// (1) a link grants nothing — naming a private thing beside a public one moves no right
/// anywhere; (2) a link shows one caller exactly what their own rights admit of each
/// member, and nothing of any other target travels in any response byte — not a title,
/// not an anchor's quoted text, not a person's contact details; (3) protection reaches
/// the membership, never the resources — a member naming a feature whose exact position
/// the caller may not see is withheld outright, the installation's reveal setting
/// re-admits the name alone (the feature still answers with its protected view when
/// followed), and no setting re-admits that name beside a sibling member that shows the
/// same caller exact coordinates.
///
/// Every negative asserts its matching positive in the same test, so a fixture that
/// quietly stopped working cannot pass for a passing refusal.
/// </summary>
public sealed class ResLinkProtectionFloorTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly ITestOutputHelper output;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — owns every fixture, so every negative has its positive
    private HttpClient viewer = null!;  // Viewer — the caller all three properties are about
    private HttpClient keeper = null!;  // Manager — keeps the roster, writes caver entries
    private HttpClient writer = null!;  // Editor — owns only what they upload; no exact view on others' caves
    private long caveTypeId;
    private long genericTypeId;

    public ResLinkProtectionFloorTests(PostgresFixture postgres, ITestOutputHelper output)
    {
        this.output = output;
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-rlfloor-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rf-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rf-view-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rf-keep-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rf-wrt-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            genericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"rf-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"rf-view-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"rf-keep-{suffix}@t.local");
        writer = await AuthHelper.BearerClientAsync(factory, $"rf-wrt-{suffix}@t.local");
    }

    // ---- a link grants nothing ---------------------------------------------------------

    [Fact]
    public async Task A_link_grants_access_to_nothing_it_names()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var openCave = await CreateCaveAsync($"Open gate {suffix}", "authenticated");
        var hiddenName = $"Quiet spring {suffix}";
        var hiddenFeature = await CreateGenericFeatureAsync(hiddenName, "private");
        var (documentId, _) = await UploadDocumentAsync($"field-notes-{suffix}.txt");

        // The private things are unreadable before any link exists…
        (await viewer.GetAsync($"/api/v1/features/{hiddenFeature}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var linkId = await CreateLinkAsync(
            Member("feature", openCave),
            Member("feature", hiddenFeature, sortOrder: 1),
            Member("document", documentId, sortOrder: 2));

        // …and exactly as unreadable after: membership beside a public thing moves no right.
        (await viewer.GetAsync($"/api/v1/features/{hiddenFeature}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nor does the picker start finding what visibility hides — while the owner
        // finding it proves the thinning is rights, not the query.
        (await SearchIdsAsync(viewer, "feature", hiddenName)).ShouldBeEmpty();
        (await SearchIdsAsync(owner, "feature", hiddenName)).ShouldBe([hiddenFeature]);

        // The link itself reads: the open member whole, the private ones as bare rows —
        // and no byte of the private feature's name travels.
        var body = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        body.ShouldNotContain(hiddenName);
        var members = MembersOf(body);
        members.Count.ShouldBe(3);
        DisplayTitle(members, openCave).ShouldContain(suffix);
        foreach (var privateId in new[] { hiddenFeature, documentId })
        {
            var bare = members.Single(m => m.GetProperty("targetId").GetGuid() == privateId);
            bare.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
            bare.GetProperty("anchor").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        // The owner reads all three whole.
        MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}"))
            .ShouldAllBe(m => m.GetProperty("display").ValueKind == JsonValueKind.Object);
    }

    // ---- per-member readability --------------------------------------------------------

    [Fact]
    public async Task A_caller_reads_exactly_the_members_their_own_rights_admit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var openCave = await CreateCaveAsync($"Junction {suffix}", "authenticated");
        var tripTitle = $"Recon walk {suffix}";
        var hiddenTrip = await CreateTripLogAsync(tripTitle, openCave, "private");
        var (documentId, _) = await UploadDocumentAsync($"open-report-{suffix}.txt");
        await MakeDocumentReadableAsync(documentId);
        var guardedName = $"Guarded pit {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);

        var linkId = await CreateLinkAsync(
            Member("feature", openCave),
            Member("tripLog", hiddenTrip, sortOrder: 1),
            Member("document", documentId, sortOrder: 2),
            Member("feature", guarded, sortOrder: 3));

        // The viewer's read, member by member: the readable ones display, the unreadable
        // trip stays a bare row, and the guarded feature's member is not there at all —
        // a bare row would still name the feature, which is the fact being kept.
        var body = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        var members = MembersOf(body);
        members.Count.ShouldBe(3);
        DisplayTitle(members, openCave).ShouldContain(suffix);
        DisplayTitle(members, documentId).ShouldContain(suffix);
        var bareTrip = members.Single(m => m.GetProperty("targetId").GetGuid() == hiddenTrip);
        bareTrip.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        members.ShouldAllBe(m => m.GetProperty("targetId").GetGuid() != guarded);

        // Content, not just counts: nothing of the withheld targets is in the bytes.
        body.ShouldNotContain(tripTitle);
        body.ShouldNotContain(guardedName);

        // The owner reads all four whole — the absences above were the caller's rights.
        var mine = MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}"));
        mine.Count.ShouldBe(4);
        mine.ShouldAllBe(m => m.GetProperty("display").ValueKind == JsonValueKind.Object);
        DisplayTitle(mine, guarded).ShouldBe(guardedName);
    }

    // ---- protection reaches the membership, never the resources ------------------------

    [Fact]
    public async Task The_setting_reopens_the_name_and_the_position_stays_protected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded shaft {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var sibling = await CreateCaveAsync($"Beacon cave {suffix}", "authenticated");
        var linkId = await CreateLinkAsync(
            Member("feature", guarded), Member("feature", sibling, sortOrder: 1));

        // Off, which is how an installation starts: the guarded member is withheld from
        // whoever may not place the cave — while its owner reads both members whole.
        var withheld = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        withheld.ShouldNotContain(guardedName);
        MembersOf(withheld).Single().GetProperty("targetId").GetGuid().ShouldBe(sibling);
        MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(2);

        await SetRevealAsync(true);
        try
        {
            // On, the same caller is told the name — through the ordinary feature
            // resolver, so a title and a route and never a coordinate.
            var revealed = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));
            revealed.Count.ShouldBe(2);
            var member = revealed.Single(m => m.GetProperty("targetId").GetGuid() == guarded);
            member.GetProperty("display").GetProperty("title").GetString().ShouldBe(guardedName);
            member.GetProperty("display").GetProperty("route").GetString().ShouldBe($"/caves/{guarded}");

            // Following it is ordinary feature authorisation: the protected view, with
            // the position still redacted — the setting opened the name and nothing else.
            var theirs = await ReadJsonAsync(await viewer.GetAsync($"/api/v1/caves/{guarded}"));
            theirs.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
            theirs.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
            var mine = await ReadJsonAsync(await owner.GetAsync($"/api/v1/caves/{guarded}"));
            mine.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task No_setting_seats_a_guarded_name_beside_visible_coordinates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded sink {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);

        await SetRevealAsync(true);
        try
        {
            // Control: a sibling that shows the caller no position of its own — a plain
            // readable document — leaves the revealed name in place. This is the same
            // link shape as every carve-out below, minus the coordinates.
            var (plainDocId, _) = await UploadDocumentAsync($"plain-notes-{suffix}.txt");
            await MakeDocumentReadableAsync(plainDocId);
            var control = await CreateLinkAsync(
                Member("feature", guarded), Member("document", plainDocId, sortOrder: 1));
            var controlMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{control}"));
            controlMembers.Count.ShouldBe(2);
            DisplayTitle(controlMembers, guarded).ShouldBe(guardedName);

            // A geotagged photo whose capture point the caller may see: the guarded name
            // would stand beside a position, so its member is withheld — the one pairing
            // the reveal setting never opens.
            var photoFileId = await UploadFileAsync(
                $"entrance-{suffix}.jpg", GeotaggedJpeg(45.53127, 25.44721), "image/jpeg");
            var photoDocId = await DocumentIdOfAsync(photoFileId);
            await MakeDocumentReadableAsync(photoDocId);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                // Fixture proof: the upload really read a capture point out of the image,
                // so the carve-out is exercised rather than merely not contradicted.
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photoFileId))
                    .Geom.ShouldNotBeNull();
            }

            (await viewer.GetAsync($"/api/v1/documents/{photoDocId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
            var geoLink = await CreateLinkAsync(
                Member("feature", guarded), Member("document", photoDocId, sortOrder: 1));
            var geoBody = await BodyAsync(viewer, $"/api/v1/reslinks/{geoLink}");
            geoBody.ShouldNotContain(guardedName);
            MembersOf(geoBody).Single().GetProperty("targetId").GetGuid().ShouldBe(photoDocId);

            // A feature member whose exact position the caller may see — a public GPS
            // point — is the same visible coordinate, so the guarded member stays out.
            var pointId = await CreateGenericFeatureAsync($"Signpost {suffix}", "public");
            var pointLink = await CreateLinkAsync(
                Member("feature", guarded), Member("feature", pointId, sortOrder: 1));
            var pointBody = await BodyAsync(viewer, $"/api/v1/reslinks/{pointLink}");
            pointBody.ShouldNotContain(guardedName);
            MembersOf(pointBody).Single().GetProperty("targetId").GetGuid().ShouldBe(pointId);

            // A waypoint anchor reads coordinates out of a geofile the caller may read —
            // while the very same geofile as a whole member shows none, so only the
            // anchored link withholds the guarded name.
            var geofileId = await UploadGeofileAsync($"track-{suffix}.geojson", "authenticated");
            var waypointLink = await CreateLinkAsync(
                Member("feature", guarded),
                Member("geofile", geofileId, sortOrder: 1, anchorKind: "waypoint", anchor: new { index = 0 }));
            var waypointBody = await BodyAsync(viewer, $"/api/v1/reslinks/{waypointLink}");
            waypointBody.ShouldNotContain(guardedName);
            MembersOf(waypointBody).Single().GetProperty("targetId").GetGuid().ShouldBe(geofileId);

            var wholeLink = await CreateLinkAsync(
                Member("feature", guarded), Member("geofile", geofileId, sortOrder: 1));
            var wholeMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{wholeLink}"));
            wholeMembers.Count.ShouldBe(2);
            DisplayTitle(wholeMembers, guarded).ShouldBe(guardedName);

            // A survey model resolves only for callers with exact view on its cave, and
            // its display routes straight to that cave — absolute georeferenced
            // coordinates one step away — so beside a model the caller may open, the
            // guarded member stays out exactly as beside a placeable feature.
            var surveyedCave = await CreateCaveAsync($"Surveyed cave {suffix}", "authenticated");
            var modelId = await CreateSurveyModelAsync(surveyedCave, $"survey-{suffix}.lox");
            var modelLink = await CreateLinkAsync(
                Member("feature", guarded), Member("surveyModel", modelId, sortOrder: 1));
            var modelBody = await BodyAsync(viewer, $"/api/v1/reslinks/{modelLink}");
            modelBody.ShouldNotContain(guardedName);
            var modelMember = MembersOf(modelBody).Single();
            modelMember.GetProperty("targetId").GetGuid().ShouldBe(modelId);
            modelMember.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Object);

            // The owner may place the cave exactly, so nothing was withheld from them in
            // any of the links above — the absences were this caller's, not the links'.
            foreach (var linkId in new[] { geoLink, pointLink, waypointLink, modelLink })
            {
                MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(2, linkId.ToString());
            }
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    /// <summary>
    /// The same arm, asked about the other position a trip can carry. A trip states where its
    /// party gathers as well as where the trip went, and a trip that states only the first is
    /// every bit as positioned as one that states only the second — so a floor that asked about
    /// the sketch alone would re-admit a guarded name beside coordinates, silently, on exactly
    /// the trips this arm was written for.
    /// </summary>
    [Fact]
    public async Task A_trip_whose_only_position_is_where_its_party_meets_counts_as_positioned()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded meet {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var openCave = await CreateCaveAsync($"Meet junction {suffix}", "authenticated");

        var flatTrip = await CreateTripLogAsync($"Flat meet {suffix}", openCave, "authenticated");
        var meetingTrip = await CreateTripLogAsync(
            $"Met at {suffix}", openCave, "authenticated",
            meetingGeom: new { type = "Point", coordinates = new[] { 25.44721, 45.53127 } });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Fixture proof, and it is the whole point of the case: the positioned trip carries
            // no sketch at all, so what makes it positioned can only be the meeting point.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var met = await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == meetingTrip);
            met.Geom.ShouldBeNull();
            met.MeetingGeom.ShouldNotBeNull();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == flatTrip)).MeetingGeom.ShouldBeNull();
        }

        var flatLink = await CreateLinkAsync(
            Member("feature", guarded), Member("tripLog", flatTrip, sortOrder: 1));
        var meetingLink = await CreateLinkAsync(
            Member("feature", guarded), Member("tripLog", meetingTrip, sortOrder: 1));

        await SetRevealAsync(true);
        try
        {
            // Control: nothing in the flat link puts a position beside the guarded name, so the
            // setting re-admits it.
            var flatMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{flatLink}"));
            flatMembers.Count.ShouldBe(2);
            DisplayTitle(flatMembers, guarded).ShouldBe(guardedName);

            // The meeting point is coordinates this caller may see, so the pairing is refused.
            var meetingBody = await BodyAsync(viewer, $"/api/v1/reslinks/{meetingLink}");
            meetingBody.ShouldNotContain(guardedName);
            MembersOf(meetingBody).Single().GetProperty("targetId").GetGuid().ShouldBe(meetingTrip);

            // The owner may place the cave exactly, so both links read whole for them.
            MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{meetingLink}")).Count.ShouldBe(2);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_trip_carrying_its_own_sketch_seats_no_guarded_name_beside_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded shaft {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var openCave = await CreateCaveAsync($"Junction {suffix}", "authenticated");

        // Three trips differing in exactly the two facts the arm reads: whether the trip
        // carries a sketch of its own, and whether this caller may read the trip at all.
        // A trip's sketch is served exactly to every reader of the trip — it is never
        // snapped or omitted the way a protected feature's geometry is — so a readable
        // positioned trip shows coordinates as plainly as a placeable feature member.
        var flatTitle = $"Flat trip {suffix}";
        var drawnTitle = $"Drawn trip {suffix}";
        var hiddenTitle = $"Hidden trip {suffix}";
        var sketch = new { type = "Point", coordinates = new[] { 25.44721, 45.53127 } };
        var flatTrip = await CreateTripLogAsync(flatTitle, openCave, "authenticated");
        var drawnTrip = await CreateTripLogAsync(drawnTitle, openCave, "authenticated", sketch);
        var hiddenTrip = await CreateTripLogAsync(hiddenTitle, openCave, "private", sketch);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Fixture proof: the sketches really were stored and the flat trip really has
            // none, so each refusal below is exercised rather than merely uncontradicted.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == flatTrip)).Geom.ShouldBeNull();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == drawnTrip)).Geom.ShouldNotBeNull();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == hiddenTrip)).Geom.ShouldNotBeNull();
        }

        // …and the readability half of the fixture: this caller reads the drawn trip and
        // does not read the hidden one, which is what makes the two links differ.
        (await viewer.GetAsync($"/api/v1/trip-logs/{drawnTrip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/trip-logs/{hiddenTrip}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var flatLink = await CreateLinkAsync(
            Member("feature", guarded), Member("tripLog", flatTrip, sortOrder: 1));
        var drawnLink = await CreateLinkAsync(
            Member("feature", guarded), Member("tripLog", drawnTrip, sortOrder: 1));
        var hiddenLink = await CreateLinkAsync(
            Member("feature", guarded), Member("tripLog", hiddenTrip, sortOrder: 1));

        await SetRevealAsync(true);
        try
        {
            // Control: beside a trip with no sketch the setting re-admits the guarded
            // name, because nothing in the link puts a position next to it.
            var flatMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{flatLink}"));
            flatMembers.Count.ShouldBe(2);
            DisplayTitle(flatMembers, guarded).ShouldBe(guardedName);

            // The same link with a positioned trip: the name would stand beside
            // coordinates this caller may see, which is the pairing no setting opens.
            var drawnBody = await BodyAsync(viewer, $"/api/v1/reslinks/{drawnLink}");
            drawnBody.ShouldNotContain(guardedName);
            MembersOf(drawnBody).Single().GetProperty("targetId").GetGuid().ShouldBe(drawnTrip);

            // A positioned trip this caller may not read shows them nothing, so it counts
            // against nobody: the member stays a bare row and the name comes back.
            var hiddenMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{hiddenLink}"));
            hiddenMembers.Count.ShouldBe(2);
            DisplayTitle(hiddenMembers, guarded).ShouldBe(guardedName);
            hiddenMembers.Single(m => m.GetProperty("targetId").GetGuid() == hiddenTrip)
                .GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);

            // The owner may place the cave exactly, so all three links read whole for
            // them — the absence above was this caller's rights, not the link's shape.
            foreach (var linkId in new[] { flatLink, drawnLink, hiddenLink })
            {
                MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(2, linkId.ToString());
            }

            // With the setting off the guarded member is withheld either way — and the
            // trip member is never itself the thing withheld: it names no feature, so it
            // travels whole in both links, sketch or no sketch.
            await SetRevealAsync(false);
            var offFlat = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{flatLink}"));
            offFlat.Single().GetProperty("targetId").GetGuid().ShouldBe(flatTrip);
            var offDrawn = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{drawnLink}"));
            offDrawn.Single().GetProperty("targetId").GetGuid().ShouldBe(drawnTrip);
            DisplayTitle(offDrawn, drawnTrip).ShouldBe(drawnTitle);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_superseded_geotag_counts_against_exactly_whoever_can_reach_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded chimney {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);

        // The writer — no exact view on the cave — uploads a geotagged photo, then
        // re-versions it with a plain image: the capture point survives only on the
        // superseded file, which the document's own rules serve to its writers alone.
        var photoFileId = await UploadFileAsync(
            $"old-entrance-{suffix}.jpg", GeotaggedJpeg(45.53127, 25.44721), "image/jpeg", writer);
        var photoDocId = await DocumentIdOfAsync(photoFileId);
        await MakeDocumentReadableAsync(photoDocId, writer);
        var currentFileId = await UploadVersionAsync(
            photoFileId, $"old-entrance-{suffix}.jpg", PlainJpeg(), "image/jpeg", writer);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Fixture proof: the geotag lives on the superseded file and nowhere current,
            // so whatever the link answers below is about reachability, not absence.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photoFileId))
                .Geom.ShouldNotBeNull();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == currentFileId))
                .Geom.ShouldBeNull();
        }

        var linkId = await CreateLinkAsync(
            Member("feature", guarded), Member("document", photoDocId, sortOrder: 1));

        await SetRevealAsync(true);
        try
        {
            // A caller who may read the document but not write it can never fetch the
            // superseded file, so no route shows them its capture point — the revealed
            // name stands beside what is, to them, a plain document.
            var readers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));
            readers.Count.ShouldBe(2);
            DisplayTitle(readers, guarded).ShouldBe(guardedName);

            // The document's writer can open version history onto that capture point, so
            // for them the guarded name would stand beside a reachable position — the
            // member naming the cave is withheld from exactly this caller.
            var writers = await BodyAsync(writer, $"/api/v1/reslinks/{linkId}");
            writers.ShouldNotContain(guardedName);
            MembersOf(writers).Single().GetProperty("targetId").GetGuid().ShouldBe(photoDocId);

            // And the cave's owner may place it exactly anyway: both members, always.
            MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(2);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_centerline_member_is_withheld_like_the_cave_it_draws()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateProtectedCaveAsync($"Guarded gallery {suffix}");
        var centerlineName = $"secret-line-{suffix}";
        var centerlineId = await UploadCenterlineAsync(guarded, $"{centerlineName}.gpx");
        var pointId = await CreateGenericFeatureAsync($"Trail marker {suffix}", "public");
        var linkId = await CreateLinkAsync(
            Member("feature", centerlineId), Member("feature", pointId, sortOrder: 1));

        // A centerline is coordinates from its first vertex to its last and inherits the
        // cave's protection, so its membership is withheld from whoever may not place
        // the cave — the same rule, reached through the descendant.
        var body = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        body.ShouldNotContain(centerlineName);
        MembersOf(body).Single().GetProperty("targetId").GetGuid().ShouldBe(pointId);

        // While the cave's owner reads both members whole.
        var mine = MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}"));
        mine.Count.ShouldBe(2);
        DisplayTitle(mine, centerlineId).ShouldBe(centerlineName);
    }

    // ---- the write acknowledgments ------------------------------------------------------

    [Fact]
    public async Task The_create_echo_answers_its_author_with_the_member_they_asserted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded well {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);

        // Authoring takes Read, not exact view: the viewer sees the guarded cave's
        // protected view and links it. The acknowledgment answers them with the very
        // membership they just asserted — a cut echo of a single-member link would
        // read as a failed write while disclosing nothing new to its author.
        var created = await viewer.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[] { Member("feature", guarded) },
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var echo = JsonDocument.Parse(payload).RootElement;
        var linkId = echo.GetProperty("id").GetGuid();
        var asserted = MembersOf(echo);
        asserted.Single().GetProperty("targetId").GetGuid().ShouldBe(guarded);
        DisplayTitle(asserted, guarded).ShouldBe(guardedName);

        // The echo was the exemption, not a right: the same caller's ordinary read
        // applies the ordinary rule and withholds the membership again.
        var read = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        read.ShouldNotContain(guardedName);
        MembersOf(read).ShouldBeEmpty();

        // While the cave's owner — exact view — reads the member whole.
        MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(1);
    }

    // ---- anchor payloads follow their target -------------------------------------------

    [Fact]
    public async Task A_quote_of_an_unreadable_document_travels_in_no_response_byte()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var quote = $"the entrance lies at the marked bend {suffix}";
        var openCave = await CreateCaveAsync($"Cited cave {suffix}", "authenticated");
        var (documentId, fileId) = await UploadDocumentAsync($"cited-{suffix}.txt");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[]
            {
                Member("feature", openCave),
                Member("document", documentId, sortOrder: 1, anchorKind: "textRange",
                    anchor: new { start = 0, end = 8, quote }, anchorFileId: fileId),
            },
        });
        var createdPayload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, createdPayload);
        var dto = JsonDocument.Parse(createdPayload).RootElement;
        var linkId = dto.GetProperty("id").GetGuid();
        var shortCode = dto.GetProperty("shortCode").GetString()!;

        // The author reads their quote back — the fixture really carries it.
        (await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).ShouldContain(quote);

        // A payload can quote what it anchors to, so for a caller who may not read the
        // document no route serves a single byte of it: the link by id, the link by its
        // short code, and the panel on the readable sibling target.
        (await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}")).ShouldNotContain(quote);
        (await BodyAsync(viewer, $"/api/v1/reslinks/{shortCode}")).ShouldNotContain(quote);
        var panel = await BodyAsync(viewer, $"/api/v1/reslinks/for-target?type=feature&id={openCave}");
        panel.ShouldNotContain(quote);

        // The panel did list the link — the silence above was the payload, not the row.
        JsonDocument.Parse(panel).RootElement.GetProperty("items").EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid()).ShouldContain(linkId);
    }

    [Fact]
    public async Task A_stale_pin_degrades_without_routing_a_read_only_caller_into_history()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var openCave = await CreateCaveAsync($"Pinned cave {suffix}", "authenticated");
        var (documentId, v1FileId) = await UploadDocumentAsync($"pinned-{suffix}.txt");
        await MakeDocumentReadableAsync(documentId);
        var linkId = await CreateLinkAsync(
            Member("feature", openCave),
            Member("document", documentId, sortOrder: 1, anchorKind: "textRange",
                anchor: new { start = 0, end = 8, quote = "contents" }, anchorFileId: v1FileId));
        await UploadVersionAsync(v1FileId, $"pinned-{suffix}.txt", "reworded entirely"u8.ToArray(), "text/plain");

        // A caller who may read the document but not write it is told the anchor is
        // degraded — that it no longer addresses what the document serves is a fact of
        // this link — but the superseded file's id is not handed over: their document
        // read would refuse that version, so the marker must not route them into it.
        var theirs = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"))
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        theirs.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Object);
        theirs.GetProperty("anchor").ValueKind.ShouldBe(JsonValueKind.Object);
        theirs.GetProperty("anchorState").GetString().ShouldBe("degraded");
        theirs.GetProperty("anchorFileId").ValueKind.ShouldBe(JsonValueKind.Null);

        // The document's writer keeps the pin: version history answers them anyway.
        var mine = MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}"))
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        mine.GetProperty("anchorState").GetString().ShouldBe("degraded");
        mine.GetProperty("anchorFileId").GetGuid().ShouldBe(v1FileId);
    }

    // ---- the caver tiers ---------------------------------------------------------------

    [Fact]
    public async Task Roster_contact_tiers_hold_in_link_display()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fullName = $"Maria Petrescu {suffix}";
        var email = $"maria-{suffix}@example.org";
        var phone = "+40-721-000-111";
        var caverId = await CreateCaverAsync(fullName, email, phone);
        var linkId = await CreateLinkAsync(Member("caver", caverId));

        // Whoever keeps the roster reads an account-less person's contact details — the
        // tier that consented on their behalf.
        var kept = MembersOf(await BodyAsync(keeper, $"/api/v1/reslinks/{linkId}")).Single();
        kept.GetProperty("display").GetProperty("title").GetString().ShouldBe(fullName);
        kept.GetProperty("display").GetProperty("subtitle").GetString().ShouldBe(email);

        // Any other signed-in caller gets the name — a club's member list has always
        // been readable — and not one byte of contact data.
        var body = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        var theirs = MembersOf(body).Single();
        theirs.GetProperty("display").GetProperty("title").GetString().ShouldBe(fullName);
        theirs.GetProperty("display").GetProperty("subtitle").ValueKind.ShouldBe(JsonValueKind.Null);
        body.ShouldNotContain(email);
        body.ShouldNotContain(phone);
    }

    // ---- the panel never leaks through a withheld member -------------------------------

    [Fact]
    public async Task The_panel_lists_no_link_connected_only_through_a_withheld_member()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded resurgence {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var pointId = await CreateGenericFeatureAsync($"Trailhead {suffix}", "public");
        var (documentId, _) = await UploadDocumentAsync($"panel-notes-{suffix}.txt");
        await MakeDocumentReadableAsync(documentId);

        // Two links, each connected to the guarded cave only by the member naming it:
        // one beside visible coordinates, one beside a plain document.
        var exposingLink = await CreateLinkAsync(
            Member("feature", guarded), Member("feature", pointId, sortOrder: 1));
        var plainLink = await CreateLinkAsync(
            Member("feature", guarded), Member("document", documentId, sortOrder: 1));

        // Off: the guarded cave's own panel admits nothing to the viewer — a single row,
        // or a non-zero badge total, would say links about this feature exist. The owner
        // holds both.
        var closed = await ReadJsonAsync(
            await viewer.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={guarded}"));
        closed.GetProperty("totalItems").GetInt32().ShouldBe(0);
        closed.GetProperty("items").GetArrayLength().ShouldBe(0);
        (await ReadJsonAsync(await owner.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={guarded}")))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);

        // From the open side the link was always reachable — with the guarded member
        // absent, so reaching it discloses nothing.
        var openSide = await BodyAsync(viewer, $"/api/v1/reslinks/for-target?type=feature&id={pointId}");
        openSide.ShouldNotContain(guardedName);
        var openPanel = JsonDocument.Parse(openSide).RootElement;
        openPanel.GetProperty("totalItems").GetInt32().ShouldBe(1);
        MembersOf(openPanel.GetProperty("items")[0])
            .Single().GetProperty("targetId").GetGuid().ShouldBe(pointId);

        await SetRevealAsync(true);
        try
        {
            // On, each link answers for itself: the one whose connecting member survives
            // is listed, the one whose member would put the name beside coordinates is
            // not — and the total stays the badge, counting only what is listed.
            var revealed = await ReadJsonAsync(
                await viewer.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={guarded}"));
            revealed.GetProperty("totalItems").GetInt32().ShouldBe(1);
            revealed.GetProperty("items").EnumerateArray()
                .Single().GetProperty("id").GetGuid().ShouldBe(plainLink);

            // The dropped link still reads from its open side, guarded member absent.
            var stillOpen = await BodyAsync(viewer, $"/api/v1/reslinks/{exposingLink}");
            stillOpen.ShouldNotContain(guardedName);
            MembersOf(stillOpen).Single().GetProperty("targetId").GetGuid().ShouldBe(pointId);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    /// <summary>
    /// The curation answer survives the arm that pages in memory. A protected feature's
    /// panel materialises every candidate before it can know which survive the disclosure
    /// cut — that is what keeps the badge total honest — and slices afterwards, so the
    /// capability is decided on the rows returned rather than alongside the projection.
    /// Both halves of the answer over the one listing: its author is told yes, and a
    /// Viewer who wrote none of it and holds nothing over it is told no.
    /// </summary>
    [Fact]
    public async Task A_revealed_panel_states_curation_for_the_rows_it_returns()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateProtectedCaveAsync($"Guarded curation {suffix}");
        var (documentId, _) = await UploadDocumentAsync($"curation-notes-{suffix}.txt");
        await MakeDocumentReadableAsync(documentId);
        var linkId = await CreateLinkAsync(
            Member("feature", guarded), Member("document", documentId, sortOrder: 1));

        await SetRevealAsync(true);
        try
        {
            var mine = await ReadJsonAsync(
                await owner.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={guarded}"));
            mine.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetGuid() == linkId)
                .GetProperty("mayEdit").GetBoolean().ShouldBeTrue();

            var theirs = await ReadJsonAsync(
                await viewer.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={guarded}"));
            var listed = theirs.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetGuid() == linkId);
            listed.GetProperty("mayEdit").GetBoolean().ShouldBeFalse();
            theirs.GetProperty("totalItems").GetInt32()
                .ShouldBe(theirs.GetProperty("items").GetArrayLength());
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_panel_asked_for_one_relation_counts_exactly_the_rows_it_lists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded rift {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var openCave = await CreateCaveAsync($"Junction {suffix}", "authenticated");
        var pointId = await CreateGenericFeatureAsync($"Parking {suffix}", "public");
        var (documentId, _) = await UploadDocumentAsync($"roles-{suffix}.txt");
        await MakeDocumentReadableAsync(documentId);

        // One trip standing as the main member of every role link below, deliberately
        // without a sketch of its own: a positioned trip shows its readers coordinates
        // and would re-withhold the guarded member for that reason rather than the one
        // under test here.
        var tripId = await CreateTripLogAsync($"Roles {suffix}", openCave, "authenticated");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == tripId)).Geom.ShouldBeNull();
        }

        var visited = await RelationIdAsync("trip-visited");
        var surveyed = await RelationIdAsync("trip-surveyed");

        // Three links on the same guarded cave: two visits, one of which seats the cave
        // beside a public point, and one survey that no question about visits may answer.
        var visitedPlain = await CreateTypedLinkAsync(
            visited,
            Member("tripLog", tripId, isMain: true),
            Member("feature", guarded, sortOrder: 1),
            Member("document", documentId, sortOrder: 2));
        var visitedBesideCoordinates = await CreateTypedLinkAsync(
            visited,
            Member("tripLog", tripId, isMain: true),
            Member("feature", guarded, sortOrder: 1),
            Member("feature", pointId, sortOrder: 2));
        var surveyedPlain = await CreateTypedLinkAsync(
            surveyed,
            Member("tripLog", tripId, isMain: true),
            Member("feature", guarded, sortOrder: 1),
            Member("document", documentId, sortOrder: 2));

        // Setting off, the guarded cave's own panel admits nothing to the viewer whatever
        // it is asked for — a filtered badge is a badge, and a non-zero one would say
        // links of that role about this feature exist.
        await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-visited", 0);
        await ShouldAgreeWithItsRowsAsync(viewer, guarded, null, 0);

        // Whoever may place the cave reads the same panel whole, and the filter narrows it
        // rather than emptying it: two visits of three links, and the survey on its own.
        await ShouldAgreeWithItsRowsAsync(owner, guarded, null, 3);
        await ShouldAgreeWithItsRowsAsync(owner, guarded, "trip-visited", 2);
        var ownerSurvey = await ShouldAgreeWithItsRowsAsync(owner, guarded, "trip-surveyed", 1);
        ownerSurvey.GetProperty("items").EnumerateArray()
            .Single().GetProperty("id").GetGuid().ShouldBe(surveyedPlain);

        // A role nobody used here answers empty rather than refusing — it is a question
        // with an answer, and the answer is none.
        await ShouldAgreeWithItsRowsAsync(owner, guarded, "trip-dug", 0);

        // The trip's own side pages in the database rather than in memory, and the
        // identity has to hold there too: every role link is listed from the trip, with
        // the guarded cave's name absent from every byte of the filtered answer. Three
        // visits from this side, not two: writing the trip's cave list is itself recorded
        // as a visit, so the open cave the trip was created naming has a link of its own.
        var fromTrip = await ShouldAgreeWithItsRowsAsync(viewer, tripId, "trip-visited", 3, "tripLog");
        fromTrip.GetRawText().ShouldNotContain(guardedName);
        await ShouldAgreeWithItsRowsAsync(viewer, tripId, "trip-surveyed", 1, "tripLog");
        await ShouldAgreeWithItsRowsAsync(viewer, tripId, null, 4, "tripLog");

        await SetRevealAsync(true);
        try
        {
            // Setting on, each link decides for itself and the filter is applied before
            // that decision, not after it: unfiltered, the two links whose connecting
            // member survives are listed; asked for visits, only the visit among them is,
            // and the total counts that one row rather than the two candidates it came
            // from. The link seating the guarded name beside coordinates is listed by
            // neither question.
            var revealed = await ShouldAgreeWithItsRowsAsync(viewer, guarded, null, 2);
            revealed.GetRawText().ShouldNotContain(visitedBesideCoordinates.ToString());

            var revealedVisits = await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-visited", 1);
            revealedVisits.GetProperty("items").EnumerateArray()
                .Single().GetProperty("id").GetGuid().ShouldBe(visitedPlain);
            revealedVisits.GetRawText().ShouldNotContain(surveyedPlain.ToString());

            var revealedSurvey = await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-surveyed", 1);
            revealedSurvey.GetProperty("items").EnumerateArray()
                .Single().GetProperty("id").GetGuid().ShouldBe(surveyedPlain);
            revealedSurvey.GetRawText().ShouldNotContain(visitedPlain.ToString());
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_role_withholds_the_cave_it_names_exactly_as_any_other_link_does()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded sink {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var openCave = await CreateCaveAsync($"Portal {suffix}", "authenticated");

        // Fixture proof for the negative below: this caller reads the cave and may not
        // place it, while the owner may — the absences asserted here are that difference
        // and not an unreadable row.
        var viewerCave = await ReadJsonAsync(await viewer.GetAsync($"/api/v1/caves/{guarded}"));
        viewerCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        var ownerCave = await ReadJsonAsync(await owner.GetAsync($"/api/v1/caves/{guarded}"));
        ownerCave.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();

        // …and the sibling that does the exposing really does show this caller a position.
        var pointName = $"Track end {suffix}";
        var pointId = await CreateGenericFeatureAsync(pointName, "public");
        var viewerPoint = await ReadJsonAsync(await viewer.GetAsync($"/api/v1/features/{pointId}"));
        var viewerPointFeature = viewerPoint.GetProperty("feature");
        viewerPointFeature.GetProperty("geometry").ValueKind.ShouldNotBe(JsonValueKind.Null);
        viewerPointFeature.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();

        var tripId = await CreateTripLogAsync($"Roll {suffix}", openCave, "authenticated");
        var dug = await RelationIdAsync("trip-dug");

        // Two links of the same role, differing only in what stands beside the guarded
        // cave: nothing that shows a position, and a public point that does.
        var plainRole = await CreateTypedLinkAsync(
            dug,
            Member("tripLog", tripId, isMain: true),
            Member("feature", guarded, sortOrder: 1));
        var roleBesideCoordinates = await CreateTypedLinkAsync(
            dug,
            Member("tripLog", tripId, isMain: true),
            Member("feature", guarded, sortOrder: 1),
            Member("feature", pointId, sortOrder: 2));

        // Setting off: a role names the guarded cave to nobody who cannot place it. The
        // trip member is never the thing withheld — it names no feature — so it travels
        // whole and the link still reads as a role, with the cave simply absent.
        var offPlain = await BodyAsync(viewer, $"/api/v1/reslinks/{plainRole}");
        offPlain.ShouldNotContain(guardedName);
        MembersOf(offPlain).Single().GetProperty("targetId").GetGuid().ShouldBe(tripId);
        JsonDocument.Parse(offPlain).RootElement
            .GetProperty("relationType").GetProperty("code").GetString().ShouldBe("trip-dug");

        var offBeside = await BodyAsync(viewer, $"/api/v1/reslinks/{roleBesideCoordinates}");
        offBeside.ShouldNotContain(guardedName);
        var offBesideIds = MembersOf(offBeside).Select(m => m.GetProperty("targetId").GetGuid()).ToList();
        offBesideIds.Count.ShouldBe(2);
        offBesideIds.ShouldContain(tripId);
        offBesideIds.ShouldContain(pointId);

        // The positive, in the same test: the owner places the cave exactly, so both role
        // links read whole for them and the withholding above was this caller's rights.
        MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{plainRole}")).Count.ShouldBe(2);
        var ownerBeside = MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{roleBesideCoordinates}"));
        ownerBeside.Count.ShouldBe(3);
        DisplayTitle(ownerBeside, guarded).ShouldBe(guardedName);

        await SetRevealAsync(true);
        try
        {
            // Setting on re-admits the name where nothing seats it beside coordinates…
            var onPlain = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{plainRole}"));
            onPlain.Count.ShouldBe(2);
            DisplayTitle(onPlain, guarded).ShouldBe(guardedName);

            // …and never where something does. A role is a link like any other here: the
            // rule reads the members standing together, not the relation they stand in.
            var onBeside = await BodyAsync(viewer, $"/api/v1/reslinks/{roleBesideCoordinates}");
            onBeside.ShouldNotContain(guardedName);
            var onBesideIds = MembersOf(onBeside).Select(m => m.GetProperty("targetId").GetGuid()).ToList();
            onBesideIds.Count.ShouldBe(2);
            onBesideIds.ShouldContain(pointId);

            // The cave's own panel, asked for this one role, lists exactly the link whose
            // connecting member survived — and counts it, the badge agreeing with the row.
            var panel = await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-dug", 1);
            panel.GetProperty("items").EnumerateArray()
                .Single().GetProperty("id").GetGuid().ShouldBe(plainRole);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task A_trips_own_sketch_withholds_the_cave_its_role_names_whatever_the_setting_says()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guardedName = $"Guarded pot {suffix}";
        var guarded = await CreateProtectedCaveAsync(guardedName);
        var openCave = await CreateCaveAsync($"Doline {suffix}", "authenticated");

        // The two trips differ in one fact only: whether the trip carries a sketch. A
        // trip's sketch is served exactly to everyone who may read the trip, so a
        // positioned trip standing as a role's main member shows the caller coordinates
        // as plainly as a placeable feature would — and a role puts a trip in every link
        // it makes, which is where this now decides most of what a caller sees.
        var flatTitle = $"Flat survey {suffix}";
        var drawnTitle = $"Drawn survey {suffix}";
        var flatTrip = await CreateTripLogAsync(flatTitle, openCave, "authenticated");
        var drawnTrip = await CreateTripLogAsync(
            drawnTitle, openCave, "authenticated",
            new { type = "Point", coordinates = new[] { 25.44803, 45.52914 } });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Fixture proof: one sketch really was stored and the other really was not,
            // so each half below is exercised rather than merely uncontradicted.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == flatTrip)).Geom.ShouldBeNull();
            (await db.TripLogs.AsNoTracking().FirstAsync(t => t.Id == drawnTrip)).Geom.ShouldNotBeNull();
        }

        // …and the readability half: this caller reads both trips, so the drawn one really
        // does show them a position rather than counting against nobody.
        (await viewer.GetAsync($"/api/v1/trip-logs/{flatTrip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/trip-logs/{drawnTrip}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var surveyed = await RelationIdAsync("trip-surveyed");
        var flatRole = await CreateTypedLinkAsync(
            surveyed,
            Member("tripLog", flatTrip, isMain: true),
            Member("feature", guarded, sortOrder: 1));
        var drawnRole = await CreateTypedLinkAsync(
            surveyed,
            Member("tripLog", drawnTrip, isMain: true),
            Member("feature", guarded, sortOrder: 1));

        await SetRevealAsync(true);
        try
        {
            // Setting on: the flat trip's role reads whole — the control that proves the
            // refusal beside it is about the sketch and not about roles.
            var flatMembers = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{flatRole}"));
            flatMembers.Count.ShouldBe(2);
            DisplayTitle(flatMembers, guarded).ShouldBe(guardedName);

            // The same role behind a positioned trip: no setting seats a guarded name
            // beside coordinates its reader may see, and the trip is those coordinates.
            var drawnBody = await BodyAsync(viewer, $"/api/v1/reslinks/{drawnRole}");
            drawnBody.ShouldNotContain(guardedName);
            var drawnMember = MembersOf(drawnBody).Single();
            drawnMember.GetProperty("targetId").GetGuid().ShouldBe(drawnTrip);
            drawnMember.GetProperty("display").GetProperty("title").GetString().ShouldBe(drawnTitle);

            // The cave's panel for that role therefore lists the flat trip's link alone,
            // and says so in its count.
            var panel = await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-surveyed", 1);
            panel.GetProperty("items").EnumerateArray()
                .Single().GetProperty("id").GetGuid().ShouldBe(flatRole);

            // Whoever may place the cave sees both roles whole, sketch or no sketch.
            foreach (var linkId in new[] { flatRole, drawnRole })
            {
                MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")).Count.ShouldBe(2, linkId.ToString());
            }
            await ShouldAgreeWithItsRowsAsync(owner, guarded, "trip-surveyed", 2);
        }
        finally
        {
            await SetRevealAsync(false);
        }

        // Setting off, the cave is withheld from both roles — and the trip member stays
        // whole in both, because a member naming no feature has no position to guard.
        foreach (var (linkId, tripId, title) in
            new[] { (flatRole, flatTrip, flatTitle), (drawnRole, drawnTrip, drawnTitle) })
        {
            var body = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
            body.ShouldNotContain(guardedName);
            var members = MembersOf(body);
            members.Single().GetProperty("targetId").GetGuid().ShouldBe(tripId);
            DisplayTitle(members, tripId).ShouldBe(title);
        }
        await ShouldAgreeWithItsRowsAsync(viewer, guarded, "trip-surveyed", 0);
    }

    // ---- the timeline is never a side door ---------------------------------------------

    [Fact]
    public async Task A_features_timeline_names_no_link_its_live_reads_withhold()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateProtectedCaveAsync($"Guarded spring {suffix}");
        var pointId = await CreateGenericFeatureAsync($"Cairn {suffix}", "public");
        var linkId = await CreateLinkAsync(
            Member("feature", guarded), Member("feature", pointId, sortOrder: 1));

        // The membership event belongs to the cave's timeline, but which link it joined
        // is the association every live read withholds from this caller — and an audit
        // row can weigh neither the reveal setting nor the link's siblings, so the
        // timeline serves the event and never the link id. A timeline that named it
        // would hand out the very row the link read drops, one hop from the sibling
        // that shows this caller exact coordinates.
        var theirs = await BodyAsync(viewer, $"/api/v1/history?entityType=feature&entityId={guarded}");
        theirs.ShouldNotContain(linkId.ToString());
        var redactedRow = JsonDocument.Parse(theirs).RootElement.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("entityType").GetString() == "ResLinkMember");
        redactedRow.GetProperty("redactedProperties").EnumerateArray()
            .Select(p => p.GetString()).ShouldContain("ResLinkId");

        // Whoever may place the cave reads the same timeline whole, link id included.
        (await BodyAsync(owner, $"/api/v1/history?entityType=feature&entityId={guarded}"))
            .ShouldContain(linkId.ToString());
    }

    // ---- what the honest total costs ----------------------------------------------------

    /// <summary>
    /// The panel of a protected feature under the reveal setting is the one read that
    /// materialises every candidate rather than a page of them, because the total it
    /// reports is the count of candidates that survived the disclosure cut and there is no
    /// way to count those without projecting them. That is a deliberate price, and this is
    /// the measurement of it at a link count a heavily-worked cave can plausibly reach —
    /// so that a change which multiplies the candidate set (a per-row decision taken before
    /// the slice, say) shows up as a failure here rather than as a slow page in an
    /// installation. The budget is loose on purpose: it is a regression trip-wire on a
    /// shared, containerised database, not a benchmark.
    /// </summary>
    [Fact]
    public async Task A_revealed_panel_pays_for_its_honest_total_within_budget()
    {
        const int Links = 150;
        const int PageSize = 20;
        // Measured at ~40 ms on a development machine against a containerised database.
        // The budget is two orders of magnitude above that on purpose: it must survive a
        // loaded CI agent while still failing loudly if the arm ever turns quadratic.
        const long BudgetMs = 2000;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateProtectedCaveAsync($"Busy pot {suffix}");
        var openCave = await CreateCaveAsync($"Doline {suffix}", "authenticated");
        var visited = await RelationIdAsync("trip-visited");

        // Every link is a trip naming the guarded cave — the shape a role really makes,
        // and the one that puts a main member on every row for the curation answer to be
        // taken over. One trip serves them all: what is being measured is the number of
        // candidate links, not the number of distinct trips.
        var tripId = await CreateTripLogAsync($"Load {suffix}", openCave, "authenticated");
        for (var i = 0; i < Links; i++)
        {
            await CreateTypedLinkAsync(
                visited, Member("tripLog", tripId, isMain: true), Member("feature", guarded, sortOrder: 1));
        }

        await SetRevealAsync(true);
        try
        {
            var route = $"/api/v1/reslinks/for-target?type=feature&id={guarded}&pageSize={PageSize}";

            // Warm up first (connection pool, query plans), then measure — otherwise the
            // number reported is mostly first-request cost.
            (await BodyAsync(viewer, route)).ShouldNotBeNullOrEmpty();
            var stopwatch = Stopwatch.StartNew();
            var panel = JsonDocument.Parse(await BodyAsync(viewer, route)).RootElement;
            stopwatch.Stop();

            output.WriteLine(
                $"revealed panel, {Links} candidates, page of {PageSize}: {stopwatch.ElapsedMilliseconds} ms");

            // The total is the whole point of paying for it: it counts what survived the
            // cut, not what the page holds.
            panel.GetProperty("totalItems").GetInt32().ShouldBe(Links);
            panel.GetProperty("items").GetArrayLength().ShouldBe(PageSize);
            stopwatch.ElapsedMilliseconds.ShouldBeLessThan(
                BudgetMs, $"{Links} candidates took {stopwatch.ElapsedMilliseconds} ms");
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    // ---- helpers -----------------------------------------------------------------------

    /// <summary>A member payload in the create/add wire shape.</summary>
    private static object Member(
        string targetType,
        Guid targetId,
        bool isMain = false,
        int sortOrder = 0,
        string anchorKind = "whole",
        object? anchor = null,
        Guid? anchorFileId = null) => new
    {
        targetType,
        targetId,
        isMain,
        sortOrder,
        note = (string?)null,
        anchorKind,
        anchor,
        anchorFileId,
    };

    /// <summary>Creates an untyped link as the owner and returns its id.</summary>
    private Task<Guid> CreateLinkAsync(params object[] members) =>
        CreateTypedLinkAsync(null, members);

    /// <summary>Creates a link of one relation type as the owner and returns its id. A
    /// directed relation wants exactly one member marked main once the link has two, so
    /// callers pass one.</summary>
    private async Task<Guid> CreateTypedLinkAsync(long? relationTypeId, params object[] members)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId,
            description = (string?)null,
            members,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The id of a relation type, by the code clients address it as.</summary>
    private async Task<long> RelationIdAsync(string code)
    {
        var listed = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        return listed.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == code)
            .GetProperty("id").GetInt64();
    }

    /// <summary>
    /// Reads one panel — optionally asked for a single relation — and asserts the count it
    /// reports is both the expected one and the number of rows it actually returned. The
    /// total doubles as the badge, so a filter that reached the rows without reaching the
    /// count (or the other way round) would show a number nothing on the page explains.
    /// </summary>
    private static async Task<JsonElement> ShouldAgreeWithItsRowsAsync(
        HttpClient client, Guid targetId, string? relation, int expectedTotal, string targetType = "feature")
    {
        var route = $"/api/v1/reslinks/for-target?type={targetType}&id={targetId}"
            + (relation is null ? string.Empty : $"&relation={Uri.EscapeDataString(relation)}");
        var panel = await ReadJsonAsync(await client.GetAsync(route));
        var total = panel.GetProperty("totalItems").GetInt32();
        total.ShouldBe(expectedTotal, route);
        panel.GetProperty("items").GetArrayLength().ShouldBe(total, route);
        return panel;
    }

    /// <summary>GETs a route, asserts 200, and returns the raw body — the byte-level
    /// assertions here are about what travels, not what deserializes.</summary>
    private static async Task<string> BodyAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return payload;
    }

    private static List<JsonElement> MembersOf(string linkBody) =>
        MembersOf(JsonDocument.Parse(linkBody).RootElement);

    private static List<JsonElement> MembersOf(JsonElement link) =>
        [.. link.GetProperty("members").EnumerateArray()];

    private static string DisplayTitle(IEnumerable<JsonElement> members, Guid targetId) =>
        members.Single(m => m.GetProperty("targetId").GetGuid() == targetId)
            .GetProperty("display").GetProperty("title").GetString()!;

    /// <summary>
    /// Flips the installation's reveal setting. The row lives in a database shared with
    /// every other class in this collection, which is why <see cref="DisposeAsync"/>
    /// deletes it: a policy left switched on here would quietly change what those
    /// classes are testing.
    /// </summary>
    private async Task SetRevealAsync(bool reveal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(
            AppSettingSections.Protection, new ProtectionSettings { RevealProtectedAssociations = reveal });
    }

    private Task<Guid> CreateCaveAsync(string name, string visibility) =>
        CreateCaveAsync(name, visibility, locationProtected: false);

    /// <summary>A location-protected, otherwise readable cave — visible without exact view.</summary>
    private Task<Guid> CreateProtectedCaveAsync(string name) =>
        CreateCaveAsync(name, "authenticated", locationProtected: true);

    private async Task<Guid> CreateCaveAsync(string name, string visibility, bool locationProtected)
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
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateGenericFeatureAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = genericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.81, 45.81 } },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A trip as the owner. <paramref name="geom"/> is the trip's own sketch —
    /// left null unless a test is about a positioned trip, since a trip with a geometry
    /// exposes coordinates to every reader and would change what its siblings disclose.
    /// </summary>
    private async Task<Guid> CreateTripLogAsync(
        string title, Guid caveId, string visibility, object? geom = null, object? meetingGeom = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-05-01",
            geom,
            meetingGeom,
            caveIds = new[] { caveId },
            participants = Array.Empty<object>(),
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaverAsync(string fullName, string? email, string? phone)
    {
        var response = await keeper.PostAsJsonAsync("/api/v1/cavers/", new
        {
            fullName,
            email,
            phone,
            notes = (string?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads an empty GeoJSON as the owner; a null visibility keeps the
    /// upload default (private), anything else is set right after.</summary>
    private async Task<Guid> UploadGeofileAsync(string fileName, string? visibility)
    {
        var content = new ByteArrayContent("""{"type":"FeatureCollection","features":[]}"""u8.ToArray());
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var geofile = JsonDocument.Parse(payload).RootElement;
        var id = geofile.GetProperty("id").GetGuid();
        if (visibility is not null)
        {
            var updated = await owner.PutAsJsonAsync($"/api/v1/geofiles/{id}", new
            {
                name = geofile.GetProperty("name").GetString(),
                description = (string?)null,
                style = (object?)null,
                cavingGroupId = (Guid?)null,
                visibility,
            });
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        }

        return id;
    }

    private async Task<Guid> UploadFileAsync(
        string fileName, byte[] bytes, string contentType, HttpClient? client = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await (client ?? owner).PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a new revision over an existing file as its document's writer and
    /// returns the new file's id; the old file becomes a superseded version.</summary>
    private async Task<Guid> UploadVersionAsync(
        Guid fileId, string fileName, byte[] bytes, string contentType, HttpClient? client = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await (client ?? owner).PostAsync($"/api/v1/files/{fileId}/versions", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a survey model file onto a cave as the owner and returns its id.</summary>
    private async Task<Guid> CreateSurveyModelAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes($"LOX-{Guid.NewGuid():N}"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a small GPX centerline onto a cave as the owner; the created
    /// centerline is a feature of its own, named after the file.</summary>
    private async Task<Guid> UploadCenterlineAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="silexgis-test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><trkseg>
                <trkpt lat="45.500" lon="25.500"><ele>800</ele></trkpt>
                <trkpt lat="45.501" lon="25.501"><ele>810</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a small text file as the owner and returns (documentId, fileId).
    /// The document is born private — exactly what these tests need.</summary>
    private async Task<(Guid DocumentId, Guid FileId)> UploadDocumentAsync(string fileName)
    {
        var fileId = await UploadFileAsync(
            fileName, System.Text.Encoding.UTF8.GetBytes($"contents of {fileName}"), "text/plain");
        return (await DocumentIdOfAsync(fileId), fileId);
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    /// <summary>Widens a born-private document to every signed-in caller, through the
    /// document's own write surface, acting as the document's writer.</summary>
    private async Task MakeDocumentReadableAsync(Guid documentId, HttpClient? client = null)
    {
        client ??= owner;
        var current = await ReadJsonAsync(await client.GetAsync($"/api/v1/documents/{documentId}"));
        var response = await client.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = current.GetProperty("title").GetString(),
            documentTypeId = current.GetProperty("documentTypeId").ValueKind == JsonValueKind.Number
                ? current.GetProperty("documentTypeId").GetInt64()
                : (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<Guid>> SearchIdsAsync(HttpClient client, string type, string query)
    {
        var hits = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/targets/search?type={type}&q={Uri.EscapeDataString(query)}");
        return [.. hits.EnumerateArray().Select(h => h.GetProperty("id").GetGuid())];
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    /// <summary>A small JPEG with no EXIF at all — re-versioning a photo with this is
    /// exactly the "the geotag had to go" edit.</summary>
    private static byte[] PlainJpeg()
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

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

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Protection).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        keeper?.Dispose();
        writer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
