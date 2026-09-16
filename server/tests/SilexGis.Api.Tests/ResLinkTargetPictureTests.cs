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
/// The picture on a resource-link member: whether one is produced at all, what it opens, and
/// who is told about it.
///
/// This exists because the two halves of the feature were each proved on their own and their
/// join was proved by nothing. A viewer was shown to draw a strip of photographs given a URL;
/// the link surface was shown to describe a membership; and in between, every display the
/// server built carried a null picture, so the strip was empty against the real API on every
/// screen that used it. A test that asserts only the empty case cannot see that — which is why
/// every refusal below is stated beside the URL the same fixture produces for somebody
/// entitled to it.
///
/// Nothing here asserts anything about image content. What is asserted is the shape of the
/// capability: that the URL names the file's rendering route, and that the token on it opens
/// the rendering and refuses the stored bytes.
/// </summary>
public sealed class ResLinkTargetPictureTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — owns every fixture, so every refusal has its positive
    private HttpClient viewer = null!;  // Viewer — reads caves, holds nothing over exact locations
    private HttpClient keeper = null!;  // Manager — keeps the roster, so only they may link an account
    private long caveTypeId;

    public ResLinkTargetPictureTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-rlpic-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rp-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rp-view-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rp-keep-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"rp-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"rp-view-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"rp-keep-{suffix}@t.local");
    }

    /// <summary>
    /// The case the station strip is made of: a photograph and a station of a survey model in
    /// one link. Both halves are asserted — the anchor that says which station, and the picture
    /// that is hung on it — because either one missing empties the strip, and an empty strip
    /// looks the same whichever half failed.
    /// </summary>
    [Fact]
    public async Task A_photograph_anchored_to_a_station_comes_back_with_a_rendering_only_URL()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cave = await CreateCaveAsync($"Station strip {suffix}", "authenticated");
        var modelId = await CreateSurveyModelAsync(cave, $"model-{suffix}.lox");
        var photoFileId = await UploadFileAsync($"at-station-{suffix}.jpg", PlainJpeg(), "image/jpeg");
        var photoDocId = await DocumentIdOfAsync(photoFileId);

        var linkId = await CreateLinkAsync(
            Member("surveyModel", modelId, anchorKind: "modelStation", anchor: new { station = "1.5" }),
            Member("document", photoDocId, sortOrder: 1));

        var members = MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}"));

        // The anchor half: which station the picture belongs to.
        var station = members.Single(m => m.GetProperty("targetId").GetGuid() == modelId);
        station.GetProperty("anchorKind").GetString().ShouldBe("modelStation");
        station.GetProperty("anchor").GetProperty("station").GetString().ShouldBe("1.5");

        // The picture half: the field that was null on every path until now.
        var picture = ThumbnailOf(members, photoDocId);
        picture.ShouldNotBeNull();
        picture.ShouldStartWith($"/api/v1/files/{photoFileId}/thumbnail?");
        picture.ShouldContain("token=");

        // What the token opens, asserted by spending it rather than by reading it. The
        // rendering is served…
        (await owner.GetAsync(picture)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // …and the stored bytes are not, to the very caller who was just handed the URL. That
        // is the whole difference between a derivatives-only reach and a full one: a
        // photograph's own bytes carry the GPS fix its camera wrote, and a chip that minted the
        // wider reach would hand that out to everybody who can see the chip.
        (await owner.GetAsync($"/api/v1/files/{photoFileId}/content?token={TokenIn(picture)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the same token is no skeleton key: it names one file.
        var otherFileId = await UploadFileAsync($"elsewhere-{suffix}.jpg", PlainJpeg(), "image/jpeg");
        (await owner.GetAsync($"/api/v1/files/{otherFileId}/thumbnail?size=480&token={TokenIn(picture)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A picture is minted on the branch that decided the caller may read the document, and on
    /// no other. The document here is born private, so the viewer is refused it — and the same
    /// document, widened, reaches the same viewer with a URL, which is what makes the refusal
    /// above a decision about rights rather than a fixture that never worked.
    /// </summary>
    [Fact]
    public async Task A_caller_who_may_not_read_the_photograph_is_handed_no_picture()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cave = await CreateCaveAsync($"Private album {suffix}", "authenticated");
        var photoFileId = await UploadFileAsync($"unshared-{suffix}.jpg", PlainJpeg(), "image/jpeg");
        var photoDocId = await DocumentIdOfAsync(photoFileId);

        var linkId = await CreateLinkAsync(
            Member("feature", cave), Member("document", photoDocId, sortOrder: 1));

        // The whole display is withheld, so there is no field for a picture to sit in — and no
        // byte of the delivery route travels either, which is the assertion that would catch a
        // URL leaking through some other field.
        var refusedBody = await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}");
        refusedBody.ShouldNotContain(photoFileId.ToString());
        var refused = MembersOf(refusedBody).Single(m => m.GetProperty("targetId").GetGuid() == photoDocId);
        refused.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);

        // Its owner, on the same link and the same document, is handed the picture.
        ThumbnailOf(MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{linkId}")), photoDocId)
            .ShouldNotBeNull();

        // And widening the document hands it to the viewer too: the refusal moved with the
        // right, rather than being a picture the server never produces for anybody.
        await MakeDocumentReadableAsync(photoDocId);
        var admitted = ThumbnailOf(MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}")), photoDocId);
        admitted.ShouldNotBeNull();
        admitted.ShouldStartWith($"/api/v1/files/{photoFileId}/thumbnail?");
        (await viewer.GetAsync(admitted)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A document that serves something other than an image has no rendering to point at, and
    /// says null rather than offering a URL that would draw nothing. Stated beside an image in
    /// the same link, so "null" is being read off the file's kind and not off a resolver that
    /// stopped producing pictures.
    /// </summary>
    [Fact]
    public async Task A_document_that_serves_no_picture_offers_none()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (reportId, _) = await UploadTextDocumentAsync($"trip-report-{suffix}.txt");
        await MakeDocumentReadableAsync(reportId);
        var photoFileId = await UploadFileAsync($"gallery-{suffix}.jpg", PlainJpeg(), "image/jpeg");
        var photoDocId = await DocumentIdOfAsync(photoFileId);
        await MakeDocumentReadableAsync(photoDocId);

        var linkId = await CreateLinkAsync(
            Member("document", reportId), Member("document", photoDocId, sortOrder: 1));
        var members = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));

        // Readable either way — the media type travels for both, which is how a reader knows
        // which viewer opens which — and only the one made of pixels carries a picture.
        DisplayOf(members, reportId).GetProperty("mediaType").GetString().ShouldBe("text/plain");
        ThumbnailOf(members, reportId).ShouldBeNull();

        DisplayOf(members, photoDocId).GetProperty("mediaType").GetString().ShouldBe("image/jpeg");
        ThumbnailOf(members, photoDocId).ShouldNotBeNull();
    }

    /// <summary>
    /// A cave's headline picture on a chip is the same picture its own page leads with, and is
    /// kept back by the same rule — so a caller who may not place a guarded cave is never handed
    /// a photograph stamped with coordinates under that cave's name.
    ///
    /// Both settings are walked, because they withhold for different reasons and only one of
    /// them is observable at the picture: with the association rule at its default the whole
    /// membership goes, and with the installation revealing names the membership stays while
    /// the picture does not. The open cave rides along throughout as the positive leg.
    /// </summary>
    [Fact]
    public async Task A_guarded_cave_keeps_its_headline_picture_from_a_caller_who_may_not_place_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The guarded cave, led by a photograph carrying a capture point of its own — the
        // pairing that is the position rather than a fact about it.
        var guarded = await CreateCaveAsync($"Guarded portal {suffix}", "authenticated", locationProtected: true);
        var guardedPhotoId = await UploadFileAsync(
            $"guarded-entrance-{suffix}.jpg", GeotaggedJpeg(45.53127, 25.44721), "image/jpeg");
        await MakeDocumentReadableAsync(await DocumentIdOfAsync(guardedPhotoId));
        await MakeHeadlineAsync(guardedPhotoId, guarded);

        // The open cave, led by a photograph with no fix at all: nothing about it is guarded,
        // and it must keep its picture for every caller throughout.
        var open = await CreateCaveAsync($"Open portal {suffix}", "authenticated");
        var openPhotoId = await UploadFileAsync($"open-entrance-{suffix}.jpg", PlainJpeg(), "image/jpeg");
        await MakeDocumentReadableAsync(await DocumentIdOfAsync(openPhotoId));
        await MakeHeadlineAsync(openPhotoId, open);

        var guardedLink = await CreateLinkAsync(Member("feature", guarded));
        var openLink = await CreateLinkAsync(Member("feature", open));

        // Default settings: the guarded membership does not reach the viewer at all, so there
        // is nothing for a picture to hang on…
        MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{guardedLink}")).ShouldBeEmpty();

        // …while its owner, who may place the cave, is handed the picture…
        var ownersGuarded = ThumbnailOf(
            MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{guardedLink}")), guarded);
        ownersGuarded.ShouldNotBeNull();
        ownersGuarded.ShouldStartWith($"/api/v1/files/{guardedPhotoId}/thumbnail?");

        // …and the same viewer is handed the open cave's, which is what makes the silence above
        // a protection rather than a resolver that produces no pictures for this caller.
        ThumbnailOf(MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{openLink}")), open)
            .ShouldNotBeNull();

        await SetRevealAsync(true);
        try
        {
            // The setting reopens the name, and only the name. The viewer now reads the guarded
            // cave's chip whole — title and route — and still gets no picture, because a
            // photograph carrying a position, placed under a guarded cave's name, is the one
            // pairing no setting opens.
            var revealed = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{guardedLink}"));
            var display = DisplayOf(revealed, guarded);
            display.GetProperty("title").GetString()!.ShouldContain(suffix);
            display.GetProperty("route").GetString().ShouldBe($"/caves/{guarded}");
            ThumbnailOf(revealed, guarded).ShouldBeNull();

            // Both positives still stand under the same setting: the owner keeps the guarded
            // cave's picture, and the viewer keeps the open one's. Without these, a resolver
            // that had simply stopped minting anything would pass the assertion above.
            ThumbnailOf(MembersOf(await BodyAsync(owner, $"/api/v1/reslinks/{guardedLink}")), guarded)
                .ShouldNotBeNull();
            ThumbnailOf(MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{openLink}")), open)
                .ShouldNotBeNull();
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    /// <summary>
    /// The two-chip case: a guarded cave and its own geotagged entrance photograph standing in
    /// one link, read with the installation revealing associations. It looks like the pairing
    /// the headline rule refuses one chip over, and it is not, because the two chips are saying
    /// different things.
    ///
    /// A picture on the <em>cave's</em> chip asserts that this photograph is that cave's
    /// headline. Nothing else tells this caller so — the attachment surface withholds that
    /// association whatever the setting says, because the photograph carries a fix of its own —
    /// so the picture there would be the disclosure, and it is withheld.
    ///
    /// A picture on the <em>document's</em> chip asserts only what the document is, to a caller
    /// the same walk has just admitted to reading it. That the two are linked is the link's own
    /// content, which is precisely what the reveal setting was switched on to open. So the chip
    /// hands over no reach the caller did not already hold, and this test proves that rather
    /// than asserting it: the viewer walks the member row's target id to the document, to its
    /// file, and out of the file surface comes the same rendering route, at the same size, that
    /// refuses the stored bytes in the same way.
    /// </summary>
    [Fact]
    public async Task A_guarded_caves_photograph_hands_over_no_reach_its_document_did_not_already()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateCaveAsync($"Paired chips {suffix}", "authenticated", locationProtected: true);
        var photoFileId = await UploadFileAsync(
            $"paired-entrance-{suffix}.jpg", GeotaggedJpeg(45.60011, 25.51234), "image/jpeg");
        var photoDocId = await DocumentIdOfAsync(photoFileId);
        await MakeDocumentReadableAsync(photoDocId);
        await MakeHeadlineAsync(photoFileId, guarded);

        var linkId = await CreateLinkAsync(
            Member("feature", guarded), Member("document", photoDocId, sortOrder: 1));

        // Default settings: the cave's membership never reaches the viewer, so the two chips
        // never stand together in the first place and only the document is named.
        var closed = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));
        closed.Select(m => m.GetProperty("targetId").GetGuid()).ShouldBe([photoDocId]);

        await SetRevealAsync(true);
        try
        {
            var revealed = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));

            // The cave's chip opens by name and by route, and keeps its headline back.
            DisplayOf(revealed, guarded).GetProperty("route").GetString().ShouldBe($"/caves/{guarded}");
            ThumbnailOf(revealed, guarded).ShouldBeNull();

            // The document's chip carries the photograph.
            var chip = ThumbnailOf(revealed, photoDocId);
            chip.ShouldNotBeNull();

            // The same caller, walking the member row on their own, is handed the identical
            // route by the file's own surface. This is the assertion that says the chip is a
            // shortcut and not a channel: if it ever mints something the document surface would
            // not, these two stop matching.
            var document = JsonDocument.Parse(
                await BodyAsync(viewer, $"/api/v1/documents/{photoDocId}")).RootElement;
            document.GetProperty("currentFileId").GetGuid().ShouldBe(photoFileId);

            var file = JsonDocument.Parse(
                await BodyAsync(viewer, $"/api/v1/files/{photoFileId}")).RootElement;
            var ownSurface = file.GetProperty("thumbnailUrl").GetString();
            ownSurface.ShouldNotBeNull();

            var route = $"/api/v1/files/{photoFileId}/thumbnail?size=480&token=";
            chip.ShouldStartWith(route);
            ownSurface.ShouldStartWith(route);

            // Both open the rendering; neither opens the stored bytes, which is where the
            // camera's fix actually lives. The file surface says as much in its own words, and
            // withholds the position it would otherwise print beside them.
            (await viewer.GetAsync(chip)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await viewer.GetAsync(ownSurface)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await viewer.GetAsync($"/api/v1/files/{photoFileId}/content?token={TokenIn(chip)}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await viewer.GetAsync($"/api/v1/files/{photoFileId}/content?token={TokenIn(ownSurface)}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            file.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeFalse();
            file.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    /// <summary>
    /// A person's chip shows the portrait their own account chose, and a roster entry with no
    /// account shows nothing — both stated here, because "no picture" is the right answer for
    /// one of them and would be a defect for the other.
    /// </summary>
    [Fact]
    public async Task A_person_is_shown_under_the_portrait_their_account_chose()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var withAccount = await CreateCaverAsync($"Ana Portret {suffix}");
        await LinkAccountAsync(withAccount, await MyUserIdAsync(owner));
        var avatarFileId = await UploadAvatarAsync();

        // The same roster, minus an account: nobody has chosen a picture for this person, and
        // the resolver must not go looking for one elsewhere.
        var withoutAccount = await CreateCaverAsync($"Barbu Anonim {suffix}");

        var linkId = await CreateLinkAsync(
            Member("caver", withAccount), Member("caver", withoutAccount, sortOrder: 1));
        var members = MembersOf(await BodyAsync(viewer, $"/api/v1/reslinks/{linkId}"));

        var portrait = ThumbnailOf(members, withAccount);
        portrait.ShouldNotBeNull();
        portrait.ShouldStartWith($"/api/v1/files/{avatarFileId}/thumbnail?");
        (await viewer.GetAsync(portrait)).StatusCode.ShouldBe(HttpStatusCode.OK);

        ThumbnailOf(members, withoutAccount).ShouldBeNull();
    }

    // ---- fixtures ----------------------------------------------------------------------

    private static object Member(
        string targetType,
        Guid targetId,
        int sortOrder = 0,
        string anchorKind = "whole",
        object? anchor = null) => new
    {
        targetType,
        targetId,
        isMain = false,
        sortOrder,
        note = (string?)null,
        anchorKind,
        anchor,
        anchorFileId = (Guid?)null,
    };

    private async Task<Guid> CreateLinkAsync(params object[] members)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> BodyAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return payload;
    }

    private static List<JsonElement> MembersOf(string linkBody) =>
        [.. JsonDocument.Parse(linkBody).RootElement.GetProperty("members").EnumerateArray()];

    private static JsonElement DisplayOf(IEnumerable<JsonElement> members, Guid targetId)
    {
        var display = members.Single(m => m.GetProperty("targetId").GetGuid() == targetId)
            .GetProperty("display");
        display.ValueKind.ShouldBe(JsonValueKind.Object, $"no display resolved for {targetId}");
        return display;
    }

    /// <summary>The member's picture, or null when it has none — the field under test.</summary>
    private static string? ThumbnailOf(IEnumerable<JsonElement> members, Guid targetId)
    {
        var thumbnail = DisplayOf(members, targetId).GetProperty("thumbnailUrl");
        return thumbnail.ValueKind == JsonValueKind.Null ? null : thumbnail.GetString();
    }

    /// <summary>The token a delivery URL carries, still escaped as it travelled.</summary>
    private static string TokenIn(string url) =>
        url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..];

    private Task<Guid> CreateCaveAsync(string name, string visibility) =>
        CreateCaveAsync(name, visibility, locationProtected: false);

    private async Task<Guid> CreateCaveAsync(string name, string visibility, bool locationProtected)
    {
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
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A roster entry, created by the person who keeps the roster.</summary>
    private async Task<Guid> CreateCaverAsync(string fullName)
    {
        var response = await keeper.PostAsJsonAsync("/api/v1/cavers/", new
        {
            fullName,
            email = (string?)null,
            phone = (string?)null,
            notes = (string?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Attaches an account to a roster entry — a roster-keeper's act, never the
    /// account holder's own.</summary>
    private async Task LinkAccountAsync(Guid caverId, Guid userId)
    {
        var response = await keeper.PostAsJsonAsync($"/api/v1/cavers/{caverId}/account-link", new { userId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> MyUserIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Gives the owner's account a portrait and returns the file it is stored as, read back out
    /// of the URL the account surface publishes — so a chip pointing at a different file than
    /// the profile does would show up here rather than as two pictures nobody compared.
    /// </summary>
    private async Task<Guid> UploadAvatarAsync()
    {
        var content = new ByteArrayContent(PlainJpeg());
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", "portrait.jpg" } };
        var response = await owner.PostAsync("/api/v1/me/avatar", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        var avatarUrl = JsonDocument.Parse(payload).RootElement.GetProperty("avatarUrl").GetString();
        avatarUrl.ShouldNotBeNull();
        var afterFiles = avatarUrl["/api/v1/files/".Length..];
        return Guid.Parse(afterFiles[..afterFiles.IndexOf('/', StringComparison.Ordinal)]);
    }

    private async Task<Guid> CreateSurveyModelAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes($"LOX-{Guid.NewGuid():N}"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadFileAsync(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store warns
        // about; saying yes up front is what a person would do.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(Guid DocumentId, Guid FileId)> UploadTextDocumentAsync(string fileName)
    {
        var fileId = await UploadFileAsync(
            fileName, System.Text.Encoding.UTF8.GetBytes($"contents of {fileName}"), "text/plain");
        return (await DocumentIdOfAsync(fileId), fileId);
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    /// <summary>Attaches a file to a feature and makes it that feature's headline — the
    /// picture the feature leads with, which is what a chip shows.</summary>
    private async Task MakeHeadlineAsync(Guid fileId, Guid featureId)
    {
        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = featureId,
            role = "photoEntrance",
            sortOrder = 0,
        });
        var payload = await attached.Content.ReadAsStringAsync();
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var attachmentId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var promoted = await owner.PutAsync($"/api/v1/attachments/{attachmentId}/primary?primary=true", null);
        promoted.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await promoted.Content.ReadAsStringAsync());
    }

    /// <summary>Widens a born-private document to every signed-in caller, through the
    /// document's own write surface.</summary>
    private async Task MakeDocumentReadableAsync(Guid documentId)
    {
        var response = await owner.GetAsync($"/api/v1/documents/{documentId}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var current = JsonDocument.Parse(body).RootElement;

        var updated = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = current.GetProperty("title").GetString(),
            documentTypeId = current.GetProperty("documentTypeId").ValueKind == JsonValueKind.Number
                ? current.GetProperty("documentTypeId").GetInt64()
                : (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Flips the installation's reveal setting. The row lives in a database shared with every
    /// other class in this collection, which is why it is put back in a finally and deleted on
    /// the way out: a policy left switched on here would quietly change what those classes are
    /// testing.
    /// </summary>
    private async Task SetRevealAsync(bool reveal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(
            AppSettingSections.Protection, new ProtectionSettings { RevealProtectedAssociations = reveal });
    }

    /// <summary>A small JPEG with no EXIF at all: a picture that places nothing.</summary>
    private static byte[] PlainJpeg()
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>A JPEG stamped with a capture point, which is what makes a photograph able to
    /// place the thing it is filed under.</summary>
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
        viewer?.Dispose();
        keeper?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
