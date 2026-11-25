// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The announced test logins refuse to start on a database holding an account they did not
/// create, unless the operator says that is known and wanted — an environment file copied from a
/// test host onto a real one must not print a full administrator's password on its sign-in page.
/// The refusal is shown beside the cases where the logins go ahead.
/// </summary>
public sealed class TestLoginRealDataGuardTests : IDisposable, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "bootstrap-logins@t.local";
    private const string AdminPassword = "bootstrap-logins-pass-1";

    private readonly string connectionString;
    private readonly SilexGisApiFactory factory;

    public TestLoginRealDataGuardTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword,
        });
    }

    [Fact]
    public async Task Test_logins_refuse_a_database_with_somebody_elses_account_unless_told_it_is_known()
    {
        // Built-in accounts only: the test logins go ahead.
        using (var clean = TestLoginHost(allowOnRealData: false))
        {
            var login = await clean.CreateClient().PostAsJsonAsync(
                "/api/v1/auth/login", new { email = "editor@test.local", password = "guard-pass-aaa1" });
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "somebody@club.example");

        using (var refused = TestLoginHost(allowOnRealData: false))
        {
            var failure = Should.Throw<Exception>(() => refused.CreateClient());
            Flatten(failure).ShouldContain("AllowOnRealData");
            Flatten(failure).ShouldNotContain("somebody@club.example", Case.Insensitive);
        }

        using var allowed = TestLoginHost(allowOnRealData: true);
        var allowedLogin = await allowed.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "editor@test.local", password = "guard-pass-aaa1" });
        allowedLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private SilexGisApiFactory TestLoginHost(bool allowOnRealData)
    {
        // The bootstrap administrator exists before the test logins look, as it does on a real start.
        factory.CreateClient().Dispose();
        return new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword,
            ["TestLogins:Enabled"] = "true",
            ["TestLogins:Password"] = "guard-pass-aaa1",
            ["TestLogins:AllowOnRealData"] = allowOnRealData ? "true" : "false",
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
