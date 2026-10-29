// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A held phone upload across every part an upload carries — rows, people, photos, the links that
/// hang them on places, trip recordings and their scans, raster maps and their pins — and the decisions that reach more than one
/// line: a line that needs two held rows, a dependency kept by an earlier upload, a rejection the
/// reviewer takes back, and two decisions racing for the same lines.
/// </summary>
/// <remarks>Every picture here is generated, with an invented position.</remarks>
public sealed class UploadReviewSectionsTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid set;
    private string marker = null!;

    public UploadReviewSectionsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-reviewsections-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"urs-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"urs-up-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"urs-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"urs-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var marked = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = true });
        marked.StatusCode.ShouldBe(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_section_is_held_nothing_of_it_exists_until_accepted_and_then_it_all_does()
    {
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var deviceUser = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var recordingId = Guid.CreateVersion7();
        var pointId = Guid.CreateVersion7();
        var bytes = Corpus.GeotaggedJpeg(45.21, 25.37);
        var staged = await StageAsync("IMG_0901.jpg", bytes);

        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "Every part"), Entrance(entranceId, caveId) },
            people = new[] { new { deviceUserId = deviceUser, username = $"held-{marker}", firstName = "Ana" } },
            documents = new[] { Photo(documentId, bytes, staged) },
            documentLinks = new[] { Link(documentId, caveId) },
            tripRecordings = new[] { Recording(recordingId, caveId) },
            tripPoints = new[] { Scan(pointId, recordingId, entranceId) },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();
        foreach (var section in new[] { "rows", "people", "documents", "documentLinks", "tripRecordings", "tripPoints" })
        {
            var results = answer.GetProperty(section).EnumerateArray().ToList();
            results.ShouldNotBeEmpty(section);
            results.ShouldAllBe(r => r.GetProperty("status").GetString() == "held", section);
            results.ShouldAllBe(r => r.GetProperty("reviewId").GetGuid() != Guid.Empty, section);
        }

        (await CountWrittenAsync(caveId, entranceId, deviceUser, documentId, recordingId, pointId))
            .ShouldBe(new Written(0, 0, 0, 0, 0, 0));

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var decided = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided");
        decided.EnumerateArray().Select(l => (l.GetProperty("section").GetString(), l.GetProperty("decision").GetString()))
            .ShouldAllBe(x => x.Item2 == "accepted");
        decided.GetArrayLength().ShouldBe(7);

        (await CountWrittenAsync(caveId, entranceId, deviceUser, documentId, recordingId, pointId))
            .ShouldBe(new Written(2, 1, 1, 1, 1, 1));

        // The recording is made into a trip by one job for the batch, as an upload nobody held.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TripRecordingConvert)
            .Select(j => j.Payload)
            .ToListAsync();
        jobs.Count(p => JsonSerializer.Deserialize<TripRecordingConvertPayload>(p, JsonSerializerOptions.Web)!.ImportBatchId == batchId)
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_link_needs_both_its_held_photo_and_its_held_cave()
    {
        // Rejecting the cave rejects the link with it, though the link was first recorded as
        // depending on the photo; the photo itself waits on.
        var caveId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var bytes = Corpus.GeotaggedJpeg(45.22, 25.38);
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "Linked cave") },
            documents = new[] { Photo(documentId, bytes, await StageAsync("IMG_0902.jpg", bytes)) },
            documentLinks = new[] { Link(documentId, caveId) },
        });
        var caveReview = ReviewIdOf(answer, "rows");
        var documentReview = ReviewIdOf(answer, "documents");
        var linkReview = ReviewIdOf(answer, "documentLinks");

        var detail = await reviewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/review/batches/{answer.GetProperty("importBatchId").GetGuid()}");
        detail.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("reviewId").GetGuid() == linkReview)
            .GetProperty("dependsOnReviewIds").EnumerateArray().Select(x => x.GetGuid())
            .ShouldBe([documentReview, caveReview], ignoreOrder: true);

        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{caveReview}/reject", new { });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray()
            .Select(l => l.GetProperty("reviewId").GetGuid())
            .ShouldBe([caveReview, linkReview], ignoreOrder: true);
        (await DecisionOfAsync(documentReview)).ShouldBe(ImportReviewDecision.Pending);

        // Accepting a link pulls in both rows it needs.
        var secondCave = Guid.CreateVersion7();
        var secondDocument = Guid.CreateVersion7();
        var secondBytes = Corpus.GeotaggedJpeg(45.23, 25.39);
        var second = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(secondCave, "Second linked cave") },
            documents = new[] { Photo(secondDocument, secondBytes, await StageAsync("IMG_0903.jpg", secondBytes)) },
            documentLinks = new[] { Link(secondDocument, secondCave) },
        });
        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{ReviewIdOf(second, "documentLinks")}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var lines = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray().ToList();
        lines.Count.ShouldBe(3);
        lines.ShouldAllBe(l => l.GetProperty("decision").GetString() == "accepted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await (from link in db.Attachments
                join file in db.StoredFiles on link.FileId equals file.Id
                join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                where link.FeatureId == secondCave && version.DocumentId == secondDocument
                select link.Id).CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Accepting_a_batch_writes_a_place_after_the_cave_an_earlier_upload_left_waiting()
    {
        // The cave waits from the first upload. The second sends it again unchanged — a repeat,
        // nothing new kept — beside an unrelated new cave and a place under the first one.
        var caveId = Guid.CreateVersion7();
        var first = await UploadRowsAsync(Cave(caveId, "Waiting since yesterday"));
        var firstBatch = first.GetProperty("importBatchId").GetGuid();

        var otherId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var second = await UploadRowsAsync(
            Cave(caveId, "Waiting since yesterday"), Cave(otherId, "Unrelated"), Entrance(entranceId, caveId));
        var secondBatch = second.GetProperty("importBatchId").GetGuid();
        second.GetProperty("rows")[0].GetProperty("reviewId").GetGuid()
            .ShouldBe(first.GetProperty("rows")[0].GetProperty("reviewId").GetGuid());

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{secondBatch}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("decided").EnumerateArray().Select(l => l.GetProperty("decision").GetString())
            .ShouldBe(["accepted", "accepted", "accepted"]);
        result.GetProperty("batches").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .ShouldBe([firstBatch, secondBatch], ignoreOrder: true);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().CountAsync(f => f.Id == caveId || f.Id == otherId || f.Id == entranceId))
            .ShouldBe(3);
        (await db.CaveEntrances.AsNoTracking().SingleAsync(e => e.Id == entranceId)).CaveFeatureId.ShouldBe(caveId);
    }

    [Fact]
    public async Task A_rejected_photo_can_be_accepted_again_while_its_bytes_are_kept_and_not_after()
    {
        var bytes = Corpus.GeotaggedJpeg(45.24, 25.40);
        var staged = await StageAsync("IMG_0904.jpg", bytes);
        var documentId = Guid.CreateVersion7();
        var lateBytes = Corpus.GeotaggedJpeg(45.25, 25.41);
        var lateDocument = Guid.CreateVersion7();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            documents = new[]
            {
                Photo(documentId, bytes, staged),
                Photo(lateDocument, lateBytes, await StageAsync("IMG_0905.jpg", lateBytes)),
            },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();
        var reviews = answer.GetProperty("documents").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()).ToList();

        // The queue shows what the waiting photos weigh.
        var queued = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        queued.GetProperty("batch").GetProperty("heldBytes").GetInt64().ShouldBe(bytes.Length + lateBytes.Length);

        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}"))
            .GetProperty("batch").GetProperty("heldBytes").GetInt64().ShouldBe(0);

        // The second was turned down longer ago than the bytes are kept for.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.ImportBatchItems.Where(i => i.ReviewId == reviews[1])
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DecidedAt, DateTimeOffset.UtcNow.AddDays(-31)));
        }

        var again = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviews[0]}/accept", new { note = "On second look." });
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var result = await again.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("decided")[0].GetProperty("decision").GetString().ShouldBe("accepted");
        result.GetProperty("batches")[0].GetProperty("reviewState").GetString().ShouldBe("partlyAccepted");

        var late = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviews[1]}/accept", new { });
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict, await late.Content.ReadAsStringAsync());
        (await CodeAsync(late)).ShouldBe("review.not_pending");

        // Rejecting a decided line again is still refused.
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviews[1]}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == documentId)).ShouldBeTrue();
        (await read.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == lateDocument)).ShouldBeFalse();
    }

    [Fact]
    public async Task Two_decisions_racing_for_one_line_decide_it_once()
    {
        for (var round = 0; round < 3; round++)
        {
            var caveId = Guid.CreateVersion7();
            var answer = await UploadRowsAsync(Cave(caveId, $"Raced {round}"));
            var reviewId = answer.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

            var responses = await Task.WhenAll(
                reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { }),
                reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { }),
                reviewer.PostAsJsonAsync($"/api/v1/review/batches/{answer.GetProperty("importBatchId").GetGuid()}/accept", new { }));
            var statuses = responses.Select(r => r.StatusCode).ToList();
            statuses.Count(s => s == HttpStatusCode.OK).ShouldBe(1, string.Join(", ", statuses));
            statuses.Count(s => s == HttpStatusCode.Conflict).ShouldBe(2, string.Join(", ", statuses));

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await DecisionOfAsync(reviewId)).ShouldBe(ImportReviewDecision.Accepted);
            (await db.Features.IgnoreQueryFilters().CountAsync(f => f.Id == caveId)).ShouldBe(1);
            (await db.AuditEntries.AsNoTracking().CountAsync(a => a.EntityId == caveId.ToString() && a.Action == AuditActions.Created))
                .ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_raster_map_and_its_pin_are_held_and_accepting_the_pin_writes_what_it_needs_which_an_undo_takes_back()
    {
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var mapId = Guid.CreateVersion7();
        var pinId = Guid.CreateVersion7();
        var picture = Corpus.Jpeg();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "Mapped cave"), Entrance(entranceId, caveId) },
            rasterMaps = new[] { RasterMap(mapId, caveId, picture, await StageAsync("plan.jpg", picture)) },
            rasterPins = new[] { new { id = pinId, rasterMapId = mapId, placeId = entranceId, x = 321, y = 654 } },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();
        foreach (var section in new[] { "rasterMaps", "rasterPins" })
        {
            answer.GetProperty(section).EnumerateArray().ShouldAllBe(r => r.GetProperty("status").GetString() == "held", section);
        }

        var mapReview = ReviewIdOf(answer, "rasterMaps");
        var pinReview = ReviewIdOf(answer, "rasterPins");
        var entranceReview = answer.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == entranceId).GetProperty("reviewId").GetGuid();
        (await RasterCountsAsync(mapId, pinId)).ShouldBe((0, 0));

        // The pin waits on its held map and its held place; the map on its held cave.
        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        detail.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("reviewId").GetGuid() == pinReview)
            .GetProperty("dependsOnReviewIds").EnumerateArray().Select(x => x.GetGuid())
            .ShouldBe([mapReview, entranceReview], ignoreOrder: true);

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{pinReview}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var decided = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray().ToList();
        decided.Count.ShouldBe(4);
        decided.ShouldAllBe(l => l.GetProperty("decision").GetString() == "accepted");
        (await RasterCountsAsync(mapId, pinId)).ShouldBe((1, 1));

        // The accepted lines name what they wrote, as the lines of an upload nobody held do.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == mapReview)).RasterMapId.ShouldBe(mapId);
            (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == pinReview)).RasterPinId.ShouldBe(pinId);
        }

        // The uploader's phone reads the decision with the revision the pin now has.
        var told = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{set}/reviews");
        var pinTold = told.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("reviewId").GetGuid() == pinReview);
        pinTold.GetProperty("entity").GetString().ShouldBe("rasterPins");
        pinTold.GetProperty("outcome").GetString().ShouldBe("accepted");
        pinTold.GetProperty("revision").ValueKind.ShouldBe(JsonValueKind.String);

        var undone = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        var undoAnswer = await undone.Content.ReadAsStringAsync();
        undone.StatusCode.ShouldBe(HttpStatusCode.OK, undoAnswer);
        var items = JsonDocument.Parse(undoAnswer).RootElement.GetProperty("items").EnumerateArray().ToList();
        items.ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        items.Count(i => i.TryGetProperty("rasterMapId", out var id) && id.ValueKind == JsonValueKind.String).ShouldBe(1);
        items.Count(i => i.TryGetProperty("rasterPinId", out var id) && id.ValueKind == JsonValueKind.String).ShouldBe(1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == mapId)).DeletedAt.ShouldNotBeNull();
            (await db.ResLinks.AsNoTracking().SingleAsync(p => p.Id == pinId)).DeletedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task A_held_pin_on_a_place_a_reviewer_may_not_place_is_shown_with_its_pixel()
    {
        // A protected cave and entrance the uploader made before being placed under review; then,
        // held, a map of the cave with a pin on the entrance. A reviewer who may read the entrance
        // but not place it is shown where on the map the entrance lies, as everything else held is.
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        await MarkAsync(false);
        await UploadRowsAsync(Cave(caveId, "Guarded mapped cave"), Entrance(entranceId, caveId));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(f => f.LocationProtected, true)
                    .SetProperty(f => f.IsProtectedEffective, true));
        }

        await MarkAsync(true);
        var mapId = Guid.CreateVersion7();
        var pinId = Guid.CreateVersion7();
        var picture = Corpus.Jpeg();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rasterMaps = new[] { RasterMap(mapId, caveId, picture, await StageAsync("guarded.jpg", picture)) },
            rasterPins = new[] { new { id = pinId, rasterMapId = mapId, placeId = entranceId, x = 777123, y = 888456 } },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"urs-pin-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"urs-pin-{marker}@t.local");
        await GrantAsync(readerId, AccessDomain.UploadReview);
        await GrantAsync(readerId, AccessDomain.Features);

        var exact = await reviewer.GetStringAsync($"/api/v1/review/batches/{batchId}");
        exact.ShouldContain("777123");
        var seen = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        var pin = JsonDocument.Parse(seen).RootElement.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("rowId").GetGuid() == pinId).GetProperty("proposal").GetProperty("pin");
        pin.GetProperty("placeId").GetGuid().ShouldBe(entranceId);
        pin.GetProperty("x").GetInt32().ShouldBe(777123);
        pin.GetProperty("y").GetInt32().ShouldBe(888456);

        // Outside review the entrance is still snapped for the same account.
        var envelope = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/features/{entranceId}");
        envelope.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_held_photo_link_on_a_recording_a_reviewer_may_not_read_is_shown_whole()
    {
        // A recording the uploader sent before being placed under review, kept private; then, held,
        // a photo hung on it. Whether the link may be shown is the recording's to decide.
        var caveId = Guid.CreateVersion7();
        var recordingId = Guid.CreateVersion7();
        await MarkAsync(false);
        await UploadRowsAsync(Cave(caveId, "Recorded cave"));

        // A link to a recording is taken only when the recording's cave is in the set's selection.
        var created = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Recorded {marker}",
            uploadVisibility = "private",
            rootFeatureIds = new[] { caveId },
            settings = new { },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var recordedSet = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var recorded = await UploadAsync(into: recordedSet, body: new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new[] { Recording(recordingId, caveId) },
        });
        recorded.GetProperty("tripRecordings")[0].GetProperty("status").GetString().ShouldBe("created");

        await MarkAsync(true);
        var documentId = Guid.CreateVersion7();
        var bytes = Corpus.GeotaggedJpeg(45.24, 25.31);
        var answer = await UploadAsync(into: recordedSet, body: new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            documents = new[] { Photo(documentId, bytes, await StageAsync("IMG_0910.jpg", bytes)) },
            documentLinks = new[] { new { documentId, target = new { kind = "tripRecording", id = recordingId }, deleted = false } },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();
        var linkReview = ReviewIdOf(answer, "documentLinks");

        // May read features and the queue, but no trip log.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"urs-rec-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"urs-rec-{marker}@t.local");
        await GrantAsync(readerId, AccessDomain.UploadReview);

        var exact = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        exact.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reviewId").GetGuid() == linkReview)
            .GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Object);
        var seen = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        var link = seen.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reviewId").GetGuid() == linkReview)
            .GetProperty("proposal");
        link.ValueKind.ShouldBe(JsonValueKind.Object);
        link.GetRawText().ShouldContain(recordingId.ToString());

        // Outside review the same account still may not read the recording: the cave page's list
        // of recordings, which shows it to the uploader, shows it nothing.
        (await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-recordings?caveId={caveId}")).GetProperty("items")
            .EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ShouldContain(recordingId);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-recordings?caveId={caveId}")).GetProperty("items")
            .GetArrayLength().ShouldBe(0);
    }

    private sealed record Written(int Features, int People, int Documents, int Links, int Recordings, int Points);

    private async Task<Written> CountWrittenAsync(
        Guid caveId, Guid entranceId, Guid deviceUser, Guid documentId, Guid recordingId, Guid pointId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var links = await (from link in db.Attachments
                           join file in db.StoredFiles on link.FileId equals file.Id
                           join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                           where link.FeatureId == caveId && version.DocumentId == documentId
                           select link.Id).CountAsync();
        return new Written(
            await db.Features.IgnoreQueryFilters().CountAsync(f => f.Id == caveId || f.Id == entranceId),
            await db.DevicePersons.CountAsync(p => p.DeviceUserId == deviceUser),
            await db.Documents.IgnoreQueryFilters().CountAsync(d => d.Id == documentId),
            links,
            await db.TripRecordings.IgnoreQueryFilters().CountAsync(r => r.Id == recordingId),
            await db.TripRecordingPoints.IgnoreQueryFilters().CountAsync(p => p.Id == pointId));
    }

    private async Task<(int Maps, int Pins)> RasterCountsAsync(Guid mapId, Guid pinId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.RasterMaps.IgnoreQueryFilters().CountAsync(m => m.Id == mapId),
            await db.ResLinks.CountAsync(p => p.Id == pinId));
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task GrantAsync(Guid userId, AccessDomain domain)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = domain,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private static object RasterMap(Guid id, Guid caveId, byte[] picture, Guid staged) => new
    {
        id,
        caveId,
        title = "Held plan",
        mapType = "planView",
        fileName = "plan.jpg",
        sha256 = Convert.ToHexStringLower(SHA256.HashData(picture)),
        sizeBytes = picture.Length,
        stagedUploadId = staged,
    };

    private async Task<ImportReviewDecision?> DecisionOfAsync(Guid reviewId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ImportBatchItems.AsNoTracking().Where(i => i.ReviewId == reviewId)
            .Select(i => i.ReviewDecision).SingleAsync();
    }

    private async Task<Guid> StageAsync(string fileName, byte[] bytes)
    {
        var opened = await uploader.PostAsJsonAsync("/api/v1/files/uploads", new
        {
            fileName,
            sizeBytes = bytes.Length,
            stageForSync = true,
        });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await uploader.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private Task<JsonElement> UploadRowsAsync(params object[] rows) => UploadAsync(new
    {
        batchId = Guid.CreateVersion7(),
        contractVersion = 1,
        clientFeatures = new[] { "held" },
        rows,
    });

    private async Task<JsonElement> UploadAsync(object body, Guid? into = null)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{into ?? set}/upload", body);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static Guid ReviewIdOf(JsonElement answer, string section) =>
        answer.GetProperty(section).EnumerateArray().Single().GetProperty("reviewId").GetGuid();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object Entrance(Guid id, Guid caveId) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name = "Intrare",
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = new[] { 22.52, 45.62 } },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    private static object Photo(Guid id, byte[] bytes, Guid staged) => new
    {
        id,
        title = "Held picture",
        fileName = "held.jpg",
        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        sizeBytes = bytes.Length,
        stagedUploadId = staged,
    };

    private static object Link(Guid documentId, Guid featureId) =>
        new { documentId, target = new { kind = "feature", id = featureId }, deleted = false };

    private static object Recording(Guid id, Guid caveId) => new
    {
        id,
        caveId,
        title = "Held trip",
        description = "Invented for the test",
        startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
        createdByDeviceUserId = Guid.CreateVersion7(),
    };

    private static object Scan(Guid id, Guid recordingId, Guid placeId) => new
    {
        id,
        recordingId,
        placeId,
        scannedAt = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero),
        notes = "scan at the entrance",
    };

    public ValueTask DisposeAsync()
    {
        uploader?.Dispose();
        reviewer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
