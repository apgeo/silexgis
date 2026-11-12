// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The administrators' test-data generator: who may reach it and when, what a run writes and
/// lists, and that removal takes away exactly what was listed — never data somebody else added.
/// </summary>
public sealed class TestDataGeneratorTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/admin/test-data";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient editor = null!;

    public TestDataGeneratorTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"td-admin-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"td-editor-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"td-admin-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"td-editor-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Only_a_full_administrator_reaches_the_generator_and_only_while_it_is_switched_on()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Base)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // An editor writes caves but is not a full administrator: refused, and not offered it.
        await ShouldBeProblemAsync(await editor.GetAsync(Base), HttpStatusCode.Forbidden, "access.forbidden");
        await ShouldBeProblemAsync(
            await editor.PostAsJsonAsync($"{Base}/runs", new { profileName = "small", confirmAlongsideExistingData = true }),
            HttpStatusCode.Forbidden, "access.forbidden");
        (await CapabilityAsync(editor)).ShouldBeFalse();

        // The positive half of the same request.
        (await admin.GetAsync(Base)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CapabilityAsync(admin)).ShouldBeTrue();

        // The administrators switch it off: the routes answer as if it did not exist.
        var off = await admin.PutAsJsonAsync("/api/v1/admin/settings/test-data", new { enabled = false });
        off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        var snapshot = await off.Content.ReadFromJsonAsync<JsonElement>();
        snapshot.GetProperty("testData").GetProperty("enabled").GetBoolean().ShouldBeFalse();
        snapshot.GetProperty("testData").GetProperty("deploymentAllows").GetBoolean().ShouldBeTrue();
        try
        {
            await ShouldBeProblemAsync(await admin.GetAsync(Base), HttpStatusCode.NotFound, "testdata.disabled");
            await ShouldBeProblemAsync(
                await admin.PostAsJsonAsync($"{Base}/runs", new { profileName = "small", confirmAlongsideExistingData = true }),
                HttpStatusCode.NotFound, "testdata.disabled");
            (await CapabilityAsync(admin)).ShouldBeFalse();
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/v1/admin/settings/test-data", new { enabled = true }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // An editor may not flip the switch either.
        (await editor.PutAsJsonAsync("/api/v1/admin/settings/test-data", new { enabled = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_deployment_that_does_not_allow_the_generator_shows_no_trace_of_it()
    {
        using var closed = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["TestData:Allowed"] = "false" },
            JobWorkers.RemoveFrom,
            ownHost: true);
        await AuthHelper.CreateUserAsync(closed, GlobalRoles.Admin, $"td-closed-{suffix}@t.local");
        using var client = await AuthHelper.BearerClientAsync(closed, $"td-closed-{suffix}@t.local");

        await ShouldBeProblemAsync(await client.GetAsync(Base), HttpStatusCode.NotFound, "testdata.disabled");
        (await CapabilityAsync(client)).ShouldBeFalse();
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        settings.GetProperty("testData").GetProperty("deploymentAllows").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_profile_that_cannot_run_is_refused_with_its_problems()
    {
        var before = await RunCountAsync();
        var zero = await admin.PostAsJsonAsync($"{Base}/runs", new
        {
            profile = new { caves = new { count = 0 } },
            confirmAlongsideExistingData = true,
        });
        await ShouldBeProblemAsync(zero, HttpStatusCode.BadRequest, "testdata.profile_invalid");

        var unknown = await admin.PostAsJsonAsync($"{Base}/runs", new { profileName = "enormous" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RunCountAsync()).ShouldBe(before, "a refused profile must not leave a run behind");
    }

    [Fact]
    public async Task A_run_writes_its_caves_lists_every_row_and_removal_takes_back_exactly_those()
    {
        var runId = await StartAsync(Profile(seed: 101, count: 12));

        // One run at a time.
        await ShouldBeProblemAsync(
            await admin.PostAsJsonAsync($"{Base}/runs", new { profileName = "small", confirmAlongsideExistingData = true }),
            HttpStatusCode.Conflict, "testdata.run_in_progress");

        var run = await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId);
        run.GetProperty("status").GetString().ShouldBe("succeeded", run.ToString());
        var counts = run.GetProperty("counts");
        counts.GetProperty("caves").GetInt32().ShouldBe(12);
        counts.GetProperty("entrances").GetInt32().ShouldBeGreaterThanOrEqualTo(12);
        counts.GetProperty("massifs").GetInt32().ShouldBe(2);
        counts.GetProperty("protectedCaves").GetInt32().ShouldBe(1);

        List<Guid> listed;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            listed = (await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync())
                .Select(Guid.Parse).ToList();

            // Everything listed exists, carries the marking tag, and every generated cave is listed.
            var features = await db.Features.Where(f => listed.Contains(f.Id)).ToListAsync();
            features.Count.ShouldBe(listed.Count);
            features.Count(f => f.Kind == FeatureKind.Cave).ShouldBe(12);
            var tagged = await db.Taggings.Where(t => t.FeatureId != null && listed.Contains(t.FeatureId.Value)).CountAsync();
            tagged.ShouldBe(listed.Count);

            // The shares asked for: something private, something protected, and the protection reaching the entrances.
            features.ShouldContain(f => f.Kind == FeatureKind.Cave && f.Visibility == Visibility.Private);
            features.ShouldContain(f => f.Kind == FeatureKind.CaveEntrance && f.IsProtectedEffective);
            features.Where(f => f.Kind == FeatureKind.Cave).ShouldAllBe(f => f.Description!.Contains("testgenerated"));

            // The history names the generator as the source of every row it wrote.
            var listedText = listed.Select(id => id.ToString()).ToList();
            var history = await db.AuditEntries
                .Where(a => a.EntityType!.StartsWith(FeatureAudit.RootName) && listedText.Contains(a.EntityId!))
                .ToListAsync();
            history.ShouldNotBeEmpty();
            history.ShouldAllBe(a => a.Source == ChangeSource.Generated);
        }

        // The coverage report measures every table generated so far, and finds rows in each.
        var coverage = await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}/coverage");
        var measured = coverage.EnumerateArray().ToDictionary(t => t.GetProperty("table").GetString()!, t => t.GetProperty("rows").GetInt64());
        foreach (var table in TestDataTables.GeneratedNow())
        {
            measured.ShouldContainKey(table);
            measured[table].ShouldBeGreaterThan(0, $"the run wrote no {table}");
        }

        var caves = coverage.EnumerateArray().Single(t => t.GetProperty("table").GetString() == "caves");
        caves.GetProperty("columns").EnumerateArray()
            .Single(c => c.GetProperty("column").GetString() == "identification_code")
            .GetProperty("nonNull").GetInt64().ShouldBe(12);

        // Removal needs saying twice.
        await ShouldBeProblemAsync(
            await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = false }),
            HttpStatusCode.BadRequest, "testdata.confirm_required");
        var removing = await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true });
        removing.StatusCode.ShouldBe(HttpStatusCode.Accepted, await removing.Content.ReadAsStringAsync());

        var removed = await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId);
        removed.GetProperty("status").GetString().ShouldBe("removed");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.IgnoreQueryFilters().CountAsync(f => listed.Contains(f.Id))).ShouldBe(0);
            (await db.TestDataRows.CountAsync(r => r.RunId == runId)).ShouldBe(0);
        }

        // Gone is gone: a removed run cannot be removed again.
        await ShouldBeProblemAsync(
            await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true }),
            HttpStatusCode.Conflict, "testdata.run_not_removable");
    }

    [Fact]
    public async Task Generating_beside_data_no_run_created_needs_confirming_and_removal_leaves_that_data_alone()
    {
        var own = await CreateCaveAsync($"Peștera proprie {suffix}", parentId: null);

        var refused = await admin.PostAsJsonAsync($"{Base}/runs", new { profile = Profile(seed: 202, count: 4) });
        await ShouldBeProblemAsync(refused, HttpStatusCode.Conflict, "testdata.existing_data_confirm_required");
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("existing").GetProperty("caves").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        var runId = await StartAsync(Profile(seed: 202, count: 4));
        (await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId)).GetProperty("status").GetString().ShouldBe("succeeded");
        (await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}")).GetProperty("alongsideExistingData").GetBoolean().ShouldBeTrue();

        // Somebody puts a cave of their own inside a generated karst area.
        Guid area;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listed = await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync();
            var karstArea = await db.FeatureTypes.Where(t => t.Code == TestDataVocabulary.KarstAreaType).Select(t => t.Id).SingleAsync();
            area = await db.Features
                .Where(f => listed.Contains(f.Id.ToString()) && f.FeatureTypeId == karstArea)
                .Select(f => f.Id).FirstAsync();
        }

        var inside = await CreateCaveAsync($"Peștera adăugată {suffix}", area);

        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var removed = await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId);
        removed.GetProperty("status").GetString().ShouldBe("removed");
        removed.GetProperty("notes").EnumerateArray().ShouldContain(n => n.GetString()!.Contains("kept", StringComparison.Ordinal));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AnyAsync(f => f.Id == own)).ShouldBeTrue("a cave nobody generated was removed");
            (await db.Features.AnyAsync(f => f.Id == inside)).ShouldBeTrue("a cave added inside a generated area was removed");
            (await db.Features.AnyAsync(f => f.Id == area)).ShouldBeTrue("the area holding somebody's cave was removed from under it");
            (await db.Features.CountAsync(f => f.Kind == FeatureKind.Cave && f.Description!.Contains("testgenerated"))).ShouldBe(0);
        }
    }

    [Fact]
    public async Task The_same_seed_generates_the_same_caves_again_and_a_cancelled_run_writes_nothing()
    {
        var first = await StartAsync(Profile(seed: 303, count: 6));
        await RunJobAsync(ProcessingJobKinds.TestDataGenerate, first);
        var before = await GeneratedCavesAsync(first);
        before.Count.ShouldBe(6);
        await RemoveAsync(first);

        var second = await StartAsync(Profile(seed: 303, count: 6));
        await RunJobAsync(ProcessingJobKinds.TestDataGenerate, second);
        (await GeneratedCavesAsync(second)).ShouldBe(before);
        await RemoveAsync(second);

        // Cancelled before its job ran: closed at once, and the job finds nothing to do.
        var cancelled = await StartAsync(Profile(seed: 404, count: 6));
        var answer = await admin.PostAsync($"{Base}/runs/{cancelled}/cancel", null);
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var run = await RunJobAsync(ProcessingJobKinds.TestDataGenerate, cancelled);
        run.GetProperty("status").GetString().ShouldBe("cancelled");
        (await GeneratedCavesAsync(cancelled)).ShouldBeEmpty();
        await ShouldBeProblemAsync(await admin.PostAsync($"{Base}/runs/{cancelled}/cancel", null),
            HttpStatusCode.Conflict, "testdata.run_not_cancellable");
        await RemoveAsync(cancelled);
    }

    private static object Profile(long seed, int count) => new
    {
        seed,
        regions = new[] { "bucegi", "praid" },
        caves = new { count },
    };

    private async Task<Guid> StartAsync(object profile)
    {
        var response = await admin.PostAsJsonAsync($"{Base}/runs", new { profile, confirmAlongsideExistingData = true });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task RemoveAsync(Guid runId)
    {
        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId)).GetProperty("status").GetString().ShouldBe("removed");
    }

    /// <summary>Runs the newest job of <paramref name="kind"/> for the run and answers the run as the page would read it.</summary>
    private async Task<JsonElement> RunJobAsync(string kind, Guid runId)
    {
        var job = (await QueuedJob.OfKindAsync(factory.Services, kind))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await QueuedJob.RunAsync(factory.Services, job.Id);
        return await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
    }

    private async Task<List<string>> GeneratedCavesAsync(Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var listed = await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync();
        return await db.Caves
            .Where(c => listed.Contains(c.Id.ToString()))
            .OrderBy(c => c.IdentificationCode)
            .Select(c => c.IdentificationCode + " " + c.Feature.Name)
            .ToListAsync();
    }

    private async Task<Guid> CreateCaveAsync(string name, Guid? parentId)
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
        }

        var response = await admin.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "authenticated",
            parentId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<int> RunCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().TestDataRuns.CountAsync();
    }

    private static async Task<bool> CapabilityAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/me/capabilities")).GetProperty("testDataGenerator").GetBoolean();

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
