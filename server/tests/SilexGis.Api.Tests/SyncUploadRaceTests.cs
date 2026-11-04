// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Two phones pushing an edit of the same row from the same revision at the same time, raced for
/// real: two uploads in flight against one database, with the first held open half way through
/// its batch while the second arrives.
/// </summary>
/// <remarks>
/// The arbitration is a comparison of the revision a phone read against the revision the row has.
/// Sent one after the other, every implementation of it passes: the second upload reads the
/// first's committed revision and refuses itself. The case it exists for is the overlap — the
/// first upload has written the row but not yet committed (it is still working through the rest
/// of its batch) when the second one reads it — and only a test that holds the first upload
/// inside that window can tell an arbitration that holds from one that merely looks right.
/// </remarks>
public sealed class SyncUploadRaceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient first = null!;
    private HttpClient second = null!;
    private Guid firstSet;
    private Guid secondSet;
    private string marker = null!;

    public SyncUploadRaceTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];

        // Two accounts, two phones, two sets: the evening at base camp, not one phone retrying.
        // Both are editors, so each may write the other's rows and the only thing that can refuse
        // the loser is the revision.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"race-a-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"race-b-{marker}@t.local");
        first = await AuthHelper.BearerClientAsync(factory, $"race-a-{marker}@t.local");
        second = await AuthHelper.BearerClientAsync(factory, $"race-b-{marker}@t.local");
        firstSet = await CreateSetAsync(first);
        secondSet = await CreateSetAsync(second);
    }

    [Fact]
    public async Task Two_overlapping_edits_of_one_row_from_one_revision_land_once_and_the_other_is_told_of_the_conflict()
    {
        var contestedId = Guid.CreateVersion7();
        var stallId = Guid.CreateVersion7();
        var created = await UploadAsync(first, firstSet, Guid.CreateVersion7(),
            Cave(contestedId, $"Contested {marker}"), Cave(stallId, $"Stall {marker}"));
        var contestedRevision = Row(created, contestedId).GetProperty("revision").GetDateTimeOffset();
        var stallRevision = Row(created, stallId).GetProperty("revision").GetDateTimeOffset();

        // Another connection holds the second row of the first upload's batch. The first upload
        // writes the contested row, then parks on this one with its own write of the contested row
        // uncommitted — the window a long batch (photos to adopt, documents to describe) keeps open
        // for as long as it runs.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var stall = await db.Database.BeginTransactionAsync();
        (await db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM features WHERE id = {stallId} FOR UPDATE").ToListAsync())
            .Count.ShouldBe(1, "the stall row must exist and be locked");

        var firstUpload = PostAsync(first, firstSet,
            Cave(contestedId, "The first phone's text", baseRevision: contestedRevision),
            Cave(stallId, "Renamed by the first phone", baseRevision: stallRevision));
        await WaitForLockWaitersAsync(db, 1, firstUpload);

        // The second phone read the same revision and sends its own edit while the first upload's
        // write of the row is still uncommitted.
        var secondUpload = PostAsync(second, secondSet,
            Cave(contestedId, "The second phone's text", baseRevision: contestedRevision));
        await WaitForLockWaitersAsync(db, 2, secondUpload);

        await stall.CommitAsync();
        var firstAnswer = await AnswerOf(await firstUpload);
        var secondAnswer = await AnswerOf(await secondUpload);

        // The first upload got to the row first, so it is the one that lands.
        Row(firstAnswer, contestedId).GetProperty("status").GetString().ShouldBe("updated");
        Row(firstAnswer, stallId).GetProperty("status").GetString().ShouldBe("updated");

        // The second phone is told what the protocol tells a phone that edited a row which moved
        // on under it, and handed the revision that stands now — not answered "updated" over a
        // write it never saw.
        var lost = Row(secondAnswer, contestedId);
        lost.GetProperty("status").GetString().ShouldBe("conflict");
        lost.GetProperty("code").GetString().ShouldBe("sync.conflict");
        var standing = Row(firstAnswer, contestedId).GetProperty("revision").GetDateTimeOffset();
        lost.GetProperty("revision").GetDateTimeOffset().ShouldBe(standing);
        secondAnswer.GetProperty("written").GetInt32().ShouldBe(0);
        secondAnswer.GetProperty("refused").GetInt32().ShouldBe(1);

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.Features.AsNoTracking().SingleAsync(f => f.Id == contestedId)).Name
            .ShouldBe("The first phone's text");
    }

    [Fact]
    public async Task Two_overlapping_edits_of_one_trip_recording_land_once_and_the_other_is_told_of_the_conflict()
    {
        // Every section that edits a row against a revision has its own check, so the window is
        // raced for one more of them: a recorded trip, whose rows are a table of their own.
        var caveId = Guid.CreateVersion7();
        var contestedId = Guid.CreateVersion7();
        var stallId = Guid.CreateVersion7();
        var created = await AnswerOf(await PostTripsAsync(first, firstSet, [Cave(caveId, $"Trip cave {marker}")],
            Recording(contestedId, caveId, "Contested trip"), Recording(stallId, caveId, "Stall trip")));
        var contestedRevision = Result(created, "tripRecordings", contestedId).GetProperty("revision").GetDateTimeOffset();
        var stallRevision = Result(created, "tripRecordings", stallId).GetProperty("revision").GetDateTimeOffset();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var stall = await db.Database.BeginTransactionAsync();
        (await db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM trip_recordings WHERE id = {stallId} FOR UPDATE").ToListAsync())
            .Count.ShouldBe(1, "the stall row must exist and be locked");

        var firstUpload = PostTripsAsync(first, firstSet, [],
            Recording(contestedId, caveId, "The first phone's trip", contestedRevision),
            Recording(stallId, caveId, "Renamed by the first phone", stallRevision));
        await WaitForLockWaitersAsync(db, 1, firstUpload);

        var secondUpload = PostTripsAsync(second, secondSet, [],
            Recording(contestedId, caveId, "The second phone's trip", contestedRevision));
        await WaitForLockWaitersAsync(db, 2, secondUpload);

        await stall.CommitAsync();
        var firstAnswer = await AnswerOf(await firstUpload);
        var secondAnswer = await AnswerOf(await secondUpload);

        Result(firstAnswer, "tripRecordings", contestedId).GetProperty("status").GetString().ShouldBe("updated");
        var lost = Result(secondAnswer, "tripRecordings", contestedId);
        lost.GetProperty("status").GetString().ShouldBe("conflict");
        lost.GetProperty("revision").GetDateTimeOffset()
            .ShouldBe(Result(firstAnswer, "tripRecordings", contestedId).GetProperty("revision").GetDateTimeOffset());

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.TripRecordings.AsNoTracking().IgnoreQueryFilters().SingleAsync(r => r.Id == contestedId)).Title
            .ShouldBe("The first phone's trip");
    }

    [Fact]
    public async Task The_same_batch_sent_again_while_the_first_copy_is_still_applying_is_answered_with_the_first_copys_answer()
    {
        // A phone that timed out and retried with the same batch identifier while the first
        // attempt was still being applied: the first copy is held open on the stall row, the
        // second arrives, and must neither apply the batch a second time nor fail.
        var stallId = Guid.CreateVersion7();
        var stallRevision = Row(
            await UploadAsync(first, firstSet, Guid.CreateVersion7(), Cave(stallId, $"Stall {marker}")),
            stallId).GetProperty("revision").GetDateTimeOffset();
        var newId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        object[] rows = [Cave(newId, $"New {marker}"), Cave(stallId, "Renamed by the first copy", baseRevision: stallRevision)];

        await using var stall = await RowStall.HoldAsync(factory, "features", stallId);
        var firstCopy = PostBatchAsync(first, firstSet, batchId, rows);
        await stall.WaitForWaitersAsync(1, firstCopy);

        // The second copy claims the same batch identifier and waits on the first copy's claim.
        var secondCopy = PostBatchAsync(first, firstSet, batchId, rows);
        await stall.WaitForWaitersAsync(2, secondCopy);

        await stall.ReleaseAsync();
        var applied = await AnswerOf(await firstCopy);
        var again = await secondCopy;
        var againPayload = await again.Content.ReadAsStringAsync();

        applied.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        Row(applied, newId).GetProperty("status").GetString().ShouldBe("created");
        if (again.StatusCode == HttpStatusCode.OK)
        {
            var replay = JsonDocument.Parse(againPayload).RootElement;
            replay.GetProperty("replayed").GetBoolean().ShouldBeTrue(againPayload);
            replay.GetProperty("importBatchId").GetGuid().ShouldBe(applied.GetProperty("importBatchId").GetGuid());
            Row(replay, newId).GetProperty("status").GetString().ShouldBe("created");
        }
        else
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict, againPayload);
            JsonDocument.Parse(againPayload).RootElement.GetProperty("code").GetString().ShouldBe("sync.batch_conflict");
        }

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.ImportBatches.AsNoTracking().CountAsync(b => b.SyncBatchId == batchId)).ShouldBe(1);
        (await read.Features.AsNoTracking().CountAsync(f => f.Id == newId)).ShouldBe(1);
        (await read.Features.AsNoTracking().SingleAsync(f => f.Id == stallId)).Name.ShouldBe("Renamed by the first copy");
    }

    [Fact]
    public async Task The_same_batch_of_several_rows_sent_twice_at_once_lands_once_and_both_copies_get_the_same_answer()
    {
        for (var round = 0; round < 3; round++)
        {
            var batchId = Guid.CreateVersion7();
            Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
            object[] rows = [.. ids.Select((id, i) => Cave(id, $"Twice {marker} {round}.{i}"))];

            var answers = await Task.WhenAll(
                PostBatchAsync(first, firstSet, batchId, rows),
                PostBatchAsync(first, firstSet, batchId, rows));
            var bodies = new List<JsonElement>();
            foreach (var answer in answers)
            {
                bodies.Add(await AnswerOf(answer));
            }

            bodies[0].GetProperty("importBatchId").GetGuid().ShouldBe(bodies[1].GetProperty("importBatchId").GetGuid());
            bodies.Count(b => b.GetProperty("replayed").GetBoolean()).ShouldBeLessThanOrEqualTo(1);
            foreach (var body in bodies)
            {
                foreach (var id in ids)
                {
                    Row(body, id).GetProperty("status").GetString().ShouldBe("created");
                }
            }

            await using var check = factory.Services.CreateAsyncScope();
            var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await read.ImportBatches.AsNoTracking().CountAsync(b => b.SyncBatchId == batchId)).ShouldBe(1);
            (await read.Features.AsNoTracking().CountAsync(f => ids.Contains(f.Id))).ShouldBe(ids.Length);
        }
    }

    [Fact]
    public async Task The_same_new_row_in_two_batches_at_once_is_a_conflict_to_retry_and_the_retry_finds_it_here()
    {
        // The rows of a batch that timed out, resent under a new batch identifier while the first
        // attempt is still applying: the second insert of the phone's identifier waits for the
        // first and then finds it taken.
        var stallId = Guid.CreateVersion7();
        var stallRevision = Row(
            await UploadAsync(first, firstSet, Guid.CreateVersion7(), Cave(stallId, $"Stall {marker}")),
            stallId).GetProperty("revision").GetDateTimeOffset();
        var newId = Guid.CreateVersion7();

        await using var stall = await RowStall.HoldAsync(factory, "features", stallId);
        var firstUpload = PostBatchAsync(first, firstSet, Guid.CreateVersion7(),
            [Cave(newId, $"New {marker}"), Cave(stallId, "Renamed by the first upload", baseRevision: stallRevision)]);
        await stall.WaitForWaitersAsync(1, firstUpload);

        var resentBatch = Guid.CreateVersion7();
        var resent = PostBatchAsync(first, firstSet, resentBatch, [Cave(newId, $"New {marker}")]);
        await stall.WaitForWaitersAsync(2, resent);

        await stall.ReleaseAsync();
        Row(await AnswerOf(await firstUpload), newId).GetProperty("status").GetString().ShouldBe("created");

        // Not a server failure: the phone is told to send it again, and nothing of it was kept.
        var collided = await resent;
        var payload = await collided.Content.ReadAsStringAsync();
        collided.StatusCode.ShouldBe(HttpStatusCode.Conflict, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe("sync.batch_conflict");

        var retried = await AnswerOf(await PostBatchAsync(first, firstSet, resentBatch, [Cave(newId, $"New {marker}")]));
        Row(retried, newId).GetProperty("status").GetString().ShouldBe("unchanged");
        retried.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.Features.AsNoTracking().CountAsync(f => f.Id == newId)).ShouldBe(1);
    }

    private static Task<HttpResponseMessage> PostBatchAsync(HttpClient client, Guid syncSet, Guid batchId, object[] rows) =>
        client.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId,
            contractVersion = 1,
            rows,
        });

    /// <summary>
    /// Waits until the database reports <paramref name="count"/> statements of this database
    /// parked on a lock, so each upload is known to be inside the window before the next step.
    /// An upload that answered instead was never held, and nothing was raced.
    /// </summary>
    private static async Task WaitForLockWaitersAsync(SilexGisDbContext db, int count, Task<HttpResponseMessage> pending)
    {
        var waiting = 0L;
        for (var attempt = 0; attempt < 400 && waiting < count; attempt++)
        {
            pending.IsCompleted.ShouldBeFalse(
                "the upload answered without waiting on the row another writer holds, so nothing was raced");
            // The activity view is read once per transaction and then answered from that copy,
            // and this connection is inside the transaction that holds the stall row: without
            // dropping the copy every poll after the first would repeat the first.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
                + "AND pid <> pg_backend_pid()").SingleAsync();
            if (waiting < count)
            {
                await Task.Delay(50);
            }
        }

        if (waiting < count)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            var states = await db.Database.SqlQueryRaw<string>(
                "SELECT coalesce(wait_event_type, '-') || '/' || coalesce(wait_event, '-') || '/' || coalesce(state, '-') "
                + "|| ': ' || left(query, 160) AS \"Value\" FROM pg_stat_activity "
                + "WHERE datname = current_database() AND pid <> pg_backend_pid()").ToListAsync();
            waiting.ShouldBe(count, "each upload in the race must be parked on a row lock before the next step; "
                + string.Join(" | ", states));
        }
    }

    private static async Task<JsonElement> AnswerOf(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid syncSet, params object[] rows) =>
        client.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows,
        });

    private static Task<HttpResponseMessage> PostTripsAsync(
        HttpClient client, Guid syncSet, object[] rows, params object[] tripRecordings) =>
        client.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows,
            tripRecordings,
        });

    private static JsonElement Result(JsonElement answer, string section, Guid id) =>
        answer.GetProperty(section).EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == id);

    private static object Recording(Guid id, Guid caveId, string title, DateTimeOffset? baseRevision = null) => new
    {
        id,
        baseRevision,
        caveId,
        title,
        description = "Invented for the test",
        log = "entered; turned back at the sump",
        startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
        createdByDeviceUserId = Guid.CreateVersion7(),
    };

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, Guid syncSet, Guid batchId, params object[] rows)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId,
            contractVersion = 1,
            rows,
        });
        return await AnswerOf(response);
    }

    private static JsonElement Row(JsonElement answer, Guid id) =>
        answer.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == id);

    private static object Cave(Guid id, string name, DateTimeOffset? baseRevision = null) => new
    {
        id,
        kind = "cave",
        baseRevision,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private async Task<Guid> CreateSetAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            cavingGroupId = (Guid?)null,
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public void Dispose()
    {
        first?.Dispose();
        second?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
