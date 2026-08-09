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
[Collection(PostgresCollection.Name)]
public sealed class DocumentViewerBytesTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the cave, uploads onto it, may place it
    private HttpClient reader = null!;  // Viewer — reads the cave, may not place it
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public DocumentViewerBytesTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-viewbytes-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
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
    /// what the photo shows is shown the picture and never the stored bytes, and the picture
    /// they are shown carries none of the metadata the bytes do.
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
        seen.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();

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

        // What it must not reach for, however it is spelled: the stored bytes, which still hold
        // the fix the camera wrote. The published URL is refused, and so is the same URL asked
        // for by a screen that is only displaying something.
        (await reader.GetAsync(seen.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner may place the cave, so both halves succeed for them — the refusal above is
        // the rule being applied, not a URL that never worked.
        var held = await ReadJsonAsync(await owner.GetAsync($"/api/v1/files/{fileId}"));
        held.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        (await owner.GetAsync(held.GetProperty("thumbnailUrl").GetString()!.Replace("size=480", "size=1200")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync(held.GetProperty("contentUrl").GetString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
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

    public Task DisposeAsync() => Task.CompletedTask;

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
