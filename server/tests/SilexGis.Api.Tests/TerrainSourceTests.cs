// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The three ways rasters reach a build: obtained for the rectangle, sent through the browser, and
/// read from a directory on the server the operator has listed.
///
/// <para>
/// The directory is the one that has to be right. It is the single path in this application where a
/// signed-in request names a location the server reads directly, so a mistake hands over whatever
/// the service account can open. Three guards, and they are not alternatives: the caller holds the
/// terrain right, the caller is a full administrator — naming a location on the machine is not a
/// right anything can be scoped to, and it is the same bar the document import is held to — and the
/// path is inside a directory the operator named when they deployed the installation, because an
/// administrator account is not the same thing as the operator who owns the machine. All of them
/// are judged again when the build actually runs, because minutes or hours pass in between, and in
/// that time the disk can change and a right can be taken away.
/// </para>
/// <para>
/// Nothing here reaches the network. The elevation client answers from a stub, so "a cell that is
/// all sea" is a fact this test states rather than one it hopes the bucket will supply.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainSourceTests : IAsyncLifetime, IDisposable
{
    /// <summary>The cell this stub refuses, standing in for one that is entirely ocean.</summary>
    private const string UnpublishedCell = "E023";

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly string importRoot;
    private readonly string outsideRoot;

    private HttpClient starter = null!;        // an ordinary account granted Execute on the terrain domain
    private HttpClient outsider = null!;       // an ordinary account granted nothing at all
    private HttpClient administrator = null!;  // a full administrator: the only one who may name a directory
    private HttpClient anonymous = null!;      // nobody at all
    private Guid starterId;
    private Guid administratorId;

    public TerrainSourceTests(PostgresFixture postgres)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tsrc-builds-{suffix}");
        importRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tsrc-allowed-{suffix}");
        outsideRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tsrc-elsewhere-{suffix}");
        Directory.CreateDirectory(importRoot);
        Directory.CreateDirectory(outsideRoot);

        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Terrain:BuildRoot"] = buildRoot,
                ["Files:ImportRoots:0"] = importRoot,
            },
            services =>
            {
                // This class queues terrain work, so it runs none of the drains itself: every test
                // class shares one PostGIS container and the queue lives in it. What it queues is
                // deleted again when it finishes, for the mirror-image reason.
                JobWorkers.RemoveFrom(services);

                services.AddHttpClient(CopernicusFetcher.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new SeaAndLand());
            });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        starterId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsrc-run-{suffix}@t.local");
        starter = await AuthHelper.BearerClientAsync(factory, $"tsrc-run-{suffix}@t.local");
        await GrantAsync(starterId);

        // Granted nothing at all. The ruleset every account joins opens map layers, tags,
        // taxonomies and the caver directory and says nothing about terrain, so this account
        // genuinely holds no terrain right rather than being assumed to hold none.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsrc-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tsrc-out-{suffix}@t.local");

        // Naming a directory for the server to read is asked of the machine, not of the content, so
        // it takes a full administrator however generous the terrain grant is.
        administratorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tsrc-adm-{suffix}@t.local");
        administrator = await AuthHelper.BearerClientAsync(factory, $"tsrc-adm-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The path guard, both ways round in one test. A refusal on its own proves nothing — the
    /// failure mode of a path guard is not "an attack got through", it is "somebody tightened it
    /// until ordinary directories stopped working" — so the directory the operator did allow has to
    /// be accepted in the same breath.
    /// </summary>
    [Fact]
    public async Task A_directory_outside_the_operators_list_is_refused_and_one_inside_is_taken()
    {
        var elsewhere = Path.Combine(outsideRoot, "lidar");
        Directory.CreateDirectory(elsewhere);
        await WriteRasterAsync(Path.Combine(elsewhere, "secret.tif"));

        var allowed = Path.Combine(importRoot, "lidar");
        Directory.CreateDirectory(allowed);
        await WriteRasterAsync(Path.Combine(allowed, "island.tif"));

        var refused = await SubmitAsync(Request(22.10, 46.10, elsewhere), administrator);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedBody);
        CodeOf(refusedBody).ShouldBe(TerrainBuildEndpoints.DirectoryUnavailableCode);

        // Walking out of an allowed root by naming the way out is the same refusal: containment is
        // judged on where the path actually resolves to, not on how it is spelled.
        var traversal = Path.Combine(importRoot, "..", Path.GetFileName(outsideRoot), "lidar");
        CodeOf(await BodyAsync(await SubmitAsync(Request(22.20, 46.20, traversal), administrator)))
            .ShouldBe(TerrainBuildEndpoints.DirectoryUnavailableCode);

        // A path that is simply not there answers exactly as a path that is not permitted does.
        // Told apart, the pair would be a way of testing whether any given directory on the
        // server's disk exists, one request at a time, without ever reading a byte of it.
        var missing = Path.Combine(importRoot, "no-such-directory");
        var vanished = await SubmitAsync(Request(22.25, 46.25, missing), administrator);
        var vanishedBody = await BodyAsync(vanished);
        vanished.StatusCode.ShouldBe(HttpStatusCode.BadRequest, vanishedBody);
        CodeOf(vanishedBody).ShouldBe(TerrainBuildEndpoints.DirectoryUnavailableCode);

        // The pass that makes the refusals above mean something.
        var accepted = await SubmitAsync(Request(22.30, 46.30, allowed), administrator);
        var body = await BodyAsync(accepted);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, body);

        var buildId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        var sources = await SourcesAsync(buildId);
        sources.ShouldContain(s => s.Kind == TerrainBuildSourceKind.ServerDirectory);

        // The resolved path rather than what was typed: the record should say which directory was
        // actually read, not which one somebody meant.
        sources.Single(s => s.Kind == TerrainBuildSourceKind.ServerDirectory).Reference
            .ShouldBe(Path.GetFullPath(allowed));
    }

    /// <summary>
    /// A link inside an allowed directory is not a way to read what it points at.
    /// </summary>
    /// <remarks>
    /// Links are resolved <i>before</i> containment is judged, and that is the whole of the check
    /// being worth anything — otherwise the allow-list admits its own directory and everything
    /// anybody has ever linked into it. The ordinary directory beside the link is submitted in the
    /// same test, because a guard tightened until every directory is refused would satisfy the
    /// refusal on its own.
    ///
    /// <para>
    /// Creating a link needs a privilege a host may withhold — an unelevated process on Windows
    /// without developer mode cannot make one, which is the state of at least one machine this
    /// suite runs on. Where it cannot, the link half of this test does not run, and the test says
    /// so in its output rather than reporting a green it did not earn. The framework version here
    /// has no way to mark a test skipped from inside it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_link_pointing_out_of_an_allowed_directory_is_refused()
    {
        var target = Path.Combine(outsideRoot, "linked");
        Directory.CreateDirectory(target);
        await WriteRasterAsync(Path.Combine(target, "secret.tif"));

        // The positive half, which runs everywhere: an ordinary directory inside the allowed root
        // is taken. Without it a refusal proves only that something was refused.
        var ordinary = Path.Combine(importRoot, "not-a-link");
        Directory.CreateDirectory(ordinary);
        await WriteRasterAsync(Path.Combine(ordinary, "island.tif"));
        var taken = await SubmitAsync(Request(22.35, 46.35, ordinary), administrator);
        taken.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(taken));

        var link = Path.Combine(importRoot, "shortcut");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Directory.Exists(link).ShouldBeFalse();
            Console.WriteLine(
                "Link resolution was NOT exercised: this host refuses to create a symbolic link. "
                + $"({e.GetType().Name})");
            return;
        }

        var refused = await SubmitAsync(Request(22.40, 46.40, link), administrator);
        var body = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(TerrainBuildEndpoints.DirectoryUnavailableCode);
    }

    /// <summary>
    /// Holding the terrain right is not the same as being allowed to name a location on the
    /// machine, and the two are asked separately.
    /// </summary>
    /// <remarks>
    /// The document import restricts reading the server's own disk to full administrators, saying
    /// why in its own words: there is no object to scope a right to, and the question is about the
    /// machine rather than about any content on it. The terrain routes read the same operator list,
    /// so they answer to the same bar — an account that may start builds all day is refused the one
    /// source that names a path, while its uploads and its obtained coverage still work.
    /// </remarks>
    [Fact]
    public async Task Naming_a_directory_takes_more_than_the_right_to_start_builds()
    {
        var allowed = Path.Combine(importRoot, "delegated");
        Directory.CreateDirectory(allowed);
        await WriteRasterAsync(Path.Combine(allowed, "tile.tif"));

        // Holds Execute over the whole terrain domain, and is refused — with the same answer a
        // path outside the roots would get, so nothing about the path is disclosed either.
        var refused = await SubmitAsync(Request(22.42, 46.42, allowed), starter);
        var refusedBody = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        CodeOf(refusedBody).ShouldBe(TerrainBuildEndpoints.ForbiddenCode);

        // The right it does hold still does what it says: a build with no directory in it goes
        // through, so what was refused above was the directory and not the account.
        var ordinary = await SubmitAsync(Request(22.44, 46.44), starter);
        ordinary.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(ordinary));

        // And the administrator, on the same directory, is taken.
        var accepted = await SubmitAsync(Request(22.46, 46.46, allowed), administrator);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(accepted));
    }

    /// <summary>
    /// The directory is judged again when the build runs, not only when the request was accepted.
    /// </summary>
    /// <remarks>
    /// Hours can pass in between, and the check that counts is the one nearest the read: a
    /// directory that has been moved, replaced by a link to somewhere else, or taken off the
    /// operator's list would otherwise be read anyway, on a delay.
    /// </remarks>
    [Fact]
    public async Task A_directory_that_goes_away_after_the_request_stops_the_build_when_it_runs()
    {
        var allowed = Path.Combine(importRoot, "vanishing");
        Directory.CreateDirectory(allowed);
        await WriteRasterAsync(Path.Combine(allowed, "tile.tif"));

        var buildId = await AcceptedAsync(
            Request(22.50, 46.50, allowed, fetchCoverage: false), administrator);
        Directory.Delete(allowed, recursive: true);

        await RunAsync(buildId, administratorId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.SourceUnreadable);
        build.Phase.ShouldBe(TerrainBuildPhase.Fetch);
    }

    /// <summary>
    /// The rights of whoever asked are rebuilt when the build runs, and the directory is not read
    /// for somebody who may no longer name one.
    /// </summary>
    /// <remarks>
    /// The same build row is run three times, by three different requesters, and only the last one
    /// gets the rasters. Without the pass at the end this would be satisfied by a step that never
    /// reads a directory at all; without the two refusals, deleting the check inside the handler
    /// would leave every test in this suite green while a right taken away hours ago still had work
    /// done under it.
    /// </remarks>
    [Fact]
    public async Task The_requesters_rights_are_judged_again_when_the_build_runs()
    {
        var allowed = Path.Combine(importRoot, "delegated-run");
        Directory.CreateDirectory(allowed);
        await WriteRasterAsync(Path.Combine(allowed, "tile.tif"));

        var buildId = await AcceptedAsync(
            Request(22.55, 46.55, allowed, fetchCoverage: false), administrator);

        // Handed over on behalf of an account holding the terrain right and nothing more. What was
        // true when the request was accepted does not carry: the answer is rebuilt here.
        await RunAsync(buildId, starterId);
        var withoutTheBar = await ReadAsync(buildId);
        withoutTheBar.Status.ShouldBe(TerrainBuildStatus.Failed);
        withoutTheBar.ErrorCode.ShouldBe(TerrainBuildFailures.SourceUnreadable);
        withoutTheBar.Phase.ShouldBe(TerrainBuildPhase.Fetch);
        Directory.GetFiles(Path.Combine(buildRoot, buildId.ToString("N"), "input")).ShouldBeEmpty();

        // A queue row naming nobody — work the installation started for itself — has no rights to
        // rebuild, so it cannot be the way a directory gets read either.
        await RunAsync(buildId, withoutRequester: true);
        (await ReadAsync(buildId)).ErrorCode.ShouldBe(TerrainBuildFailures.SourceUnreadable);

        // The pass that makes both refusals mean something: the account that still holds both
        // answers gets exactly the raster the directory holds.
        await RunAsync(buildId, administratorId);
        var built = await ReadAsync(buildId);
        built.Status.ShouldBe(TerrainBuildStatus.Succeeded, built.ErrorCode ?? built.Message ?? "");
        Directory.GetFiles(Path.Combine(buildRoot, buildId.ToString("N"), "input"))
            .Select(Path.GetFileName).ShouldBe(["tile.tif"]);
    }

    /// <summary>
    /// Two rasters that share a name and a size are both kept, and a raster already taken from the
    /// same place is not taken twice.
    /// </summary>
    /// <remarks>
    /// Several of the formats accepted here are fixed-size grids — every tile of a given resolution
    /// is exactly the same number of bytes — so "same name, same length" is not "the same file". A
    /// build that dropped the second would report success over ground it never read, and ground
    /// that was never read bakes as smooth terrain with nothing anywhere saying so.
    /// </remarks>
    [Fact]
    public async Task Two_rasters_alike_in_name_and_size_are_both_taken_and_a_resumed_build_takes_neither_twice()
    {
        var first = Path.Combine(importRoot, "block1");
        var second = Path.Combine(importRoot, "block2");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        // Same name, same length, different content — which is what two neighbouring tiles of a
        // fixed-size grid format look like on disk.
        await File.WriteAllBytesAsync(Path.Combine(first, "tile.hgt"), [1, 2, 3, 4, 5, 6, 7, 8]);
        await File.WriteAllBytesAsync(Path.Combine(second, "tile.hgt"), [8, 7, 6, 5, 4, 3, 2, 1]);

        var buildId = await AcceptedAsync(
            new TerrainBuildSubmitRequest(
                22.65, 46.65, 22.70, 46.70, 13, null, null, false,
                [
                    new(TerrainBuildSourceKind.ServerDirectory, first, "The operator's own lidar", null),
                    new(TerrainBuildSourceKind.ServerDirectory, second, "The operator's own lidar", null),
                ]),
            administrator);

        await RunAsync(buildId, administratorId);

        var input = Path.Combine(buildRoot, buildId.ToString("N"), "input");
        var taken = Directory.GetFiles(input).Select(Path.GetFileName).Order().ToList();
        taken.ShouldBe(["tile-1.hgt", "tile.hgt"]);

        // Both sets of bytes are there: the second was numbered rather than dropped.
        var contents = Directory.GetFiles(input).Select(File.ReadAllBytes).ToList();
        contents.ShouldContain(b => b[0] == 1);
        contents.ShouldContain(b => b[0] == 8);

        // Handed the same build again the way a restart hands one back: nothing is copied a second
        // time, so resuming stays cheap and the input does not fill up with numbered duplicates.
        await InterruptAsync(buildId);
        await RunAsync(buildId, administratorId);
        Directory.GetFiles(input).Select(Path.GetFileName).Order().ShouldBe(["tile-1.hgt", "tile.hgt"]);
    }

    /// <summary>
    /// A cell the dataset does not publish is skipped, and the rest of the rectangle is built.
    /// </summary>
    /// <remarks>
    /// Cells that are entirely ocean are never published, so any rectangle drawn near a coast asks
    /// for some that are not there. It is the single easiest rule in the whole fetch to forget,
    /// because it only ever shows up at the sea — and forgetting it refuses every coastal area on
    /// Earth with what looks like a broken download.
    /// </remarks>
    [Fact]
    public async Task A_cell_the_dataset_does_not_publish_is_skipped_rather_than_failing_the_build()
    {
        // Two cells wide, and the stub answers "not there" for the eastern one.
        var buildId = await AcceptedAsync(new TerrainBuildSubmitRequest(
            22.50, 46.50, 23.50, 46.90, 13, null, null));

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");
        build.Phase.ShouldBe(TerrainBuildPhase.Fetch);

        // The cell that exists is on disk under its own name; the one that does not is simply
        // absent, and no fragment was left behind by either.
        var input = Path.Combine(buildRoot, buildId.ToString("N"), "input");
        var obtained = Directory.GetFiles(input).Select(Path.GetFileName).ToList();
        obtained.ShouldBe(["N46_00_E022_00.tif"]);
    }

    /// <summary>
    /// A rectangle whose every cell is unpublished is a well-formed request that cannot be built.
    /// </summary>
    [Fact]
    public async Task A_rectangle_with_nothing_published_under_it_stops_and_says_so()
    {
        // Entirely inside the cell the stub refuses.
        var buildId = await AcceptedAsync(new TerrainBuildSubmitRequest(
            23.10, 46.10, 23.40, 46.40, 13, null, null));

        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Failed);
        build.ErrorCode.ShouldBe(TerrainBuildFailures.NoRasters);
    }

    /// <summary>
    /// Sending a raster through the browser, and building from it — with the same permission pair
    /// every other terrain route is held to.
    /// </summary>
    [Fact]
    public async Task A_raster_sent_through_the_browser_takes_the_terrain_right_and_becomes_a_source()
    {
        (await UploadAsync(anonymous, "island.tif")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await UploadAsync(outsider, "island.tif")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The pass that makes the refusal above mean something.
        var sent = await UploadAsync(starter, "island.tif");
        var sentBody = await BodyAsync(sent);
        sent.StatusCode.ShouldBe(HttpStatusCode.Created, sentBody);
        var reference = JsonDocument.Parse(sentBody).RootElement.GetProperty("reference").GetString()!;

        // Nothing a caller sends is part of a path here: the name comes back from the server.
        reference.ShouldNotContain("island");
        reference.ShouldEndWith(".tif");

        // A file this installation is not holding is refused rather than becoming a build that
        // fails later with nobody watching.
        CodeOf(await BodyAsync(await SubmitAsync(Request(22.60, 46.60, uploaded: "0123456789abcdef0123456789abcdef.tif"))))
            .ShouldBe(TerrainBuildEndpoints.SourceInvalidCode);

        var buildId = await AcceptedAsync(
            Request(22.70, 46.70, uploaded: reference, fetchCoverage: false));
        await RunAsync(buildId);

        var build = await ReadAsync(buildId);
        build.Status.ShouldBe(TerrainBuildStatus.Succeeded, build.ErrorCode ?? build.Message ?? "");

        var input = Path.Combine(buildRoot, buildId.ToString("N"), "input");
        Directory.GetFiles(input).Length.ShouldBe(1);

        var sources = await SourcesAsync(buildId);
        sources.Single().Kind.ShouldBe(TerrainBuildSourceKind.Uploaded);
        sources.Single().Attribution.ShouldBe("Somebody's lidar");
    }

    /// <summary>
    /// A build has to be made from something, and what it is made from carries its own credit.
    /// </summary>
    [Fact]
    public async Task A_build_made_from_nothing_is_refused_and_the_obtained_coverage_credits_itself()
    {
        var nothing = new TerrainBuildSubmitRequest(
            22.80, 46.80, 22.85, 46.85, 13, null, null, FetchCoverage: false);
        CodeOf(await BodyAsync(await SubmitAsync(nothing)))
            .ShouldBe(TerrainBuildEndpoints.NoSourcesCode);

        var buildId = await AcceptedAsync(new TerrainBuildSubmitRequest(
            22.90, 46.90, 22.95, 46.95, 13, null, null));

        // The credit belongs to the dataset rather than to whoever pressed the button, so it is
        // never taken from the request: a pyramid must not be able to carry a credit somebody made
        // up, and a false licence statement on screen is worse than none.
        var source = (await SourcesAsync(buildId)).Single();
        source.Kind.ShouldBe(TerrainBuildSourceKind.Fetched);
        source.Attribution.ShouldBe(CopernicusCoverage.Attribution);
        source.Licence.ShouldBe(CopernicusCoverage.Licence);
    }

    /// <summary>
    /// The directories an installation reads from are published, so a page can offer them rather
    /// than asking somebody to type a path and guess — and only to somebody who may start a build.
    /// </summary>
    [Fact]
    public async Task The_directories_this_installation_reads_from_are_offered_to_whoever_may_name_one()
    {
        (await anonymous.GetAsync("/api/v1/terrain/source-directories")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        (await outsider.GetAsync("/api/v1/terrain/source-directories")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // The list is the operator's own directory layout, so it is answered to exactly whoever
        // may name one of them in a build — and holding the terrain right is not that.
        (await starter.GetAsync("/api/v1/terrain/source-directories")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var offered = await administrator.GetAsync("/api/v1/terrain/source-directories");
        var body = await BodyAsync(offered);
        offered.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain(JsonEncodedRoot());
    }

    private string JsonEncodedRoot() =>
        JsonSerializer.Serialize(Path.GetFullPath(importRoot)).Trim('"');

    private static TerrainBuildSubmitRequest Request(
        double west,
        double south,
        string? directory = null,
        string? uploaded = null,
        bool fetchCoverage = true)
    {
        var sources = new List<TerrainBuildSourceRequest>();
        if (directory is not null)
        {
            sources.Add(new TerrainBuildSourceRequest(
                TerrainBuildSourceKind.ServerDirectory, directory, "The operator's own lidar", null));
        }

        if (uploaded is not null)
        {
            sources.Add(new TerrainBuildSourceRequest(
                TerrainBuildSourceKind.Uploaded, uploaded, "Somebody's lidar", "Used with permission"));
        }

        return new TerrainBuildSubmitRequest(
            west, south, west + 0.05, south + 0.05, 13, null, null, fetchCoverage, sources);
    }

    private Task<HttpResponseMessage> SubmitAsync(
        TerrainBuildSubmitRequest request, HttpClient? client = null) =>
        (client ?? starter).PostAsJsonAsync("/api/v1/terrain/builds", request);

    private async Task<Guid> AcceptedAsync(TerrainBuildSubmitRequest request, HttpClient? client = null)
    {
        var response = await SubmitAsync(request, client);
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string name)
    {
        var content = new ByteArrayContent(RasterBytes());
        content.Headers.ContentType = new("image/tiff");

        // Awaited inside the using: the form must outlive the request body being read.
        using var form = new MultipartFormDataContent { { content, "file", name } };
        return await client.PostAsync("/api/v1/terrain/rasters", form);
    }

    /// <summary>
    /// Runs the handler directly rather than waiting for a worker, so the assertions are about what
    /// the handler did and not about how quickly something got to it.
    /// </summary>
    private async Task RunAsync(Guid buildId, Guid? requestedBy = null, bool withoutRequester = false)
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
                    RequestedBy = withoutRequester ? null : requestedBy ?? starterId,
                    Payload = JsonSerializer.Serialize(
                        new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A build that stops throws so the queue row goes red; what is asserted here is the
            // record it left on the build's own row, which is where somebody diagnosing it looks.
        }
    }

    /// <summary>Puts a build back the way a process that died halfway through would leave it.</summary>
    private async Task InterruptAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, TerrainBuildStatus.Running)
            .SetProperty(b => b.FinishedAt, (DateTimeOffset?)null));
    }

    private async Task<TerrainBuild> ReadAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().FirstAsync(b => b.Id == buildId);
    }

    private async Task<List<TerrainBuildSource>> SourcesAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuildSources.AsNoTracking()
            .Where(s => s.TerrainBuildId == buildId).OrderBy(s => s.Id).ToListAsync();
    }

    private async Task GrantAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = AccessAction.Execute | AccessAction.Read,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private static Task WriteRasterAsync(string path) => File.WriteAllBytesAsync(path, RasterBytes());

    /// <summary>
    /// Bytes standing in for a raster. Nothing here opens one as an image — what is under test is
    /// which files end up where, and a real GeoTIFF would only make the fixtures slower.
    /// </summary>
    private static byte[] RasterBytes() => [0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00];

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    /// <summary>
    /// Everything this class queued, taken out of the shared queue again. Left there, another test
    /// class's terrain worker would claim these and run them against a host with none of this
    /// class's directories.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.TerrainBuild).ExecuteDeleteAsync();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        starter?.Dispose();
        outsider?.Dispose();
        administrator?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();

        foreach (var tree in new[] { buildRoot, importRoot, outsideRoot })
        {
            if (Directory.Exists(tree))
            {
                Directory.Delete(tree, recursive: true);
            }
        }
    }

    /// <summary>
    /// Stands in for the open dataset: every cell answers with bytes except the one named as sea,
    /// which is not published at all.
    /// </summary>
    private sealed class SeaAndLand : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(UnpublishedCell, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent([]),
                });
            }

            var body = RasterBytes();
            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = body.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
