// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// A build that adds rasters to a finished one instead of meshing a pyramid from nothing.
///
/// <para>
/// The tile-maker's in-place addition was measured on a pilot: it keeps every tile and rewrites the
/// manifest from the new rasters alone, so every tile of the base is still on disk and advertised
/// nowhere, and the pyramid draws nothing but the newest patch. That shape validates as damaged
/// nowhere, which is why the check refuses a tile the manifest does not advertise — and why an
/// extension has to put the base's coverage back before that check runs. These tests drive both
/// halves: the request that is the only way the addition can be asked for, and the merge that makes
/// what comes back a pyramid the check will pass.
/// </para>
///
/// <para>
/// The program that makes tiles is stood in for by writing what it would write: the far side of the
/// handover is a directory, so a test can be the far side, and the exact manifest the pilot saw can
/// be produced without a container.
/// </para>
/// </summary>
public sealed class TerrainBuildExtendTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The version the base pyramid was stamped with when it was checked.</summary>
    private const string BaseVersion = "1.1.0-0123456789ab";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly string spoolRoot;
    private readonly string publishRoot;

    private HttpClient operating = null!;  // an ordinary account granted Read, Execute and Delete on terrain
    private Guid operatorId;

    public TerrainBuildExtendTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        var scratch = Path.Combine(TestScratch.Root, $"silexgis-textend-{Guid.NewGuid():N}");
        buildRoot = Path.Combine(scratch, "builds");
        spoolRoot = Path.Combine(scratch, "spool");
        publishRoot = Path.Combine(scratch, "published");
        factory = Installation();
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        operatorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tx-op-{suffix}@t.local");
        operating = await AuthHelper.BearerClientAsync(factory, $"tx-op-{suffix}@t.local");
        await GrantAsync(operatorId, AccessAction.Read | AccessAction.Execute | AccessAction.Delete);
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.TerrainBuild).ExecuteDeleteAsync();
        await db.TerrainBuilds.ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }

    public void Dispose()
    {
        operating?.Dispose();

        try
        {
            var scratch = Path.GetDirectoryName(buildRoot);
            if (scratch is not null && Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    /// <summary>
    /// Every reason a base cannot be added to, each with a code of its own, and then the pass that
    /// proves the refusals were about the base and not about the request.
    /// </summary>
    [Fact]
    public async Task A_base_that_is_missing_unchecked_not_on_disk_or_built_to_another_depth_is_refused_with_its_own_code()
    {
        var raster = await UploadAsync();

        var missing = await SubmitAsync(Extension(Guid.NewGuid(), raster));
        var missingBody = await BodyAsync(missing);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest, missingBody);
        CodeOf(missingBody).ShouldBe(TerrainBuildEndpoints.BaseNotFoundCode);

        // Finished as far as the row says, and never checked: the version is the only mark that
        // the tiles were ever read back and found whole.
        var neverChecked = await SeedBaseAsync(checkedWhole: false, published: false);
        var neverCheckedBody = await BodyAsync(await SubmitAsync(Extension(neverChecked, raster)));
        CodeOf(neverCheckedBody).ShouldBe(TerrainBuildEndpoints.BaseUncheckedCode);

        var unpublished = await SeedBaseAsync(checkedWhole: true, published: false);
        var unpublishedBody = await BodyAsync(await SubmitAsync(Extension(unpublished, raster)));
        CodeOf(unpublishedBody).ShouldBe(TerrainBuildEndpoints.BaseNotPublishedCode);

        var good = await SeedBaseAsync(checkedWhole: true, published: true, "Copernicus DEM");

        var deeper = await SubmitAsync(Extension(good, raster, depth: 14));
        var deeperBody = await BodyAsync(deeper);
        deeper.StatusCode.ShouldBe(HttpStatusCode.BadRequest, deeperBody);
        CodeOf(deeperBody).ShouldBe(TerrainBuildEndpoints.BaseDepthDiffersCode);

        // The sentence says what the base was built to and why that is the only depth there is.
        JsonDocument.Parse(deeperBody).RootElement.GetProperty("detail").GetString()
            .ShouldNotBeNull().ShouldContain("level 13");

        var otherDatum = await SubmitAsync(Extension(good, raster, datum: TerrainHeightDatum.Ellipsoidal));
        CodeOf(await BodyAsync(otherDatum)).ShouldBe(TerrainBuildEndpoints.BaseDatumDiffersCode);

        // Coverage is not obtained again for an extension unless asked, so one adding nothing
        // else adds nothing at all.
        var nothing = await SubmitAsync(Extension(good, uploaded: null));
        CodeOf(await BodyAsync(nothing)).ShouldBe(TerrainBuildEndpoints.NoSourcesCode);

        // The pass that makes the refusals above mean something.
        var accepted = await SubmitAsync(Extension(good, raster));
        var body = await BodyAsync(accepted);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, body);

        var created = JsonDocument.Parse(body).RootElement;
        created.GetProperty("baseBuildId").GetGuid().ShouldBe(good);
        created.GetProperty("status").GetString().ShouldBe("queued");

        // The new build covers the base's ground and the ground being added together. The patch
        // here lies inside the base, so the union is the base's rectangle.
        var build = await ReadAsync(created.GetProperty("id").GetGuid());
        build.BaseBuildId.ShouldBe(good);
        build.Extent.EnvelopeInternal.ShouldBe(new Envelope(25.0, 25.1, 46.0, 46.1));

        // The base's sources come along, marked, so the credit composes; the new raster is the
        // build's own.
        var detail = await operating.GetAsync($"/api/v1/terrain/builds/{build.Id}");
        var detailBody = await BodyAsync(detail);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, detailBody);
        var sources = JsonDocument.Parse(detailBody).RootElement.GetProperty("sources").EnumerateArray().ToList();
        sources.Count.ShouldBe(2);
        sources[0].GetProperty("attribution").GetString().ShouldBe("Copernicus DEM");
        sources[0].GetProperty("inherited").GetBoolean().ShouldBeTrue();
        sources[1].GetProperty("attribution").GetString().ShouldBe("A county lidar survey");
        sources[1].GetProperty("inherited").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The whole chain, with the tile-maker doing exactly what the pilot measured it doing: the
    /// request asks for an addition to a copy of the base, the manifest that comes back advertises
    /// the new patch alone, and what is published advertises everything, carries the composed
    /// credit, and is stamped with a version of its own. The base is untouched throughout.
    /// </summary>
    [Fact]
    public async Task An_extension_is_added_to_a_copy_of_the_base_and_publishes_the_union_with_the_composed_credit()
    {
        var baseId = await SeedBaseAsync(checkedWhole: true, published: true, "Copernicus DEM");
        var basePublished = Path.Combine(publishRoot, baseId.ToString("N"));
        var baseManifestBefore = File.ReadAllText(Path.Combine(basePublished, "layer.json"));

        var extensionId = await AcceptedAsync(Extension(baseId, await UploadAsync()));

        string? requestText = null;
        string? outputManifestWhenTaken = null;
        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            requestText = File.ReadAllText(Path.Combine(directory, "request"));

            // What the output directory held the moment the request was taken: the base's whole
            // pyramid, manifest included, or there would be nothing to add to.
            var manifest = Path.Combine(request["output"], "layer.json");
            outputManifestWhenTaken = File.Exists(manifest) ? File.ReadAllText(manifest) : null;

            AddPatchAsTheTileMakerDoes(request["output"]);
            Say(directory, "Generating tiles for level 1", "Done");
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(extensionId);

        var build = await ReadAsync(extensionId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? build.LogTail ?? "");
        build.Phase.ShouldBe(TerrainBuildPhase.Publish);

        // The flag reaches the tile-maker through the request and the request alone, and the
        // output it is asked to add to is this build's own tiles directory, seeded from the base.
        requestText.ShouldNotBeNull().ShouldContain("modify=true\n");
        requestText.ShouldContain($"output={Path.Combine(buildRoot, extensionId.ToString("N"), "tiles")}\n");
        outputManifestWhenTaken.ShouldBe(baseManifestBefore);

        var published = Path.Combine(publishRoot, extensionId.ToString("N"));
        var manifest = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(published, "layer.json")))!;
        var levels = TerrainManifest.Available(manifest);

        // Every rectangle the base advertised is still there, and the patch beside it. This is the
        // pilot's failure undone: the tile-maker's own manifest held only the second pair.
        levels.Count.ShouldBe(2);
        levels[0].ShouldBe([new TerrainTileRange(0, 0, 1, 0), new TerrainTileRange(1, 0, 1, 0)]);
        levels[1].ShouldBe([new TerrainTileRange(0, 0, 3, 1), new TerrainTileRange(6, 4, 7, 5)]);
        TerrainManifest.Bounds(manifest).ShouldBe([25.0, 46.0, 25.1, 46.1]);

        // The credit composes from the base's sources and the new raster's, in that order, and the
        // version is this pyramid's own rather than the base's or the tile-maker's constant.
        manifest["attribution"]!.GetValue<string>().ShouldBe("Copernicus DEM; A county lidar survey");
        manifest["name"].ShouldBeNull();
        manifest["legend"].ShouldBeNull();
        var version = manifest["version"]!.GetValue<string>();
        version.ShouldStartWith("1.1.0-");
        version.ShouldNotBe(BaseVersion);
        version.ShouldNotBe("1.1.0");
        build.PyramidVersion.ShouldBe(version);

        // Both the base's tiles and the patch's are in the published pyramid.
        File.Exists(Path.Combine(published, "1", "0", "0.terrain")).ShouldBeTrue();
        File.Exists(Path.Combine(published, "1", "6", "4.terrain")).ShouldBeTrue();

        // The base is exactly as it was, and is still published.
        File.ReadAllText(Path.Combine(basePublished, "layer.json")).ShouldBe(baseManifestBefore);
        (await ReadAsync(baseId)).Status.ShouldBe(TerrainBuildStatus.Succeeded);

        // A good bake takes its handover directory with it.
        Directory.Exists(Path.Combine(spoolRoot, extensionId.ToString("N"))).ShouldBeFalse();
    }

    /// <summary>
    /// The guard bites: the tile-maker's bare output — the base's tiles with a manifest describing
    /// only the patch — is refused by the check, and the same tiles under the merged manifest pass.
    /// </summary>
    /// <remarks>
    /// This is what makes the merge load-bearing rather than tidy. Skipped, an extension would not
    /// fail loudly at the bake; it would reach the check with precisely this shape, and the check
    /// is the only thing between it and a published pyramid that draws one patch.
    /// </remarks>
    [Fact]
    public async Task The_check_refuses_the_tile_makers_bare_output_and_accepts_it_once_merged()
    {
        var buildId = await SeedPlainBuildAsync("Copernicus DEM");
        var tiles = Path.Combine(buildRoot, buildId.ToString("N"), "tiles");

        WriteBasePyramid(tiles);
        var baseManifest = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(tiles, "layer.json")))!;
        AddPatchAsTheTileMakerDoes(tiles);
        var bare = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(tiles, "layer.json")))!;

        // No spool beside a pyramid on disk is a bake already adjudicated, so the walk goes
        // straight to the check.
        await RunAsync(buildId);

        var refused = await ReadAsync(buildId);
        refused.Status.ShouldBe(TerrainBuildStatus.Failed);
        refused.ErrorCode.ShouldBe(TerrainBuildFailures.PyramidUnadvertised);
        refused.PyramidVersion.ShouldBeNull();

        // The same tiles, with the base's coverage put back.
        File.WriteAllText(
            Path.Combine(tiles, "layer.json"), TerrainManifest.Merge(baseManifest, bare).ToJsonString());
        await RewindAsync(buildId);
        await RunAsync(buildId);

        var accepted = await ReadAsync(buildId);
        accepted.Status.ShouldBe(TerrainBuildStatus.Succeeded, accepted.ErrorCode ?? accepted.Message ?? "");
        accepted.Phase.ShouldBe(TerrainBuildPhase.Publish);
    }

    /// <summary>
    /// A run stopped between copying the base's pyramid in and writing the request leaves a whole
    /// pyramid on disk that no bake has touched. The run that comes back meshes rather than reading
    /// that copy as finished work — otherwise the base would be checked, published and drawn as the
    /// extension, with the new rasters never meshed and nothing anywhere saying so.
    /// </summary>
    [Fact]
    public async Task A_handover_interrupted_after_the_base_was_copied_in_is_started_over_rather_than_read_as_done()
    {
        var baseId = await SeedBaseAsync(checkedWhole: true, published: true, "Copernicus DEM");
        var extensionId = await AcceptedAsync(Extension(baseId, await UploadAsync()));

        // Exactly what that interruption leaves: the handover directory made and empty, and the
        // tiles directory holding the base's pyramid.
        Directory.CreateDirectory(Path.Combine(spoolRoot, extensionId.ToString("N")));
        TerrainWorkspace.Copy(
            Path.Combine(publishRoot, baseId.ToString("N")),
            Path.Combine(buildRoot, extensionId.ToString("N"), "tiles"));

        var bakes = 0;
        await using var worker = new FakeBakeWorker(spoolRoot, (directory, request) =>
        {
            Interlocked.Increment(ref bakes);
            AddPatchAsTheTileMakerDoes(request["output"]);
            Answer(directory, "succeeded", exit: 0);
        });

        await RunAsync(extensionId);

        var build = await ReadAsync(extensionId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");
        Volatile.Read(ref bakes).ShouldBe(1);

        var published = Path.Combine(publishRoot, extensionId.ToString("N"));
        File.Exists(Path.Combine(published, "1", "6", "4.terrain")).ShouldBeTrue();
    }

    /// <summary>
    /// A base is kept while an extension of it has not finished, because that extension still
    /// reads the base's pyramid; afterwards it can go, and the extension only loses the record of
    /// where it started.
    /// </summary>
    [Fact]
    public async Task A_base_cannot_be_deleted_while_an_extension_of_it_is_unfinished_and_can_afterwards()
    {
        var baseId = await SeedBaseAsync(checkedWhole: true, published: true, "Copernicus DEM");
        var extensionId = await AcceptedAsync(Extension(baseId, await UploadAsync()));

        var refused = await operating.DeleteAsync($"/api/v1/terrain/builds/{baseId}");
        var refusedBody = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, refusedBody);
        CodeOf(refusedBody).ShouldBe(TerrainBuildEndpoints.BeingExtendedCode);
        (await ReadAsync(baseId)).Status.ShouldBe(TerrainBuildStatus.Succeeded);

        await FinishAsync(extensionId);

        var allowed = await operating.DeleteAsync($"/api/v1/terrain/builds/{baseId}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await BodyAsync(allowed));

        var extension = await ReadAsync(extensionId);
        extension.BaseBuildId.ShouldBeNull();
        extension.Status.ShouldBe(TerrainBuildStatus.Succeeded);
    }

    /// <summary>
    /// A request to add a patch inside the base's rectangle, from one uploaded raster.
    /// </summary>
    private static TerrainBuildSubmitRequest Extension(
        Guid baseId,
        string? uploaded,
        int depth = 13,
        TerrainHeightDatum? datum = null) =>
        new(
            25.05, 46.05, 25.09, 46.09, depth, datum, null,
            FetchCoverage: null,
            Sources: uploaded is null
                ? null
                : [new TerrainBuildSourceRequest(
                    TerrainBuildSourceKind.Uploaded, uploaded, "A county lidar survey", "Used with permission")],
            BaseBuildId: baseId);

    private Task<HttpResponseMessage> SubmitAsync(TerrainBuildSubmitRequest request) =>
        operating.PostAsJsonAsync("/api/v1/terrain/builds", request);

    private async Task<Guid> AcceptedAsync(TerrainBuildSubmitRequest request)
    {
        var response = await SubmitAsync(request);
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Sends a raster through the browser's route and answers with its reference.</summary>
    private async Task<string> UploadAsync()
    {
        var content = new ByteArrayContent([0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00]);
        content.Headers.ContentType = new("image/tiff");

        using var form = new MultipartFormDataContent { { content, "file", "survey.tif" } };
        var response = await operating.PostAsync("/api/v1/terrain/rasters", form);
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("reference").GetString()!;
    }

    /// <summary>
    /// A finished build, with or without the mark of having been checked and with or without its
    /// pyramid where terrain is served from.
    /// </summary>
    private async Task<Guid> SeedBaseAsync(bool checkedWhole, bool published, params string[] attributions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var build = new TerrainBuild
        {
            Extent = Rectangle(),
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Succeeded,
            Phase = TerrainBuildPhase.Publish,
            Progress = 100,
            PyramidVersion = checkedWhole ? BaseVersion : null,
            SizeBytes = 4096,
            HeightDatum = TerrainHeightDatum.Orthometric,
            FinishedAt = DateTimeOffset.UtcNow,
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

        if (published)
        {
            WriteBasePyramid(Path.Combine(publishRoot, build.Id.ToString("N")), stamped: true);
        }

        return build.Id;
    }

    /// <summary>A build from nothing, queued, whose tiles this class writes itself.</summary>
    private async Task<Guid> SeedPlainBuildAsync(params string[] attributions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var build = new TerrainBuild
        {
            Extent = Rectangle(),
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

    private static Polygon Rectangle() =>
        new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon(
        [
            new Coordinate(25.0, 46.0),
            new Coordinate(25.1, 46.0),
            new Coordinate(25.1, 46.1),
            new Coordinate(25.0, 46.1),
            new Coordinate(25.0, 46.0),
        ]);

    /// <summary>
    /// The base pyramid: two levels, each advertising one rectangle, with the ring of tiles one
    /// wide that the tile-maker always writes around what it advertises.
    /// </summary>
    /// <param name="stamped">
    /// Whether the manifest reads as one this application checked — its own credit and a version
    /// derived from the tiles — rather than as the tile-maker left it.
    /// </param>
    private static void WriteBasePyramid(string tiles, bool stamped = false)
    {
        var manifest = new JsonObject
        {
            ["tilejson"] = "2.1.0",
            ["format"] = "quantized-mesh-1.0",
            ["scheme"] = "tms",
            ["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}"),
            ["bounds"] = new JsonArray(25.0, 46.0, 25.1, 46.1),
            ["available"] = new JsonArray(
                new JsonArray(Range(0, 0, 1, 0)),
                new JsonArray(Range(0, 0, 3, 1))),
        };

        if (stamped)
        {
            manifest["attribution"] = "Copernicus DEM";
            manifest["description"] = TerrainManifest.PlainDescription;
            manifest["version"] = BaseVersion;
        }
        else
        {
            manifest["name"] = "insert name here";
            manifest["description"] = "insert description here";
            manifest["attribution"] = "insert attribution here";
            manifest["legend"] = "legend.png";
            manifest["version"] = "1.1.0";
        }

        Directory.CreateDirectory(tiles);
        File.WriteAllText(Path.Combine(tiles, "layer.json"), manifest.ToJsonString());

        for (var x = 0; x <= 2; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                WriteTile(Path.Combine(tiles, "0", Invariant(x), Invariant(y) + ".terrain"));
            }
        }

        for (var x = 0; x <= 4; x++)
        {
            for (var y = 0; y <= 2; y++)
            {
                WriteTile(Path.Combine(tiles, "1", Invariant(x), Invariant(y) + ".terrain"));
            }
        }
    }

    /// <summary>
    /// What the tile-maker's in-place addition leaves, as measured on the pilot: the tiles already
    /// there untouched, the new patch's tiles written (with the usual ring), and the manifest
    /// rewritten from the new raster alone — its bounds, its rectangles, and the tool's own
    /// constants where the credit and the version were.
    /// </summary>
    private static void AddPatchAsTheTileMakerDoes(string tiles)
    {
        for (var x = 5; x <= 8; x++)
        {
            for (var y = 3; y <= 6; y++)
            {
                WriteTile(Path.Combine(tiles, "1", Invariant(x), Invariant(y) + ".terrain"), lowest: 300f, highest: 900f);
            }
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
            ["version"] = "1.1.0",
            ["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}"),
            ["bounds"] = new JsonArray(25.05, 46.05, 25.09, 46.09),
            ["available"] = new JsonArray(
                new JsonArray(Range(1, 0, 1, 0)),
                new JsonArray(Range(6, 4, 7, 5))),
        };

        File.WriteAllText(Path.Combine(tiles, "layer.json"), manifest.ToJsonString());
    }

    private static JsonObject Range(int startX, int startY, int endX, int endY) => new()
    {
        ["startX"] = startX,
        ["startY"] = startY,
        ["endX"] = endX,
        ["endY"] = endY,
    };

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

    private static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What the tile maker said, as it would have said it.</summary>
    private static void Say(string directory, params string[] lines) =>
        File.WriteAllText(Path.Combine(directory, "bake.log"), string.Join('\n', lines) + "\n");

    /// <summary>The answer, written under another name and renamed into place, as both sides must.</summary>
    private static void Answer(string directory, string status, int? exit = null)
    {
        var text = $"status={status}\nexit={exit}\nfinished={DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n";
        var partial = Path.Combine(directory, "result.part");
        File.WriteAllText(partial, text);
        File.Move(partial, Path.Combine(directory, "result"));
    }

    /// <summary>
    /// Runs the handler directly rather than waiting for a worker, so what is asserted is what the
    /// handler did and not how quickly something got to it.
    /// </summary>
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
                    RequestedBy = operatorId,
                    Payload = JsonSerializer.Serialize(
                        new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A step that stops throws a bounded stand-in, so that the queue sees a failed job. The
            // reason is recorded on the build row before it is thrown, and the row is what every
            // test in this class reads.
        }
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

    private async Task FinishAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(
            s => s.SetProperty(b => b.Status, TerrainBuildStatus.Succeeded));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than their author holds, which is exactly what a fixture
    /// needs to do.
    /// </summary>
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

    /// <summary>
    /// An installation whose first two steps are stood in for and whose meshing, checking and
    /// publishing steps are the real ones.
    /// </summary>
    /// <remarks>
    /// The steps before the bake are replaced rather than joined, because the walk takes the first
    /// implementation claiming a step and a stand-in registered beside a real one would never be
    /// asked. What each of them does is tested where it lives; here they only have to leave a
    /// prepared set behind without a raster library, a network or a minute of waiting.
    /// </remarks>
    private SilexGisApiFactory Installation()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Terrain:BuildRoot"] = buildRoot,
            ["Terrain:SpoolRoot"] = spoolRoot,
            ["Terrain:PublishRoot"] = publishRoot,
            ["Terrain:BakeEnabled"] = "true",
            ["Terrain:BakePickupSeconds"] = "1",
        };

        return new SilexGisApiFactory(postgres.ConnectionString, settings, services =>
        {
            JobWorkers.RemoveFrom(services);

            foreach (var step in services.Where(s => s.ServiceType == typeof(ITerrainPhase)).ToList())
            {
                services.Remove(step);
            }

            services.AddSingleton<ITerrainPhase, StubFetchPhase>();
            services.AddSingleton<ITerrainPhase, StubPreparePhase>();
            services.AddScoped<ITerrainPhase, TerrainBakePhase>();
            services.AddScoped<ITerrainPhase, TerrainValidatePhase>();
            services.AddScoped<ITerrainPhase, TerrainPublishPhase>();

            foreach (var registered in services
                .Where(s => s.ServiceType == typeof(ITerrainRasterPreparer)).ToList())
            {
                services.Remove(registered);
            }

            services.AddSingleton<ITerrainRasterPreparer, DirectoryPreparer>();
        });
    }

    /// <summary>Stands in for the step that obtains rasters. Nothing here needs one.</summary>
    private sealed class StubFetchPhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(true);

        public Task RunAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Stands in for the step that prepares them, leaving behind what a real one leaves: a file in
    /// the directory the meshing step reads.
    /// </summary>
    private sealed class StubPreparePhase : ITerrainPhase
    {
        public TerrainBuildPhase Phase => TerrainBuildPhase.Prepare;

        public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
            Task.FromResult(File.Exists(OneOf(context)));

        public async Task RunAsync(TerrainBuildContext context, CancellationToken ct) =>
            await File.WriteAllTextAsync(OneOf(context), "not really a raster", ct);

        private static string OneOf(TerrainBuildContext context) =>
            Path.Combine(context.Directories.Prepared, "survey.tif");
    }

    /// <summary>
    /// A raster chain that answers only the one question the meshing step asks it: whether the
    /// prepared set is there.
    /// </summary>
    private sealed class DirectoryPreparer : ITerrainRasterPreparer
    {
        public IReadOnlyList<PreparedTerrainRaster> Prepare(
            TerrainRasterPrepareRequest request, CancellationToken ct) =>
            DescribePrepared(request) ?? [];

        public IReadOnlyList<PreparedTerrainRaster>? DescribePrepared(
            TerrainRasterPrepareRequest request)
        {
            if (!Directory.Exists(request.OutputDirectory))
            {
                return null;
            }

            var files = Directory.GetFiles(request.OutputDirectory, "*.tif");
            return files.Length == 0 ? null : [.. files.Select(f => Describe(f)!)];
        }

        public PreparedTerrainRaster? Describe(string path) =>
            File.Exists(path)
                ? new PreparedTerrainRaster(path, 8, 8, 0.0125, 25, 46, 25.1, 46.1, -9999, 64)
                : null;
    }
}
