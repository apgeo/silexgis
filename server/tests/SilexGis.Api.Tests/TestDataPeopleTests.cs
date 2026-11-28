// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Tests;

/// <summary>
/// The people a test-data run invents, on an installation that holds nothing else: every kind of
/// account exists and can be signed in to with the password the run was given (the shut one
/// cannot), nobody generated can be sent a message, club-only caves are bound to clubs and seen
/// by their members only, the grants make the accounts see protected caves differently, and
/// removal takes every one of those rows back.
/// </summary>
/// <remarks>
/// One test on purpose: the class's database starts empty, and shares and QR publications are
/// only generated on an installation holding nothing but generated data, which a second test
/// leaving data behind would change.
/// </remarks>
public sealed class TestDataPeopleTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/admin/test-data";
    private const string AccountPassword = "generated-people-pass-7";

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;

    public TestDataPeopleTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tdp-admin-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tdp-admin-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_run_invents_people_who_see_the_data_as_their_rights_say_and_removal_takes_them_back()
    {
        var overview = await admin.GetFromJsonAsync<JsonElement>(Base);
        var existing = overview.GetProperty("existing");
        (existing.GetProperty("caves").GetInt32() + existing.GetProperty("trips").GetInt32() + existing.GetProperty("documents").GetInt32())
            .ShouldBe(0, "the class's database must start empty for this test to mean anything");

        // A password the installation's rules refuse is refused before anything is queued.
        var weak = await admin.PostAsJsonAsync($"{Base}/runs", new { profile = Profile(), accountPassword = "x" });
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await weak.Content.ReadAsStringAsync());
        (await weak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("testdata.password_invalid");

        var start = await admin.PostAsJsonAsync($"{Base}/runs", new { profile = Profile(), accountPassword = AccountPassword });
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted, await start.Content.ReadAsStringAsync());
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await start.Content.ReadAsStringAsync()).ShouldNotContain(AccountPassword);

        var run = await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId);
        run.GetProperty("status").GetString().ShouldBe("succeeded", run.ToString());
        run.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ShouldContain(TestDataNotes.AccountsSharePassword);
        var counts = run.GetProperty("counts");
        counts.GetProperty("users").GetInt32().ShouldBe(12);
        counts.GetProperty("cavers").GetInt32().ShouldBe(24);
        counts.GetProperty("cavingGroups").GetInt32().ShouldBe(3);

        var plan = TestDataPlanner.Plan(TestDataProfile.FromJson(JsonSerializer.Serialize(Profile(), TestDataProfile.JsonOptions)));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listed = await db.TestDataRows.Where(r => r.RunId == runId).ToListAsync();
            var userIds = listed.Where(r => r.TableName == "users").Select(r => Guid.Parse(r.RowId)).ToList();
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();

            // Nobody generated can be written to: reserved addresses and numbers, and the paid and mailed channels off.
            users.ShouldAllBe(u => u.Email!.EndsWith("@" + TestDataPeoplePlanner.EmailDomain));
            users.Where(u => u.PhoneNumber != null).ShouldAllBe(u => u.PhoneNumber!.StartsWith(TestDataPeoplePlanner.PhonePrefix));
            var configurable = NotificationCategories.All.Count(NotificationCategories.IsUserConfigurable);
            foreach (var user in users)
            {
                var off = await db.UserNotificationPreferences
                    .CountAsync(p => p.UserId == user.Id && p.Choice == NotificationChannelChoice.Off
                        && (p.Channel == NotificationChannelKind.Email || p.Channel == NotificationChannelKind.Sms));
                off.ShouldBe(configurable * 2, $"{user.Email} can still be mailed or texted");
            }

            // Each club is led, has the two permission groups a new club gets, and its owner manages it.
            var clubIds = listed.Where(r => r.TableName == "caving_groups").Select(r => Guid.Parse(r.RowId)).ToList();
            foreach (var club in clubIds)
            {
                (await db.CavingGroupMemberships.CountAsync(m => m.CavingGroupId == club && m.Role == CavingGroupRole.Owner)).ShouldBe(1);
                (await db.PermissionGroupMembers.CountAsync(m => m.MemberKind == AccessSubjectKind.CavingGroup && m.MemberId == club)).ShouldBe(1);
            }

            // Club-only caves are bound to a club the run made; none is written as signed-in-only.
            var featureIds = listed.Where(r => r.TableName == "features").Select(r => Guid.Parse(r.RowId)).ToList();
            var clubOnly = await db.Features.Where(f => featureIds.Contains(f.Id) && f.Kind == FeatureKind.Cave && f.Visibility == Visibility.CavingGroup).ToListAsync();
            clubOnly.Count.ShouldBe(plan.Caves.Count(c => c.Visibility == Visibility.CavingGroup));
            clubOnly.ShouldAllBe(f => f.CavingGroupId != null && clubIds.Contains(f.CavingGroupId.Value));
            run.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ShouldNotContain(TestDataNotes.ClubOnlyAsSignedIn);

            // On an installation with nothing else, the run opens a few caves to people without an account too.
            listed.Count(r => r.TableName == "feature_shares").ShouldBe(2);
            listed.Count(r => r.TableName == "cave_qr_publications").ShouldBe(2);
        }

        // The coverage report finds rows in every table the run's parts write.
        var coverage = await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}/coverage");
        var measured = coverage.EnumerateArray().ToDictionary(t => t.GetProperty("table").GetString()!, t => t.GetProperty("rows").GetInt64());
        foreach (var table in TestDataTables.GeneratedNow())
        {
            measured.ShouldContainKey(table);
            measured[table].ShouldBeGreaterThan(0, $"the run wrote no {table}");
        }

        await AccountsSeeTheDataAsTheirRightsSayAsync(runId, plan);

        // Removal takes every listed row back, people included, and forgets the password.
        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId)).GetProperty("status").GetString().ShouldBe("removed");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var report = await TestDataCoverageSql.MeasureAsync(db, runId, CancellationToken.None);
            report.ShouldAllBe(t => t.Rows == 0);
            (await db.Users.CountAsync(u => u.Email!.EndsWith("@" + TestDataPeoplePlanner.EmailDomain))).ShouldBe(0);
            (await db.Cavers.CountAsync(c => c.Email != null && c.Email.EndsWith("@" + TestDataPeoplePlanner.EmailDomain))).ShouldBe(0);
            (await db.PermissionGroups.CountAsync(g => g.IsSeeded && g.Name.Contains("«"))).ShouldBe(0);
            (await db.TestDataRuns.SingleAsync(r => r.Id == runId)).AccountPasswordHash.ShouldBeNull();

            // What the run did not write is still there: the seeded groups and the administrator.
            (await db.PermissionGroups.AnyAsync(g => g.Slug == SeededPermissionGroups.AllUsersSlug)).ShouldBeTrue();
            (await db.Users.AnyAsync(u => u.Email == $"tdp-admin-{suffix}@t.local")).ShouldBeTrue();
        }
    }

    /// <summary>
    /// The paired check: signed in as generated accounts, the same caves read differently — a
    /// club's own cave only to its members, a protected cave exactly only to whoever holds a grant
    /// for it — and the shut account cannot sign in at all.
    /// </summary>
    private async Task AccountsSeeTheDataAsTheirRightsSayAsync(Guid runId, TestDataWorldPlan plan)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var listed = await db.TestDataRows.Where(r => r.RunId == runId).ToListAsync();
        var userIds = listed.Where(r => r.TableName == "users").Select(r => Guid.Parse(r.RowId)).ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var cavers = await db.Cavers.Where(c => c.UserId != null && userIds.Contains(c.UserId.Value)).ToDictionaryAsync(c => c.UserId!.Value);
        var memberships = await db.CavingGroupMemberships.ToListAsync();
        var entryIds = listed.Where(r => r.TableName == "access_entries").Select(r => long.Parse(r.RowId, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var grants = await db.AccessEntries.Where(e => entryIds.Contains(e.Id)).ToListAsync();
        var groupsOf = (Guid user) => memberships.Where(m => m.CaverId == cavers[user].Id).Select(m => m.CavingGroupId).ToHashSet();
        var plainMembers = users.Values
            .Where(u => plan.People.AccountHolders.Single(c => c.Account!.Email == u.Email).Account!.Kind == TestDataAccountKind.Member)
            .Where(u => !grants.Any(g => g.SubjectKind == AccessSubjectKind.User && g.SubjectId == u.Id))
            .ToList();
        plainMembers.ShouldNotBeEmpty("the plan has no member without a grant of their own to compare with");

        // The shut account cannot sign in; every other kind can, with the run's password.
        var shut = users.Values.Single(u => u.LockoutEnd > DateTimeOffset.UtcNow);
        using (var client = factory.CreateClient())
        {
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = shut.Email, password = AccountPassword });
            login.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        }

        // A club's own cave: its member reads it, a member of no club of its does not.
        var clubCaves = await db.Features.Where(f => f.Kind == FeatureKind.Cave && f.Visibility == Visibility.CavingGroup && f.CavingGroupId != null).ToListAsync();
        var signable = users.Values.Where(u => u.LockoutEnd == null && !u.TwoFactorEmailEnabled).ToList();
        var (clubCave, insider, outsider) = clubCaves
            .Select(c => (Cave: c,
                Insider: signable.FirstOrDefault(u => groupsOf(u.Id).Contains(c.CavingGroupId!.Value)),
                Outsider: plainMembers.FirstOrDefault(u => !groupsOf(u.Id).Contains(c.CavingGroupId!.Value))))
            .FirstOrDefault(x => x.Insider is not null && x.Outsider is not null);
        clubCave.ShouldNotBeNull("no club-only cave has both a member of its club and a plain member outside it to compare");
        using (var client = await AuthHelper.BearerClientAsync(factory, insider!.Email!, AccountPassword))
        {
            (await client.GetAsync($"/api/v1/features/{clubCave.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var client = await AuthHelper.BearerClientAsync(factory, outsider!.Email!, AccountPassword))
        {
            (await client.GetAsync($"/api/v1/features/{clubCave.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // A protected cave everyone signed in may read: exact for the account granted it, snapped for one without.
        var protectedCaves = await db.Features
            .Where(f => f.Kind == FeatureKind.Cave && f.IsProtectedEffective
                && (f.Visibility == Visibility.Public || f.Visibility == Visibility.Authenticated))
            .Select(f => f.Id)
            .ToListAsync();
        var granted = grants.FirstOrDefault(g => g.SubjectKind == AccessSubjectKind.User && g.ScopeKind == AccessScopeKind.Subtree
            && g.ScopeFeatureId is { } cave && protectedCaves.Contains(cave)
            && users[g.SubjectId!.Value].LockoutEnd == null && !users[g.SubjectId!.Value].TwoFactorEmailEnabled);
        granted.ShouldNotBeNull(
            "the plan gives no signed-in-visible protected cave to an account by a grant; grants: "
            + string.Join("; ", grants.Select(g => $"{g.SubjectKind} {g.Effect} {g.ScopeKind} {g.Actions} {g.ScopeFeatureId}"))
            + "; protected and visible: " + string.Join(", ", protectedCaves));
        var clubsSeeingIt = grants.Where(g => g.SubjectKind == AccessSubjectKind.CavingGroup && g.ScopeFeatureId == granted.ScopeFeatureId)
            .Select(g => g.SubjectId!.Value).ToHashSet();
        var without = plainMembers.FirstOrDefault(u => !groupsOf(u.Id).Overlaps(clubsSeeingIt));
        without.ShouldNotBeNull();

        using (var client = await AuthHelper.BearerClientAsync(factory, users[granted.SubjectId!.Value].Email!, AccountPassword))
        {
            var envelope = await client.GetFromJsonAsync<JsonElement>($"/api/v1/features/{granted.ScopeFeatureId}");
            envelope.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
        }

        using (var client = await AuthHelper.BearerClientAsync(factory, without.Email!, AccountPassword))
        {
            var envelope = await client.GetFromJsonAsync<JsonElement>($"/api/v1/features/{granted.ScopeFeatureId}");
            envelope.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        }

        // And the administrator sees the club's cave and the protected one exactly.
        (await admin.GetAsync($"/api/v1/features/{clubCave.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminView = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/features/{granted.ScopeFeatureId}");
        adminView.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeFalse();
    }

    private static object Profile() => new
    {
        seed = 4242,
        regions = new[] { "bucegi" },
        caves = new { count = 30 },
        people = new { clubs = 2, organisations = 1, cavers = 24, accounts = 12 },
    };

    private async Task<JsonElement> RunJobAsync(string kind, Guid runId)
    {
        var job = (await QueuedJob.OfKindAsync(factory.Services, kind))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await QueuedJob.RunAsync(factory.Services, job.Id);
        return await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
    }
}
