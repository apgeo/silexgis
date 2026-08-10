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
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The gallery: every photograph in one place, and every rule the per-object pages apply,
/// applied to a listing that hands out a hundred at a time.
///
/// <para>
/// A gallery is a bulk path to material those pages release one at a time, so the assertions
/// that matter here are the ones about what it does <em>not</em> show: somebody else's private
/// picture, a capture position the map itself would withhold, and the stored bytes of a
/// photograph whose subject the caller may not place.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoGalleryTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads and edits
    private HttpClient member = null!;  // an ordinary member: adds photographs, edits their own
    private Guid memberId;

    public PhotoGalleryTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-gallery-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gal-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"gal-own-{suffix}@t.local");

        // Not a second Editor: an Editor reads every document in the installation, so "cannot
        // see somebody else's picture" could never be true of one.
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"gal-mem-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"gal-mem-{suffix}@t.local");
        await GrantAsync(memberId, AccessAction.Create, AccessScopeKind.All);
        await GrantAsync(memberId, AccessAction.Read | AccessAction.Write, AccessScopeKind.Own);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        member?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task The_gallery_shows_the_callers_photographs_newest_first()
    {
        var first = await UploadPhotoAsync(owner, "one.png");
        var second = await UploadPhotoAsync(owner, "two.png");

        var page = await GalleryAsync(owner);

        var ids = page.Select(p => p.GetProperty("documentId").GetGuid()).ToList();
        ids.ShouldContain(first.DocumentId);
        ids.ShouldContain(second.DocumentId);
        ids.IndexOf(second.DocumentId).ShouldBeLessThan(ids.IndexOf(first.DocumentId));
    }

    [Fact]
    public async Task It_lists_photographs_and_not_the_rest_of_the_archive()
    {
        var photo = await UploadPhotoAsync(owner, "picture.png");
        var report = await UploadAsync(owner, "report.txt", "some notes"u8.ToArray(), "text/plain");

        var ids = (await GalleryAsync(owner)).Select(p => p.GetProperty("documentId").GetGuid()).ToList();

        ids.ShouldContain(photo.DocumentId);
        ids.ShouldNotContain(await DocumentIdOfAsync(report));
    }

    [Fact]
    public async Task Somebody_elses_private_photograph_is_not_in_it()
    {
        var mine = await UploadPhotoAsync(member, "mine.png");
        var theirs = await UploadPhotoAsync(owner, "theirs.png");

        var ids = (await GalleryAsync(member)).Select(p => p.GetProperty("documentId").GetGuid()).ToList();

        ids.ShouldContain(mine.DocumentId);
        // The gallery is the ordinary document read rule narrowed to pictures — not a way past it.
        ids.ShouldNotContain(theirs.DocumentId);
    }

    [Fact]
    public async Task A_grid_tile_is_a_rendering_and_never_the_upload()
    {
        var photo = await UploadPhotoAsync(owner, "picture.png");

        var tile = (await GalleryAsync(owner))
            .Single(p => p.GetProperty("documentId").GetGuid() == photo.DocumentId);

        // Renderings are drawn here and stripped of every metadata profile, which is what makes
        // them safe to hand out far more freely than the file they came from.
        tile.GetProperty("thumbnailUrl").GetString()!.ShouldContain("/thumbnail?");
        tile.GetProperty("previewUrl").GetString()!.ShouldContain("/thumbnail?");
        tile.GetProperty("contentUrl").GetString()!.ShouldContain("/content?");
    }

    [Fact]
    public async Task A_photograph_is_credited_to_a_caver_and_carries_a_licence()
    {
        var photo = await UploadPhotoAsync(owner, "credited.png");
        var caverId = await CreateCaverAsync("Ana Ionescu");

        var saved = await owner.PutAsJsonAsync($"/api/v1/photos/{photo.DocumentId}/credit", new
        {
            photographerCaverId = caverId,
            caption = "The main entrance in winter",
            licenceCode = PhotoLicences.CcBySa,
            placeName = "Peștera Demo Mare",
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var credit = (await ReadJsonAsync(saved)).GetProperty("credit");
        credit.GetProperty("photographerCaverId").GetGuid().ShouldBe(caverId);
        // The caver's own name rather than whatever anybody typed, so one person is not shown
        // under two names on the same page.
        credit.GetProperty("photographerName").GetString().ShouldBe("Ana Ionescu");
        credit.GetProperty("licenceCode").GetString().ShouldBe(PhotoLicences.CcBySa);
        credit.GetProperty("placeName").GetString().ShouldBe("Peștera Demo Mare");
    }

    [Fact]
    public async Task A_licence_has_to_be_one_of_the_known_ones()
    {
        var photo = await UploadPhotoAsync(owner, "licence.png");

        var refused = await owner.PutAsJsonAsync($"/api/v1/photos/{photo.DocumentId}/credit", new
        {
            licenceCode = "ask Ana",
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Absent is valid and is not the same answer as "all rights reserved": most of an
        // archive is in the state where nobody has decided.
        (await owner.PutAsJsonAsync($"/api/v1/photos/{photo.DocumentId}/credit", new
        {
            licenceCode = (string?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_gallery_can_be_narrowed_to_one_photographer()
    {
        var caverId = await CreateCaverAsync($"Ana {Guid.NewGuid():N}"[..12]);
        var credited = await UploadPhotoAsync(owner, "hers.png");
        var other = await UploadPhotoAsync(owner, "somebody-elses.png");

        await owner.PutAsJsonAsync($"/api/v1/photos/{credited.DocumentId}/credit", new
        {
            photographerCaverId = caverId,
        });

        var ids = (await GalleryAsync(owner, $"caverId={caverId}"))
            .Select(p => p.GetProperty("documentId").GetGuid()).ToList();

        ids.ShouldBe([credited.DocumentId]);
        ids.ShouldNotContain(other.DocumentId);
    }

    [Fact]
    public async Task Naming_a_cave_nobody_may_read_answers_empty_rather_than_forbidden()
    {
        var caveId = await CreateCaveAsync();

        // A filter is a term of the same visibility-filtered query, so naming an object the
        // caller cannot read is indistinguishable from naming one that is not there — which is
        // what stops the filters becoming a way to test whether something exists.
        var response = await member.GetAsync($"/api/v1/photos?caveId={caveId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Turning_a_photograph_changes_what_is_drawn_and_not_what_was_uploaded()
    {
        var photo = await UploadPhotoAsync(owner, "sideways.png");
        var before = await FileFactsAsync(photo.FileId);

        var turned = await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId },
            rotateQuarterTurns = 1,
        });
        turned.StatusCode.ShouldBe(HttpStatusCode.OK, await turned.Content.ReadAsStringAsync());

        var after = await FileFactsAsync(photo.FileId);
        after.Orientation.ShouldBe(1);

        // The upload is untouched — which is what keeps duplicate detection and "these are the
        // bytes we were given" true through a rotation.
        after.Sha256.ShouldBe(before.Sha256);
        after.SizeBytes.ShouldBe(before.SizeBytes);

        // And the grid is told the shape it will actually get, so a tile is not laid out
        // portrait for a picture that arrives landscape.
        var tile = (await GalleryAsync(owner))
            .Single(p => p.GetProperty("documentId").GetGuid() == photo.DocumentId);
        tile.GetProperty("orientationQuarterTurns").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Turning_a_photograph_four_times_leaves_it_where_it_started()
    {
        var photo = await UploadPhotoAsync(owner, "spun.png");

        for (var i = 0; i < 4; i++)
        {
            (await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
            {
                documentIds = new[] { photo.DocumentId },
                rotateQuarterTurns = 1,
            })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await FileFactsAsync(photo.FileId)).Orientation.ShouldBe(0);
    }

    [Fact]
    public async Task A_bulk_change_reports_what_it_could_not_touch_and_says_nothing_about_the_rest()
    {
        var mine = await UploadPhotoAsync(member, "mine.png");
        var readable = await UploadPhotoAsync(owner, "readable.png");
        var invisible = await UploadPhotoAsync(owner, "invisible.png");

        // Readable but not writable, by name.
        await GrantAsync(memberId, AccessAction.Read, AccessScopeKind.Object, readable.DocumentId);

        var response = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId, readable.DocumentId, invisible.DocumentId },
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var result = await ReadJsonAsync(response);
        result.GetProperty("changed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([mine.DocumentId]);
        result.GetProperty("refused").GetProperty(readable.DocumentId.ToString()).GetString()
            .ShouldBe("photo.write_forbidden");

        // The one they cannot read is in neither list: a refusal naming it would confirm it
        // exists.
        result.GetProperty("refused").EnumerateObject().Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_deleted_photograph_leaves_every_listing_and_can_be_got_back()
    {
        var photo = await UploadPhotoAsync(owner, "mistake.png");

        (await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId },
            delete = true,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Gone from the gallery, from its own page, and from everything else that reads
        // documents — which is what the model-wide filter is for.
        (await GalleryAsync(owner)).Select(p => p.GetProperty("documentId").GetGuid())
            .ShouldNotContain(photo.DocumentId);
        (await owner.GetAsync($"/api/v1/photos/{photo.DocumentId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/documents/{photo.DocumentId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // And listed as restorable, with the window it has left.
        var deleted = await owner.GetFromJsonAsync<JsonElement>("/api/v1/photos/deleted?pageSize=100");
        var row = deleted.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("documentId").GetGuid() == photo.DocumentId);
        row.GetProperty("restorableUntil").GetDateTimeOffset()
            .ShouldBeGreaterThan(row.GetProperty("deletedAt").GetDateTimeOffset());

        (await owner.PostAsync($"/api/v1/photos/{photo.DocumentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GalleryAsync(owner)).Select(p => p.GetProperty("documentId").GetGuid())
            .ShouldContain(photo.DocumentId);
    }

    [Fact]
    public async Task The_purge_takes_the_rows_and_the_bytes_once_the_window_has_passed()
    {
        var photo = await UploadPhotoAsync(owner, "expired.png");
        var storagePath = (await FileFactsAsync(photo.FileId)).StoragePath;

        (await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId },
            delete = true,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Backdated past the window rather than waiting thirty days for it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.Documents.IgnoreQueryFilters().FirstAsync(d => d.Id == photo.DocumentId);
            document.DeletedAt = DateTimeOffset.UtcNow - SoftDeleteRules.DefaultRetention - TimeSpan.FromDays(1);
            await db.SaveChangesAsync();
        }

        await RunPurgeAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == photo.DocumentId)).ShouldBeFalse();
        }

        // The bytes go too, which is the half that makes soft deletion a deletion. Without it
        // the store only ever grows.
        File.Exists(Path.Combine(filesRoot, storagePath.Replace('/', Path.DirectorySeparatorChar)))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Restoring_is_refused_once_the_window_has_passed()
    {
        var photo = await UploadPhotoAsync(owner, "too-late.png");

        (await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId },
            delete = true,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.Documents.IgnoreQueryFilters().FirstAsync(d => d.Id == photo.DocumentId);
            document.DeletedAt = DateTimeOffset.UtcNow - SoftDeleteRules.DefaultRetention - TimeSpan.FromDays(1);
            await db.SaveChangesAsync();
        }

        // Past the window the bytes may already be gone, so a restore would produce a row
        // pointing at nothing — worse than refusing.
        (await owner.PostAsync($"/api/v1/photos/{photo.DocumentId}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_headline_picture_is_chosen_and_there_is_only_ever_one()
    {
        var caveId = await CreateCaveAsync();
        var first = await UploadPhotoAsync(owner, "first.png");
        var second = await UploadPhotoAsync(owner, "second.png");

        var a = await AttachAsync(first.FileId, caveId);
        var b = await AttachAsync(second.FileId, caveId);

        (await owner.PutAsync($"/api/v1/attachments/{a}/primary?primary=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PrimaryIdsAsync(caveId)).ShouldBe([a]);

        // Choosing another replaces it rather than making two: two headline pictures is a state
        // nothing downstream can resolve, and it would resolve differently on every query.
        (await owner.PutAsync($"/api/v1/attachments/{b}/primary?primary=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PrimaryIdsAsync(caveId)).ShouldBe([b]);
    }

    [Fact]
    public async Task A_headline_picture_reaches_the_cave_summary_as_a_rendering_only()
    {
        var caveId = await CreateCaveAsync();
        var photo = await UploadPhotoAsync(owner, "headline.png");
        var attachmentId = await AttachAsync(photo.FileId, caveId);

        (await owner.PutAsync($"/api/v1/attachments/{attachmentId}/primary?primary=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/summary");
        var headline = summary.GetProperty("headlinePicture");
        headline.GetProperty("documentId").GetGuid().ShouldBe(photo.DocumentId);
        headline.GetProperty("attachmentId").GetGuid().ShouldBe(attachmentId);

        // The URL carries its own delivery token, so the card can draw the picture without the
        // reader holding a session for the file — and that token opens renderings and nothing
        // else. A headline picture is shown to everybody who may see the cave at all, so this is
        // the difference between naming a photograph and handing over its bytes.
        var thumbnailUrl = headline.GetProperty("thumbnailUrl").GetString()!;
        thumbnailUrl.ShouldContain($"/api/v1/files/{photo.FileId}/thumbnail");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(thumbnailUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var token = thumbnailUrl[(thumbnailUrl.IndexOf("token=", StringComparison.Ordinal) + 6)..];
        (await anonymous.GetAsync($"/api/v1/files/{photo.FileId}/content?token={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_headline_picture_is_withheld_from_a_reader_of_the_cave_who_may_not_read_it()
    {
        // The cave is public and the photograph is not, which is the ordinary case: a club
        // publishes its caves and keeps the photographs to members.
        var caveId = await CreateCaveAsync();
        var photo = await UploadPhotoAsync(owner, "private-headline.png");
        var attachmentId = await AttachAsync(photo.FileId, caveId);
        (await owner.PutAsync($"/api/v1/attachments/{attachmentId}/primary?primary=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var summary = await member.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/summary");

        // Filtered by the same rule as the count beside it, and it has to be: a headline picture
        // is the loudest thing on the card, so one that outlived the reader's rule would announce
        // the photograph more plainly than any listing.
        summary.GetProperty("headlinePicture").ValueKind.ShouldBe(JsonValueKind.Null);
        summary.GetProperty("attachmentCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Byte_identical_photographs_are_grouped_as_duplicates()
    {
        var bytes = MakePng(64, 64, $"{Guid.NewGuid()}");

        var first = await UploadPhotoAsync(owner, "original.png", bytes);
        var second = await UploadPhotoAsync(owner, "copy.png", bytes, allowDuplicate: true);

        var groups = await owner.GetFromJsonAsync<JsonElement>("/api/v1/photos/duplicates");

        var group = groups.EnumerateArray().Single(g => g.GetProperty("photos").EnumerateArray()
            .Any(p => p.GetProperty("documentId").GetGuid() == first.DocumentId));
        group.GetProperty("photos").EnumerateArray()
            .Select(p => p.GetProperty("documentId").GetGuid())
            .ShouldBe([first.DocumentId, second.DocumentId], ignoreOrder: true);
    }

    private sealed record UploadedPhoto(Guid FileId, Guid DocumentId);

    private async Task<UploadedPhoto> UploadPhotoAsync(
        HttpClient client, string name, byte[]? bytes = null, bool allowDuplicate = false)
    {
        // Unique bytes unless a test is deliberately making a copy: the store warns about
        // content it already holds, and these suites share one database.
        var content = bytes ?? MakePng(64, 64, $"{name} {Guid.NewGuid()}");
        var file = await UploadAsync(client, name, content, "image/png", allowDuplicate);
        return new UploadedPhoto(file, await DocumentIdOfAsync(file));
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, string name, byte[] bytes, string mediaType, bool allowDuplicate = false)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(mediaType);
        using var form = new MultipartFormDataContent { { content, "file", name } };

        // Awaited inside the using: the form must outlive the request body being read.
        var response = await client.PostAsync(
            $"/api/v1/files/?allowDuplicate={allowDuplicate.ToString().ToLowerInvariant()}", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A picture whose bytes are unique, so deduplication does not decide a test.</summary>
    private static byte[] MakePng(uint width, uint height, string marker)
    {
        using var image = new MagickImage(MagickColors.SlateGray, width, height);
        image.Comment = marker;
        return image.ToByteArray(MagickFormat.Png);
    }

    private static async Task<List<JsonElement>> GalleryAsync(HttpClient client, string? query = null)
    {
        var url = query is null ? "/api/v1/photos?pageSize=100" : $"/api/v1/photos?pageSize=100&{query}";
        var page = await client.GetFromJsonAsync<JsonElement>(url);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private sealed record FileFacts(string Sha256, long SizeBytes, int Orientation, string StoragePath);

    private async Task<FileFacts> FileFactsAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        return new FileFacts(file.Sha256, file.SizeBytes, file.OrientationQuarterTurns, file.StoragePath);
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task<Guid> CreateCaverAsync(string fullName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = fullName };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    private async Task<Guid> CreateCaveAsync()
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cave {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "public",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AttachAsync(Guid fileId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "photoEntrance",
            caption = (string?)null,
            sortOrder = 0,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> PrimaryIdsAsync(Guid caveId)
    {
        var attachments = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments?entityType=feature&entityId={caveId}");
        return
        [
            .. attachments.EnumerateArray()
                .Where(a => a.GetProperty("isPrimary").GetBoolean())
                .Select(a => a.GetProperty("id").GetGuid()),
        ];
    }

    private async Task RunPurgeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.DocumentPurge);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.DocumentPurge }, CancellationToken.None);
    }

    /// <summary>Writes a direct access entry, so a negative can be paired with a positive.</summary>
    private async Task GrantAsync(
        Guid userId, AccessAction actions, AccessScopeKind scopeKind, Guid? scopeId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Documents,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
