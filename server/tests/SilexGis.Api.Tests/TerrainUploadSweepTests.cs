// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Rasters sent for a build and never used are taken away once they have waited long enough, and
/// nothing any build names is ever taken.
/// </summary>
/// <remarks>
/// Both halves are asserted in one pass over one directory, because each is only meaningful beside
/// the other: a sweep that deleted nothing would pass every "kept" assertion, and one that deleted
/// everything old would pass every "taken" one.
/// </remarks>
public sealed class TerrainUploadSweepTests : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    public TerrainUploadSweepTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(TestScratch.Root, $"silexgis-tsweep-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom,
            ownHost: true);
    }

    [Fact]
    public async Task An_old_upload_no_build_names_is_taken_and_a_named_or_fresh_one_stays()
    {
        var uploads = Path.Combine(buildRoot, "uploads");
        Directory.CreateDirectory(uploads);
        var old = DateTime.UtcNow.AddHours(-48);

        var abandoned = Upload(uploads, old);
        var usedByFinished = Upload(uploads, old);
        var usedByStopped = Upload(uploads, old);
        var usedByWaiting = Upload(uploads, old);
        var justSent = Upload(uploads, DateTime.UtcNow.AddMinutes(-5));

        // What an interrupted upload leaves: a fragment, and a conversion's working directory.
        var fragment = Path.Combine(uploads, $"{Guid.NewGuid():N}.tif.part");
        File.WriteAllText(fragment, "half");
        File.SetLastWriteTimeUtc(fragment, old);
        var staging = Path.Combine(uploads, $"{Guid.NewGuid():N}.staging");
        Directory.CreateDirectory(staging);
        Directory.SetLastWriteTimeUtc(staging, old);

        // A stopped build may be run again and will read what it was made from, so it holds its
        // uploads exactly as a finished or a waiting one does.
        await SeedBuildAsync(TerrainBuildStatus.Succeeded, usedByFinished);
        await SeedBuildAsync(TerrainBuildStatus.Failed, usedByStopped);
        await SeedBuildAsync(TerrainBuildStatus.Queued, usedByWaiting);

        await SweepAsync();

        File.Exists(Path.Combine(uploads, abandoned)).ShouldBeFalse();
        File.Exists(fragment).ShouldBeFalse();
        Directory.Exists(staging).ShouldBeFalse();

        File.Exists(Path.Combine(uploads, usedByFinished)).ShouldBeTrue();
        File.Exists(Path.Combine(uploads, usedByStopped)).ShouldBeTrue();
        File.Exists(Path.Combine(uploads, usedByWaiting)).ShouldBeTrue();
        File.Exists(Path.Combine(uploads, justSent)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(24, 24)]
    [InlineData(1_000_000, 8760)]
    public void The_retention_is_brought_inside_an_hour_to_a_year(int hours, int expected) =>
        TerrainUploadSweepHandler.Retention(new TerrainBuildOptions { UploadRetentionHours = hours })
            .ShouldBe(TimeSpan.FromHours(expected));

    private static string Upload(string uploads, DateTime writtenAt)
    {
        var name = $"{Guid.NewGuid():N}.tif";
        var path = Path.Combine(uploads, name);
        File.WriteAllText(path, "an elevation raster");
        File.SetLastWriteTimeUtc(path, writtenAt);
        return name;
    }

    private async Task SeedBuildAsync(TerrainBuildStatus status, string upload)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var build = new TerrainBuild
        {
            Extent = new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
            [
                new Coordinate(25.0, 46.0),
                new Coordinate(25.1, 46.0),
                new Coordinate(25.1, 46.1),
                new Coordinate(25.0, 46.1),
                new Coordinate(25.0, 46.0),
            ]),
            RequestedMaxDepth = 13,
            Status = status,
            ErrorCode = status == TerrainBuildStatus.Failed ? TerrainBuildFailures.BakeUnavailable : null,
            HeightDatum = TerrainHeightDatum.Orthometric,
        };
        db.TerrainBuilds.Add(build);
        db.TerrainBuildSources.Add(new TerrainBuildSource
        {
            TerrainBuildId = build.Id,
            Kind = TerrainBuildSourceKind.Uploaded,
            Reference = upload,
            Attribution = "a survey of our own",
        });
        await db.SaveChangesAsync();
    }

    private async Task SweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainUploadSweep);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TerrainUploadSweep }, CancellationToken.None);
    }

    public void Dispose()
    {
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .TerrainBuilds.ExecuteDelete();
        }

        factory.Dispose();

        try
        {
            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }
}
