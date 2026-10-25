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
/// The upload as a request of several sections rather than a list of rows: what counts as an
/// empty upload and as too large a one, the behaviours a device says it understands, and the
/// record a resend is answered from — including a record written before the request had sections.
/// </summary>
/// <remarks>
/// The installation's item ceiling is set low here so the ceiling can be crossed by a handful of
/// rows; every refusal is asserted beside an upload of the same shape that was accepted.
/// </remarks>
public sealed class SyncUploadSectionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int Ceiling = 3;

    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private Guid set;
    private string marker = null!;

    public SyncUploadSectionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Sync:UploadRowsMax"] = Ceiling.ToString() });

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sec-own-{marker}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sec-own-{marker}@t.local");

        var response = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task An_upload_with_no_item_in_any_section_is_refused_and_one_with_a_row_is_applied()
    {
        // No array at all, and an empty one: both are an upload of nothing, and both are refused
        // before anything is recorded.
        var absent = await owner.PostAsJsonAsync(
            $"/api/v1/sync/sets/{set}/upload", new { batchId = Guid.CreateVersion7(), contractVersion = 1 });
        await ShouldBeRefusedAsync(absent, HttpStatusCode.BadRequest, "validation.failed");

        var empty = await owner.PostAsJsonAsync(
            $"/api/v1/sync/sets/{set}/upload",
            new { batchId = Guid.CreateVersion7(), contractVersion = 1, rows = Array.Empty<object>() });
        await ShouldBeRefusedAsync(empty, HttpStatusCode.BadRequest, "validation.failed");

        var caveId = Guid.CreateVersion7();
        var answer = await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, rows = new[] { Cave(caveId, "one") } });
        answer.GetProperty("written").GetInt32().ShouldBe(1);
        answer.GetProperty("rows")[0].GetProperty("status").GetString().ShouldBe("created");
    }

    [Fact]
    public async Task The_item_ceiling_bounds_the_whole_upload_and_an_upload_over_it_writes_nothing()
    {
        var overBatch = Guid.CreateVersion7();
        var over = Enumerable.Range(0, Ceiling + 1).Select(_ => Guid.CreateVersion7()).ToList();
        var refused = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = overBatch,
            contractVersion = 1,
            rows = over.Select((id, i) => Cave(id, $"over {i}")).ToArray(),
        });
        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "sync.batch_too_large");

        var at = Enumerable.Range(0, Ceiling).Select(_ => Guid.CreateVersion7()).ToList();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = at.Select((id, i) => Cave(id, $"at {i}")).ToArray(),
        });
        answer.GetProperty("written").GetInt32().ShouldBe(Ceiling);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatches.IgnoreQueryFilters().CountAsync(b => b.SyncBatchId == overBatch)).ShouldBe(0);
        (await db.Features.IgnoreQueryFilters().CountAsync(f => over.Contains(f.Id))).ShouldBe(0);
        (await db.Features.IgnoreQueryFilters().CountAsync(f => at.Contains(f.Id))).ShouldBe(Ceiling);
    }

    [Fact]
    public async Task The_behaviours_a_device_names_are_kept_on_its_batch_once_each_and_a_device_naming_none_records_none()
    {
        var named = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held", "somethingLater", "held" },
            rows = new[] { Cave(Guid.CreateVersion7(), "named") },
        });
        var silent = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { Cave(Guid.CreateVersion7(), "silent") },
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var namedOptions = await OptionsOfAsync(db, named.GetProperty("importBatchId").GetGuid());
            namedOptions.GetProperty("syncSetId").GetGuid().ShouldBe(set);
            namedOptions.GetProperty("clientFeatures").EnumerateArray().Select(e => e.GetString())
                .ShouldBe(["held", "somethingLater"]);

            var silentOptions = await OptionsOfAsync(db, silent.GetProperty("importBatchId").GetGuid());
            silentOptions.GetProperty("syncSetId").GetGuid().ShouldBe(set);
            silentOptions.TryGetProperty("clientFeatures", out _).ShouldBeFalse();
        }

        // A list longer than any device has reason to send is refused rather than kept.
        var tooMany = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = Enumerable.Range(0, 17).Select(i => $"f{i}").ToArray(),
            rows = new[] { Cave(Guid.CreateVersion7(), "too many") },
        });
        await ShouldBeRefusedAsync(tooMany, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task A_batch_is_recorded_per_section_and_one_recorded_as_a_bare_list_of_rows_is_still_answered_on_a_resend()
    {
        var recent = Guid.CreateVersion7();
        var recentCave = Guid.CreateVersion7();
        var older = Guid.CreateVersion7();
        var olderCave = Guid.CreateVersion7();
        var recentFirst = await UploadAsync(new { batchId = recent, contractVersion = 1, rows = new[] { Cave(recentCave, "recent") } });
        var olderFirst = await UploadAsync(new { batchId = older, contractVersion = 1, rows = new[] { Cave(olderCave, "older") } });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var recorded = await db.ImportBatches.AsNoTracking()
                .Where(b => b.SyncBatchId == recent).Select(b => b.SyncResult).SingleAsync();
            using (var record = JsonDocument.Parse(recorded!))
            {
                record.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
                record.RootElement.GetProperty("rows")[0].GetProperty("id").GetGuid().ShouldBe(recentCave);
            }

            // What every upload recorded before the request had sections: the row answers alone,
            // as a bare array. A phone may still resend such a batch.
            var olderRecord = await db.ImportBatches.AsNoTracking()
                .Where(b => b.SyncBatchId == older).Select(b => b.SyncResult).SingleAsync();
            using var parsed = JsonDocument.Parse(olderRecord!);
            var bare = parsed.RootElement.GetProperty("rows").GetRawText();
            (await db.ImportBatches.Where(b => b.SyncBatchId == older)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncResult, bare))).ShouldBe(1);
        }

        foreach (var (batchId, caveId, first) in new[] { (recent, recentCave, recentFirst), (older, olderCave, olderFirst) })
        {
            var again = await UploadAsync(new { batchId, contractVersion = 1, rows = new[] { Cave(caveId, "changed") } });
            again.GetProperty("replayed").GetBoolean().ShouldBeTrue();
            again.GetProperty("written").GetInt32().ShouldBe(1);
            var row = again.GetProperty("rows")[0];
            row.GetProperty("id").GetGuid().ShouldBe(caveId);
            row.GetProperty("status").GetString().ShouldBe("created");
            row.GetProperty("revision").GetDateTimeOffset()
                .ShouldBe(first.GetProperty("rows")[0].GetProperty("revision").GetDateTimeOffset());
        }
    }

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private async Task<JsonElement> UploadAsync(object request)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code, payload);
    }

    private static async Task<JsonElement> OptionsOfAsync(SilexGisDbContext db, Guid importBatchId)
    {
        var options = await db.ImportBatches.AsNoTracking()
            .Where(b => b.Id == importBatchId).Select(b => b.Options).SingleAsync();
        return JsonDocument.Parse(options!).RootElement.Clone();
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
