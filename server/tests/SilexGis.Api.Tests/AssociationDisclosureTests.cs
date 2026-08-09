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
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a caller without exact-location rights is told about a protected cave's documents.
/// The document is never the thing withheld — being attached to a protected feature does not
/// hide it, keep it out of a listing or withhold its text. What is withheld is the pairing.
///
/// Every negative here is built rather than assumed: the reader is a Viewer, who holds
/// nothing over documents and nothing over exact locations, and the cave is protected by its
/// own row. The owner is in each test as the positive case — they created the cave, so
/// ownership gives them exact view of it — which is what stops a fixture that quietly stopped
/// attaching anything from reading as a passing security assertion.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssociationDisclosureTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — creates the protected cave and uploads onto it
    private HttpClient reader = null!;  // Viewer — reads the cave, may not place it exactly
    private long caveTypeId;

    public AssociationDisclosureTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-assoc-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ad-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"ad-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ad-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"ad-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task A_document_on_a_protected_cave_is_served_while_the_attachment_naming_it_is_not()
    {
        var caveId = await CreateProtectedCaveAsync();
        var fileId = await UploadAsync("survey-report.txt", "notes"u8.ToArray(), "text/plain");
        var attachmentId = await AttachAsync(fileId, caveId);
        var documentId = await DocumentIdOfAsync(fileId);

        // The cave is readable and the position is not: this is the caller the rule is about,
        // and this is the fixture proving they really are that caller.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The document itself is served, through the very attachment whose existence is being
        // kept from them — the point of the rule. Withholding the document instead would take
        // the whole archive of a protected cave away from everyone who works on it.
        var document = await reader.GetAsync($"/api/v1/documents/{documentId}");
        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(document)).GetProperty("title").GetString().ShouldBe("survey-report.txt");

        // What they are not told is that it is this cave's document.
        (await AttachmentIdsAsync(reader, caveId)).ShouldBeEmpty();
        (await SummaryAttachmentCountAsync(reader, caveId)).ShouldBe(0);

        // And the owner, who may place the cave exactly, is told all of it — so the absence
        // above is the rule and not an empty fixture.
        (await AttachmentIdsAsync(owner, caveId)).ShouldBe([attachmentId]);
        (await SummaryAttachmentCountAsync(owner, caveId)).ShouldBe(1);
    }

    [Fact]
    public async Task The_setting_shows_the_association_and_still_refuses_the_position()
    {
        var caveId = await CreateProtectedCaveAsync();
        var attachmentId = await AttachAsync(
            await UploadAsync("trip-report.txt", "notes"u8.ToArray(), "text/plain"), caveId);

        // Off, which is how an installation starts.
        (await AttachmentIdsAsync(reader, caveId)).ShouldBeEmpty();

        await SetRevealAsync(true);

        // On, the same caller is told which cave the report is about…
        (await AttachmentIdsAsync(reader, caveId)).ShouldBe([attachmentId]);
        (await SummaryAttachmentCountAsync(reader, caveId)).ShouldBe(1);

        // …and following it changes nothing about where the cave is: the feature is authorised
        // exactly as it always was, so the position is still the protected one.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        var ownerCave = await ReadJsonAsync(await owner.GetAsync($"/api/v1/caves/{caveId}"));
        ownerCave.GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_photo_carrying_its_own_position_stays_unpaired_whichever_way_the_setting_is_set()
    {
        var caveId = await CreateProtectedCaveAsync();
        var photoFileId = await UploadAsync("entrance.jpg", GeotaggedJpeg(45.53127, 25.44721), "image/jpeg");
        var photoAttachmentId = await AttachAsync(photoFileId, caveId);
        var noteAttachmentId = await AttachAsync(
            await UploadAsync("notes.txt", "notes"u8.ToArray(), "text/plain"), caveId);

        // Fixture proof: the upload really did read a capture point out of the image, so the
        // carve-out below is being exercised rather than merely not contradicted.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photoFileId)).Geom.ShouldNotBeNull();
        }

        (await AttachmentIdsAsync(reader, caveId)).ShouldBeEmpty();

        await SetRevealAsync(true);

        // The setting reveals names. A photo stamped with where it was taken, placed beside
        // this cave's name, is not a name — it is the cave's position to within a walk — so
        // that one pairing stays shut while the ordinary one opens.
        (await AttachmentIdsAsync(reader, caveId)).ShouldBe([noteAttachmentId]);
        (await SummaryAttachmentCountAsync(reader, caveId)).ShouldBe(1);

        // The owner may place the cave exactly, so nothing is withheld from them either way.
        (await AttachmentIdsAsync(owner, caveId)).Order().ShouldBe(new[] { photoAttachmentId, noteAttachmentId }.Order());
    }

    [Fact]
    public async Task An_unprotected_cave_discloses_its_documents_to_everyone_who_can_read_it()
    {
        // The rule reaches only protected positions: a cave nobody guards keeps working the
        // way it always has, setting or no setting.
        var caveId = await CreateCaveAsync(locationProtected: false);
        var attachmentId = await AttachAsync(
            await UploadAsync("open.txt", "notes"u8.ToArray(), "text/plain"), caveId);

        (await AttachmentIdsAsync(reader, caveId)).ShouldBe([attachmentId]);
        (await SummaryAttachmentCountAsync(reader, caveId)).ShouldBe(1);

        // Same document, same caller, on a cave that is guarded: withheld.
        var protectedCaveId = await CreateProtectedCaveAsync();
        await AttachAsync(await UploadAsync("closed.txt", "notes"u8.ToArray(), "text/plain"), protectedCaveId);
        (await AttachmentIdsAsync(reader, protectedCaveId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_change_timeline_names_the_attached_document_under_the_same_rule_as_every_other_surface()
    {
        // The timeline is a second way of asking "what is attached to this cave", and an
        // audit row keeps its diff long after the attachment listing has been filtered. It
        // has to answer the way the listing answers, in all four states — otherwise the
        // pairing the other surfaces withhold is readable from the history page.
        var caveId = await CreateProtectedCaveAsync();
        var noteFileId = await UploadAsync("survey-notes.txt", "notes"u8.ToArray(), "text/plain");
        await AttachAsync(noteFileId, caveId, caption: "sump survey");

        // Default: the event is there — somebody attached something, and when — while what
        // they attached and what they called it are not.
        var withheld = await AttachmentEventAsync(reader, caveId);
        Names(withheld).ShouldBeFalse();
        Redacted(withheld).ShouldBe(["FileId", "Caption"]);

        // The owner may place the cave exactly, so nothing is taken from their timeline. This
        // is the leg that stops an empty or broken fixture from reading as a passing assertion.
        var owned = await AttachmentEventAsync(owner, caveId);
        Names(owned).ShouldBeTrue();
        Redacted(owned).ShouldBeEmpty();

        await SetRevealAsync(true);

        // With the setting on, the same caller is told which document it is — the timeline
        // reads the installation's answer rather than keeping one of its own.
        Names(await AttachmentEventAsync(reader, caveId)).ShouldBeTrue();

        // …and the position is still refused, which is what the setting does not touch.
        var seenCave = await ReadJsonAsync(await reader.GetAsync($"/api/v1/caves/{caveId}"));
        seenCave.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();

        // The carve-out the setting may not open: a photo stamped with where it was taken,
        // named beside this cave, places the cave. On a fresh protected cave so the assertion
        // is about the photo and not about the note above.
        var photoCaveId = await CreateProtectedCaveAsync();
        var photoFileId = await UploadAsync("entrance.jpg", GeotaggedJpeg(45.53127, 25.44721), "image/jpeg");
        await AttachAsync(photoFileId, photoCaveId, caption: "entrance");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photoFileId)).Geom.ShouldNotBeNull();
        }

        var carveOut = await AttachmentEventAsync(reader, photoCaveId);
        Names(carveOut).ShouldBeFalse();
        Redacted(carveOut).ShouldBe(["FileId", "Caption"]);
        Names(await AttachmentEventAsync(owner, photoCaveId)).ShouldBeTrue();
    }

    // ---- helpers ----

    /// <summary>The creation event of the attachment on this cave's own timeline.</summary>
    private static async Task<JsonElement> AttachmentEventAsync(HttpClient client, Guid caveId)
    {
        var history = await ReadJsonAsync(
            await client.GetAsync($"/api/v1/history?entityType=feature&entityId={caveId}"));
        var events = history.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == "Attachment")
            .ToList();
        events.Count.ShouldBe(1);
        return events[0];
    }

    /// <summary>Whether the row still says which document it is about.</summary>
    private static bool Names(JsonElement e)
    {
        var changes = e.GetProperty("changes");
        return changes.ValueKind == JsonValueKind.Object
            && changes.TryGetProperty("FileId", out _)
            && changes.TryGetProperty("Caption", out _);
    }

    private static string[] Redacted(JsonElement e) =>
        [.. e.GetProperty("redactedProperties").EnumerateArray().Select(x => x.GetString()!)];

    /// <summary>
    /// Flips the installation's setting. The row lives in a database shared with every other
    /// class in this collection, which is why <see cref="DisposeAsync"/> deletes it: a policy
    /// left switched on here would quietly change what those classes are testing.
    /// </summary>
    private async Task SetRevealAsync(bool reveal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(
            AppSettingSections.Protection, new ProtectionSettings { RevealProtectedAssociations = reveal });
    }

    private Task<Guid> CreateProtectedCaveAsync() => CreateCaveAsync(locationProtected: true);

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Assoc {Guid.NewGuid():N}"[..30],
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

    private async Task<Guid> AttachAsync(Guid fileId, Guid caveId, string? caption = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            caption,
            sortOrder = 0,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid[]> AttachmentIdsAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={caveId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray().Select(a => a.GetProperty("id").GetGuid())];
    }

    private static async Task<int> SummaryAttachmentCountAsync(HttpClient client, Guid caveId)
    {
        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/summary");
        return summary.GetProperty("attachmentCount").GetInt32();
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static byte[] GeotaggedJpeg(double lat, double lon)
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

    /// <summary>Degrees → EXIF degrees/minutes/seconds rationals.</summary>
    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Protection).ExecuteDeleteAsync();
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
