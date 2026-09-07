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
/// Albums, the links that put one on a club's website, and the installation's curated public
/// gallery.
///
/// <para>
/// Two of these three are anonymous surfaces, so most of what is asserted here is what they do
/// not hand over: the stored bytes of any photograph, a capture position, or anything at all
/// once a link is revoked. The album's own rules are the other half — a cover has to be a
/// member, an order is something somebody chose, and deleting an album must never be a way to
/// lose the pictures in it.
/// </para>
/// </summary>
public sealed class AlbumAndPublicGalleryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;      // Editor — makes albums
    private HttpClient admin = null!;      // full administrator — the only one who may publish
    private HttpClient anonymous = null!;
    private Guid memberId;
    private HttpClient member = null!;     // an ordinary member

    public AlbumAndPublicGalleryTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-albums-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"alb-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"alb-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"alb-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"alb-adm-{suffix}@t.local");

        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"alb-mem-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"alb-mem-{suffix}@t.local");
        await GrantAsync(memberId, AccessAction.Create, AccessScopeKind.All);
        await GrantAsync(memberId, AccessAction.Read | AccessAction.Write, AccessScopeKind.Own);

        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        admin?.Dispose();
        member?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_album_keeps_the_order_somebody_put_its_pictures_in()
    {
        var albumId = await CreateAlbumAsync();
        var a = await UploadPhotoAsync(owner, "a.png");
        var b = await UploadPhotoAsync(owner, "b.png");
        var c = await UploadPhotoAsync(owner, "c.png");

        await AddAsync(albumId, a, b, c);
        (await AlbumOrderAsync(albumId)).ShouldBe([a, b, c]);

        // Moved to the front — the one thing a saved filter cannot express, and the reason an
        // album is an entity.
        (await owner.PostAsJsonAsync($"/api/v1/albums/{albumId}/reorder", new
        {
            documentId = c,
            afterDocumentId = (Guid?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AlbumOrderAsync(albumId)).ShouldBe([c, a, b]);

        // And into the middle.
        (await owner.PostAsJsonAsync($"/api/v1/albums/{albumId}/reorder", new
        {
            documentId = c,
            afterDocumentId = a,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AlbumOrderAsync(albumId)).ShouldBe([a, c, b]);
    }

    [Fact]
    public async Task Adding_the_same_picture_twice_is_the_same_fact()
    {
        var albumId = await CreateAlbumAsync();
        var photo = await UploadPhotoAsync(owner, "once.png");

        await AddAsync(albumId, photo);
        await AddAsync(albumId, photo);

        (await AlbumOrderAsync(albumId)).ShouldBe([photo]);
    }

    [Fact]
    public async Task A_cover_has_to_be_one_of_the_albums_pictures()
    {
        var albumId = await CreateAlbumAsync();
        var inside = await UploadPhotoAsync(owner, "inside.png");
        var outside = await UploadPhotoAsync(owner, "outside.png");
        await AddAsync(albumId, inside);

        // A cover from outside the album is a picture that vanishes from the shelf for anybody
        // who may not read it — a listing whose covers depend on the reader is one nobody can
        // describe.
        (await owner.PutAsync($"/api/v1/albums/{albumId}/cover?documentId={outside}", null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.PutAsync($"/api/v1/albums/{albumId}/cover?documentId={inside}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var album = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{albumId}");
        album.GetProperty("coverDocumentId").GetGuid().ShouldBe(inside);
        album.GetProperty("coverThumbnailUrl").GetString()!.ShouldContain("/thumbnail?");
    }

    [Fact]
    public async Task Taking_the_cover_out_of_the_album_clears_it()
    {
        var albumId = await CreateAlbumAsync();
        var photo = await UploadPhotoAsync(owner, "cover.png");
        await AddAsync(albumId, photo);
        await owner.PutAsync($"/api/v1/albums/{albumId}/cover?documentId={photo}", null);

        (await owner.DeleteAsync($"/api/v1/albums/{albumId}/items/{photo}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var album = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{albumId}");
        album.GetProperty("coverDocumentId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Deleting_an_album_leaves_its_photographs_alone()
    {
        var albumId = await CreateAlbumAsync();
        var photo = await UploadPhotoAsync(owner, "kept.png");
        await AddAsync(albumId, photo);

        (await owner.DeleteAsync($"/api/v1/albums/{albumId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // An album is an arrangement of pictures, never the pictures — deleting one must not be
        // a way to lose them.
        (await owner.GetAsync($"/api/v1/photos/{photo}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_album_shows_only_the_pictures_its_reader_could_already_see()
    {
        var albumId = await CreateAlbumAsync(Visibility.Authenticated);
        var open = await UploadPhotoAsync(owner, "open.png");
        var closed = await UploadPhotoAsync(owner, "closed.png");
        await SetVisibilityAsync(open, "authenticated");
        await AddAsync(albumId, open, closed);

        // The album is readable, and its count is what this reader may see rather than what is
        // in it — a number larger than the grid would state exactly how much was withheld.
        var album = await member.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{albumId}");
        album.GetProperty("photoCount").GetInt32().ShouldBe(1);

        var listed = await GalleryAsync(member, $"albumId={albumId}");
        listed.ShouldBe([open]);
    }

    [Fact]
    public async Task A_share_link_opens_the_album_to_somebody_who_is_not_signed_in()
    {
        var albumId = await CreateAlbumAsync();
        var photo = await UploadPhotoAsync(owner, "shared.png");
        await AddAsync(albumId, photo);

        var token = await ShareAsync(albumId);

        var shared = await anonymous.GetAsync($"/api/v1/public/albums/{token}");
        shared.StatusCode.ShouldBe(HttpStatusCode.OK, await shared.Content.ReadAsStringAsync());

        var body = await ReadJsonAsync(shared);
        var pictures = body.GetProperty("photos").EnumerateArray().ToList();
        pictures.Count.ShouldBe(1);

        // Renderings only, and no way to ask for anything else: the response has no field for a
        // capture position or for the upload, so neither can leak into this surface by somebody
        // adding one to the response every other surface shares.
        var picture = pictures[0];
        picture.GetProperty("thumbnailUrl").GetString()!.ShouldContain("/thumbnail?");
        picture.TryGetProperty("contentUrl", out _).ShouldBeFalse();
        picture.TryGetProperty("position", out _).ShouldBeFalse();
        picture.TryGetProperty("photo", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_revoked_link_opens_nothing_and_says_no_more_than_an_unknown_one()
    {
        var albumId = await CreateAlbumAsync();
        await AddAsync(albumId, await UploadPhotoAsync(owner, "revoked.png"));
        var token = await ShareAsync(albumId);

        (await anonymous.GetAsync($"/api/v1/public/albums/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/albums/{albumId}/share"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Revoked, unknown and sign-in-only all answer the same way: a token is the whole of the
        // caller's claim, and distinguishing the reasons would say which tokens exist.
        (await anonymous.GetAsync($"/api/v1/public/albums/{token}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/v1/public/albums/not-a-real-token")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_public_gallery_shows_only_what_an_administrator_published()
    {
        var published = await UploadPhotoAsync(owner, "published.png");
        var readable = await UploadPhotoAsync(owner, "readable.png");
        await SetVisibilityAsync(readable, "public");

        (await admin.PutAsync($"/api/v1/photos/{published}/public?published=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gallery = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/public/photos?pageSize=100");
        var ids = gallery.GetProperty("items").EnumerateArray()
            .Select(p => p.GetProperty("documentId").GetGuid()).ToList();

        ids.ShouldContain(published);
        // Public visibility means every account here may read it. Putting a picture on the
        // internet is a different decision, taken by a different person — so a readable picture
        // nobody published is not in the public gallery.
        ids.ShouldNotContain(readable);
    }

    [Fact]
    public async Task Publishing_to_the_internet_takes_more_than_being_able_to_edit()
    {
        var photo = await UploadPhotoAsync(owner, "not-mine-to-publish.png");

        // An Editor writes documents all day and still may not publish one to the internet.
        (await owner.PutAsync($"/api/v1/photos/{photo}/public?published=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.PutAsync($"/api/v1/photos/{photo}/public?published=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Withdrawing_a_photograph_takes_it_out_of_the_public_gallery()
    {
        var photo = await UploadPhotoAsync(owner, "withdrawn.png");
        await admin.PutAsync($"/api/v1/photos/{photo}/public?published=true", null);
        (await PublicIdsAsync()).ShouldContain(photo);

        (await admin.PutAsync($"/api/v1/photos/{photo}/public?published=false", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PublicIdsAsync()).ShouldNotContain(photo);
    }

    [Fact]
    public async Task The_anonymous_surfaces_need_no_sign_in_and_the_rest_of_the_gallery_does()
    {
        (await anonymous.GetAsync("/api/v1/public/photos")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Everything else is exactly as closed as it was.
        (await anonymous.GetAsync("/api/v1/photos")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/albums")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreateAlbumAsync(Visibility visibility = Visibility.Private)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/albums", new
        {
            title = $"Album {Guid.NewGuid():N}"[..20],
            description = (string?)null,
            visibility = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(visibility.ToString()),
            cavingGroupId = (Guid?)null,
            subjectEntityType = (string?)null,
            subjectEntityId = (Guid?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddAsync(Guid albumId, params Guid[] documentIds)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/albums/{albumId}/items", documentIds);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> ShareAsync(Guid albumId)
    {
        var response = await owner.PostAsync($"/api/v1/albums/{albumId}/share", null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("token").GetString()!;
    }

    private async Task<List<Guid>> AlbumOrderAsync(Guid albumId) =>
        await GalleryAsync(owner, $"albumId={albumId}");

    private static async Task<List<Guid>> GalleryAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/photos?pageSize=100&{query}");
        return [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("documentId").GetGuid())];
    }

    private async Task<List<Guid>> PublicIdsAsync()
    {
        var gallery = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/public/photos?pageSize=100");
        return
        [
            .. gallery.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("documentId").GetGuid()),
        ];
    }

    private async Task<Guid> UploadPhotoAsync(HttpClient client, string name)
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        image.Comment = $"{name} {Guid.NewGuid()}";

        var content = new ByteArrayContent(image.ToByteArray(MagickFormat.Png));
        content.Headers.ContentType = new("image/png");
        using var form = new MultipartFormDataContent { { content, "file", name } };

        // Awaited inside the using: the form must outlive the request body being read.
        var response = await client.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);

        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task SetVisibilityAsync(Guid documentId, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { documentId },
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

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
