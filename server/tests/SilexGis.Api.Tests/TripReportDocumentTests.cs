// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A generated write-up is the widest thing this application produces: one file holding a trip's
/// shape, the caves it names, its photographs, the people on it and its prose — and then it
/// leaves, forwarded and opened long after the rules that produced it changed. Each of those has
/// an audience of its own, and the audiences are not additive.
///
/// So every case here is stated over the bytes of the produced document rather than over the
/// code that produced it, and each one asserts both halves over one fixture: what the entitled
/// caller's copy says, and what the same request hands somebody who is not entitled to it. A
/// document that had quietly stopped carrying anything at all would satisfy a "does not contain"
/// assertion perfectly, which is why no case here consists of one.
///
/// The withheld side is always a Viewer. The seeded Editors group reads past visibility by
/// design, so an Editor who cannot see something proves nothing about the rule.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripReportDocumentTests : IAsyncLifetime, IDisposable
{
    // Distinctive enough to find in a document as text, and far enough from any obfuscation
    // grid that a rounded position could not contain these digits by accident. They belong to
    // this suite alone: the database is shared, and a cave another test placed at this point
    // would put these digits into a payload and be read here as a disclosure.
    private const double CaveLat = 45.13947;
    private const double CaveLon = 23.42681;

    // Where the trip itself was drawn — deliberately somewhere else, so that finding the trip's
    // own position in a document says nothing about whether the cave's leaked.
    private const double SketchLat = 44.51728;
    private const double SketchLon = 24.19356;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the caves and the trips, may place them
    private HttpClient reader = null!;  // Viewer — may read a trip, may not write it or place a cave
    private HttpClient admin = null!;   // needed only to give a club a purpose with a safety section
    private long caveTypeId;
    private long entranceTypeId;

    public TripReportDocumentTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-tripreport-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trp-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"trp-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trp-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"trp-read-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"trp-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"trp-adm-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// The caves a write-up names are the ones its reader's own reading of the trip named — the
    /// list the trip read already redacted — and never the rows behind it.
    /// </summary>
    /// <remarks>
    /// Both caves are on the trip and both readings are taken over the one fixture, because the
    /// failure this guards against is not "redaction stopped working" but "the document stopped
    /// asking for it": a document built from the link rows would name the guarded cave, and a
    /// document that had stopped naming caves at all would pass a bare "does not contain".
    /// </remarks>
    [Fact]
    public async Task A_cave_this_reader_may_not_place_is_not_named_in_the_document_they_are_handed()
    {
        var guarded = await CreateCaveAsync(locationProtected: true);
        var open = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync(body => body["caveIds"] = new[] { guarded.Id, open.Id });

        // The person who created the caves may place both, so their copy names both — which is
        // what makes the shorter list below a redaction rather than an empty store.
        var ownersCopy = await DocumentTextAsync(owner, tripId);
        ownersCopy.ShouldContain(guarded.Name);
        ownersCopy.ShouldContain(open.Name);

        var readersCopy = await DocumentTextAsync(reader, tripId);
        readersCopy.ShouldContain(open.Name);
        readersCopy.ShouldNotContain(guarded.Name);
    }

    /// <summary>
    /// A cave this reader may not open is not named in the document they are handed, even though
    /// the trip they may read names it.
    /// </summary>
    /// <remarks>
    /// The trip's list of caves says which caves the trip was about; it is not a right to read
    /// those caves. On the screen an unreadable one is a bare identifier, because the page asks
    /// the cave itself and is refused — so a document that named it would be stating something
    /// the screen would not, which is the one thing a circulated file must never do.
    /// </remarks>
    [Fact]
    public async Task A_cave_this_reader_may_not_open_is_not_named_in_the_document_they_are_handed()
    {
        var hidden = await CreateCaveAsync(locationProtected: false, visibility: "private");
        var open = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync(body => body["caveIds"] = new[] { hidden.Id, open.Id });

        // What the screen does with it: the trip names the cave, and asking for the cave is
        // refused, so the page has nothing but the identifier to print.
        (await reader.GetAsync($"/api/v1/caves/{hidden.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ownersCopy = await DocumentTextAsync(owner, tripId);
        ownersCopy.ShouldContain(hidden.Name);
        ownersCopy.ShouldContain(open.Name);

        var readersCopy = await DocumentTextAsync(reader, tripId);
        readersCopy.ShouldContain(open.Name);
        readersCopy.ShouldNotContain(hidden.Name);
    }

    /// <summary>
    /// The account of what went wrong is written up only for whoever may change the trip; that
    /// something went wrong is written up for every reader.
    /// </summary>
    /// <remarks>
    /// A narrower audience is only as narrow as its widest emitter, and a generated document is
    /// the widest one built over this field: it is downloaded, kept and forwarded. The reader
    /// here genuinely reads the trip and genuinely cannot write it, which is the only state that
    /// proves anything.
    /// </remarks>
    [Fact]
    public async Task What_went_wrong_is_written_up_only_for_whoever_may_change_the_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTripTypeAsync(suffix);
        const string Account = "Ana ran out of light below the third pitch; the spare was at camp.";

        var tripId = await CreateTripAsync(body =>
        {
            body["tripTypeId"] = typeId;
            body["hadIncident"] = true;
            body["safety"] = JsonSerializer.SerializeToElement(new { incident_account = Account });
        });

        var writersCopy = await DocumentTextAsync(owner, tripId);
        writersCopy.ShouldContain(Account);

        // The reader is handed the trip, and told that something went wrong, and told nothing at
        // all where the account would be — no empty heading either, which on a circulated
        // document would read as "nothing happened" on exactly the record where that is worst.
        var readersCopy = await DocumentTextAsync(reader, tripId);
        readersCopy.ShouldContain("Incident");
        readersCopy.ShouldNotContain("third pitch");
        readersCopy.ShouldNotContain("Safety");
    }

    /// <summary>
    /// No coordinate appears in a document that its caller could not read on screen.
    /// </summary>
    /// <remarks>
    /// Two positions are in play and they answer differently. A cave's position is guarded, and
    /// no document states it — not even for the person who may see it on the cave's own page,
    /// because a write-up names caves and never places them. The trip's own sketch is exact for
    /// everybody who may read the trip, by decision, so it is written out for both callers: that
    /// is what makes the silence about the cave a rule rather than an empty document.
    /// </remarks>
    [Fact]
    public async Task No_coordinate_the_caller_could_not_read_on_screen_is_in_the_document()
    {
        var guarded = await CreateCaveAsync(locationProtected: true);
        await AddEntranceAsync(guarded.Id);
        var tripId = await CreateTripAsync(body =>
        {
            body["caveIds"] = new[] { guarded.Id };
            body["geom"] = new { type = "Point", coordinates = new[] { SketchLon, SketchLat } };
        });

        // The position genuinely exists in this installation and the owner genuinely reads it,
        // so a document that stayed silent about it stayed silent by rule.
        var entrances = await owner.GetStringAsync($"/api/v1/caves/{guarded.Id}/entrances");
        entrances.ShouldContain(Digits(CaveLat));
        entrances.ShouldContain(Digits(CaveLon));

        foreach (var (who, copy) in new[]
        {
            ("owner", await DocumentTextAsync(owner, tripId)),
            ("reader", await DocumentTextAsync(reader, tripId)),
        })
        {
            copy.ShouldNotContain(Digits(CaveLat), Case.Sensitive, $"{who}'s copy places the cave");
            copy.ShouldNotContain(Digits(CaveLon), Case.Sensitive, $"{who}'s copy places the cave");

            // The trip's own shape, which both of them see exactly as drawn on the trip's page.
            copy.ShouldContain(Digits(SketchLat), Case.Sensitive, $"{who}'s copy lost the sketch");
            copy.ShouldContain(Digits(SketchLon), Case.Sensitive, $"{who}'s copy lost the sketch");
        }
    }

    /// <summary>
    /// A photograph goes into a document as a rendering, and the rendering carries none of the
    /// upload's metadata.
    /// </summary>
    /// <remarks>
    /// A photograph taken at a cave holds the position in its own metadata — the position
    /// itself, not a fact about it — so a document that embedded the upload would publish the
    /// entrance to anybody the file is forwarded to, whatever the trip's page withholds.
    /// </remarks>
    [Fact]
    public async Task A_photograph_goes_in_as_a_rendering_and_carries_no_metadata_profile()
    {
        var tripId = await CreateTripAsync(_ => { });
        var original = GeotaggedJpeg(CaveLat, CaveLon);
        await AttachToTripAsync(await UploadAsync("plate.jpg", original), tripId);

        // The upload really does carry the fix, so the stripped copy below is stripped rather
        // than merely different.
        using (var uploaded = new MagickImage(original))
        {
            uploaded.GetExifProfile().ShouldNotBeNull()
                .GetValue(ExifTag.GPSLatitude).ShouldNotBeNull();
        }

        var pictures = await PicturesAsync(owner, tripId);
        pictures.Count.ShouldBe(1);

        using var embedded = new MagickImage(pictures[0]);
        embedded.GetExifProfile().ShouldBeNull();
        embedded.GetXmpProfile().ShouldBeNull();
        embedded.GetIptcProfile().ShouldBeNull();
        pictures[0].ShouldNotBe(original);
    }

    /// <summary>
    /// A write-up kept against the trip lands in the slot the trip's report document lives in,
    /// and answers to who may change the trip rather than to who may read it.
    /// </summary>
    [Fact]
    public async Task Keeping_a_write_up_fills_the_report_slot_and_needs_write_on_the_trip()
    {
        var tripId = await CreateTripAsync(_ => { });

        var refused = await reader.PostAsync($"/api/v1/trip-logs/{tripId}/report", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        var kept = await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());

        var attachments = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        var reports = attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .ToList();
        reports.Count.ShouldBe(1);

        // Regenerating replaces rather than accumulates: two reports side by side would leave
        // which one is current to whichever happened to be listed first.
        var again = await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var after = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        after.EnumerateArray()
            .Count(a => a.GetProperty("role").GetString() == "report")
            .ShouldBe(1);
    }

    /// <summary>
    /// What is filed against the trip holds only what every reader of the trip already holds.
    /// </summary>
    /// <remarks>
    /// A file attached to a trip is reachable by everybody who may read that trip — reading it is
    /// not gated on being allowed to change anything. So the copy that is filed cannot be the
    /// copy the person filing it would download: that one carries the account of what went wrong
    /// and the caves only they may place, and filing it would hand a narrower audience's material
    /// to a wider one, permanently and silently. Both halves are asserted over one fixture,
    /// because a filed copy that had quietly become empty would satisfy the withholding half on
    /// its own.
    /// </remarks>
    [Fact]
    public async Task What_is_filed_against_the_trip_holds_only_what_every_reader_of_it_holds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTripTypeAsync(suffix);
        const string Account = "Vlad slipped on the traverse; the deviation had been left off the rig.";

        var guarded = await CreateCaveAsync(locationProtected: true);
        var open = await CreateCaveAsync(locationProtected: false);
        var tripId = await CreateTripAsync(body =>
        {
            body["tripTypeId"] = typeId;
            body["hadIncident"] = true;
            body["safety"] = JsonSerializer.SerializeToElement(new { incident_account = Account });
            body["caveIds"] = new[] { guarded.Id, open.Id };
        });

        // The copy the filer downloads carries both, which is what makes their absence below a
        // rule about the filed copy rather than a document that stopped saying anything.
        var ownersCopy = await DocumentTextAsync(owner, tripId);
        ownersCopy.ShouldContain(Account);
        ownersCopy.ShouldContain(guarded.Name);

        var kept = await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());

        var filed = await FiledReportTextAsync(reader, tripId);
        filed.ShouldContain(open.Name);
        filed.ShouldNotContain(Account);
        filed.ShouldNotContain(guarded.Name);
    }

    /// <summary>
    /// A report somebody filed by hand stays on the trip when a write-up is generated.
    /// </summary>
    /// <remarks>
    /// Two things write to the report slot: a club uploading the report it wrote, and this
    /// generator. An uploaded document is reached through the object it hangs on and nowhere
    /// else, so taking it off the trip leaves nobody but its uploader able to find it again —
    /// which is not something pressing "keep the write-up" should do, and would not be reported
    /// if it did.
    /// </remarks>
    [Fact]
    public async Task A_report_filed_by_hand_survives_a_generated_one_taking_its_place()
    {
        var tripId = await CreateTripAsync(_ => { });
        const string ClubsOwn = "club-report.jpg";
        await AttachToTripAsync(
            await UploadAsync(ClubsOwn, GeotaggedJpeg(SketchLat, SketchLon)), tripId, role: "report");

        (await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var reports = await ReportsOnTripAsync(owner, tripId);
        reports.Count(name => name == ClubsOwn).ShouldBe(1, "the club's own report was taken off the trip");

        // …and the generated ones still replace each other rather than accumulate.
        reports.Count(name => name.StartsWith("trip-report-", StringComparison.Ordinal)).ShouldBe(1);
    }

    /// <summary>A trip nobody may read has no document either — the page's own refusal.</summary>
    [Fact]
    public async Task A_trip_this_caller_cannot_read_has_no_document()
    {
        var tripId = await CreateTripAsync(body => body["visibility"] = "private");

        var refused = await reader.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/trip-logs/{tripId}/report")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // …and the owner's own copy is produced, so the refusal is about the caller.
        (await DocumentTextAsync(owner, tripId)).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>The words of a generated document, as a word processor would read them.</summary>
    private static async Task<string> DocumentTextAsync(HttpClient client, Guid tripId)
    {
        using var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        response.Content.Headers.ContentDisposition!.FileName
            .ShouldNotBeNull().ShouldContain("trip-report");

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);

        // A file a word processor refuses to open is not a report. Checked here rather than in a
        // suite of its own so that every case below is stated over a document that opens.
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));

        return document.MainDocumentPart!.Document!.InnerText;
    }

    /// <summary>
    /// The words of the write-up filed against the trip, fetched the way a reader of the trip
    /// reaches it: the trip's attachments, and the delivery URL the listing hands out.
    /// </summary>
    private static async Task<string> FiledReportTextAsync(HttpClient client, Guid tripId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        var report = attachments.EnumerateArray()
            .Single(a => a.GetProperty("role").GetString() == "report");

        using var response = await client.GetAsync(
            report.GetProperty("file").GetProperty("contentUrl").GetString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return document.MainDocumentPart!.Document!.InnerText;
    }

    /// <summary>What sits in the trip's report slot, by file name.</summary>
    private static async Task<List<string>> ReportsOnTripAsync(HttpClient client, Guid tripId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        return [.. attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .Select(a => a.GetProperty("file").GetProperty("originalName").GetString() ?? string.Empty)];
    }

    /// <summary>The pictures inside a generated document, as bytes.</summary>
    private static async Task<List<byte[]>> PicturesAsync(HttpClient client, Guid tripId)
    {
        using var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);

        // The placed-picture markup is the fiddliest part of the format and the part a reader
        // refuses outright when it is wrong, so a document carrying one is checked as a document
        // before anything is said about its contents.
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));

        var pictures = new List<byte[]>();
        foreach (var part in document.MainDocumentPart!.ImageParts)
        {
            using var stream = part.GetStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            pictures.Add(buffer.ToArray());
        }

        return pictures;
    }

    private static string Digits(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);

    private async Task<(Guid Id, string Name)> CreateCaveAsync(
        bool locationProtected, string visibility = "authenticated")
    {
        var name = $"Report {Guid.NewGuid():N}"[..30];
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return (JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>The cave's position: it hangs on an entrance, not on the cave row.</summary>
    private async Task AddEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { CaveLon, CaveLat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateTripAsync(Action<Dictionary<string, object?>> shape)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = $"Report trip {Guid.NewGuid():N}"[..30],
            ["tripDate"] = "2026-07-01",
            ["participants"] = Array.Empty<object>(),
            // Readable by every account here, so the Viewer below genuinely reads the trip and
            // genuinely cannot change it — the only state a withholding rule can be proved on.
            ["visibility"] = "authenticated",
        };
        shape(body);

        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A club purpose whose safety section asks what happened.</summary>
    private async Task<long> CreateTripTypeAsync(string suffix)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/trip-types", new
        {
            code = $"report_{suffix}",
            name = $"Report {suffix}",
            description = (string?)null,
            sortOrder = 0,
            fieldDataSchema = (string?)null,
            logisticsSchema = (string?)null,
            safetySchema =
                """{"type":"object","properties":{"incident_account":{"type":"string","title":"What happened"}}}""",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachToTripAsync(Guid fileId, Guid tripId, string role = "other")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "tripLog",
            entityId = tripId,
            role,
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static byte[] GeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.SaddleBrown, 320, 240);
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
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
