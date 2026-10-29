// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A place a phone pinned on a picture of a cave is a link between the picture, at a pixel, and the
/// place — and a point on a map of the cave beside a place's name is that place's position. So the
/// link is shown whole or not at all, on every route that emits links: only to a reader who may read
/// the picture, may read the place and may see the place exactly.
/// <para>
/// Every case is built where the rule every other link answers to would have shown something: the
/// installation reveals protected associations and the picture carries no geotag; the place is
/// private to a club the reader is not in and is under no protection; the picture is one the reader
/// may not open; the place is deleted. The uploader, who may read and place everything here, is the
/// positive control, so "hide every pin" fails too.
/// </para>
/// </summary>
public sealed class PlacePointProtectionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!; // Editor in both clubs: reads, places and changes everything here
    private HttpClient reader = null!;   // Viewer in the first club only: places nothing guarded
    private Guid uploaderId;
    private Guid clubId;
    private Guid otherClubId;
    private long caveTypeId;
    private long placeTypeId;

    public PlacePointProtectionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-placepoints-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pp-up-{marker}@t.local");
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pp-rd-{marker}@t.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Club {marker}", Slug = $"club-{marker}" };
            var other = new CavingGroup { Name = $"Other {marker}", Slug = $"other-{marker}" };
            db.CavingGroups.AddRange(club, other);
            await db.SaveChangesAsync();
            clubId = club.Id;
            otherClubId = other.Id;
            await RosterHelper.AddMemberAsync(db, clubId, uploaderId);
            await RosterHelper.AddMemberAsync(db, otherClubId, uploaderId);
            await RosterHelper.AddMemberAsync(db, clubId, readerId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"pp-up-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"pp-rd-{marker}@t.local");
    }

    [Fact]
    public async Task A_place_pin_is_shown_whole_or_not_at_all_on_every_route_that_emits_links()
    {
        await SetRevealAsync(true);
        var cave = await CreateCaveAsync();
        var clubCave = await CreateCaveAsync(clubOnly: true);
        var open = await CreatePlaceAsync(cave);
        var guarded = await CreatePlaceAsync(cave, locationProtected: true);
        var clubOnly = await CreatePlaceAsync(clubCave, clubOnly: true);
        var deleted = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave, clubCave], clubId);

        var map = Guid.CreateVersion7();
        var closedMap = Guid.CreateVersion7();
        var clubMap = Guid.CreateVersion7();
        var shown = Guid.CreateVersion7();          // (v) every reader here may be given it
        var onGuarded = Guid.CreateVersion7();      // (i)
        var onClubOnly = Guid.CreateVersion7();     // (ii)
        var onDeleted = Guid.CreateVersion7();      // (iv)
        var onClosedPicture = Guid.CreateVersion7(); // (iii)
        var answer = await UploadAsync(uploader, set,
            maps: [await NewMapAsync(map, cave, "Plan"), await NewMapAsync(closedMap, cave, "Detail"), await NewMapAsync(clubMap, clubCave, "Club")],
            pins:
            [
                Pin(shown, map, open, 10, 10), Pin(onGuarded, map, guarded, 20, 20), Pin(onClubOnly, clubMap, clubOnly, 30, 30),
                Pin(onDeleted, map, deleted, 40, 40), Pin(onClosedPicture, closedMap, open, 5, 5),
            ]);
        Statuses(answer, "rasterPins").ShouldAllBe(s => s == "created");
        var picture = await DocumentOfAsync(map);
        var closedPicture = await DocumentOfAsync(closedMap);
        var clubPicture = await DocumentOfAsync(clubMap);
        await CloseAsync(closedPicture);
        await OpenAsync(clubPicture);
        (await uploader.DeleteAsync($"/api/v1/features/{deleted}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Fixture proof: the reader opens both pictures and reads the open and the guarded place,
        // reads neither the club-only place nor the closed picture, and the uploader reads them all.
        (await reader.GetAsync($"/api/v1/documents/{picture}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/documents/{clubPicture}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/documents/{closedPicture}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/features/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/features/{clubOnly}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync($"/api/v1/features/{clubOnly}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/documents/{closedPicture}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var hiddenFromReader = new[] { onGuarded, onClubOnly, onDeleted, onClosedPicture };

        // The picture's panel: items and the total the badge shows.
        var readerPanel = await PanelAsync(reader, "document", picture);
        LinkIds(readerPanel).ShouldBe([shown]);
        readerPanel.GetProperty("totalItems").GetInt32().ShouldBe(1);
        var uploaderPanel = await PanelAsync(uploader, "document", picture);
        LinkIds(uploaderPanel).ShouldBe([shown, onGuarded], ignoreOrder: true);
        uploaderPanel.GetProperty("totalItems").GetInt32().ShouldBe(2);
        var clubPanel = await PanelAsync(reader, "document", clubPicture);
        LinkIds(clubPanel).ShouldBeEmpty();
        clubPanel.GetProperty("totalItems").GetInt32().ShouldBe(0);
        LinkIds(await PanelAsync(uploader, "document", clubPicture)).ShouldBe([onClubOnly]);

        // The place's panel: the open place lists only the pin on the picture the reader may open;
        // the guarded place lists none, and counts none.
        var openPanel = await PanelAsync(reader, "feature", open);
        LinkIds(openPanel).ShouldBe([shown]);
        openPanel.GetProperty("totalItems").GetInt32().ShouldBe(1);
        LinkIds(await PanelAsync(uploader, "feature", open)).ShouldBe([shown, onClosedPicture], ignoreOrder: true);
        var guardedPanel = await PanelAsync(reader, "feature", guarded);
        LinkIds(guardedPanel).ShouldBeEmpty();
        guardedPanel.GetProperty("totalItems").GetInt32().ShouldBe(0);
        LinkIds(await PanelAsync(uploader, "feature", guarded)).ShouldBe([onGuarded]);

        // By id and by short code.
        foreach (var pin in hiddenFromReader)
        {
            (await reader.GetAsync($"/api/v1/reslinks/{pin}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, pin.ToString());
            var code = await ShortCodeAsync(pin);
            var byCode = await reader.GetAsync($"/api/v1/reslinks/{code}");
            byCode.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(byCode)).ShouldBe("reslink.code.unresolved");
        }

        (await reader.GetAsync($"/api/v1/reslinks/{shown}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/reslinks/{await ShortCodeAsync(shown)}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/reslinks/{onClubOnly}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/reslinks/{onDeleted}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "its place is deleted");

        // Every generic write answers a hidden pin as one that is not there, before anything else.
        foreach (var pin in hiddenFromReader)
        {
            foreach (var response in await GenericWritesAsync(reader, pin))
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound, pin.ToString());
                (await ProblemCodeAsync(response)).ShouldBe("reslink.not_found");
            }
        }

        // No timeline reaches a picture's member rows, the rows that carry a pin's point: a
        // document has no history route of its own, to any reader.
        foreach (var client in new[] { reader, uploader })
        {
            (await client.GetAsync($"/api/v1/history?entityType=document&entityId={picture}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);
        }

        // A place's timeline carries its member rows, which hold no point; the rule every history
        // row answers to takes the link they name from a reader who may not place the place.
        var guardedHistory = await GetPageAsync(reader, $"/api/v1/history?entityType=feature&entityId={guarded}&pageSize=100");
        guardedHistory.GetRawText().ShouldNotContain(onGuarded.ToString());
        (await GetPageAsync(uploader, $"/api/v1/history?entityType=feature&entityId={guarded}&pageSize=100"))
            .GetRawText().ShouldContain(onGuarded.ToString());
    }

    [Fact]
    public async Task A_place_pin_is_made_and_changed_only_through_its_own_routes()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var other = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var map = Guid.CreateVersion7();
        var pin = Guid.CreateVersion7();
        await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, place, 10, 10)]);
        var picture = await DocumentOfAsync(map);
        var file = await CurrentFileAsync(picture);
        var relation = await RelationIdAsync("map-place-point");

        // The uploader may change the cave, and is still refused every side door.
        foreach (var response in await GenericWritesAsync(uploader, pin, otherPlace: other, includeDelete: false))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(response)).ShouldBe("reslink.place_point.fixed");
        }

        var made = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = relation,
            members = new object[]
            {
                new { targetType = "document", targetId = picture, isMain = true, anchorKind = "imageRegion", anchor = new { shape = "point", x = 0.5, y = 0.5 }, anchorFileId = file },
                new { targetType = "feature", targetId = other, isMain = false },
            },
        });
        made.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(made)).ShouldBe("reslink.place_point.fixed");

        // A stored pixel is not asserted through the generic routes under any relation.
        var related = await RelationIdAsync("related-to");
        var pixel = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = related,
            members = new object[]
            {
                new { targetType = "document", targetId = picture, isMain = false, anchorKind = "imageRegion", anchor = new { shape = "point", x = 0.5, y = 0.5, px = 1, py = 1 }, anchorFileId = file },
            },
        });
        pixel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(pixel)).ShouldBe("reslink.member.invalid_anchor");

        // Nor is a link of another kind turned into a pin.
        var plain = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = related,
            members = new object[] { new { targetType = "document", targetId = picture }, new { targetType = "feature", targetId = other } },
        });
        plain.StatusCode.ShouldBe(HttpStatusCode.Created, await plain.Content.ReadAsStringAsync());
        var plainId = (await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var turned = await uploader.PatchAsJsonAsync($"/api/v1/reslinks/{plainId}", new { relationTypeId = relation });
        turned.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(turned)).ShouldBe("reslink.place_point.fixed");

        // The ordinary delete removes the pin the way a phone does: it is gone from the links and
        // told to the phones as a tombstone.
        (await reader.DeleteAsync($"/api/v1/reslinks/{pin}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await uploader.DeleteAsync($"/api/v1/reslinks/{pin}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await uploader.GetAsync($"/api/v1/reslinks/{pin}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        LinkIds(await PanelAsync(uploader, "document", picture)).ShouldNotContain(pin);
        var stream = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");
        PinTombstones(stream).ShouldBe([pin]);
        Map(stream, map).GetProperty("pins").GetArrayLength().ShouldBe(0);
    }

    // ---- reading -------------------------------------------------------------------------------

    private static async Task<JsonElement> PanelAsync(HttpClient client, string type, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/reslinks/for-target?type={type}&id={id}&pageSize=100&relation=map-place-point");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static IReadOnlyList<Guid> LinkIds(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())];

    /// <summary>Every generic write of one link, each answered.</summary>
    private async Task<List<HttpResponseMessage>> GenericWritesAsync(
        HttpClient client, Guid link, Guid? otherPlace = null, bool includeDelete = true)
    {
        var member = await AnyMemberAsync(link);
        var responses = new List<HttpResponseMessage>
        {
            await client.PatchAsJsonAsync($"/api/v1/reslinks/{link}", new { description = "changed" }),
            await client.PostAsJsonAsync($"/api/v1/reslinks/{link}/members", new { targetType = "feature", targetId = otherPlace ?? Guid.CreateVersion7() }),
            await client.PatchAsJsonAsync($"/api/v1/reslinks/{link}/members/{member}", new { isMain = false, sortOrder = 3 }),
            await client.DeleteAsync($"/api/v1/reslinks/{link}/members/{member}"),
        };
        if (includeDelete)
        {
            responses.Add(await client.DeleteAsync($"/api/v1/reslinks/{link}"));
        }

        return responses;
    }

    private async Task<Guid> AnyMemberAsync(Guid link)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == link && m.FeatureId != null).Select(m => m.Id).FirstAsync();
    }

    private async Task<string> ShortCodeAsync(Guid link)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinks.AsNoTracking().Where(l => l.Id == link).Select(l => l.ShortCode).SingleAsync();
    }

    private async Task<long> RelationIdAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkRelationTypes.AsNoTracking().Where(r => r.Code == code).Select(r => r.Id).SingleAsync();
    }

    private async Task<Guid> DocumentOfAsync(Guid mapId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.RasterMaps.AsNoTracking().Where(m => m.Id == mapId).Select(m => m.DocumentId).SingleAsync();
    }

    private async Task<Guid> CurrentFileAsync(Guid document)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from version in db.DocumentVersions.AsNoTracking()
                      join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                      where version.DocumentId == document && version.IsCurrent
                      orderby file.CreatedAt, file.Id
                      select file.Id).FirstAsync();
    }

    /// <summary>
    /// A picture nobody but its owner may open: private, bound to no club, and hung on nothing —
    /// the map's attachment to its cave would otherwise open it to every reader of the cave.
    /// </summary>
    private async Task CloseAsync(Guid document)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var files = db.DocumentVersions.Where(v => v.DocumentId == document)
            .SelectMany(v => db.StoredFiles.Where(f => f.DocumentVersionId == v.Id).Select(f => f.Id));
        await db.Attachments.Where(a => files.Contains(a.FileId)).ExecuteDeleteAsync();
        await db.Documents.Where(d => d.Id == document)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.Visibility, Visibility.Private).SetProperty(d => d.CavingGroupId, (Guid?)null));
    }

    /// <summary>A picture every signed-in account may open by its own rules.</summary>
    private async Task OpenAsync(Guid document)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Documents.Where(d => d.Id == document)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.Visibility, Visibility.Authenticated));
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        var root = JsonDocument.Parse(payload).RootElement;
        return root.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static IReadOnlyList<Guid> PinTombstones(JsonElement page) =>
        [.. page.GetProperty("pinTombstones").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];

    private static JsonElement Map(JsonElement page, Guid id) =>
        page.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);

    private static IReadOnlyList<string?> Statuses(JsonElement answer, string section) =>
        [.. answer.GetProperty(section).EnumerateArray().Select(r => r.GetProperty("status").GetString())];

    // ---- writing -------------------------------------------------------------------------------

    private async Task<object> NewMapAsync(Guid id, Guid caveId, string title)
    {
        var picture = Corpus.Jpeg();
        var staged = await StageAsync(uploader, "map.jpg", picture);
        return new
        {
            id,
            caveId,
            title,
            mapType = "planView",
            fileName = "map.jpg",
            sha256 = Convert.ToHexStringLower(SHA256.HashData(picture)),
            sizeBytes = picture.Length,
            stagedUploadId = staged,
        };
    }

    private static object Pin(Guid id, Guid mapId, Guid placeId, int x, int y) =>
        new { id, rasterMapId = mapId, placeId, x, y };

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, Guid set, IReadOnlyList<object>? maps = null, IReadOnlyList<object>? pins = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rasterMaps = maps,
            rasterPins = pins,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<Guid> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var opened = await client.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots, Guid? cavingGroupId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            cavingGroupId,
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A cave every account reads — or, club-only, one only the second club reads.</summary>
    private async Task<Guid> CreateCaveAsync(bool clubOnly = false)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pestera {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = clubOnly ? "cavingGroup" : "authenticated",
            cavingGroupId = clubOnly ? otherClubId : (Guid?)null,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A marked place inside a cave, owned by the uploader: guarded as its own protection root, or
    /// visible only to the second club, which the reader is not in.
    /// </summary>
    private async Task<Guid> CreatePlaceAsync(Guid caveId, bool locationProtected = false, bool clubOnly = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = $"punct {Guid.NewGuid():N}"[..12],
            FeatureTypeId = placeTypeId,
            OwnerUserId = uploaderId,
            Visibility = clubOnly ? Visibility.CavingGroup : Visibility.Authenticated,
            CavingGroupId = clubOnly ? otherClubId : null,
            LocationProtected = locationProtected,
            Properties = """{"speleolocSchemaVersion":1}""",
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
        await db.SaveChangesAsync();
        return feature.Id;
    }

    /// <summary>
    /// Flips the installation's reveal setting; <see cref="DisposeAsync"/> takes it away again, so
    /// no other class tests under it.
    /// </summary>
    private async Task SetRevealAsync(bool reveal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(
            AppSettingSections.Protection, new ProtectionSettings { RevealProtectedAssociations = reveal });
    }

    public async ValueTask DisposeAsync()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.AppSettings.Where(s => s.Key == AppSettingSections.Protection).ExecuteDeleteAsync();
        }

        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
