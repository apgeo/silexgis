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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The document surface as a resource domain of its own: rules written against a document
/// decide who may read and who may write it, uploading one is a Create right, and a refusal
/// never discloses that the document exists.
///
/// Every negative here builds the state it claims: a Viewer holds nothing over documents
/// beyond the built-ins, so a Viewer with no grant genuinely cannot reach a document rather
/// than merely happening not to; where a domain-wide allow is in play, the refusal is an
/// explicit deny. Each one asserts the matching positive in the same test, so a fixture that
/// silently stopped working cannot pass as a passing security assertion.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentAccessApiTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads and owns the documents below
    private HttpClient reader = null!;  // Viewer — nothing over documents until granted
    private HttpClient editor = null!;  // second Editor — domain-wide rights from the seed
    private HttpClient anonymous = null!;
    private Guid readerId;
    private Guid editorId;

    public DocumentAccessApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-docacl-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"da-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"da-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"da-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"da-read-{suffix}@t.local");

        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"da-ed-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"da-ed-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_document_is_served_to_its_owner_and_withheld_from_a_caller_no_rule_reaches()
    {
        var documentId = await UploadDocumentAsync("field-notes.txt", "notes"u8.ToArray());

        // The uploader owns the document, so the ownership built-in admits them.
        var mine = await owner.GetAsync($"/api/v1/documents/{documentId}");
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(mine)).GetProperty("title").GetString().ShouldBe("field-notes.txt");

        // A Viewer holds no document rights of any kind and the document is private, so
        // nothing reaches it — and its existence is not disclosed by the refusal.
        var refused = await reader.GetAsync($"/api/v1/documents/{documentId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");

        // The same caller, named by a rule against this one document, reads it. Same
        // document, same request — only the rule changed.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);
        var granted = await reader.GetAsync($"/api/v1/documents/{documentId}");
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(granted)).GetProperty("id").GetGuid().ShouldBe(documentId);
    }

    [Fact]
    public async Task A_deny_written_against_one_document_binds_over_a_domain_wide_allow()
    {
        var denied = await UploadDocumentAsync("minutes.txt", "minutes"u8.ToArray());
        var untouched = await UploadDocumentAsync("agenda.txt", "agenda"u8.ToArray());

        // The seeded editor ruleset carries Read on documents domain-wide, so both are
        // readable before anything narrows that.
        (await editor.GetAsync($"/api/v1/documents/{denied}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.GetAsync($"/api/v1/documents/{untouched}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await GrantAsync(editorId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, denied);

        // A deny naming the document is more specific than the domain-wide allow and wins;
        // it takes back that document and nothing else.
        var refused = await editor.GetAsync($"/api/v1/documents/{denied}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");
        (await editor.GetAsync($"/api/v1/documents/{untouched}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reader_of_a_document_cannot_write_it_until_a_rule_says_so()
    {
        var documentId = await UploadDocumentAsync("draft.txt", "draft"u8.ToArray());
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);

        // Reading is not writing: a caller who may read learns only that they may not
        // write, which is why this is a refusal rather than a disappearance.
        var refused = await reader.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Renamed" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("document.write_forbidden");

        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Write, AccessScopeKind.Object, documentId);
        var accepted = await reader.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Renamed" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(accepted)).GetProperty("title").GetString().ShouldBe("Renamed");
    }

    [Fact]
    public async Task A_caller_no_rule_reaches_is_refused_the_update_without_learning_the_document_exists()
    {
        var documentId = await UploadDocumentAsync("private.txt", "private"u8.ToArray());

        // No read, no write, no disclosure — the refusal is indistinguishable from a
        // document that was never there.
        var refused = await reader.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Renamed" });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");

        // Proving the request itself is well-formed: the owner's identical call succeeds.
        var accepted = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Renamed" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_document_routes_require_a_signed_in_caller()
    {
        var documentId = await UploadDocumentAsync("open.txt", "open"u8.ToArray());

        (await anonymous.GetAsync($"/api/v1/documents/{documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Renamed" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The routes work — it is the missing caller that is refused, not the request.
        (await owner.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Uploading_a_document_needs_the_create_right_in_the_documents_domain()
    {
        // A Viewer's baseline rules carry no Create over documents, so the upload is
        // refused before a single byte is filed.
        using var form = BuildForm("unwanted.txt", "nope"u8.ToArray(), "text/plain");
        var refused = await reader.PostAsync("/api/v1/files/", form);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe(CreateRules.ForbiddenCode);

        // Granted domain-wide Create — the shape that answers "may this person add a
        // document at all" — the very same upload is accepted.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Create, AccessScopeKind.All);
        using var retry = BuildForm("wanted.txt", "yes"u8.ToArray(), "text/plain");
        var accepted = await reader.PostAsync("/api/v1/files/", retry);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_update_is_validated_before_any_rule_is_consulted()
    {
        var documentId = await UploadDocumentAsync("titled.txt", "body"u8.ToArray());

        var rejected = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "" });
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Metadata must be an object, not a scalar dressed as one.
        var badMetadata = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { title = "Fine", metadata = 7 });
        badMetadata.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new { title = "Fine" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reading_a_photo_document_is_not_a_route_to_where_the_photo_was_taken()
    {
        const double lat = 45.8;
        const double lon = 25.8;
        var fileId = await UploadFileAsync("cave-entrance.jpg", MakeGeotaggedJpeg(lat, lon), "image/jpeg");
        var documentId = await DocumentIdOfAsync(fileId);

        // Fixture proof: the capture point really was read off the bytes and stored, and it
        // really is inside the bbox queried below. Without this the assertions underneath
        // could pass for a photo that simply has no position.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
            stored.Geom.ShouldNotBeNull();
            stored.Geom!.Y.ShouldBe(lat, 0.001);
            stored.Geom.X.ShouldBe(lon, 0.001);
        }

        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);

        // The grant genuinely works — the document is served in full …
        var served = await reader.GetAsync($"/api/v1/documents/{documentId}");
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await served.Content.ReadAsStringAsync();
        JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid().ShouldBe(documentId);

        // … and it carries no position: the document surface describes the content, never
        // where it was taken. The coordinate is not merely hidden by the client.
        payload.ShouldNotContain("45.8");
        payload.ShouldNotContain("25.8");
        payload.ShouldNotContain("geom", Case.Insensitive);
        payload.ShouldNotContain("coordinates", Case.Insensitive);
        payload.ShouldNotContain("latitude", Case.Insensitive);
        payload.ShouldNotContain("longitude", Case.Insensitive);

        // Nor does reading the document put the photo on the map. A position is emitted only
        // through something that has a place — a cave, a trip — and this photo hangs off
        // nothing, so it has no place to be shown at, for this caller or its uploader.
        const string bbox = "25.5,45.5,26.1,46.1";
        (await PhotoIdsAsync(reader, bbox)).ShouldNotContain(fileId);
        (await PhotoIdsAsync(owner, bbox)).ShouldNotContain(fileId);
    }

    // ---- helpers ----

    /// <summary>
    /// A rule naming one person directly. Written straight into storage: the authoring
    /// surface refuses rules that hand out more than the author holds, which is exactly
    /// what a fixture needs to do.
    /// </summary>
    private async Task GrantAsync(
        Guid userId, AccessEffect effect, AccessAction actions, AccessScopeKind scopeKind, Guid? scopeId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.Documents,
            Actions = actions,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> UploadDocumentAsync(string fileName, byte[] bytes) =>
        await DocumentIdOfAsync(await UploadFileAsync(fileName, bytes, "text/plain"));

    private async Task<Guid> UploadFileAsync(string fileName, byte[] bytes, string contentType)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await owner.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    /// <summary>File ids present in the photo map for a bbox.</summary>
    private static async Task<Guid[]> PhotoIdsAsync(HttpClient client, string bbox)
    {
        var collection = await client.GetFromJsonAsync<JsonElement>($"/api/v1/map/photos?bbox={bbox}");
        return [.. collection.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())];
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] MakeGeotaggedJpeg(double lat, double lon)
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        editor?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
