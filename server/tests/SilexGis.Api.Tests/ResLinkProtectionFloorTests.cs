// SPDX-License-Identifier: AGPL-3.0-or-later
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
[Collection(PostgresCollection.Name)]
public sealed class ResLinkProtectionFloorTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — owns every fixture, so every negative has its positive
    private HttpClient viewer = null!;  // Viewer — the caller all three properties are about
    private HttpClient keeper = null!;  // Manager — keeps the roster, writes caver entries
    private HttpClient writer = null!;  // Editor — owns only what they upload; no exact view on others' caves
    private long caveTypeId;
    private long genericTypeId;

    public ResLinkProtectionFloorTests(PostgresFixture postgres)
    {
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
    private async Task<Guid> CreateLinkAsync(params object[] members)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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

    private async Task<Guid> CreateTripLogAsync(string title, Guid caveId, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-05-01",
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
        var response = await (client ?? owner).PostAsync("/api/v1/files/", form);
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
