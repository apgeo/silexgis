// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a viewer is allowed to fetch in order to show a document.
///
/// The property under test is one sentence: showing a document is never a way to obtain bytes
/// the download would refuse. A viewer draws pictures this application produced — a page
/// rendering, a photo rendering — and those are open to a caller who may not have the stored
/// file, because they carry nothing the file carried beyond what they depict. The stored bytes
/// stay behind the same single gate they were already behind.
///
/// Every refusal below is paired with the same request succeeding for someone entitled to it,
/// over the same fixture. The reader is a Viewer, who holds no exact-location right anywhere;
/// the owner created the cave, so ownership gives them one. A fixture that quietly stopped
/// attaching the photo, or stopped reading a capture point out of it, would take the positive
/// assertion down with it rather than passing as a guarantee.
/// </summary>
public sealed class DocumentViewerBytesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the cave, uploads onto it, may place it
    private HttpClient reader = null!;  // Viewer — reads the cave, may not place it
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public DocumentViewerBytesTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-viewbytes-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"vb-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"vb-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"vb-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"vb-read-{suffix}@t.local");

        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// The whole reason a page picture exists: it is open to the narrower of the two reaches a
    /// delivery URL can be signed with, and the stored file is not. Both halves are asserted
    /// against the same file with the same two tokens, so this cannot pass by the picture route
    /// having quietly become as permissive as the download route — the download refuses the very
    /// token the picture answers.
    /// </summary>
    [Fact]
    public async Task A_page_picture_answers_a_reach_the_stored_file_refuses()
    {
        var fileId = await UploadAsync("report.pdf", Pdf(2), "application/pdf");

        var tokens = factory.Services.GetRequiredService<IFileAccessTokenService>();
        var renderingsOnly = tokens.CreateToken(fileId, FileDelivery.DerivativesOnly);
        var everything = tokens.CreateToken(fileId, FileDelivery.Full);

        // A picture of the page: drawn here, so either reach opens it.
        foreach (var (token, reach) in new[] { (renderingsOnly, "renderings only"), (everything, "full") })
        {
            var response = await anonymous.GetAsync(RenderUrl(fileId, page: 1, size: 480, token));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"reach: {reach}");
            response.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");

            using var drawn = new MagickImage(await response.Content.ReadAsByteArrayAsync());
            drawn.Width.ShouldBeGreaterThan(0u);
            drawn.ProfileNames.ShouldBeEmpty("a rendering must carry no profile of its own");
        }

        // The stored file: only the wider reach opens it, which is what makes the pair above a
        // rule rather than two routes that happen to agree.
        (await anonymous.GetAsync($"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(everything)}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(renderingsOnly)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And a URL nobody minted opens neither, without saying whether the file is there.
        (await anonymous.GetAsync(RenderUrl(fileId, page: 1, size: 480, "not-a-token")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Each page is its own picture. Asserted because the page number is an option passed to the
    /// renderer rather than part of the file it opens: a build that dropped it would serve page
    /// one under every number and read as a working page strip.
    /// </summary>
    [Fact]
    public async Task Each_page_is_drawn_from_the_page_that_was_asked_for()
    {
        var fileId = await UploadAsync("two-pages.pdf", Pdf(2), "application/pdf");
        var token = factory.Services.GetRequiredService<IFileAccessTokenService>()
            .CreateToken(fileId, FileDelivery.DerivativesOnly);

        var first = await BytesAsync(RenderUrl(fileId, page: 1, size: 480, token));
        var second = await BytesAsync(RenderUrl(fileId, page: 2, size: 480, token));
        second.ShouldNotBe(first);

        // A page the file does not have is not a page, and asking for one is not an error worth
        // a different answer than asking for a file that is not there.
        (await anonymous.GetAsync(RenderUrl(fileId, page: 3, size: 480, token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(RenderUrl(fileId, page: 0, size: 480, token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A wider picture asked for is a wider picture drawn, over a page the size of ordinary
    /// paper — which is the case every one of the offered widths was chosen for.
    ///
    /// Asserted because a page, unlike a photograph, has no size of its own to be "already large
    /// enough": the resolution it is drawn at is decided here. A rule that declined to raise that
    /// resolution would return the same small picture under every width, so the two largest ones
    /// — the ones that exist so the words on a scanned page can be read — would be indistinguishable
    /// from the smallest, and every assertion about them still passing would say nothing.
    /// </summary>
    [Fact]
    public async Task A_wider_page_picture_is_drawn_wider()
    {
        var fileId = await UploadAsync(
            "a4.pdf", Pdf(1, widthPoints: 595, heightPoints: 842), "application/pdf");
        var token = factory.Services.GetRequiredService<IFileAccessTokenService>()
            .CreateToken(fileId, FileDelivery.DerivativesOnly);

        using var small = new MagickImage(await BytesAsync(RenderUrl(fileId, 1, 480, token)));
        using var large = new MagickImage(await BytesAsync(RenderUrl(fileId, 1, 1200, token)));
        using var largest = new MagickImage(await BytesAsync(RenderUrl(fileId, 1, 2400, token)));

        // The longer side lands on the box that was asked for, give or take the rounding of a
        // page measured in points into whole pixels.
        small.Height.ShouldBeInRange(460u, 500u);
        large.Height.ShouldBeInRange(1160u, 1240u);
        largest.Height.ShouldBeInRange(2320u, 2480u);

        // Never past it, in either direction: the box bounds the picture.
        largest.Width.ShouldBeLessThanOrEqualTo(2400u);
        largest.Height.ShouldBeLessThanOrEqualTo(2400u);

        // And the tap that enlarges a page really does fetch something with more in it.
        (await BytesAsync(RenderUrl(fileId, 1, 2400, token)))
            .Length.ShouldBeGreaterThan((await BytesAsync(RenderUrl(fileId, 1, 480, token))).Length);
    }

    /// <summary>
    /// A page may declare itself any size, and the lowest resolution a page is drawn at is
    /// still tens of thousands of pixels a side for one declared hundreds of thousands of points
    /// across. The pixels a page is drawn with stay under a ceiling whatever it declares, and an
    /// ordinary page is drawn at the resolution that lands it on the box as before.
    /// </summary>
    [Theory]
    [InlineData(200_000, 200_000, 160)]
    [InlineData(200_000, 200_000, 2400)]
    [InlineData(1_000_000, 2_000, 480)]
    [InlineData(14_400, 14_400, 2400)]
    public void A_page_of_any_declared_size_is_drawn_within_the_pixel_ceiling(
        int pointsWide, int pointsHigh, int size)
    {
        var dpi = PageRenderService.Resolution(pointsWide, pointsHigh, size);

        // Within a part in a billion: the square root that lands a page on the ceiling comes
        // back a rounding step above it, which is no allocation at all.
        var pixels = (pointsWide * dpi / 72.0) * (pointsHigh * dpi / 72.0);
        pixels.ShouldBeLessThanOrEqualTo(PageRenderService.MaxPagePixels * (1 + 1e-9));
        dpi.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void An_ordinary_page_lands_on_the_box_it_was_asked_for()
    {
        // An A4 page at the largest box: the longer side, 842 points, comes out 2400 pixels.
        var dpi = PageRenderService.Resolution(595, 842, 2400);
        (842 * dpi / 72.0).ShouldBe(2400, tolerance: 0.5);
    }

    [Fact]
    public async Task A_page_declared_enormous_is_still_drawn_inside_the_box()
    {
        var fileId = await UploadAsync(
            "sheet.pdf", Pdf(1, widthPoints: 60_000, heightPoints: 60_000), "application/pdf");
        var token = factory.Services.GetRequiredService<IFileAccessTokenService>()
            .CreateToken(fileId, FileDelivery.DerivativesOnly);

        using var picture = new MagickImage(await BytesAsync(RenderUrl(fileId, 1, 480, token)));
        picture.Width.ShouldBeLessThanOrEqualTo(480u);
        picture.Height.ShouldBeLessThanOrEqualTo(480u);
    }

    /// <summary>
    /// A picture a few tens of kilobytes long can declare four hundred million pixels, all of
    /// which a decode holds. Its rendering is refused from what the header claims, before a
    /// pixel is decoded, while an ordinary picture uploaded beside it is still drawn.
    /// </summary>
    [Fact]
    public async Task A_picture_declaring_more_pixels_than_may_be_drawn_gets_no_rendering()
    {
        var bomb = await UploadAsync("plan.png", PictureBombs.BlankPng(20_000, 20_000), "image/png");
        var ordinary = await UploadAsync("plan-small.png", PictureBombs.BlankPng(400, 300), "image/png");
        var tokens = factory.Services.GetRequiredService<IFileAccessTokenService>();

        using (var picture = new MagickImage(await BytesAsync(ThumbnailUrl(
            ordinary, 160, tokens.CreateToken(ordinary, FileDelivery.DerivativesOnly)))))
        {
            picture.Width.ShouldBe(160u);
        }

        var refused = await anonymous.GetAsync(ThumbnailUrl(
            bomb, 160, tokens.CreateToken(bomb, FileDelivery.DerivativesOnly)));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("file.thumbnail_failed");
    }

    /// <summary>
    /// The location-free copy of a picture is written from a full decode of the stored file, so
    /// a picture whose header declares more pixels than may be drawn is refused from that claim
    /// before it is decoded, and answers as a copy that cannot be made does. An ordinary picture
    /// beside it is still handed over through the same reach.
    /// </summary>
    [Fact]
    public async Task A_picture_declaring_more_pixels_than_may_be_drawn_gets_no_location_free_copy()
    {
        var bomb = await UploadAsync("plan.png", PictureBombs.BlankPng(20_000, 20_000), "image/png");
        var ordinary = await UploadAsync("plan-small.png", PictureBombs.BlankPng(400, 300), "image/png");
        var tokens = factory.Services.GetRequiredService<IFileAccessTokenService>();

        var handed = await anonymous.GetAsync(
            $"/api/v1/files/{ordinary}/content?token={Uri.EscapeDataString(tokens.CreateToken(ordinary, FileDelivery.LocationFree))}");
        handed.StatusCode.ShouldBe(HttpStatusCode.OK, await handed.Content.ReadAsStringAsync());
        using (var copy = new MagickImage(await handed.Content.ReadAsByteArrayAsync()))
        {
            copy.Width.ShouldBe(400u);
        }

        var refused = await anonymous.GetAsync(
            $"/api/v1/files/{bomb}/content?token={Uri.EscapeDataString(tokens.CreateToken(bomb, FileDelivery.LocationFree))}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("file.not_found");
    }

    /// <summary>
    /// A drawing can name another file for the renderer to draw into it. A file on the server
    /// named that way must never come back inside the rendering: the drawing is drawn without
    /// it. The named file here is a picture of solid red the test writes itself, so a single red
    /// pixel in the rendering means it was read. An ordinary drawing is rendered first, so the
    /// hostile one is judged by a renderer shown to draw drawings at all, rather than passing
    /// because none is drawn.
    /// </summary>
    [Fact]
    public async Task A_drawing_naming_a_file_on_the_server_never_draws_that_file()
    {
        var tokens = factory.Services.GetRequiredService<IFileAccessTokenService>();
        var plain = await UploadAsync(
            "plain.svg",
            Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">"
                + "<rect width=\"200\" height=\"200\" fill=\"#0000ff\"/></svg>"),
            "image/svg+xml");
        var drawn = await anonymous.GetAsync(
            ThumbnailUrl(plain, 160, tokens.CreateToken(plain, FileDelivery.DerivativesOnly)));
        drawn.StatusCode.ShouldBe(HttpStatusCode.OK, await drawn.Content.ReadAsStringAsync());
        using (var control = new MagickImage(await drawn.Content.ReadAsByteArrayAsync()))
        {
            CountPixels(control, c => c.B > 200 && c.R < 80 && c.G < 80)
                .ShouldBeGreaterThan(0, "an ordinary drawing is no longer drawn");
        }

        var secretDir = Path.Combine(filesRoot, "outside-the-store");
        Directory.CreateDirectory(secretDir);
        var secret = Path.Combine(secretDir, "red.png");
        using (var red = new MagickImage(new MagickColor(255, 0, 0), 64, 64))
        {
            await red.WriteAsync(secret, MagickFormat.Png);
        }

        var href = new Uri(secret).AbsoluteUri;
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\""
            + " width=\"200\" height=\"200\">"
            + "<rect width=\"200\" height=\"200\" fill=\"#ffffff\"/>"
            + $"<image x=\"0\" y=\"0\" width=\"200\" height=\"200\" href=\"{href}\" xlink:href=\"{href}\"/>"
            + $"<image x=\"0\" y=\"0\" width=\"200\" height=\"200\" xlink:href=\"{secret}\"/>"
            + "</svg>");
        var fileId = await UploadAsync("plan.svg", svg, "image/svg+xml");
        var token = tokens.CreateToken(fileId, FileDelivery.DerivativesOnly);

        var response = await anonymous.GetAsync(ThumbnailUrl(fileId, 160, token));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var picture = new MagickImage(await response.Content.ReadAsByteArrayAsync());
        CountPixels(picture, c => c.R > 200 && c.G < 80 && c.B < 80)
            .ShouldBe(0, "the rendering drew a file the drawing named on the server");
    }

    private static int CountPixels(MagickImage picture, Func<IMagickColor<byte>, bool> matches)
    {
        using var pixels = picture.GetPixels();
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (matches(pixel.ToColor()!))
            {
                count++;
            }
        }

        return count;
    }

    private static string ThumbnailUrl(Guid fileId, int size, string token) =>
        $"/api/v1/files/{fileId}/thumbnail?size={size}&token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// A format whose pages nothing here can draw says so as an absence rather than as a broken
    /// picture, and a width the renderer does not offer is refused as a bad request — which is a
    /// different thing from a refusal to disclose, and is answered differently on purpose.
    /// </summary>
    [Fact]
    public async Task Only_a_paged_format_at_an_offered_width_is_drawn()
    {
        var pdfId = await UploadAsync("report.pdf", Pdf(1), "application/pdf");
        var textId = await UploadAsync("notes.txt", "no pages here"u8.ToArray(), "text/plain");
        var tokens = factory.Services.GetRequiredService<IFileAccessTokenService>();

        // The positive half, so the two refusals below are about what was asked for.
        (await anonymous.GetAsync(
                RenderUrl(pdfId, 1, 1200, tokens.CreateToken(pdfId, FileDelivery.DerivativesOnly))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.GetAsync(
                RenderUrl(textId, 1, 1200, tokens.CreateToken(textId, FileDelivery.Full))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var badWidth = await anonymous.GetAsync(
            RenderUrl(pdfId, 1, 999, tokens.CreateToken(pdfId, FileDelivery.DerivativesOnly)));
        badWidth.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badWidth.Content.ReadAsStringAsync()).ShouldContain("file.render_size_unsupported");
    }

    /// <summary>
    /// The viewer's own path for a photo, at the width it displays: someone who may not place
    /// what the photo shows is shown the picture and never the stored bytes — the rendering
    /// carries none of the metadata the bytes do, and the whole picture they may download
    /// carries no location.
    /// </summary>
    [Fact]
    public async Task Showing_a_guarded_photo_serves_the_rendering_and_never_the_original()
    {
        var caveId = await CreateCaveAsync(locationProtected: true);
        var fileId = await UploadAsync("entrance.jpg", GeotaggedJpeg(45.53127, 25.44721, 1600, 1200), "image/jpeg");
        await AttachAsync(fileId, caveId);

        // Fixture proof, both halves: the upload read a capture point out of the image, and the
        // reader really is someone who may read this cave without being told where it is.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId)).Geom.ShouldNotBeNull();
        }

        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var seen = await ReadJsonAsync(await reader.GetAsync($"/api/v1/files/{fileId}"));

        // What the viewer shows: the largest rendering offered, asked for with the token that
        // came with the file. This is the substitution the display makes, and it works.
        var shown = seen.GetProperty("thumbnailUrl").GetString()!.Replace("size=480", "size=1200");
        var displayed = await reader.GetAsync(shown);
        displayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var rendering = new MagickImage(await displayed.Content.ReadAsByteArrayAsync()))
        {
            rendering.GetExifProfile().ShouldBeNull();
            rendering.ProfileNames.ShouldBeEmpty();
        }

        // What it must not be handed, however it is spelled: the stored bytes, which still hold
        // the fix the camera wrote. The published URL serves the whole picture with the fix
        // taken out instead.
        await PhotoDelivery.ShouldBeHandedWithoutLocationAsync(reader, seen);

        // The owner may place the cave, so both halves succeed for them — the refusal above is
        // the rule being applied, not a URL that never worked.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        (await owner.GetAsync(held.GetProperty("thumbnailUrl").GetString()!.Replace("size=480", "size=1200")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await PhotoDelivery.ShouldBeHandedStoredAsync(owner, held);
    }

    // ---- helpers ----

    private static string RenderUrl(Guid fileId, int page, int size, string token) =>
        $"/api/v1/files/{fileId}/pages/{page}/render?size={size}&token={Uri.EscapeDataString(token)}";

    private async Task<byte[]> BytesAsync(string url)
    {
        var response = await anonymous.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"View {Guid.NewGuid():N}"[..30],
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

    /// <summary>
    /// A portable document of the given number of pages, each drawing a different filled
    /// rectangle so one page's picture is not another's. Assembled here because nothing in the
    /// tree writes this format, and the cross-reference table has to carry real offsets.
    ///
    /// The page size is a parameter because it decides what the widths mean: a small page fits
    /// inside every offered box, so a fixture that only ever used one would agree with a renderer
    /// that ignored the width entirely.
    /// </summary>
    private static byte[] Pdf(int pageCount, int widthPoints = 300, int heightPoints = 300)
    {
        var contents = Enumerable.Range(0, pageCount)
            .Select(i => Ascii(
                $"{(i % 2 == 0 ? "0.1 0.2 0.9" : "0.9 0.3 0.1")} rg "
                + $"{20 + (i * 40)} 20 200 200 re f"))
            .ToList();

        var kids = string.Join(' ', Enumerable.Range(0, pageCount).Select(i => $"{3 + (2 * i)} 0 R"));
        var objects = new List<byte[]>
        {
            Ascii("<</Type/Catalog/Pages 2 0 R>>"),
            Ascii($"<</Type/Pages/Kids[{kids}]/Count {pageCount}>>"),
        };

        for (var i = 0; i < pageCount; i++)
        {
            objects.Add(Ascii(
                $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 {widthPoints} {heightPoints}]"
                + $"/Resources<<>>/Contents {4 + (2 * i)} 0 R>>"));
            objects.Add(
            [
                .. Ascii($"<</Length {contents[i].Length}>>\nstream\n"),
                .. contents[i],
                .. Ascii("\nendstream"),
            ]);
        }

        var file = new MemoryStream();
        var offsets = new List<long>(objects.Count);
        Write(file, Ascii("%PDF-1.4\n"));
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Length);
            Write(file, Ascii($"{i + 1} 0 obj\n"));
            Write(file, objects[i]);
            Write(file, Ascii("\nendobj\n"));
        }

        var startXref = file.Length;
        Write(file, Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            Write(file, Ascii($"{offset:D10} 00000 n \n"));
        }

        Write(file, Ascii(
            $"trailer\n<</Size {objects.Count + 1}/Root 1 0 R>>\nstartxref\n{startXref}\n%%EOF"));
        return file.ToArray();
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
