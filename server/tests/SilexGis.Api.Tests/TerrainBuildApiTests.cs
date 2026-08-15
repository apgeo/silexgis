// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading the record of terrain builds: who may, who may not, and that a page of builds is a
/// page rather than a suggestion.
///
/// <para>
/// The permission assertions all have the same shape, and the shape is the point. A full
/// administrator is short-circuited before any rule is read — five separate places in the
/// evaluator, the query filters and the SQL fragments return "allowed" on that fact alone — so an
/// endpoint whose permission check had been deleted entirely would still pass every assertion
/// driven by an administrator. Every refusal here is therefore paired, in the same test, with an
/// ordinary account that holds exactly the terrain right and nothing else: the pass is what proves
/// the refusal was about the right and not about the fixture.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainBuildApiTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient holder = null!;    // an ordinary account granted Read on the terrain domain
    private HttpClient outsider = null!;  // an ordinary account granted nothing at all
    private HttpClient anonymous = null!;
    private Guid holderId;

    // Nothing here queues work, so this host runs none of the job drains. Every test class shares
    // one PostGIS container and the queue lives in it, so a drain started here would claim work
    // queued by another class and fail it against storage this class does not have.
    public TerrainBuildApiTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Ordinary accounts on both sides. An administrator would be waved through before any
        // terrain rule was consulted, so the holder is a plain member with one entry written
        // straight into storage — which is also the only way to write it, since the authoring
        // surface refuses rules handing out more than their author holds.
        holderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tb-hold-{suffix}@t.local");
        holder = await AuthHelper.BearerClientAsync(factory, $"tb-hold-{suffix}@t.local");
        await GrantAsync(holderId, AccessDomain.Terrain, AccessAction.Read, AccessScopeKind.All);

        // Granted nothing. The seeded ruleset every account joins opens map layers, tags,
        // taxonomies and the caver directory; it says nothing about terrain, so this account
        // genuinely holds no terrain right rather than being assumed to hold none.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tb-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tb-out-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The whole permission table for the listing, in one test: refused without a signature,
    /// refused with a signature and no right, allowed with the right and nothing else.
    /// </summary>
    [Fact]
    public async Task Listing_builds_takes_the_terrain_right_and_nothing_less()
    {
        await SeedBuildAsync("a build to be seen or not seen", TerrainBuildStatus.Succeeded);

        (await anonymous.GetAsync("/api/v1/terrain/builds")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await outsider.GetAsync("/api/v1/terrain/builds");
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        CodeOf(refusedBody).ShouldBe("access.forbidden");

        // The pass that makes the refusal above mean something.
        var allowed = await holder.GetAsync("/api/v1/terrain/builds");
        var allowedBody = await allowed.Content.ReadAsStringAsync();
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, allowedBody);
        JsonDocument.Parse(allowedBody).RootElement
            .GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The same table for one build, plus the two answers a caller who may read must be able to
    /// tell apart: a build that is not there, and a build that is.
    /// </summary>
    [Fact]
    public async Task Reading_one_build_takes_the_terrain_right_and_reports_a_missing_one_as_missing()
    {
        var id = await SeedBuildAsync("one build in full", TerrainBuildStatus.Failed, sources: true);

        (await anonymous.GetAsync($"/api/v1/terrain/builds/{id}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await outsider.GetAsync($"/api/v1/terrain/builds/{id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        var allowed = await holder.GetAsync($"/api/v1/terrain/builds/{id}");
        var body = await allowed.Content.ReadAsStringAsync();
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var payload = JsonDocument.Parse(body).RootElement;
        var build = payload.GetProperty("build");
        build.GetProperty("id").GetGuid().ShouldBe(id);
        build.GetProperty("status").GetString().ShouldBe("failed");
        build.GetProperty("sizeBytes").GetInt64().ShouldBe(4096);
        build.GetProperty("extent").GetProperty("type").GetString().ShouldBe("Polygon");

        // The datum travels with the build, and the offset it implies is served rather than left
        // to be recomputed in a browser: the rule has one home, and a second copy of it would be a
        // forty-metre error with nothing on screen to explain it.
        build.GetProperty("heightDatum").GetString().ShouldBe("ellipsoidal");
        build.GetProperty("surveyHeightOffsetM").GetDouble().ShouldBe(
            GeoidOffset.SurveyToSceneOffsetM(TerrainHeightDatum.Ellipsoidal, 43.03));

        // Sources and the log tail are on the single build and not on the list, because both are
        // unbounded per row.
        payload.GetProperty("logTail").GetString().ShouldNotBeNullOrEmpty();
        var listed = payload.GetProperty("sources").EnumerateArray().ToList();
        listed.Count.ShouldBe(2);
        listed.Select(s => s.GetProperty("attribution").GetString())
            .ShouldBe(["Copernicus DEM", "National lidar"]);

        var missing = await holder.GetAsync($"/api/v1/terrain/builds/{Guid.NewGuid()}");
        var missingBody = await missing.Content.ReadAsStringAsync();
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, missingBody);
        CodeOf(missingBody).ShouldBe("terrain_build.not_found");
    }

    /// <summary>
    /// Paging is a total order, not a hint. Builds submitted in the same instant are ordered by
    /// their key as well as their timestamp, so walking the pages sees every row exactly once —
    /// without the tie-break, two rows sharing a timestamp can sort differently between one query
    /// and the next, and a row is then read twice or skipped entirely.
    /// </summary>
    [Fact]
    public async Task Pages_of_builds_are_stable_when_builds_share_a_timestamp()
    {
        // The listing has no filter — deliberately, since a build belongs to nobody and there is
        // no audience to narrow it to — so "these six rows are the whole listing" has to be made
        // true rather than assumed. The classes sharing this database run one at a time, and the
        // other one that writes builds clears them again when it finishes.
        await ClearBuildsAsync();

        var sameInstant = DateTimeOffset.UtcNow;
        var seeded = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            seeded.Add(await SeedBuildAsync($"tied build {i}", TerrainBuildStatus.Queued, createdAt: sameInstant));
        }

        var firstPage = await PageAsync(1, 3);
        var secondPage = await PageAsync(2, 3);

        // Read the same pages again: an unstable order shows up here and nowhere else.
        (await PageAsync(1, 3)).ShouldBe(firstPage);
        (await PageAsync(2, 3)).ShouldBe(secondPage);

        var walked = firstPage.Concat(secondPage).ToList();
        walked.Distinct().Count().ShouldBe(walked.Count);

        // Sorted the way the database sorts a uuid — over its sixteen bytes in the order the
        // canonical text form prints them, which is not the order the runtime's own comparison
        // uses. Time-ordered keys then put the newest first, which is where the timestamp was
        // already pointing.
        walked.ShouldBe([.. seeded.OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal)]);
    }

    private async Task<List<Guid>> PageAsync(int page, int pageSize)
    {
        var response = await holder.GetAsync($"/api/v1/terrain/builds?page={page}&pageSize={pageSize}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return [.. JsonDocument.Parse(body).RootElement.GetProperty("items")
            .EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    private async Task ClearBuildsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    private static Polygon SomeExtent() =>
        NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePolygon(
        [
            new Coordinate(22.5, 46.5),
            new Coordinate(22.6, 46.5),
            new Coordinate(22.6, 46.6),
            new Coordinate(22.5, 46.6),
            new Coordinate(22.5, 46.5),
        ]);

    private async Task<Guid> SeedBuildAsync(
        string message,
        TerrainBuildStatus status,
        bool sources = false,
        DateTimeOffset? createdAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var build = new TerrainBuild
        {
            Extent = SomeExtent(),
            RequestedMaxDepth = 14,
            Status = status,
            Phase = TerrainBuildPhase.Bake,
            Progress = 40,
            Message = message,
            ErrorCode = status == TerrainBuildStatus.Failed ? "terrain.bake_failed" : null,
            LogTail = "the last thing the tools said",
            SizeBytes = 4096,
            PyramidVersion = "1a2b3c",
            HeightDatum = TerrainHeightDatum.Ellipsoidal,
            GeoidHeightM = 43.03,
        };
        db.TerrainBuilds.Add(build);
        if (sources)
        {
            db.TerrainBuildSources.AddRange(
                new TerrainBuildSource
                {
                    TerrainBuildId = build.Id,
                    Kind = TerrainBuildSourceKind.Fetched,
                    Reference = "copernicus/N46E022",
                    Attribution = "Copernicus DEM",
                    Licence = "Copernicus free, full and open",
                },
                new TerrainBuildSource
                {
                    TerrainBuildId = build.Id,
                    Kind = TerrainBuildSourceKind.ServerDirectory,
                    Reference = "/srv/rasters/laki",
                    Attribution = "National lidar",
                });
        }

        await db.SaveChangesAsync();

        if (createdAt is { } stamp)
        {
            // The timestamp is maintained on save, so the tie this test exists to provoke has to
            // be written after the fact.
            await db.TerrainBuilds.Where(x => x.Id == build.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, stamp));
        }

        return build.Id;
    }

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than the author holds, which is exactly what a fixture needs
    /// to do.
    /// </summary>
    private async Task GrantAsync(
        Guid userId, AccessDomain domain, AccessAction actions, AccessScopeKind scopeKind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = domain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = scopeKind,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        holder?.Dispose();
        outsider?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }
}
