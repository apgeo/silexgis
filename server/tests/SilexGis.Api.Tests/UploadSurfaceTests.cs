// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The upload surface as callers meet it: the format is decided from the bytes rather than
/// from what the upload claims, the size limit is installation configuration, and every
/// file response names the document it belongs to.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UploadSurfaceTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;
    private HttpClient owner = null!;

    public UploadSurfaceTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-upload-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"us-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"us-own-{suffix}@t.local");
    }

    [Fact]
    public async Task Every_file_response_names_the_document_behind_it()
    {
        var uploaded = await UploadAsync(owner, "plan.png", Corpus.Png(), "image/png");
        var documentId = uploaded.GetProperty("documentId").GetGuid();
        documentId.ShouldNotBe(Guid.Empty);

        // The point of the identifier: it survives a new version, while the file id does not.
        var fileId = uploaded.GetProperty("id").GetGuid();
        using var form = BuildForm("plan-v2.png", Corpus.Png(), "image/png");
        var second = await owner.PostAsync($"/api/v1/files/{fileId}/versions", form);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());

        var next = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        next.GetProperty("id").GetGuid().ShouldNotBe(fileId);
        next.GetProperty("documentId").GetGuid().ShouldBe(documentId);

        // And the metadata read of the original file reports the same document.
        var reread = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/{fileId}");
        reread.GetProperty("documentId").GetGuid().ShouldBe(documentId);
    }

    [Fact]
    public async Task A_mislabelled_upload_is_filed_by_its_bytes_and_an_honest_one_by_the_same_rule()
    {
        // The negative case, built explicitly: a real PNG announced as plain text under a
        // .txt name. Believing either hint would file it as a document, which is what the
        // text extractor would later be handed and fail to read.
        var bytes = Corpus.Png();
        var mislabelled = await UploadAsync(owner, "notes.txt", bytes, "text/plain");

        mislabelled.GetProperty("kind").GetString().ShouldBe("image");
        mislabelled.GetProperty("mimeType").GetString().ShouldBe("image/png");
        // A kind decided correctly is also what turns on the image-only behaviour behind it.
        mislabelled.GetProperty("thumbnailUrl").GetString().ShouldNotBeNull();

        // The positive twin, in the same test: the identical bytes labelled honestly are
        // filed identically, so the assertion above is about the bytes and not about the lie.
        var honest = await UploadAsync(owner, "photo.png", bytes, "image/png");
        honest.GetProperty("kind").GetString().ShouldBe("image");
        honest.GetProperty("mimeType").GetString().ShouldBe("image/png");
        honest.GetProperty("thumbnailUrl").GetString().ShouldNotBeNull();

        // The thumbnail route is gated on the stored kind, so the mislabelled upload is
        // served exactly like the honest one rather than 404ing.
        var thumbnail = await owner.GetAsync(mislabelled.GetProperty("thumbnailUrl").GetString());
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_still_image_in_the_recording_container_family_is_treated_as_the_image_it_is()
    {
        // HEIC and AVIF are ISO base media files in exactly the way an MP4 is: the brand after
        // "ftyp" is the whole difference between a phone photograph and a recording. Filed as
        // a recording, the photograph silently loses everything gated on being an image — so
        // what this asserts is the gated behaviour itself, on a real encoded file.
        var photo = await UploadAsync(owner, "entrance.dat", Corpus.Avif(), "application/octet-stream");

        photo.GetProperty("kind").GetString().ShouldBe("image");
        photo.GetProperty("mimeType").GetString().ShouldBe("image/avif");

        var thumbnail = await owner.GetAsync(photo.GetProperty("thumbnailUrl").GetString());
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{photo.GetProperty("documentId").GetGuid()}");
        document.GetProperty("pageCount").GetInt32().ShouldBe(1);

        // The twin that shares the container byte-for-byte in structure and differs only in
        // its brand: still a recording, and offered no thumbnail at all. So the answers above
        // are the brand being read and not "anything in this container is now a picture".
        var recording = await UploadAsync(owner, "descent.dat", Corpus.Mp4(), "application/octet-stream");

        recording.GetProperty("kind").GetString().ShouldBe("video");
        recording.GetProperty("thumbnailUrl").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_mislabelled_photo_still_has_its_capture_location_read_and_protected()
    {
        // Reading EXIF is gated on the file's kind. A geotagged photo filed as a document
        // because of its label would keep its coordinates unread — and therefore outside
        // every rule that governs coordinates.
        var jpeg = Corpus.GeotaggedJpeg(45.5, 25.6);
        var mislabelled = await UploadAsync(owner, "field.dat", jpeg, "application/octet-stream");
        var honest = await UploadAsync(owner, "field.jpg", jpeg, "image/jpeg");

        var mislabelledId = mislabelled.GetProperty("id").GetGuid();
        var honestId = honest.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var mislabelledPoint = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == mislabelledId).Select(f => f.Geom).SingleAsync();
        var honestPoint = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == honestId).Select(f => f.Geom).SingleAsync();

        mislabelledPoint.ShouldNotBeNull();
        honestPoint.ShouldNotBeNull();
        mislabelledPoint.Y.ShouldBe(honestPoint.Y, 0.0001);
        mislabelledPoint.X.ShouldBe(honestPoint.X, 0.0001);
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf", "document")]
    [InlineData("notes.txt", "text/plain", "document")]
    [InlineData("table.csv", "text/csv", "document")]
    [InlineData("minutes.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "document")]
    [InlineData("minutes.odt", "application/vnd.oasis.opendocument.text", "document")]
    [InlineData("photo.png", "image/png", "image")]
    [InlineData("photo.jpg", "image/jpeg", "image")]
    [InlineData("entrance.heic", "image/heic", "image")]
    [InlineData("entrance.avif", "image/avif", "image")]
    [InlineData("interview.mp3", "audio/mpeg", "audio")]
    [InlineData("descent.mp4", "video/mp4", "video")]
    [InlineData("bundle.zip", "application/zip", "other")]
    public async Task A_corpus_of_real_files_is_recognised_from_its_content(
        string fileName, string expectedMimeType, string expectedKind)
    {
        // Every one of these is uploaded under a media type that says nothing, so the answer
        // can only have come from the bytes.
        var uploaded = await UploadAsync(
            owner, fileName, Corpus.Of(fileName), "application/octet-stream");

        uploaded.GetProperty("mimeType").GetString().ShouldBe(expectedMimeType);
        uploaded.GetProperty("kind").GetString().ShouldBe(expectedKind);
    }

    [Fact]
    public async Task A_container_that_declares_an_impossible_media_type_is_stored_as_the_archive_it_is()
    {
        // What a container says it is comes from whoever built it, so neither its length nor
        // its cost to read is ours to assume: the recorded media type has a fixed width, and
        // a compressed entry inflates to whatever its author chose. Reading it whole and
        // believing it would fail the write against the column with the bytes already in the
        // store and no row pointing at them — the upload 500s and leaves a dead blob behind.
        var padded = Corpus.ZipDeclaring(
            "application/vnd.oasis.opendocument.text" + new string('x', 200));

        var stored = await UploadAsync(owner, "padded.odt", padded, "application/octet-stream");

        stored.GetProperty("mimeType").GetString().ShouldBe("application/zip");
        stored.GetProperty("kind").GetString().ShouldBe("other");

        // The positive twin, through the same code path — an archive that announces itself
        // inside rather than in its opening bytes — so the answer above is the declaration
        // being impossible and not the container going unopened.
        var honest = await UploadAsync(
            owner,
            "real.odt",
            Corpus.ZipDeclaring("application/vnd.oasis.opendocument.text"),
            "application/octet-stream");

        honest.GetProperty("mimeType").GetString().ShouldBe("application/vnd.oasis.opendocument.text");
        honest.GetProperty("kind").GetString().ShouldBe("document");
    }

    [Fact]
    public async Task An_image_upload_records_the_one_page_it_is()
    {
        var uploaded = await UploadAsync(owner, "sketch.png", Corpus.Png(), "image/png");
        var documentId = uploaded.GetProperty("documentId").GetGuid();

        var document = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        document.GetProperty("pageCount").GetInt32().ShouldBe(1);

        // A format whose pages nothing has counted yet says so, rather than guessing one.
        var pdf = await UploadAsync(owner, "survey.pdf", Corpus.Pdf(), "application/pdf");
        var pdfDocument = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{pdf.GetProperty("documentId").GetGuid()}");
        pdfDocument.GetProperty("pageCount").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task What_a_file_states_about_itself_lands_in_columns_a_list_can_order_by()
    {
        // A photograph states its photographer, the software that wrote it and when it was
        // taken; a recording states how long it runs and how it is encoded. Both are read
        // from the file rather than asked of the uploader, and both live in columns rather
        // than in the per-kind bag, because document lists filter and order on them.
        var photo = await UploadAsync(owner, "entrance.jpg", Corpus.TaggedJpeg(), "image/jpeg");
        var described = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{photo.GetProperty("documentId").GetGuid()}");

        described.GetProperty("author").GetString().ShouldBe("A. Popescu");
        described.GetProperty("producer").GetString().ShouldBe("SilexCam 2.1");
        described.GetProperty("contentCreatedAt").GetDateTimeOffset()
            .ShouldBe(new DateTimeOffset(2026, 3, 12, 9, 41, 7, TimeSpan.Zero));

        var video = await UploadAsync(owner, "descent.mp4", Corpus.Mp4(), "video/mp4");
        var timed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{video.GetProperty("documentId").GetGuid()}");

        timed.GetProperty("kind").GetString().ShouldBe("video");
        timed.GetProperty("durationSeconds").GetDouble().ShouldBe(3d, 0.0001);
        timed.GetProperty("codec").GetString().ShouldBe("avc1");

        // The positive twin's opposite: a format that states none of this says so, rather
        // than carrying a value someone would then sort by.
        var plain = await UploadAsync(owner, "notes.txt", Corpus.Of("notes.txt"), "text/plain");
        var bare = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{plain.GetProperty("documentId").GetGuid()}");

        bare.GetProperty("author").ValueKind.ShouldBe(JsonValueKind.Null);
        bare.GetProperty("durationSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
        bare.GetProperty("codec").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_upload_limit_is_published_and_enforced_at_the_configured_value()
    {
        // A separate installation, configured with a limit small enough to cross in a test.
        const long cap = 4096;
        var root = Path.Combine(Path.GetTempPath(), $"silexgis-test-cap-{Guid.NewGuid():N}");
        await using var capped = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = root,
                ["Keys:Path"] = Path.Combine(root, "keys"),
                ["Files:MaxUploadBytes"] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Editor, $"cap-{suffix}@t.local");
        var client = await AuthHelper.BearerClientAsync(capped, $"cap-{suffix}@t.local");

        try
        {
            var published = await client.GetFromJsonAsync<JsonElement>("/api/v1/files/config");
            published.GetProperty("maxUploadBytes").GetInt64().ShouldBe(cap);

            // Over the limit is refused with a code the client can act on, not a bare
            // transport error — which is why the request-body ceiling sits above the file
            // limit rather than on it.
            using var tooLarge = BuildForm("big.bin", new byte[cap + 1], "application/octet-stream");
            var rejected = await client.PostAsync("/api/v1/files/", tooLarge);
            rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(rejected)).ShouldBe("file.too_large");

            // The positive twin: a file of exactly the configured limit is accepted, so the
            // rejection above is the limit and not an off-by-one that hides real uploads.
            using var atLimit = BuildForm("exact.bin", new byte[cap], "application/octet-stream");
            var accepted = await client.PostAsync("/api/v1/files/", atLimit);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());

            // Empty stays its own answer rather than collapsing into "too large".
            using var empty = BuildForm("nothing.bin", [], "application/octet-stream");
            var refused = await client.PostAsync("/api/v1/files/", empty);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(refused)).ShouldBe("file.empty");
        }
        finally
        {
            client.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Every_limit_an_upload_passes_is_derived_from_the_one_configured_number()
    {
        // The limit that matters is not the one the handler checks — it is the two the web
        // server applies before the handler exists. A body cut off by either arrives as a
        // bare transport error with nothing in the log to explain it, so raising the
        // application's cap alone changes nothing. This asserts they move together.
        const long cap = 700L * 1024 * 1024;
        var root = Path.Combine(Path.GetTempPath(), $"silexgis-test-limits-{Guid.NewGuid():N}");
        await using var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = root,
                ["Keys:Path"] = Path.Combine(root, "keys"),
                ["Files:MaxUploadBytes"] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        try
        {
            using var scope = configured.Services.CreateScope();
            var files = scope.ServiceProvider.GetRequiredService<IOptions<FilesOptions>>().Value;
            files.MaxUploadBytes.ShouldBe(cap);

            // The transport ceiling sits above the file limit, never on it: a multipart
            // request carries boundaries and headers around the bytes, so a file of exactly
            // the limit makes a request slightly larger than it.
            files.MaxRequestBodyBytes.ShouldBeGreaterThan(cap);

            // The multipart reader's own ceiling, which no request-size limit lifts.
            scope.ServiceProvider.GetRequiredService<IOptions<FormOptions>>()
                .Value.MultipartBodyLengthLimit.ShouldBe(files.MaxRequestBodyBytes);

            // And the request-body ceiling on the two routes that carry an upload. Left off,
            // the web server's own default (well under 30 MB) decides instead.
            // Matched on the pattern as written, route constraint included — that is what the
            // router keeps, and spelling it out here means a renamed route fails this test
            // rather than silently matching nothing.
            var endpoints = configured.Services.GetRequiredService<EndpointDataSource>().Endpoints;
            foreach (var route in (string[])["/api/v1/files/", "/api/v1/files/{id:guid}/versions"])
            {
                var upload = endpoints.OfType<RouteEndpoint>().Single(e =>
                    e.RoutePattern.RawText == route
                    && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

                upload.Metadata.GetMetadata<IRequestSizeLimitMetadata>()
                    .ShouldNotBeNull($"{route} accepts an upload with no request-size limit of its own.")
                    .MaxRequestBodySize.ShouldBe(files.MaxRequestBodyBytes);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Lowering_the_file_limit_does_not_cut_off_the_routes_with_a_fixed_larger_one()
    {
        // The multipart ceiling is process-wide while the raster route accepts a fixed 512 MB
        // of its own, so deriving that ceiling from the configurable file limit alone would
        // let an administrator who tightens document uploads silently break raster uploads —
        // at a size no message anywhere names. The ceiling tracks whichever limit is larger.
        const long cap = 16L * 1024 * 1024;
        var root = Path.Combine(Path.GetTempPath(), $"silexgis-test-lowcap-{Guid.NewGuid():N}");
        await using var tightened = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = root,
                ["Keys:Path"] = Path.Combine(root, "keys"),
                ["Files:MaxUploadBytes"] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        try
        {
            using var scope = tightened.Services.CreateScope();
            var files = scope.ServiceProvider.GetRequiredService<IOptions<FilesOptions>>().Value;
            files.MaxUploadBytes.ShouldBe(cap);

            var rasterUpload = tightened.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
                .Select(e => e.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize ?? 0)
                .Max();

            // The positive twin: the file limit itself did follow the configuration down, so
            // the assertion below is the ceiling holding independently and not both numbers
            // simply staying where they started.
            files.MaxRequestBodyBytes.ShouldBeLessThan(rasterUpload);
            scope.ServiceProvider.GetRequiredService<IOptions<FormOptions>>()
                .Value.MultipartBodyLengthLimit.ShouldBeGreaterThan(rasterUpload);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task The_published_limit_defaults_to_the_shipped_value()
    {
        var published = await owner.GetFromJsonAsync<JsonElement>("/api/v1/files/config");

        published.GetProperty("maxUploadBytes").GetInt64().ShouldBe(512L * 1024 * 1024);
    }

    [Fact]
    public async Task Reading_the_limit_needs_an_account()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/files/config");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, string fileName, byte[] bytes, string contentType)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await client.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }
}

/// <summary>
/// Files that really are what they claim: a genuine PNG and JPEG from the imaging library,
/// a PDF with a real cross-reference table, actual ZIP packages laid out the way Word and
/// OpenDocument lay theirs out, and media containers with real box structures. Built here
/// rather than committed so that nothing depends on how a checkout handles binary files.
/// </summary>
internal static class Corpus
{
    public static byte[] Of(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => Png(),
        ".jpg" or ".jpeg" => Jpeg(),
        ".heic" => Heic(),
        ".avif" => Avif(),
        ".pdf" => Pdf(),
        ".txt" => Text("Raport de tură\nPeștera Urșilor, 12 martie\n"),
        ".csv" => Text("cavitate;lungime;adancime\nUrsilor;1500;42\n"),
        ".docx" => Docx(),
        ".odt" => Odt(),
        ".mp3" => Mp3(),
        ".mp4" => Mp4(),
        ".zip" => Zip(),
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "No sample for this extension."),
    };

    public static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.DarkSlateBlue, 64, 48);
        return image.ToByteArray(MagickFormat.Png);
    }

    public static byte[] Jpeg()
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 48);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A photograph carrying the tags a camera writes about itself.</summary>
    public static byte[] TaggedJpeg()
    {
        using var image = new MagickImage(MagickColors.Sienna, 48, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Artist, "A. Popescu");
        exif.SetValue(ExifTag.Software, "SilexCam 2.1");
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:03:12 09:41:07");
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    public static byte[] GeotaggedJpeg(double lat, double lon)
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

    /// <summary>
    /// A real still image inside the MP4 container family — exactly the file a "not audio,
    /// therefore video" reader mis-files, and a genuine one, so the image-only behaviour
    /// behind the decision (thumbnail, EXIF, one page) can be exercised for real.
    /// </summary>
    public static byte[] Avif()
    {
        using var image = new MagickImage(MagickColors.Sienna, 64, 48);
        return image.ToByteArray(MagickFormat.Avif);
    }

    /// <summary>
    /// The same thing in the format a modern phone actually writes. Built by hand rather than
    /// encoded, because the imaging library this project carries reads HEIC but cannot write
    /// one — so this is a real container with the brand that matters and no picture inside it,
    /// which is all the format decision reads.
    /// </summary>
    public static byte[] Heic() => MediaSamples.IsoBaseMediaImage("heic", "mif1heicmiaf");

    public static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    /// <summary>A one-page PDF that a reader can actually open.</summary>
    public static byte[] Pdf()
    {
        var body = new StringBuilder();
        var offsets = new List<int>();
        void Object(string content)
        {
            offsets.Add(body.Length);
            body.Append(content);
        }

        body.Append("%PDF-1.4\n");
        Object("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Object("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Object("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n");

        var xref = body.Length;
        body.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>A ZIP laid out the way Office Open XML lays one out: the parts name the format.</summary>
    public static byte[] Docx()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",
                """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>""");
            Write(archive, "word/document.xml",
                """<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>""");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A ZIP laid out the way OpenDocument requires: an uncompressed <c>mimetype</c> entry
    /// first, which is what makes the format readable from the file's opening bytes.
    /// </summary>
    public static byte[] Odt()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetype = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetype.Open()))
            {
                writer.Write("application/vnd.oasis.opendocument.text");
            }

            Write(archive, "content.xml", """<?xml version="1.0"?><office:document-content/>""");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A ZIP that announces its own format in a <b>compressed</b> <c>mimetype</c> entry — the
    /// layout produced by a tool that ignores OpenDocument's "first entry, stored, uncompressed"
    /// rule, and therefore the one that can only be read by opening the archive.
    /// </summary>
    public static byte[] ZipDeclaring(string mediaType)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "mimetype", mediaType);
            Write(archive, "content.xml", """<?xml version="1.0"?><office:document-content/>""");
        }

        return buffer.ToArray();
    }

    public static byte[] Zip()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "readme.txt", "field notes");
        }

        return buffer.ToArray();
    }

    /// <summary>An MPEG audio file: an ID3v2 tag followed by a frame header.</summary>
    public static byte[] Mp3()
    {
        var bytes = new byte[512];
        Encoding.ASCII.GetBytes("ID3").CopyTo(bytes, 0);
        bytes[3] = 0x04; // version 2.4
        bytes[9] = 0x0A; // synchsafe tag size
        bytes[20] = 0xFF;
        bytes[21] = 0xFB; // MPEG-1 layer III frame sync
        return bytes;
    }

    /// <summary>An ISO base media file whose brand says video, three seconds long.</summary>
    public static byte[] Mp4() =>
        MediaSamples.IsoBaseMedia(timescale: 30_000, duration: 90_000, codec: "avc1");

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }
}
