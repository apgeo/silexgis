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
public sealed class PhotoGalleryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads and edits
    private HttpClient member = null!;  // an ordinary member: adds photographs, edits their own
    private Guid memberId;

    public PhotoGalleryTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-gallery-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),

            // A window, because the purge and the past-the-window refusal are what some of these
            // tests are about; an installation that sets none keeps everything it deletes.
            ["DocumentRetention:RetentionDays"] = ((int)SoftDeleteRules.DefaultRetention.TotalDays).ToString(),
        });
    }

    public async ValueTask InitializeAsync()
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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
    public async Task A_phones_portrait_photograph_stored_on_its_side_gets_a_portrait_tile()
    {
        // Stored 64 wide and 48 high, with the code a phone writes for a portrait shot: drawn
        // upright it is 48 wide and 64 high, and that is the shape the grid is told.
        var bytes = Corpus.Oriented(MagickFormat.Jpeg, 6, 64, 48, comment: $"portrait {Guid.NewGuid()}");
        var file = await UploadAsync(owner, "portrait.jpg", bytes, "image/jpeg");
        var documentId = await DocumentIdOfAsync(file);

        var tile = (await GalleryAsync(owner)).Single(p => p.GetProperty("documentId").GetGuid() == documentId);
        tile.GetProperty("width").GetInt32().ShouldBe(48);
        tile.GetProperty("height").GetInt32().ShouldBe(64);

        // Fixture proof: the rendition really is that shape.
        using (var rendition = new MagickImage(await owner.GetByteArrayAsync(tile.GetProperty("thumbnailUrl").GetString())))
        {
            ((int)rendition.Width, (int)rendition.Height).ShouldBe((48, 64));
        }

        // A quarter turn recorded on top of the code's quarter turn lies it on its other side.
        (await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { documentId },
            rotateQuarterTurns = 1,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var turned = (await GalleryAsync(owner)).Single(p => p.GetProperty("documentId").GetGuid() == documentId);
        turned.GetProperty("width").GetInt32().ShouldBe(64);
        turned.GetProperty("height").GetInt32().ShouldBe(48);
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
    public async Task A_turn_is_recorded_on_the_upload_and_never_on_a_copy_made_from_it()
    {
        var photo = await UploadPhotoAsync(owner, "converted.png");

        // A copy derived from the upload, on the same revision and stored after it — what a
        // conversion leaves beside an original. The upload's row is then rewritten through an
        // indexed column, there and back, which moves it behind the copy's both in the table and
        // in every index, so a read that takes "the first file" in whatever order the database
        // returns finds the copy.
        Guid copyId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var upload = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photo.FileId);
            var digest = Guid.NewGuid().ToString("N");
            var copy = new StoredFile
            {
                DocumentVersionId = upload.DocumentVersionId,
                ConvertedFromFileId = upload.Id,
                StoragePath = $"test/{upload.Id:N}-converted",
                OriginalName = $"{upload.Id:N}.webp",
                MimeType = "image/webp",
                Kind = FileKind.Image,
                Sha256 = digest + digest,
                SizeBytes = 1,
            };
            db.StoredFiles.Add(copy);
            await db.SaveChangesAsync();
            copyId = copy.Id;
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == copyId)).CreatedAt
                .ShouldBeGreaterThan(upload.CreatedAt);
            await db.StoredFiles.Where(f => f.Id == upload.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Kind, FileKind.Other));
            await db.StoredFiles.Where(f => f.Id == upload.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Kind, FileKind.Image));
        }

        var turned = await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId },
            rotateQuarterTurns = 1,
        });
        turned.StatusCode.ShouldBe(HttpStatusCode.OK, await turned.Content.ReadAsStringAsync());

        (await FileFactsAsync(photo.FileId)).Orientation.ShouldBe(1);
        (await FileFactsAsync(copyId)).Orientation.ShouldBe(0);
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

    /// <summary>
    /// Attaching pictures in bulk hands them to everyone who can read the target, so it takes
    /// write on the target whichever way the target is named — and an id with no type names
    /// nothing and is refused, rather than being read as a feature and attached unchecked.
    /// </summary>
    [Fact]
    public async Task A_bulk_attach_takes_write_on_the_target_and_an_id_without_a_type_is_refused()
    {
        var mine = await UploadPhotoAsync(member, "mine.png");
        var readableCave = await CreateCaveAsync();       // the member reads it, writes nothing
        var unreadableCave = await CreatePrivateCaveAsync();

        // An id with no type. This used to attach to the feature with no check at all.
        var untyped = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId },
            attachEntityId = readableCave,
        });
        untyped.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await untyped.Content.ReadAsStringAsync());

        // A type with no id names nothing either.
        (await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId },
            attachEntityType = "feature",
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Named properly, a cave the member reads but may not write is forbidden ...
        var unwritable = await BulkAttachAsync(member, mine.DocumentId, readableCave);
        unwritable.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await unwritable.Content.ReadAsStringAsync());

        // ... and one they may not read is answered exactly as one that does not exist, so the
        // route is no oracle for which feature ids are real.
        foreach (var hidden in new[] { unreadableCave, Guid.NewGuid() })
        {
            var absent = await BulkAttachAsync(member, mine.DocumentId, hidden);
            absent.StatusCode.ShouldBe(HttpStatusCode.NotFound, await absent.Content.ReadAsStringAsync());
            (await ReadJsonAsync(absent)).GetProperty("code").GetString().ShouldBe("attachment.entity_not_found");
        }

        (await AttachmentCountAsync(mine.FileId)).ShouldBe(0);

        // Given write on the cave, the same member attaches the same picture — the refusals
        // above were the rule, not a broken fixture.
        await GrantFeatureWriteAsync(memberId, readableCave);
        var allowed = await BulkAttachAsync(member, mine.DocumentId, readableCave);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ReadJsonAsync(allowed)).GetProperty("changed").EnumerateArray()
            .Select(x => x.GetGuid()).ShouldBe([mine.DocumentId]);
        (await AttachmentCountAsync(mine.FileId, readableCave)).ShouldBe(1);
    }

    [Fact]
    public async Task A_bulk_request_names_something_to_do_and_at_most_the_documented_number_of_photographs()
    {
        var mine = await UploadPhotoAsync(member, "mine.png");

        (await member.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = new[] { mine.DocumentId } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await member.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = Array.Empty<Guid>(), visibility = "private" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).Append(mine.DocumentId).ToArray();
        (await member.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = tooMany, visibility = "private" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The same request at the limit is acted on.
        var atLimit = tooMany[^500..];
        var allowed = await member.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = atLimit, visibility = "private" });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ReadJsonAsync(allowed)).GetProperty("changed").EnumerateArray()
            .Select(x => x.GetGuid()).ShouldBe([mine.DocumentId]);
    }

    /// <summary>
    /// A tag named in a bulk request is looked up once, before any picture is touched: one that
    /// does not exist is a refusal naming it, with nothing changed, and naming the same tag twice
    /// applies it once — neither is a server error that rolls back every other picture.
    /// </summary>
    [Fact]
    public async Task A_bulk_tag_that_does_not_exist_is_refused_and_a_repeated_one_is_applied_once()
    {
        var mine = await UploadPhotoAsync(member, "tagged.png");
        var tagId = await CreateTagAsync($"bulk-tag-{Guid.NewGuid():N}");
        const long Unknown = long.MaxValue - 7;

        var unknown = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId },
            addTagIds = new[] { tagId, Unknown },
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await unknown.Content.ReadAsStringAsync());
        (await ReadJsonAsync(unknown)).GetProperty("code").GetString().ShouldBe("tag.not_found");
        (await TaggingCountAsync(mine.FileId, tagId)).ShouldBe(0);

        var repeated = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId },
            addTagIds = new[] { tagId, tagId },
        });
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await repeated.Content.ReadAsStringAsync());
        (await ReadJsonAsync(repeated)).GetProperty("changed").EnumerateArray()
            .Select(x => x.GetGuid()).ShouldBe([mine.DocumentId]);
        (await TaggingCountAsync(mine.FileId, tagId)).ShouldBe(1);
    }

    /// <summary>
    /// Adding to an album from the gallery answers the album routes' own way: an album the caller
    /// cannot read, or one that does not exist, is not found; one they read but may not write is
    /// forbidden; their own is filled.
    /// </summary>
    [Fact]
    public async Task A_bulk_add_to_an_album_answers_an_unreadable_or_unknown_album_as_not_found()
    {
        var mine = await UploadPhotoAsync(member, "for-an-album.png");
        var theirs = await CreateAlbumAsync(owner, "public");      // readable, not the member's
        var hidden = await CreateAlbumAsync(owner, "private");     // the member has no grant on it
        var own = await CreateAlbumAsync(member, "private");

        foreach (var absent in new[] { hidden, Guid.NewGuid() })
        {
            var refused = await BulkAddToAlbumAsync(member, mine.DocumentId, absent);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync());
            (await ReadJsonAsync(refused)).GetProperty("code").GetString().ShouldBe("album.not_found");
        }

        var forbidden = await BulkAddToAlbumAsync(member, mine.DocumentId, theirs);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await forbidden.Content.ReadAsStringAsync());
        (await ReadJsonAsync(forbidden)).GetProperty("code").GetString().ShouldBe("album.write_forbidden");

        // The owner reads the hidden album — it was hidden from the member, not broken.
        (await owner.GetAsync($"/api/v1/albums/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var allowed = await BulkAddToAlbumAsync(member, mine.DocumentId, own);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await AlbumItemIdsAsync(own)).ShouldBe([mine.DocumentId]);
    }

    /// <summary>
    /// Filing pictures into an album from the gallery takes what the album's own items route
    /// takes — write on the album and read on each picture — because it changes the album, not
    /// the picture. The same picture in a request that also changes it is refused for want of
    /// write on it, and one the caller cannot read is filed nowhere and named nowhere.
    /// </summary>
    [Fact]
    public async Task Filing_a_readable_picture_into_ones_own_album_takes_no_write_on_the_picture()
    {
        var readable = await UploadPhotoAsync(owner, "club-photo.png");
        var invisible = await UploadPhotoAsync(owner, "unseen-photo.png");
        await GrantAsync(memberId, AccessAction.Read, AccessScopeKind.Object, readable.DocumentId);
        var own = await CreateAlbumAsync(member, "private");

        // Changing the picture as well still takes write on it.
        var changing = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { readable.DocumentId },
            addToAlbumId = own,
            rotateQuarterTurns = 1,
        });
        changing.StatusCode.ShouldBe(HttpStatusCode.OK, await changing.Content.ReadAsStringAsync());
        (await ReadJsonAsync(changing)).GetProperty("refused").GetProperty(readable.DocumentId.ToString())
            .GetString().ShouldBe("photo.write_forbidden");
        (await AlbumItemIdsAsync(own)).ShouldBeEmpty();

        var filing = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { readable.DocumentId, invisible.DocumentId },
            addToAlbumId = own,
        });
        filing.StatusCode.ShouldBe(HttpStatusCode.OK, await filing.Content.ReadAsStringAsync());
        var result = await ReadJsonAsync(filing);
        result.GetProperty("changed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([readable.DocumentId]);
        result.GetProperty("refused").EnumerateObject().ShouldBeEmpty();
        (await AlbumItemIdsAsync(own)).ShouldBe([readable.DocumentId]);

        // The owner reads the picture the member could not, so it was hidden, not missing.
        (await owner.GetAsync($"/api/v1/documents/{invisible.DocumentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Club visibility on a picture that belongs to no club would admit nobody but its owner and
    /// the administrators, silently. The bulk route names no club, so it refuses such a picture
    /// by name and gives the band to the pictures that have a club.
    /// </summary>
    [Fact]
    public async Task Club_visibility_in_bulk_is_refused_for_a_picture_that_belongs_to_no_club()
    {
        var clubless = await UploadPhotoAsync(member, "no-club.png");
        var clubbed = await UploadPhotoAsync(member, "a-club.png");
        await SetClubAsync(clubbed.DocumentId);

        var response = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { clubless.DocumentId, clubbed.DocumentId },
            visibility = "cavingGroup",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await ReadJsonAsync(response);
        result.GetProperty("changed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([clubbed.DocumentId]);
        result.GetProperty("refused").GetProperty(clubless.DocumentId.ToString()).GetString()
            .ShouldBe("photo.caving_group_required");

        (await VisibilityOfAsync(clubless.DocumentId)).ShouldNotBe(Visibility.CavingGroup);
        (await VisibilityOfAsync(clubbed.DocumentId)).ShouldBe(Visibility.CavingGroup);
    }

    private async Task SetClubAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var group = new CavingGroup { Name = $"Invented Club {suffix}", Slug = $"invented-club-{suffix}" };
        db.CavingGroups.Add(group);
        var document = await db.Documents.FirstAsync(d => d.Id == documentId);
        document.CavingGroupId = group.Id;
        await db.SaveChangesAsync();
    }

    private async Task<Visibility> VisibilityOfAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.Visibility).FirstAsync();
    }

    /// <summary>
    /// An album hangs on a subject only when the subject exists and the caller may read it. One
    /// they cannot read is answered as one that does not exist; an id that names nothing is a
    /// coded refusal rather than a failed save; an id with no type is refused rather than
    /// dropped in silence. The same member hangs one on a cave they read.
    /// </summary>
    [Fact]
    public async Task An_album_names_only_a_subject_its_creator_can_read()
    {
        var readableCave = await CreateCaveAsync();
        var unreadableCave = await CreatePrivateCaveAsync();

        foreach (var absent in new[] { unreadableCave, Guid.NewGuid() })
        {
            var refused = await member.PostAsJsonAsync("/api/v1/albums", AlbumOn("feature", absent));
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync());
            (await ReadJsonAsync(refused)).GetProperty("code").GetString().ShouldBe("album.subject_not_found");
        }

        (await member.PostAsJsonAsync("/api/v1/albums", AlbumOn(null, readableCave)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The owner reads the private cave, so it is real; the member was refused for not reading it.
        (await owner.GetAsync($"/api/v1/features/{unreadableCave}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await member.PostAsJsonAsync("/api/v1/albums", AlbumOn("feature", readableCave));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var albumId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        // Moving it onto a subject the member cannot read is refused the same way, and re-saving
        // it under the subject it already has is not.
        (await member.PutAsJsonAsync($"/api/v1/albums/{albumId}", AlbumOn("feature", unreadableCave)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.PutAsJsonAsync($"/api/v1/albums/{albumId}", AlbumOn("feature", readableCave)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Adding to an album takes photographs only, and at most as many in one request as the bulk
    /// route does.
    /// </summary>
    [Fact]
    public async Task Adding_to_an_album_takes_photographs_only_and_a_bounded_number_of_them()
    {
        var album = await CreateAlbumAsync(member, "private");
        var photo = await UploadPhotoAsync(member, "album-item.png");
        var notes = await DocumentIdOfAsync(await UploadAsync(
            member, "album-notes.txt", System.Text.Encoding.UTF8.GetBytes($"invented notes {Guid.NewGuid()}"), "text/plain"));

        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();
        var refused = await member.PostAsJsonAsync($"/api/v1/albums/{album}/items", tooMany);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await ReadJsonAsync(refused)).GetProperty("code").GetString().ShouldBe("album.too_many_items");

        var added = await member.PostAsJsonAsync($"/api/v1/albums/{album}/items", new[] { photo.DocumentId, notes });
        added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        (await AlbumItemIdsAsync(album)).ShouldBe([photo.DocumentId]);
    }

    private static object AlbumOn(string? subjectType, Guid subjectId) => new
    {
        title = "An invented album",
        visibility = "private",
        subjectEntityType = subjectType,
        subjectEntityId = subjectId,
    };

    private static async Task<Guid> CreateAlbumAsync(HttpClient client, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/albums", new { title = "An invented album", visibility });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> BulkAddToAlbumAsync(HttpClient client, Guid documentId, Guid albumId) =>
        client.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { documentId },
            addToAlbumId = albumId,
        });

    private async Task<List<Guid>> AlbumItemIdsAsync(Guid albumId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AlbumItems.AsNoTracking().Where(i => i.AlbumId == albumId)
            .Select(i => i.DocumentId).ToListAsync();
    }

    private async Task<long> CreateTagAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tag = new Tag { Name = name, Slug = Tag.Slugify(name) };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<int> TaggingCountAsync(Guid fileId, long tagId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Taggings.AsNoTracking().CountAsync(t =>
            t.TagId == tagId && t.EntityType == AttachedEntityType.StoredFile && t.EntityId == fileId);
    }

    private static Task<HttpResponseMessage> BulkAttachAsync(HttpClient client, Guid documentId, Guid featureId) =>
        client.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { documentId },
            attachEntityType = "feature",
            attachEntityId = featureId,
        });

    private async Task<int> AttachmentCountAsync(Guid fileId, Guid? featureId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Attachments.AsNoTracking()
            .CountAsync(a => a.FileId == fileId && (featureId == null || a.FeatureId == featureId));
    }

    /// <summary>Write on one feature, named, so a refusal can be paired with the same caller allowed.</summary>
    private async Task GrantFeatureWriteAsync(Guid userId, Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read | AccessAction.Write,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
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
    public async Task A_headline_picture_is_offered_to_everybody_the_attachment_reaches()
    {
        // Worth pinning because it surprises: the photograph is private and owned by somebody
        // else, and the ordinary member is still shown it. Hanging a document on an object the
        // caller may read is what grants the read — otherwise attaching a survey to a cave
        // would put it out of reach of everyone who works on that cave. The headline picture
        // rides on exactly that rule rather than on one of its own, which is the point.
        var caveId = await CreateCaveAsync();
        var photo = await UploadPhotoAsync(owner, "shared-headline.png");
        var attachmentId = await AttachAsync(photo.FileId, caveId);
        (await owner.PutAsync($"/api/v1/attachments/{attachmentId}/primary?primary=true", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var summary = await member.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/summary");

        summary.GetProperty("attachmentCount").GetInt32().ShouldBe(1);
        summary.GetProperty("headlinePicture").GetProperty("documentId").GetGuid()
            .ShouldBe(photo.DocumentId);

        // Reaching the cave is what carried it, so a cave the member cannot read carries
        // nothing: the summary itself is refused rather than answered without the picture.
        var privateCaveId = await CreatePrivateCaveAsync();
        (await member.GetAsync($"/api/v1/caves/{privateCaveId}/summary"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
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

    /// <summary>
    /// Two sets of copies come back as two groups, each holding its own pair and nothing of the
    /// other — however the groups' members are gathered and projected.
    /// </summary>
    [Fact]
    public async Task Two_sets_of_copies_are_two_groups_each_holding_only_its_own()
    {
        var bytesA = MakePng(64, 64, $"a {Guid.NewGuid()}");
        var bytesB = MakePng(64, 64, $"b {Guid.NewGuid()}");
        var a1 = await UploadPhotoAsync(owner, "a-original.png", bytesA);
        var a2 = await UploadPhotoAsync(owner, "a-copy.png", bytesA, allowDuplicate: true);
        var b1 = await UploadPhotoAsync(owner, "b-original.png", bytesB);
        var b2 = await UploadPhotoAsync(owner, "b-copy.png", bytesB, allowDuplicate: true);

        var groups = await owner.GetFromJsonAsync<JsonElement>("/api/v1/photos/duplicates?limit=200");

        List<Guid> MembersOf(Guid documentId) =>
        [
            .. groups.EnumerateArray()
                .Single(g => g.GetProperty("photos").EnumerateArray()
                    .Any(p => p.GetProperty("documentId").GetGuid() == documentId))
                .GetProperty("photos").EnumerateArray()
                .Select(p => p.GetProperty("documentId").GetGuid()),
        ];

        MembersOf(a1.DocumentId).ShouldBe([a1.DocumentId, a2.DocumentId], ignoreOrder: true);
        MembersOf(b1.DocumentId).ShouldBe([b1.DocumentId, b2.DocumentId], ignoreOrder: true);
    }

    /// <summary>
    /// Taking a tag off in bulk is a change to the picture: it goes from one the caller may write,
    /// and one they may only read is refused with the tag left on it.
    /// </summary>
    [Fact]
    public async Task A_bulk_tag_removal_takes_write_on_the_picture()
    {
        var mine = await UploadPhotoAsync(member, "untag-mine.png");
        var readable = await UploadPhotoAsync(owner, "untag-readable.png");
        await GrantAsync(memberId, AccessAction.Read, AccessScopeKind.Object, readable.DocumentId);
        var tagId = await CreateTagAsync($"bulk-untag-{Guid.NewGuid():N}");
        await TagFileAsync(mine.FileId, tagId);
        await TagFileAsync(readable.FileId, tagId);

        var response = await member.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { mine.DocumentId, readable.DocumentId },
            removeTagIds = new[] { tagId },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var result = await ReadJsonAsync(response);
        result.GetProperty("changed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([mine.DocumentId]);
        result.GetProperty("refused").GetProperty(readable.DocumentId.ToString()).GetString()
            .ShouldBe("photo.write_forbidden");
        (await TaggingCountAsync(mine.FileId, tagId)).ShouldBe(0);
        (await TaggingCountAsync(readable.FileId, tagId)).ShouldBe(1);
    }

    /// <summary>
    /// A bulk turn names photographs; a document that is not one is not turned, not reported and
    /// not refused — the same silence an unreadable id gets.
    /// </summary>
    [Fact]
    public async Task A_bulk_turn_leaves_a_document_that_is_not_a_picture_alone()
    {
        var photo = await UploadPhotoAsync(owner, "turn-me.png");
        var report = await UploadAsync(owner, "turn-not.txt", "notes to leave alone"u8.ToArray(), "text/plain");
        var reportDocument = await DocumentIdOfAsync(report);

        var response = await owner.PostAsJsonAsync("/api/v1/photos/bulk", new
        {
            documentIds = new[] { photo.DocumentId, reportDocument },
            rotateQuarterTurns = 1,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var result = await ReadJsonAsync(response);
        result.GetProperty("changed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([photo.DocumentId]);
        result.GetProperty("refused").EnumerateObject().Count().ShouldBe(0);
        (await TurnsOfAsync(photo.FileId)).ShouldBe(1);
        (await TurnsOfAsync(report)).ShouldBe(0);
    }

    private async Task TagFileAsync(Guid fileId, long tagId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.Taggings.Add(new Tagging { TagId = tagId, EntityType = AttachedEntityType.StoredFile, EntityId = fileId });
        await db.SaveChangesAsync();
    }

    private async Task<int> TurnsOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == fileId).Select(f => f.OrientationQuarterTurns).SingleAsync();
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

    [Fact]
    public async Task A_gallery_window_that_is_not_a_window_is_refused_and_a_real_one_is_not()
    {
        foreach (var window in new[] { "not-a-box", "8,40,9", "9,41,8,40", "-181,40,-179,41", "8,-91,9,-89" })
        {
            var refused = await owner.GetAsync($"/api/v1/photos?bbox={window}");
            var body = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe("map.invalid_bbox");
        }

        // A blank window is no window; a well-formed one is read.
        (await owner.GetAsync("/api/v1/photos?bbox=")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync("/api/v1/photos?bbox=-180,-90,180,90")).StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private Task<Guid> CreateCaveAsync() => CreateCaveAsync("public");

    /// <summary>A cave the ordinary member cannot read, so a negative has something to rest on.</summary>
    private Task<Guid> CreatePrivateCaveAsync() => CreateCaveAsync("private");

    private async Task<Guid> CreateCaveAsync(string visibility)
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
            visibility,
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
