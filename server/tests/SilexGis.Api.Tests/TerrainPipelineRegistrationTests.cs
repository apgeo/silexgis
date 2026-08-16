// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// That this installation actually has the steps a terrain build walks through.
///
/// <para>
/// Every other test of the chain replaces the registered steps with stand-ins, which is right —
/// what each of them is about is one step's behaviour, not the container. But it leaves the wiring
/// itself untested, and the wiring fails silently: the walk runs the steps that are there and stops
/// at the first one nothing implements, and stopping is an ordinary end that finishes the build as
/// succeeded. A registration deleted in a refactor therefore produces builds that report success in
/// seconds, having done none of the work, with nothing failing anywhere to say so.
/// </para>
///
/// <para>
/// So this one resolves them from the container the application really builds, and asserts nothing
/// about what they do.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerrainPipelineRegistrationTests : IAsyncLifetime
{
    private readonly SilexGisApiFactory factory;

    public TerrainPipelineRegistrationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>(),
            // The queue is shared with every other class running against the same container, and
            // this one asks nothing of it. Nothing else is touched: the registrations are the point.
            JobWorkers.RemoveFrom);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public void The_steps_a_build_walks_through_are_all_registered()
    {
        using var scope = factory.Services.CreateScope();

        var phases = scope.ServiceProvider.GetServices<ITerrainPhase>().ToList();

        phases.Select(p => p.Phase).ShouldContain(TerrainBuildPhase.Fetch);
        phases.Select(p => p.Phase).ShouldContain(TerrainBuildPhase.Prepare);

        // One implementation per step, because the walk takes the first that claims a step and a
        // second one would simply never run — silently, and differently depending on the order the
        // container happened to be built in.
        phases.GroupBy(p => p.Phase).ShouldAllBe(g => g.Count() == 1);

        scope.ServiceProvider.GetService<ITerrainRasterPreparer>().ShouldNotBeNull();
    }
}
