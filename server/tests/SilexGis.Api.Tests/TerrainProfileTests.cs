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
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The surface profile along a drawn line. The ground is a ramp rising one metre per pixel column,
/// which bilinear reading reproduces, so every expected height is a function of longitude alone.
/// </summary>
public sealed class TerrainProfileTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    private HttpClient reader = null!;
    private HttpClient groundless = null!;
    private HttpClient anonymous = null!;

    public TerrainProfileTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-tprof-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var readerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"tp-read-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tp-none-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = readerId,
                Domain = AccessDomain.Terrain,
                Actions = AccessAction.Read,
                Effect = AccessEffect.Allow,
                ScopeKind = AccessScopeKind.All,
            });

            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
            var build = new TerrainBuild
            {
                Extent = new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
                [
                    new Coordinate(West, North - (16 * Pixel)),
                    new Coordinate(West + (16 * Pixel), North - (16 * Pixel)),
                    new Coordinate(West + (16 * Pixel), North),
                    new Coordinate(West, North),
                    new Coordinate(West, North - (16 * Pixel)),
                ]),
                RequestedMaxDepth = 13,
                Status = TerrainBuildStatus.Queued,
                Phase = TerrainBuildPhase.Pending,
                HeightDatum = TerrainHeightDatum.Orthometric,
                GeoidHeightM = 0,
                IsActive = true,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            WriteRamp(build.Id);
        }

        reader = await AuthHelper.BearerClientAsync(factory, $"tp-read-{suffix}@t.local");
        groundless = await AuthHelper.BearerClientAsync(factory, $"tp-none-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_line_over_the_ramp_climbs_by_what_the_ramp_climbs()
    {
        // Three pixel columns east, inside the raster and away from its edges.
        var body = await ProfileAsync(reader, (25.004, 46.09), (25.010, 46.09));

        body.GetProperty("hasTerrain").GetBoolean().ShouldBeTrue();
        body.GetProperty("lengthM").GetDouble().ShouldBeInRange(450, 480);
        var samples = body.GetProperty("samples").EnumerateArray().ToList();
        samples.Count.ShouldBeGreaterThan(5);
        samples[0].GetProperty("distanceAlongM").GetDouble().ShouldBe(0);
        samples.ShouldAllBe(s => s.GetProperty("outcome").GetString() == "sampled");

        // The ramp is 1000 m at the first column centre and rises 1 m per column.
        var first = samples[0].GetProperty("elevationM").GetDouble();
        var last = samples[^1].GetProperty("elevationM").GetDouble();
        (last - first).ShouldBe(6, 0.05);
        body.GetProperty("ascentM").GetDouble().ShouldBe(6, 0.05);
        body.GetProperty("descentM").GetDouble().ShouldBe(0, 1e-6);
        body.GetProperty("highestM").GetDouble().ShouldBe(last, 1e-6);
    }

    [Fact]
    public async Task Stations_beyond_the_built_ground_are_gaps_and_never_zero()
    {
        var body = await ProfileAsync(reader, (24.0, 46.09), (24.001, 46.09));

        body.GetProperty("readCount").GetInt32().ShouldBe(0);
        body.GetProperty("lowestM").ValueKind.ShouldBe(JsonValueKind.Null);
        foreach (var sample in body.GetProperty("samples").EnumerateArray())
        {
            sample.GetProperty("outcome").GetString().ShouldBe("outsideCoverage");
            sample.GetProperty("elevationM").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Without_the_terrain_right_the_route_is_refused_and_with_it_it_answers()
    {
        var request = new { points = new[] { new { longitude = 25.004, latitude = 46.09 }, new { longitude = 25.01, latitude = 46.09 } } };

        var refused = await groundless.PostAsJsonAsync("/api/v1/terrain/profile", request);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("access.forbidden");

        (await reader.PostAsJsonAsync("/api/v1/terrain/profile", request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.PostAsJsonAsync("/api/v1/terrain/profile", request))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_line_the_reader_should_not_be_asked_about_is_refused_before_anything_is_read()
    {
        (await reader.PostAsJsonAsync("/api/v1/terrain/profile", new { points = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsJsonAsync("/api/v1/terrain/profile",
                new { points = new[] { new { longitude = 25.0, latitude = 46.0 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsJsonAsync("/api/v1/terrain/profile",
                new { points = new[] { new { longitude = 25.0, latitude = 46.0 }, new { longitude = 200.0, latitude = 46.0 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooMany = Enumerable.Range(0, 201).Select(i => new { longitude = 25.0 + (i * 1e-5), latitude = 46.0 });
        (await reader.PostAsJsonAsync("/api/v1/terrain/profile", new { points = tooMany }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        reader?.Dispose();
        groundless?.Dispose();
        anonymous?.Dispose();
    }

    private static async Task<JsonElement> ProfileAsync(
        HttpClient client, (double Lon, double Lat) from, (double Lon, double Lat) to)
    {
        var response = await client.PostAsJsonAsync("/api/v1/terrain/profile", new
        {
            points = new[]
            {
                new { longitude = from.Lon, latitude = from.Lat },
                new { longitude = to.Lon, latitude = to.Lat },
            },
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private void WriteRamp(Guid build)
    {
        GdalBase.ConfigureAll();
        var directory = Path.Combine(buildRoot, build.ToString("N"), "prepared");
        Directory.CreateDirectory(directory);

        using var dataset = Gdal.GetDriverByName("GTiff")
            .Create(Path.Combine(directory, "ground.tif"), 16, 16, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, Pixel, 0, North, 0, -Pixel]);
        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(TerrainRasterPreparation.VoidValue);
        var pixels = new float[16 * 16];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                pixels[(y * 16) + x] = 1000 + x;
            }
        }

        band.WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
    }
}
