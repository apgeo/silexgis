// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a terrain build records about the accuracy of its elevation: filled from the open
/// dataset's published figures when the build is made only from it, typed by an operator for
/// anything else, correctable afterwards, and never invented for a mixture.
///
/// <para>
/// Every refusal is paired with an account holding exactly the right being tested: a full
/// administrator is waved through before any terrain rule is read, so only ordinary accounts prove
/// the check is about the right. Correcting the record takes Execute, the right that starts a
/// build; an account that may only read builds sees the figures and cannot change them.
/// </para>
/// </summary>
public sealed class TerrainAccuracyTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot = Path.Combine(
        TestScratch.Root, "silexgis-tacc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<Guid> seeded = [];

    private HttpClient starter = null!;   // granted Execute on the terrain domain, and nothing else
    private HttpClient reader = null!;    // granted Read on the terrain domain, and nothing else
    private HttpClient outsider = null!;  // granted nothing at all
    private HttpClient anonymous = null!;

    public TerrainAccuracyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var starterId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tacc-run-{suffix}@t.local");
        starter = await AuthHelper.BearerClientAsync(factory, $"tacc-run-{suffix}@t.local");
        await GrantAsync(starterId, AccessAction.Execute);

        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tacc-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tacc-read-{suffix}@t.local");
        await GrantAsync(readerId, AccessAction.Read);

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tacc-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tacc-out-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_build_made_only_from_the_obtained_coverage_records_that_datasets_published_figures()
    {
        var response = await starter.PostAsJsonAsync("/api/v1/terrain/builds", Submit(23.10, 46.10));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        var build = JsonDocument.Parse(body).RootElement;
        seeded.Add(build.GetProperty("id").GetGuid());

        var accuracy = build.GetProperty("accuracy");
        accuracy.GetProperty("sourceResolutionM").GetDouble().ShouldBe(CopernicusCoverage.SourceResolutionM);
        accuracy.GetProperty("verticalAccuracyM").GetDouble().ShouldBe(CopernicusCoverage.VerticalAccuracyM);

        // The statistic is named, because the dataset publishes a ninety-per-cent figure and
        // reading it as a root-mean-square error would overstate the model's quality.
        accuracy.GetProperty("verticalAccuracyStatistic").GetString().ShouldBe("LE90");
        accuracy.GetProperty("accuracySource").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task What_the_operator_types_wins_and_a_figure_without_its_statistic_is_refused()
    {
        var typed = new TerrainAccuracyRequest(1d, 0.3, "RMSE", "A survey report in invented words");
        var response = await starter.PostAsJsonAsync("/api/v1/terrain/builds", Submit(23.20, 46.20, typed));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        var accuracy = JsonDocument.Parse(body).RootElement.GetProperty("accuracy");
        seeded.Add(JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid());
        accuracy.GetProperty("verticalAccuracyM").GetDouble().ShouldBe(0.3);
        accuracy.GetProperty("verticalAccuracyStatistic").GetString().ShouldBe("RMSE");
        accuracy.GetProperty("accuracySource").GetString().ShouldBe("A survey report in invented words");

        // The positive case above is what makes this refusal about the statistic and not about
        // the route: the same request with the statistic taken away.
        var noStatistic = new TerrainAccuracyRequest(1d, 0.3, null, null);
        (await starter.PostAsJsonAsync("/api/v1/terrain/builds", Submit(23.25, 46.25, noStatistic)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Correcting_the_record_takes_the_right_to_run_terrain_work_and_can_clear_it()
    {
        var id = await SeedBuildAsync();
        var path = $"/api/v1/terrain/builds/{id}/accuracy";
        var request = new TerrainAccuracyRequest(2d, 0.5, "LE95", "A vendor's statement");

        (await anonymous.PutAsJsonAsync(path, request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.PutAsJsonAsync(path, request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Holds a terrain right, and the wrong one for writing.
        (await reader.PutAsJsonAsync(path, request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await starter.PutAsJsonAsync(path, request);
        var body = await allowed.Content.ReadAsStringAsync();
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("accuracy")
            .GetProperty("verticalAccuracyStatistic").GetString().ShouldBe("LE95");

        // The read-only account sees what was recorded.
        var seen = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/terrain/builds/{id}");
        seen.GetProperty("build").GetProperty("accuracy").GetProperty("accuracySource").GetString()
            .ShouldBe("A vendor's statement");

        (await starter.PutAsJsonAsync($"/api/v1/terrain/builds/{Guid.NewGuid()}/accuracy", request))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await starter.PutAsJsonAsync(path, new TerrainAccuracyRequest(-1d, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing stated clears the record: "not recorded", which is not zero.
        var cleared = await starter.PutAsJsonAsync(path, new TerrainAccuracyRequest(null, null, null, null));
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await cleared.Content.ReadFromJsonAsync<JsonElement>());
        (!after.TryGetProperty("accuracy", out var none) || none.ValueKind == JsonValueKind.Null)
            .ShouldBeTrue(after.ToString());
    }

    private static TerrainBuildSubmitRequest Submit(double west, double south, TerrainAccuracyRequest? accuracy = null) =>
        new(west, south, west + 0.05, south + 0.05, 13, null, null, true, null, null, accuracy);

    private async Task<Guid> SeedBuildAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var gf = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var extent = gf.CreatePolygon([
            new Coordinate(24.0, 46.0), new Coordinate(24.05, 46.0), new Coordinate(24.05, 46.05),
            new Coordinate(24.0, 46.05), new Coordinate(24.0, 46.0)]);
        var build = new TerrainBuild
        {
            Extent = extent,
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Succeeded,
            Phase = TerrainBuildPhase.Publish,
        };
        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        seeded.Add(build.Id);
        return build.Id;
    }

    private async Task GrantAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        // What this class queued and wrote is removed so a later class sharing the container does
        // not find builds, or queued terrain work, it did not make.
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.TerrainBuild).ExecuteDelete();
            db.TerrainBuilds.Where(b => seeded.Contains(b.Id)).ExecuteDelete();
        }
        catch (Exception)
        {
            // The container is thrown away with the run; a failed sweep must not mask a result.
        }

        starter?.Dispose();
        reader?.Dispose();
        outsider?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        try { Directory.Delete(buildRoot, recursive: true); } catch (IOException) { }
    }
}
