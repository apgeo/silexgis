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
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A held phone upload across every part an upload carries — rows, people, photos, the links that
/// hang them on places, trip recordings and their scans — and the decisions that reach more than one
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

    private async Task<JsonElement> UploadAsync(object body)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", body);
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
