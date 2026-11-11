// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Survey stations pinned on a sheet's picture are offered as control points, each with where the
/// survey puts it — and only to a caller who may see the survey exactly.
/// </summary>
/// <remarks>
/// The queue's workers are taken out so a survey upload's reading job never runs: its stations are
/// written straight into the table, with invented names and positions, and the sheet's drawing job
/// is run by hand.
/// </remarks>
[Collection(RasterScratchCollection.Name)]
public sealed class GeoreferencedMapStationPointTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private long caveTypeId;

    public GeoreferencedMapStationPointTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gmp-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"gmp-out-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"gmp-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"gmp-out-{suffix}@t.local");
    }

    [Fact]
    public async Task Pinned_stations_are_control_points_for_callers_who_may_see_the_survey_exactly()
    {
        // A sheet, drawn.
        var form = new MultipartFormDataContent();
        var pdf = new ByteArrayContent(PdfSheetFixtures.PlainPdf());
        pdf.Headers.ContentType = new("application/pdf");
        form.Add(pdf, "file", "pinned.pdf");
        var upload = await owner.PostAsync("/api/v1/georeferenced-maps/", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var map = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var render = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.MapSheetRender)).First();
        await QueuedJob.RunAsync(factory.Services, render.Id);
        var drawn = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/georeferenced-maps/{map}");
        drawn.GetProperty("status").GetString().ShouldBe("unplaced");

        // A protected cave with a survey whose three stations are known.
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pinned Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cave = (await caveResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var model = await SurveyWithStationsAsync(cave, ("1", 24.0001, 45.5001), ("2", 24.0031, 45.5011), ("3", 24.0021, 45.5002));

        // The sheet's map is visible to everybody signed in, so only the survey decides.
        var put = await owner.PutAsJsonAsync($"/api/v1/georeferenced-maps/{map}", new
        {
            name = "Pinned sheet",
            description = (string?)null,
            mapKind = "caveMap",
            minZoom = (int?)null,
            maxZoom = (int?)null,
            attribution = (string?)null,
            defaultOpacity = 0.8,
            caveFeatureId = (Guid?)null,
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        // Pins on the sheet's preview picture for stations 1 and 2, and one for a station the
        // survey does not have.
        Guid documentId, previewId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.GeoreferencedMaps.SingleAsync(m => m.Id == map);
            previewId = row.PreviewFileId!.Value;
            documentId = await db.StoredFiles.Where(f => f.Id == row.SourceFileId)
                .Join(db.DocumentVersions, f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId).SingleAsync();
        }

        await PinAsync(documentId, previewId, model, "1", 0.1, 0.2);
        await PinAsync(documentId, previewId, model, "2", 0.8, 0.3);
        await PinAsync(documentId, previewId, model, "ghost", 0.5, 0.5);

        var points = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/georeferenced-maps/{map}/station-points");
        points.GetArrayLength().ShouldBe(2);
        var first = points.EnumerateArray().Single(p => p.GetProperty("label").GetString() == "1");
        first.GetProperty("u").GetDouble().ShouldBe(0.1);
        first.GetProperty("v").GetDouble().ShouldBe(0.2);
        first.GetProperty("x").GetDouble().ShouldBe(24.0001, 1e-9);
        first.GetProperty("y").GetDouble().ShouldBe(45.5001, 1e-9);

        // The outsider reads the map, but the survey's cave is protected: no stations for them.
        var withheld = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/georeferenced-maps/{map}/station-points");
        withheld.GetArrayLength().ShouldBe(0);
    }

    private async Task<Guid> SurveyWithStationsAsync(Guid cave, params (string Name, double Lon, double Lat)[] stations)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "pins.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{cave}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(stations.Select(s => new SurveyStation
        {
            SurveyModelId = modelId,
            Name = s.Name,
            SurveyName = null,
            Position = new Point(new CoordinateZ(s.Lon, s.Lat, 300)) { SRID = 4326 },
            Flags = SurveyStationFlags.Underground,
        }));
        await db.SaveChangesAsync();
        return modelId;
    }

    private async Task PinAsync(Guid documentId, Guid fileId, Guid surveyModelId, string station, double x, double y)
    {
        long relationTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            relationTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .ResLinkRelationTypes.Where(r => r.Code == ResLinkRelationTypeSeeds.MapStationPointCode)
                .Select(r => r.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)relationTypeId,
            description = (string?)null,
            members = new object[]
            {
                new
                {
                    targetType = "document", targetId = documentId, isMain = true, sortOrder = 0,
                    anchorKind = "imageRegion", anchor = new { shape = "point", x, y }, anchorFileId = fileId,
                },
                new
                {
                    targetType = "surveyModel", targetId = surveyModelId, isMain = false, sortOrder = 1,
                    anchorKind = "modelStation", anchor = new { station },
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
