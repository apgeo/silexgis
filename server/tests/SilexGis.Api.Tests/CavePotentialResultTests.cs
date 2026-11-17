// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaxRev.Gdal.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Potential;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Runs of cave-potential models: a run queued, computed into a map and recorded with what it
/// read; and who may read the result — which, for a map weighed from recorded positions, is only
/// an account that may place every one of them.
/// </summary>
/// <remarks>
/// Every test works in a box of open ocean of its own, far enough from the others that no layer of
/// one reads a feature of another. Rasters are generated here; nothing is real ground.
/// </remarks>
[Collection(RasterScratchCollection.Name)]
public sealed class CavePotentialResultTests : IAsyncLifetime, IDisposable
{
    private const string Url = "/api/v1/cave-potential/results";
    private const string ModelsUrl = "/api/v1/cave-potential/models";
    private const double North = -48.9;
    private const double Pixel = 0.001;
    private const double South = North - (16 * Pixel);
    private const double Latitude = North - 0.008;

    // One box per test, a third of a degree apart: some twenty kilometres at this latitude, four
    // times as far as any layer reaches for its features.
    private const double TerrainBox = 83.0;
    private const double ProtectedBox = 83.3;
    private const double RefusalBox = 83.6;
    private const double FailureBox = 83.9;
    private const double HiddenAreaBox = 84.2;
    private const double SharedModelBox = 84.5;
    private const double NoTerrainBox = 84.8;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient withoutTerrain = null!;
    private Guid buildId;
    private long sinkholeTypeId;
    private long karstAreaTypeId;

    public CavePotentialResultTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-potres-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pr-own-{suffix}@t.local");
        // A viewer reads what everyone signed in may read and holds no grant to place a protected
        // position: the reader a map made from one has to stay out of reach of.
        var viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pr-read-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pr-dry-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var id in new[] { ownerId, viewerId })
            {
                db.AccessEntries.Add(new AccessEntry
                {
                    SubjectKind = AccessSubjectKind.User,
                    SubjectId = id,
                    Domain = AccessDomain.Terrain,
                    Actions = AccessAction.Read,
                    Effect = AccessEffect.Allow,
                    ScopeKind = AccessScopeKind.All,
                });
            }

            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == FeatureTypeSeeds.Sinkhole).Select(t => t.Id).SingleAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == FeatureTypeSeeds.KarstArea).Select(t => t.Id).SingleAsync();

            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
            var build = new TerrainBuild
            {
                Extent = Rectangle(TerrainBox),
                RequestedMaxDepth = 13,
                Status = TerrainBuildStatus.Queued,
                Phase = TerrainBuildPhase.Pending,
                HeightDatum = TerrainHeightDatum.Orthometric,
                GeoidHeightM = 0,
                IsActive = true,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            buildId = build.Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"pr-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pr-read-{suffix}@t.local");
        withoutTerrain = await AuthHelper.BearerClientAsync(factory, $"pr-dry-{suffix}@t.local");
    }

    // ---------- who may ask at all ----------

    [Fact]
    public async Task An_anonymous_caller_is_turned_away_and_an_account_without_the_terrain_right_is_refused()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();
        var run = new { modelId = id, areaFeatureId = id, cellMeters = 50 };

        (await anonymous.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Url, run)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // The map's own bytes answer without a session, and only to a token.
        (await anonymous.GetAsync($"{Url}/{id}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"{Url}/{id}/content?token=nonsense")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Signed in, but holding no right to read the terrain: refused, the same for an id that
        // names a result and one that names nothing.
        (await withoutTerrain.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutTerrain.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutTerrain.PostAsJsonAsync(Url, run)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The same account's sibling, holding the right, gets an answer.
        (await viewer.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- a run over the ground alone ----------

    [Fact]
    public async Task A_run_over_the_ground_alone_writes_a_dated_map_anyone_with_the_terrain_right_may_read()
    {
        // Slope rises two degrees a column from west to east; the model calls thirty degrees ideal.
        await SeedSlopeAsync(TerrainBox, column => column * 2f);
        var area = await AreaAsync(TerrainBox, locationProtected: false);
        var model = await ModelAsync(owner, "slope", new { kind = "fuzzyLinear", low = 0, high = 30 });

        var asked = await owner.PostAsJsonAsync(Url, new { modelId = model, areaFeatureId = area, cellMeters = 50 });
        var askedBody = await asked.Content.ReadAsStringAsync();
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, askedBody);
        var queued = JsonDocument.Parse(askedBody).RootElement;
        queued.GetProperty("status").GetString().ShouldBe("queued");
        queued.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = queued.GetProperty("id").GetGuid();

        // Until it is computed it is its asker's business only.
        (await viewer.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await RunAsync(id);

        var ready = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        ready.GetProperty("status").GetString().ShouldBe("ready");
        ready.GetProperty("mine").GetBoolean().ShouldBeTrue();
        ready.GetProperty("mayDelete").GetBoolean().ShouldBeTrue();
        ready.GetProperty("usesPositions").GetBoolean().ShouldBeFalse();
        ready.GetProperty("evidenceFeatures").GetInt32().ShouldBe(0);
        ready.GetProperty("modelId").GetGuid().ShouldBe(model);
        ready.GetProperty("areaFeatureId").GetGuid().ShouldBe(area);
        ready.GetProperty("cellMeters").GetDouble().ShouldBe(50);
        ready.GetProperty("computedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-10));
        // Which ground it read: the build that was active, and the slope picture with its settings.
        ready.GetProperty("terrainBuildId").GetGuid().ShouldBe(buildId);
        ready.GetProperty("terrainBuildActive").GetBoolean().ShouldBeTrue();
        var inputs = ready.GetProperty("terrainInputs");
        inputs.GetArrayLength().ShouldBe(1);
        inputs[0].GetProperty("layer").GetString().ShouldBe("slope");
        inputs[0].GetProperty("derivativeLayerId").GetGuid().ShouldNotBe(Guid.Empty);
        inputs[0].GetProperty("settings").GetString().ShouldNotBeNullOrEmpty();
        // The model as run travels with the result.
        ready.GetProperty("definition").GetProperty("layers")[0].GetProperty("layer").GetString().ShouldBe("slope");
        ready.GetProperty("weighting").GetProperty("weights")[0].GetProperty("weight").GetDouble().ShouldBe(1, 1e-9);

        var summary = ready.GetProperty("summary");
        var cells = ready.GetProperty("columns").GetInt32() * ready.GetProperty("rows").GetInt32();
        summary.GetProperty("areaCells").GetInt32().ShouldBeInRange(cells - 80, cells);
        summary.GetProperty("scoredCells").GetInt32().ShouldBe(summary.GetProperty("areaCells").GetInt32());
        summary.GetProperty("minimum").GetDouble().ShouldBe(0, 1e-6);
        summary.GetProperty("maximum").GetDouble().ShouldBe(1, 1e-6);
        summary.GetProperty("layers")[0].GetProperty("features").GetInt32().ShouldBe(0);

        // The map itself: favourability follows the slope, column by column, and stops at one.
        var path = await PathAsync(id);
        ValueAt(path, TerrainBox + 0.0005, Latitude).ShouldBe(0f, 1e-5f);
        ValueAt(path, TerrainBox + 0.0065, Latitude).ShouldBe(12f / 30f, 1e-5f);
        ValueAt(path, TerrainBox + 0.0155, Latitude).ShouldBe(1f, 1e-5f);

        // When another elevation build is made active, the run still says which one it read, and
        // that it is no longer the current one.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(b => b.Id == buildId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var replaced = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        replaced.GetProperty("terrainBuildId").GetGuid().ShouldBe(buildId);
        replaced.GetProperty("terrainBuildActive").GetBoolean().ShouldBeFalse();

        // A reader who holds the terrain right and never asked for it reads it too: no position
        // went into it. They may not delete it.
        var forViewer = await viewer.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        // The model was kept private, and a run does not widen who is shown its recipe: the map is
        // read, the name, the layers and the weights are not.
        forViewer.GetProperty("definition").ValueKind.ShouldBe(JsonValueKind.Null);
        forViewer.GetProperty("weighting").ValueKind.ShouldBe(JsonValueKind.Null);
        forViewer.GetProperty("modelName").ValueKind.ShouldBe(JsonValueKind.Null);
        ready.GetProperty("modelName").GetString().ShouldNotBeNullOrEmpty();
        (await viewer.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}"))[0]
            .GetProperty("definition").ValueKind.ShouldBe(JsonValueKind.Null);
        forViewer.GetProperty("mine").GetBoolean().ShouldBeFalse();
        forViewer.GetProperty("mayDelete").GetBoolean().ShouldBeFalse();
        (await viewer.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}"))
            .EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ShouldBe([id]);
        (await viewer.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The address handed out with the listing opens the raster for anything that holds it,
        // for that result only.
        using var anonymous = factory.CreateClient();
        var map = await anonymous.GetAsync(forViewer.GetProperty("url").GetString());
        map.StatusCode.ShouldBe(HttpStatusCode.OK);
        map.Content.Headers.ContentType!.MediaType.ShouldBe("image/tiff");
        (await map.Content.ReadAsByteArrayAsync()).LongLength.ShouldBe(ready.GetProperty("sizeBytes").GetInt64());
        var token = forViewer.GetProperty("url").GetString()!.Split("?token=")[1];
        (await anonymous.GetAsync($"{Url}/{Guid.NewGuid()}/content?token={token}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Deleting the model leaves the result standing, with what it was run from.
        (await owner.DeleteAsync($"{ModelsUrl}/{model}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var orphaned = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        orphaned.GetProperty("modelId").ValueKind.ShouldBe(JsonValueKind.Null);
        orphaned.GetProperty("definition").GetProperty("layers").GetArrayLength().ShouldBe(1);

        // Its asker takes it away, map and all.
        (await owner.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        File.Exists(path).ShouldBeFalse();
        (await anonymous.GetAsync(forViewer.GetProperty("url").GetString())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_model_shared_with_everyone_signed_in_shows_its_recipe_with_its_runs()
    {
        await SeedSlopeAsync(SharedModelBox, column => column * 2f);
        var area = await AreaAsync(SharedModelBox, locationProtected: false);
        var model = await ModelAsync(
            owner, "slope", new { kind = "fuzzyLinear", low = 0, high = 30 }, visibility: "authenticated");
        var id = await AskAndRunAsync(owner, model, area);

        var forViewer = await viewer.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        forViewer.GetProperty("definition").GetProperty("layers")[0].GetProperty("layer").GetString().ShouldBe("slope");
        forViewer.GetProperty("weighting").ValueKind.ShouldBe(JsonValueKind.Object);
        forViewer.GetProperty("modelName").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_finished_map_over_an_area_the_reader_may_not_place_is_not_there_for_them()
    {
        // The map's no-data mask traces the area's outline, so a protected area's outline is a
        // protected position: the owner places it and reads the run, the viewer does not.
        await SeedSlopeAsync(HiddenAreaBox, column => column * 2f);
        var area = await AreaAsync(HiddenAreaBox, locationProtected: true);
        var model = await ModelAsync(owner, "slope", new { kind = "fuzzyLinear", low = 0, high = 30 });
        var id = await AskAndRunAsync(owner, model, area);

        var owners = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        owners.GetProperty("status").GetString().ShouldBe("ready");
        owners.GetProperty("url").GetString().ShouldNotBeNullOrEmpty();

        var refused = await viewer.GetAsync($"{Url}/{id}");
        var missing = await viewer.GetAsync($"{Url}/{Guid.NewGuid()}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Shape(await refused.Content.ReadAsStringAsync()).ShouldBe(Shape(await missing.Content.ReadAsStringAsync()));
        (await viewer.GetFromJsonAsync<JsonElement>(Url))
            .EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ShouldNotContain(id);
        (await viewer.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}")).GetArrayLength().ShouldBe(0);
        (await viewer.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_model_that_weighs_the_ground_is_refused_at_once_when_no_elevation_is_built()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var area = await AreaAsync(NoTerrainBox, locationProtected: false);
        var ground = await ModelAsync(owner, "slope", new { kind = "fuzzyLinear", low = 0, high = 30 });
        var refused = await owner.PostAsJsonAsync(Url, new { modelId = ground, areaFeatureId = area, cellMeters = 50 });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Code(await refused.Content.ReadAsStringAsync()).ShouldBe("cave_potential_result.no_terrain");
        (await owner.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}")).GetArrayLength().ShouldBe(0);

        // A model of recorded features alone needs no ground and is queued as before.
        var features = await ModelAsync(
            owner, "sinkhole_distance", new { kind = "fuzzyLinear", low = 400, high = 0 });
        (await owner.PostAsJsonAsync(Url, new { modelId = features, areaFeatureId = area, cellMeters = 50 }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    // ---------- a map made from positions ----------

    [Fact]
    public async Task A_map_made_from_a_protected_sinkhole_does_not_exist_for_a_reader_who_may_not_place_it()
    {
        const double PublicLongitude = ProtectedBox + 0.003;
        const double ProtectedLongitude = ProtectedBox + 0.013;
        var area = await AreaAsync(ProtectedBox, locationProtected: false);
        await SinkholeAsync(PublicLongitude, locationProtected: false);
        await SinkholeAsync(ProtectedLongitude, locationProtected: true);
        // "The nearer a sinkhole the better": one on it, nought from 400 m out. Shared with
        // everyone signed in, so the viewer may run the very same recipe.
        var model = await ModelAsync(
            owner, "sinkhole_distance", new { kind = "fuzzyLinear", low = 400, high = 0 }, visibility: "authenticated");

        var ownersId = await AskAndRunAsync(owner, model, area);
        var viewersId = await AskAndRunAsync(viewer, model, area);

        // The owner places both sinkholes; their map was made from both and says so.
        var owners = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{ownersId}");
        owners.GetProperty("status").GetString().ShouldBe("ready");
        owners.GetProperty("usesPositions").GetBoolean().ShouldBeTrue();
        owners.GetProperty("evidenceFeatures").GetInt32().ShouldBe(2);
        owners.GetProperty("protectedFeatures").GetInt32().ShouldBe(1);
        ValueAt(await PathAsync(ownersId), ProtectedLongitude, Latitude).ShouldBeGreaterThan(0.8f);

        // For the viewer that map is not there: the same answer, word for word, as for an id that
        // names nothing — by reading, by listing and by deleting.
        var refused = await viewer.GetAsync($"{Url}/{ownersId}");
        var missing = await viewer.GetAsync($"{Url}/{Guid.NewGuid()}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Shape(await refused.Content.ReadAsStringAsync()).ShouldBe(Shape(await missing.Content.ReadAsStringAsync()));
        (await viewer.DeleteAsync($"{Url}/{ownersId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var listed = (await viewer.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}"))
            .EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        listed.ShouldBe([viewersId]);

        // What the viewer does read is their own run of the same model: made from the one sinkhole
        // they may place, and flat nought over the one they may not — the map of a register that
        // never held it.
        var viewers = await viewer.GetFromJsonAsync<JsonElement>($"{Url}/{viewersId}");
        viewers.GetProperty("status").GetString().ShouldBe("ready");
        viewers.GetProperty("evidenceFeatures").GetInt32().ShouldBe(1);
        viewers.GetProperty("protectedFeatures").GetInt32().ShouldBe(0);
        var viewersPath = await PathAsync(viewersId);
        ValueAt(viewersPath, ProtectedLongitude, Latitude).ShouldBe(0f, 1e-5f);
        ValueAt(viewersPath, PublicLongitude, Latitude).ShouldBeGreaterThan(0.8f);

        // The owner, who may place everything either map was made from, reads both.
        (await owner.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}"))
            .EnumerateArray().Select(r => r.GetProperty("id").GetGuid())
            .ShouldBe([ownersId, viewersId], ignoreOrder: true);
        (await owner.GetAsync($"{Url}/{viewersId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- what a run may be asked over ----------

    [Fact]
    public async Task A_run_is_refused_over_an_area_the_caller_may_not_place_a_model_they_may_not_read_or_a_grid_out_of_bounds()
    {
        var open = await AreaAsync(RefusalBox, locationProtected: false);
        var hidden = await AreaAsync(RefusalBox, locationProtected: true);
        var shared = await ModelAsync(
            owner, "sinkhole_distance", new { kind = "fuzzyLinear", low = 400, high = 0 }, visibility: "authenticated");
        var kept = await ModelAsync(owner, "sinkhole_distance", new { kind = "fuzzyLinear", low = 400, high = 0 });

        // An area the viewer can read but not place is refused exactly as one that does not exist.
        await StudyAreaRefusal.ShouldBeTheSharedRefusalAsync(
            await viewer.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = hidden, cellMeters = 50 }),
            "an area the caller may not place");
        await StudyAreaRefusal.ShouldBeTheSharedRefusalAsync(
            await viewer.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = Guid.NewGuid(), cellMeters = 50 }),
            "an area that does not exist");
        // The owner, who placed it, may run over it; and the viewer, over the open one.
        (await owner.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = hidden, cellMeters = 50 }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await viewer.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = open, cellMeters = 50 }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Somebody else's private model is not there to be run.
        var privateModel = await viewer.PostAsJsonAsync(Url, new { modelId = kept, areaFeatureId = open, cellMeters = 50 });
        privateModel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Code(await privateModel.Content.ReadAsStringAsync()).ShouldBe("cave_potential_model.not_found");

        // A point encloses no ground.
        var sinkhole = await SinkholeAsync(RefusalBox + 0.005, locationProtected: false);
        var point = await owner.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = sinkhole, cellMeters = 50 });
        point.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Code(await point.Content.ReadAsStringAsync()).ShouldBe("cave_potential_result.study_area_not_an_outline");

        // Cell sizes outside the bounds never reach the queue.
        (await owner.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = open, cellMeters = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Url, new { modelId = shared, areaFeatureId = open, cellMeters = 5000 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- a run that cannot be finished ----------

    [Fact]
    public async Task A_run_whose_evidence_was_never_computed_fails_with_its_reason_and_is_shown_to_its_asker_alone()
    {
        var area = await AreaAsync(FailureBox, locationProtected: false);
        // Nobody has computed a roughness surface for this elevation.
        var model = await ModelAsync(owner, "roughness", new { kind = "fuzzyLinear", low = 0, high = 10 });

        var asked = await owner.PostAsJsonAsync(Url, new { modelId = model, areaFeatureId = area, cellMeters = 50 });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await Should.ThrowAsync<CavePotentialRunException>(() => RunAsync(id));

        var failed = await owner.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("errorCode").GetString().ShouldBe(CavePotentialRunFailures.EvidenceUnavailable);
        failed.GetProperty("message").GetString().ShouldBe("roughness: no_layer");
        failed.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        failed.GetProperty("summary").ValueKind.ShouldBe(JsonValueKind.Null);
        Directory.Exists(Path.Combine(buildRoot, "cave-potential", id.ToString("N"))).ShouldBeFalse();

        (await viewer.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}")).GetArrayLength().ShouldBe(0);
        (await owner.GetFromJsonAsync<JsonElement>($"{Url}?areaFeatureId={area}")).GetArrayLength().ShouldBe(1);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        withoutTerrain?.Dispose();
    }

    private static string Code(string payload) =>
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()!;

    /// <summary>A refusal with what differs between any two requests taken out.</summary>
    private static string Shape(string payload)
    {
        var root = JsonDocument.Parse(payload).RootElement;
        return string.Join(
            "|",
            root.EnumerateObject()
                .Where(p => p.Name is not ("traceId" or "instance"))
                .Select(p => $"{p.Name}={p.Value}"));
    }

    private async Task<Guid> AskAndRunAsync(HttpClient client, Guid model, Guid area)
    {
        var asked = await client.PostAsJsonAsync(Url, new { modelId = model, areaFeatureId = area, cellMeters = 50 });
        var payload = await asked.Content.ReadAsStringAsync();
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
        await RunAsync(id);
        return id;
    }

    /// <summary>Carries the run's own queued job through, the way the terrain worker would.</summary>
    private async Task RunAsync(Guid resultId)
    {
        var jobs = await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.CavePotentialRun);
        var job = jobs.Single(j =>
            JsonSerializer.Deserialize<CavePotentialRunPayload>(j.Payload, JsonSerializerOptions.Web)!
                .CavePotentialResultId == resultId);
        await QueuedJob.RunAsync(factory.Services, job.Id);
    }

    private async Task<string> PathAsync(Guid resultId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.CavePotentialResults.Where(r => r.Id == resultId).Select(r => r.Path).SingleAsync())!;
    }

    /// <summary>The value the written map holds at a place, read back with the raster library.</summary>
    private static float ValueAt(string path, double longitude, double latitude)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
        var transform = new double[6];
        dataset.GetGeoTransform(transform);
        var x = (int)Math.Floor((longitude - transform[0]) / transform[1]);
        var y = (int)Math.Floor((latitude - transform[3]) / transform[5]);
        var pixel = new float[1];
        using var band = dataset.GetRasterBand(1);
        band.ReadRaster(x, y, 1, 1, pixel, 1, 1, 0, 0).ShouldBe(CPLErr.CE_None);
        return pixel[0];
    }

    private async Task<Guid> ModelAsync(HttpClient client, string layer, object transform, string visibility = "private")
    {
        var response = await client.PostAsJsonAsync(ModelsUrl, new
        {
            name = $"PotRes {Guid.NewGuid():N}"[..24],
            method = "directWeights",
            definition = new { layers = new object[] { new { layer, weight = 1, transform } } },
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task<Guid> SinkholeAsync(double longitude, bool locationProtected) =>
        FeatureAsync(
            sinkholeTypeId,
            new { type = "Point", coordinates = new[] { longitude, Latitude } },
            locationProtected);

    private Task<Guid> AreaAsync(double west, bool locationProtected)
    {
        var east = west + (16 * Pixel);
        return FeatureAsync(
            karstAreaTypeId,
            new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { west, South }, new[] { east, South }, new[] { east, North },
                        new[] { west, North }, new[] { west, South },
                    },
                },
            },
            locationProtected);
    }

    private async Task<Guid> FeatureAsync(long featureTypeId, object geometry, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"PotRes {Guid.NewGuid():N}"[..24],
            featureTypeId,
            geometry,
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static Polygon Rectangle(double west) =>
        new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
        [
            new Coordinate(west, South),
            new Coordinate(west + (16 * Pixel), South),
            new Coordinate(west + (16 * Pixel), North),
            new Coordinate(west, North),
            new Coordinate(west, South),
        ]);

    /// <summary>
    /// A slope surface of the active build over one box, in a generated raster whose value is a
    /// function of the pixel column, registered ready and current the way the job that draws it would.
    /// </summary>
    private async Task SeedSlopeAsync(double west, Func<int, float> byColumn)
    {
        var settings = TerrainDerivativeRegistry.Normalise(
            new TerrainDerivativeSettings { Derivative = TerrainDerivative.Slope, SlopeUnit = TerrainSlopeUnit.Degrees });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = TerrainDerivative.Slope,
            Settings = TerrainDerivativeRegistry.Describe(settings),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = "Slope",
            Status = TerrainDerivativeStatus.Ready,
            Version = 1,
            MethodRevision = TerrainDerivativeRegistry.MethodRevision(TerrainDerivative.Slope),
            ComputedAt = DateTimeOffset.UtcNow,
        };
        db.TerrainDerivativeLayers.Add(layer);

        var directory = Path.Combine(buildRoot, buildId.ToString("N"), "derivatives", layer.Id.ToString("N"), "v1");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "tile.tif");
        WriteColumns(file, west, byColumn);

        db.TerrainDerivativeRasters.Add(new TerrainDerivativeRaster
        {
            TerrainDerivativeLayerId = layer.Id,
            SourcePath = Path.Combine(buildRoot, buildId.ToString("N"), "prepared", "ground.tif"),
            Path = file,
            Width = 16,
            Height = 16,
            PixelSizeDegrees = Pixel,
            Footprint = Rectangle(west),
            SizeBytes = new FileInfo(file).Length,
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static void WriteColumns(string path, double west, Func<int, float> byColumn)
    {
        GdalBase.ConfigureAll();
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([west, Pixel, 0, North, 0, -Pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(-9999);
        var pixels = new float[16 * 16];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                pixels[(y * 16) + x] = byColumn(x);
            }
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
