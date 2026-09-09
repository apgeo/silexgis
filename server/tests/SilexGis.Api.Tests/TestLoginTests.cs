// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The test-installation demo logins: off by default (nothing seeded, nothing announced), and
/// when switched on the sign-in config names three working accounts whose rulesets differ —
/// a full administrator, an editor and a baseline viewer — with the announced password kept
/// true even when configuration changes it after the accounts already exist.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TestLoginTests : IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    public TestLoginTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    [Fact]
    public async Task Auth_config_carries_no_test_logins_by_default()
    {
        var config = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");

        config.GetProperty("testLogins").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_switch_gates_the_writes_and_not_only_the_announcement()
    {
        // Both halves live in one test on purpose. Every class in this suite shares one
        // database, so "no account with this address exists" is not a claim a test can make --
        // a sibling running with the switch on has already created them. What *is* provable,
        // and is the property that matters, is that a host started with the switch off writes
        // nothing: it does not bring an existing account to the password it was configured
        // with, which is the one observable write this seeder performs after creation.
        using (var enabled = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
               {
                   ["TestLogins:Enabled"] = "true",
                   ["TestLogins:Password"] = "gated-pass-aaa1",
               }))
        {
            var seeded = await enabled.CreateClient().PostAsJsonAsync(
                "/api/v1/auth/login", new { email = "editor@test.local", password = "gated-pass-aaa1" });
            seeded.StatusCode.ShouldBe(HttpStatusCode.OK, "the enabled half must establish the baseline");
        }

        using var disabled = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["TestLogins:Enabled"] = "false",
            ["TestLogins:Password"] = "gated-pass-bbb2",
        });

        // The disabled host must not have reconciled the account to its configured password...
        var wouldHaveWritten = await disabled.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "editor@test.local", password = "gated-pass-bbb2" });
        wouldHaveWritten.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and must have left the earlier one alone rather than clearing it.
        var untouched = await disabled.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "editor@test.local", password = "gated-pass-aaa1" });
        untouched.StatusCode.ShouldBe(HttpStatusCode.OK);

        // And it announces nothing regardless of what sits in the database.
        var config = await disabled.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");
        config.GetProperty("testLogins").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Enabled_installation_announces_three_working_accounts_with_distinct_rulesets()
    {
        using var enabled = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["TestLogins:Enabled"] = "true",
        });

        var config = await enabled.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");
        var logins = config.GetProperty("testLogins").EnumerateArray().ToList();

        logins.Count.ShouldBe(3);
        logins[0].GetProperty("role").GetString().ShouldBe("administrator");
        logins[0].GetProperty("email").GetString().ShouldBe("admin@test.local");
        logins[1].GetProperty("role").GetString().ShouldBe("editor");
        logins[2].GetProperty("role").GetString().ShouldBe("viewer");

        // Every announced credential signs in — the page prints these, so each must work.
        foreach (var login in logins)
        {
            using var client = enabled.CreateClient();
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = login.GetProperty("email").GetString(),
                password = login.GetProperty("password").GetString(),
            });
            response.StatusCode.ShouldBe(
                HttpStatusCode.OK, $"sign-in failed for {login.GetProperty("email").GetString()}");
        }

        using var scope = enabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();

        (await GroupSlugsOfAsync(db, userManager, "admin@test.local"))
            .ShouldBe(["full-administrators"]);
        (await GroupSlugsOfAsync(db, userManager, "editor@test.local"))
            .ShouldBe(["editors"]);
        // The viewer holds nothing beyond the implicit All Users baseline.
        (await GroupSlugsOfAsync(db, userManager, "viewer@test.local")).ShouldBeEmpty();

        // The password is public, so lockout would be a denial-of-service lever, not a defence.
        var admin = await userManager.FindByEmailAsync("admin@test.local");
        admin!.LockoutEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_changed_password_is_applied_to_accounts_that_already_exist()
    {
        using (var first = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
               {
                   ["TestLogins:Enabled"] = "true",
                   ["TestLogins:Password"] = "first-pass-123",
               }))
        {
            _ = first.Services; // start the host so seeding runs
        }

        using var second = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["TestLogins:Enabled"] = "true",
            ["TestLogins:Password"] = "second-pass-456",
        });

        var stale = await second.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "editor@test.local", password = "first-pass-123" });
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var current = await second.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "editor@test.local", password = "second-pass-456" });
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<List<string>> GroupSlugsOfAsync(
        SilexGisDbContext db, UserManager<SilexGisUser> userManager, string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull($"account {email} was not seeded");
        return await db.PermissionGroupMembers
            .Where(m => m.MemberKind == AccessSubjectKind.User && m.MemberId == user!.Id)
            .Join(db.PermissionGroups, m => m.PermissionGroupId, g => g.Id, (m, g) => g.Slug)
            .OrderBy(s => s)
            .ToListAsync();
    }

    public void Dispose() => factory.Dispose();
}
