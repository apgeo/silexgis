// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Optimistic concurrency over the feature aggregate. The version token is the FEATURE
/// row — one ETag covers a cave's own columns, its subtype attributes and the derived
/// mirror an entrance change refreshes — so a stale editor of any part is refused. Single
/// GETs carry the ETag, writes honor If-Match with 412 on staleness, and a fresh ETag is
/// issued after every successful update.
/// </summary>
public sealed class ConcurrencyTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private Guid ownerCaverId;
    private long caveTypeId;
    private long entranceTypeId;

    public ConcurrencyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cc-own-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            ownerCaverId = await db.Cavers.Where(c => c.UserId == ownerUserId).Select(c => c.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cc-own-{suffix}@t.local");
    }

    /// <summary>
    /// Every versioned table names a table that exists. The allow-list is an enum beside a
    /// dictionary and the compiler checks neither against the other, so a table registered in
    /// one and forgotten in the other would first be noticed as a failure to serve an ETag on a
    /// live endpoint. Asking for a row that is not there answers "no version" from a real
    /// database, which is only possible if both halves and the schema agree.
    /// </summary>
    [Fact]
    public async Task Every_versioned_table_resolves_against_the_schema()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        foreach (var table in Enum.GetValues<VersionedTable>())
        {
            var version = await ConcurrencySql.VersionAsync(db, table, Guid.CreateVersion7(), default);
            version.ShouldBeNull($"{table} should resolve and report no row.");
        }
    }

    [Fact]
    public async Task Etag_and_if_match_guard_against_lost_updates()
    {
        var caveId = await CreateCaveAsync("Concurrent Cave v1");

        var get = await owner.GetAsync($"/api/v1/caves/{caveId}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = get.Headers.ETag!.Tag;
        etag.ShouldNotBeNullOrWhiteSpace();
        // The token is the feature row's version, not something the cave subtype owns.
        etag.ShouldBe(await FeatureETagAsync(caveId));

        // Update WITH the fresh ETag → accepted, and a new ETag is issued.
        var okResponse = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v2"), etag);
        okResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await okResponse.Content.ReadAsStringAsync());
        var newEtag = okResponse.Headers.ETag!.Tag;
        newEtag.ShouldNotBe(etag); // xmin advanced with the update

        // Replaying the OLD ETag now → 412 with the stable code; nothing is written.
        var staleResponse = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v3 (lost)"), etag);
        staleResponse.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await staleResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}"))
            .GetProperty("name").GetString().ShouldBe("Concurrent Cave v2");

        // A PUT without the header is refused with 428, so an edit of a loaded resource can
        // never silently overwrite.
        var noHeader = await owner.PutAsJsonAsync($"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v3"));
        noHeader.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await noHeader.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.if_match_required");

        // Stale delete → 412; delete with the current ETag → gone.
        var currentEtag = await FeatureETagAsync(caveId);
        currentEtag.ShouldBe(newEtag);
        (await DeleteWithIfMatchAsync($"/api/v1/caves/{caveId}", etag))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await DeleteWithIfMatchAsync($"/api/v1/caves/{caveId}", currentEtag))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // DELETE stays lenient — list/map deletes carry no loaded version — so no header deletes.
        var second = await CreateCaveAsync("Lenient Delete Cave");
        (await owner.DeleteAsync($"/api/v1/caves/{second}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A tag a reverse proxy rewrote still names the version it was issued for.
    /// </summary>
    /// <remarks>
    /// The deployment this project ships puts nginx in front of the API with compression on for
    /// JSON, and nginx appends the coding it applied to the entity tag: the browser is handed
    /// <c>"1031-gzip"</c> for a row whose version is 1031. It stores that and replays it on the
    /// next write, which is not compressed and so arrives unmangled — and a literal comparison
    /// then reports a row that never moved as changed, refusing every edit and every delete made
    /// through a real installation while passing every test made against the server directly.
    /// The same happens to a tag a cache downgraded to weak. Both forms are asserted here because
    /// neither is reachable from a test that talks to Kestrel, which is the whole reason the
    /// defect shipped.
    /// </remarks>
    [Fact]
    public async Task If_match_accepts_a_tag_a_compressing_proxy_rewrote()
    {
        var caveId = await CreateCaveAsync("Proxied Tag Cave v1");
        var etag = await FeatureETagAsync(caveId);

        var gzipped = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Proxied Tag Cave v2"), InsideQuotes(etag, "-gzip"));
        gzipped.StatusCode.ShouldBe(HttpStatusCode.OK, await gzipped.Content.ReadAsStringAsync());

        var weak = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Proxied Tag Cave v3"), "W/" + await FeatureETagAsync(caveId));
        weak.StatusCode.ShouldBe(HttpStatusCode.OK, await weak.Content.ReadAsStringAsync());

        // The suffix is stripped, not ignored: a stale version wearing one is still stale.
        (await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Proxied Tag Cave v4 (lost)"), InsideQuotes(etag, "-gzip")))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // And the delete the report came in about: the page loaded the trip over a compressed
        // response, so the only tag it has to send is the rewritten one.
        (await DeleteWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", InsideQuotes(await FeatureETagAsync(caveId), "-gzip")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A write whose version was replaced while it was being checked is refused, not merged.
    /// </summary>
    /// <remarks>
    /// The other writer changes the cave's row and holds its transaction open; the request under
    /// test carries the version both of them started from. A check that only reads the version
    /// sees the old one (a read waits for nobody), lets the request through, and the request's own
    /// update then lands on top of the other writer's: each keeps the columns it changed and
    /// neither is told. The check has to hold the row from the comparison to the commit, so the
    /// request waits for the other writer, finds the version it carries is no longer the row's,
    /// and is refused with the code a stale replay gets.
    /// </remarks>
    [Fact]
    public async Task A_cave_save_raced_by_another_writer_is_refused_and_writes_nothing()
    {
        var caveId = await CreateCaveAsync("Raced Cave v1");
        var etag = await FeatureETagAsync(caveId);

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE features SET name = 'Raced Cave by the other writer' WHERE id = '{caveId}'",
            () => owner.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", CaveBody("Raced Cave v2 (lost)"), etag));

        raced.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await raced.Content.ReadAsStringAsync());
        (await raced.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}"))
            .GetProperty("name").GetString().ShouldBe("Raced Cave by the other writer");

        // The version the other writer left is the one that goes through.
        var current = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Raced Cave v3"), await FeatureETagAsync(caveId));
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Two saves sent at once with the same version: exactly one lands, the other is refused, and
    /// the stored cave is the winner's.
    /// </summary>
    [Fact]
    public async Task Two_cave_saves_holding_one_version_land_exactly_one()
    {
        var caveId = await CreateCaveAsync("Twice Saved Cave v1");
        var etag = await FeatureETagAsync(caveId);

        var answers = await Task.WhenAll(
            owner.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", CaveBody("Twice Saved Cave by A"), etag),
            owner.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", CaveBody("Twice Saved Cave by B"), etag));

        answers.Select(a => a.StatusCode).OrderBy(s => (int)s).ToArray()
            .ShouldBe([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed]);
        var winner = answers.Single(a => a.StatusCode == HttpStatusCode.OK);
        var stored = await owner.GetAsync($"/api/v1/caves/{caveId}");
        (await stored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString()
            .ShouldBe((await winner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
        winner.Headers.ETag!.Tag.ShouldBe(stored.Headers.ETag!.Tag);
    }

    /// <summary>
    /// The trip's full update, which a trip's own update-time token does not protect: the other
    /// writer moves a column without touching that token, so only a held version catches it.
    /// </summary>
    [Fact]
    public async Task A_trip_save_raced_by_another_writer_is_refused_and_writes_nothing()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody("Raced trip v1"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var path = created.Headers.Location!.OriginalString;
        var tripId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE trip_logs SET title = 'Raced trip by the other writer' WHERE id = '{tripId}'",
            () => owner.PutWithIfMatchAsync(path, TripBody("Raced trip v2 (lost)"), etag));

        raced.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await raced.Content.ReadAsStringAsync());
        (await owner.GetFromJsonAsync<JsonElement>(path))
            .GetProperty("title").GetString().ShouldBe("Raced trip by the other writer");
    }

    /// <summary>
    /// Every write that checks a version answers the one it produced, including the edits that
    /// leave the header optional, so a caller can save twice without a read in between.
    /// </summary>
    [Fact]
    public async Task An_entrance_edit_answers_the_version_it_produced()
    {
        var caveId = await CreateCaveAsync("Answered Entrance Cave");
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/caves/{caveId}/entrances", EntranceBody("Main", 25.82, 45.92, surveyedAt: null));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var entranceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var first = await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.82, 45.92, surveyedAt: new DateOnly(2026, 5, 1)),
            await FeatureETagAsync(entranceId));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Headers.ETag!.Tag.ShouldBe(await FeatureETagAsync(entranceId));
        // Filed under the feature, whose read hands out the same version: the entrance's own path
        // has no read, so a version filed there would never be replaced and would go stale at the
        // first change made anywhere else.
        first.Content.Headers.ContentLocation!.OriginalString.ShouldBe($"/api/v1/features/{entranceId}");

        var second = await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.82, 45.92, surveyedAt: new DateOnly(2026, 5, 2)),
            first.Headers.ETag!.Tag);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        second.Headers.ETag!.Tag.ShouldBe(await FeatureETagAsync(entranceId));
    }

    /// <summary>
    /// The side routes of a feature — its links and its parents — answer the feature's version, and
    /// file it under the feature, because their own reads hand out none.
    /// </summary>
    /// <remarks>
    /// A version filed under a path whose read never supplies one is replayed on every later write
    /// of that path and never replaced. The first change made through another route then leaves
    /// every save there refused as stale, and reading the page again does not help. Filed under
    /// the feature, it is what the feature's read hands out and what its next write needs.
    /// </remarks>
    [Fact]
    public async Task A_feature_side_route_files_its_version_under_the_feature()
    {
        var caveId = await CreateCaveAsync("Side Route Cave");

        var links = await owner.PutAsJsonAsync($"/api/v1/features/{caveId}/links", new { links = Array.Empty<object>() });
        links.StatusCode.ShouldBe(HttpStatusCode.OK, await links.Content.ReadAsStringAsync());
        links.Content.Headers.ContentLocation!.OriginalString.ShouldBe($"/api/v1/features/{caveId}");
        links.Headers.ETag!.Tag.ShouldBe(await FeatureETagAsync(caveId));

        var parents = await owner.PutAsJsonAsync($"/api/v1/features/{caveId}/parents", new { parents = Array.Empty<object>() });
        parents.StatusCode.ShouldBe(HttpStatusCode.OK, await parents.Content.ReadAsStringAsync());
        parents.Content.Headers.ContentLocation!.OriginalString.ShouldBe($"/api/v1/features/{caveId}");
        parents.Headers.ETag!.Tag.ShouldBe(await FeatureETagAsync(caveId));
    }

    /// <summary>
    /// The generic feature's full update, raced the same way as the cave's: the other writer's
    /// change survives and the save holding the version both started from is refused.
    /// </summary>
    [Fact]
    public async Task A_feature_save_raced_by_another_writer_is_refused_and_writes_nothing()
    {
        long sinkholeTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        }

        var created = await owner.PostAsJsonAsync("/api/v1/features", GenericBody("Raced Sinkhole v1", sinkholeTypeId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var featureId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var etag = await FeatureETagAsync(featureId);

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE features SET description = 'described by the other writer' WHERE id = '{featureId}'",
            () => owner.PutWithIfMatchAsync(
                $"/api/v1/features/{featureId}", GenericBody("Raced Sinkhole v2 (lost)", sinkholeTypeId), etag));

        raced.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await raced.Content.ReadAsStringAsync());
        (await raced.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId);
            stored.Name.ShouldBe("Raced Sinkhole v1");
            stored.Description.ShouldBe("described by the other writer");
        }
    }

    /// <summary>
    /// A last-write-wins save — no precondition sent — raced by another writer is applied to the
    /// row as the other writer left it, and answers what is stored.
    /// </summary>
    /// <remarks>
    /// The request loads the entrance before its row is held, so the copy it changes can be older
    /// than the row. Setting a column back to the value that copy holds then compares equal and is
    /// not written: the other writer's value stays, while the answer shows the requested one beside
    /// the version of a row that does not hold it.
    /// </remarks>
    [Fact]
    public async Task A_save_without_a_precondition_raced_by_another_writer_answers_what_it_stored()
    {
        var caveId = await CreateCaveAsync("Last Write Cave");
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/caves/{caveId}/entrances", EntranceBody("Original name", 25.82, 45.92, surveyedAt: null));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var entranceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var raced = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE features SET name = 'Renamed by the other writer' WHERE id = '{entranceId}'",
            () => owner.PutAsJsonAsync(
                $"/api/v1/cave-entrances/{entranceId}", EntranceBody("Original name", 25.82, 45.92, surveyedAt: null)));

        raced.StatusCode.ShouldBe(HttpStatusCode.OK, await raced.Content.ReadAsStringAsync());
        (await raced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().ShouldBe("Original name");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AsNoTracking().Where(f => f.Id == entranceId).Select(f => f.Name).SingleAsync())
                .ShouldBe("Original name", "the save was applied to the row as the other writer left it");
        }

        raced.Headers.ETag!.Tag.ShouldBe(await FeatureETagAsync(entranceId));
    }

    /// <summary>
    /// A move of a cave's main entrance and a flip of the cave's protection, made at the same
    /// moment, both answer: the first to reach the cave lands, the other is told the cave changed.
    /// </summary>
    /// <remarks>
    /// Each write holds the row it names from its check to its commit, and each also writes the
    /// other's row: the entrance move refreshes the cave's point, and the protection flip restamps
    /// every entrance below the cave. Held in opposite orders, the two wait on each other, and the
    /// database breaks the knot by aborting one with a deadlock, which reaches its caller as a
    /// server error. Both saves are parked here until each holds its own row, by another
    /// transaction that keeps the history table from being written; when it lets go, both reach
    /// for the other's row at once.
    /// </remarks>
    [Fact]
    public async Task A_main_entrance_move_and_its_caves_protection_flip_at_once_do_not_deadlock()
    {
        var caveId = await CreateCaveAsync("Lock Order Cave");
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/caves/{caveId}/entrances", EntranceBody("Main", 25.82, 45.92, surveyedAt: null));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var entranceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var caveTag = await FeatureETagAsync(caveId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // The waits are watched from a connection of their own: inside the parking transaction the
        // database answers every read of its activity from the snapshot its first read took.
        await using var watching = factory.Services.CreateAsyncScope();
        var watcher = watching.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        HttpResponseMessage move, flip;
        await using (var parking = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE audit_log IN SHARE MODE");

            var moving = owner.PutAsJsonAsync(
                $"/api/v1/cave-entrances/{entranceId}", EntranceBody("Main", 25.83, 45.93, surveyedAt: null));
            await LockWaitersAsync(watcher, 1, moving);
            var flipping = owner.PutWithIfMatchAsync(
                $"/api/v1/caves/{caveId}",
                new { name = "Lock Order Cave", caveTypeId, visibility = "private", locationProtected = true,
                      explorationStatus = "unknown", isShowCave = false },
                caveTag);
            await LockWaitersAsync(watcher, 2, flipping);

            await parking.CommitAsync();
            move = await moving;
            flip = await flipping;
        }

        move.StatusCode.ShouldBe(HttpStatusCode.OK, await move.Content.ReadAsStringAsync());
        flip.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await flip.Content.ReadAsStringAsync());
        (await flip.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
    }

    /// <summary>Waits until the database reports <paramref name="count"/> statements waiting on a lock.</summary>
    private static async Task LockWaitersAsync(SilexGisDbContext db, int count, Task<HttpResponseMessage> pending)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (pending.IsCompleted)
            {
                var early = await (Task<HttpResponseMessage>)pending;
                throw new ShouldAssertException(
                    $"the request answered {(int)early.StatusCode} without waiting: {await early.Content.ReadAsStringAsync()}");
            }

            var waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND state = 'active' AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting >= count)
            {
                return;
            }

            await Task.Delay(50);
        }

        var activity = await db.Database.SqlQueryRaw<string>(
            "SELECT coalesce(state, '') || ' | ' || coalesce(wait_event_type, '') || ':' || coalesce(wait_event, '') "
            + "|| ' | ' || left(coalesce(query, ''), 120) AS \"Value\" FROM pg_stat_activity "
            + "WHERE pid <> pg_backend_pid() AND backend_type = 'client backend' AND state <> 'idle'").ToListAsync();
        throw new ShouldAssertException(
            $"{count} statements never waited on a lock; the database was doing: " + string.Join(" || ", activity));
    }

    private static object GenericBody(string name, long featureTypeId) => new
    {
        kind = "generic",
        name,
        featureTypeId,
        geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
        visibility = "private",
    };

    /// <summary>A tag with a suffix appended inside its quotes, the way nginx rewrites one.</summary>
    private static string InsideQuotes(string etag, string suffix) =>
        etag[..^1] + suffix + '"';

    [Fact]
    public async Task Entrance_edits_version_on_the_feature_row_and_bump_the_cave_aggregate()
    {
        var caveId = await CreateCaveAsync("Aggregate Token Cave");
        var caveEtagBefore = await FeatureETagAsync(caveId);

        // Adding an entrance refreshes the cave's derived mirror (count + representative
        // point), so the cave's own token advances and a stale editor is refused.
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/caves/{caveId}/entrances", EntranceBody("Main", 25.81, 45.91, surveyedAt: null));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var entranceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var caveEtagAfter = await FeatureETagAsync(caveId);
        caveEtagAfter.ShouldNotBe(caveEtagBefore);
        (await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Aggregate Token Cave v2"), caveEtagBefore))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // An entrance is versioned on its own feature row, and a subtype-only edit still
        // advances that token — otherwise an attribute change would slip past a concurrent
        // editor unnoticed.
        var entranceEtag = await FeatureETagAsync(entranceId);
        var edit = await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.81, 45.91, surveyedAt: new DateOnly(2026, 5, 17)),
            entranceEtag);
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        (await edit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("surveyedAt").GetString().ShouldBe("2026-05-17");

        var entranceEtagAfter = await FeatureETagAsync(entranceId);
        entranceEtagAfter.ShouldNotBe(entranceEtag);

        (await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.81, 45.91, surveyedAt: new DateOnly(2026, 6, 1)),
            entranceEtag))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // Entrance edits keep the lenient contract: no header is still last-write-wins.
        (await owner.PutAsJsonAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main entrance", 25.81, 45.91, surveyedAt: null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The row's current version as an ETag value — the token every feature kind shares.</summary>
    private async Task<string> FeatureETagAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var version = await ConcurrencySql.VersionAsync(db, VersionedTable.Features, featureId, CancellationToken.None);
        version.ShouldNotBeNull();
        return $"\"{version}\"";
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A write on a trip answers the version it produced, so the next write carries it without a
    /// read in between.
    /// </summary>
    /// <remarks>
    /// Only a read used to hand out a version, so every write left its caller one version behind
    /// the row they had just written, and a second save a moment later was refused as a conflict
    /// nobody could see; the client answered by not calling a save finished until it had read the
    /// trip back, seconds with the button spinning. Each write here carries forward the tag the
    /// previous write answered — never one a read supplied — and a read after each confirms it is
    /// the row's version and not a number of the write's own. An action posted beside the trip
    /// says whose version it answers in Content-Location, because its own path is not where the
    /// trip's next write will look.
    /// </remarks>
    [Fact]
    public async Task A_trip_write_answers_the_version_it_produced()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", TripBody("Versioned trip v1"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var path = created.Headers.Location!.OriginalString;
        path.ShouldBe($"/api/v1/trip-logs/{(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}");
        var afterCreate = created.Headers.ETag!.Tag;
        afterCreate.ShouldBe(await TripETagAsync(path));

        // A full update carrying the tag the creation answered: the request path names the trip,
        // so nothing else has to.
        var updated = await owner.PutWithIfMatchAsync(path, TripBody("Versioned trip v2"), afterCreate);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        updated.Content.Headers.ContentLocation.ShouldBeNull();
        var afterUpdate = updated.Headers.ETag!.Tag;
        afterUpdate.ShouldNotBe(afterCreate);
        afterUpdate.ShouldBe(await TripETagAsync(path));

        // An action posted beside the trip: the trip's version, and whose it is.
        var moved = await owner.PostWithIfMatchAsync($"{path}/state", new { state = "proposed" }, afterUpdate);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        moved.Content.Headers.ContentLocation!.OriginalString.ShouldBe(path);
        var afterMove = moved.Headers.ETag!.Tag;
        afterMove.ShouldNotBe(afterUpdate);
        afterMove.ShouldBe(await TripETagAsync(path));

        var armed = await owner.PostWithIfMatchAsync(
            $"{path}/callout",
            new { expectedReturnAt = "2026-07-01T18:00:00Z", calloutAlarmAt = "2026-07-01T20:00:00Z" },
            afterMove);
        armed.StatusCode.ShouldBe(HttpStatusCode.OK, await armed.Content.ReadAsStringAsync());
        armed.Content.Headers.ContentLocation!.OriginalString.ShouldBe(path);
        var afterArming = armed.Headers.ETag!.Tag;
        afterArming.ShouldNotBe(afterMove);
        afterArming.ShouldBe(await TripETagAsync(path));

        // Standing down carries no precondition in, and the version out all the same.
        var stood = await owner.PostAsync($"{path}/callout/stand-down", null);
        stood.StatusCode.ShouldBe(HttpStatusCode.OK, await stood.Content.ReadAsStringAsync());
        stood.Content.Headers.ContentLocation!.OriginalString.ShouldBe(path);
        var afterStandingDown = stood.Headers.ETag!.Tag;
        afterStandingDown.ShouldNotBe(afterArming);
        afterStandingDown.ShouldBe(await TripETagAsync(path));

        // And what an action answered is what the next full update needs: the tag from before it
        // is stale, the one it handed over goes through.
        var stale = await owner.PutWithIfMatchAsync(path, TripBody("Versioned trip v3 (lost)"), afterArming);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var current = await owner.PutWithIfMatchAsync(path, TripBody("Versioned trip v3"), afterStandingDown);
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());

        // The plan door is the same create path and answers the same way.
        var planned = await owner.PostAsJsonAsync("/api/v1/trip-logs/plans", TripBody("Versioned plan"));
        planned.StatusCode.ShouldBe(HttpStatusCode.Created, await planned.Content.ReadAsStringAsync());
        planned.Headers.ETag!.Tag.ShouldBe(await TripETagAsync(planned.Headers.Location!.OriginalString));
    }

    private async Task<string> TripETagAsync(string path)
    {
        var read = await owner.GetAsync(path);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        return read.Headers.ETag!.Tag;
    }

    /// <summary>
    /// A trip the owner is named on, so that the owner is somebody the trip concerns and may say
    /// its party is out.
    /// </summary>
    private object TripBody(string title) => new
    {
        title,
        tripDate = "2026-07-01",
        caveIds = Array.Empty<Guid>(),
        participants = new[] { new { caverId = ownerCaverId } },
    };

    private async Task<HttpResponseMessage> DeleteWithIfMatchAsync(string url, string ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await owner.SendAsync(request);
    }

    private object CaveBody(string name) => new
    {
        name,
        caveTypeId,
        visibility = "private",
        locationProtected = false,
        explorationStatus = "unknown",
        isShowCave = false,
    };

    private object EntranceBody(string? name, double lon, double lat, DateOnly? surveyedAt) => new
    {
        name,
        entranceTypeId,
        isMain = true,
        geom = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude = (decimal?)null,
        description = (string?)null,
        positionQuality = "gps",
        surveyedAt,
    };

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner.Dispose();
        factory.Dispose();
    }
}
