// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Places a phone maps to stations of a survey — a server survey model, or a survey file the phone
/// uploaded itself — or to the far end of a splay from one: the upload's <c>placeStations</c>
/// section, the mappings on the survey-models stream, review, undo, and the browser's lists.
///
/// The uploader is an Editor in the club and may change and place everything here; the member is a
/// Viewer in the same club who reads every cave, places nothing guarded and changes nothing. Survey
/// files are invented bytes.
/// </summary>
public sealed class PlaceStationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!;
    private HttpClient member = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public PlaceStationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-placestations-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ps-up-{marker}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ps-mem-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ps-adm-{marker}@t.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Club {marker}", Slug = $"club-{marker}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
            await RosterHelper.AddMemberAsync(db, clubId, uploaderId);
            await RosterHelper.AddMemberAsync(db, clubId, memberId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"ps-up-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"ps-mem-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"ps-adm-{marker}@t.local");
    }

    [Fact]
    public async Task A_phone_maps_places_to_stations_of_models_and_of_its_own_survey_files_and_reads_them_back()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var otherCave = await CreateCaveAsync(locationProtected: false);
        var guardedCave = await CreateCaveAsync(locationProtected: true);
        var first = await CreatePlaceAsync(cave);
        var second = await CreatePlaceAsync(cave);
        var elsewhere = await CreatePlaceAsync(otherCave);
        var guardedPlace = await CreatePlaceAsync(guardedCave);
        var model = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Sistemul");
        var guardedModel = await CreateModelAsync(guardedCave, SurveyModelFormat.Lox, "Ascuns");
        var mesh = await CreateModelAsync(cave, SurveyModelFormat.Stl, "Pereti");
        var set = await CreateSetAsync(uploader, [cave, otherCave, guardedCave], clubId);
        var survey = await UploadSurveyFileAsync(set, cave);
        var otherSurvey = await UploadSurveyFileAsync(set, otherCave);

        var atStation = Guid.CreateVersion7();
        var atSplay = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var body = new
        {
            batchId,
            contractVersion = 1,
            placeStations = new object[]
            {
                new { id = atStation, placeId = first, surveyModelId = model, station = "1.12" },
                new { id = atSplay, placeId = second, documentId = survey, station = "A.3", splay = new[] { 1.5, -2, 0.25 } },
            },
        };
        var answer = await new ContractFixture()
            .Literal(marker, "marker")
            .Name(set, "set").Name(batchId, "batch").Name(first, "place").Name(second, "other-place")
            .Name(model, "survey-model").Name(survey, "survey-document")
            .Name(atStation, "place-station").Name(atSplay, "place-splay")
            .RecordPostAsync("32-upload-place-stations", uploader, $"/api/v1/sync/sets/{set}/upload", body);
        Statuses(answer).ShouldBe(["created", "created"]);
        answer.GetProperty("placeStations").EnumerateArray().ShouldAllBe(r => r.GetProperty("revision").ValueKind == JsonValueKind.String);

        var page = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models");
        var station = Row(page, "placeStations", atStation);
        station.GetProperty("placeId").GetGuid().ShouldBe(first);
        station.GetProperty("surveyModelId").GetGuid().ShouldBe(model);
        station.GetProperty("station").GetString().ShouldBe("1.12");
        station.TryGetProperty("splay", out _).ShouldBeFalse();
        var splay = Row(page, "placeStations", atSplay);
        splay.GetProperty("documentId").GetGuid().ShouldBe(survey);
        splay.TryGetProperty("surveyModelId", out _).ShouldBeFalse();
        splay.GetProperty("splay").EnumerateArray().Select(v => v.GetDouble()).ShouldBe([1.5, -2, 0.25]);
        splay.GetProperty("revision").GetString().ShouldBe(answer.GetProperty("placeStations")[1].GetProperty("revision").GetString());

        // Refusals: a second station of a place in one survey; an identifier another mapping holds;
        // a survey that is not here, or is a mesh; a place outside the survey's cave or outside the
        // cave a survey file is filed on.
        var refused = await UploadAsync(uploader, set, new object[]
        {
            new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = model, station = "1.13" },
            new { id = atStation, placeId = second, surveyModelId = model, station = "1.12" },
            new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = Guid.CreateVersion7(), station = "1" },
            new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = mesh, station = "1" },
            new { id = Guid.CreateVersion7(), placeId = elsewhere, surveyModelId = model, station = "1" },
            new { id = Guid.CreateVersion7(), placeId = first, documentId = otherSurvey, station = "1" },
        });
        Codes(refused).ShouldBe(
        [
            "sync.place_station_exists", "sync.id_conflict", "sync.survey_not_found", "sync.survey_not_found",
            "sync.place_not_found", "sync.place_not_found",
        ]);

        // The member may change no place, and is not shown the model of a cave it may not place.
        var memberSet = await CreateSetAsync(member, [cave, guardedCave]);
        Codes(await UploadAsync(member, memberSet, new object[]
        {
            new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = model, station = "2" },
            new { id = Guid.CreateVersion7(), placeId = guardedPlace, surveyModelId = guardedModel, station = "2" },
        })).ShouldBe(["sync.row_forbidden", "sync.survey_not_found"]);

        // A mapping in the guarded cave reaches the uploader and not the member.
        var guardedStation = Guid.CreateVersion7();
        Statuses(await UploadAsync(uploader, set, new object[]
        {
            new { id = guardedStation, placeId = guardedPlace, surveyModelId = guardedModel, station = "9" },
        })).ShouldBe(["created"]);
        var memberPage = await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/survey-models");
        Ids(memberPage, "placeStations").ShouldBe([atStation, atSplay], ignoreOrder: true);
        memberPage.GetRawText().ShouldNotContain(guardedStation.ToString());
        Ids(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "placeStations")
            .ShouldBe([atStation, atSplay, guardedStation], ignoreOrder: true);

        // The validator: exactly one survey, a station, a splay of three numbers.
        var invalid = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            placeStations = new object[]
            {
                new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = model, documentId = survey, station = "1" },
                new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = model, station = "1", splay = new[] { 1.0, 2.0 } },
                new { id = Guid.CreateVersion7(), placeId = first, surveyModelId = model, station = "" },
            },
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_mapping_is_changed_and_removed_on_its_revision_and_its_removal_is_told()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var place = await CreatePlaceAsync(cave);
        var model = await CreateModelAsync(cave, SurveyModelFormat.Survex3d, "Sistemul");
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var id = Guid.CreateVersion7();
        var created = await UploadAsync(uploader, set, new object[] { new { id, placeId = place, surveyModelId = model, station = "sys.4" } });
        var revision = Revision(created);

        var start = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models");
        var cursor = start.GetProperty("nextCursor").GetString();

        var changed = await UploadAsync(uploader, set, new object[]
        {
            new { id, baseRevision = revision, placeId = place, surveyModelId = model, station = "sys.4", splay = new[] { 0.5, 0.5, -1.0 } },
        });
        Statuses(changed).ShouldBe(["updated"]);
        Revision(changed).ShouldBeGreaterThan(revision);
        var moved = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models?cursor={cursor}");
        Row(moved, "placeStations", id).GetProperty("splay").EnumerateArray().Select(v => v.GetDouble()).ShouldBe([0.5, 0.5, -1.0]);
        cursor = moved.GetProperty("nextCursor").GetString();

        // On the revision it read, and nothing else; a mapping stays with its survey.
        Codes(await UploadAsync(uploader, set, new object[]
        {
            new { id, baseRevision = revision, placeId = place, surveyModelId = model, station = "sys.5" },
        })).ShouldBe(["sync.conflict"]);
        Codes(await UploadAsync(uploader, set, new object[]
        {
            new { id, baseRevision = Revision(changed), placeId = place, documentId = Guid.CreateVersion7(), station = "sys.5" },
        })).ShouldBe(["sync.id_conflict"]);

        var removed = await UploadAsync(uploader, set, new object[] { new { id, baseRevision = Revision(changed), deleted = true } });
        Statuses(removed).ShouldBe(["deleted"]);
        var told = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models?cursor={cursor}");
        Ids(told, "placeStations").ShouldBeEmpty();
        Ids(told, "placeStationTombstones").ShouldBe([id]);
        told.GetProperty("placeStationTombstones")[0].GetProperty("deleted").GetBoolean().ShouldBeTrue();

        Codes(await UploadAsync(uploader, set, new object[]
        {
            new { id, baseRevision = Revision(changed), placeId = place, surveyModelId = model, station = "sys.6" },
        })).ShouldBe(["sync.row_deleted"]);
        Statuses(await UploadAsync(uploader, set, new object[] { new { id, baseRevision = Revision(changed), deleted = true } }))
            .ShouldBe(["unchanged"]);
    }

    [Fact]
    public async Task A_held_mapping_waits_for_a_reviewer_and_undoing_uploads_takes_their_mappings_back()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var place = await CreatePlaceAsync(cave);
        var model = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Sistemul");
        var set = await CreateSetAsync(uploader, [cave], clubId);
        await MarkAsync(true);

        var id = Guid.CreateVersion7();
        var held = await UploadAsync(uploader, set, new object[] { new { id, placeId = place, surveyModelId = model, station = "7" } }, held: true);
        Statuses(held).ShouldBe(["held"]);
        (await LiveLinkAsync(id)).ShouldBeFalse();
        var reviewId = held.GetProperty("placeStations")[0].GetProperty("reviewId").GetGuid();
        var batch = held.GetProperty("importBatchId").GetGuid();

        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batch}");
        var line = detail.GetProperty("lines").EnumerateArray().SingleOrDefault(l => l.GetProperty("reviewId").GetGuid() == reviewId);
        line.ValueKind.ShouldBe(JsonValueKind.Object, detail.GetRawText());
        line.GetProperty("section").GetString().ShouldBe("placeStations");
        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        (await LiveLinkAsync(id)).ShouldBeTrue();
        var told = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{set}/reviews");
        var decision = told.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("reviewId").GetGuid() == reviewId);
        decision.GetProperty("entity").GetString().ShouldBe("placeStations");
        decision.GetProperty("outcome").GetString().ShouldBe("accepted");
        decision.GetProperty("revision").ValueKind.ShouldBe(JsonValueKind.String);
        await MarkAsync(false);

        // An edit taken back puts the station back; a removal taken back brings the mapping back.
        var revision = DateTimeOffset.Parse(decision.GetProperty("revision").GetString()!);
        var edit = await UploadAsync(uploader, set, new object[] { new { id, baseRevision = revision, placeId = place, surveyModelId = model, station = "8" } });
        await RevertAsync(edit.GetProperty("importBatchId").GetGuid());
        Row(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "placeStations", id)
            .GetProperty("station").GetString().ShouldBe("7");

        var current = DateTimeOffset.Parse(Row(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "placeStations", id)
            .GetProperty("revision").GetString()!);
        var removal = await UploadAsync(uploader, set, new object[] { new { id, baseRevision = current, deleted = true } });
        (await LiveLinkAsync(id)).ShouldBeFalse();
        await RevertAsync(removal.GetProperty("importBatchId").GetGuid());
        (await LiveLinkAsync(id)).ShouldBeTrue();

        // And the accepted creation taken back removes the mapping, which the phones are told.
        var undo = await RevertAsync(batch);
        undo.GetProperty("items").EnumerateArray()
            .Single(i => i.TryGetProperty("placeStationId", out var named) && named.ValueKind == JsonValueKind.String)
            .GetProperty("outcome").GetString().ShouldBe("undone");
        (await LiveLinkAsync(id)).ShouldBeFalse();
        Ids(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "placeStationTombstones").ShouldBe([id]);
    }

    [Fact]
    public async Task The_browser_lists_a_mapping_and_removes_it_for_the_phones_and_never_makes_or_edits_one()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var place = await CreatePlaceAsync(cave);
        var other = await CreatePlaceAsync(cave);
        var model = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Sistemul");
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var id = Guid.CreateVersion7();
        Statuses(await UploadAsync(uploader, set, new object[] { new { id, placeId = place, surveyModelId = model, station = "5" } }))
            .ShouldBe(["created"]);

        // In the place's list and the model's list, removable by whoever may change the place.
        var onPlace = await PanelAsync(uploader, "feature", place);
        Ids(onPlace, "items").ShouldBe([id]);
        onPlace.GetProperty("items")[0].GetProperty("mayEdit").GetBoolean().ShouldBeTrue();
        Ids(await PanelAsync(uploader, "surveyModel", model), "items").ShouldBe([id]);
        var seen = await PanelAsync(member, "feature", place);
        Ids(seen, "items").ShouldBe([id]);
        seen.GetProperty("items")[0].GetProperty("mayEdit").GetBoolean().ShouldBeFalse();

        // Every side door is refused, to the uploader too.
        var placeMember = await PlaceMemberAsync(id);
        foreach (var response in new[]
                 {
                     await uploader.PatchAsJsonAsync($"/api/v1/reslinks/{id}", new { description = "changed" }),
                     await uploader.PostAsJsonAsync($"/api/v1/reslinks/{id}/members", new { targetType = "feature", targetId = other }),
                     await uploader.PatchAsJsonAsync($"/api/v1/reslinks/{id}/members/{placeMember}", new { isMain = true, sortOrder = 3 }),
                     await uploader.DeleteAsync($"/api/v1/reslinks/{id}/members/{placeMember}"),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(response)).ShouldBe("reslink.place_station.fixed");
        }

        var relation = await RelationIdAsync("place-at-station");
        var made = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = relation,
            members = new object[]
            {
                new { targetType = "feature", targetId = other, isMain = true },
                new { targetType = "surveyModel", targetId = model, isMain = false, anchorKind = "modelStation", anchor = new { station = "6" } },
            },
        });
        made.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(made)).ShouldBe("reslink.place_station.fixed");

        var plain = await uploader.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = await RelationIdAsync("related-to"),
            members = new object[] { new { targetType = "feature", targetId = other }, new { targetType = "surveyModel", targetId = model } },
        });
        plain.StatusCode.ShouldBe(HttpStatusCode.Created, await plain.Content.ReadAsStringAsync());
        var plainId = (await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var turned = await uploader.PatchAsJsonAsync($"/api/v1/reslinks/{plainId}", new { relationTypeId = relation });
        turned.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(turned)).ShouldBe("reslink.place_station.fixed");

        // The ordinary delete removes it the way a phone does: gone from the lists, told as a tombstone.
        (await member.DeleteAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await uploader.DeleteAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await uploader.GetAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Ids(await PanelAsync(uploader, "feature", place), "items").ShouldBeEmpty();
        Ids(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "placeStationTombstones").ShouldBe([id]);
    }

    // ---- reading -------------------------------------------------------------------------------

    [Fact]
    public async Task Two_overlapping_edits_of_one_mapping_from_one_revision_land_once_and_the_other_is_a_conflict()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var placeA = await CreatePlaceAsync(cave);
        var placeB = await CreatePlaceAsync(cave);
        var model = await CreateModelAsync(cave, SurveyModelFormat.Survex3d, "Sistemul");
        var set = await CreateSetAsync(uploader, [cave], clubId);
        var contested = Guid.CreateVersion7();
        var stallId = Guid.CreateVersion7();
        var created = await UploadAsync(uploader, set, new object[]
        {
            new { id = contested, placeId = placeA, surveyModelId = model, station = "sys.1" },
            new { id = stallId, placeId = placeB, surveyModelId = model, station = "sys.2" },
        });
        var revisions = created.GetProperty("placeStations").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetGuid(), r => r.GetProperty("revision").GetDateTimeOffset());

        // A mapping's edit takes its place first and then the mapping: the first upload parks on
        // the stall mapping another connection holds, the second waits behind the first.
        await using var stall = await RowStall.HoldAsync(factory, "res_links", stallId);
        var firstUpload = PostAsync(uploader, set, new object[]
        {
            new { id = contested, baseRevision = revisions[contested], placeId = placeA, surveyModelId = model, station = "sys.first" },
            new { id = stallId, baseRevision = revisions[stallId], placeId = placeB, surveyModelId = model, station = "sys.2b" },
        });
        await stall.WaitForWaitersAsync(1, firstUpload);
        var secondUpload = PostAsync(uploader, set, new object[]
        {
            new { id = contested, baseRevision = revisions[contested], placeId = placeA, surveyModelId = model, station = "sys.second" },
        });
        await stall.WaitForWaitersAsync(2, secondUpload);
        await stall.ReleaseAsync();

        var won = await AnswerOf(await firstUpload);
        var lost = await AnswerOf(await secondUpload);
        Statuses(won).ShouldBe(["updated", "updated"]);
        Statuses(lost).ShouldBe(["conflict"]);
        Revision(lost).ShouldBe(Revision(won));

        var page = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models");
        Row(page, "placeStations", contested).GetProperty("station").GetString().ShouldBe("sys.first");
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid set, IReadOnlyList<object> stations) =>
        client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            placeStations = stations,
        });

    private static async Task<JsonElement> AnswerOf(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static IReadOnlyList<Guid> Ids(JsonElement page, string array) =>
        [.. page.GetProperty(array).EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];

    private static JsonElement Row(JsonElement page, string array, Guid id) =>
        page.GetProperty(array).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    private static IReadOnlyList<string?> Statuses(JsonElement answer) =>
        [.. answer.GetProperty("placeStations").EnumerateArray().Select(r => r.GetProperty("status").GetString())];

    private static IReadOnlyList<string?> Codes(JsonElement answer) =>
        [.. answer.GetProperty("placeStations").EnumerateArray()
            .Select(r => r.TryGetProperty("code", out var code) ? code.GetString() : null)];

    private static DateTimeOffset Revision(JsonElement answer) =>
        DateTimeOffset.Parse(answer.GetProperty("placeStations")[0].GetProperty("revision").GetString()!);

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static Task<JsonElement> PanelAsync(HttpClient client, string type, Guid id) =>
        GetPageAsync(client, $"/api/v1/reslinks/for-target?type={type}&id={id}&pageSize=100&relation=place-at-station");

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(payload) ? null
            : JsonDocument.Parse(payload).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<bool> LiveLinkAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinks.AsNoTracking().AnyAsync(l => l.Id == id && l.DeletedAt == null);
    }

    private async Task<Guid> PlaceMemberAsync(Guid link)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkMembers.AsNoTracking().Where(m => m.ResLinkId == link && m.FeatureId != null).Select(m => m.Id).SingleAsync();
    }

    private async Task<long> RelationIdAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkRelationTypes.AsNoTracking().Where(r => r.Code == code).Select(r => r.Id).SingleAsync();
    }

    // ---- writing -------------------------------------------------------------------------------

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid set, IReadOnlyList<object> stations, bool held = false)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = held ? new[] { "held" } : null,
            placeStations = stations,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>A survey file the uploader's phone sent and filed on the cave as survey data.</summary>
    private async Task<Guid> UploadSurveyFileAsync(Guid set, Guid caveId)
    {
        var id = Guid.CreateVersion7();
        var bytes = Encoding.UTF8.GetBytes($"invented phone survey {id}");
        var staged = await StageAsync(uploader, "telefon.lox", bytes);
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { new { id, fileName = "telefon.lox", sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), sizeBytes = bytes.Length, stagedUploadId = staged } },
            documentLinks = new[] { new { documentId = id, target = new { kind = "feature", id = caveId }, role = "surveyData" } },
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("documentLinks")[0].GetProperty("status").GetString().ShouldBe("created", payload);
        return id;
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

    private async Task<JsonElement> RevertAsync(Guid importBatchId)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync($"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ---- fixtures ------------------------------------------------------------------------------

    /// <summary>A survey model of a cave with an invented file, as an upload leaves one once read.</summary>
    private async Task<Guid> CreateModelAsync(Guid caveId, SurveyModelFormat format, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var bytes = Encoding.UTF8.GetBytes($"invented survey file {name} {Guid.NewGuid():N}");
        string path;
        await using (var content = new MemoryStream(bytes))
        {
            path = await store.SaveAsync(content, ".lox");
        }

        var file = documents.Create(
            new StoredContent(path, name + ".lox", "application/octet-stream", bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), FileKind.Survey),
            name,
            uploaderId,
            uploaderId).File;
        var model = new SurveyModel { CaveFeatureId = caveId, Name = name, FileId = file.Id, Format = format, Status = SurveyModelStatus.Ready };
        db.SurveyModels.Add(model);
        await db.SaveChangesAsync();
        return model.Id;
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

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Statii {Guid.NewGuid():N}"[..30],
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

    private async Task<Guid> CreatePlaceAsync(Guid caveId)
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
            Visibility = Visibility.Authenticated,
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
