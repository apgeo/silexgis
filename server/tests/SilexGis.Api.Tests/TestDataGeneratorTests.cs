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
        (await unknown.RefusedFieldsAsync()).ShouldBe(["ProfileName"]);
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
        List<Guid> generatedCaves;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listed = await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync();
            var karstArea = await db.FeatureTypes.Where(t => t.Code == TestDataVocabulary.KarstAreaType).Select(t => t.Id).SingleAsync();
            area = await db.Features
                .Where(f => listed.Contains(f.Id.ToString()) && f.FeatureTypeId == karstArea)
                .Select(f => f.Id).FirstAsync();
            generatedCaves = await db.Features
                .Where(f => listed.Contains(f.Id.ToString()) && f.Kind == FeatureKind.Cave)
                .Select(f => f.Id).ToListAsync();
        }
        generatedCaves.ShouldNotBeEmpty();

        var inside = await CreateCaveAsync($"Peștera adăugată {suffix}", area);

        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var removed = await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId);
        removed.GetProperty("status").GetString().ShouldBe("removed");
        removed.GetProperty("notes").EnumerateArray().ShouldContain(n => n.GetString()!.StartsWith("keptBeneath:", StringComparison.Ordinal));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AnyAsync(f => f.Id == own)).ShouldBeTrue("a cave nobody generated was removed");
            (await db.Features.AnyAsync(f => f.Id == inside)).ShouldBeTrue("a cave added inside a generated area was removed");
            (await db.Features.AnyAsync(f => f.Id == area)).ShouldBeTrue("the area holding somebody's cave was removed from under it");
            // This run's own caves only: the class shares one database, and another test keeps
            // generated caves people have used on purpose.
            (await db.Features.IgnoreQueryFilters().CountAsync(f => generatedCaves.Contains(f.Id))).ShouldBe(0);
        }
    }

    [Fact]
    public async Task Removal_keeps_a_generated_cave_people_have_used_and_says_why()
    {
        var own = await CreateCaveAsync($"Peștera legată {suffix}", parentId: null);
        var runId = await StartAsync(Profile(seed: 505, count: 4));
        (await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId)).GetProperty("status").GetString().ShouldBe("succeeded");

        List<Guid> caves;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listed = (await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync())
                .Select(Guid.Parse).ToList();
            caves = await db.Features.Where(f => listed.Contains(f.Id) && f.Kind == FeatureKind.Cave)
                .OrderBy(f => f.Id).Select(f => f.Id).ToListAsync();
        }

        // Somebody links one generated cave to a cave of their own — the way a trip names a cave.
        var used = caves[0];
        var linked = await admin.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = "A link somebody made by hand",
            members = new[] { used, own }.Select((id, index) => new
            {
                targetType = "feature",
                targetId = id,
                isMain = false,
                sortOrder = index,
                note = (string?)null,
                anchorKind = "whole",
                anchor = (object?)null,
                anchorFileId = (Guid?)null,
            }).ToArray(),
        });
        linked.StatusCode.ShouldBe(HttpStatusCode.Created, await linked.Content.ReadAsStringAsync());
        var linkId = (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // And somebody else draws a typed link from a cave of their own to a second generated one.
        // The link's row cascades with either end, so the generated end has to stay.
        var drawnTo = caves[1];
        long featureLinkId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var kindId = await db.LinkKinds.OrderBy(k => k.Id).Select(k => k.Id).FirstAsync();
            var featureLink = new FeatureLink { FromId = own, ToId = drawnTo, LinkKindId = kindId, Note = "drawn by hand" };
            db.FeatureLinks.Add(featureLink);
            await db.SaveChangesAsync();
            featureLinkId = featureLink.Id;
        }

        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var removed = await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId);
        removed.GetProperty("status").GetString().ShouldBe("removed");
        removed.GetProperty("notes").EnumerateArray()
            .ShouldContain(n => n.GetString()!.StartsWith("keptInUse:", StringComparison.Ordinal), removed.ToString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AnyAsync(f => f.Id == used)).ShouldBeTrue("a generated cave somebody linked was removed");
            (await db.ResLinkMembers.CountAsync(m => m.ResLinkId == linkId)).ShouldBe(2, "the link lost a member");
            (await db.Features.CountAsync(f => f.Kind == FeatureKind.CaveEntrance && f.Entrance!.CaveFeatureId == used))
                .ShouldBeGreaterThan(0, "the kept cave lost its entrances");
            (await db.Features.AnyAsync(f => f.Id == drawnTo)).ShouldBeTrue("a generated cave somebody drew a link to was removed");
            (await db.FeatureLinks.AnyAsync(l => l.Id == featureLinkId)).ShouldBeTrue("the drawn link was removed with its generated end");

            // The positive half: the generated caves nobody touched went.
            var untouched = caves.Skip(2).ToList();
            untouched.ShouldNotBeEmpty();
            (await db.Features.IgnoreQueryFilters().CountAsync(f => untouched.Contains(f.Id))).ShouldBe(0);

            // The kept caves stay in this class's database; their codes are cleared so another
            // test's run, whose planned codes may be the same, writes its own in full.
            await db.Caves.Where(c => c.Id == used || c.Id == drawnTo)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.IdentificationCode, (string?)null));
        }
    }

    [Fact]
    public async Task A_planned_code_a_cave_here_already_carries_is_left_off_the_generated_cave()
    {
        var profile = Profile(seed: 808, count: 3);
        var planned = TestDataPlanner.Plan(TestDataProfile.FromJson(JsonSerializer.Serialize(profile))).Caves;
        var own = await CreateCaveAsync($"Peștera din registru {suffix}", parentId: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Caves.Where(c => c.Id == own)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.IdentificationCode, planned[0].IdentificationCode));
        }

        var runId = await StartAsync(profile);
        var run = await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId);
        run.GetProperty("status").GetString().ShouldBe("succeeded", run.ToString());
        run.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ShouldContain("codesTaken:1", run.ToString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Caves.CountAsync(c => c.IdentificationCode == planned[0].IdentificationCode)).ShouldBe(1, "the code is now ambiguous");
            var listed = await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "features").Select(r => r.RowId).ToListAsync();
            var codes = await db.Caves.Where(c => listed.Contains(c.Id.ToString())).Select(c => c.IdentificationCode).ToListAsync();
            codes.Count.ShouldBe(3);
            codes.Count(c => c == null).ShouldBe(1);
            await db.Caves.Where(c => c.Id == own)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.IdentificationCode, (string?)null));
        }

        await RemoveAsync(runId);
    }

    [Fact]
    public async Task A_removal_that_fails_closes_the_run_as_failed_so_it_neither_blocks_new_runs_nor_stays_unremovable()
    {
        var runId = await StartAsync(Profile(seed: 606, count: 2));
        (await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId)).GetProperty("status").GetString().ShouldBe("succeeded");

        // A registry row of a table the removal does not know makes it throw before it touches anything.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var last = await db.TestDataRows.Where(r => r.RunId == runId).MaxAsync(r => r.Sequence);
            db.TestDataRows.Add(new TestDataRow { RunId = runId, Sequence = last + 1, TableName = "no_such_table", RowId = "1" });
            await db.SaveChangesAsync();
        }

        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var removeJob = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TestDataRemove))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await Should.ThrowAsync<InvalidOperationException>(() => QueuedJob.RunAsync(factory.Services, removeJob.Id));
        var failed = await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
        failed.GetProperty("status").GetString().ShouldBe("failed", failed.ToString());
        failed.GetProperty("error").GetString()!.ShouldContain("no_such_table");

        // Not the one run in progress any more: another can start, and is cancelled again here.
        var next = await StartAsync(Profile(seed: 607, count: 2));
        (await admin.PostAsync($"{Base}/runs/{next}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RemoveAsync(next);

        // And the failed removal can be asked for again; with the stray row gone it now succeeds.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "no_such_table").ExecuteDeleteAsync();
        }

        await RemoveAsync(runId);
        var removed = await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
        removed.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null, removed.ToString());
    }

    [Fact]
    public async Task Starts_sent_at_the_same_moment_queue_one_run_and_refuse_the_rest()
    {
        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => admin.PostAsJsonAsync(
            $"{Base}/runs", new { profile = Profile(seed: 700 + i, count: 2), confirmAlongsideExistingData = true })));
        try
        {
            answers.Count(a => a.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
            answers.Count(a => a.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        }
        finally
        {
            foreach (var accepted in answers.Where(a => a.StatusCode == HttpStatusCode.Accepted))
            {
                var id = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                (await admin.PostAsync($"{Base}/runs/{id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
                await RemoveAsync(id);
            }
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
