// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The bytes a photo is delivered as, rather than the metadata around it. Three properties are
/// pinned here: a generated rendering carries none of the source's metadata whatever its
/// size; a caller who may not place what a photo shows is handed the whole picture with its
/// location taken out, never the stored file; and the installation's switch decides whether
/// even a caller who may place it gets the stored file or the same location-free copy.
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
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-photobytes-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
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
    public async Task A_caller_who_may_not_place_the_cave_gets_the_whole_picture_without_its_location()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.53127, 25.44721, 240, 180);
        var fileId = await UploadAsync("entrance.jpg", uploaded, "image/jpeg");
        await AttachAsync(fileId, caveId);

        // Fixture proof, all halves: the upload read a capture point out of the image, the image
        // really carries the camera record, an XMP packet and IPTC place names, and the reader
        // really is a caller who may read this cave without placing it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId)).Geom.ShouldNotBeNull();
        }

        using (var source = new MagickImage(uploaded))
        {
            source.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
            source.GetExifProfile()!.GetValue(ExifTag.Model).ShouldNotBeNull();
            source.GetXmpProfile().ShouldNotBeNull();
            source.GetIptcProfile().ShouldNotBeNull().GetValue(IptcTag.City).ShouldNotBeNull();
        }

        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The reader can read the file — it hangs on a cave they may read — so this is about
        // which bytes they get, not about whether they get any.
        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));

        // The rendering, as always.
        (await reader.GetAsync(seen.GetProperty("thumbnailUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The whole picture too, and the response says so — but the record's position is still
        // not printed beside it: the switch is about what is inside the file.
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        seen.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);

        var copy = await ContentBytesAsync(reader, seen);
        ShouldBeLocationFreeCopyOf(copy, uploaded);

        // Asked again, the same copy: it is cached, not re-made per request.
        var again = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        Sha(await ContentBytesAsync(reader, again)).ShouldBe(Sha(copy));

        // The owner may place the cave exactly, so the same request serves them the stored file
        // byte for byte — which is what makes the copy above the rule rather than a re-encode
        // everybody gets.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        held.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Object);
        Sha(await ContentBytesAsync(owner, held)).ShouldBe(Sha(uploaded));
    }

    [Fact]
    public async Task A_photo_that_places_nothing_keeps_its_original_downloadable()
    {
        // The rule reaches capture points that could give away a guarded position. A photo on
        // an unguarded cave gives away nothing that cave was not already saying out loud.
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var openBytes = GeotaggedJpeg(45.1, 25.1, 240, 180);
        var openPhotoId = await UploadAsync("open.jpg", openBytes, "image/jpeg");
        await AttachAsync(openPhotoId, openCaveId);

        var openPhoto = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{openPhotoId}"));
        Sha(await ContentBytesAsync(reader, openPhoto)).ShouldBe(Sha(openBytes));

        // A file with no capture point in it is outside the rule entirely, even on a cave
        // nobody may place: there is nothing in these bytes to give a position away.
        var protectedCaveId = await CreateCaveAsync(locationProtected: true);
        var textId = await UploadAsync("report.txt", "notes"u8.ToArray(), "text/plain");
        await AttachAsync(textId, protectedCaveId);

        var report = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{textId}"));
        (await ContentBytesAsync(reader, report)).ShouldBe("notes"u8.ToArray());

        // Same caller, same kind of request, on a photo that does place a guarded cave: the
        // picture, but not the stored bytes.
        var guardedBytes = GeotaggedJpeg(45.2, 25.2, 240, 180);
        var guardedPhotoId = await UploadAsync("guarded.jpg", guardedBytes, "image/jpeg");
        await AttachAsync(guardedPhotoId, protectedCaveId);
        var guarded = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{guardedPhotoId}"));
        ShouldBeLocationFreeCopyOf(await ContentBytesAsync(reader, guarded), guardedBytes);
    }

    [Fact]
    public async Task Hanging_on_one_guarded_cave_is_enough_even_beside_an_open_one()
    {
        // A photo inherits from everything it hangs on and the strictest wins, so a route
        // through an open cave does not become a way to ask for the same bytes again.
        var openCaveId = await CreateCaveAsync(locationProtected: false);
        var guardedCaveId = await CreateCaveAsync(locationProtected: true);
        var uploaded = GeotaggedJpeg(45.3, 25.3, 240, 180);
        var fileId = await UploadAsync("both.jpg", uploaded, "image/jpeg");
        await AttachAsync(fileId, openCaveId);

        // Attached to the open cave alone, the stored file is served — the positive case, on
        // the same file, one attachment before the location is taken out.
        var before = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        Sha(await ContentBytesAsync(reader, before)).ShouldBe(Sha(uploaded));

        await AttachAsync(fileId, guardedCaveId);

        var after = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        ShouldBeLocationFreeCopyOf(await ContentBytesAsync(reader, after), uploaded);

        // The attachment list is the other side of the same fixture: the open cave still
        // shows its own attachment, so nothing here has simply stopped working.
        var openList = await reader.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={openCaveId}");
        openList.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await openList.Content.ReadAsStringAsync())
            .RootElement.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task With_the_switch_off_the_caller_who_may_place_the_photo_gets_the_location_free_copy_too()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.4, 25.4, 240, 180);
        var fileId = await UploadAsync("switched.jpg", uploaded, "image/jpeg");
        await AttachAsync(fileId, caveId);

        // Switch on (the default): the owner, who may place the cave, gets the stored file.
        var on = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        Sha(await ContentBytesAsync(owner, on)).ShouldBe(Sha(uploaded));

        await SetKeepLocationAsync(false);
        try
        {
            var off = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
            ShouldBeLocationFreeCopyOf(await ContentBytesAsync(owner, off), uploaded);

            // The record's position is a separate question and still the disclosure rule's:
            // the owner may place the cave, so they are still told where the photo was taken.
            off.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Object);

            // The reader is handed the same copy, and is still not told the position.
            var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
            ShouldBeLocationFreeCopyOf(await ContentBytesAsync(reader, seen), uploaded);
            seen.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);

            // A file that is not a picture is not touched by the switch at all.
            var textId = await UploadAsync("notes.txt", "plain notes"u8.ToArray(), "text/plain");
            await AttachAsync(textId, caveId);
            var text = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{textId}"));
            (await ContentBytesAsync(owner, text)).ShouldBe("plain notes"u8.ToArray());
        }
        finally
        {
            await SetKeepLocationAsync(true);
        }
    }

    [Fact]
    public async Task A_video_whose_location_cannot_be_removed_has_no_original_for_a_caller_who_may_not_place_it()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("descent.mp4", Mp4Bytes(), "video/mp4");

        // Nothing here reads a position out of a video, so the fixture records one the way a
        // placed file holds it — the property under test is what happens to a file that has one
        // and whose bytes this application cannot rewrite.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.FirstAsync(f => f.Id == fileId);
            file.Kind.ShouldBe(FileKind.Video);
            file.Geom = new Point(25.5, 45.5) { SRID = 4326 };
            await db.SaveChangesAsync();
        }

        await AttachAsync(fileId, caveId);

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner may place the cave and the switch is on: the stored bytes, as always.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await ContentBytesAsync(owner, held)).ShouldBe(Mp4Bytes());
    }

    [Fact]
    public async Task A_phone_video_with_no_recorded_position_has_no_original_for_a_caller_who_may_not_place_it()
    {
        // What an upload really produces: nothing reads the position a phone writes into a video
        // container, so the row records none, while the bytes may well hold one.
        var caveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("phone-descent.mp4", Mp4Bytes(), "video/mp4");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
            file.Kind.ShouldBe(FileKind.Video);
            file.Geom.ShouldBeNull();
        }

        await AttachAsync(fileId, caveId);

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
        (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await ContentBytesAsync(owner, held)).ShouldBe(Mp4Bytes());
    }

    [Fact]
    public async Task Clearing_a_photos_recorded_position_does_not_hand_its_geotagged_bytes_to_a_caller_who_may_not_place_it()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var uploaded = PlacedJpeg(45.41, 25.41, 240, 180);
        var fileId = await UploadAsync("cleared.jpg", uploaded, "image/jpeg");
        await AttachAsync(fileId, caveId);

        // Taking the position off changes the record and never the file: the GPS fix is still in
        // the stored bytes afterwards.
        var cleared = await owner.PutAsJsonAsync($"/api/v1/files/{fileId}/position", new
        {
            position = (double[]?)null,
            replaceRecordedFix = true,
        });
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId)).Geom.ShouldBeNull();
        }

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        ShouldBeLocationFreeCopyOf(await ContentBytesAsync(reader, seen), uploaded);

        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        Sha(await ContentBytesAsync(owner, held)).ShouldBe(Sha(uploaded));
    }

    /// <summary>
    /// One smoke case per format the location-free copy is written in: the picture comes back in
    /// its own format and size, without its fix or its XMP packet, with its camera record. The
    /// delivery route around it is the same for all three and is proved above on a JPEG.
    /// </summary>
    [Theory]
    [InlineData(MagickFormat.Jpeg)]
    [InlineData(MagickFormat.Png)]
    [InlineData(MagickFormat.WebP)]
    public void Each_rewritable_format_comes_back_without_its_location(MagickFormat format)
    {
        byte[] source;
        using (var image = new MagickImage(PlacedJpeg(45.7, 25.7, 120, 90)))
        {
            source = image.ToByteArray(format);
        }

        // Fixture proof: written in this format, the picture still carries the fix and the packet.
        using (var written = new MagickImage(source))
        {
            written.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
            written.GetXmpProfile().ShouldNotBeNull();
        }

        using var stripped = new MagickImage(source);
        LocationFreeCopyService.RemoveLocation(stripped);
        using var copy = new MagickImage(stripped.ToByteArray(format));

        copy.Format.ShouldBe(format);
        copy.Width.ShouldBe(120u);
        copy.GetExifProfile()?.GetValue(ExifTag.GPSLatitude).ShouldBeNull();
        copy.GetXmpProfile().ShouldBeNull();
        copy.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.Model).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_rendering_token_still_opens_no_whole_file()
    {
        // The location-free reach is a new word in the token; the renderings reach has to keep
        // meaning renderings only, or every address minted for a gallery tile became a download.
        var caveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("tile.jpg", GeotaggedJpeg(45.6, 25.6, 240, 180), "image/jpeg");
        await AttachAsync(fileId, caveId);

        string renderingsOnly;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            renderingsOnly = scope.ServiceProvider.GetRequiredService<SilexGis.Api.Common.IFileAccessTokenService>()
                .CreateToken(fileId, SilexGis.Api.Common.FileDelivery.DerivativesOnly);
        }

        var token = Uri.EscapeDataString(renderingsOnly);
        (await reader.GetAsync($"/api/v1/files/{fileId}/thumbnail?size=160&token={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/files/{fileId}/content?token={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    [Fact]
    public async Task With_the_location_switch_off_a_video_reaches_the_caller_who_may_place_it_only_through_the_second_switch()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var videoId = await UploadAsync("switch-two.mp4", Mp4Bytes(), "video/mp4");
        await AttachAsync(videoId, caveId);
        var uploaded = PlacedJpeg(45.42, 25.42, 240, 180);
        var photoId = await UploadAsync("switch-two.jpg", uploaded, "image/jpeg");
        await AttachAsync(photoId, caveId);

        await SetProtectionAsync(p => p with { KeepLocationInPhotoFiles = false });
        try
        {
            // Second switch off (the default): nobody gets the video as a file, not even the owner,
            // who may place the cave — there is no copy of it without its location.
            var withheld = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{videoId}"));
            withheld.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
            (await owner.GetAsync(withheld.GetProperty("contentUrl").GetString()))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);

            await SetProtectionAsync(p => p with
            {
                KeepLocationInPhotoFiles = false,
                ServeUnstrippableOriginalsToLocationReaders = true,
            });

            // On: the owner gets the stored video, byte for byte.
            var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{videoId}"));
            held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
            (await ContentBytesAsync(owner, held)).ShouldBe(Mp4Bytes());

            // The reader, who may not place the cave, still gets nothing of it.
            var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{videoId}"));
            seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
            (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);

            // A picture that can be rewritten is not the second switch's business: with the
            // location switch off the owner still gets it without its location.
            var photo = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{photoId}"));
            ShouldBeLocationFreeCopyOf(await ContentBytesAsync(owner, photo), uploaded);
        }
        finally
        {
            await SetProtectionAsync(p => p with
            {
                KeepLocationInPhotoFiles = true,
                ServeUnstrippableOriginalsToLocationReaders = false,
            });
        }
    }

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

    private static async Task<byte[]> ContentBytesAsync(HttpClient client, JsonElement file)
    {
        var response = await client.GetAsync(file.GetProperty("contentUrl").GetString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// The handed-over bytes are the uploaded picture at full size with no location data in
    /// them, and with the camera record still there whenever the upload had one.
    /// </summary>
    private void ShouldBeLocationFreeCopyOf(byte[] copy, byte[] uploaded)
    {
        Sha(copy).ShouldNotBe(Sha(uploaded), "the stored bytes, GPS and all, were handed over");

        using var source = new MagickImage(uploaded);
        using var image = new MagickImage(copy);
        image.Width.ShouldBe(source.Width, "the copy was not the full-size picture");
        image.Height.ShouldBe(source.Height, "the copy was not the full-size picture");
        image.Format.ShouldBe(source.Format);

        var exif = image.GetExifProfile();
        exif?.GetValue(ExifTag.GPSLatitude).ShouldBeNull("the copy kept the GPS latitude");
        exif?.GetValue(ExifTag.GPSLongitude).ShouldBeNull("the copy kept the GPS longitude");
        image.GetXmpProfile().ShouldBeNull("the copy kept its XMP packet");
        image.GetIptcProfile()?.GetValue(IptcTag.City).ShouldBeNull("the copy kept its IPTC city");
        image.GetIptcProfile()?.GetValue(IptcTag.Country).ShouldBeNull("the copy kept its IPTC country");

        // What the upload pipeline itself reads a position with finds none.
        var probe = Path.Combine(filesRoot, $"probe-{Guid.NewGuid():N}.jpg");
        Directory.CreateDirectory(filesRoot);
        File.WriteAllBytes(probe, copy);
        try
        {
            new MagickPhotoGeotagReader().Read(probe).Point.ShouldBeNull("a capture point was read out of the copy");
        }
        finally
        {
            File.Delete(probe);
        }

        // The camera record stays when the upload had one: it says how the picture was taken,
        // not where.
        if (source.GetExifProfile()?.GetValue(ExifTag.Model) is { } model)
        {
            exif.ShouldNotBeNull().GetValue(ExifTag.Model).ShouldNotBeNull().Value.ShouldBe(model.Value);
            exif.GetValue(ExifTag.Make).ShouldNotBeNull().Value.ShouldBe(source.GetExifProfile()!.GetValue(ExifTag.Make)!.Value);
            exif.GetValue(ExifTag.FNumber).ShouldNotBeNull();
            image.GetIptcProfile().ShouldNotBeNull().GetValue(IptcTag.Caption).ShouldNotBeNull(
                "the IPTC fields that do not name a place went with the place names");
        }
    }

    private async Task SetKeepLocationAsync(bool keep)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var current = await settings.GetProtectionAsync();
        await settings.SaveAsync(AppSettingSections.Protection, current with { KeepLocationInPhotoFiles = keep });
    }

    private async Task SetProtectionAsync(Func<ProtectionSettings, ProtectionSettings> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Protection, change(await settings.GetProtectionAsync()));
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// A minimal ISO media file: a type box naming the ordinary MP4 brand and an empty media
    /// box. Enough for the upload to type it as a video; nothing here plays it.
    /// </summary>
    private static byte[] Mp4Bytes()
    {
        byte[] ftyp = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0, 0, 2, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'1'];
        byte[] mdat = [0, 0, 0, 0x10, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 1, 2, 3, 4, 5, 6, 7, 8];
        return [.. ftyp, .. mdat];
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

    /// <summary>
    /// A generated picture carrying everything a camera and an editor would put in one: an
    /// invented GPS fix, the camera and exposure record, an XMP packet naming a fix and a town,
    /// and IPTC fields naming the town and the country beside a caption.
    /// </summary>
    private static byte[] PlacedJpeg(double lat, double lon, int width, int height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        exif.SetValue(ExifTag.Make, "Invented Optics");
        exif.SetValue(ExifTag.Model, "Test Body 1");
        exif.SetValue(ExifTag.FNumber, new Rational(28, 10));
        image.SetProfile(exif);

        image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
            + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\" "
            + "xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\" "
            + $"exif:GPSLatitude=\"{lat:0.0000}N\" exif:GPSLongitude=\"{lon:0.0000}E\" photoshop:City=\"Invented Town\"/>"
            + "</rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>")));

        var iptc = new IptcProfile();
        iptc.SetValue(IptcTag.City, "Invented Town");
        iptc.SetValue(IptcTag.Country, "Invented Country");
        iptc.SetValue(IptcTag.Caption, "A generated test picture");
        image.SetProfile(iptc);

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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
