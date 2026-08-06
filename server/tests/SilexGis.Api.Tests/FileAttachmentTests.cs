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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Files + attachments end-to-end: multipart upload, token-authenticated content
/// streaming with Range support, Magick.NET thumbnails, and access rules that flow
/// through the attached target's visibility.
/// <para>
/// Targets speak a two-world vocabulary: "feature" plus a feature id addresses any
/// physical feature whatever its kind (caves, entrances, centerlines, generic kinds),
/// while non-feature entities keep their own names ("tripLog", "geofile",
/// "georeferencedMap", "mapView", "cavingGroup", and "storedFile" for taggings only).
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FileAttachmentTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private HttpClient viewer = null!;   // Viewer role, for the upload gate
    private HttpClient admin = null!;    // Admin role
    private Guid ownerId;
    private long caveTypeId;
    private long entranceTypeId;
    private long genericTypeId;

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
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fa-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fa-out-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fa-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"fa-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            genericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fa-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"fa-out-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"fa-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"fa-adm-{suffix}@t.local");
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
        var attach = await AttachAsync(
            owner, fileId, "feature", caveId, role: "photoEntrance", caption: "Main entrance in winter", sortOrder: 1);
        attach.StatusCode.ShouldBe(HttpStatusCode.Created, await attach.Content.ReadAsStringAsync());

        // The row echoes the target back in the same vocabulary it was addressed with.
        var createdDto = await attach.Content.ReadFromJsonAsync<JsonElement>();
        createdDto.GetProperty("entityType").GetString().ShouldBe("feature");
        createdDto.GetProperty("entityId").GetGuid().ShouldBe(caveId);

        // Another authenticated user reads the gallery through the cave's visibility.
        var listed = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=feature&entityId={caveId}");
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
    public async Task Target_vocabulary_covers_both_worlds_and_rejects_everything_else()
    {
        var file = await UploadAsync(owner, "vocab.txt", "notes"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        var caveId = await CreateCaveAsync(owner, "Vocabulary Cave", "authenticated");

        // One name covers every feature kind, cave and entrance alike.
        var entranceId = await CreateEntranceAsync(caveId, 25.44, 45.44);
        foreach (var featureId in new[] { caveId, entranceId })
        {
            (await AttachAsync(owner, fileId, "feature", featureId)).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=feature&entityId={featureId}"))
                .EnumerateArray().Single().GetProperty("entityId").GetGuid().ShouldBe(featureId);
        }

        // Retired kind-specific names are gone: every feature is addressed as "feature".
        foreach (var retired in new[] { "cave", "caveEntrance", "surfaceFeature" })
        {
            var listing = await owner.GetAsync($"/api/v1/attachments/?entityType={retired}&entityId={caveId}");
            listing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(listing)).ShouldBe("attachment.entity_type_unknown");

            var tagListing = await owner.GetAsync($"/api/v1/taggings/?entityType={retired}&entityId={caveId}");
            tagListing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(tagListing)).ShouldBe("tagging.entity_type_unknown");

            // The write path rejects them in validation, before any target lookup.
            var write = await AttachAsync(owner, fileId, retired, caveId);
            write.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(write)).ShouldBe("validation.failed");
        }

        // Entity types the shared enum defines for resource links (documents, survey
        // models, cavers, cabinets, and the reserved comment kind) are not attachment or
        // tagging vocabulary: they must read exactly like unknown names on reads and
        // writes alike, so appending to the enum never widens what these tables accept.
        foreach (var reserved in new[] { "document", "surveyModel", "caver", "cabinet", "comment" })
        {
            var listing = await owner.GetAsync($"/api/v1/attachments/?entityType={reserved}&entityId={caveId}");
            listing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(listing)).ShouldBe("attachment.entity_type_unknown");

            var tagListing = await owner.GetAsync($"/api/v1/taggings/?entityType={reserved}&entityId={caveId}");
            tagListing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(tagListing)).ShouldBe("tagging.entity_type_unknown");

            var write = await AttachAsync(owner, fileId, reserved, caveId);
            write.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(write)).ShouldBe("validation.failed");

            var tagWrite = await owner.PostAsJsonAsync("/api/v1/taggings/", new
            {
                tagName = $"rsv-{Guid.NewGuid():N}"[..16], entityType = reserved, entityId = caveId,
            });
            tagWrite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(tagWrite)).ShouldBe("validation.failed");
        }

        // A file is a tag target but never an attachment target — allowing it would let the
        // polymorphic access resolver recurse file → attachment → file without bound.
        var fileTarget = await AttachAsync(owner, fileId, "storedFile", fileId);
        fileTarget.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(fileTarget)).ShouldBe("validation.failed");

        (await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName = $"vocab-{Guid.NewGuid():N}"[..16], entityType = "storedFile", entityId = fileId,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);
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
        (await AttachAsync(owner, fileId, "feature", privateCave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={privateCave}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // …until it is also attached to something the caller can read.
        var openCave = await CreateCaveAsync(owner, "Open Gallery Cave", "authenticated");
        (await AttachAsync(owner, fileId, "feature", openCave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Role gates: viewers cannot upload; editors cannot attach to features they cannot write.
        using (var form = BuildForm("x.png", MakePng(8, 8), "image/png"))
        {
            (await viewer.PostAsync("/api/v1/files/", form)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Readable but not writable for the outsider.
        (await AttachAsync(outsider, fileId, "feature", openCave)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CavingGroup_attachments_open_to_members_and_close_to_everyone_else()
    {
        // The club's documents are not its directory entry: every account can browse the
        // caving-groups list, but a file attached to the group is for its members — or
        // for someone a rule names on this very group. Writing takes Write on the group
        // record; membership alone manages nothing.
        var created = await owner.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Attach Club {Guid.NewGuid():N}"[..30],
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The creator (whose per-group manager seed carries Write on this group) enrolls
        // the member and attaches the club document.
        (await owner.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
        {
            caverId = await RosterHelper.CaverIdForAsync(
                factory,
                await FindUserIdAsync(viewer)),
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var file = await UploadAsync(owner, "club-statute.txt", "statute"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        (await AttachAsync(owner, fileId, "cavingGroup", groupId, role: "document"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // A member reads the gallery and the file's metadata through the group target.
        (await viewer.GetAsync($"/api/v1/attachments/?entityType=cavingGroup&entityId={groupId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A non-member sees neither — the group being browsable in the directory does
        // not make its documents readable.
        (await outsider.GetAsync($"/api/v1/attachments/?entityType=cavingGroup&entityId={groupId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Membership reads; it does not manage: the member may not attach.
        (await AttachAsync(viewer, fileId, "cavingGroup", groupId))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>The authenticated user id behind a bearer client, via /me.</summary>
    private static async Task<Guid> FindUserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return me.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Soft_deleting_a_feature_hides_its_attachments_from_every_read_path()
    {
        var file = await UploadAsync(owner, "feature-note.txt", "field notes"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        file.GetProperty("kind").GetString().ShouldBe("document");
        file.GetProperty("thumbnailUrl").ValueKind.ShouldBe(JsonValueKind.Null); // not an image

        var featureId = await CreateGenericFeatureAsync(owner, "Attachment Feature", "authenticated");
        (await AttachAsync(owner, fileId, "feature", featureId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Before the delete the attachment is the outsider's path to the file.
        (await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=feature&entityId={featureId}"))
            .GetArrayLength().ShouldBe(1);
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/features/{featureId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The delete is a soft one, so the rows survive — but the target is gone from every
        // read path, and with it the readability the attachment conferred on the file.
        (await owner.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={featureId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/attachments/?entityType=feature&entityId={featureId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK); // uploader

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await verifyDb.Attachments.CountAsync(a => a.FeatureId == featureId)).ShouldBe(1);
        // The file itself survives (immutable; orphan cleanup is a later maintenance job).
        (await verifyDb.StoredFiles.CountAsync(f => f.Id == fileId)).ShouldBe(1);

        // Deletions are forensics: the timeline keeps a kind-qualified row for the feature.
        var key = featureId.ToString();
        (await verifyDb.AuditEntries.CountAsync(a =>
            a.EntityType == "Feature:Generic" && a.EntityId == key && a.Action == AuditActions.Deleted))
            .ShouldBe(1);
    }

    [Fact]
    public async Task File_version_chain_repoints_attachments_of_both_target_shapes()
    {
        var v1 = await UploadAsync(owner, "report.txt", "draft"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();
        v1.GetProperty("versionNumber").GetInt32().ShouldBe(1);

        // One document, two target worlds: a feature FK row and a polymorphic-pair row.
        var caveId = await CreateCaveAsync(owner, "Versioned Doc Cave", "authenticated");
        var tripId = await CreateTripAsync("Versioned Doc Trip");
        (await AttachAsync(owner, v1Id, "feature", caveId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AttachAsync(owner, v1Id, "tripLog", tripId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Upload v2 onto the head → both attachments repoint to v2, version number increments.
        using (var form = BuildForm("report.txt", "corrected"u8.ToArray(), "text/plain"))
        {
            var response = await owner.PostAsync($"/api/v1/files/{v1Id}/versions", form);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
            JsonDocument.Parse(body).RootElement.GetProperty("versionNumber").GetInt32().ShouldBe(2);
        }

        var caveAttachment = (await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=feature&entityId={caveId}")).EnumerateArray().Single();
        var headId = caveAttachment.GetProperty("file").GetProperty("id").GetGuid();
        headId.ShouldNotBe(v1Id); // repointed to the new head
        caveAttachment.GetProperty("file").GetProperty("versionNumber").GetInt32().ShouldBe(2);

        var tripAttachment = (await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}")).EnumerateArray().Single();
        tripAttachment.GetProperty("file").GetProperty("id").GetGuid().ShouldBe(headId);

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

        // The repoint is audited under each target's own root pointer: the feature world
        // points at the cave feature, the polymorphic world at the trip log.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveIdStr = caveId.ToString();
        var tripIdStr = tripId.ToString();
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == "Attachment" && a.RootEntityType == "Feature"
            && a.RootEntityId == caveIdStr && a.Action == AuditActions.Updated))
            .ShouldBeGreaterThan(0);
        (await db.AuditEntries.CountAsync(a =>
            a.EntityType == "Attachment" && a.RootEntityType == "TripLog"
            && a.RootEntityId == tripIdStr && a.Action == AuditActions.Updated))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task File_tags_follow_the_head_across_versions_and_respect_the_write_rule()
    {
        var v1 = await UploadAsync(owner, "hydro.txt", "notes"u8.ToArray(), "text/plain");
        var v1Id = v1.GetProperty("id").GetGuid();

        // Attach to an authenticated cave: outsiders/viewers can Read it, only the owner can Write.
        var caveId = await CreateCaveAsync(owner, "Tagged File Cave", "authenticated");
        (await AttachAsync(owner, v1Id, "feature", caveId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The uploader (an editor with Write on the cave) can tag the file.
        var tagName = $"hydrology-{Guid.NewGuid():N}"[..20];
        var tagResponse = await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName, entityType = "storedFile", entityId = v1Id,
        });
        tagResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await tagResponse.Content.ReadAsStringAsync());

        // A reader without write on any attached entity cannot tag it (403, not 404 — the file is visible).
        (await outsider.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName = $"spelunking-{Guid.NewGuid():N}"[..20], entityType = "storedFile", entityId = v1Id,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Upload v2 → the tag moves to the new head (tags belong to the document, not the version).
        Guid v2Id;
        using (var form = BuildForm("hydro.txt", "revised"u8.ToArray(), "text/plain"))
        {
            var response = await owner.PostAsync($"/api/v1/files/{v1Id}/versions", form);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            v2Id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        // Listing tags on the new head shows the tag; the old version carries none.
        var headTags = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/taggings/?entityType=storedFile&entityId={v2Id}");
        headTags.EnumerateArray().Select(t => t.GetProperty("tag").GetProperty("name").GetString()).ShouldContain(tagName);
        headTags.EnumerateArray().Single().GetProperty("entityType").GetString().ShouldBe("storedFile");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/taggings/?entityType=storedFile&entityId={v1Id}"))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Document_date_round_trips_validates_and_carries_across_versions()
    {
        var file = await UploadAsync(owner, "survey.txt", "field"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        file.GetProperty("documentDate").ValueKind.ShouldBe(JsonValueKind.Null); // unset on upload

        // A future date is rejected.
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3).ToString("yyyy-MM-dd");
        (await owner.PutAsJsonAsync($"/api/v1/files/{fileId}", new { documentDate = future }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A valid past date round-trips.
        var setResponse = await owner.PutAsJsonAsync($"/api/v1/files/{fileId}", new { documentDate = "2019-08-01" });
        setResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());
        (await setResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentDate").GetString().ShouldBe("2019-08-01");

        // Attach to an authenticated cave: a reader-without-write cannot set the date (404, existence hidden).
        var caveId = await CreateCaveAsync(owner, "Dated File Cave", "authenticated");
        (await AttachAsync(owner, fileId, "feature", caveId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await outsider.PutAsJsonAsync($"/api/v1/files/{fileId}", new { documentDate = "2018-01-01" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The date carries to a new version.
        using var form = BuildForm("survey.txt", "field v2"u8.ToArray(), "text/plain");
        var versionResponse = await owner.PostAsync($"/api/v1/files/{fileId}/versions", form);
        versionResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await versionResponse.Content.ReadAsStringAsync());
        (await versionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentDate").GetString().ShouldBe("2019-08-01");
    }

    [Fact]
    public async Task Attachment_metadata_edit_round_trips_and_forbids_non_writers()
    {
        var file = await UploadAsync(owner, "map.txt", "sketch"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();

        var caveId = await CreateCaveAsync(owner, "Editable Attachment Cave", "authenticated");
        var created = await AttachAsync(owner, fileId, "feature", caveId, caption: "draft");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var attachmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The owner edits role/caption/order.
        var updated = await owner.PutAsJsonAsync($"/api/v1/attachments/{attachmentId}", new
        {
            role = "other", caption = "final caption", sortOrder = 7,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("caption").GetString().ShouldBe("final caption");
        body.GetProperty("role").GetString().ShouldBe("other");
        body.GetProperty("sortOrder").GetInt32().ShouldBe(7);
        // The target is fixed at creation and survives a metadata edit unchanged.
        body.GetProperty("entityType").GetString().ShouldBe("feature");
        body.GetProperty("entityId").GetGuid().ShouldBe(caveId);

        // The change persists in the listing.
        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=feature&entityId={caveId}");
        listed.EnumerateArray().Single().GetProperty("caption").GetString().ShouldBe("final caption");

        // A reader without Write on the cave cannot edit the attachment.
        (await outsider.PutAsJsonAsync($"/api/v1/attachments/{attachmentId}", new
        {
            role = "document", caption = "tampered", sortOrder = 0,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Attachments_tags_and_history_reach_the_raster_and_saved_view_worlds()
    {
        // Rasters and saved views are non-feature targets that the polymorphic resolver
        // used to answer "no" for unconditionally — attaching, tagging and reading their
        // timeline all failed. They are first-class targets now.
        var file = await UploadAsync(owner, "legend.txt", "sheet legend"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();

        var targets = new[]
        {
            ("georeferencedMap", await SeedGeoreferencedMapAsync(fileId, Visibility.Authenticated), "GeoreferencedMap"),
            ("mapView", await CreateMapViewAsync("Karst overview", "authenticated"), "MapView"),
        };

        foreach (var (typeName, entityId, auditName) in targets)
        {
            var attach = await AttachAsync(owner, fileId, typeName, entityId, caption: "legend sheet");
            attach.StatusCode.ShouldBe(HttpStatusCode.Created, await attach.Content.ReadAsStringAsync());

            var listed = await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/attachments/?entityType={typeName}&entityId={entityId}");
            var single = listed.EnumerateArray().Single();
            single.GetProperty("entityType").GetString().ShouldBe(typeName);
            single.GetProperty("entityId").GetGuid().ShouldBe(entityId);

            var tagName = $"overlay-{Guid.NewGuid():N}"[..18];
            var tagging = await owner.PostAsJsonAsync("/api/v1/taggings/", new { tagName, entityType = typeName, entityId });
            tagging.StatusCode.ShouldBe(HttpStatusCode.Created, await tagging.Content.ReadAsStringAsync());
            (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/taggings/?entityType={typeName}&entityId={entityId}"))
                .EnumerateArray().Select(t => t.GetProperty("tag").GetProperty("name").GetString())
                .ShouldContain(tagName);

            // The timeline resolves through the same vocabulary and carries both children.
            var history = await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/history?entityType={typeName}&entityId={entityId}");
            var eventTypes = history.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("entityType").GetString()).ToList();
            eventTypes.ShouldContain("Attachment");
            eventTypes.ShouldContain("Tagging");
            eventTypes.ShouldContain(auditName);

            // The file is reachable through the raster/view alone for a reader of it.
            (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Readable, not writable: attaching and tagging are Write operations.
            (await AttachAsync(outsider, fileId, typeName, entityId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await outsider.PostAsJsonAsync("/api/v1/taggings/", new
            {
                tagName = $"foreign-{Guid.NewGuid():N}"[..18], entityType = typeName, entityId,
            })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // A private target of either world discloses nothing — not its attachments, not its
        // timeline; unreadable and non-existent are the same answer.
        var privateRaster = await SeedGeoreferencedMapAsync(fileId, Visibility.Private);
        var privateView = await CreateMapViewAsync("Private overview", "private");
        foreach (var (typeName, entityId) in new[] { ("georeferencedMap", privateRaster), ("mapView", privateView) })
        {
            var listing = await outsider.GetAsync($"/api/v1/attachments/?entityType={typeName}&entityId={entityId}");
            listing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadCodeAsync(listing)).ShouldBe("attachment.entity_not_found");

            var history = await outsider.GetAsync($"/api/v1/history?entityType={typeName}&entityId={entityId}");
            history.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadCodeAsync(history)).ShouldBe("history.entity_not_found");
        }
    }

    [Fact]
    public async Task Attachments_show_in_the_features_own_timeline()
    {
        var file = await UploadAsync(owner, "timeline.txt", "notes"u8.ToArray(), "text/plain");
        var fileId = file.GetProperty("id").GetGuid();
        var caveId = await CreateCaveAsync(owner, "Timeline Cave", "authenticated");
        (await AttachAsync(owner, fileId, "feature", caveId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // One uniform address for every feature kind; the kind lives in the event rows.
        var history = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/history?entityType=feature&entityId={caveId}");
        var events = history.GetProperty("items").EnumerateArray()
            .Select(x => (Type: x.GetProperty("entityType").GetString(), Action: x.GetProperty("action").GetString()))
            .ToList();
        events.ShouldContain(e => e.Type == "Attachment" && e.Action == AuditActions.Created);
        events.ShouldContain(e => e.Type == "Feature:Cave" && e.Action == AuditActions.Created);
    }

    [Fact]
    public async Task Geotagged_photo_shows_on_the_photo_map_when_its_entity_is_readable()
    {
        // A JPEG with GPS EXIF; the point is inside the query bbox below.
        var geo = await UploadAsync(owner, "geo.jpg", MakeGeotaggedJpeg(45.8, 25.8), "image/jpeg");
        var geoId = geo.GetProperty("id").GetGuid();
        var plain = await UploadAsync(owner, "plain.png", MakePng(16, 16), "image/png"); // no GPS
        var plainId = plain.GetProperty("id").GetGuid();

        var caveId = await CreateCaveAsync(owner, "Geo Photo Cave", "authenticated");
        foreach (var id in new[] { geoId, plainId })
        {
            (await AttachAsync(owner, id, "feature", caveId, role: "photoEntrance"))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        const string bbox = "25.5,45.5,26.1,46.1";
        // The geotagged photo is on the map for the uploader and for any reader of the cave;
        // the photo without GPS never is.
        (await PhotoIdsAsync(owner, bbox)).ShouldContain(geoId);
        (await PhotoIdsAsync(owner, bbox)).ShouldNotContain(plainId);
        (await PhotoIdsAsync(outsider, bbox)).ShouldContain(geoId);
        (await PhotoIdsAsync(outsider, bbox)).ShouldNotContain(plainId);
    }

    [Fact]
    public async Task Photo_map_withholds_photos_of_protected_caves_from_callers_without_exact_location()
    {
        var geo = await UploadAsync(owner, "protected.jpg", MakeGeotaggedJpeg(45.8, 25.8), "image/jpeg");
        var geoId = geo.GetProperty("id").GetGuid();

        // Attached to a protected cave the outsider can read but not view exactly.
        var protectedCave = await CreateCaveAsync(owner, "Protected Photo Cave", "authenticated", locationProtected: true);
        (await AttachAsync(owner, geoId, "feature", protectedCave, role: "photoEntrance"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        const string bbox = "25.5,45.5,26.1,46.1";
        // The owner has exact-location on their own cave; the outsider does not — the EXIF point
        // is the protected location, so it is withheld entirely (not snapped).
        (await PhotoIdsAsync(owner, bbox)).ShouldContain(geoId);
        (await PhotoIdsAsync(outsider, bbox)).ShouldNotContain(geoId);

        // A photo attached only to a private cave is invisible to the outsider (plain visibility).
        var privateGeo = await UploadAsync(owner, "private.jpg", MakeGeotaggedJpeg(45.81, 25.81), "image/jpeg");
        var privateGeoId = privateGeo.GetProperty("id").GetGuid();
        var privateCave = await CreateCaveAsync(owner, "Private Photo Cave", "private");
        (await AttachAsync(owner, privateGeoId, "feature", privateCave, role: "photoEntrance"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PhotoIdsAsync(owner, bbox)).ShouldContain(privateGeoId);
        (await PhotoIdsAsync(outsider, bbox)).ShouldNotContain(privateGeoId);
    }

    [Fact]
    public async Task Photo_geo_backfill_repopulates_missing_points_and_requires_admin_to_enqueue()
    {
        var geo = await UploadAsync(owner, "backfill.jpg", MakeGeotaggedJpeg(45.8, 25.8), "image/jpeg");
        var geoId = geo.GetProperty("id").GetGuid();

        // Simulate a file that predates geotag-at-upload: clear its point.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.FirstAsync(f => f.Id == geoId);
            file.Geom = null;
            await db.SaveChangesAsync();
        }

        // Enqueue is admin-only.
        (await viewer.PostAsync("/api/v1/jobs/photo-geo-backfill", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var enqueue = await admin.PostAsync("/api/v1/jobs/photo-geo-backfill", null);
        enqueue.StatusCode.ShouldBe(HttpStatusCode.OK, await enqueue.Content.ReadAsStringAsync());
        (await enqueue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString().ShouldBe("photo-geo-backfill");

        // Run the handler directly (the worker executes the same code) → the point is recovered.
        using (var scope = factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .First(h => h.Kind == ProcessingJobKinds.PhotoGeoBackfill);
            await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.PhotoGeoBackfill }, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == geoId)).Geom.ShouldNotBeNull();
        }
    }

    // ---- helpers ----

    /// <summary>Attaches a file to a target named in the two-world vocabulary.</summary>
    private static Task<HttpResponseMessage> AttachAsync(
        HttpClient client,
        Guid fileId,
        string entityType,
        Guid entityId,
        string role = "document",
        string? caption = null,
        int sortOrder = 0) =>
        client.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId,
            role,
            caption,
            sortOrder,
        });

    /// <summary>File ids present in the /map/photos response for a bbox.</summary>
    private static async Task<Guid[]> PhotoIdsAsync(HttpClient client, string bbox)
    {
        var collection = await client.GetFromJsonAsync<JsonElement>($"/api/v1/map/photos?bbox={bbox}");
        return [.. collection.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())];
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

    private async Task<Guid> CreateCaveAsync(HttpClient client, string name, string visibility, bool locationProtected = false)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Adds an entrance to a cave and returns its own feature id.</summary>
    private async Task<Guid> CreateEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A data-driven (generic) feature — the former surface-feature world.</summary>
    private async Task<Guid> CreateGenericFeatureAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {Guid.NewGuid():N}"[..40],
            featureTypeId = genericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.81, 45.81 } },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-06-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateMapViewAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/map-views", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            config = new { center = new[] { 25.6, 45.5 }, zoom = 10 },
            isHome = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A raster catalog row seeded directly: the upload endpoint wants a real GeoTIFF and
    /// queues COG normalization, neither of which this test is about. Nothing here is
    /// derived state, so no write service is involved.
    /// </summary>
    private async Task<Guid> SeedGeoreferencedMapAsync(Guid fileId, Visibility visibility)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var map = new GeoreferencedMap
        {
            Name = $"Raster {Guid.NewGuid():N}"[..24],
            FileId = fileId,
            OwnerUserId = ownerId,
            Visibility = visibility,
        };
        db.GeoreferencedMaps.Add(map);
        await db.SaveChangesAsync();
        return map.Id;
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
