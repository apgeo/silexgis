// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// On a small club's installation the bootstrap administrator may be the only account, and the one
/// that entered the whole real register. The announced test logins must refuse such a database
/// even though it holds no other account: what the bootstrap administrator owns counts as real
/// unless the demonstration dataset or a test-data run made it. Shown beside the case where the
/// bootstrap administrator owns only the demonstration dataset and the logins go ahead. A class of
/// its own, on a database of its own, because the accounts other guard tests add would be this
/// one's "real data".
/// </summary>
public sealed class TestLoginBootstrapContentGuardTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "bootstrap-content@t.local";
    private const string AdminPassword = "bootstrap-content-pass-1";

    private readonly string connectionString;
    private readonly SilexGisApiFactory factory;

    public TestLoginBootstrapContentGuardTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword,
        });
    }

    [Fact]
    public async Task Test_logins_refuse_real_content_the_bootstrap_administrator_owns_and_accept_the_demo_it_owns()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await DemoSeedCommand.RunAsync(scope.ServiceProvider, ["seed-demo"])).Outcome.ShouldBe(DemoSeedOutcome.Seeded);
        }

        // The bootstrap administrator owns the demonstration dataset and nothing else: the logins go ahead.
        using (var demoOnly = TestLoginHost())
        {
            var login = await demoOnly.CreateClient().PostAsJsonAsync(
                "/api/v1/auth/login", new { email = "editor@test.local", password = "content-pass-aaa1" });
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // One of its caves now carries a register code rather than the dataset's: the register is real.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Caves.Where(c => c.IdentificationCode == "DEMO-0003")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdentificationCode, "1000/1"))).ShouldBe(1);
            (await db.Users.CountAsync(u => u.Email != AdminEmail && !u.Email!.EndsWith("@test.local")))
                .ShouldBe(0, "the refusal below must come from the content alone");
        }

        using var refused = TestLoginHost();
        var failure = Should.Throw<Exception>(() => refused.CreateClient());
        Flatten(failure).ShouldContain("AllowOnRealData");
        Flatten(failure).ShouldContain("1 cave(s)");
    }

    private SilexGisApiFactory TestLoginHost()
    {
        // The bootstrap administrator exists before the test logins look, as it does on a real start.
        factory.CreateClient().Dispose();
        return new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword,
            ["TestLogins:Enabled"] = "true",
            ["TestLogins:Password"] = "content-pass-aaa1",
            ["TestLogins:AllowOnRealData"] = "false",
        });
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            messages.Add(e.Message);
            if (e is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(Flatten));
            }
        }

        return string.Join(" | ", messages);
    }

    public void Dispose() => factory.Dispose();
}
