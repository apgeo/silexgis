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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The bytes a photo is delivered as, rather than the metadata around it. Two properties are
/// pinned here: a generated rendering carries none of the source's metadata whatever its
/// size, and the source itself is not handed to a caller who may not place what it shows.
///
/// Every refusal below is paired with the same request succeeding for someone entitled to it,
/// over the same fixture: the reader is a Viewer, who holds no exact-location right anywhere,
/// and the owner created the cave, so ownership gives them one. A fixture that quietly
/// stopped attaching the photo, or stopped reading a capture point out of it, would take the
/// positive assertion down with it rather than passing as a security guarantee.
/// </summary>
public sealed class PhotoBytesProtectionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the cave, uploads onto it, may place it
    private HttpClient reader = null!;  // Viewer — reads the cave, may not place it
    private long caveTypeId;

    public PhotoBytesProtectionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-photobytes-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pb-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"pb-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pb-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"pb-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// Both sizes matter and for opposite reasons: resizing happens to discard the profiles as
    /// a side effect, so an image smaller than the box takes the branch where nothing resizes
    /// and nothing would have been discarded. Testing only the large one would have passed
    /// against a build that published GPS on every small photo.
    /// </summary>
    [Theory]
    [InlineData(1400, 300)] // wider than every box — the resizing branch
    [InlineData(64, 64)]     // smaller than every box — the branch that copies straight through
    public async Task No_rendering_of_a_geotagged_photo_carries_its_metadata(int width, int height)
    {
        var fileId = await UploadAsync(
            "entrance.jpg", GeotaggedJpeg(45.53127, 25.44721, width, height), "image/jpeg");

        // Fixture proof: the source really does carry the GPS these renderings must not.
        using (var source = new MagickImage(GeotaggedJpeg(45.53127, 25.44721, width, height)))
        {
            source.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
        }

        foreach (var size in new[] { 160, 480, 1200 })
        {
            var bytes = await ThumbnailBytesAsync(owner, fileId, size);
            using var rendering = new MagickImage(bytes);
            rendering.GetExifProfile().ShouldBeNull($"size {size} kept an EXIF profile");
            rendering.ProfileNames.ShouldBeEmpty($"size {size} kept a profile");
        }
    }

    [Fact]
    public async Task A_caller_who_may_not_place_the_cave_gets_the_rendering_and_not_the_original()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("entrance.jpg", GeotaggedJpeg(45.53127, 25.44721, 240, 180), "image/jpeg");
        await AttachAsync(fileId, caveId);

        // Fixture proof, both halves: the upload read a capture point out of the image, and
        // the reader really is a caller who may read this cave without placing it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId)).Geom.ShouldNotBeNull();
        }

        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The reader can read the file — it hangs on a cave they may read — so this is about
        // which bytes they get, not about whether they get any.
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));

        // The picture, yes: it is stripped, so it shows the entrance without saying where it is.
        (await reader.GetAsync(seen.GetProperty("thumbnailUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The stored bytes, no: those still hold the GPS fix the camera wrote, and handing
        // them over would walk straight around the protection on the cave.
        (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the response says so up front, so a client can label a download control it
        // must not offer instead of discovering the refusal by following the link.
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();

        // The owner may place the cave exactly, so the same request serves them the original —
        // which is what makes the refusal above the rule rather than a broken URL.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await owner.GetAsync(held.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_photo_that_places_nothing_keeps_its_original_downloadable()
    {
        // The rule reaches capture points that could give away a guarded position. A photo on
        // an unguarded cave gives away nothing that cave was not already saying out loud.
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var openPhotoId = await UploadAsync("open.jpg", GeotaggedJpeg(45.1, 25.1, 240, 180), "image/jpeg");
        await AttachAsync(openPhotoId, openCaveId);

        var openPhoto = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{openPhotoId}"));
        (await reader.GetAsync(openPhoto.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A file with no capture point in it is outside the rule entirely, even on a cave
        // nobody may place: there is nothing in these bytes to give a position away.
        var protectedCaveId = await CreateCaveAsync(locationProtected: true);
        var textId = await UploadAsync("report.txt", "notes"u8.ToArray(), "text/plain");
        await AttachAsync(textId, protectedCaveId);

        var report = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{textId}"));
        (await reader.GetAsync(report.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Same caller, same kind of request, on a photo that does place a guarded cave.
        var guardedPhotoId = await UploadAsync("guarded.jpg", GeotaggedJpeg(45.2, 25.2, 240, 180), "image/jpeg");
        await AttachAsync(guardedPhotoId, protectedCaveId);
        var guarded = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{guardedPhotoId}"));
        (await reader.GetAsync(guarded.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hanging_on_one_guarded_cave_is_enough_even_beside_an_open_one()
    {
        // A photo inherits from everything it hangs on and the strictest wins, so a route
        // through an open cave does not become a way to ask for the same bytes again.
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("both.jpg", GeotaggedJpeg(45.3, 25.3, 240, 180), "image/jpeg");
        await AttachAsync(fileId, openCaveId);

        // Attached to the open cave alone, the original is served — the positive case, on the
        // same file, one attachment before the refusal.
        var before = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        (await reader.GetAsync(before.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await AttachAsync(fileId, guardedCaveId);

        var after = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        (await reader.GetAsync(after.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The attachment list is the other side of the same fixture: the open cave still
        // shows its own attachment, so nothing here has simply stopped working.
        var openList = await reader.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={openCaveId}");
        openList.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await openList.Content.ReadAsStringAsync())
            .RootElement.GetArrayLength().ShouldBe(1);
    }

    // ---- helpers ----

    private async Task<byte[]> ThumbnailBytesAsync(HttpClient client, Guid fileId, int size)
    {
        var seen = await ReadJsonAsync(await client.GetAsync($"/api/v1/files/{fileId}"));
        // The published URL carries a token minted at 480; re-point it at the size under test
        // rather than minting a second one, so the token being tested is the published one.
        var url = seen.GetProperty("thumbnailUrl").GetString()!.Replace("size=480", $"size={size}");
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Bytes {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static byte[] GeotaggedJpeg(double lat, double lon, int width, int height)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, (uint)width, (uint)height);
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
