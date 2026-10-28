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
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The server's own row on the reviews stream for a turned-down edit or removal of every section
/// that carries one — a photo or document, a link, a recording, a scan, a map and a pin — shaped
/// byte for byte as that section's own stream hands it, and absent whenever that stream would not
/// hand this account the row now.
/// </summary>
/// <remarks>
/// <para>
/// The uploader is an Editor who creates the cave and its places, and so is told where they are.
/// Each "withheld" step below gives a feature to somebody else, guards it, and denies the uploader
/// exact positions: the same decision is then read again, and the row hanging there is gone from
/// it as it is gone from the stream. Every absence is asserted beside a presence over the same
/// fixture. The reviewer is a full administrator.
/// </para>
/// <para>Every picture is generated.</para>
/// </remarks>
public sealed class SyncReviewCurrentTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid reviewerId;
    private long caveTypeId;
    private long placeTypeId;

    public SyncReviewCurrentTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncreviewcurrent-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        },
        // The trips a recording becomes are made by a job; a report made in the middle of these
        // tests would only move rows the assertions compare.
        JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"src-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"src-up-{marker}@t.local");
        reviewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"src-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"src-adm-{marker}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        uploader?.Dispose();
        reviewer?.Dispose();
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task A_turned_down_recording_and_scans_come_back_as_the_trips_stream_hands_them_and_go_when_it_would_not()
    {
        var cave = await CreateCaveAsync();
        var placeA = await CreatePlaceAsync(cave);
        var placeB = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync([cave]);

        var recording = Guid.CreateVersion7();
        var atA = Guid.CreateVersion7();
        var atB = Guid.CreateVersion7();
        var created = await UploadAsync(set, held: false, new
        {
            tripRecordings = new[] { Recording(recording, cave, "Trip") },
            tripPoints = new[] { Point(atA, recording, placeA, "scan A"), Point(atB, recording, placeB, "scan B") },
        });
        var page = await GetAsync($"/api/v1/sync/sets/{set}/trips");
        var streamed = Entry(page, "recordings", recording);

        await MarkAsync(true);
        var held = await UploadAsync(set, held: true, new
        {
            tripRecordings = new[] { Recording(recording, cave, "Renamed", Revision(created, "tripRecordings", recording)) },
            tripPoints = new object[]
            {
                Point(atA, recording, placeA, "changed", Revision(created, "tripPoints", atA)),
                new { id = atB, recordingId = recording, baseRevision = Revision(created, "tripPoints", atB), deleted = true },
            },
        });
        var reviewRecording = ReviewId(held, "tripRecordings", recording);
        var reviewEdit = ReviewId(held, "tripPoints", atA);
        var reviewRemoval = ReviewId(held, "tripPoints", atB);
        await RejectAsync(reviewRecording, reviewEdit, reviewRemoval);

        // Exactly the stream's entries: the recording with both its scans, the edited scan and the
        // removed one each as one entry of that list — what the phone puts back.
        var reviews = await ReviewsAsync(set);
        Current(reviews, reviewRecording)!.Value.GetRawText().ShouldBe(streamed.GetRawText());
        Current(reviews, reviewEdit)!.Value.GetRawText().ShouldBe(Entry(streamed, "points", atA).GetRawText());
        Current(reviews, reviewRemoval)!.Value.GetRawText().ShouldBe(Entry(streamed, "points", atB).GetRawText());

        // A scan's place withheld: the scan is absent from its recording's current and has none
        // of its own, while the recording and the other scan are still told.
        await WithholdAsync(placeA);
        reviews = await ReviewsAsync(set);
        Ids(Current(reviews, reviewRecording)!.Value, "points").ShouldBe([atB]);
        Current(reviews, reviewEdit).ShouldBeNull();
        Current(reviews, reviewRemoval).ShouldNotBeNull();
        reviews.GetRawText().ShouldNotContain(placeA.ToString());
        Ids(Entry(await GetAsync($"/api/v1/sync/sets/{set}/trips"), "recordings", recording), "points").ShouldBe([atB]);

        // The cave withheld: nothing of the recording is told, as the stream tells nothing of it.
        await WithholdAsync(cave);
        reviews = await ReviewsAsync(set);
        Current(reviews, reviewRecording).ShouldBeNull();
        Current(reviews, reviewRemoval).ShouldBeNull();
        Outcome(reviews, reviewRecording).ShouldBe("rejected");
        (await GetAsync($"/api/v1/sync/sets/{set}/trips")).GetProperty("recordings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_turned_down_map_and_pins_come_back_as_the_rasters_stream_hands_them_and_go_when_it_would_not()
    {
        var cave = await CreateCaveAsync();
        var placeA = await CreatePlaceAsync(cave);
        var placeB = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync([cave]);

        var map = Guid.CreateVersion7();
        var pinA = Guid.CreateVersion7();
        var pinB = Guid.CreateVersion7();
        var created = await UploadAsync(set, held: false, new
        {
            rasterMaps = new[] { await NewMapAsync(map, cave) },
            rasterPins = new[] { Pin(pinA, map, placeA, 10, 20), Pin(pinB, map, placeB, 30, 40) },
        });
        var streamed = Entry(await GetAsync($"/api/v1/sync/sets/{set}/rasters"), "maps", map);

        await MarkAsync(true);
        var held = await UploadAsync(set, held: true, new
        {
            rasterMaps = new[]
            {
                new { id = map, baseRevision = Revision(created, "rasterMaps", map), caveId = cave, title = "Renamed", mapType = "planView" },
            },
            rasterPins = new object[]
            {
                Pin(pinA, map, placeA, 11, 21, Revision(created, "rasterPins", pinA)),
                new { id = pinB, rasterMapId = map, baseRevision = Revision(created, "rasterPins", pinB), deleted = true },
            },
        });
        var reviewMap = ReviewId(held, "rasterMaps", map);
        var reviewPin = ReviewId(held, "rasterPins", pinA);
        var reviewRemoval = ReviewId(held, "rasterPins", pinB);
        await RejectAsync(reviewMap, reviewPin, reviewRemoval);

        var reviews = await ReviewsAsync(set);
        Current(reviews, reviewMap)!.Value.GetRawText().ShouldBe(streamed.GetRawText());
        Current(reviews, reviewPin)!.Value.GetRawText().ShouldBe(Entry(streamed, "pins", pinA).GetRawText());
        Current(reviews, reviewRemoval)!.Value.GetRawText().ShouldBe(Entry(streamed, "pins", pinB).GetRawText());

        // A pin's place withheld: no pixel of it anywhere in the answer.
        await WithholdAsync(placeA);
        reviews = await ReviewsAsync(set);
        Ids(Current(reviews, reviewMap)!.Value, "pins").ShouldBe([pinB]);
        Current(reviews, reviewPin).ShouldBeNull();
        Current(reviews, reviewRemoval).ShouldNotBeNull();
        reviews.GetRawText().ShouldNotContain(placeA.ToString());
        reviews.GetRawText().ShouldNotContain("\"x\":10");

        await WithholdAsync(cave);
        reviews = await ReviewsAsync(set);
        Current(reviews, reviewMap).ShouldBeNull();
        Current(reviews, reviewRemoval).ShouldBeNull();
        (await GetAsync($"/api/v1/sync/sets/{set}/rasters")).GetProperty("maps").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_turned_down_document_and_link_come_back_as_the_catalogue_lists_them_and_go_when_it_would_not()
    {
        var cave = await CreateCaveAsync();
        var set = await CreateSetAsync([cave]);
        var hung = Guid.CreateVersion7();
        var loose = Guid.CreateVersion7();
        var hungBytes = Corpus.Jpeg();
        var looseBytes = Corpus.GeotaggedJpeg(45.36, 25.66);
        await UploadAsync(set, held: false, new
        {
            documents = new[]
            {
                new { id = hung, fileName = "hung.jpg", sha256 = Hash(hungBytes), stagedUploadId = await StageAsync("hung.jpg", hungBytes) },
                new { id = loose, fileName = "loose.jpg", sha256 = Hash(looseBytes), stagedUploadId = await StageAsync("loose.jpg", looseBytes) },
            },
            documentLinks = new[] { new { documentId = hung, target = new { kind = "feature", id = cave }, caption = "First" } },
        });
        var streamedLink = (await GetAsync($"/api/v1/sync/sets/{set}/documents"))
            .GetProperty("links").EnumerateArray().Single(l => l.GetProperty("documentId").GetGuid() == hung).Clone();
        var streamedDocument = streamedLink.GetProperty("document");
        var hungRevision = await DocumentRevisionAsync(hung);
        var looseRevision = await DocumentRevisionAsync(loose);

        await MarkAsync(true);
        var held = await UploadAsync(set, held: true, new
        {
            documents = new[]
            {
                new { id = hung, baseRevision = hungRevision, title = "Renamed", baseVersionId = streamedDocument.GetProperty("versionId").GetGuid() },
                new { id = loose, baseRevision = looseRevision, title = "Renamed", baseVersionId = await VersionAsync(loose) },
            },
            documentLinks = new[] { new { documentId = hung, target = new { kind = "feature", id = cave }, caption = "Second" } },
        });
        var reviewDocument = ReviewId(held, "documents", hung);
        var reviewLoose = ReviewId(held, "documents", loose);
        var reviewLink = ReviewId(held, "documentLinks", hung);
        await RejectAsync(reviewDocument, reviewLoose, reviewLink);

        // The catalogue's own entries. A document the catalogue lists through no link — filed on
        // nothing, so no phone is handed it — has none: absence means what it means on the stream.
        var reviews = await ReviewsAsync(set);
        Current(reviews, reviewDocument)!.Value.GetRawText().ShouldBe(streamedDocument.GetRawText());
        Current(reviews, reviewLink)!.Value.GetRawText().ShouldBe(streamedLink.GetRawText());
        Current(reviews, reviewLoose).ShouldBeNull();
        Outcome(reviews, reviewLoose).ShouldBe("rejected");
        RowId(reviews, reviewLink).ShouldBe(hung);

        await WithholdAsync(cave);
        reviews = await ReviewsAsync(set);
        Current(reviews, reviewDocument).ShouldBeNull();
        Current(reviews, reviewLink).ShouldBeNull();
        (await GetAsync($"/api/v1/sync/sets/{set}/documents")).GetProperty("links").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task An_edit_accepted_in_part_is_marked_and_carries_the_servers_row_and_one_accepted_whole_carries_neither()
    {
        var set = await CreateSetAsync([]);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var created = await UploadAsync(set, held: false, new { rows = new[] { CaveRow(caveId), EntranceRow(entranceId, caveId, "Intrare") } });

        await MarkAsync(true);
        var held = await UploadAsync(set, held: true, new
        {
            rows = new[] { EntranceRow(entranceId, caveId, "Renamed", Revision(created, "rows", entranceId), [22.6333, 45.6444]) },
        });
        var partialReview = ReviewId(held, "rows", entranceId);
        await AcceptAsync(partialReview, ["name"]);

        // The name was written and the position was not: the phone is told so, and given the row
        // as the server now holds it — the new name at the old point.
        var reviews = await ReviewsAsync(set);
        Outcome(reviews, partialReview).ShouldBe("accepted");
        Review(reviews, partialReview).GetProperty("partial").GetBoolean().ShouldBeTrue();
        var current = Current(reviews, partialReview)!.Value;
        current.GetProperty("id").GetGuid().ShouldBe(entranceId);
        current.GetProperty("name").GetString().ShouldBe("Renamed");
        current.GetRawText().ShouldContain("22.51");
        current.GetRawText().ShouldNotContain("22.6333");
        var written = Review(reviews, partialReview).GetProperty("revision").GetDateTimeOffset();

        // Accepted whole: nothing for the phone to put back, so neither the mark nor the row.
        var again = await UploadAsync(set, held: true, new
        {
            rows = new[] { EntranceRow(entranceId, caveId, "Whole", written) },
        });
        var wholeReview = ReviewId(again, "rows", entranceId);
        await AcceptAsync(wholeReview, null);
        reviews = await ReviewsAsync(set);
        Outcome(reviews, wholeReview).ShouldBe("accepted");
        Review(reviews, wholeReview).TryGetProperty("partial", out _).ShouldBeFalse();
        Current(reviews, wholeReview).ShouldBeNull();

        // The entrance withheld: still marked, and the row is absent rather than blurred.
        await WithholdAsync(entranceId);
        reviews = await ReviewsAsync(set);
        Review(reviews, partialReview).GetProperty("partial").GetBoolean().ShouldBeTrue();
        Current(reviews, partialReview).ShouldBeNull();
        reviews.GetRawText().ShouldNotContain("22.51");
    }

    [Fact]
    public async Task A_recording_accepted_in_part_comes_back_as_the_trips_stream_hands_it()
    {
        var cave = await CreateCaveAsync();
        var set = await CreateSetAsync([cave]);
        var recording = Guid.CreateVersion7();
        var created = await UploadAsync(set, held: false, new { tripRecordings = new[] { Recording(recording, cave, "Trip") } });

        await MarkAsync(true);
        var held = await UploadAsync(set, held: true, new
        {
            tripRecordings = new[]
            {
                new
                {
                    id = recording,
                    baseRevision = Revision(created, "tripRecordings", recording),
                    caveId = cave,
                    title = "Renamed",
                    log = "a log the reviewer turned down",
                    startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
                    endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
                },
            },
        });
        var review = ReviewId(held, "tripRecordings", recording);
        await AcceptAsync(review, ["title"]);

        var streamed = Entry(await GetAsync($"/api/v1/sync/sets/{set}/trips"), "recordings", recording);
        streamed.GetProperty("title").GetString().ShouldBe("Renamed");
        var reviews = await ReviewsAsync(set);
        Review(reviews, review).GetProperty("partial").GetBoolean().ShouldBeTrue();
        Current(reviews, review)!.Value.GetRawText().ShouldBe(streamed.GetRawText());
        Current(reviews, review)!.Value.GetRawText().ShouldNotContain("turned down");

        await WithholdAsync(cave);
        Current(await ReviewsAsync(set), review).ShouldBeNull();
    }

    // ---- requests -----------------------------------------------------------------------------

    private static object Recording(Guid id, Guid caveId, string title, DateTimeOffset? baseRevision = null) => new
    {
        id,
        baseRevision,
        caveId,
        title,
        startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
    };

    private static object Point(Guid id, Guid recordingId, Guid placeId, string notes, DateTimeOffset? baseRevision = null) => new
    {
        id,
        recordingId,
        baseRevision,
        placeId,
        scannedAt = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero),
        notes,
    };

    private static object CaveRow(Guid id) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name = "Peștera inventată",
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object EntranceRow(Guid id, Guid caveId, string name, DateTimeOffset? baseRevision = null, double[]? at = null) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = at ?? [22.51, 45.61] },
        altitude = 640.0,
        positionQuality = "unknown",
    };

    private static object Pin(Guid id, Guid mapId, Guid placeId, int x, int y, DateTimeOffset? baseRevision = null) =>
        new { id, rasterMapId = mapId, baseRevision, placeId, x, y };

    private async Task<object> NewMapAsync(Guid id, Guid caveId)
    {
        var picture = Corpus.Jpeg();
        return new
        {
            id,
            caveId,
            title = "Plan",
            mapType = "planView",
            fileName = "map.jpg",
            sha256 = Hash(picture),
            stagedUploadId = await StageAsync("map.jpg", picture),
        };
    }

    private async Task<JsonElement> UploadAsync(Guid set, bool held, object sections)
    {
        var body = JsonSerializer.SerializeToNode(sections, JsonSerializerOptions.Web)!.AsObject();
        body["batchId"] = Guid.CreateVersion7();
        body["contractVersion"] = 1;
        if (held)
        {
            body["clientFeatures"] = new System.Text.Json.Nodes.JsonArray("held");
        }

        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var answer = JsonDocument.Parse(payload).RootElement.Clone();
        foreach (var section in body.Select(p => p.Key).Where(k => k is not ("batchId" or "contractVersion" or "clientFeatures")))
        {
            foreach (var row in answer.GetProperty(section).EnumerateArray())
            {
                row.GetProperty("status").GetString().ShouldBe(held ? "held" : "created", payload);
            }
        }

        return answer;
    }

    private async Task RejectAsync(params Guid[] reviewIds)
    {
        foreach (var reviewId in reviewIds)
        {
            var response = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/reject", new { note = "No" });
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task AcceptAsync(Guid reviewId, string[]? fields)
    {
        var response = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { fields });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray()
            .Single(l => l.GetProperty("reviewId").GetGuid() == reviewId).GetProperty("decision").GetString()
            .ShouldBe("accepted");
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> StageAsync(string fileName, byte[] bytes)
    {
        var opened = await uploader.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await uploader.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    // ---- reading answers ----------------------------------------------------------------------

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static DateTimeOffset Revision(JsonElement answer, string section, Guid id) =>
        answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
            .GetProperty("revision").GetDateTimeOffset();

    private static Guid ReviewId(JsonElement answer, string section, Guid id) =>
        answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
            .GetProperty("reviewId").GetGuid();

    private static JsonElement Entry(JsonElement parent, string list, Guid id) =>
        parent.GetProperty(list).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id).Clone();

    private static IReadOnlyList<Guid> Ids(JsonElement parent, string list) =>
        [.. parent.GetProperty(list).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];

    private static JsonElement Review(JsonElement page, Guid reviewId) =>
        page.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("reviewId").GetGuid() == reviewId);

    private static JsonElement? Current(JsonElement page, Guid reviewId) =>
        Review(page, reviewId).TryGetProperty("current", out var current) ? current.Clone() : null;

    private static string? Outcome(JsonElement page, Guid reviewId) =>
        Review(page, reviewId).GetProperty("outcome").GetString();

    private static Guid RowId(JsonElement page, Guid reviewId) =>
        Review(page, reviewId).GetProperty("rowId").GetGuid();

    private Task<JsonElement> ReviewsAsync(Guid set) => GetAsync($"/api/v1/sync/sets/{set}/reviews");

    private async Task<JsonElement> GetAsync(string url)
    {
        var response = await uploader.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    // ---- the store ----------------------------------------------------------------------------

    private async Task<DateTimeOffset> DocumentRevisionAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.UpdatedAt).SingleAsync();
    }

    private async Task<Guid> VersionAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId && v.IsCurrent).Select(v => v.Id).SingleAsync();
    }

    /// <summary>
    /// Gives a feature to somebody else and guards it, and denies the uploader exact positions —
    /// an owner is always told where their own rows are, so both are needed for it to be withheld.
    /// </summary>
    private async Task WithholdAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => f.Id == featureId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.OwnerUserId, reviewerId)
                .SetProperty(f => f.LocationProtected, true)
                .SetProperty(f => f.IsProtectedEffective, true));
        if (!await db.AccessEntries.AnyAsync(e => e.SubjectId == uploaderId && e.Effect == AccessEffect.Deny))
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = uploaderId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Features,
                Actions = AccessAction.ViewExactLocation,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }
    }

    // ---- fixtures -----------------------------------------------------------------------------

    private async Task<Guid> CreateSetAsync(IReadOnlyList<Guid> roots)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Current {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePlaceAsync(Guid caveId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = $"Marcaj {Guid.NewGuid():N}"[..14],
            FeatureTypeId = placeTypeId,
            OwnerUserId = uploaderId,
            Visibility = Visibility.Authenticated,
            Properties = """{"speleolocSchemaVersion":1}""",
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
        await db.SaveChangesAsync();
        return feature.Id;
    }
}
