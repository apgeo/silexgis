// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The landform picture (geomorphons) through the API: listed with whether this installation can
/// compute it and why not, refused when asked for where WhiteboxTools does not run, and queued
/// like every other picture where it does.
/// </summary>
/// <remarks>
/// Two applications over one database: one as an installation that left the analysis program off
/// (the default), one whose analysis program is a stand-in that is always available.
/// </remarks>
public sealed class TerrainLandformApiTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory without;
    private readonly SilexGisApiFactory with;
    private readonly List<Guid> seeded = [];

    private HttpClient holderWithout = null!;
    private HttpClient holderWith = null!;
    private HttpClient outsider = null!;
    private HttpClient anonymous = null!;

    public TerrainLandformApiTests(PostgresFixture postgres)
    {
        without = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);
        with = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<IWhiteboxTools>(new FakeWhiteboxTools()));
            });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Viewers holding exactly the terrain entry, and one holding nothing: an administrator would
        // be waved through before the terrain rule is read.
        var holderId = await AuthHelper.CreateUserAsync(without, GlobalRoles.Viewer, $"tl-hold-{suffix}@t.local");
        await GrantAsync(holderId);
        holderWithout = await AuthHelper.BearerClientAsync(without, $"tl-hold-{suffix}@t.local");
        holderWith = await AuthHelper.BearerClientAsync(with, $"tl-hold-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(without, GlobalRoles.Viewer, $"tl-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(without, $"tl-out-{suffix}@t.local");
        anonymous = without.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await using (var scope = without.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(b => seeded.Contains(b.Id)).ExecuteDeleteAsync();
        }

        await without.DisposeAsync();
        await with.DisposeAsync();
    }

    [Fact]
    public async Task The_kinds_listing_takes_the_terrain_right_and_says_why_landforms_are_not_offered()
    {
        (await anonymous.GetAsync("/api/v1/terrain/derivatives/kinds")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.GetAsync("/api/v1/terrain/derivatives/kinds")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var kinds = await KindsAsync(holderWithout);
        kinds.Count.ShouldBe(Enum.GetValues<Domain.Terrain.TerrainDerivative>().Length);
        var landforms = kinds.Single(k => k.GetProperty("derivative").GetString() == "geomorphons");
        landforms.GetProperty("available").GetBoolean().ShouldBeFalse();
        landforms.GetProperty("unavailableReason").GetString().ShouldBe(WhiteboxUnavailable.NotConfigured);

        // Everything the raster library draws stays on offer whatever the analysis program does.
        var slope = kinds.Single(k => k.GetProperty("derivative").GetString() == "slope");
        slope.GetProperty("available").GetBoolean().ShouldBeTrue();
        slope.GetProperty("unavailableReason").ValueKind.ShouldBe(JsonValueKind.Null);

        var offered = (await KindsAsync(holderWith))
            .Single(k => k.GetProperty("derivative").GetString() == "geomorphons");
        offered.GetProperty("available").GetBoolean().ShouldBeTrue();
        offered.GetProperty("unavailableReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Refused before anything is written where the program does not run — a queued run could only
    /// fail — while the same account asking for steepness over the same build is queued.
    /// </summary>
    [Fact]
    public async Task Landforms_are_refused_with_a_reason_where_the_analysis_program_does_not_run()
    {
        var buildId = await SeedBuildAsync();

        var refused = await holderWithout.PostAsJsonAsync("/api/v1/terrain/derivatives",
            new { terrainBuildId = buildId, derivative = "geomorphons", name = "landforms" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("terrain_derivative.analysis_unavailable");
        problem.GetProperty("reason").GetString().ShouldBe(WhiteboxUnavailable.NotConfigured);

        var steepness = await holderWithout.PostAsJsonAsync("/api/v1/terrain/derivatives",
            new { terrainBuildId = buildId, derivative = "slope", name = "steepness" });
        steepness.StatusCode.ShouldBe(HttpStatusCode.OK, await steepness.Content.ReadAsStringAsync());

        await using var scope = without.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var kinds = await db.TerrainDerivativeLayers.Where(l => l.TerrainBuildId == buildId)
            .Select(l => l.Derivative).ToListAsync();
        kinds.ShouldBe([Domain.Terrain.TerrainDerivative.Slope]);
    }

    /// <summary>
    /// Where the program runs, a landform picture is queued like any other, with its three settings
    /// written out as the usual ones when left out — and two requests that differ only by naming the
    /// usual values are one picture.
    /// </summary>
    [Fact]
    public async Task Landforms_are_queued_where_the_analysis_program_runs_and_their_settings_are_checked()
    {
        var buildId = await SeedBuildAsync();

        var asked = await holderWith.PostAsJsonAsync("/api/v1/terrain/derivatives",
            new { terrainBuildId = buildId, derivative = "geomorphons", name = "landforms" });
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var picture = await asked.Content.ReadFromJsonAsync<JsonElement>();
        picture.GetProperty("status").GetString().ShouldBe("queued");
        picture.GetProperty("derivative").GetString().ShouldBe("geomorphons");

        using (var record = JsonDocument.Parse(picture.GetProperty("settings").GetString()!))
        {
            record.RootElement.GetProperty("geomorphonSearchMetres").GetDouble().ShouldBe(500d);
            record.RootElement.GetProperty("geomorphonSkipMetres").GetDouble().ShouldBe(0d);
            record.RootElement.GetProperty("geomorphonFlatnessDegrees").GetDouble().ShouldBe(1d);
        }

        var again = await holderWith.PostAsJsonAsync("/api/v1/terrain/derivatives", new
        {
            terrainBuildId = buildId,
            derivative = "geomorphons",
            name = "landforms, spelled out",
            geomorphonSearchMetres = 500,
            geomorphonSkipMetres = 0,
            geomorphonFlatnessDegrees = 1,
        });
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
            .ShouldBe(picture.GetProperty("id").GetGuid());

        var invalid = await holderWith.PostAsJsonAsync("/api/v1/terrain/derivatives", new
        {
            terrainBuildId = buildId,
            derivative = "geomorphons",
            name = "landforms looking past everything",
            geomorphonSearchMetres = 300,
            geomorphonSkipMetres = 300,
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var scope = with.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var layerId = picture.GetProperty("id").GetGuid();
        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.TerrainDerivative)
            .Select(j => j.Payload)
            .ToListAsync();
        queued.Count(p => p.Contains(layerId.ToString(), StringComparison.Ordinal)).ShouldBe(1);
    }

    /// <summary>
    /// A landform picture drawn by one version of the analysis program reads as out of date once the
    /// program reports another, and asking for it again queues it afresh. Left alone where the
    /// versions agree, where the picture never recorded one, and where the program says nothing now.
    /// </summary>
    [Fact]
    public async Task A_landform_picture_drawn_by_another_version_of_the_program_reads_as_out_of_date()
    {
        var buildId = await SeedBuildAsync(active: true);
        var older = await SeedLandformsAsync(buildId, 300, "WhiteboxTools 0.0-older");
        var same = await SeedLandformsAsync(buildId, 400, FakeWhiteboxTools.FakeVersion);
        var unrecorded = await SeedLandformsAsync(buildId, 600, toolVersion: null);

        // Where the program is the stand-in, which reports its own version.
        var listed = await ListAsync(holderWith, buildId);
        listed[older].ShouldBe("methodRevised");
        listed[same].ShouldBeNull();
        listed[unrecorded].ShouldBeNull();

        // Where the installation left the program off it cannot say what version it is, and says
        // nothing about the pictures it drew earlier: the mark needs two known versions.
        var silent = await ListAsync(holderWithout, buildId);
        silent[older].ShouldBeNull();

        var again = await holderWith.PostAsJsonAsync("/api/v1/terrain/derivatives", new
        {
            terrainBuildId = buildId,
            derivative = "geomorphons",
            name = "landforms",
            geomorphonSearchMetres = 300,
        });
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var picture = await again.Content.ReadFromJsonAsync<JsonElement>();
        picture.GetProperty("id").GetGuid().ShouldBe(older);
        picture.GetProperty("status").GetString().ShouldBe("queued");

        // The one that was current is not re-queued by asking for it again.
        var current = await holderWith.PostAsJsonAsync("/api/v1/terrain/derivatives", new
        {
            terrainBuildId = buildId,
            derivative = "geomorphons",
            name = "landforms",
            geomorphonSearchMetres = 400,
        });
        (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .ShouldBe("ready");
    }

    private static async Task<Dictionary<Guid, string?>> ListAsync(HttpClient client, Guid buildId)
    {
        var response = await client.GetAsync($"/api/v1/terrain/derivatives?buildId={buildId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToDictionary(
            e => e.GetProperty("id").GetGuid(),
            e => e.GetProperty("staleReason").ValueKind == JsonValueKind.Null
                ? null
                : e.GetProperty("staleReason").GetString());
    }

    /// <summary>A finished landform picture over <paramref name="buildId"/>, as a run would have left it.</summary>
    private async Task<Guid> SeedLandformsAsync(Guid buildId, int searchMetres, string? toolVersion)
    {
        await using var scope = without.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var settings = new TerrainDerivativeSettings
        {
            Derivative = TerrainDerivative.Geomorphons,
            GeomorphonSearchMetres = searchMetres,
        };
        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = TerrainDerivative.Geomorphons,
            Settings = TerrainDerivativeRegistry.Describe(TerrainDerivativeRegistry.Normalise(settings)),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = $"landforms {searchMetres}",
            Status = TerrainDerivativeStatus.Ready,
            Version = 1,
            MethodRevision = TerrainDerivativeRegistry.MethodRevision(TerrainDerivative.Geomorphons),
            ToolVersion = toolVersion,
            ComputedAt = DateTimeOffset.UtcNow,
        };
        db.TerrainDerivativeLayers.Add(layer);
        await db.SaveChangesAsync();
        return layer.Id;
    }

    private static async Task<List<JsonElement>> KindsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/terrain/derivatives/kinds");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private async Task GrantAsync(Guid userId)
    {
        await using var scope = without.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = AccessAction.Read | AccessAction.Execute,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A finished build, not made the active one: these requests need it finished, not served.</summary>
    private async Task<Guid> SeedBuildAsync(bool active = false)
    {
        await using var scope = without.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // At most one build is the active one, held by a unique index, and the container is shared.
        if (active)
        {
            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var build = new TerrainBuild
        {
            Extent = factory.CreatePolygon(
            [
                new Coordinate(25.0, 46.0), new Coordinate(25.1, 46.0), new Coordinate(25.1, 46.1),
                new Coordinate(25.0, 46.1), new Coordinate(25.0, 46.0),
            ]),
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Succeeded,
            Phase = TerrainBuildPhase.Publish,
            IsActive = active,
        };
        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        seeded.Add(build.Id);
        return build.Id;
    }
}
