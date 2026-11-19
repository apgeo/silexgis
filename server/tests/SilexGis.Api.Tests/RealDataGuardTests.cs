// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The demonstration dataset refuses a database holding accounts or content it did not create,
/// unless the operator names the switch that says the installation's real data is known about.
/// Each refusal is shown beside the case where the dataset goes ahead, so a guard that refuses
/// everything would fail here as surely as one that refuses nothing. The test logins' half of the
/// same guard is in <see cref="TestLoginRealDataGuardTests"/>, on a database of its own, because
/// the accounts each test adds would otherwise be the other's "real data".
/// </summary>
public sealed class RealDataGuardTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "bootstrap-guard@t.local";
    private const string AdminPassword = "bootstrap-guard-pass-1";

    private readonly SilexGisApiFactory factory;

    public RealDataGuardTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword,
        });
    }

    [Fact]
    public async Task The_demo_dataset_refuses_a_database_with_content_or_accounts_it_did_not_create()
    {
        // Only the bootstrap administrator: the dataset goes in.
        (await RunDemoAsync()).Outcome.ShouldBe(DemoSeedOutcome.Seeded);
        // And again on top of itself — its own rows are no reason to refuse.
        (await RunDemoAsync()).Outcome.ShouldBe(DemoSeedOutcome.Seeded);

        // A cave whose code is not the dataset's: somebody's register.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Caves.Where(c => c.IdentificationCode == "DEMO-0003")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdentificationCode, "1000/1"));
        }

        var refusedForCave = await RunDemoAsync();
        refusedForCave.Outcome.ShouldBe(DemoSeedOutcome.RefusedRealData);
        refusedForCave.Foreign!.Caves.ShouldBe(1);
        refusedForCave.Foreign.Accounts.ShouldBe(0);

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "member@club.example");
        var refusedForAccount = await RunDemoAsync();
        refusedForAccount.Outcome.ShouldBe(DemoSeedOutcome.RefusedRealData);
        refusedForAccount.Foreign!.Accounts.ShouldBe(1);

        (await RunDemoAsync(DemoSeedCommand.AlongsideRealDataSwitch)).Outcome.ShouldBe(DemoSeedOutcome.Seeded);
    }

    private async Task<DemoSeedCommand.Result> RunDemoAsync(params string[] args)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await DemoSeedCommand.RunAsync(scope.ServiceProvider, ["seed-demo", .. args]);
    }

    public void Dispose() => factory.Dispose();
}
