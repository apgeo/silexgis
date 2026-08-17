// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Moving a checked pyramid to where it is served from.
///
/// <para>
/// Everything this step protects is invisible from outside. A pyramid published while it is still
/// being written answers the one probe a viewer makes — which reads the coarsest tile and nothing
/// else — and then fails at depth, drawing the coarse level's heights as though they were the fine
/// ones, with no error on either side. So what is asserted here is not "the files ended up
/// somewhere" but that the address a viewer asks for never holds a partial pyramid, that a build's
/// tiles get an address of their own rather than replacing what sits behind a shared one, and that
/// the source rasters a build was given are nowhere near the directory that gets served.
/// </para>
///
/// <para>
/// No database and no HTTP: this step touches neither, and giving it a container would only make
/// the same assertions take a minute each.
/// </para>
/// </summary>
public sealed class TerrainPublishPhaseTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "silexgis-publish-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly TerrainWorkspace workspace;
    private readonly TerrainPublishPhase phase;

    public TerrainPublishPhaseTests()
    {
        workspace = new TerrainWorkspace(
            Options.Create(new TerrainBuildOptions
            {
                BuildRoot = Path.Combine(root, "builds"),
                SpoolRoot = Path.Combine(root, "spool"),
                PublishRoot = Path.Combine(root, "published"),
            }),
            NullLogger<TerrainWorkspace>.Instance);

        phase = new TerrainPublishPhase(workspace);
    }

    [Fact]
    public async Task A_checked_pyramid_is_moved_to_an_address_of_its_own()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "the first bake");

        await phase.RunAsync(Context(build, directories), CancellationToken.None);

        var published = workspace.PublishedFor(build.Id);
        File.ReadAllText(Path.Combine(published, TerrainPyramid.ManifestFileName))
            .ShouldContain("the first bake");
        File.Exists(Path.Combine(published, "0", "0", "0" + TerrainPyramid.TileExtension)).ShouldBeTrue();

        // Named the same as the address it is fetched from, so that a directory on disk and a
        // request from a browser cannot come to mean different things.
        Path.GetFileName(published).ShouldBe(TerrainPyramid.PublishedName(build.Id));
        TerrainPyramid.PublishedUrl(build.Id).ShouldEndWith(TerrainPyramid.PublishedName(build.Id) + "/");
    }

    /// <summary>
    /// The security property, asserted rather than assumed: what is served is the pyramid and only
    /// the pyramid.
    /// </summary>
    /// <remarks>
    /// A build keeps the rasters it was handed and the intermediates it made from them beside its
    /// tiles, and whatever serves terrain is pointed at a directory and serves everything under it
    /// with no request ever reaching this application. So the directory that gets served has to be
    /// one that never held any of that — which is what publishing into a root of its own buys, and
    /// what publishing the build's own folder would have thrown away.
    /// </remarks>
    [Fact]
    public async Task Nothing_a_build_was_given_ends_up_where_terrain_is_served_from()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "tiles");
        File.WriteAllText(Path.Combine(directories.Input, "secret-lidar.tif"), "the operator's own data");
        File.WriteAllText(Path.Combine(directories.Prepared, "prepared.tif"), "and what was made of it");

        await phase.RunAsync(Context(build, directories), CancellationToken.None);

        var served = Directory.EnumerateFiles(workspace.PublishRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f))
            .ToList();

        served.ShouldNotContain("secret-lidar.tif");
        served.ShouldNotContain("prepared.tif");
        served.ShouldBe([TerrainPyramid.ManifestFileName, "0" + TerrainPyramid.TileExtension], ignoreOrder: true);
    }

    [Fact]
    public async Task A_pyramid_already_at_its_address_is_not_moved_again()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "a bake");

        var context = Context(build, directories);
        (await phase.IsAlreadyDoneAsync(context, CancellationToken.None)).ShouldBeFalse();

        await phase.RunAsync(context, CancellationToken.None);

        (await phase.IsAlreadyDoneAsync(context, CancellationToken.None)).ShouldBeTrue();
    }

    /// <summary>
    /// A build made again replaces what it published before, and the address holds one whole
    /// pyramid at every moment either side of it.
    /// </summary>
    [Fact]
    public async Task Publishing_a_second_time_replaces_what_was_there()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "the first bake");
        await phase.RunAsync(Context(build, directories), CancellationToken.None);

        WritePyramid(directories.Tiles, "the second bake");
        await phase.RunAsync(Context(build, directories), CancellationToken.None);

        var published = workspace.PublishedFor(build.Id);
        File.ReadAllText(Path.Combine(published, TerrainPyramid.ManifestFileName))
            .ShouldContain("the second bake");

        // Nothing half-moved is left lying under the served root: a leftover directory there is
        // bytes nobody will ever ask for, on the disk that runs out first.
        Directory.EnumerateDirectories(workspace.PublishRoot)
            .Select(Path.GetFileName)
            .ShouldBe([TerrainPyramid.PublishedName(build.Id)]);
    }

    /// <summary>
    /// A publication that cannot finish leaves the pyramid where the build can still find it, and
    /// says so truthfully.
    /// </summary>
    /// <remarks>
    /// Getting the pyramid beside its address consumes the build's own copy — that is what makes
    /// publishing one rename rather than a second copy of tens of gigabytes — so everything that
    /// can still fail afterwards is failing while the only pyramid there is sits under a name
    /// nothing looks for. The step that would notice is the meshing one, which asks the build's own
    /// folder whether tiles are there; finding none it meshes again, and on the ordinary
    /// installation — which has no tile maker of its own — it cannot, so a validated pyramid is
    /// lost to a rename that failed for a second.
    /// </remarks>
    [Fact]
    public async Task A_publication_that_cannot_be_finished_puts_the_pyramid_back()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "a bake worth hours");

        // A file where the published directory has to go. Renaming a directory onto a file fails on
        // every operating system this runs on, and needs no permissions a test may not have — it
        // stands in for the busy directory or the stalled volume that fails the same rename.
        Directory.CreateDirectory(workspace.PublishRoot);
        await File.WriteAllTextAsync(workspace.PublishedFor(build.Id), "in the way");

        var refusal = await Should.ThrowAsync<TerrainBuildException>(
            () => phase.RunAsync(Context(build, directories), CancellationToken.None));

        refusal.Code.ShouldBe(TerrainBuildFailures.PublishFailed);

        // The pyramid is back in the build's own folder, which is where the message says it is and
        // where the step before this one looks for it.
        File.ReadAllText(Path.Combine(directories.Tiles, TerrainPyramid.ManifestFileName))
            .ShouldContain("a bake worth hours");
        refusal.Message.ShouldContain("Nothing has been lost");

        // And with the obstruction gone the same build publishes, rather than needing a new bake.
        File.Delete(workspace.PublishedFor(build.Id));
        await phase.RunAsync(Context(build, directories), CancellationToken.None);
        File.ReadAllText(Path.Combine(workspace.PublishedFor(build.Id), TerrainPyramid.ManifestFileName))
            .ShouldContain("a bake worth hours");
    }

    /// <summary>
    /// A process killed between the two renames publishing is made of leaves the pyramid beside the
    /// address it was going to; the run that follows puts it back rather than meshing it again.
    /// </summary>
    [Fact]
    public void An_interrupted_publication_is_put_back_where_the_build_looks_for_it()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "the bake that was being published");

        // Exactly what a kill between the two renames leaves: the staged pyramid, and a tiles
        // directory that the next run's own set-up recreates empty.
        Directory.CreateDirectory(workspace.PublishRoot);
        Directory.Move(directories.Tiles, workspace.StagingFor(build.Id));
        workspace.For(build.Id);

        workspace.RecoverInterruptedPublication(build.Id);

        File.ReadAllText(Path.Combine(directories.Tiles, TerrainPyramid.ManifestFileName))
            .ShouldContain("the bake that was being published");
        Directory.Exists(workspace.StagingFor(build.Id)).ShouldBeFalse();
    }

    /// <summary>
    /// A staging directory with no manifest in it is a copy that stopped part way, and is not put
    /// back over anything: it would be a pyramid the checking step passes and a viewer cannot draw.
    /// </summary>
    [Fact]
    public void Half_a_pyramid_beside_the_address_is_not_put_back()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        Directory.CreateDirectory(workspace.StagingFor(build.Id));
        File.WriteAllBytes(
            Path.Combine(workspace.StagingFor(build.Id), "0" + TerrainPyramid.TileExtension), [1, 2, 3]);

        workspace.RecoverInterruptedPublication(build.Id);

        Directory.EnumerateFileSystemEntries(directories.Tiles).ShouldBeEmpty();
    }

    /// <summary>
    /// Deleting a build reclaims what an interrupted publication left beside the address too.
    /// </summary>
    /// <remarks>
    /// Those directories sit under the root whatever serves terrain is pointed at, and no row names
    /// them — so left behind they are a pyramid's worth of bytes that nothing will ever account for
    /// or come back for, on the volume disk is tightest on, and reachable by anyone who can reach
    /// the site.
    /// </remarks>
    [Fact]
    public void Removing_a_build_takes_what_a_failed_publication_left_beside_the_address()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        WritePyramid(directories.Tiles, "a bake");
        WritePyramid(workspace.StagingFor(build.Id), "one that was being moved");
        WritePyramid(workspace.ReplacedFor(build.Id), "one that was being taken away");

        workspace.Remove(build.Id);

        Directory.EnumerateFileSystemEntries(workspace.PublishRoot).ShouldBeEmpty();
        Directory.Exists(workspace.RootFor(build.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_directory_with_no_manifest_in_it_is_refused_rather_than_published()
    {
        var build = Build();
        var directories = workspace.For(build.Id);
        File.WriteAllBytes(Path.Combine(directories.Tiles, "stray" + TerrainPyramid.TileExtension), [1, 2, 3]);

        var refusal = await Should.ThrowAsync<TerrainBuildException>(
            () => phase.RunAsync(Context(build, directories), CancellationToken.None));

        refusal.Code.ShouldBe(TerrainBuildFailures.PyramidUnreadable);
        Directory.Exists(workspace.PublishedFor(build.Id)).ShouldBeFalse();
    }

    private static TerrainBuildContext Context(TerrainBuild build, TerrainBuildDirectories directories) =>
        new(build, directories, (_, _, _) => Task.CompletedTask);

    private static void WritePyramid(string tiles, string mark)
    {
        Directory.CreateDirectory(Path.Combine(tiles, "0", "0"));
        File.WriteAllText(
            Path.Combine(tiles, TerrainPyramid.ManifestFileName),
            $$"""{"format":"quantized-mesh-1.0","version":"1.1.0-abc","description":"{{mark}}"}""");
        File.WriteAllBytes(Path.Combine(tiles, "0", "0", "0" + TerrainPyramid.TileExtension), [7, 7, 7, 7]);
    }

    private static TerrainBuild Build() => new()
    {
        Extent = NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePolygon(
        [
            new Coordinate(22.5, 46.5),
            new Coordinate(22.6, 46.5),
            new Coordinate(22.6, 46.6),
            new Coordinate(22.5, 46.6),
            new Coordinate(22.5, 46.5),
        ]),
        RequestedMaxDepth = 13,
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory a virus scanner still has open. Nothing worth failing a run for.
        }
    }
}
