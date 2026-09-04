// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
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
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Choosing which build the 3D scene draws, and removing one.
///
/// <para>
/// Every refusal here is paired, in the same test, with an account that holds exactly the right
/// being tested and nothing else. A full administrator is waved through several separate places
/// before any terrain rule is read, so an endpoint whose permission check had been deleted outright
/// would still pass every assertion driven by one — and the two rights this class covers are
/// different rights, so an account holding the wrong one is the honest proof that the right one is
/// what is being asked for.
/// </para>
///
/// <para>
/// The other thing under test is that only one build can be the terrain at a time. That rule is
/// held by the database rather than by the handler, precisely because two people pressing the button
/// in the same instant is the case a read-then-write cannot survive — so it is exercised the way it
/// fails, with both requests in flight at once.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainActivationTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string publishRoot = Path.Combine(
        Path.GetTempPath(), "silexgis-active-" + Guid.NewGuid().ToString("N")[..8]);

    private HttpClient executor = null!;  // granted Execute on the terrain domain, and nothing else
    private HttpClient remover = null!;   // granted Delete on the terrain domain, and nothing else
    private HttpClient outsider = null!;  // granted nothing at all
    private HttpClient anonymous = null!;

    public TerrainActivationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // Its own directory, because the check that a build has something to draw asks the
                // disk rather than the row: a build whose status says it succeeded and whose tiles
                // somebody has since removed would otherwise become the terrain and draw nothing.
                ["Terrain:PublishRoot"] = publishRoot,
                ["Terrain:BuildRoot"] = Path.Combine(publishRoot, "builds"),
            },
            JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var executorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ta-exec-{suffix}@t.local");
        executor = await AuthHelper.BearerClientAsync(factory, $"ta-exec-{suffix}@t.local");
        await GrantAsync(executorId, AccessAction.Execute);

        var removerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ta-del-{suffix}@t.local");
        remover = await AuthHelper.BearerClientAsync(factory, $"ta-del-{suffix}@t.local");
        await GrantAsync(removerId, AccessAction.Delete);

        // The ruleset every account joins opens map layers, tags, taxonomies and the caver
        // directory and says nothing about terrain, so this account genuinely holds no terrain
        // right rather than being assumed to hold none.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ta-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"ta-out-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Choosing_the_terrain_takes_the_right_to_run_terrain_work_and_nothing_less()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();

        (await anonymous.PostAsync(Active(build), null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        (await outsider.PostAsync(Active(build), null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Holds a terrain right, and the wrong one. Removing a build and choosing what everyone
        // sees are separate decisions, so holding one of them must not carry the other.
        (await remover.PostAsync(Active(build), null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await executor.PostAsync(Active(build), null);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await ActiveIdsAsync()).ShouldBe([build]);
    }

    /// <summary>
    /// A build nothing has checked, and a build whose checked tiles are gone, are both refused —
    /// beside one that is genuinely there and is accepted.
    /// </summary>
    /// <remarks>
    /// The status a build carries is deliberately not the signal. Terrain that is not where it is
    /// served from draws as smooth bare ground with nothing anywhere reporting a problem, so what
    /// is asked is whether there is a pyramid at the address, not whether a row says there was one.
    /// </remarks>
    [Fact]
    public async Task A_build_with_nothing_to_draw_is_refused_whatever_its_status_says()
    {
        await ClearBuildsAsync();

        var unchecked_ = await SeedBuildAsync(TerrainBuildStatus.Succeeded, version: null, publish: false);
        var refusedUnchecked = await executor.PostAsync(Active(unchecked_), null);
        refusedUnchecked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(await refusedUnchecked.Content.ReadAsStringAsync())
            .ShouldBe(TerrainBuildEndpoints.NotPublishedCode);

        var vanished = await SeedBuildAsync(TerrainBuildStatus.Succeeded, version: "1.1.0-abc", publish: false);
        var refusedVanished = await executor.PostAsync(Active(vanished), null);
        refusedVanished.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(await refusedVanished.Content.ReadAsStringAsync())
            .ShouldBe(TerrainBuildEndpoints.NotPublishedCode);

        var whole = await SeedPublishedBuildAsync();
        (await executor.PostAsync(Active(whole), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ActiveIdsAsync()).ShouldBe([whole]);
    }

    [Fact]
    public async Task Choosing_terrain_moves_the_mark_rather_than_adding_one()
    {
        await ClearBuildsAsync();
        var first = await SeedPublishedBuildAsync();
        var second = await SeedPublishedBuildAsync();

        (await executor.PostAsync(Active(first), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await executor.PostAsync(Active(second), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ActiveIdsAsync()).ShouldBe([second]);

        // Choosing the one already chosen is not an error, and does not end with two.
        (await executor.PostAsync(Active(second), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ActiveIdsAsync()).ShouldBe([second]);
    }

    /// <summary>
    /// Two people choosing terrain in the same instant, which is the pair the rule exists for.
    /// </summary>
    /// <remarks>
    /// Left to the handler alone this is the shape that gets through: both requests read a state in
    /// which nothing is being drawn, and both then write their own row. What must not happen is two
    /// rows carrying the mark — the scene would draw whichever one a later query happened to return
    /// first — and what must also not happen is a pair of requests that both fail on the database's
    /// refusal, which is a correct rule producing an answer nobody can act on.
    /// </remarks>
    [Fact]
    public async Task Two_people_choosing_terrain_at_once_leave_exactly_one()
    {
        await ClearBuildsAsync();
        var first = await SeedPublishedBuildAsync();
        var second = await SeedPublishedBuildAsync();

        var answers = await Task.WhenAll(
            executor.PostAsync(Active(first), null),
            executor.PostAsync(Active(second), null));

        foreach (var answer in answers)
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        }

        var active = await ActiveIdsAsync();
        active.Count.ShouldBe(1);
        active[0].ShouldBeOneOf(first, second);
    }

    /// <summary>
    /// Choosing a build and removing it arriving together: whichever wins, what is left is
    /// consistent, and the pair can never both succeed.
    /// </summary>
    /// <remarks>
    /// The refusal that protects the terrain on screen is a read followed by a write, so without
    /// the two being held together one administrator's delete can read a build as not being drawn
    /// in the moment before another's choice makes it the one that is — and then remove the row and
    /// the pyramid under it. Nothing reports that: the scene silently drops to a bare globe, which
    /// is exactly what the refusal exists to prevent and exactly what a bare globe never explains.
    /// </remarks>
    [Fact]
    public async Task Choosing_terrain_and_removing_it_at_once_can_never_both_succeed()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();

        var answers = await Task.WhenAll(
            executor.PostAsync(Active(build), null),
            remover.DeleteAsync(Build(build)));

        var chosen = answers[0].StatusCode;
        var removed = answers[1].StatusCode;
        var what = $"chose {chosen}, removed {removed}";

        (chosen == HttpStatusCode.OK && removed == HttpStatusCode.NoContent).ShouldBeFalse(what);

        if (removed == HttpStatusCode.NoContent)
        {
            // The delete got there first, so choosing it found nothing to choose, and nothing is
            // left claiming to be the terrain being drawn.
            chosen.ShouldBe(HttpStatusCode.NotFound, what);
            (await ExistsAsync(build)).ShouldBeFalse();
            (await ActiveIdsAsync()).ShouldBeEmpty();
        }
        else
        {
            // The choice got there first, so the removal was refused for the stated reason and the
            // pyramid the scene draws is still on disk.
            chosen.ShouldBe(HttpStatusCode.OK, what);
            removed.ShouldBe(HttpStatusCode.Conflict, what);
            CodeOf(await answers[1].Content.ReadAsStringAsync()).ShouldBe(TerrainBuildEndpoints.ActiveCode);
            (await ActiveIdsAsync()).ShouldBe([build]);
            File.Exists(Path.Combine(
                publishRoot, TerrainPyramid.PublishedName(build), TerrainPyramid.ManifestFileName))
                .ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Terrain_can_be_put_away_again_and_that_takes_the_same_right()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();
        (await executor.PostAsync(Active(build), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.DeleteAsync(Active(build))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.DeleteAsync(Active(build))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await executor.DeleteAsync(Active(build))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ActiveIdsAsync()).ShouldBeEmpty();

        // Putting away what is already put away is what was asked for, not a conflict.
        (await executor.DeleteAsync(Active(build))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Removing_a_build_takes_the_right_to_remove_one_and_nothing_less()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();

        (await anonymous.DeleteAsync(Build(build))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.DeleteAsync(Build(build))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Holds the right that starts builds and chooses terrain, and not the one that removes.
        (await executor.DeleteAsync(Build(build))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var removed = await remover.DeleteAsync(Build(build));
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());

        (await ExistsAsync(build)).ShouldBeFalse();

        // The bytes go with the row. A build kept until somebody deletes it means the deletion has
        // to actually reclaim the disk it was kept on, or "delete" is a word about a table.
        Directory.Exists(Path.Combine(publishRoot, TerrainPyramid.PublishedName(build))).ShouldBeFalse();
    }

    [Fact]
    public async Task The_terrain_being_drawn_is_not_removed_until_something_else_is_drawn()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();
        (await executor.PostAsync(Active(build), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await remover.DeleteAsync(Build(build));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe(TerrainBuildEndpoints.ActiveCode);
        (await ExistsAsync(build)).ShouldBeTrue();

        (await executor.DeleteAsync(Active(build))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await remover.DeleteAsync(Build(build))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ExistsAsync(build)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_build_that_has_not_finished_is_not_removed_from_under_the_worker()
    {
        await ClearBuildsAsync();
        var running = await SeedBuildAsync(TerrainBuildStatus.Running, version: null, publish: false);

        var refused = await remover.DeleteAsync(Build(running));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe(TerrainBuildEndpoints.RunningCode);

        // The same build once it has stopped, so the refusal is proved to be about it still going
        // and not about anything else this fixture happens to have set.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(x => x.Id == running)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TerrainBuildStatus.Failed));
        }

        (await remover.DeleteAsync(Build(running))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_build_that_is_not_there_is_said_not_to_be_there()
    {
        var missing = Guid.CreateVersion7();

        var activate = await executor.PostAsync(Active(missing), null);
        activate.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        CodeOf(await activate.Content.ReadAsStringAsync()).ShouldBe(TerrainBuildEndpoints.NotFoundCode);

        (await remover.DeleteAsync(Build(missing))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A build that finishes puts itself on the screen, so that the step between building an area
    /// and seeing it is not a button somebody has to know about.
    /// </summary>
    [Fact]
    public async Task A_finished_build_draws_itself_when_nothing_is_drawn()
    {
        await ClearBuildsAsync();
        var built = await SeedPublishedBuildAsync();

        (await DrawAutomaticallyAsync(built)).ShouldBeTrue();

        (await ActiveIdsAsync()).ShouldBe([built]);
    }

    /// <summary>
    /// The pair the guard exists for: an automatic choice may be moved on by the next finished
    /// build, a deliberate one may not.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted together because either alone passes against a rule that is wrong
    /// in the other direction. "Draw it whenever nothing is drawn" satisfies the first test on an
    /// empty installation and then never fires again; "always draw the newest" satisfies it for
    /// ever and quietly overrules the operator who picked a coarser build on purpose. The
    /// deliberate choice is made through the endpoint rather than written into storage, because
    /// what marks it deliberate is something that endpoint does.
    /// </remarks>
    [Fact]
    public async Task A_finished_build_takes_over_from_an_automatic_choice_but_never_from_a_chosen_one()
    {
        await ClearBuildsAsync();
        var drewItself = await SeedPublishedBuildAsync();
        var next = await SeedPublishedBuildAsync();

        (await DrawAutomaticallyAsync(drewItself)).ShouldBeTrue();
        (await DrawAutomaticallyAsync(next)).ShouldBeTrue();
        (await ActiveIdsAsync()).ShouldBe([next]);

        var chosen = await SeedPublishedBuildAsync();
        (await executor.PostAsync(Active(chosen), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var later = await SeedPublishedBuildAsync();
        (await DrawAutomaticallyAsync(later)).ShouldBeFalse();
        (await ActiveIdsAsync()).ShouldBe([chosen]);
    }

    /// <summary>
    /// A run can succeed without leaving a pyramid — the chain stops at the last step this
    /// installation implements — and terrain that is not there draws as smooth bare ground with
    /// nothing anywhere saying so.
    /// </summary>
    /// <remarks>
    /// The second assertion is the one that matters: refusing to draw the new build must not also
    /// take away the build that was being drawn, which is what a rule written as "let the mark go,
    /// then decide" would do.
    /// </remarks>
    [Fact]
    public async Task A_finished_build_with_nothing_to_draw_leaves_the_scene_where_it_was()
    {
        await ClearBuildsAsync();
        var drawing = await SeedPublishedBuildAsync();
        (await DrawAutomaticallyAsync(drawing)).ShouldBeTrue();

        var succeededWithNoPyramid = await SeedBuildAsync(
            TerrainBuildStatus.Succeeded, version: null, publish: false);

        (await DrawAutomaticallyAsync(succeededWithNoPyramid)).ShouldBeFalse();
        (await ActiveIdsAsync()).ShouldBe([drawing]);
    }

    /// <summary>
    /// What the worker calls when a build finishes, driven the way the worker drives it: the
    /// question of whether there is a pyramid is put to the disk, not to the row.
    /// </summary>
    private async Task<bool> DrawAutomaticallyAsync(Guid buildId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var workspace = scope.ServiceProvider.GetRequiredService<TerrainWorkspace>();
        return await TerrainBuildWrites.DrawIfNothingWasChosenAsync(
            db, buildId, workspace.HasPublishedPyramid(buildId), CancellationToken.None);
    }

    private static string Build(Guid id) => $"/api/v1/terrain/builds/{id}";

    private static string Active(Guid id) => $"/api/v1/terrain/builds/{id}/active";

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    private async Task<List<Guid>> ActiveIdsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking()
            .Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TerrainBuilds.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    private async Task ClearBuildsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    private Task<Guid> SeedPublishedBuildAsync() =>
        SeedBuildAsync(TerrainBuildStatus.Succeeded, "1.1.0-abcdef123456", publish: true);

    private async Task<Guid> SeedBuildAsync(TerrainBuildStatus status, string? version, bool publish)
    {
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var build = new TerrainBuild
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
                Status = status,
                Phase = status == TerrainBuildStatus.Succeeded
                    ? TerrainBuildPhase.Publish
                    : TerrainBuildPhase.Bake,
                PyramidVersion = version,
                SizeBytes = 4096,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            id = build.Id;
        }

        if (publish)
        {
            var directory = Path.Combine(publishRoot, TerrainPyramid.PublishedName(id));
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, TerrainPyramid.ManifestFileName),
                """{"format":"quantized-mesh-1.0","version":"1.1.0-abcdef123456"}""");
        }

        return id;
    }

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than their author holds, which is exactly what a fixture
    /// granting a right nobody in it has needs to do.
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        executor?.Dispose();
        remover?.Dispose();
        outsider?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();

        try
        {
            if (Directory.Exists(publishRoot))
            {
                Directory.Delete(publishRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory something else still has open. Not worth failing a run for.
        }
    }
}
