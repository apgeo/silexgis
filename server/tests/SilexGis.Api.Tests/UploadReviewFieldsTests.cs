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
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Accepting part of a held edit of a photo, a recording, a scan or a raster map: the batch view
/// draws each field the row has now beside the proposal, a reviewer's subset writes only those
/// fields, a line accepted without one still goes whole, and the kinds that are one fact refuse a
/// subset.
/// </summary>
/// <remarks>Every picture here is generated, with an invented position.</remarks>
public sealed class UploadReviewFieldsTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid set;
    private string marker = null!;

    public UploadReviewFieldsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-reviewfields-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"urf-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"urf-up-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"urf-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"urf-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_photo_edit_accepted_by_its_caption_alone_keeps_its_title_and_its_file_and_one_left_whole_takes_all()
    {
        var documentId = Guid.CreateVersion7();
        var original = Corpus.GeotaggedJpeg(45.41, 25.61);
        var created = await UploadAsync(new
        {
            documents = new[] { Photo(documentId, "Before", "Old caption", original, await StageAsync("IMG_2001.jpg", original)) },
        });
        created.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("created");

        await MarkAsync(true);
        var replacement = Corpus.GeotaggedJpeg(45.42, 25.62);
        var replacementStaged = await StageAsync("IMG_2002.jpg", replacement);
        var held = await UploadAsync(held: true, body: new
        {
            documents = new[]
            {
                Photo(documentId, "After", "New caption", replacement, replacementStaged)
                    with { BaseRevision = await DocumentRevisionAsync(documentId) },
            },
        });
        var reviewId = ReviewIdOf(held, "documents");

        var line = await LineAsync(held, reviewId);
        Strings(line.GetProperty("acceptableFields")).ShouldBe(["title", "description", "file"]);
        var fields = FieldsOf(line);
        fields["title"].GetProperty("current").GetString().ShouldBe("Before");
        fields["title"].GetProperty("proposed").GetString().ShouldBe("After");
        fields["title"].GetProperty("differs").GetBoolean().ShouldBeTrue();
        fields["description"].GetProperty("current").GetString().ShouldBe("Old caption");
        fields["description"].GetProperty("proposed").GetString().ShouldBe("New caption");
        fields["file"].GetProperty("proposed").GetProperty("fileName").GetString().ShouldBe("After.jpg");
        fields["file"].GetProperty("current").GetProperty("fileName").GetString().ShouldBe("Before.jpg");

        await AcceptAsync(reviewId, "description");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId)).Title.ShouldBe("Before");
            (await db.PhotoDetails.AsNoTracking().SingleAsync(p => p.DocumentId == documentId)).Caption.ShouldBe("New caption");
            (await db.DocumentVersions.AsNoTracking().CountAsync(v => v.DocumentId == documentId)).ShouldBe(1);
            (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == reviewId)).AcceptedFields.ShouldBe(["description"]);

            // The new file was not taken: its bytes are given back to the sweep, not kept for ever.
            (await db.UploadSessions.AsNoTracking().SingleAsync(s => s.Id == replacementStaged)).ExpiresAt
                .ShouldBeLessThan(HeldUploadBytes.Parked);
        }

        // A line accepted without naming fields is written whole.
        var again = Corpus.GeotaggedJpeg(45.43, 25.63);
        var whole = await UploadAsync(held: true, body: new
        {
            documents = new[]
            {
                Photo(documentId, "Whole", "Whole caption", again, await StageAsync("IMG_2003.jpg", again))
                    with { BaseRevision = await DocumentRevisionAsync(documentId) },
            },
        });
        await AcceptAsync(ReviewIdOf(whole, "documents"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId)).Title.ShouldBe("Whole");
            (await db.PhotoDetails.AsNoTracking().SingleAsync(p => p.DocumentId == documentId)).Caption.ShouldBe("Whole caption");
            (await db.DocumentVersions.AsNoTracking().CountAsync(v => v.DocumentId == documentId)).ShouldBe(2);
        }
    }

    [Fact]
    public async Task A_recording_edit_accepted_by_its_log_alone_keeps_everything_else_and_one_left_whole_takes_all()
    {
        var caveId = Guid.CreateVersion7();
        var recordingId = Guid.CreateVersion7();
        var created = await UploadAsync(new
        {
            rows = new[] { Cave(caveId, "Recorded cave") },
            tripRecordings = new[] { Recording(recordingId, caveId, "First title", "First log", Start) },
        });
        var revision = created.GetProperty("tripRecordings")[0].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(held: true, body: new
        {
            tripRecordings = new[] { Recording(recordingId, caveId, "Second title", "Second log", Start.AddHours(1)) with { BaseRevision = revision } },
        });
        var reviewId = ReviewIdOf(held, "tripRecordings");

        var line = await LineAsync(held, reviewId);
        Strings(line.GetProperty("acceptableFields")).ShouldBe(["title", "description", "log", "times", "cave"]);
        var fields = FieldsOf(line);
        fields["title"].GetProperty("differs").GetBoolean().ShouldBeTrue();
        fields["log"].GetProperty("current").GetString().ShouldBe("First log");
        fields["times"].GetProperty("differs").GetBoolean().ShouldBeTrue();
        fields["cave"].GetProperty("current").GetGuid().ShouldBe(caveId);
        fields["cave"].GetProperty("differs").GetBoolean().ShouldBeFalse();
        fields["description"].GetProperty("differs").GetBoolean().ShouldBeFalse();

        await AcceptAsync(reviewId, "log");
        var after = await RecordingAsync(recordingId);
        (after.Title, after.Log, after.StartedAt, after.CaveFeatureId).ShouldBe(("First title", "Second log", Start, caveId));

        var whole = await UploadAsync(held: true, body: new
        {
            tripRecordings = new[] { Recording(recordingId, caveId, "Third title", "Third log", Start.AddHours(2)) with { BaseRevision = after.UpdatedAt } },
        });
        await AcceptAsync(ReviewIdOf(whole, "tripRecordings"));
        var last = await RecordingAsync(recordingId);
        (last.Title, last.Log, last.StartedAt).ShouldBe(("Third title", "Third log", Start.AddHours(2)));
    }

    [Fact]
    public async Task A_scan_edit_accepted_by_notes_and_time_keeps_its_place_and_an_earlier_place_the_viewer_may_not_read_is_left_out()
    {
        var caveId = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var recordingId = Guid.CreateVersion7();
        var pointId = Guid.CreateVersion7();
        var created = await UploadAsync(new
        {
            rows = new[] { Cave(caveId, "Scanned cave"), Entrance(first, caveId, "Intrarea A"), Entrance(second, caveId, "Intrarea B") },
            tripRecordings = new[] { Recording(recordingId, caveId, "Scanned trip", null, Start) },
            tripPoints = new[] { Scan(pointId, recordingId, first, "first note", Start.AddMinutes(10)) },
        });
        var revision = created.GetProperty("tripPoints")[0].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(held: true, body: new
        {
            tripPoints = new[] { Scan(pointId, recordingId, second, "second note", Start.AddMinutes(20)) with { BaseRevision = revision } },
        });
        var reviewId = ReviewIdOf(held, "tripPoints");

        var line = await LineAsync(held, reviewId);
        Strings(line.GetProperty("acceptableFields")).ShouldBe(["notes", "place", "time"]);
        FieldsOf(line)["place"].GetProperty("current").GetGuid().ShouldBe(first);

        // A reader of the queue who may read the trip and the new place but not the earlier one is
        // shown the comparison without it.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"urf-rd-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"urf-rd-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.TripLogs);
        await GrantAsync(readerId, AccessEffect.Deny, AccessDomain.Features, first);
        var blindRaw = await reader.GetStringAsync($"/api/v1/review/batches/{held.GetProperty("importBatchId").GetGuid()}");
        var blind = JsonDocument.Parse(blindRaw).RootElement.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("reviewId").GetGuid() == reviewId);
        blind.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Object);
        var place = FieldsOf(blind)["place"];
        place.GetProperty("currentWithheld").GetBoolean().ShouldBeTrue();
        place.GetProperty("differs").GetBoolean().ShouldBeTrue();
        (place.TryGetProperty("current", out var current) ? current.ValueKind : JsonValueKind.Null).ShouldBe(JsonValueKind.Null);
        place.GetProperty("proposed").GetGuid().ShouldBe(second);
        blindRaw.ShouldNotContain(first.ToString());

        await AcceptAsync(reviewId, "notes", "time");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var after = await db.TripRecordingPoints.AsNoTracking().SingleAsync(p => p.Id == pointId);
        (after.Notes, after.ScannedAt, after.PlaceFeatureId).ShouldBe(("second note", Start.AddMinutes(20), (Guid?)first));
    }

    [Fact]
    public async Task A_raster_map_edit_accepted_by_its_type_alone_keeps_its_title_and_order()
    {
        var caveId = Guid.CreateVersion7();
        var mapId = Guid.CreateVersion7();
        var picture = Corpus.Jpeg();
        var created = await UploadAsync(new
        {
            rows = new[] { Cave(caveId, "Mapped cave") },
            rasterMaps = new[]
            {
                new
                {
                    id = mapId,
                    caveId,
                    title = "Plan one",
                    mapType = "planView",
                    sortOrder = 1,
                    fileName = "plan.jpg",
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(picture)),
                    sizeBytes = picture.Length,
                    stagedUploadId = await StageAsync("plan.jpg", picture),
                },
            },
        });
        var revision = created.GetProperty("rasterMaps")[0].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(held: true, body: new
        {
            rasterMaps = new[] { new { id = mapId, baseRevision = revision, caveId, title = "Plan two", mapType = "extendedProfile", sortOrder = 5 } },
        });
        var reviewId = ReviewIdOf(held, "rasterMaps");

        var line = await LineAsync(held, reviewId);
        Strings(line.GetProperty("acceptableFields")).ShouldBe(["title", "mapType", "sortOrder", "caveArea"]);
        var fields = FieldsOf(line);
        fields["mapType"].GetProperty("current").GetString().ShouldBe("planView");
        fields["mapType"].GetProperty("proposed").GetString().ShouldBe("extendedProfile");
        fields["caveArea"].GetProperty("differs").GetBoolean().ShouldBeFalse();

        await AcceptAsync(reviewId, "mapType");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var after = await db.RasterMaps.AsNoTracking().SingleAsync(m => m.Id == mapId);
        (after.Title, after.MapType, after.SortOrder).ShouldBe(("Plan one", RasterMapType.ExtendedProfile, 1));
    }

    [Fact]
    public async Task A_create_and_a_kind_that_is_one_fact_are_accepted_only_whole()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var recordingId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var bytes = Corpus.GeotaggedJpeg(45.44, 25.64);
        var held = await UploadAsync(held: true, body: new
        {
            rows = new[] { Cave(caveId, "Whole cave") },
            documents = new[] { Photo(documentId, "New photo", null, bytes, await StageAsync("IMG_2004.jpg", bytes)) },
            documentLinks = new[] { new { documentId, target = new { kind = "feature", id = caveId }, deleted = false } },
            tripRecordings = new[] { Recording(recordingId, caveId, "New trip", null, Start) },
        });

        foreach (var (section, field) in new[] { ("documentLinks", "caption"), ("tripRecordings", "title"), ("documents", "title") })
        {
            var reviewId = ReviewIdOf(held, section);
            var line = await LineAsync(held, reviewId);
            line.GetProperty("acceptableFields").GetArrayLength().ShouldBe(0, section);
            line.GetProperty("fields").ValueKind.ShouldBe(JsonValueKind.Null, section);

            var refused = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { fields = new[] { field } });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, section);
            (await CodeAsync(refused)).ShouldBe("review.fields_unsupported", section);
        }
    }

    private async Task AcceptAsync(Guid reviewId, params string[] fields)
    {
        var response = await reviewer.PostAsJsonAsync(
            $"/api/v1/review/items/{reviewId}/accept", new { fields = fields.Length == 0 ? null : fields });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        JsonDocument.Parse(raw).RootElement.GetProperty("decided").EnumerateArray()
            .Single(l => l.GetProperty("reviewId").GetGuid() == reviewId)
            .GetProperty("decision").GetString().ShouldBe("accepted", raw);
    }

    private async Task<TripRecording> RecordingAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<DateTimeOffset> DocumentRevisionAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id)).UpdatedAt;
    }

    private async Task<JsonElement> LineAsync(JsonElement answer, Guid reviewId)
    {
        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{answer.GetProperty("importBatchId").GetGuid()}");
        return detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reviewId").GetGuid() == reviewId);
    }

    private static Dictionary<string, JsonElement> FieldsOf(JsonElement line) =>
        line.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("field").GetString()!);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(x => x.GetString()!)];

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task GrantAsync(Guid userId, AccessEffect effect, AccessDomain domain, Guid? only = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = domain,
            Actions = AccessAction.Read,
            ScopeKind = only is null ? AccessScopeKind.All : AccessScopeKind.Object,
            ScopeFeatureId = only,
        });
        await db.SaveChangesAsync();
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

    /// <summary>One upload, its sections as given, held only when it says it understands holding.</summary>
    private async Task<JsonElement> UploadAsync(object body, bool held = false)
    {
        var request = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        request["batchId"] = Guid.CreateVersion7();
        request["contractVersion"] = 1;
        if (held)
        {
            request["clientFeatures"] = new System.Text.Json.Nodes.JsonArray("held");
        }

        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", request);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        var answer = JsonDocument.Parse(raw).RootElement.Clone();
        if (held)
        {
            foreach (var section in request.Select(p => p.Key).Where(k => k is not ("batchId" or "contractVersion" or "clientFeatures")))
            {
                answer.GetProperty(section).EnumerateArray().ShouldAllBe(r => r.GetProperty("status").GetString() == "held", raw);
            }
        }

        return answer;
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

    private static object Entrance(Guid id, Guid caveId, string name) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = false,
        geometry = new { type = "Point", coordinates = new[] { 22.53, 45.63 } },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    private sealed record PhotoRow(
        Guid Id, string Title, string? Description, string FileName, string Sha256, long SizeBytes, Guid StagedUploadId,
        DateTimeOffset? BaseRevision = null);

    private static PhotoRow Photo(Guid id, string title, string? description, byte[] bytes, Guid staged) =>
        new(id, title, description, $"{title}.jpg", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length, staged);

    private sealed record RecordingRow(
        Guid Id, Guid CaveId, string Title, string? Description, string? Log, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
        DateTimeOffset? BaseRevision = null);

    private static RecordingRow Recording(Guid id, Guid caveId, string title, string? log, DateTimeOffset startedAt) =>
        new(id, caveId, title, "Invented for the test", log, startedAt, startedAt.AddHours(6));

    private sealed record ScanRow(
        Guid Id, Guid RecordingId, Guid PlaceId, string Notes, DateTimeOffset ScannedAt, DateTimeOffset? BaseRevision = null);

    private static ScanRow Scan(Guid id, Guid recordingId, Guid placeId, string notes, DateTimeOffset scannedAt) =>
        new(id, recordingId, placeId, notes, scannedAt);

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
