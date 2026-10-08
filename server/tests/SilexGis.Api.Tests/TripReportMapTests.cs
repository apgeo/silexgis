// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ImageMagick;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace SilexGis.Api.Tests;

/// <summary>
/// A write-up somebody downloads may carry a picture of a map their own browser drew. It is the
/// one part of the document this application did not make, so these cases are about the three
/// things that follow from that: the picture is taken as an upload and redrawn rather than
/// trusted, it goes into that person's own copy and never into the copy filed for every reader
/// of the trip, and nothing of it is kept once the answer has been sent.
/// </summary>
/// <remarks>
/// Stated over the bytes of the produced document, like the cases beside these that guard what a
/// write-up says in words. The bounds on a picture are set low for this class, so that a picture
/// a test can afford to make is one that crosses them.
/// </remarks>
public sealed class TripReportMapTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int MaxMapBytes = 60_000;
    private const int MaxMapPixels = 500_000;

    private const string DocxMediaType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Where the trip itself was drawn. Its digits are what the written sketch line carries, and
    // finding them beside the picture is what shows the words were kept.
    private const double SketchLat = 44.61357;
    private const double SketchLon = 24.28461;

    private const string OwnerName = "Ana Raportor";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;   // Editor — writes the trips, and has a name of their own
    private HttpClient reader = null!;  // Viewer — reads a trip, may not change it, has chosen no name
    private HttpClient admin = null!;   // needed only to store a layout
    private Guid readerId;

    public TripReportMapTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-tripmap-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Reports:MaxMapBytes"] = MaxMapBytes.ToString(CultureInfo.InvariantCulture),
            ["Reports:MaxMapPixels"] = MaxMapPixels.ToString(CultureInfo.InvariantCulture),
        });
    }

    public async Task InitializeAsync()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, ReaderEmail);
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"trm-adm-{suffix}@t.local");

        // One of the two has said what they are called. The other has not, which is the ordinary
        // state of an account nobody has edited — and the one in which a careless label would be
        // the address they signed in with.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var account = (await users.FindByIdAsync(ownerId.ToString())).ShouldNotBeNull();
            account.DisplayName = OwnerName;
            (await users.UpdateAsync(account)).Succeeded.ShouldBeTrue();
        }

        owner = await AuthHelper.BearerClientAsync(factory, OwnerEmail);
        reader = await AuthHelper.BearerClientAsync(factory, ReaderEmail);
        admin = await AuthHelper.BearerClientAsync(factory, $"trm-adm-{suffix}@t.local");
    }

    private string OwnerEmail => $"trm-own-{suffix}@t.local";

    private string ReaderEmail => $"trm-read-{suffix}@t.local";

    /// <summary>
    /// The picture goes where the write-up says where the trip went, after the words and not in
    /// place of them, and it says whose view it is.
    /// </summary>
    [Fact]
    public async Task A_download_carries_the_picture_it_was_sent_after_the_words_that_say_where()
    {
        var tripId = await CreateTripAsync(body =>
            body["geom"] = new { type = "Point", coordinates = new[] { SketchLon, SketchLat } });
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        using var response = await owner.PostAsync(DownloadUrl(tripId), Form(Map(600, 400)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe(DocxMediaType);
        response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().ShouldContain("trip-report");

        var after = DateOnly.FromDateTime(DateTime.UtcNow);
        using var document = Open(await response.Content.ReadAsByteArrayAsync());
        var paragraphs = Paragraphs(document);

        // One picture, and it is a picture a word processor reads: the format every other plate
        // in the document is written in, at the proportions it was sent in.
        var pictures = Pictures(document);
        pictures.Count.ShouldBe(1);
        using (var placed = new MagickImage(pictures[0]))
        {
            placed.Format.ShouldBe(MagickFormat.Jpeg);
            ((double)placed.Width / placed.Height).ShouldBe(600d / 400d, 0.01);
        }

        var sketch = paragraphs.FindIndex(p => p.Text.StartsWith("Sketch: ", StringComparison.Ordinal));
        var drawing = paragraphs.FindIndex(p => p.HasDrawing);
        sketch.ShouldBeGreaterThanOrEqualTo(0, "the written sketch line is gone");
        drawing.ShouldBe(sketch + 1, "the picture is not directly after the words that say where");

        // The words are kept: a position somebody can copy out of the document is still there.
        paragraphs[sketch].Text.ShouldContain(Digits(SketchLat));
        paragraphs[sketch].Text.ShouldContain(Digits(SketchLon));

        // And the line under the picture says whose view it is and when. Either side of
        // midnight, so the case does not fail for having been run across it.
        var caption = paragraphs[drawing + 1].Text;
        caption.ShouldBeOneOf(
            $"Map as shown to {OwnerName} on {before:yyyy-MM-dd} (UTC); positions as this reader may see them.",
            $"Map as shown to {OwnerName} on {after:yyyy-MM-dd} (UTC); positions as this reader may see them.");
    }

    /// <summary>
    /// Whoever the picture was drawn for is named the way every other surface names a person,
    /// which for somebody who never chose a name is not the address they sign in with.
    /// </summary>
    [Fact]
    public async Task The_line_under_the_picture_never_names_its_reader_by_their_address()
    {
        var tripId = await CreateTripAsync(_ => { });

        using var response = await reader.PostAsync(DownloadUrl(tripId), Form(Map(600, 400)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var document = Open(await response.Content.ReadAsByteArrayAsync());
        var text = document.MainDocumentPart!.Document!.InnerText;
        text.ShouldContain($"Map as shown to user-{readerId.ToString("N")[..8]} on ");
        text.ShouldNotContain(ReaderEmail);
        text.ShouldNotContain("t.local");
    }

    /// <summary>
    /// A request that brings no picture is answered with exactly the plain download's document,
    /// however it goes about bringing none.
    /// </summary>
    [Fact]
    public async Task Without_a_picture_the_answer_is_the_plain_downloads()
    {
        var tripId = await CreateTripAsync(body =>
        {
            body["description"] = "A day at the far sump.";
            body["geom"] = new { type = "Point", coordinates = new[] { SketchLon, SketchLat } };
        });

        using var plain = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
        using var expected = Open(await plain.Content.ReadAsByteArrayAsync());
        var expectedXml = expected.MainDocumentPart!.Document!.OuterXml;
        expectedXml.ShouldContain("A day at the far sump.");

        using var otherField = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        using var declaredOnly = new ByteArrayContent([]);
        declaredOnly.Headers.ContentType = new("multipart/form-data");
        foreach (var (how, body) in new (string, HttpContent?)[]
        {
            // The plainest request there is, and the one a route that expected a form would
            // answer as a fault rather than as a request for the document.
            ("no body at all", null),
            ("a form declared and nothing sent", declaredOnly),
            ("a form with something else in it", otherField),
        })
        {
            using var response = await owner.PostAsync(DownloadUrl(tripId), body);
            response.StatusCode.ShouldBe(
                HttpStatusCode.OK, $"{how}: {await response.Content.ReadAsStringAsync()}");
            response.Content.Headers.ContentType!.MediaType.ShouldBe(DocxMediaType);

            using var document = Open(await response.Content.ReadAsByteArrayAsync());
            document.MainDocumentPart!.Document!.OuterXml.ShouldBe(expectedXml, how);
            Pictures(document).ShouldBeEmpty(how);
        }
    }

    /// <summary>
    /// What is placed in the document is a picture this application drew from the one it was
    /// sent, so nothing that travelled inside the upload travels on inside the document.
    /// </summary>
    [Fact]
    public async Task The_picture_goes_in_redrawn_and_carries_nothing_it_arrived_with()
    {
        var tripId = await CreateTripAsync(_ => { });
        var upload = JpegCarryingAPosition();

        // The upload really does carry a position and a remark, so their absence below is a
        // removal and not a picture that never had them.
        using (var sent = new MagickImage(upload))
        {
            sent.GetExifProfile().ShouldNotBeNull().GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
            sent.Comment.ShouldBe("north entrance, behind the boulder");
        }

        using var response = await owner.PostAsync(DownloadUrl(tripId), Form(upload, fileName: "map.jpg"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var document = Open(bytes);
        var picture = Pictures(document).ShouldHaveSingleItem();
        picture.ShouldNotBe(upload);

        using var placed = new MagickImage(picture);
        placed.GetExifProfile().ShouldBeNull();
        placed.GetXmpProfile().ShouldBeNull();
        placed.GetIptcProfile().ShouldBeNull();
        placed.Comment.ShouldBeNull();
        Encoding.Latin1.GetString(picture).ShouldNotContain("boulder");
    }

    /// <summary>
    /// A write-up filed against the trip takes no picture, and says so rather than filing the
    /// document without it.
    /// </summary>
    /// <remarks>
    /// A filed copy is opened by everybody who may read the trip, and a picture shows what the
    /// one person who sent it may see. The page never sends one here; this is the same rule kept
    /// where a caller who is not the page meets it. Every way of bringing a picture is refused,
    /// and — the half that makes the refusal a rule about pictures — a request that brings none
    /// is filed as it always was.
    /// </remarks>
    [Fact]
    public async Task A_write_up_filed_against_the_trip_takes_no_picture()
    {
        var tripId = await CreateTripAsync(_ => { });
        var keepUrl = $"/api/v1/trip-logs/{tripId}/report";

        using var asPart = await owner.PostAsync(keepUrl, Form(Map(600, 400)));
        asPart.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await asPart.Content.ReadAsStringAsync());
        (await CodeOfAsync(asPart)).ShouldBe("trip_report.map_not_filed");

        // Under another name it is still a picture somebody tried to file.
        using var renamed = await owner.PostAsync(keepUrl, Form(Map(600, 400), partName: "picture"));
        renamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await renamed.Content.ReadAsStringAsync());
        (await CodeOfAsync(renamed)).ShouldBe("trip_report.map_not_filed");

        // And as the body itself.
        using var raw = new ByteArrayContent(Map(600, 400));
        raw.Headers.ContentType = new("image/png");
        using var asBody = await owner.PostAsync(keepUrl, raw);
        asBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await asBody.Content.ReadAsStringAsync());
        (await CodeOfAsync(asBody)).ShouldBe("trip_report.map_not_filed");

        // Somebody who could not have filed the write-up at all is refused as they always were,
        // whatever they sent: the picture is not a way to learn anything else about the trip.
        using var notTheirs = await reader.PostAsync(keepUrl, Form(Map(600, 400)));
        notTheirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await notTheirs.Content.ReadAsStringAsync());

        // Nothing was filed by any of that.
        (await ReportsOnTripAsync(tripId)).ShouldBeEmpty();

        // A request that brings no picture files the write-up, form or no form.
        using var noFile = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        using var kept = await owner.PostAsync(keepUrl, noFile);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        using var again = await owner.PostAsync(keepUrl, null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());

        var filed = (await ReportsOnTripAsync(tripId)).ShouldHaveSingleItem();
        using var filedResponse = await reader.GetAsync(filed.ContentUrl);
        filedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var filedDocument = Open(await filedResponse.Content.ReadAsByteArrayAsync());
        Pictures(filedDocument).ShouldBeEmpty();
        filedDocument.MainDocumentPart!.Document!.InnerText.ShouldNotContain("Map as shown to");
    }

    /// <summary>
    /// A picture is refused for the reason that is actually true of it, by a code a client can
    /// act on, and the refusal is a refusal of the request and not a fault.
    /// </summary>
    [Theory]
    [InlineData("nothing in the part", "trip_report.map_empty")]
    [InlineData("words", "trip_report.map_format_unsupported")]
    [InlineData("a gif", "trip_report.map_format_unsupported")]
    [InlineData("a png that breaks off", "trip_report.map_unreadable")]
    [InlineData("too many bytes", "trip_report.map_too_large")]
    [InlineData("too many pixels", "trip_report.map_too_many_pixels")]
    public async Task A_picture_that_cannot_be_taken_is_refused_by_what_is_wrong_with_it(
        string what, string code)
    {
        var tripId = await CreateTripAsync(_ => { });

        byte[] bytes = what switch
        {
            "nothing in the part" => [],
            "words" => Encoding.UTF8.GetBytes("not a picture at all"),
            "a gif" => Gif(),
            "a png that breaks off" => [.. Map(600, 400)[..40], .. new byte[200]],
            "too many bytes" => Noise(400, 300),
            _ => Map(1000, 600),
        };

        // The fixtures are what they claim: over one bound and inside the other, so each of the
        // two refusals is the one its own bound gives.
        if (what == "too many bytes")
        {
            bytes.Length.ShouldBeGreaterThan(MaxMapBytes);
            (400 * 300).ShouldBeLessThan(MaxMapPixels);
        }

        if (what == "too many pixels")
        {
            bytes.Length.ShouldBeLessThan(MaxMapBytes);
            (1000 * 600).ShouldBeGreaterThan(MaxMapPixels);
        }

        using var refused = await owner.PostAsync(DownloadUrl(tripId), Form(bytes));
        var payload = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = JsonDocument.Parse(payload).RootElement;
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();

        // The same trip, the same caller, a picture inside both bounds: taken. So each refusal
        // above was about the picture.
        using var taken = await owner.PostAsync(DownloadUrl(tripId), Form(Map(600, 400)));
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A body that is not a form is turned away as the wrong kind of body, not read as a request
    /// for the plain document: a caller who sent a picture some other way must not be handed a
    /// document without it and left to wonder.
    /// </summary>
    [Fact]
    public async Task A_body_that_is_not_a_form_is_turned_away_as_the_wrong_kind_of_body()
    {
        var tripId = await CreateTripAsync(_ => { });

        using var json = await owner.PostAsJsonAsync(DownloadUrl(tripId), new { map = "aGVsbG8=" });
        json.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);

        using var raw = new ByteArrayContent(Map(600, 400));
        raw.Headers.ContentType = new("image/png");
        using var bare = await owner.PostAsync(DownloadUrl(tripId), raw);
        bare.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    /// <summary>
    /// Who may have the document is decided exactly as the plain download decides it, and before
    /// anything is said about the picture.
    /// </summary>
    /// <remarks>
    /// Each refused request here carries a picture that would itself be refused. A route that
    /// looked at the picture first would answer about the picture, to somebody it should have
    /// told nothing but that there is no such trip for them.
    /// </remarks>
    [Fact]
    public async Task Who_may_have_the_document_is_settled_before_the_picture_is_looked_at()
    {
        var hidden = await CreateTripAsync(body => body["visibility"] = "private");
        var open = await CreateTripAsync(_ => { });
        var notAPicture = Encoding.UTF8.GetBytes("not a picture at all");

        using var anonymous = factory.CreateClient();
        using var unsigned = await anonymous.PostAsync(DownloadUrl(open), Form(notAPicture));
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var unreadable = await reader.PostAsync(DownloadUrl(hidden), Form(notAPicture));
        unreadable.StatusCode.ShouldBe(HttpStatusCode.NotFound, await unreadable.Content.ReadAsStringAsync());
        (await CodeOfAsync(unreadable)).ShouldBe("trip_log.not_found");

        // Letter for letter what the plain download tells the same reader about the same trip.
        using var plain = await reader.GetAsync($"/api/v1/trip-logs/{hidden}/report");
        plain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(plain)).ShouldBe("trip_log.not_found");

        using var noSuchTrip = await owner.PostAsync(DownloadUrl(Guid.NewGuid()), Form(notAPicture));
        noSuchTrip.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(noSuchTrip)).ShouldBe("trip_log.not_found");

        using var noSuchLayout = await owner.PostAsync(
            $"{DownloadUrl(open)}?templateId={Guid.NewGuid()}", Form(notAPicture));
        noSuchLayout.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(noSuchLayout)).ShouldBe("report_template.not_found");

        // …and the owner, with a real picture, gets the document of the trip the reader may not
        // read: the refusals above were about who was asking.
        using var theirs = await owner.PostAsync(DownloadUrl(hidden), Form(Map(600, 400)));
        theirs.StatusCode.ShouldBe(HttpStatusCode.OK, await theirs.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The picture is used for the one answer it came with and kept nowhere: no document, no
    /// stored file, no attachment, and nothing written under the file store.
    /// </summary>
    [Fact]
    public async Task Nothing_of_the_picture_is_kept()
    {
        var tripId = await CreateTripAsync(_ => { });
        var rowsBefore = await RowsAsync();
        var filesBefore = StoredFilesOnDisk();

        using var response = await owner.PostAsync(DownloadUrl(tripId), Form(Map(600, 400)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var document = Open(await response.Content.ReadAsByteArrayAsync()))
        {
            // It really was a download with a picture in it, so "nothing kept" is not the
            // emptiness of a request that did nothing.
            Pictures(document).Count.ShouldBe(1);
        }

        (await RowsAsync()).ShouldBe(rowsBefore);
        StoredFilesOnDisk().ShouldBe(filesBefore);
        (await ReportsOnTripAsync(tripId)).ShouldBeEmpty();
    }

    /// <summary>
    /// A trip that drew no sketch of its own still gets its picture where the layout talks about
    /// place — and a download without one still leaves that part out altogether.
    /// </summary>
    [Fact]
    public async Task A_trip_with_no_sketch_gets_its_picture_where_the_layout_speaks_of_place()
    {
        var tripId = await CreateTripAsync(body => body["description"] = "No sketch was drawn.");

        using var withPicture = await owner.PostAsync(DownloadUrl(tripId), Form(Map(600, 400)));
        withPicture.StatusCode.ShouldBe(HttpStatusCode.OK, await withPicture.Content.ReadAsStringAsync());
        using var pictured = Open(await withPicture.Content.ReadAsByteArrayAsync());
        var paragraphs = Paragraphs(pictured);

        var heading = paragraphs.FindIndex(p => p.Text == "Where");
        var drawing = paragraphs.FindIndex(p => p.HasDrawing);
        heading.ShouldBeGreaterThanOrEqualTo(0, "the part that says where was left out");
        drawing.ShouldBe(heading + 1);
        paragraphs.ShouldNotContain(p => p.Text.StartsWith("Sketch: ", StringComparison.Ordinal));

        // Without the picture there is nothing to say under that heading, and no heading.
        using var plain = await owner.PostAsync(DownloadUrl(tripId), null);
        using var unpictured = Open(await plain.Content.ReadAsByteArrayAsync());
        Paragraphs(unpictured).ShouldNotContain(p => p.Text == "Where");
    }

    /// <summary>
    /// A layout that says nowhere where the trip went still carries the picture its reader asked
    /// for, at the end, with its line under it.
    /// </summary>
    [Fact]
    public async Task A_layout_that_never_speaks_of_place_carries_the_picture_at_its_end()
    {
        var tripId = await CreateTripAsync(body =>
            body["geom"] = new { type = "Point", coordinates = new[] { SketchLon, SketchLat } });
        var layoutId = await StoreLayoutAsync("title: {title}\nfield: Date = {dates}\ntext: {description}");
        try
        {
            using var response = await owner.PostAsync(
                $"{DownloadUrl(tripId)}?templateId={layoutId}", Form(Map(600, 400)));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            using var document = Open(await response.Content.ReadAsByteArrayAsync());
            var paragraphs = Paragraphs(document);
            paragraphs.Count.ShouldBeGreaterThan(2);
            paragraphs[^2].HasDrawing.ShouldBeTrue("the picture is not the last thing in the document");
            paragraphs[^1].Text.ShouldStartWith("Map as shown to ");

            // The layout asked for no position in words, and got none: the picture did not bring
            // the sketch line back with it.
            document.MainDocumentPart!.Document!.InnerText.ShouldNotContain(Digits(SketchLat));
        }
        finally
        {
            // A stored layout belongs to the whole installation, so it does not outlive the case.
            (await admin.DeleteAsync($"/api/v1/report-templates/{layoutId}")).EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// The catalogue of map backgrounds says, source by source, whether a document may copy it —
    /// which is what the page drawing the picture goes by, and all it goes by.
    /// </summary>
    [Fact]
    public async Task The_catalogue_says_which_backgrounds_a_document_may_copy()
    {
        var layers = await reader.GetFromJsonAsync<JsonElement>("/api/v1/map-layers");
        var byName = layers.EnumerateArray().ToDictionary(
            layer => layer.GetProperty("name").GetString()!,
            layer => layer.GetProperty("inDocuments").GetBoolean());

        byName["OpenStreetMap"].ShouldBeTrue();

        // Present, switched on, and not to be copied: the sources filed under restricted terms.
        var restricted = byName.Where(pair => pair.Key.StartsWith("Google ", StringComparison.Ordinal)).ToList();
        restricted.ShouldNotBeEmpty();
        restricted.ShouldAllBe(pair => !pair.Value);

        // And an ordinary source nobody marked is not copied either — the answer is no unless
        // the catalogue says yes.
        byName["Esri World Imagery"].ShouldBeFalse();
    }

    // ---- the produced document --------------------------------------------------------------

    private sealed record Line(string Text, bool HasDrawing);

    /// <summary>
    /// Opens a produced document, and holds it to being one a word processor opens: the placed
    /// picture is the part of the format a reader refuses outright when it is wrong.
    /// </summary>
    private static WordprocessingDocument Open(byte[] bytes)
    {
        var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));
        return document;
    }

    private static List<Line> Paragraphs(WordprocessingDocument document) =>
        [.. document.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>()
            .Select(p => new Line(p.InnerText, p.Descendants<W.Drawing>().Any()))];

    private static List<byte[]> Pictures(WordprocessingDocument document)
    {
        var pictures = new List<byte[]>();
        foreach (var part in document.MainDocumentPart!.ImageParts)
        {
            using var stream = part.GetStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            pictures.Add(buffer.ToArray());
        }

        return pictures;
    }

    private static string Digits(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);

    // ---- pictures ---------------------------------------------------------------------------

    /// <summary>A plain picture of a given size: a ground, and two shapes so it is not one colour.</summary>
    private static byte[] Map(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.WhiteSmoke, width, height);
        using var mark = new MagickImage(MagickColors.Teal, width / 6, height / 6);
        image.Composite(mark, (int)(width / 3), (int)(height / 3), CompositeOperator.Over);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>A picture nothing compresses, so its size in bytes follows from its size in pixels.</summary>
    private static byte[] Noise(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.Black, width, height);
        image.AddNoise(NoiseType.Random);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static byte[] Gif()
    {
        using var image = new MagickImage(MagickColors.Olive, 32, 32);
        return image.ToByteArray(MagickFormat.Gif);
    }

    private static byte[] JpegCarryingAPosition()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 480, 320);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(45u), new Rational(8u), new Rational(22u)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(23u), new Rational(25u), new Rational(36u)]);
        image.SetProfile(exif);
        image.Comment = "north entrance, behind the boulder";
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static MultipartFormDataContent Form(
        byte[] bytes, string partName = "map", string fileName = "map.png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(
            fileName.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : "image/png");
        return new MultipartFormDataContent { { content, partName, fileName } };
    }

    // ---- arranging ----------------------------------------------------------------------------

    private static string DownloadUrl(Guid tripId) => $"/api/v1/trip-logs/{tripId}/report/download";

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    private async Task<Guid> CreateTripAsync(Action<Dictionary<string, object?>> shape)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = $"Map trip {Guid.NewGuid():N}"[..28],
            ["tripDate"] = "2026-07-01",
            ["participants"] = Array.Empty<object>(),
            // Readable by every account here, so the Viewer genuinely reads the trip and
            // genuinely cannot change it.
            ["visibility"] = "authenticated",
        };
        shape(body);

        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> StoreLayoutAsync(string body)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/report-templates/", new
        {
            name = $"Placeless {suffix}",
            body,
            isDefault = false,
            kind = "trip",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue(payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private sealed record FiledReport(string Name, string ContentUrl);

    /// <summary>What sits in the trip's report slot, as a reader of the trip is shown it.</summary>
    private async Task<List<FiledReport>> ReportsOnTripAsync(Guid tripId)
    {
        var attachments = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        return [.. attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .Select(a => new FiledReport(
                a.GetProperty("file").GetProperty("originalName").GetString() ?? string.Empty,
                a.GetProperty("file").GetProperty("contentUrl").GetString() ?? string.Empty))];
    }

    /// <summary>How many documents, stored files and attachments the installation holds.</summary>
    private async Task<(int Documents, int Files, int Attachments)> RowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (
            await db.Documents.IgnoreQueryFilters().CountAsync(),
            await db.StoredFiles.IgnoreQueryFilters().CountAsync(),
            await db.Attachments.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>
    /// Every file under the store, apart from the key ring the host keeps beside it — which is
    /// written when a token is first signed and has nothing to do with what was uploaded.
    /// </summary>
    private List<string> StoredFilesOnDisk()
    {
        if (!Directory.Exists(filesRoot))
        {
            return [];
        }

        var keys = Path.Combine(filesRoot, "keys") + Path.DirectorySeparatorChar;
        return [.. Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(keys, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
