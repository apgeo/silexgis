// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Loaded beside real data with the explicit switch, the demonstration dataset ties its trips,
/// links and texts only to rows it made itself. A real register code such as 1000/1 sorts before
/// the dataset's DEMO- codes, so a dataset that took "the first caves by code" would hang its
/// trips on the real register. A class of its own, on a database of its own, because the account
/// it adds is real data to every other guard test.
/// </summary>
public sealed class DemoAlongsideRealDataTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "bootstrap-alongside@t.local";

    private readonly SilexGisApiFactory factory;

    public DemoAlongsideRealDataTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = "bootstrap-alongside-pass-1",
        });
    }

    public ValueTask InitializeAsync()
    {
        // The bootstrap administrator first, so it is the oldest administrator and owns the dataset.
        factory.CreateClient().Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Seeded_alongside_a_real_register_the_demo_ties_nothing_to_a_real_cave()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, "registrar@club.example");
        var registrar = await AuthHelper.BearerClientAsync(factory, "registrar@club.example");
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
        }

        var created = await registrar.PostAsJsonAsync("/api/v1/caves", new
        {
            name = "Peștera din registru",
            caveTypeId,
            visibility = "authenticated",
            parentId = (Guid?)null,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var real = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Caves.Where(c => c.Id == real)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdentificationCode, "1000/1"))).ShouldBe(1);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await DemoSeedCommand.RunAsync(scope.ServiceProvider, ["seed-demo"])).Outcome
                .ShouldBe(DemoSeedOutcome.RefusedRealData);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await DemoSeedCommand.RunAsync(scope.ServiceProvider, ["seed-demo", DemoSeedCommand.AlongsideRealDataSwitch]))
                .Outcome.ShouldBe(DemoSeedOutcome.Seeded);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            // The positive half: the demo's trips are tied to its own caves.
            (await db.ResLinkMembers.CountAsync(m => m.FeatureId != null
                && db.Caves.Any(c => c.Id == m.FeatureId && c.IdentificationCode!.StartsWith("DEMO-")))).ShouldBeGreaterThan(0);

            (await db.ResLinkMembers.IgnoreQueryFilters().CountAsync(m => m.FeatureId == real)).ShouldBe(0, "a demo link names the real cave");
            (await db.FeatureLinks.CountAsync(l => l.FromId == real || l.ToId == real)).ShouldBe(0);
            (await db.TripTrackings.IgnoreQueryFilters().CountAsync(t => t.CaveFeatureId == real)).ShouldBe(0);
            (await db.Attachments.IgnoreQueryFilters().CountAsync(a => a.FeatureId == real)).ShouldBe(0);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
