// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Tests;

/// <summary>
/// Places pinned on phones' maps, written from every side: the browser moves, adds and removes them
/// and the phones receive each change under the pin's own identifier; a turn recorded on the picture
/// and a new revision of it redraw the pins without moving what a phone holds; an undo puts a pin
/// back out of history written before and after pins were links; two uploads pinning one place at
/// once leave one pin; and the pins go with their picture when it is removed for good.
/// <para>
/// The uploader is an Editor in the club and may change every cave here; the reader is a Viewer in
/// the same club, reads every cave and place here and may change none.
/// </para>
/// </summary>
public sealed class PlacePointWriteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private Guid uploaderId;
    private Guid clubId;
    private Guid otherClubId;
    private long caveTypeId;
    private long placeTypeId;

    public PlacePointWriteTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-placepointwrites-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pw-up-{marker}@t.local");
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pw-rd-{marker}@t.local");
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
            await RosterHelper.AddMemberAsync(db, clubId, readerId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"pw-up-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"pw-rd-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_pin_moved_in_the_browser_reaches_the_phones_under_its_identifier_with_its_new_pixel()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, pin) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var made = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, place, 10, 20)]);
        var revision = Revision(made, "rasterPins", pin);
        var picture = await DocumentOfAsync(map);
        var file = await CurrentFileAsync(picture);
        var first = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");

        // Whoever may change the cave may move it, and the button says so; a reader may not.
        LinkOf(await PanelAsync(uploader, "document", picture), pin).GetProperty("mayEdit").GetBoolean().ShouldBeTrue();
        LinkOf(await PanelAsync(reader, "document", picture), pin).GetProperty("mayEdit").GetBoolean().ShouldBeFalse();

        (await anonymous.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = file, x = 0.5, y = 0.5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var invalid = await uploader.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = file, x = 1.5, y = 0.5 });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = file, x = 0.5, y = 0.5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var nowhere = await uploader.PutAsJsonAsync($"/api/v1/reslinks/{Guid.CreateVersion7()}/place-point", new { fileId = file, x = 0.5, y = 0.5 });
        nowhere.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(nowhere)).ShouldBe("reslink.not_found");
        var elsewhere = await uploader.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = Guid.CreateVersion7(), x = 0.5, y = 0.5 });
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(elsewhere)).ShouldBe("reslink.place_point.not_current_file");

        // A link of another kind is no pin to move.
        var plain = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = await RelationIdAsync("related-to"),
            members = new object[] { new { targetType = "document", targetId = picture }, new { targetType = "feature", targetId = place } },
        });
        var plainId = (await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await uploader.PutAsJsonAsync($"/api/v1/reslinks/{plainId}/place-point", new { fileId = file, x = 0.5, y = 0.5 }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Moved to the middle of the 64 x 48 picture, a quarter of the way down: the pixel under
        // that point, and the fraction of the pixel's centre.
        var moved = await uploader.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = file, x = 0.5, y = 0.25 });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var anchor = (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("isMain").GetBoolean()).GetProperty("anchor");
        anchor.GetProperty("px").GetInt32().ShouldBe(32);
        anchor.GetProperty("py").GetInt32().ShouldBe(12);
        anchor.GetProperty("x").GetDouble().ShouldBe(32.5 / 64, 1e-12);

        // The phones get the pin again past where they had read, under its identifier, with the new
        // pixel and a new revision.
        var again = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters?cursor={Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!)}");
        var sent = Map(again, map).GetProperty("pins").EnumerateArray().ShouldHaveSingleItem();
        sent.GetProperty("id").GetGuid().ShouldBe(pin);
        (sent.GetProperty("x").GetInt32(), sent.GetProperty("y").GetInt32()).ShouldBe((32, 12));
        DateTimeOffset.Parse(sent.GetProperty("revision").GetString()!).ShouldBeGreaterThan(revision);

        // A phone that moved it from its old revision is told it changed.
        var stale = await UploadAsync(uploader, set, pins: [Pin(pin, map, place, 1, 1, revision)]);
        Statuses(stale, "rasterPins").ShouldBe(["conflict"]);

        // A picture whose size is not known cannot be measured on for a phone.
        await ForgetSizeAsync(file);
        var unknown = await uploader.PutAsJsonAsync($"/api/v1/reslinks/{pin}/place-point", new { fileId = file, x = 0.5, y = 0.5 });
        unknown.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(unknown)).ShouldBe("reslink.place_point.frame_unknown");
    }

    [Fact]
    public async Task A_pin_added_in_the_browser_is_one_the_phones_are_given_and_may_change()
    {
        var cave = await CreateCaveAsync();
        var otherCave = await CreateCaveAsync();
        var pinned = await CreatePlaceAsync(cave);
        var free = await CreatePlaceAsync(cave);
        var outside = await CreatePlaceAsync(otherCave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, pin) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, pinned, 1, 1)]);
        var picture = await DocumentOfAsync(map);
        var file = await CurrentFileAsync(picture);
        object Body(Guid document, Guid place, Guid fileId, double x = 0.25, double y = 0.5) =>
            new { documentId = document, placeId = place, fileId, x, y };

        (await anonymous.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(picture, free, file))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await uploader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(Guid.Empty, free, file))).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(picture, free, file))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        var noMap = await uploader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(Guid.CreateVersion7(), free, file));
        noMap.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(noMap)).ShouldBe("reslink.place_point.map_not_found");
        var away = await uploader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(picture, outside, file));
        away.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(away)).ShouldBe("reslink.place_point.place_not_found");
        var twice = await uploader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(picture, pinned, file));
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(twice)).ShouldBe("reslink.place_point.place_pinned");

        var created = await uploader.PostAsJsonAsync("/api/v1/reslinks/place-points", Body(picture, free, file));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var added = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        added.ShouldNotBe(Guid.Empty);

        // The phones are given it as any other pin: an ordinary identifier, the pixel under the point.
        var page = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");
        var sent = Map(page, map).GetProperty("pins").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == added);
        sent.GetProperty("placeId").GetGuid().ShouldBe(free);
        (sent.GetProperty("x").GetInt32(), sent.GetProperty("y").GetInt32()).ShouldBe((16, 24));

        // One live pin per place holds for it as for an upload, and a phone may move it.
        var rival = await UploadAsync(uploader, set, pins: [Pin(Guid.CreateVersion7(), map, free, 3, 3)]);
        Code(rival, "rasterPins").ShouldBe("sync.pin_exists");
        var revision = DateTimeOffset.Parse(sent.GetProperty("revision").GetString()!);
        var moved = await UploadAsync(uploader, set, pins: [Pin(added, map, free, 5, 6, revision)]);
        Statuses(moved, "rasterPins").ShouldBe(["updated"]);
    }

    [Fact]
    public async Task A_turn_recorded_on_the_picture_redraws_its_pins_and_leaves_what_the_phones_hold()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, pin) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var made = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, place, 10, 20)]);
        var revision = Revision(made, "rasterPins", pin);
        var picture = await DocumentOfAsync(map);
        (await AnchorAsync(pin)).X!.Value.ShouldBe(10.5 / 64, 1e-12);

        var turned = await uploader.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = new[] { picture }, rotateQuarterTurns = 1 });
        turned.StatusCode.ShouldBe(HttpStatusCode.OK, await turned.Content.ReadAsStringAsync());

        // A quarter turn clockwise: the stored pixel (10, 20) of a 64 x 48 picture is drawn at
        // (27, 10) of the 48 x 64 one.
        var anchor = await AnchorAsync(pin);
        (anchor.Px, anchor.Py).ShouldBe((10, 20));
        anchor.X!.Value.ShouldBe(27.5 / 48, 1e-12);
        anchor.Y!.Value.ShouldBe(10.5 / 64, 1e-12);

        var sent = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map).GetProperty("pins")
            .EnumerateArray().ShouldHaveSingleItem();
        (sent.GetProperty("x").GetInt32(), sent.GetProperty("y").GetInt32()).ShouldBe((10, 20));
        (DateTimeOffset.Parse(sent.GetProperty("revision").GetString()!).UtcTicks / 10).ShouldBe(revision.UtcTicks / 10);
    }

    [Fact]
    public async Task A_new_picture_of_the_same_size_carries_the_pins_over_and_one_of_another_size_leaves_them()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, pin) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var made = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, place, 10, 20)]);
        var pinRevision = Revision(made, "rasterPins", pin);
        var picture = await DocumentOfAsync(map);

        var sameSize = Picture(MagickColors.Teal, 64, 48);
        var edited = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan", sameSize, Revision(made, "rasterMaps", map))]);
        Statuses(edited, "rasterMaps").ShouldBe(["updated"]);
        var second = await CurrentFileAsync(picture);
        (await AnchorFileAsync(pin)).ShouldBe(second);
        var sent = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map).GetProperty("pins").EnumerateArray().ShouldHaveSingleItem();
        (DateTimeOffset.Parse(sent.GetProperty("revision").GetString()!).UtcTicks / 10).ShouldBe(pinRevision.UtcTicks / 10);

        var bigger = Picture(MagickColors.Olive, 80, 60);
        var resized = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan", bigger, Revision(edited, "rasterMaps", map))]);
        Statuses(resized, "rasterMaps").ShouldBe(["updated"]);
        (await CurrentFileAsync(picture)).ShouldNotBe(second);
        (await AnchorFileAsync(pin)).ShouldBe(second, "measured on a picture of another size, it stays where it was measured");

        // The phones keep being sent it, as they always were; the browser is told it was measured on
        // an earlier picture.
        sent = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map).GetProperty("pins").EnumerateArray().ShouldHaveSingleItem();
        (sent.GetProperty("x").GetInt32(), sent.GetProperty("y").GetInt32()).ShouldBe((10, 20));
        LinkOf(await PanelAsync(uploader, "document", picture), pin).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("isMain").GetBoolean()).GetProperty("anchorState").GetString().ShouldBe("degraded");
    }

    [Fact]
    public async Task An_undo_puts_a_pin_back_from_history_written_before_and_after_pins_were_links()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, pin) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var made = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(pin, map, place, 10, 10)]);

        // Moved by a phone, and the move taken back.
        var move = await UploadAsync(uploader, set, pins: [Pin(pin, map, place, 30, 31, Revision(made, "rasterPins", pin))]);
        Statuses(move, "rasterPins").ShouldBe(["updated"]);
        var undoMove = await RevertAsync(uploader, move.GetProperty("importBatchId").GetGuid());
        undoMove.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        (await PixelAsync(set, map, pin)).ShouldBe((10, 10));

        // History written before pins were links names the pin by its own name and fields; an undo
        // of such an upload reads it the same way.
        var legacy = await LegacyMoveAsync(pin, from: (5, 6), to: (10, 10));
        var undoLegacy = await RevertAsync(uploader, legacy);
        undoLegacy.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("outcome").GetString().ShouldBe("undone");
        (await PixelAsync(set, map, pin)).ShouldBe((5, 6));

        // Removed by a phone and brought back under its own identifier.
        var current = DateTimeOffset.Parse(Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map)
            .GetProperty("pins")[0].GetProperty("revision").GetString()!);
        var removal = await UploadAsync(uploader, set, pins: [new { id = pin, rasterMapId = map, baseRevision = current, deleted = true }]);
        Statuses(removal, "rasterPins").ShouldBe(["deleted"]);
        PinTombstones(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters")).ShouldBe([pin]);
        var undoRemoval = await RevertAsync(uploader, removal.GetProperty("importBatchId").GetGuid());
        undoRemoval.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        var back = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters");
        PinTombstones(back).ShouldBeEmpty();
        Map(back, map).GetProperty("pins").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(pin);

        // And the upload that made it, taken back, removes it.
        var undoMade = await RevertAsync(uploader, made.GetProperty("importBatchId").GetGuid());
        undoMade.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        (await GetAsync(uploader, $"/api/v1/reslinks/{pin}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Two_uploads_pinning_one_place_at_once_leave_one_pin()
    {
        var cave = await CreateCaveAsync();
        var place = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var map = Guid.CreateVersion7();
        await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")]);

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            UploadAsync(uploader, set, pins: [Pin(Guid.CreateVersion7(), map, place, i, i)])));
        answers.Count(a => Statuses(a, "rasterPins").Single() == "created").ShouldBe(1);
        answers.Count(a => Code(a, "rasterPins") == "sync.pin_exists").ShouldBe(3);
        Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map).GetProperty("pins").GetArrayLength().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var problems = await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
        problems.ShouldNotContain(p => p.Check.StartsWith("place_point", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_maps_picture_removed_for_good_takes_its_pins_with_it_removed_ones_included()
    {
        var cave = await CreateCaveAsync();
        var a = await CreatePlaceAsync(cave);
        var b = await CreatePlaceAsync(cave);
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var (map, kept, gone) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var made = await UploadAsync(uploader, set, maps: [await NewMapAsync(map, cave, "Plan")], pins: [Pin(kept, map, a, 1, 1), Pin(gone, map, b, 2, 2)]);
        await UploadAsync(uploader, set, pins: [new { id = gone, rasterMapId = map, baseRevision = Revision(made, "rasterPins", gone), deleted = true }]);
        var picture = await DocumentOfAsync(map);
        var removed = await UploadAsync(uploader, set, maps: [new { id = map, baseRevision = await MapRevisionAsync(map), deleted = true }]);
        Statuses(removed, "rasterMaps").ShouldBe(["deleted"]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ResLinks.CountAsync(l => l.Id == kept || l.Id == gone)).ShouldBe(2, "a removed map keeps its pins, hidden with it");
            await db.Documents.IgnoreQueryFilters().Where(d => d.Id == picture)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow.AddYears(-5)));
        }

        await RunPurgeAsync();
        await using var check = factory.Services.CreateAsyncScope();
        var after = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await after.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == picture)).ShouldBeFalse();
        (await after.ResLinks.CountAsync(l => l.Id == kept || l.Id == gone)).ShouldBe(0);
        (await after.ResLinkMembers.CountAsync(m => m.ResLinkId == kept || m.ResLinkId == gone)).ShouldBe(0);
    }

    // ---- specific to the writes ----------------------------------------------------------------

    private async Task<(int X, int Y)> PixelAsync(Guid set, Guid map, Guid pin)
    {
        var sent = Map(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/rasters"), map).GetProperty("pins").EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == pin);
        return (sent.GetProperty("x").GetInt32(), sent.GetProperty("y").GetInt32());
    }

    /// <summary>
    /// An upload as one made before pins were links left it: a line naming the pin, and the
    /// history row of the pin under its own name, moving it from one pixel to another.
    /// </summary>
    private async Task<Guid> LegacyMoveAsync(Guid pin, (int X, int Y) from, (int X, int Y) to)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batch = new ImportBatch { ConfirmedByUserId = uploaderId, Source = ImportSource.DeviceSync };
        db.ImportBatches.Add(batch);
        db.ImportBatchItems.Add(new ImportBatchItem { ImportBatchId = batch.Id, RasterPinId = pin, Action = ImportDecisionAction.Attach });
        db.AuditEntries.Add(new AuditEntry
        {
            Action = AuditActions.Updated,
            EntityType = "RasterPin",
            EntityId = pin.ToString(),
            Changes = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["X"] = new { old = from.X, @new = to.X },
                ["Y"] = new { old = from.Y, @new = to.Y },
            }),
            Source = ChangeSource.DeviceSync,
            ImportBatchId = batch.Id,
            UserId = uploaderId,
        });
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private async Task<PlacePointAnchor.Parsed> AnchorAsync(Guid pin)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var anchor = await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == pin && m.IsMain).Select(m => m.Anchor).SingleAsync();
        return PlacePointAnchor.Read(anchor)!.Value;
    }

    private async Task<Guid?> AnchorFileAsync(Guid pin)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == pin && m.IsMain).Select(m => m.AnchorFileId).SingleAsync();
    }

    private async Task ForgetSizeAsync(Guid file)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.StoredFiles.Where(f => f.Id == file).ExecuteUpdateAsync(u => u
            .SetProperty(f => f.PixelWidth, (int?)null).SetProperty(f => f.PixelHeight, (int?)null).SetProperty(f => f.Metadata, "{}"));
    }

    private async Task<DateTimeOffset> MapRevisionAsync(Guid map)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.RasterMaps.AsNoTracking().Where(m => m.Id == map).Select(m => m.UpdatedAt).SingleAsync();
    }

    private async Task RunPurgeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>().Single(h => h.Kind == ProcessingJobKinds.DocumentPurge);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.DocumentPurge }, CancellationToken.None);
    }

    private static byte[] Picture(MagickColor colour, uint width, uint height)
    {
        using var image = new MagickImage(colour, width, height);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static JsonElement LinkOf(JsonElement page, Guid id) =>
        page.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string url) => (await client.GetAsync(url)).StatusCode;

    private static DateTimeOffset Revision(JsonElement answer, string section, Guid id) =>
        DateTimeOffset.Parse(
            answer.GetProperty(section).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
                .GetProperty("revision").GetString()!);

    private static string? Code(JsonElement answer, string section)
    {
        var row = answer.GetProperty(section).EnumerateArray().Single();
        return row.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
    }

    private static async Task<JsonElement> RevertAsync(HttpClient client, Guid importBatchId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
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

    private async Task<object> NewMapAsync(
        Guid id, Guid caveId, string title, byte[]? picture = null, DateTimeOffset? baseRevision = null)
    {
        picture ??= Corpus.Jpeg();
        var staged = await StageAsync(uploader, "map.jpg", picture);
        return new
        {
            id,
            baseRevision,
            caveId,
            title,
            mapType = "planView",
            fileName = "map.jpg",
            sha256 = Convert.ToHexStringLower(SHA256.HashData(picture)),
            sizeBytes = picture.Length,
            stagedUploadId = staged,
        };
    }

    private static object Pin(Guid id, Guid mapId, Guid placeId, int x, int y, DateTimeOffset? baseRevision = null) =>
        new { id, rasterMapId = mapId, baseRevision, placeId, x, y };

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

    public async ValueTask DisposeAsync()
    {
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
