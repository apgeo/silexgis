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
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Files + attachments end-to-end: multipart upload, token-authenticated content
/// streaming with Range support, Magick.NET thumbnails, and access rules that flow
/// through the attached entity's visibility.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FileAttachmentTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private HttpClient viewer = null!;   // Viewer role
    private long caveTypeId;

    public FileAttachmentTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fa-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fa-out-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fa-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fa-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"fa-out-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"fa-view-{suffix}@t.local");
    }

    [Fact]
    public async Task Upload_attach_stream_and_thumbnail_work_end_to_end()
    {
        // A real PNG so the thumbnail pipeline has something to chew on.
        var png = MakePng(640, 480);
        var file = await UploadAsync(owner, "entrance-photo.png", png, "image/png");
        file.GetProperty("kind").GetString().ShouldBe("image");
        var fileId = file.GetProperty("id").GetGuid();
        var contentUrl = file.GetProperty("contentUrl").GetString()!;
        var thumbnailUrl = file.GetProperty("thumbnailUrl").GetString()!;

        var caveId = await CreateCaveAsync(owner, "Attachment Cave", "authenticated");
        var attach = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "cave",
            entityId = caveId,
            role = "photoEntrance",
            caption = "Main entrance in winter",
            sortOrder = 1,
        });
        attach.StatusCode.ShouldBe(HttpStatusCode.Created, await attach.Content.ReadAsStringAsync());

        // Another authenticated user reads the gallery through the cave's visibility.
        var listed = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=cave&entityId={caveId}");
        var items = listed.EnumerateArray().ToList();
        items.Count.ShouldBe(1);
        items[0].GetProperty("caption").GetString().ShouldBe("Main entrance in winter");
        items[0].GetProperty("file").GetProperty("contentUrl").GetString().ShouldNotBeNullOrEmpty();

        // Content streams anonymously with the token (browsers can't send bearer here).
        using var anonymous = factory.CreateClient();
        var content = await anonymous.GetAsync(contentUrl);
        content.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).ShouldBe(png);

        // Range requests are honored (COG/geotiff.js relies on this).
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, contentUrl);
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
        var partial = await anonymous.SendAsync(rangeRequest);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsByteArrayAsync()).Length.ShouldBe(10);

        // Thumbnail: WebP, capped to the bounding box, cached on disk for the rerun.
        var thumb = await anonymous.GetAsync(thumbnailUrl);
        thumb.StatusCode.ShouldBe(HttpStatusCode.OK, await thumb.Content.ReadAsStringAsync());
        thumb.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        using (var thumbImage = new MagickImage(await thumb.Content.ReadAsByteArrayAsync()))
        {
            ((int)thumbImage.Width).ShouldBeLessThanOrEqualTo(480);
        }

        (await anonymous.GetAsync(thumbnailUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tampered token → not found, no existence disclosure.
        var tampered = contentUrl[..^4] + "AAAA";
        (await anonymous.GetAsync(tampered)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/files/{fileId}/content?token=garbage"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task File_access_flows_through_attached_entity_visibility()
    {
        var file = await UploadAsync(owner, "site-sketch.png", MakePng(32, 32), "image/png");
        var fileId = file.GetProperty("id").GetGuid();

        // Unattached: only the uploader (and admins) see metadata.
        (await owner.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Attached to a private cave: still invisible to others…
        var privateCave = await CreateCaveAsync(owner, "Private Gallery Cave", "private");
        (await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "cave",
            entityId = privateCave,
            role = "document",
            sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/attachments/?entityType=cave&entityId={privateCave}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // …until it is also attached to something the caller can read.
        var openCave = await CreateCaveAsync(owner, "Open Gallery Cave", "authenticated");
        (await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "cave",
            entityId = openCave,
            role = "document",
            sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Role gates: viewers cannot upload; editors cannot attach to caves they cannot write.
        using (var form = BuildForm("x.png", MakePng(8, 8), "image/png"))
        {
            (await viewer.PostAsync("/api/v1/files/", form)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await outsider.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "cave",
            entityId = openCave, // readable but not writable for the outsider
            role = "document",
            sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_the_entity_removes_its_attachment_rows()
    {
        long featureTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            featureTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
        }

        var file = await UploadAsync(owner, "feature-note.txt", "field notes"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        file.GetProperty("kind").GetString().ShouldBe("document");
        file.GetProperty("thumbnailUrl").ValueKind.ShouldBe(JsonValueKind.Null); // not an image

        var featureResponse = await owner.PostAsJsonAsync("/api/v1/surface-features", new
        {
            name = "Attachment Feature",
            featureTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.81, 45.81 } },
            visibility = "private",
        });
        featureResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var featureId = (await featureResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "surfaceFeature",
            entityId = featureId,
            role = "document",
            sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await owner.DeleteAsync($"/api/v1/surface-features/{featureId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await verifyDb.Attachments.CountAsync(a => a.EntityId == featureId)).ShouldBe(0);
        // The file itself survives (immutable; orphan cleanup is a later maintenance job).
        (await verifyDb.StoredFiles.CountAsync(f => f.Id == fileId)).ShouldBe(1);
    }

    [Fact]
    public async Task File_version_chain_upload_repoint_list_and_delete()
    {
        var v1 = await UploadAsync(owner, "report.txt", "draft"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();
        v1.GetProperty("versionNumber").GetInt32().ShouldBe(1);

        var caveId = await CreateCaveAsync(owner, "Versioned Doc Cave", "authenticated");
        (await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId = v1Id, entityType = "cave", entityId = caveId, role = "document", sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Upload v2 onto the head → the attachment repoints to v2, version number increments.
        using (var form = BuildForm("report.txt", "corrected"u8.ToArray(), "text/plain"))
        {
            var response = await owner.PostAsync($"/api/v1/files/{v1Id}/versions", form);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
            JsonDocument.Parse(body).RootElement.GetProperty("versionNumber").GetInt32().ShouldBe(2);
        }

        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=cave&entityId={caveId}");
        var headId = listed.EnumerateArray().Single().GetProperty("file").GetProperty("id").GetGuid();
        headId.ShouldNotBe(v1Id); // repointed to the new head
        listed.EnumerateArray().Single().GetProperty("file").GetProperty("versionNumber").GetInt32().ShouldBe(2);

        // Uploading onto the now-superseded v1 fails as not-head.
        using (var form = BuildForm("report.txt", "late"u8.ToArray(), "text/plain"))
        {
            var response = await owner.PostAsync($"/api/v1/files/{v1Id}/versions", form);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ReadCodeAsync(response)).ShouldBe("file.not_head");
        }

        // Version list: the editor sees both with the head flagged; a read-only user is forbidden.
        var versions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/files/{headId}/versions");
        versions.GetArrayLength().ShouldBe(2);
        versions[0].GetProperty("versionNumber").GetInt32().ShouldBe(2);
        versions[0].GetProperty("isHead").GetBoolean().ShouldBeTrue();
        versions[0].GetProperty("uploaderName").GetString().ShouldNotBeNullOrWhiteSpace();
        versions[1].GetProperty("versionNumber").GetInt32().ShouldBe(1);
        versions[1].GetProperty("isHead").GetBoolean().ShouldBeFalse();

        (await outsider.GetAsync($"/api/v1/files/{headId}/versions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Delete the superseded v1 → 204, and its content is purged. The head cannot be deleted.
        (await owner.DeleteAsync($"/api/v1/files/{v1Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync(v1.GetProperty("contentUrl").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var deleteHead = await owner.DeleteAsync($"/api/v1/files/{headId}");
        deleteHead.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(deleteHead)).ShouldBe("file.head_undeletable");

        // The repoint is audited: the cave timeline carries an Attachment update (FileId diff).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveIdStr = caveId.ToString();
        (await db.Set<AuditEntry>().CountAsync(a =>
            a.EntityType == "Attachment" && a.RootEntityId == caveIdStr && a.Action == AuditActions.Updated))
            .ShouldBeGreaterThan(0);
    }

    // ---- helpers ----

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.DarkSlateBlue, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private async Task<JsonElement> UploadAsync(HttpClient client, string fileName, byte[] bytes, string contentType)
    {
        using var form = BuildForm(fileName, bytes, contentType);
        var response = await client.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }
}
