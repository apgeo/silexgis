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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// An account whose uploads wait for a reviewer, at the phone's door: the mark and who may set it,
/// the refusal of a device that cannot be told a row is held, and what a held upload answers and
/// keeps — proposals and nothing written, a resend of the same content keeping nothing new,
/// different content replacing what waited, and a row that cannot be written without a held create
/// recorded as depending on it, in the same upload or a later one.
/// </summary>
public sealed class SyncUploadHoldTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient uploader = null!;
    private HttpClient administrator = null!;
    private Guid uploaderId;
    private Guid set;
    private string marker = null!;

    public SyncUploadHoldTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hold-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"hold-up-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"hold-adm-{marker}@t.local");
        administrator = await AuthHelper.BearerClientAsync(factory, $"hold-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
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
    public async Task The_mark_is_set_by_an_administrator_of_accounts_audited_and_refused_to_anybody_else()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.PutAsJsonAsync($"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // An editor holds no right over accounts at all.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hold-ed-{marker}@t.local");
        using var editor = await AuthHelper.BearerClientAsync(factory, $"hold-ed-{marker}@t.local");
        (await editor.PutAsJsonAsync($"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await administrator.PutAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}/upload-review", new { uploadsNeedReview = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var marked = await administrator.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = true });
        marked.StatusCode.ShouldBe(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());
        (await marked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uploadsNeedReview").GetBoolean().ShouldBeTrue();

        var read = await administrator.GetFromJsonAsync<JsonElement>($"/api/v1/users/{uploaderId}");
        read.GetProperty("uploadsNeedReview").GetBoolean().ShouldBeTrue();

        // Setting it again to what it already is changes nothing and records nothing more.
        (await administrator.PutAsJsonAsync($"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var audits = await db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.UploadReviewMarked && a.EntityId == uploaderId.ToString())
            .ToListAsync();
        audits.Count.ShouldBe(1);
        audits[0].Changes.ShouldNotBeNull().ShouldContain("UploadsNeedReview");
    }

    [Fact]
    public async Task The_administrators_group_holds_the_upload_review_right_at_the_whole_installation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var group = await db.PermissionGroups.AsNoTracking()
            .SingleAsync(g => g.Slug == SeededPermissionGroups.AdministratorsSlug);
        var entries = await db.AccessEntries.AsNoTracking()
            .Where(e => e.PermissionGroupId == group.Id && e.Domain == AccessDomain.UploadReview)
            .ToListAsync();
        entries.Count.ShouldBe(1);
        entries[0].Actions.ShouldBe(AccessActions.Everything);
        entries[0].ScopeKind.ShouldBe(AccessScopeKind.All);
        entries[0].Effect.ShouldBe(AccessEffect.Allow);

        // The editors are not reviewers.
        var editors = await db.PermissionGroups.AsNoTracking().SingleAsync(g => g.Slug == SeededPermissionGroups.EditorsSlug);
        (await db.AccessEntries.AnyAsync(e => e.PermissionGroupId == editors.Id && e.Domain == AccessDomain.UploadReview))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task An_account_not_marked_is_answered_as_ever_with_no_review_member_and_nothing_held()
    {
        var caveId = Guid.CreateVersion7();
        var (raw, answer) = await UploadRawAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "not held") },
        });

        answer.GetProperty("rows")[0].GetProperty("status").GetString().ShouldBe("created");
        raw.ShouldNotContain("reviewId");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == answer.GetProperty("importBatchId").GetGuid());
        batch.ReviewState.ShouldBe(ImportReviewState.NotHeld);
        batch.HeldAt.ShouldBeNull();
        (await db.ImportBatchItems.AnyAsync(i => i.ImportBatchId == batch.Id && i.ReviewDecision != null)).ShouldBeFalse();
        (await db.Features.AnyAsync(f => f.Id == caveId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_marked_account_whose_device_cannot_be_told_a_row_is_held_is_refused_whole_and_nothing_is_written()
    {
        await MarkAsync(true);

        var caveId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();

        // Recorded: a device that declares nothing is the one every build written before review
        // existed is, and this answer is what it has to show its user rather than retry.
        var recorded = await new ContractFixture()
            .Literal(marker, "marker").Name(set, "set").Name(batchId, "batch").Name(caveId, "cave")
            .RecordRefusedPostAsync(
                Path.Combine("16-errors", "client-update-required"),
                uploader,
                $"/api/v1/sync/sets/{set}/upload",
                new { batchId, contractVersion = 1, rows = new[] { Cave(caveId, "refused") } });
        recorded.GetProperty("code").GetString().ShouldBe("sync.client_update_required");

        foreach (var features in new[] { null, new[] { "somethingElse" } })
        {
            var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
            {
                batchId,
                contractVersion = 1,
                clientFeatures = features,
                rows = new[] { Cave(caveId, "refused") },
            });
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, payload);
            JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe("sync.client_update_required");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatches.AnyAsync(b => b.SyncBatchId == batchId)).ShouldBeFalse();
        (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == caveId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_held_upload_answers_held_writes_nothing_anybody_can_read_and_records_the_entrance_depending_on_its_cave()
    {
        // Written before the mark, so the reader below is shown to see this uploader's rows when
        // they exist: what it is not shown afterwards is absent because nothing was written.
        var earlierId = Guid.CreateVersion7();
        await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, rows = new[] { Cave(earlierId, "before") } });
        await MarkAsync(true);

        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var request = new
        {
            batchId,
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "held cave"), Entrance(entranceId, caveId) },
        };
        var answer = await new ContractFixture()
            .Literal(marker, "marker").Name(set, "set").Name(batchId, "batch")
            .Name(caveId, "cave").Name(entranceId, "entrance")
            .RecordPostAsync("20-upload-held", uploader, $"/api/v1/sync/sets/{set}/upload", request);

        answer.GetProperty("written").GetInt32().ShouldBe(0);
        answer.GetProperty("refused").GetInt32().ShouldBe(0);
        var rows = answer.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("status").GetString()).ShouldBe(["held", "held"]);
        rows.Select(r => r.GetProperty("id").GetGuid()).ShouldBe([caveId, entranceId]);
        rows.ShouldAllBe(r => r.GetProperty("revision").ValueKind == JsonValueKind.Null);
        var reviewIds = rows.Select(r => r.GetProperty("reviewId").GetGuid()).ToList();
        reviewIds.Distinct().Count().ShouldBe(2);

        // Not even a full administrator, who reads everything, finds the held rows: they were
        // never written. The same reader finds the uploader's earlier row.
        (await administrator.GetAsync($"/api/v1/features/{earlierId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await administrator.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await administrator.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.IgnoreQueryFilters().CountAsync(f => f.Id == caveId || f.Id == entranceId)).ShouldBe(0);
            (await db.AuditEntries.AnyAsync(a => a.EntityId == caveId.ToString() || a.EntityId == entranceId.ToString()))
                .ShouldBeFalse();

            var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.SyncBatchId == batchId);
            batch.ReviewState.ShouldBe(ImportReviewState.Pending);
            batch.HeldAt.ShouldNotBeNull();
            batch.CreatedCount.ShouldBe(0);

            var lines = await db.ImportBatchItems.AsNoTracking()
                .Where(i => i.ImportBatchId == batch.Id).OrderBy(i => i.Id).ToListAsync();
            lines.Count.ShouldBe(2);
            lines.ShouldAllBe(i => i.ReviewDecision == ImportReviewDecision.Pending
                && i.FeatureId == null && i.ProposalSection == "rows" && i.Proposal != null);
            lines.Select(i => i.ProposalRowId).ShouldBe([caveId, entranceId]);
            lines.Select(i => i.ReviewId!.Value).ShouldBe(reviewIds);
            lines[0].DependsOnItemIds.ShouldBeNull();
            lines[1].DependsOnItemIds.ShouldBe([lines[0].Id]);
        }

        // A resend of the same batch is answered from its record, unchanged.
        var replay = await UploadAsync(request);
        replay.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        replay.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()).ShouldBe(reviewIds);
    }

    [Fact]
    public async Task The_same_content_again_keeps_nothing_new_and_different_content_supersedes_what_waited()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var first = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "waiting"), Entrance(entranceId, caveId) },
        });
        var firstIds = first.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()).ToList();

        // What a phone does with a row whose revision did not move: sends it again, in a new batch.
        var again = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "waiting"), Entrance(entranceId, caveId) },
        });
        again.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ShouldBe(["held", "held"]);
        again.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()).ShouldBe(firstIds);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var repeat = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == again.GetProperty("importBatchId").GetGuid());
            repeat.ReviewState.ShouldBe(ImportReviewState.NotHeld);
            (await db.ImportBatchItems.CountAsync(i => i.ImportBatchId == repeat.Id)).ShouldBe(0);
            (await PendingAsync(db)).Count.ShouldBe(2);
        }

        // The cave renamed on the phone: its proposal replaces the waiting one, and the entrance
        // that waited on the old proposal now waits on the new one.
        var changed = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "renamed") },
        });
        var newCaveReview = changed.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        newCaveReview.ShouldNotBe(firstIds[0]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var old = await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == firstIds[0]);
            old.ReviewDecision.ShouldBe(ImportReviewDecision.Superseded);
            old.DecidedAt.ShouldNotBeNull();

            var waiting = await PendingAsync(db);
            waiting.Select(i => i.ReviewId!.Value).OrderBy(x => x).ShouldBe(new[] { newCaveReview, firstIds[1] }.OrderBy(x => x));
            var cave = waiting.Single(i => i.ReviewId == newCaveReview);
            cave.Proposal.ShouldNotBeNull().ShouldContain("renamed");
            waiting.Single(i => i.ReviewId == firstIds[1]).DependsOnItemIds.ShouldBe([cave.Id]);
            (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == caveId)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task A_row_sent_later_under_a_cave_still_waiting_is_held_on_it_rather_than_refused_for_having_no_cave()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(caveId, "yesterday") },
        });

        var entranceId = Guid.CreateVersion7();
        var later = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Entrance(entranceId, caveId) },
        });
        later.GetProperty("rows")[0].GetProperty("status").GetString().ShouldBe("held", later.ToString());

        // Not held, an entrance under a cave that does not exist is refused: the hold did not
        // invent a cave, it checked against the one waiting.
        await MarkAsync(false);
        var orphan = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { Entrance(Guid.CreateVersion7(), caveId) },
        });
        orphan.GetProperty("rows")[0].GetProperty("status").GetString().ShouldBe("rejected");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var waiting = await PendingAsync(db);
        var cave = waiting.Single(i => i.ProposalRowId == caveId);
        waiting.Single(i => i.ProposalRowId == entranceId).DependsOnItemIds.ShouldBe([cave.Id]);
        (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == caveId || f.Id == entranceId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_held_batch_is_not_undone_through_the_import_history()
    {
        await MarkAsync(true);
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            rows = new[] { Cave(Guid.CreateVersion7(), "not undoable") },
        });
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        var listed = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/import-batches/{batchId}");
        listed.GetProperty("batch").GetProperty("canRevert").GetBoolean().ShouldBeFalse();

        var refused = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        var payload = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe("import_batch.held");
    }

    private async Task<List<ImportBatchItem>> PendingAsync(SilexGisDbContext db) =>
        await (from item in db.ImportBatchItems.AsNoTracking()
               join batch in db.ImportBatches on item.ImportBatchId equals batch.Id
               where batch.ConfirmedByUserId == uploaderId && item.ReviewDecision == ImportReviewDecision.Pending
               select item).ToListAsync();

    private async Task MarkAsync(bool needsReview)
    {
        var response = await administrator.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
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

    private static object Entrance(Guid id, Guid caveId) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name = "Intrare",
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = new[] { 22.51, 45.61 } },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    private async Task<JsonElement> UploadAsync(object request) => (await UploadRawAsync(request)).Answer;

    private async Task<(string Raw, JsonElement Answer)> UploadRawAsync(object request)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", request);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return (raw, JsonDocument.Parse(raw).RootElement.Clone());
    }

    public void Dispose()
    {
        uploader?.Dispose();
        administrator?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
