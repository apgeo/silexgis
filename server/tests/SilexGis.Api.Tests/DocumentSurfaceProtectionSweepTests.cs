// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// One walk over every surface a document reaches a reader through, asking each of them the
/// same question: does it say where the cave is?
///
/// Each of those surfaces is tested on its own elsewhere, and this does not replace any of
/// them — it is the net under the whole set. Surfaces arrive one at a time and each is
/// reasoned about on its own merits; what nothing catches is the surface added later that was
/// never asked the question at all. Everything here is driven by one list, so adding a route
/// to that list is the whole cost of covering it, and a route that starts emitting a position
/// fails here rather than in a release.
///
/// The reader is a Viewer: they may read the cave and the documents on it, and hold nothing
/// over exact locations. The owner is asserted alongside as the positive leg — they do get the
/// coordinates, from the cave itself — so a fixture that quietly stopped attaching anything
/// cannot read as a passing security assertion.
///
/// Every surface additionally carries its own liveness marker, and that is not decoration: a
/// "this body has no coordinates in it" assertion is satisfied just as well by a refusal, by an
/// empty collection or by a route that answers about something else entirely. Each surface must
/// first answer successfully and name the thing it is supposed to be carrying — the document,
/// the file, the cave — before its silence about the position counts for anything.
/// </summary>
public sealed class DocumentSurfaceProtectionSweepTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    // Far enough from any obfuscation grid that a rounded position cannot contain these
    // digits by accident, and distinctive enough to find in a payload as text. They must also
    // belong to this test alone: the database is shared, the sweep reads whole collections,
    // and a cave some other test placed at the same point — legitimately unprotected there —
    // would put these digits in a payload and be read here as a disclosure.
    //
    // Unusual digits are not on their own enough to earn "this test alone". PerformanceTests
    // bulk-seeds two thousand caves whose points are drawn at random, at full double
    // precision, from longitude 20..29 and latitude 43.6..48 — and it seeds them visible and
    // genuinely unprotected, so the whole-collection export hands their exact coordinates to
    // any signed-in caller, correctly. For a point inside that box, a random neighbour lands
    // on the same five decimals often enough to matter, and a text search cannot tell that
    // neighbour's coordinate from a disclosure of this test's own. So this point sits outside
    // that box on both axes with room to spare, and has to stay outside it — which is asserted
    // rather than left to this comment, because the failure it prevents is intermittent, is
    // order-dependent, and reads as "a protected position was emitted" rather than as what it is.
    private const double ExactLat = 48.61953;
    private const double ExactLon = 19.06314;

    /// <summary>How far outside the bulk-seeded box this test's point has to stay, in degrees.</summary>
    private const double SeedClearanceDegrees = 0.5;

    private static readonly string[] Coordinates =
        [ExactLat.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture),
         ExactLon.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)];

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private string caveName = string.Empty;
    private long caveTypeId;
    private long entranceTypeId;

    public DocumentSurfaceProtectionSweepTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-sweep-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        // Checked here so that moving either side of the relationship fails at once, with a
        // sentence naming the real cause. The alternative is what happened before: the bulk
        // seeder's box is widened, this test starts failing on some runs and not others with
        // "emitted a protected position", and somebody spends a morning looking for a leak.
        var clearOfSeeding =
            ExactLon < PerformanceTests.SeedWest - SeedClearanceDegrees
            || ExactLon > PerformanceTests.SeedEast + SeedClearanceDegrees
            || ExactLat < PerformanceTests.SeedSouth - SeedClearanceDegrees
            || ExactLat > PerformanceTests.SeedNorth + SeedClearanceDegrees;

        clearOfSeeding.ShouldBeTrue(
            $"This sweep searches payloads for the text of ({ExactLat}, {ExactLon}), so that point "
            + "must belong to it alone. The performance fixture bulk-seeds unprotected caves at "
            + $"random across ({PerformanceTests.SeedWest}, {PerformanceTests.SeedSouth}) to "
            + $"({PerformanceTests.SeedEast}, {PerformanceTests.SeedNorth}), and one of those "
            + "landing on the same five decimals would be read here as a disclosure. Move this "
            + "test's point back outside that box, or narrow the box — do not relax the sweep.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sw-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sw-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sw-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"sw-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task No_document_surface_says_where_a_protected_cave_is()
    {
        var word = $"sweepword{Guid.NewGuid():N}"[..20];
        var caveId = await CreateProtectedCaveAsync();
        await AddEntranceAsync(caveId);
        var fileId = await UploadAsync(
            "sweep.txt", System.Text.Encoding.UTF8.GetBytes($"Raport. {word}."), "text/plain");
        await AttachAsync(fileId, caveId);
        var documentId = await DocumentIdOfAsync(fileId);

        // The reader is given the document outright rather than left to reach it through the
        // cave, and both halves of that matter.
        //
        // Reach through an attachment does not carry a document onto the listing and search
        // surfaces — both decline to walk it, by a documented choice — so a reader who held
        // nothing but the cave would be answered by an empty page there. An empty page has no
        // coordinates in it either, which is precisely how this sweep could go quiet without
        // failing. And the revision list is editor-only whatever the caller can read, so
        // nothing short of write makes that surface answer at all.
        //
        // What the grant deliberately does not carry is any right over the cave: an entry on
        // the document says nothing about seeing exactly where the cave is. So the caller
        // swept below is the sharpest version of the question — somebody trusted with the
        // document itself, who still must not learn the position from it.
        await GrantOverDocumentAsync(documentId, AccessAction.Read | AccessAction.Write);

        var cabinetId = await FileInNewCabinetAsync(documentId);
        await PostCommentAsync(documentId, $"A remark mentioning {word}.");

        // Text is read by a background worker, so the search surface has nothing to match on
        // until it has run. Sweeping before then sweeps an empty result set.
        await WaitForExtractionAsync(fileId);

        // The positive leg, and the reason the negatives below mean anything: the owner is
        // told exactly where the cave is — the position lives on its entrance — so the
        // coordinates are genuinely in this installation and reachable through an ordinary
        // read.
        var ownersEntrances = await BodyAsync(owner, $"/api/v1/caves/{caveId}/entrances");
        Coordinates.ShouldAllBe(c => ownersEntrances.Contains(c, StringComparison.Ordinal));

        // …and the reader really can reach the document, so a sweep that found nothing
        // because nothing was visible would fail here rather than pass silently.
        var seenDocument = await BodyAsync(reader, $"/api/v1/documents/{documentId}");
        seenDocument.ShouldContain("sweep.txt");

        // Every surface a document reaches this reader through that answers in JSON. A route
        // added later belongs in this list; that is the whole point of the list.
        //
        // The byte deliveries — file content, thumbnails, page pictures — are not here and are
        // not forgotten: none of them can be reached without a token minted for the caller, so
        // sweeping them from a list of URLs would sweep a refusal rather than a payload, and
        // what they hand over is a picture or a stream whose own metadata is the thing that
        // could carry a position. That is exactly what the tests over those routes assert,
        // against real bytes.
        //
        // A camp, and a way on inside it standing at the very point this sweep searches for.
        //
        // The camp surfaces were outside this net until now, and the leads board is the one that
        // most obviously should not have been: it is a ranked list of undefended ways into caves,
        // which is the single most sensitive thing this application assembles. What kept it out was
        // its liveness rule — a board with nothing on it is silent about positions for the
        // uninteresting reason, and nothing in the installation seeds a way on. So the sweep seeds
        // its own, protected and placed on this test's own point, which also means the camp
        // surfaces are swept against a position that belongs to this test rather than to the cave
        // they are beside.
        var campName = $"sweepcamp{Guid.NewGuid():N}"[..20];
        var campId = await CreateCampAsync(campName);
        var campTripId = await CreateTripAsync($"Push {campName}", caveId);
        await AddTripToCampAsync(campId, campTripId);
        await GrantAsync(AccessDomain.TripLogs, campTripId, AccessAction.Read);
        await GrantAsync(AccessDomain.Expeditions, campId, AccessAction.Read);

        // The marker beside each URL is what that surface must be seen carrying before its
        // silence about the position means anything: the id or the name this test put there.
        (string Url, string Marker)[] surfaces =
        [
            ($"/api/v1/caves/{caveId}", caveId.ToString()),
            ($"/api/v1/caves/{caveId}/entrances", caveId.ToString()),
            ($"/api/v1/documents/{documentId}", "sweep.txt"),
            ($"/api/v1/documents/{documentId}/comments", word),
            ($"/api/v1/search?q={word}", documentId.ToString()),
            ($"/api/v1/files/{fileId}", fileId.ToString()),
            ($"/api/v1/files/{fileId}/versions", fileId.ToString()),
            ($"/api/v1/cabinets/", cabinetId.ToString()),
            ($"/api/v1/cabinets/{cabinetId}/documents", documentId.ToString()),
            ($"/api/v1/history?entityType=feature&entityId={caveId}", caveId.ToString()),
            // An exported row carries the cave's name rather than its id, so that is what
            // proves this cave is in the file being swept.
            ("/api/v1/export/caves?format=geojson", caveName),
            // The camp surfaces. A camp is a set of trips, and what it says about them is built
            // from what those trips touched — so the position that must not escape one is the
            // cave's, and the camp here has a trip on this sweep's protected cave.
            //
            // Only two of the four are here, and which two were left out is worth recording,
            // because their absence looks like the oversight this sweep exists to prevent. Both
            // the leads board and the camp map hand a reader without exact view nothing at all
            // rather than something coarse: measured while adding this, against a camp and a trip
            // the reader held Read on, the board gave the owner one lead and the reader none, and
            // the map answered the reader an empty feature collection. That is the right answer —
            // a way on is itself a position — but it means neither can be swept from here, because
            // "no coordinates in an empty body" is exactly the uninteresting pass the liveness rule
            // above refuses to accept. They are covered by their own tests, which assert that
            // emptiness deliberately. If either ever starts drawing for such a reader, it belongs
            // in this list on the same day.
            ($"/api/v1/expeditions/{campId}", campName),
            ("/api/v1/expeditions/", campName),
        ];

        foreach (var (surface, marker) in surfaces)
        {
            var body = await LiveBodyAsync(reader, surface, marker);
            foreach (var coordinate in Coordinates)
            {
                body.ShouldNotContain(
                    coordinate,
                    Case.Sensitive,
                    $"{surface} emitted a protected position to a caller without exact location");
            }
        }

        // The interchange export is swept here rather than from the list above because it is
        // reached with a POST carrying a decision per cave, and a list of URLs cannot express
        // that. It is swept under every treatment it offers, not only the one that withholds
        // the position: the treatment that keeps the cave on the map's coarse grid is the one
        // that could disclose a surveyed coordinate by getting the snap wrong, so it is the
        // one worth sweeping most.
        foreach (var treatment in new[] { "grid_position", "no_position", "omit" })
        {
            var response = await reader.PostAsJsonAsync(
                "/api/v1/export/caves/karstlink",
                new { search = caveName, treatmentForAll = treatment });
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.ShouldBeTrue(
                $"the interchange export answered {(int)response.StatusCode} under '{treatment}': {body}");

            // Liveness: under the two treatments that keep the cave, the file has to be seen
            // carrying it before its silence about the position counts for anything. Under
            // "omit" the cave is correctly absent, so what proves the sweep is live there is
            // that the file says a cave was left out.
            body.ShouldContain(
                treatment == "omit" ? "\"omittedCaveCount\": 1" : caveName,
                Case.Sensitive,
                $"the interchange export under '{treatment}' carried nothing this sweep could assert about");

            foreach (var coordinate in Coordinates)
            {
                body.ShouldNotContain(
                    coordinate,
                    Case.Sensitive,
                    $"the interchange export under '{treatment}' emitted a protected position "
                    + "to a caller without exact location");
            }
        }

        // The attachment listing is left out of the sweep above and asserted here instead,
        // because for this caller the correct answer is nothing at all. What that listing
        // publishes is the pairing itself — which documents point at this cave — and a caller
        // who may not place the cave exactly is not told it. So the empty body is the rule
        // working rather than the sweep going quiet, and saying which of the two it is takes
        // both callers: the pairing is genuinely there, and it is genuinely not shown.
        var attachments = $"/api/v1/attachments/?entityType=feature&entityId={caveId}";
        var ownersAttachments = await LiveBodyAsync(owner, attachments, fileId.ToString());
        ownersAttachments.ShouldNotContain(
            Coordinates[0],
            Case.Sensitive,
            "the attachment listing carried the position of the cave it lists attachments for");

        var readersAttachments = await BodyAsync(reader, attachments);
        readersAttachments.ShouldNotContain(
            fileId.ToString(),
            Case.Insensitive,
            "a caller who may not place the cave was told which document hangs on it");
        foreach (var coordinate in Coordinates)
        {
            readersAttachments.ShouldNotContain(coordinate, Case.Sensitive);
        }
    }

    // ---- helpers ----

    /// <summary>
    /// The body of a surface, whatever it answers. A refusal is an acceptable answer here —
    /// this asks what is emitted, not who may read what — but a server fault is not, because
    /// a route that fell over says nothing about what it would have emitted.
    /// </summary>
    private static async Task<string> BodyAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBeLessThan(500, $"{url} failed: {body}");
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound, $"{url} is not a route any more");
        return body;
    }

    /// <summary>
    /// The body of a surface that has been shown to be carrying this test's data. Unlike
    /// <see cref="BodyAsync"/> a refusal is not acceptable and neither is an empty answer:
    /// the caller is about to assert that a string is absent, and that assertion is worth
    /// nothing unless something is present.
    /// </summary>
    private static async Task<string> LiveBodyAsync(HttpClient client, string url, string marker)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"{url} answered {(int)response.StatusCode}, so it was swept without emitting anything: {body}");
        body.ShouldContain(
            marker,
            Case.Insensitive,
            $"{url} did not carry '{marker}', so sweeping it asserted nothing about this document");
        return body;
    }

    private async Task<Guid> CreateProtectedCaveAsync()
    {
        caveName = $"Sweep {Guid.NewGuid():N}"[..30];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = caveName,
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The cave's position: it hangs on an entrance, not on the cave row.</summary>
    private async Task AddEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { ExactLon, ExactLat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachAsync(Guid fileId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            caption = (string?)null,
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> FileInNewCabinetAsync(Guid documentId)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/cabinets/", new
        {
            name = $"Sweep {Guid.NewGuid():N}"[..24],
            parentId = (Guid?)null,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var cabinetId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var filed = await owner.PutAsync(
            $"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null);
        ((int)filed.StatusCode).ShouldBeLessThan(
            300, await filed.Content.ReadAsStringAsync());
        return cabinetId;
    }

    private async Task PostCommentAsync(Guid documentId, string body)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments",
            new { parentId = (Guid?)null, body, anchorKind = "whole", anchor = (object?)null, anchorFileId = (Guid?)null });
        ((int)response.StatusCode).ShouldBeLessThan(
            300, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking()
            .FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    /// <summary>
    /// Gives the reader an entry over this one document, and over nothing else.
    /// </summary>
    private async Task<Guid> CreateCampAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name,
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            caveIds = new[] { caveId },
            geom = (object?)null,
            visibility = "authenticated",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddTripToCampAsync(Guid campId, Guid tripId)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An object-scoped grant. The feature world anchors on its own column and every other world
    /// on the generic one — a database check constraint enforces that, so getting it wrong fails
    /// on save rather than quietly granting nothing.
    /// </summary>
    private async Task GrantAsync(AccessDomain domain, Guid scopeId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var forFeature = domain == AccessDomain.Features;
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = readerId,
            Effect = AccessEffect.Allow,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = forFeature ? scopeId : null,
            ScopeId = forFeature ? null : scopeId,
        });
        await db.SaveChangesAsync();
    }

    private async Task GrantOverDocumentAsync(Guid documentId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = readerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = documentId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Waits until the background worker has read the upload's text.</summary>
    private async Task WaitForExtractionAsync(Guid fileId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
                var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
                if (file.TextExtraction is not TextExtractionState.Pending)
                {
                    file.TextExtraction.ShouldBe(TextExtractionState.Extracted);
                    return;
                }
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the file's text was never read");
            await Task.Delay(200);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory?.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
