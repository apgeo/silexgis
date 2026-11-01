// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
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
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.PlaceStations;

namespace SilexGis.Api.Tests;

/// <summary>
/// A place a phone mapped to a station of a georeferenced survey is a link between the place and
/// the survey, and naming the station a place is at says where the place is. So the link is shown
/// whole or not at all, on every route that emits links: only to a reader who may read the place and
/// see it exactly, and who is shown the survey — a model only with its cave's exact position open, a
/// survey file by its own rules.
/// <para>
/// Every case is built where the rule every other link answers to would have shown something: the
/// installation reveals protected associations; the place is private to a club the reader is not in
/// and is under no protection; the model is of a cave whose exact position is closed to the reader;
/// the survey file is one the reader may not open; the place is deleted. The mappings are written
/// with the store directly, so cases the upload would refuse are covered too: the rule decides on
/// what is stored, however it got there. The uploader, who may read and place everything here, is
/// the positive control, so "hide every mapping" fails too.
/// </para>
/// </summary>
public sealed class PlaceStationProtectionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
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

    public PlaceStationProtectionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-placestationprot-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"psp-up-{marker}@t.local");
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"psp-rd-{marker}@t.local");
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

        uploader = await AuthHelper.BearerClientAsync(factory, $"psp-up-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"psp-rd-{marker}@t.local");
    }

    [Fact]
    public async Task A_place_station_is_shown_whole_or_not_at_all_on_every_route_that_emits_links()
    {
        await SetRevealAsync(true);
        var cave = await CreateCaveAsync(locationProtected: false);
        var guardedCave = await CreateCaveAsync(locationProtected: true);
        var clubCave = await CreateCaveAsync(locationProtected: false, clubOnly: true);
        var open = await CreatePlaceAsync(cave);
        var guarded = await CreatePlaceAsync(cave, locationProtected: true);
        var clubOnly = await CreatePlaceAsync(clubCave, clubOnly: true);
        var deleted = await CreatePlaceAsync(cave);
        var model = await CreateModelAsync(cave, "Sistemul");
        var closedModel = await CreateModelAsync(guardedCave, "Ascuns");
        var openFile = await CreateDocumentAsync(Visibility.Authenticated);
        var closedFile = await CreateDocumentAsync(Visibility.Private);

        var shown = await MapAsync(open, model: model);            // (v) every reader here is shown it
        var onFile = await MapAsync(open, document: openFile);     // (v) a survey file the reader may open
        var onGuarded = await MapAsync(guarded, model: model);     // (i)
        var onClubOnly = await MapAsync(clubOnly, model: model);   // (ii)
        var onClosedModel = await MapAsync(open, model: closedModel); // (iii)
        var onClosedFile = await MapAsync(open, document: closedFile); // (iv)
        var onDeleted = await MapAsync(deleted, model: model);     // (vi)
        (await uploader.DeleteAsync($"/api/v1/features/{deleted}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Fixture proof: the reader reads the open and the guarded place, the open file and the
        // closed model's cave; it reads neither the club-only place nor the closed file, and may not
        // place the guarded place or the closed model's cave. The uploader reads them all.
        (await reader.GetAsync($"/api/v1/features/{guarded}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/caves/{guardedCave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/features/{clubOnly}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/documents/{openFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/documents/{closedFile}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/survey-models/{closedModel}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync($"/api/v1/features/{clubOnly}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/documents/{closedFile}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/survey-models/{closedModel}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var hiddenFromReader = new[] { onGuarded, onClubOnly, onClosedModel, onClosedFile, onDeleted };

        // The open place's list: items and the total the badge shows.
        var readerPlace = await PanelAsync(reader, "feature", open);
        LinkIds(readerPlace).ShouldBe([shown, onFile], ignoreOrder: true);
        readerPlace.GetProperty("totalItems").GetInt32().ShouldBe(2);
        var uploaderPlace = await PanelAsync(uploader, "feature", open);
        LinkIds(uploaderPlace).ShouldBe([shown, onFile, onClosedModel, onClosedFile], ignoreOrder: true);
        uploaderPlace.GetProperty("totalItems").GetInt32().ShouldBe(4);

        // The model's list: the reader is shown the open place's station only.
        var readerModel = await PanelAsync(reader, "surveyModel", model);
        LinkIds(readerModel).ShouldBe([shown]);
        readerModel.GetProperty("totalItems").GetInt32().ShouldBe(1);
        LinkIds(await PanelAsync(uploader, "surveyModel", model)).ShouldBe([shown, onGuarded, onClubOnly], ignoreOrder: true);

        // The guarded place's own list is empty for the reader, and counts nothing.
        var readerGuarded = await PanelAsync(reader, "feature", guarded);
        LinkIds(readerGuarded).ShouldBeEmpty();
        readerGuarded.GetProperty("totalItems").GetInt32().ShouldBe(0);
        LinkIds(await PanelAsync(uploader, "feature", guarded)).ShouldBe([onGuarded]);

        // By id and by short code.
        foreach (var link in hiddenFromReader)
        {
            (await reader.GetAsync($"/api/v1/reslinks/{link}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, link.ToString());
            var byCode = await reader.GetAsync($"/api/v1/reslinks/{await ShortCodeAsync(link)}");
            byCode.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(byCode)).ShouldBe("reslink.code.unresolved");
        }

        (await reader.GetAsync($"/api/v1/reslinks/{shown}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/reslinks/{await ShortCodeAsync(onFile)}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/reslinks/{onClubOnly}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.GetAsync($"/api/v1/reslinks/{onDeleted}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "its place is deleted");

        // Every generic write answers a hidden mapping as one that is not there, before anything else.
        foreach (var link in hiddenFromReader)
        {
            var member = await AnyMemberAsync(link);
            foreach (var response in new[]
                     {
                         await reader.PatchAsJsonAsync($"/api/v1/reslinks/{link}", new { description = "changed" }),
                         await reader.PostAsJsonAsync($"/api/v1/reslinks/{link}/members", new { targetType = "feature", targetId = open }),
                         await reader.PatchAsJsonAsync($"/api/v1/reslinks/{link}/members/{member}", new { isMain = true, sortOrder = 3 }),
                         await reader.DeleteAsync($"/api/v1/reslinks/{link}/members/{member}"),
                         await reader.DeleteAsync($"/api/v1/reslinks/{link}"),
                     })
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound, link.ToString());
                (await ProblemCodeAsync(response)).ShouldBe("reslink.not_found");
            }
        }

        // A place's timeline names the link its member rows belong to only to a reader who may place it.
        (await GetPageAsync(reader, $"/api/v1/history?entityType=feature&entityId={guarded}&pageSize=100"))
            .GetRawText().ShouldNotContain(onGuarded.ToString());
    }

    // ---- reading -------------------------------------------------------------------------------

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static Task<JsonElement> PanelAsync(HttpClient client, string type, Guid id) =>
        GetPageAsync(client, $"/api/v1/reslinks/for-target?type={type}&id={id}&pageSize=100&relation=place-at-station");

    private static IReadOnlyList<Guid> LinkIds(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())];

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(payload) ? null
            : JsonDocument.Parse(payload).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
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

    // ---- fixtures ------------------------------------------------------------------------------

    /// <summary>A mapping written by the store, as a phone's upload or a review leaves one.</summary>
    private async Task<Guid> MapAsync(Guid place, Guid? model = null, Guid? document = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = Guid.CreateVersion7();
        await new PlaceStationStore(db).AddAsync(id, place, model, document, "1.4", document is null ? null : [1, 2, 3], uploaderId);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A survey model of a cave with an invented file.</summary>
    private async Task<Guid> CreateModelAsync(Guid caveId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await StoreSurveyFileAsync(scope, name, Visibility.Private);
        var model = new SurveyModel
        {
            CaveFeatureId = caveId, Name = name, FileId = file, Format = SurveyModelFormat.Lox, Status = SurveyModelStatus.Ready,
        };
        db.SurveyModels.Add(model);
        await db.SaveChangesAsync();
        return model.Id;
    }

    /// <summary>
    /// A survey file as a document hung on nothing: one every signed-in account may open by its own
    /// rules, or one only its owner may open.
    /// </summary>
    private async Task<Guid> CreateDocumentAsync(Visibility visibility)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await StoreSurveyFileAsync(scope, "telefon", visibility);
        await db.SaveChangesAsync();
        return await (from f in db.StoredFiles.AsNoTracking()
                      join v in db.DocumentVersions.AsNoTracking() on f.DocumentVersionId equals v.Id
                      where f.Id == file
                      select v.DocumentId).SingleAsync();
    }

    private async Task<Guid> StoreSurveyFileAsync(AsyncServiceScope scope, string name, Visibility visibility)
    {
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var bytes = Encoding.UTF8.GetBytes($"invented survey file {name} {Guid.NewGuid():N}");
        string path;
        await using (var content = new MemoryStream(bytes))
        {
            path = await store.SaveAsync(content, ".lox");
        }

        var created = documents.Create(
            new StoredContent(path, name + ".lox", "application/octet-stream", bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), FileKind.Survey),
            name,
            uploaderId,
            uploaderId);
        var document = db.Documents.Local.Single(d => d.Id == created.Version.DocumentId);
        document.Visibility = visibility;
        await db.SaveChangesAsync();
        return created.File.Id;
    }

    /// <summary>A cave every account reads — or, club-only, one only the second club reads.</summary>
    private async Task<Guid> CreateCaveAsync(bool locationProtected, bool clubOnly = false)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pestera {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = clubOnly ? "cavingGroup" : "authenticated",
            cavingGroupId = clubOnly ? otherClubId : (Guid?)null,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

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
    /// Flips the installation's reveal setting; <see cref="DisposeAsync"/> takes it away again, so no
    /// other class tests under it.
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
