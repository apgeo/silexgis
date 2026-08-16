// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// The step that reads a finished pyramid back and decides whether it may be believed.
///
/// <para>
/// Every case here is a pyramid this test builds on disk, because every one of them is a shape that
/// nothing else in the world would report. A tile that is not a tile, a level advertised and empty,
/// tiles held and advertised nowhere, tiles written one way and served as another: all of them
/// answer every request and draw plausible ground at the wrong height, in silence. The only place
/// they can be caught is in the bytes, so the bytes are what these tests write.
/// </para>
///
/// <para>
/// The other half is the stamp. A pyramid leaves the tile-maker carrying filler where its credit
/// should be and the same constant version as every other pyramid it has ever made — and that
/// version is the cache key on the end of every tile address, so leaving it alone means a rebuilt
/// pyramid is served out of viewers' own caches for a week with no request reaching the server.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainValidatePhaseTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;

    public TerrainValidatePhaseTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tcheck-{Guid.NewGuid():N}");
        factory = Installation();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }

    public void Dispose()
    {
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

    /// <summary>
    /// The ordinary case, and everything the step is responsible for at once: the pyramid passes,
    /// the credit becomes the credit of the data this build was actually made from, the filler the
    /// tile-maker left is gone, and the version on the end of every tile address comes from the
    /// tiles themselves.
    /// </summary>
    [Fact]
    public async Task A_whole_pyramid_is_accepted_stamped_and_credited_to_what_it_was_made_from()
    {
        var buildId = await SeedAsync("Copernicus DEM", "Copernicus DEM", "A county lidar survey");
        Pyramid(TilesOf(buildId));

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        build.ErrorCode.ShouldBeNull();
        build.Phase.ShouldBe(TerrainBuildPhase.Validate);
        build.PyramidVersion.ShouldNotBeNull().ShouldStartWith("1.1.0-");
        build.SizeBytes.ShouldNotBeNull().ShouldBeGreaterThan(0);

        var manifest = Manifest(buildId);

        // Repeats folded together, in the order the sources were recorded, and never a constant:
        // a pyramid that names data it does not hold is a false statement on a viewer's screen.
        manifest["attribution"]!.GetValue<string>().ShouldBe("Copernicus DEM; A county lidar survey");

        // The name travels with the credit. Kept, it would have the pyramid naming the tile-maker's
        // idea of the source while crediting ours.
        manifest["name"].ShouldBeNull();

        // Filler is worse than nothing, because it is displayed.
        manifest["description"]!.GetValue<string>().ShouldNotStartWith("insert ");
        manifest["legend"].ShouldBeNull();

        // The stamp on the manifest and the stamp on the row are the same statement.
        manifest["version"]!.GetValue<string>().ShouldBe(build.PyramidVersion);

        // Everything the viewer needs and we did not write is still there.
        manifest["format"]!.GetValue<string>().ShouldBe("quantized-mesh-1.0");
    }

    /// <summary>
    /// A tile that is not a tile — what a disk that filled or a run that was killed leaves behind.
    /// The pyramid around it is perfect, which is exactly why nothing else would notice.
    /// </summary>
    [Fact]
    public async Task A_damaged_tile_stops_the_build_and_nothing_is_recorded_as_checked()
    {
        var buildId = await SeedAsync("Copernicus DEM");
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        // In its right place and advertised, and full of the zeros a disk that filled leaves. It
        // satisfies every plausibility test there is except the one that asks for vertices.
        File.WriteAllBytes(Path.Combine(tiles, "0", "1", "0.terrain"), new byte[92]);

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.PyramidDamaged);

        // Nothing that reads as "this was checked" is written by a check that did not pass, and the
        // manifest keeps the tile-maker's own constant rather than one of ours.
        build.PyramidVersion.ShouldBeNull();
        Manifest(buildId)["version"].ShouldBeNull();
    }

    /// <summary>
    /// A level the pyramid says it holds and holds nothing at. A viewer asks, is answered with
    /// nothing, and quietly draws the coarser level above — ground at the wrong resolution,
    /// presented as ground.
    /// </summary>
    [Fact]
    public async Task A_level_advertised_and_empty_stops_the_build()
    {
        var buildId = await SeedAsync("Copernicus DEM");
        var tiles = TilesOf(buildId);
        Pyramid(tiles, levels: 2);
        Directory.Delete(Path.Combine(tiles, "1"), recursive: true);

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.PyramidLevelEmpty);
    }

    /// <summary>
    /// The other direction, and the only shape that passes every other check: tiles on disk that
    /// the manifest advertises nowhere. Nothing ever asks for them — a viewer reads the manifest,
    /// sees no ground there, and sends no request — so the hillside is simply bare.
    ///
    /// <para>
    /// The same pyramid with the same tile advertised is checked here too, so that this is a test of
    /// the rule rather than of the fixture happening to be refused for some other reason.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_tile_the_manifest_does_not_advertise_stops_the_build()
    {
        var buildId = await SeedAsync("Copernicus DEM");
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        // Well outside the ring the tile-maker writes around what it advertises, which the fixture
        // already carries: this is a rectangle from somewhere else entirely, which is the shape a
        // pyramid added to in place leaves behind.
        WriteTile(Path.Combine(tiles, "0", "9", "9.terrain"));

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.PyramidUnadvertised);

        var second = await SeedAsync("Copernicus DEM");
        var advertised = TilesOf(second);
        Pyramid(advertised, columns: 10, rows: 10);
        WriteTile(Path.Combine(advertised, "0", "9", "9.terrain"));

        await RunAsync(second);
        (await ReadAsync(second)).Status.ShouldBe(TerrainBuildStatus.Succeeded);
    }

    /// <summary>
    /// Tiles written two different ways. Whatever serves them declares one encoding for the whole
    /// directory, so half of them would be described by bytes they do not contain.
    /// </summary>
    [Fact]
    public async Task A_pyramid_whose_tiles_are_not_all_written_the_same_way_stops_the_build()
    {
        var buildId = await SeedAsync("Copernicus DEM");
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        var compressed = new byte[128];
        compressed[0] = 0x1f;
        compressed[1] = 0x8b;
        compressed[2] = 0x08;
        File.WriteAllBytes(Path.Combine(tiles, "0", "1", "0.terrain"), compressed);

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.PyramidMixedEncoding);
    }

    /// <summary>
    /// The version is a cache key and nothing else: it must not move when the pyramid has not, and
    /// it must move when any byte of it has. Measured rather than argued about — a pyramid rebuilt
    /// with every height changed was served entirely out of browsers' caches because the version
    /// did not move.
    /// </summary>
    [Fact]
    public async Task The_version_holds_still_for_the_same_tiles_and_moves_for_different_ones()
    {
        var buildId = await SeedAsync("Copernicus DEM");
        var tiles = TilesOf(buildId);
        Pyramid(tiles);

        await RunAsync(buildId);
        var first = (await ReadAsync(buildId)).PyramidVersion.ShouldNotBeNull();

        // Checked again over an unchanged pyramid: the same string, so every address a browser
        // already holds keeps answering and nothing is fetched twice.
        await RewindAsync(buildId);
        await RunAsync(buildId);
        (await ReadAsync(buildId)).PyramidVersion.ShouldBe(first);

        // One tile's heights changed, which is what a rebuild over corrected data produces.
        WriteTile(Path.Combine(tiles, "0", "0", "0.terrain"), lowest: 11.5f, highest: 1902.25f);

        await RewindAsync(buildId);
        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded);
        build.PyramidVersion.ShouldNotBe(first);
        Manifest(buildId)["version"]!.GetValue<string>().ShouldBe(build.PyramidVersion);
    }

    private string TilesOf(Guid buildId) =>
        Path.Combine(buildRoot, buildId.ToString("N"), "tiles");

    private JsonObject Manifest(Guid buildId) =>
        (JsonObject)JsonNode.Parse(
            File.ReadAllText(Path.Combine(TilesOf(buildId), "layer.json")))!;

    /// <summary>
    /// A pyramid as the tile-maker leaves one: a manifest carrying its filler and its constant
    /// version, and at every level it says it holds, the rectangle it advertises plus the ring of
    /// tiles one wide that it always writes around it.
    /// </summary>
    /// <remarks>
    /// The ring is the part that has to be here. Read off a pyramid this pipeline actually baked —
    /// a manifest advertising columns 1154 to 1157 over tiles running 1153 to 1158 — it is written
    /// on every bake at every level, so a fixture whose tiles match its own manifest exactly is a
    /// shape that cannot occur, and a check passing against it says nothing about whether a real
    /// pyramid would pass.
    /// </remarks>
    private static void Pyramid(string tiles, int levels = 1, int columns = 2, int rows = 1)
    {
        var available = new JsonArray();
        for (var level = 0; level < levels; level++)
        {
            available.Add(new JsonArray(new JsonObject
            {
                ["startX"] = 0,
                ["startY"] = 0,
                ["endX"] = columns - 1,
                ["endY"] = rows - 1,
            }));
        }

        var manifest = new JsonObject
        {
            ["tilejson"] = "2.1.0",
            ["name"] = "insert name here",
            ["description"] = "insert description here",
            ["attribution"] = "insert attribution here",
            ["format"] = "quantized-mesh-1.0",
            ["scheme"] = "tms",
            ["legend"] = "legend.png",
            ["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}"),
            ["available"] = available,
        };

        Directory.CreateDirectory(tiles);
        File.WriteAllText(Path.Combine(tiles, "layer.json"), manifest.ToJsonString());

        // One past the advertised rectangle on each far side. The near side would be one before
        // column zero, which no pyramid can hold, and a real one is clipped there in exactly the
        // same way.
        for (var level = 0; level < levels; level++)
        {
            for (var x = 0; x <= columns; x++)
            {
                for (var y = 0; y <= rows; y++)
                {
                    WriteTile(Path.Combine(
                        tiles,
                        level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".terrain"));
                }
            }
        }
    }

    /// <summary>A tile whose header describes ground, and a body long enough to hold it.</summary>
    private static void WriteTile(string path, float lowest = 210.5f, float highest = 1840.25f)
    {
        const uint vertices = 12;
        var tile = new byte[92 + (vertices * 6)];
        BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(0), 4_200_000.5);
        BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(8), 1_700_000.25);
        BinaryPrimitives.WriteDoubleLittleEndian(tile.AsSpan(16), 4_600_000.75);
        BinaryPrimitives.WriteSingleLittleEndian(tile.AsSpan(24), lowest);
        BinaryPrimitives.WriteSingleLittleEndian(tile.AsSpan(28), highest);
        BinaryPrimitives.WriteUInt32LittleEndian(tile.AsSpan(88), vertices);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, tile);
    }

    private async Task<Guid> SeedAsync(params string[] attributions)
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
            Status = TerrainBuildStatus.Queued,
            Phase = TerrainBuildPhase.Pending,
            HeightDatum = TerrainHeightDatum.Orthometric,
        };

        db.TerrainBuilds.Add(build);

        foreach (var attribution in attributions)
        {
            db.TerrainBuildSources.Add(new TerrainBuildSource
            {
                TerrainBuildId = build.Id,
                Kind = TerrainBuildSourceKind.Fetched,
                Reference = "a cell of elevation",
                Attribution = attribution,
            });
        }

        await db.SaveChangesAsync();
        return build.Id;
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    /// <summary>Puts a build back the way a run that is about to be repeated finds it.</summary>
    private async Task RewindAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.ErrorCode, (string?)null)
            .SetProperty(b => b.Message, (string?)null)
            .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null));
    }

    private async Task RunAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        try
        {
            await handler.ExecuteAsync(
                new ProcessingJob
                {
                    Kind = ProcessingJobKinds.TerrainBuild,
                    Attempts = 1,
                    Payload = JsonSerializer.Serialize(
                        new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A step that stops throws a bounded stand-in so the queue sees a failed job. The reason
            // is recorded on the build row first, and the row is what every test here reads.
        }
    }

    /// <summary>
    /// An installation whose earlier steps are stood in for and whose checking step is the real one.
    /// </summary>
    /// <remarks>
    /// The steps before this one are replaced rather than joined, because the walk takes the first
    /// implementation claiming a step and a stand-in registered beside a real one would never be
    /// asked. What each of them does is tested where it lives; here they only have to say they have
    /// already done it, so that the walk reaches the step this class is about.
    /// </remarks>
    private SilexGisApiFactory Installation()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Terrain:BuildRoot"] = buildRoot,
        };

        return new SilexGisApiFactory(postgres.ConnectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);

            foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
            {
                services.Remove(step);
            }

            services.AddSingleton<ITerrainPhase>(new DoneStep(TerrainBuildPhase.Fetch));
            services.AddSingleton<ITerrainPhase>(new DoneStep(TerrainBuildPhase.Prepare));
            services.AddSingleton<ITerrainPhase>(new DoneStep(TerrainBuildPhase.Bake));
            services.AddScoped<ITerrainPhase, TerrainValidatePhase>();
        });
    }

    /// <summary>A step whose work this class writes to disk itself.</summary>
    private sealed class DoneStep(TerrainBuildPhase phase) : ITerrainPhase
    {
        public TerrainBuildPhase Phase => phase;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(true);

        public Task RunAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
