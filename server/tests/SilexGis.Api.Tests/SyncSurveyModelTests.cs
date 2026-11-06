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
/// The survey-models stream: the line plots (<c>.lox</c>, <c>.3d</c>) of the caves a set hands a
/// phone with their exact position, their files through the address route under the same rule, and
/// the tombstones that tell a phone to drop one.
///
/// Every absence is asserted beside a presence over the same fixture. The uploader is an Editor in
/// the club and may place every cave here; the member is a Viewer in the same club who reads every
/// cave and places nothing guarded. The survey files are invented bytes, written straight into the
/// store, so no reading job decides anything for the test.
/// </summary>
public sealed class SyncSurveyModelTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!;
    private HttpClient member = null!;
    private HttpClient anonymous = null!;
    private Guid uploaderId;
    private Guid memberId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public SyncSurveyModelTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncmodels-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sm-up-{marker}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sm-mem-{marker}@t.local");
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

        uploader = await AuthHelper.BearerClientAsync(factory, $"sm-up-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"sm-mem-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_phone_is_handed_the_line_plots_of_caves_it_may_place_and_told_to_drop_the_rest()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var place = await CreatePlaceAsync(open);

        // Created in this order so the recorded page orders the same way every run.
        var withheld = await CreateModelAsync(guarded, SurveyModelFormat.Lox, "Galeria", current: true);
        var lox = await CreateModelAsync(open, SurveyModelFormat.Lox, "Sistemul", current: true);
        var survex = await CreateModelAsync(open, SurveyModelFormat.Survex3d, "Sistemul-3d", current: false);
        var mesh = await CreateModelAsync(open, SurveyModelFormat.Stl, "Pereti", current: true);

        var uploadSet = await CreateSetAsync(uploader, [open, guarded], clubId);
        var station = Guid.CreateVersion7();
        var mapped = await UploadAsync(uploader, uploadSet, new
        {
            placeStations = new[] { new { id = station, placeId = place, surveyModelId = lox.Id, station = "1.12" } },
        });
        mapped.GetProperty("placeStations")[0].GetProperty("status").GetString().ShouldBe("created");

        // Fixture proof: the member reads the guarded cave on the web and is withheld it by the
        // feature stream, which hands the uploader both caves.
        (await member.GetAsync($"/api/v1/caves/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberSet = await CreateSetAsync(member, [open, guarded]);
        FeatureIds(await GetPageAsync(member, $"/api/v1/sync/sets/{memberSet}/download")).ShouldNotContain(guarded);
        FeatureIds(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/download")).ShouldContain(guarded);

        var page = await new ContractFixture()
            .Literal(marker, "marker")
            .Name(memberSet, "set").Name(open, "cave").Name(place, "place")
            .Name(lox.Id, "lox-model").Name(lox.Document, "lox-document").Name(lox.Version, "lox-version")
            .Name(survex.Id, "survex-model").Name(survex.Document, "survex-document").Name(survex.Version, "survex-version")
            .Name(withheld.Id, "withheld-model").Name(station, "place-station")
            .Literal(lox.Sha256, "lox-sha256").Literal(survex.Sha256, "survex-sha256")
            .RecordGetAsync("31-survey-models-first-page", member, $"/api/v1/sync/sets/{memberSet}/survey-models");

        Ids(page, "models").ShouldBe([lox.Id, survex.Id]);
        Ids(page, "tombstones").ShouldBe([withheld.Id]);
        page.GetProperty("tombstones")[0].GetProperty("deleted").GetBoolean().ShouldBeTrue();
        Ids(page, "placeStations").ShouldBe([station]);
        page.GetRawText().ShouldNotContain(mesh.Id.ToString());
        page.GetRawText().ShouldNotContain(guarded.ToString());

        var shown = Row(page, "models", lox.Id);
        shown.GetProperty("caveId").GetGuid().ShouldBe(open);
        shown.GetProperty("documentId").GetGuid().ShouldBe(lox.Document);
        shown.GetProperty("versionId").GetGuid().ShouldBe(lox.Version);
        shown.GetProperty("name").GetString().ShouldBe("Sistemul");
        shown.GetProperty("format").GetString().ShouldBe("lox");
        shown.GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        shown.GetProperty("sizeBytes").GetInt64().ShouldBe(lox.Size);
        shown.GetProperty("sha256").GetString().ShouldBe(lox.Sha256);
        Row(page, "models", survex.Id).GetProperty("format").GetString().ShouldBe("3d");
        Row(page, "models", survex.Id).GetProperty("isCurrent").GetBoolean().ShouldBeFalse();
        var mapping = Row(page, "placeStations", station);
        mapping.GetProperty("placeId").GetGuid().ShouldBe(place);
        mapping.GetProperty("surveyModelId").GetGuid().ShouldBe(lox.Id);
        mapping.GetProperty("station").GetString().ShouldBe("1.12");
        mapping.TryGetProperty("documentId", out _).ShouldBeFalse();
        mapping.TryGetProperty("splay", out _).ShouldBeFalse();

        // The uploader, who may place both caves, is handed every line plot and told to drop none.
        var all = await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploadSet}/survey-models");
        Ids(all, "models").ShouldBe([withheld.Id, lox.Id, survex.Id], ignoreOrder: true);
        Ids(all, "tombstones").ShouldBeEmpty();

        (await anonymous.GetAsync($"/api/v1/sync/sets/{uploadSet}/survey-models")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await member.GetAsync($"/api/v1/sync/sets/{uploadSet}/survey-models")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var capabilities = await GetPageAsync(member, "/api/v1/sync/capabilities");
        capabilities.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldContain("surveyModels");
    }

    [Fact]
    public async Task A_model_that_goes_or_stops_being_handed_is_a_tombstone_and_a_new_current_model_brings_both_back()
    {
        var first = await CreateCaveAsync(locationProtected: false);
        var second = await CreateCaveAsync(locationProtected: false);
        var third = await CreateCaveAsync(locationProtected: false);
        var current = await CreateModelAsync(first, SurveyModelFormat.Lox, "Vechi", current: true);
        var next = await CreateModelAsync(first, SurveyModelFormat.Survex3d, "Nou", current: false);
        var onSecond = await CreateModelAsync(second, SurveyModelFormat.Lox, "Doi", current: true);
        var onThird = await CreateModelAsync(third, SurveyModelFormat.Lox, "Trei", current: true);
        var set = await CreateSetAsync(member, [first, second, third]);

        var start = await GetPageAsync(member, $"/api/v1/sync/sets/{set}/survey-models");
        Ids(start, "models").ShouldBe([current.Id, next.Id, onSecond.Id, onThird.Id], ignoreOrder: true);
        var before = Revisions(start);
        var cursor = start.GetProperty("nextCursor").GetString();

        // Making another model current changes two rows, and both come again with new revisions.
        var made = await uploader.PutAsync($"/api/v1/survey-models/{next.Id}/current", null);
        made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
        var moved = await GetPageAsync(member, $"/api/v1/sync/sets/{set}/survey-models?cursor={cursor}");
        Ids(moved, "models").ShouldBe([current.Id, next.Id], ignoreOrder: true);
        Row(moved, "models", next.Id).GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        Row(moved, "models", current.Id).GetProperty("isCurrent").GetBoolean().ShouldBeFalse();
        var after = Revisions(moved);
        after[next.Id].ShouldBeGreaterThan(before[next.Id]);
        after[current.Id].ShouldBeGreaterThan(before[current.Id]);
        cursor = moved.GetProperty("nextCursor").GetString();

        // Removed; its cave protected, so the member may no longer place it; its cave deleted.
        (await uploader.DeleteAsync($"/api/v1/survey-models/{current.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await writer.SetLocationProtectedAsync(second, true);
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
        }

        (await uploader.DeleteAsync($"/api/v1/caves/{third}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gone = await GetPageAsync(member, $"/api/v1/sync/sets/{set}/survey-models?cursor={cursor}");
        Ids(gone, "models").ShouldBeEmpty();
        Ids(gone, "tombstones").ShouldBe([current.Id, onSecond.Id, onThird.Id], ignoreOrder: true);
        gone.GetProperty("tombstones").EnumerateArray().ShouldAllBe(t => t.GetProperty("deleted").GetBoolean());

        // Caught up, a phone is told nothing again; the uploader, who may place the protected cave,
        // still holds its model.
        var level = await GetPageAsync(member, $"/api/v1/sync/sets/{set}/survey-models?cursor={gone.GetProperty("nextCursor").GetString()}");
        Ids(level, "tombstones").ShouldBeEmpty();
        var uploaderSet = await CreateSetAsync(uploader, [second]);
        Ids(await GetPageAsync(uploader, $"/api/v1/sync/sets/{uploaderSet}/survey-models"), "models").ShouldBe([onSecond.Id]);
    }

    [Fact]
    public async Task A_model_file_is_handed_only_to_a_caller_who_may_place_its_cave_whatever_its_document_says()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var visible = await CreateModelAsync(open, SurveyModelFormat.Lox, "Deschis", current: true);

        // The member owns this model's document, so the document's own rules admit it: only the
        // cave's exact position, closed to the member, keeps the file from it.
        var owned = await CreateModelAsync(guarded, SurveyModelFormat.Survex3d, "Ascuns", current: true, owner: memberId);
        (await member.GetAsync($"/api/v1/documents/{owned.Document}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var memberLinks = await LinksAsync(member, owned.Document, visible.Document);
        memberLinks.GetProperty("refused").EnumerateArray().Single().GetProperty("documentId").GetGuid().ShouldBe(owned.Document);
        memberLinks.GetProperty("refused")[0].GetProperty("code").GetString().ShouldBe("sync.document_unavailable");
        var link = memberLinks.GetProperty("links").EnumerateArray().Single();
        link.GetProperty("documentId").GetGuid().ShouldBe(visible.Document);
        var url = link.GetProperty("url").GetString()!;
        url.ShouldContain($"/api/v1/files/{visible.File}/content");
        var fetched = await anonymous.GetAsync(url);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        Convert.ToHexStringLower(SHA256.HashData(await fetched.Content.ReadAsByteArrayAsync())).ShouldBe(visible.Sha256);

        // A picture of a survey file is no picture.
        var rendition = await member.PostAsJsonAsync("/api/v1/sync/files/links", new
        {
            items = new object[] { new { documentId = visible.Document, rendition = 480 } },
        });
        (await rendition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refused")[0].GetProperty("code").GetString()
            .ShouldBe("sync.rendition_unavailable");

        // The uploader, who may place both caves and owns neither document, is handed both.
        var uploaderLinks = await LinksAsync(uploader, owned.Document, visible.Document);
        uploaderLinks.GetProperty("refused").GetArrayLength().ShouldBe(0);
        uploaderLinks.GetProperty("links").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_survey_file_a_phone_sends_as_survey_data_stays_a_document_and_makes_no_model()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var existing = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Existent", current: true);
        var set = await CreateSetAsync(uploader, [cave], clubId);

        var lox = Guid.CreateVersion7();
        var survex = Guid.CreateVersion7();
        var loxBytes = Encoding.UTF8.GetBytes($"invented lox upload {marker}");
        var survexBytes = Encoding.UTF8.GetBytes($"invented 3d upload {marker}");
        var answer = await UploadAsync(uploader, set, new
        {
            documents = new[]
            {
                new { id = lox, fileName = "sistem.lox", sha256 = Hash(loxBytes), sizeBytes = loxBytes.Length, stagedUploadId = await StageAsync(uploader, "sistem.lox", loxBytes) },
                new { id = survex, fileName = "sistem.3d", sha256 = Hash(survexBytes), sizeBytes = survexBytes.Length, stagedUploadId = await StageAsync(uploader, "sistem.3d", survexBytes) },
            },
            documentLinks = new[]
            {
                new { documentId = lox, target = new { kind = "feature", id = cave }, role = "surveyData" },
                new { documentId = survex, target = new { kind = "feature", id = cave }, role = "surveyData" },
            },
        });
        answer.GetProperty("documents").EnumerateArray().ShouldAllBe(r => r.GetProperty("status").GetString() == "created");
        answer.GetProperty("documentLinks").EnumerateArray().ShouldAllBe(r => r.GetProperty("status").GetString() == "created");

        // No model was made of either, and the one the cave had is still its current one.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var models = await db.SurveyModels.AsNoTracking().Where(m => m.CaveFeatureId == cave).ToListAsync();
            models.Select(m => m.Id).ShouldBe([existing.Id]);
            models.Single().IsCurrent.ShouldBeTrue();
        }

        // They reach phones as documents filed on the cave as survey data, and as nothing else.
        var documents = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/documents");
        documents.GetProperty("links").EnumerateArray()
            .Where(l => l.GetProperty("documentId").GetGuid() is var d && (d == lox || d == survex))
            .Select(l => l.GetProperty("role").GetString())
            .ShouldBe(["surveyData", "surveyData"]);
        Ids(await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models"), "models").ShouldBe([existing.Id]);
    }

    [Fact]
    public async Task Walked_a_row_or_two_at_a_time_the_stream_hands_every_model_mapping_and_tombstone_exactly_once()
    {
        // The page is cut across four halves — models, removed models, mappings and removed
        // mappings — each read separately, so the boundary between two halves is where a row
        // could be handed twice or fall between two pages.
        var cave = await CreateCaveAsync(locationProtected: false);
        var placeA = await CreatePlaceAsync(cave);
        var placeB = await CreatePlaceAsync(cave);
        var kept = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Pagina-1", current: true);
        var other = await CreateModelAsync(cave, SurveyModelFormat.Survex3d, "Pagina-2", current: false);
        var third = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Pagina-3", current: false);
        var removedModel = await CreateModelAsync(cave, SurveyModelFormat.Lox, "Pagina-4", current: false);
        var set = await CreateSetAsync(uploader, [cave], clubId);

        var onA = Guid.CreateVersion7();
        var onB = Guid.CreateVersion7();
        var removedStation = Guid.CreateVersion7();
        var mapped = await UploadAsync(uploader, set, new
        {
            placeStations = new[]
            {
                new { id = onA, placeId = placeA, surveyModelId = kept.Id, station = "1.1" },
                new { id = onB, placeId = placeB, surveyModelId = kept.Id, station = "1.2" },
                new { id = removedStation, placeId = placeA, surveyModelId = other.Id, station = "2.1" },
            },
        });
        mapped.GetProperty("placeStations").EnumerateArray().Select(r => r.GetProperty("status").GetString())
            .ShouldBe(["created", "created", "created"]);
        var stationRevision = Row(mapped, "placeStations", removedStation).GetProperty("revision").GetDateTimeOffset();

        (await uploader.DeleteAsync($"/api/v1/survey-models/{removedModel.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unmapped = await UploadAsync(uploader, set, new
        {
            placeStations = new[] { new { id = removedStation, baseRevision = stationRevision, deleted = true } },
        });
        unmapped.GetProperty("placeStations")[0].GetProperty("status").GetString().ShouldBe("deleted");

        var whole = await GetPageAsync(uploader, $"/api/v1/sync/sets/{set}/survey-models");
        Ids(whole, "models").ShouldBe([kept.Id, other.Id, third.Id], ignoreOrder: true);
        Ids(whole, "tombstones").ShouldBe([removedModel.Id]);
        Ids(whole, "placeStations").ShouldBe([onA, onB], ignoreOrder: true);
        Ids(whole, "placeStationTombstones").ShouldBe([removedStation]);

        string[] arrays = ["models", "tombstones", "placeStations", "placeStationTombstones"];
        foreach (var size in new[] { 1, 2 })
        {
            var seen = arrays.ToDictionary(a => a, _ => new List<Guid>());
            string? cursor = null;
            var pages = 0;
            while (true)
            {
                var url = $"/api/v1/sync/sets/{set}/survey-models?pageSize={size}"
                    + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
                var page = await GetPageAsync(uploader, url);
                pages++;
                pages.ShouldBeLessThan(20, "the walk must end");
                var count = 0;
                foreach (var array in arrays)
                {
                    var ids = Ids(page, array);
                    seen[array].AddRange(ids);
                    count += ids.Count;
                }

                count.ShouldBeLessThanOrEqualTo(size);
                cursor = page.GetProperty("nextCursor").GetString() ?? cursor;
                if (!page.GetProperty("hasMore").GetBoolean())
                {
                    break;
                }

                count.ShouldBe(size, "a page that says more follows is a full one");
            }

            foreach (var array in arrays)
            {
                seen[array].ShouldBe(Ids(whole, array), ignoreOrder: true, $"{array} at page size {size}");
                seen[array].Distinct().Count().ShouldBe(seen[array].Count, $"{array} at page size {size} handed a row twice");
            }

            // Level with the server: one more read from the last position hands nothing.
            var after = await GetPageAsync(
                uploader, $"/api/v1/sync/sets/{set}/survey-models?pageSize={size}&cursor={Uri.EscapeDataString(cursor!)}");
            foreach (var array in arrays)
            {
                Ids(after, array).ShouldBeEmpty($"{array} after the last page at size {size}");
            }

            after.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        }
    }

    // ---- reading ---------------------------------------------------------------------------------

    private static IReadOnlyList<Guid> Ids(JsonElement page, string array) =>
        [.. page.GetProperty(array).EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];

    private static JsonElement Row(JsonElement page, string array, Guid id) =>
        page.GetProperty(array).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    private static Dictionary<Guid, DateTimeOffset> Revisions(JsonElement page) =>
        page.GetProperty("models").EnumerateArray().ToDictionary(
            r => r.GetProperty("id").GetGuid(), r => DateTimeOffset.Parse(r.GetProperty("revision").GetString()!));

    private static IReadOnlyList<Guid> FeatureIds(JsonElement page) =>
        [.. page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> LinksAsync(HttpClient client, params Guid[] documents)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/files/links", new
        {
            items = documents.Select(d => new { documentId = d, rendition = "original" }).ToArray(),
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // ---- writing ---------------------------------------------------------------------------------

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid set, object sections)
    {
        var body = JsonSerializer.SerializeToNode(sections, JsonSerializerOptions.Web)!.AsObject();
        body["batchId"] = Guid.CreateVersion7();
        body["contractVersion"] = 1;
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", body);
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

    // ---- fixtures --------------------------------------------------------------------------------

    private sealed record Model(Guid Id, Guid Document, Guid Version, Guid File, long Size, string Sha256);

    /// <summary>
    /// A survey model of a cave with its file, as an upload leaves one once read: the bytes are
    /// invented, named for the model so a recording's sizes and hashes do not move between runs.
    /// </summary>
    private async Task<Model> CreateModelAsync(
        Guid caveId, SurveyModelFormat format, string name, bool current, Guid? owner = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var extension = format switch
        {
            SurveyModelFormat.Lox => ".lox",
            SurveyModelFormat.Survex3d => ".3d",
            _ => ".stl",
        };
        var bytes = Encoding.UTF8.GetBytes($"invented survey file {name}");
        string path;
        await using (var content = new MemoryStream(bytes))
        {
            path = await store.SaveAsync(content, extension);
        }

        var sha = Hash(bytes);
        var file = documents.Create(
            new StoredContent(path, name + extension, "application/octet-stream", bytes.Length, sha, FileKind.Survey),
            name,
            owner ?? uploaderId,
            owner ?? uploaderId).File;
        var model = new SurveyModel
        {
            CaveFeatureId = caveId,
            Name = name,
            FileId = file.Id,
            Format = format,
            IsCurrent = current,
            Status = SurveyModelStatus.Ready,
        };
        db.SurveyModels.Add(model);
        await db.SaveChangesAsync();
        var version = await db.StoredFiles.AsNoTracking().Where(f => f.Id == file.Id).Select(f => f.DocumentVersionId).SingleAsync();
        var document = await db.DocumentVersions.AsNoTracking().Where(v => v.Id == version).Select(v => v.DocumentId).SingleAsync();
        return new Model(model.Id, document, version, file.Id, bytes.Length, sha);
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
            name = $"Modele {Guid.NewGuid():N}"[..30],
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
