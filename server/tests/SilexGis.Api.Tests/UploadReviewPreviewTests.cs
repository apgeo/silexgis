// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A reviewer looking at the picture a held photo or raster map keeps before deciding: a rendering
/// drawn from the waiting bytes, without metadata, only for a line the batch view would show them,
/// cached until the bytes go.
/// </summary>
/// <remarks>Every picture here is generated, with an invented position.</remarks>
public sealed class UploadReviewPreviewTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid set;
    private string marker = null!;

    public UploadReviewPreviewTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-reviewpreview-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"urp-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"urp-up-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"urp-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"urp-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await MarkAsync(true);
    }

    [Fact]
    public async Task A_held_photo_is_drawn_for_a_reviewer_at_both_sizes_without_any_metadata()
    {
        var bytes = LargeGeotaggedJpeg(45.31, 25.47);
        var documentId = Guid.CreateVersion7();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            documents = new[] { Photo(documentId, "IMG_1001.jpg", bytes, await StageAsync("IMG_1001.jpg", bytes)) },
        });
        var reviewId = ReviewIdOf(answer, "documents");

        var line = await LineAsync(reviewer, answer, reviewId);
        line.GetProperty("hasPreview").GetBoolean().ShouldBeTrue();

        foreach (var (size, longest) in new[] { (480, 480), (1200, 1200) })
        {
            var drawn = await reviewer.GetAsync($"/api/v1/review/items/{reviewId}/preview?size={size}");
            drawn.StatusCode.ShouldBe(HttpStatusCode.OK, await drawn.Content.ReadAsStringAsync());
            drawn.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
            var rendered = await drawn.Content.ReadAsByteArrayAsync();
            rendered.ShouldNotBe(bytes);

            using var image = new MagickImage(rendered);
            image.Format.ShouldBe(MagickFormat.WebP);
            Math.Max(image.Width, image.Height).ShouldBe((uint)longest);
            image.GetExifProfile().ShouldBeNull();
            image.GetXmpProfile().ShouldBeNull();
            image.GetIptcProfile().ShouldBeNull();
        }

        // The default is the line's thumbnail; another size is refused whatever the line.
        (await reviewer.GetAsync($"/api/v1/review/items/{reviewId}/preview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var odd = await reviewer.GetAsync($"/api/v1/review/items/{reviewId}/preview?size=2400");
        odd.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(odd)).ShouldBe("review.preview_size_unsupported");

        // The uploader reads their own batch, picture included; nobody signed out does.
        (await uploader.GetAsync($"/api/v1/review/items/{reviewId}/preview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/review/items/{reviewId}/preview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_held_raster_map_is_drawn_and_a_file_that_is_no_picture_has_none()
    {
        var caveId = Guid.CreateVersion7();
        var mapId = Guid.CreateVersion7();
        var picture = LargeGeotaggedJpeg(45.32, 25.48);
        var text = Corpus.Text($"Notes {marker}, invented for the test.");
        var notesId = Guid.CreateVersion7();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "Drawn cave") },
            documents = new[] { Photo(notesId, "notes.txt", text, await StageAsync("notes.txt", text)) },
            rasterMaps = new[] { RasterMap(mapId, caveId, picture, await StageAsync("plan.jpg", picture)) },
        });
        var mapReview = ReviewIdOf(answer, "rasterMaps");
        var notesReview = ReviewIdOf(answer, "documents");
        var caveReview = ReviewIdOf(answer, "rows");

        (await LineAsync(reviewer, answer, mapReview)).GetProperty("hasPreview").GetBoolean().ShouldBeTrue();
        var drawn = await reviewer.GetAsync($"/api/v1/review/items/{mapReview}/preview?size=480");
        drawn.StatusCode.ShouldBe(HttpStatusCode.OK, await drawn.Content.ReadAsStringAsync());
        using (var image = new MagickImage(await drawn.Content.ReadAsByteArrayAsync()))
        {
            image.GetExifProfile().ShouldBeNull();
        }

        (await LineAsync(reviewer, answer, notesReview)).GetProperty("hasPreview").GetBoolean().ShouldBeFalse();
        var none = await reviewer.GetAsync($"/api/v1/review/items/{notesReview}/preview");
        none.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(none)).ShouldBe("review.preview_unavailable");

        // A line that keeps no bytes at all has no picture either.
        (await LineAsync(reviewer, answer, caveReview)).GetProperty("hasPreview").GetBoolean().ShouldBeFalse();
        var cave = await reviewer.GetAsync($"/api/v1/review/items/{caveReview}/preview");
        cave.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(cave)).ShouldBe("review.preview_unavailable");
    }

    [Fact]
    public async Task Without_the_review_right_or_the_cave_a_picture_is_answered_as_no_line_at_all()
    {
        // A private cave the uploader made before being placed under review; then, held, a map of it.
        var caveId = Guid.CreateVersion7();
        await MarkAsync(false);
        await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { Cave(caveId, "Unread cave") },
        });
        await MarkAsync(true);

        var mapId = Guid.CreateVersion7();
        var picture = LargeGeotaggedJpeg(45.33, 25.49);
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rasterMaps = new[] { RasterMap(mapId, caveId, picture, await StageAsync("unread.jpg", picture)) },
        });
        var mapReview = ReviewIdOf(answer, "rasterMaps");
        (await reviewer.GetAsync($"/api/v1/review/items/{mapReview}/preview")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var missing = await reviewer.GetAsync($"/api/v1/review/items/{Guid.CreateVersion7()}/preview");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe("review.item_not_found");

        // May not read the queue at all.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"urp-out-{marker}@t.local");
        using var outsider = await AuthHelper.BearerClientAsync(factory, $"urp-out-{marker}@t.local");
        var refused = await outsider.GetAsync($"/api/v1/review/items/{mapReview}/preview");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("review.item_not_found");

        // May read the queue, but not the cave the map is of: the batch view shows the line
        // without its proposal, and the picture is answered as if the line were not there.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"urp-rd-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"urp-rd-{marker}@t.local");
        await GrantAsync(readerId, AccessDomain.UploadReview);
        var blind = await LineAsync(reader, answer, mapReview);
        blind.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Null);
        blind.GetProperty("hasPreview").GetBoolean().ShouldBeFalse();
        var hidden = await reader.GetAsync($"/api/v1/review/items/{mapReview}/preview");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(hidden)).ShouldBe("review.item_not_found");
    }

    [Fact]
    public async Task The_rendering_goes_with_the_bytes_when_they_are_swept_and_when_they_are_adopted()
    {
        var swept = LargeGeotaggedJpeg(45.34, 25.50);
        var adopted = LargeGeotaggedJpeg(45.35, 25.51);
        var sweptDocument = Guid.CreateVersion7();
        var adoptedDocument = Guid.CreateVersion7();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            documents = new[]
            {
                Photo(sweptDocument, "IMG_1010.jpg", swept, await StageAsync("IMG_1010.jpg", swept)),
                Photo(adoptedDocument, "IMG_1011.jpg", adopted, await StageAsync("IMG_1011.jpg", adopted)),
            },
        });
        var reviews = answer.GetProperty("documents").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()).ToList();
        var sessions = await SessionsOfAsync(reviews);
        foreach (var review in reviews)
        {
            (await reviewer.GetAsync($"/api/v1/review/items/{review}/preview")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await reviewer.GetAsync($"/api/v1/review/items/{review}/preview?size=1200")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        CachedRenderings(sessions[0]).ShouldBe(2);
        CachedRenderings(sessions[1]).ShouldBe(2);

        // Turned down, and kept a while: the picture can still be looked at.
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviews[0]}/reject", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.GetAsync($"/api/v1/review/items/{reviews[0]}/preview")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Then the retention runs out and the sweep takes the bytes, and their renderings with them.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.UploadSessions.Where(s => s.Id == sessions[0])
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await RunSweepAsync();
        CachedRenderings(sessions[0]).ShouldBe(0);
        var gone = await reviewer.GetAsync($"/api/v1/review/items/{reviews[0]}/preview");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe("review.preview_unavailable");

        // Accepted: the bytes are the document's own file now, shown through its own routes.
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviews[1]}/accept", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        CachedRenderings(sessions[1]).ShouldBe(0);
        (await LineAsync(reviewer, answer, reviews[1])).GetProperty("hasPreview").GetBoolean().ShouldBeFalse();
        (await reviewer.GetAsync($"/api/v1/review/items/{reviews[1]}/preview")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>How many renderings of a staged upload sit in the thumbnail cache.</summary>
    private int CachedRenderings(Guid sessionId)
    {
        var thumbs = Path.Combine(filesRoot, "thumbs");
        return Directory.Exists(thumbs) ? Directory.GetFiles(thumbs, $"{sessionId:N}-*").Length : 0;
    }

    private async Task<IReadOnlyList<Guid>> SessionsOfAsync(IReadOnlyList<Guid> reviews)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var held = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ReviewId != null && reviews.Contains(i.ReviewId.Value))
            .ToDictionaryAsync(i => i.ReviewId!.Value, i => i.HeldUploadSessionId!.Value);
        return [.. reviews.Select(r => held[r])];
    }

    private async Task RunSweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new UploadSessionSweepHandler(
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>(),
            scope.ServiceProvider.GetRequiredService<ThumbnailService>(),
            NullLogger<UploadSessionSweepHandler>.Instance);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.UploadSessionSweep }, CancellationToken.None);
    }

    /// <summary>A generated photograph larger than either rendering, carrying an invented GPS fix.</summary>
    private static byte[] LargeGeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 1600, 900);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, Dms(lat));
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, Dms(lon));
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static Rational[] Dms(double value)
    {
        var degrees = Math.Floor(value);
        var minutes = Math.Floor((value - degrees) * 60);
        var seconds = (value - degrees - (minutes / 60)) * 3600;
        return [new Rational(degrees), new Rational(minutes), new Rational(seconds)];
    }

    private async Task<JsonElement> LineAsync(HttpClient client, JsonElement answer, Guid reviewId)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{answer.GetProperty("importBatchId").GetGuid()}");
        return detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reviewId").GetGuid() == reviewId);
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

    private static object Photo(Guid id, string fileName, byte[] bytes, Guid staged) => new
    {
        id,
        title = "Held picture",
        fileName,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        sizeBytes = bytes.Length,
        stagedUploadId = staged,
    };

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
