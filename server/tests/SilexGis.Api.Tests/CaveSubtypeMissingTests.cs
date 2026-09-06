// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the cave read paths do when a row is only half a cave.
/// </summary>
/// <remarks>
/// <para>
/// A cave is two rows: the feature, which carries the name, the access control and the
/// representative point, and the subtype row that carries everything cave-specific. Only one
/// direction of that pair is guarded by the database — the subtype row's foreign key is
/// composite on identifier and kind, so it cannot exist without its feature — and the other way
/// round nothing refuses. A feature of kind cave with no subtype row is therefore a state the
/// installation can end up in, and it used to make the listing dereference a null inside its
/// projection: one broken row anywhere in the archive answered every page with a server error,
/// for every reader, not only for whoever owned the row.
/// </para>
/// <para>
/// The state is made here by writing straight to the database, which is the only way to make it:
/// no write path produces it, so a test that went through the service could not put the read
/// paths in front of the thing being tested. Both cases restore what they broke — the suite
/// shares one database and other classes assert the integrity verifier finds nothing at all.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CaveSubtypeMissingTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly LogCapture logs = new();
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];
    private HttpClient editor = null!;
    private Guid editorId;
    private long caveTypeId;

    public CaveSubtypeMissingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.AddSingleton<ILogEventSink>(logs));

    public async Task InitializeAsync()
    {
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"csm-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"csm-{tag}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// One row that is only half a cave costs its own place in the listing and nothing more: the
    /// page answers, every other cave is still on it, and the omission is recorded rather than
    /// silently swallowed — a listing that quietly dropped a broken row would leave nobody with
    /// any way to learn it exists.
    /// </summary>
    [Fact]
    public async Task A_cave_feature_without_its_subtype_row_is_left_out_of_the_listing_and_reported()
    {
        var whole = await SeedCaveAsync($"Pestera Intreaga {tag}");
        var broken = await SeedCaveAsync($"Pestera Rupta {tag}");

        // The positive half, in the same test: while both rows are whole, both are listed. Without
        // it "the broken row is not there" would read the same against a filter that dropped both.
        var before = await ListAsync();
        IdsOf(before).ShouldBe(new[] { whole, broken }, ignoreOrder: true);
        before.GetProperty("totalItems").GetInt32().ShouldBe(2);

        var caveTypeOfBroken = await BreakAsync(broken);
        try
        {
            logs.Clear();
            var after = await ListAsync();

            // Answered, not refused, and the readable row is still on it.
            IdsOf(after).ShouldBe(new[] { whole });

            // The count moves with the page: the total is the number of rows a reader can walk
            // through, so what is listed and what is counted still agree.
            after.GetProperty("totalItems").GetInt32().ShouldBe(1);

            // And the omission was said out loud, naming the row an operator has to repair.
            var warning = logs.Lines
                .FirstOrDefault(l => l.Contains("cave subtype row is missing", StringComparison.Ordinal));
            warning.ShouldNotBeNull(
                $"the listing omitted a row without reporting it; captured {logs.Lines.Count} line(s): "
                + string.Join(" | ", logs.Lines.TakeLast(5)));
            warning.ShouldContain(broken.ToString());
            warning.ShouldStartWith("Warning ");

            // The single-cave paths carry the same defect on one reader rather than on the page.
            // A row with no subtype row is nothing that can be shown, and is answered as absent
            // instead of as a fault.
            (await editor.GetAsync($"/api/v1/caves/{broken}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);
            (await editor.GetAsync($"/api/v1/caves/{broken}/summary")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            // The same two reads against the whole row, so the 404s above are about the missing
            // half and not about the caller, the tag or the route.
            (await editor.GetAsync($"/api/v1/caves/{whole}")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await editor.GetAsync($"/api/v1/caves/{whole}/summary")).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await MendAsync(broken, caveTypeOfBroken);
        }

        // Mended, the row is a cave again — which also proves the break was the subtype row and
        // not something the test did to the feature.
        IdsOf(await ListAsync()).ShouldBe(new[] { whole, broken }, ignoreOrder: true);

        // Nothing is left behind for the classes that assert the verifier finds nothing at all.
        await using var scope = factory.Services.CreateAsyncScope();
        var problems = await scope.ServiceProvider
            .GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
        problems.Where(p => p.FeatureId == whole || p.FeatureId == broken).ShouldBeEmpty();
    }

    /// <summary>
    /// A broken row costs its own place and no one else's, across page boundaries too. The
    /// listing reports a total, a client walks the pages that total implies, and the union of
    /// what it gets back has to be every cave it may read. Counting only listable rows while
    /// paging over a set that still contained the broken ones broke exactly that: the broken
    /// row consumed a slot inside the last advertised window and pushed a whole, readable cave
    /// past the last page any client would ask for, where nothing would ever have shown it.
    /// </summary>
    [Fact]
    public async Task A_broken_row_does_not_push_a_readable_cave_off_the_last_page()
    {
        // Sorted by name, so which row sits first is decided here and not by the clock: the
        // broken one leads, which is the arrangement that strands a later row.
        var broken = await SeedCaveAsync($"Pestera A Rupta {tag}");
        var second = await SeedCaveAsync($"Pestera B {tag}");
        var third = await SeedCaveAsync($"Pestera C {tag}");

        var caveType = await BreakAsync(broken);
        try
        {
            // Two per page over three rows, one of them broken: without the fix the total says
            // two, so a client asks for one page, and that page's window is the broken row plus
            // one whole one — leaving the third reachable through no page at all.
            var page = await ListAsync(pageSize: 2);
            var total = page.GetProperty("totalItems").GetInt32();
            total.ShouldBe(2);

            var seen = new List<Guid>(IdsOf(page));
            var pages = (total + 1) / 2;
            for (var n = 2; n <= pages; n++)
            {
                seen.AddRange(IdsOf(await ListAsync(pageSize: 2, page: n)));
            }

            // Every readable cave appeared on some page the total advertised, exactly once.
            seen.ShouldBe(new[] { second, third }, ignoreOrder: true);
        }
        finally
        {
            await MendAsync(broken, caveType);
        }
    }

    /// <summary>
    /// The verifier's side of the same invariant: the state the listing now survives is one an
    /// installation is told about, rather than one it has to notice by reading logs.
    /// </summary>
    [Fact]
    public async Task A_cave_feature_without_its_subtype_row_is_reported_by_the_integrity_verifier()
    {
        var cave = await SeedCaveAsync($"Pestera Verificata {tag}");

        await using (var clean = factory.Services.CreateAsyncScope())
        {
            // The seeded row must be clean, or the check below proves nothing.
            var none = await clean.ServiceProvider
                .GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
            none.Where(p => p.FeatureId == cave).ShouldBeEmpty();
        }

        var type = await BreakAsync(cave);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var problems = await scope.ServiceProvider
                .GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
            var problem = problems.Where(p => p.FeatureId == cave).ShouldHaveSingleItem();
            problem.Check.ShouldBe("subtype_row_missing");
        }
        finally
        {
            await MendAsync(cave, type);
        }
    }

    // ---- helpers ----

    /// <summary>A public cave owned by this run's editor, written through the aggregate service.</summary>
    private async Task<Guid> SeedCaveAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();

        var feature = await writer.CreateCaveAsync(
            new Feature { Name = name, OwnerUserId = editorId, Visibility = Visibility.Public },
            new Cave { CaveTypeId = caveTypeId },
            parents: []);
        await db.SaveChangesAsync();
        return feature.Id;
    }

    /// <summary>
    /// Removes the subtype row and leaves the feature, returning what it takes to put it back.
    /// Straight to the database on purpose: nothing in the application can produce this.
    /// </summary>
    private async Task<long> BreakAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var type = await db.Caves.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.Id == featureId).Select(c => c.CaveTypeId).SingleAsync();
        await db.Caves.IgnoreQueryFilters().Where(c => c.Id == featureId).ExecuteDeleteAsync();
        return type;
    }

    private async Task MendAsync(Guid featureId, long type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        if (await db.Caves.IgnoreQueryFilters().AnyAsync(c => c.Id == featureId))
        {
            return;
        }

        var cave = new Cave { Id = featureId, CaveTypeId = type };
        db.Caves.Add(cave);

        // The kind is a shadow property on the subtype row, half of the composite foreign key
        // that keeps a cave row on a cave feature. The aggregate write service sets it the same
        // way; a row added without it does not satisfy the check constraint.
        db.Entry(cave).Property("Kind").CurrentValue = FeatureKind.Cave;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The caves this run made, and only those: the suite shares one database, so a listing over
    /// every cave in it is a page whose contents another class decides.
    /// </summary>
    private async Task<JsonElement> ListAsync(int pageSize = 50, int page = 1)
    {
        var response = await editor.GetAsync(
            $"/api/v1/caves?search={tag}&sort=name&pageSize={pageSize}&page={page}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid[] IdsOf(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        factory.Dispose();
    }
}

/// <summary>
/// Everything the application logged during a test. Registered as a sink on the real logging
/// pipeline — the host reads its sinks from the service collection — so a warning asserted here
/// is one the code path under test emitted through the logger the installation actually uses.
/// </summary>
internal sealed class LogCapture : ILogEventSink
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (lines)
            {
                return [.. lines];
            }
        }
    }

    public void Clear()
    {
        lock (lines)
        {
            lines.Clear();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var line = $"{logEvent.Level} {logEvent.RenderMessage()}";
        lock (lines)
        {
            lines.Add(line);
        }
    }
}
