// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A reviewer deciding what an account under review sent from its phone: accepting writes what the
/// same upload would have written unheld, with the uploader's rights and in a change set of its
/// own; a subset of an edit's fields; what a decision pulls in and cascades to; an edit whose row
/// changed since; and who may read the queue, who may decide, and who may do neither.
/// </summary>
public sealed class UploadReviewDecisionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly PauseFirstDecisionCommit pause = new();
    private readonly PauseAtCommit commits = new();
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid reviewerId;
    private Guid set;
    private string marker = null!;

    public UploadReviewDecisionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
                services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(pause, commits)));

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rev-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"rev-up-{marker}@t.local");
        reviewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rev-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"rev-adm-{marker}@t.local");

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
    public async Task Accepting_a_held_upload_writes_what_the_same_upload_writes_unheld()
    {
        // The same content twice: once by the account before it was put under review, once after,
        // accepted whole. What lands must not differ in anything but the identifiers.
        var plainCave = Guid.CreateVersion7();
        var plainEntrance = Guid.CreateVersion7();
        var plain = await UploadAsync(Held: false, Cave(plainCave, "Peștera A"), Entrance(plainEntrance, plainCave));
        var plainBatch = plain.GetProperty("importBatchId").GetGuid();

        await MarkAsync(true);
        var heldCave = Guid.CreateVersion7();
        var heldEntrance = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(heldCave, "Peștera A"), Entrance(heldEntrance, heldCave));
        var heldBatch = held.GetProperty("importBatchId").GetGuid();
        held.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("status").GetString())
            .ShouldAllBe(s => s == "held");

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{heldBatch}/accept", new { note = "ok" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("decided").EnumerateArray().Select(l => l.GetProperty("decision").GetString())
            .ShouldAllBe(d => d == "accepted");
        result.GetProperty("batches")[0].GetProperty("reviewState").GetString().ShouldBe("accepted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var shapes = await db.Features.AsNoTracking()
            .Where(f => new[] { plainCave, plainEntrance, heldCave, heldEntrance }.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id);
        foreach (var (a, b) in new[] { (plainCave, heldCave), (plainEntrance, heldEntrance) })
        {
            var p = shapes[a];
            var h = shapes[b];
            (h.Kind, h.Name, h.FeatureTypeId, h.OwnerUserId, h.Visibility, h.CavingGroupId, h.LocationProtected)
                .ShouldBe((p.Kind, p.Name, p.FeatureTypeId, p.OwnerUserId, p.Visibility, p.CavingGroupId, p.LocationProtected));
            (h.Geom?.Coordinate.X, h.Geom?.Coordinate.Y, h.Geom?.Coordinate.Z)
                .ShouldBe((p.Geom?.Coordinate.X, p.Geom?.Coordinate.Y, p.Geom?.Coordinate.Z));
        }

        (await db.CaveEntrances.AsNoTracking().SingleAsync(e => e.Id == heldEntrance)).CaveFeatureId.ShouldBe(heldCave);

        var batches = await db.ImportBatches.AsNoTracking().Where(b => b.Id == plainBatch || b.Id == heldBatch).ToDictionaryAsync(b => b.Id);
        batches[heldBatch].CreatedCount.ShouldBe(batches[plainBatch].CreatedCount);
        batches[heldBatch].DecidedByUserId.ShouldBe(reviewerId);

        // The held lines became the provenance lines undo works from.
        var lines = await db.ImportBatchItems.AsNoTracking().Where(i => i.ImportBatchId == heldBatch).ToListAsync();
        lines.Select(l => l.FeatureId).OrderBy(x => x).ShouldBe(new Guid?[] { heldCave, heldEntrance }.OrderBy(x => x));
        lines.ShouldAllBe(l => l.ReviewDecision == ImportReviewDecision.Accepted && l.AppliedAt != null);

        // Written in a change set of its own, from the review, filed under the held batch, by the
        // reviewer — and the unheld upload's under the phone.
        var history = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == heldCave.ToString() && a.Action == AuditActions.Created)
            .SingleAsync();
        history.Source.ShouldBe(ChangeSource.ReviewDecision);
        history.ImportBatchId.ShouldBe(heldBatch);
        history.UserId.ShouldBe(reviewerId);
        (await db.AuditEntries.AsNoTracking().SingleAsync(a => a.EntityId == plainCave.ToString() && a.Action == AuditActions.Created))
            .Source.ShouldBe(ChangeSource.DeviceSync);
    }

    [Fact]
    public async Task Accepting_some_fields_of_an_edit_writes_only_those_and_a_row_changed_since_needs_saying_so()
    {
        var caveId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Before"));
        var revision = created.GetProperty("rows")[0].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(Held: true, Edit(caveId, revision, "After", "New description"));
        var reviewId = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

        // A field this line cannot be accepted by is refused before anything is decided.
        var unknown = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { fields = new[] { "kind" } });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unknown)).ShouldBe("review.fields_unsupported");

        var batchId = held.GetProperty("importBatchId").GetGuid();
        (await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}"))
            .GetProperty("lines")[0].GetProperty("changedSince").GetBoolean().ShouldBeFalse();

        // Somebody edits the cave in between: accepting writes over that only when told to.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var cave = await db.Features.SingleAsync(f => f.Id == caveId);
            cave.Description = "Written in between";
            await db.SaveChangesAsync();
        }

        // The detail says so before anybody presses anything.
        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        detail.GetProperty("lines")[0].GetProperty("changedSince").GetBoolean().ShouldBeTrue();

        var stale = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { fields = new[] { "name" } });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync());
        (await CodeAsync(stale)).ShouldBe("review.changed_since");

        var accepted = await reviewer.PostAsJsonAsync(
            $"/api/v1/review/items/{reviewId}/accept", new { fields = new[] { "name" }, overCurrent = true });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var after = await read.Features.AsNoTracking().SingleAsync(f => f.Id == caveId);
        after.Name.ShouldBe("After");
        after.Description.ShouldBe("Written in between");
        (await read.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == reviewId)).AcceptedFields
            .ShouldBe(["name"]);
    }

    [Fact]
    public async Task Accepting_a_place_pulls_in_its_held_cave_and_rejecting_a_cave_rejects_its_place()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(caveId, "Pulled in"), Entrance(entranceId, caveId));
        var entranceReview = held.GetProperty("rows")[1].GetProperty("reviewId").GetGuid();

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{entranceReview}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").GetArrayLength().ShouldBe(2);

        var rejectedCave = Guid.CreateVersion7();
        var rejectedEntrance = Guid.CreateVersion7();
        var second = await UploadAsync(Held: true, Cave(rejectedCave, "Cascaded"), Entrance(rejectedEntrance, rejectedCave));
        var caveReview = second.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{caveReview}/reject", new { note = "Not a cave." });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var rejection = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        rejection.GetProperty("decided").EnumerateArray().Select(l => l.GetProperty("decision").GetString())
            .ShouldBe(["rejected", "rejected"]);
        rejection.GetProperty("batches")[0].GetProperty("reviewState").GetString().ShouldBe("rejected");

        // Rejecting a line twice is refused. (Accepting a turned-down line after all is a change of
        // mind the review allows while its bytes are kept; that is another test's.)
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{caveReview}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{second.GetProperty("importBatchId").GetGuid()}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.IgnoreQueryFilters().CountAsync(f => f.Id == caveId || f.Id == entranceId)).ShouldBe(2);
        (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == rejectedCave || f.Id == rejectedEntrance)).ShouldBeFalse();
        (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == caveReview)).DecisionNote.ShouldBe("Not a cave.");
    }

    [Fact]
    public async Task What_a_reviewer_accepted_is_undone_like_any_upload_and_a_waiting_batch_is_not()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(caveId, "Undone later"), Entrance(entranceId, caveId));
        var batchId = held.GetProperty("importBatchId").GetGuid();

        var waiting = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        waiting.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(waiting)).ShouldBe("import_batch.held");

        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/import-batches/{batchId}");
        listed.GetProperty("batch").GetProperty("canRevert").GetBoolean().ShouldBeTrue();

        var undone = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        undone.StatusCode.ShouldBe(HttpStatusCode.OK, await undone.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.IgnoreQueryFilters().Where(f => f.Id == caveId || f.Id == entranceId)
            .AllAsync(f => f.DeletedAt != null)).ShouldBeTrue();
        (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).RevertedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_batch_whose_every_line_a_later_upload_replaced_is_closed_and_the_latest_is_what_is_accepted()
    {
        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var first = await UploadAsync(Held: true, Cave(caveId, "First word"));
        var second = await UploadAsync(Held: true, Cave(caveId, "Last word"));
        var firstBatch = first.GetProperty("importBatchId").GetGuid();
        var secondBatch = second.GetProperty("importBatchId").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var closed = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == firstBatch);
            closed.ReviewState.ShouldBe(ImportReviewState.Rejected);
            closed.DecidedByUserId.ShouldBeNull();
        }

        var queue = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/batches?pageSize=500");
        var queued = queue.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        queued.ShouldContain(secondBatch);
        queued.ShouldNotContain(firstBatch);

        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{firstBatch}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{secondBatch}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await read.Features.AsNoTracking().SingleAsync(f => f.Id == caveId)).Name.ShouldBe("Last word");
    }

    [Fact]
    public async Task An_uploader_who_lost_the_right_to_write_since_is_answered_failed()
    {
        var caveId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Writable once"));
        var revision = created.GetProperty("rows")[0].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(Held: true, Edit(caveId, revision, "Renamed later", null));
        var reviewId = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

        // The uploader is an editor; an explicit deny takes the write away again.
        await GrantAsync(uploaderId, AccessEffect.Deny, AccessDomain.Features, AccessAction.Write);

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var line = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided")[0];
        line.GetProperty("decision").GetString().ShouldBe("failed");
        line.GetProperty("failureCode").GetString().ShouldBe("sync.row_forbidden");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == caveId)).Name.ShouldBe("Writable once");
    }

    [Fact]
    public async Task The_queue_is_read_with_the_read_right_decided_with_the_write_right_and_closed_to_anybody_else()
    {
        await MarkAsync(true);
        var held = await UploadAsync(Held: true, Cave(Guid.CreateVersion7(), "Queued"));
        var batchId = held.GetProperty("importBatchId").GetGuid();
        var reviewId = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/review/batches")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer with the read right only: sees the queue and the batch, cannot decide.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-read-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-read-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        var queue = await reader.GetFromJsonAsync<JsonElement>("/api/v1/review/batches?pageSize=500");
        queue.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldContain(batchId);
        (await reader.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A viewer with no review right at all: no queue, and the batch is not there.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-none-{marker}@t.local");
        using var stranger = await AuthHelper.BearerClientAsync(factory, $"rev-none-{marker}@t.local");
        (await stranger.GetAsync("/api/v1/review/batches")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The uploader reads their own batch and cannot decide it.
        var mine = await uploader.GetFromJsonAsync<JsonElement>("/api/v1/review/mine");
        mine.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldContain(batchId);

        // The queue names who uploaded by a display label, never by the account's address.
        var queued = queue.GetProperty("items").EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId);
        var uploaderName = queued.GetProperty("uploaderName").GetString();
        uploaderName.ShouldNotBeNullOrWhiteSpace();
        uploaderName.ShouldNotContain("@");
        var own = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        own.GetProperty("lines")[0].GetProperty("decision").GetString().ShouldBe("pending");
        (await uploader.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Validation.
        (await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/reject", new { note = new string('x', 2001) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was decided by any of it.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == reviewId)).ReviewDecision
            .ShouldBe(ImportReviewDecision.Pending);
    }

    [Fact]
    public async Task A_reviewer_sees_a_held_edit_of_a_protected_row_at_its_exact_position_and_outside_review_still_does_not()
    {
        // A protected entrance the uploader edits with its true point. A reviewer who may read the
        // row but not place it is shown the point in review — they have to see what they judge —
        // and reading the same row anywhere else is still snapped.
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Guarded"), Entrance(entranceId, caveId));
        var revision = created.GetProperty("rows")[1].GetProperty("revision").GetDateTimeOffset();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.LocationProtected, true)
                    .SetProperty(f => f.IsProtectedEffective, true));
            revision = (await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId)).UpdatedAt;
        }

        await MarkAsync(true);
        var moved = Entrance(entranceId, caveId, revision, [22.6111, 45.6222]);
        var held = await UploadAsync(Held: true, moved);
        var batchId = held.GetProperty("importBatchId").GetGuid();

        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-pos-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-pos-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features, AccessAction.Read);

        // Fixture proof: outside review the reader is shown the entrance snapped, not where it is.
        await ShouldBeSnappedAsync(reader, entranceId, 22.51);

        var seen = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        seen.ShouldContain("22.6111");
        seen.ShouldContain("45.6222");
        var row = JsonDocument.Parse(seen).RootElement.GetProperty("lines")[0].GetProperty("proposal").GetProperty("row");
        row.GetProperty("id").GetGuid().ShouldBe(entranceId);
        row.GetProperty("altitude").GetDouble().ShouldBe(640.0);

        // Seeing it in review lifts nothing anywhere else.
        await ShouldBeSnappedAsync(reader, entranceId, 22.51);

        // An account without the review right is still refused the queue and is not shown the batch.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-pos-no-{marker}@t.local");
        using var outsider = await AuthHelper.BearerClientAsync(factory, $"rev-pos-no-{marker}@t.local");
        (await outsider.GetAsync("/api/v1/review/batches")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_held_point_under_a_held_cave_in_a_protected_area_is_shown_whole_to_a_reviewer_who_may_not_place_it()
    {
        // A protected area the uploader made earlier; then, held, a new cave under it and an
        // entrance under that cave with its true point. The entrance names only the held cave, so
        // only following the chain up to the area tells whether its point may be shown; a
        // reviewer is shown it whole wherever the chain leads.
        var areaId = Guid.CreateVersion7();
        await UploadAsync(Held: false, Area(areaId));
        await ProtectAsync(areaId);

        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var held = await UploadAsync(
            Held: true, Cave(caveId, "Under the guarded area", areaId), Entrance(entranceId, caveId, at: [22.7333, 45.7444]));
        var batchId = held.GetProperty("importBatchId").GetGuid();

        // A reviewer who may read the area but not place anything inside it.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-chain-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-chain-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features, AccessAction.Read);

        var seen = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        seen.ShouldContain("22.7333");
        seen.ShouldContain("45.7444");
        var entrance = JsonDocument.Parse(seen).RootElement.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("rowId").GetGuid() == entranceId);
        entrance.GetProperty("proposal").GetProperty("row").TryGetProperty("geometry", out _).ShouldBeTrue();

        // Outside review the same account is shown the area snapped.
        await ShouldBeSnappedAsync(reader, areaId, 22.70);
    }

    [Fact]
    public async Task A_reviewer_sees_a_held_edit_of_a_row_they_may_not_read_and_still_may_not_read_it_outside_review()
    {
        // An unprotected entrance kept private to its owner. Nothing about protection hides its
        // point; only that the reviewer may not read the row at all.
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Private only"), Entrance(entranceId, caveId));
        var revision = created.GetProperty("rows")[1].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(Held: true, Entrance(entranceId, caveId, revision, [22.8111, 45.8222], "Intrare ascunsa privata"));
        var batchId = held.GetProperty("importBatchId").GetGuid();

        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-unread-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-unread-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);

        // Fixture proof: the reviewer may not read the entrance or its cave anywhere else.
        (await reader.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var seen = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        seen.ShouldContain("22.8111");
        seen.ShouldContain("Intrare ascunsa privata");
        var line = JsonDocument.Parse(seen).RootElement.GetProperty("lines")[0];
        line.GetProperty("rowId").GetGuid().ShouldBe(entranceId);
        line.GetProperty("proposal").GetProperty("row").GetProperty("parentId").GetGuid().ShouldBe(caveId);

        (await reader.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Read through the shared feature route, the row is placed approximately and not at
    /// <paramref name="trueLongitude"/>.
    /// </summary>
    private static async Task ShouldBeSnappedAsync(HttpClient client, Guid featureId, double trueLongitude)
    {
        var envelope = await client.GetFromJsonAsync<JsonElement>($"/api/v1/features/{featureId}");
        var feature = envelope.GetProperty("feature");
        feature.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object)
        {
            Math.Abs(geometry.GetProperty("coordinates")[0].GetDouble() - trueLongitude).ShouldBeGreaterThan(1e-9);
        }
    }

    private async Task ProtectAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => f.Id == featureId || f.AncestorIds.Contains(featureId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.LocationProtected, f => f.Id == featureId)
                .SetProperty(f => f.IsProtectedEffective, true));
    }

    private async Task GrantAsync(Guid userId, AccessEffect effect, AccessDomain domain, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Two decisions on different lines of one batch, the first held at its commit until the second
    /// has run as far as it can: the batch ends where its lines say it stands, and its counters count
    /// both. Each decision on its own reads the other line as still waiting, so without the batch
    /// row taken first both would leave the batch waiting — and a batch left waiting can never be
    /// undone — and the second would write back a count that misses the first.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decisions_on_different_lines_of_one_batch_settle_it_and_count_both(bool secondAccepts)
    {
        await MarkAsync(true);
        var held = await UploadAsync(
            Held: true, Cave(Guid.CreateVersion7(), "Left"), Cave(Guid.CreateVersion7(), "Right"));
        var batchId = held.GetProperty("importBatchId").GetGuid();
        var firstLine = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        var secondLine = held.GetProperty("rows")[1].GetProperty("reviewId").GetGuid();

        pause.Arm();
        var first = reviewer.PostAsJsonAsync($"/api/v1/review/items/{firstLine}/accept", new { }, TestContext.Current.CancellationToken);
        (await Task.WhenAny(pause.Paused, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)))
            .ShouldBe(pause.Paused, "the first decision never reached its commit");
        var second = reviewer.PostAsJsonAsync(
            $"/api/v1/review/items/{secondLine}/{(secondAccepts ? "accept" : "reject")}", new { }, TestContext.Current.CancellationToken);

        // The second decision either finishes (nothing holds it back) or waits on the first; either
        // way the first is let go once the second has had its chance.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        pause.Release();
        var firstAnswer = await first;
        firstAnswer.StatusCode.ShouldBe(HttpStatusCode.OK, await firstAnswer.Content.ReadAsStringAsync());
        var secondAnswer = await second;
        secondAnswer.StatusCode.ShouldBe(HttpStatusCode.OK, await secondAnswer.Content.ReadAsStringAsync());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
            batch.ReviewState.ShouldBe(secondAccepts ? ImportReviewState.Accepted : ImportReviewState.PartlyAccepted);
            batch.CreatedCount.ShouldBe(secondAccepts ? 2 : 1);
        }

        var undone = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        undone.StatusCode.ShouldBe(HttpStatusCode.OK, await undone.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Once a batch is undone, a line of it that was turned down can no longer be accepted after
    /// all: what it would write could never be undone through its batch, which already says it was.
    /// The refusal has its own code, and nothing is written.
    /// </summary>
    [Fact]
    public async Task A_rejected_line_of_an_undone_batch_can_no_longer_be_reopened()
    {
        await MarkAsync(true);
        var keptId = Guid.CreateVersion7();
        var turnedDownId = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(keptId, "Accepted, then undone"), Cave(turnedDownId, "Turned down"));
        var batchId = held.GetProperty("importBatchId").GetGuid();
        var keptLine = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        var turnedDownLine = held.GetProperty("rows")[1].GetProperty("reviewId").GetGuid();

        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{keptLine}/accept", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{turnedDownLine}/reject", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var undone = await uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        undone.StatusCode.ShouldBe(HttpStatusCode.OK, await undone.Content.ReadAsStringAsync());

        var reopened = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{turnedDownLine}/accept", new { });
        reopened.StatusCode.ShouldBe(HttpStatusCode.Conflict, await reopened.Content.ReadAsStringAsync());
        (await CodeAsync(reopened)).ShouldBe("review.batch_reverted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == turnedDownId)).ShouldBeFalse();
        (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == turnedDownLine))
            .ReviewDecision.ShouldBe(ImportReviewDecision.Rejected);
    }

    /// <summary>
    /// Two requests to undo the same upload at once — a retry after a slow answer, or the uploader
    /// and an administrator together — take it back once: the second, started while the first is
    /// held at its commit and so past the route's own look at the batch, waits for the first and
    /// then finds it undone.
    /// </summary>
    [Fact]
    public async Task Two_undos_of_one_batch_at_once_take_it_back_once()
    {
        await MarkAsync(false);
        var caveId = Guid.CreateVersion7();
        var landed = await UploadAsync(Held: false, Cave(caveId, "Undone twice"));
        var batchId = landed.GetProperty("importBatchId").GetGuid();

        commits.Arm(UndoStampsItsLines);
        var first = uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { }, TestContext.Current.CancellationToken);
        await commits.WaitPausedAsync();
        var second = reviewer.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { }, TestContext.Current.CancellationToken);
        await LockWaits.WaitAsync(factory, second);
        commits.Release();

        var firstAnswer = await first;
        firstAnswer.StatusCode.ShouldBe(HttpStatusCode.OK, await firstAnswer.Content.ReadAsStringAsync());
        var secondAnswer = await second;
        secondAnswer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await secondAnswer.Content.ReadAsStringAsync());
        (await CodeAsync(secondAnswer)).ShouldBe("import_batch.already_reverted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var key = caveId.ToString();
        (await db.AuditEntries.AsNoTracking().CountAsync(a => a.EntityId == key && a.Action == AuditActions.Deleted))
            .ShouldBe(1, "the cave the upload created is deleted once, by the first undo");
    }

    /// <summary>
    /// A turned-down line reopened while an undo of its upload is on its way to commit waits for the
    /// undo, and is then refused because the upload is undone — by the decision's own look under the
    /// batch, since the route's look before it found the batch still standing.
    /// </summary>
    [Fact]
    public async Task A_line_reopened_while_its_upload_is_being_undone_waits_and_is_refused()
    {
        await MarkAsync(true);
        var turnedDownId = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(Guid.CreateVersion7(), "Accepted, then undone"), Cave(turnedDownId, "Turned down"));
        var batchId = held.GetProperty("importBatchId").GetGuid();
        var keptLine = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        var turnedDownLine = held.GetProperty("rows")[1].GetProperty("reviewId").GetGuid();
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{keptLine}/accept", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{turnedDownLine}/reject", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);

        commits.Arm(UndoStampsItsLines);
        var undo = uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { }, TestContext.Current.CancellationToken);
        await commits.WaitPausedAsync();
        var reopen = reviewer.PostAsJsonAsync($"/api/v1/review/items/{turnedDownLine}/accept", new { }, TestContext.Current.CancellationToken);
        await LockWaits.WaitAsync(factory, reopen);
        commits.Release();

        (await undo).StatusCode.ShouldBe(HttpStatusCode.OK);
        var reopened = await reopen;
        reopened.StatusCode.ShouldBe(HttpStatusCode.Conflict, await reopened.Content.ReadAsStringAsync());
        (await CodeAsync(reopened)).ShouldBe("review.batch_reverted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == turnedDownId)).ShouldBeFalse();
        (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == turnedDownLine))
            .ReviewDecision.ShouldBe(ImportReviewDecision.Rejected);
    }

    /// <summary>
    /// An undo that reaches a row a phone has just written, and not yet committed, waits for the
    /// phone and compares what the phone committed: the later edit wins, and the undo says so,
    /// instead of putting the old value back over it.
    /// </summary>
    [Fact]
    public async Task An_undo_reaching_a_row_another_writer_holds_compares_what_that_writer_committed()
    {
        await MarkAsync(false);
        var caveId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Before"));
        var revision = created.GetProperty("rows")[0].GetProperty("revision").GetDateTimeOffset();
        var edited = await UploadAsync(Held: false, Edit(caveId, revision, "After", "Written by the upload"));
        var batchId = edited.GetProperty("importBatchId").GetGuid();

        var answer = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE features SET description = 'Written by a phone since', updated_at = clock_timestamp() WHERE id = '{caveId}'",
            () => uploader.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { }));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cave = await db.Features.AsNoTracking().SingleAsync(f => f.Id == caveId);
        cave.Description.ShouldBe("Written by a phone since");
        cave.Name.ShouldBe("Before");
    }

    /// <summary>
    /// A held line whose kept proposal this build can no longer read is answered as failed with its
    /// own code, and the rest of the batch is decided as usual, instead of the whole acceptance
    /// failing.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("[1]")]
    public async Task A_held_line_whose_proposal_no_longer_reads_fails_alone(string unreadable)
    {
        await MarkAsync(true);
        var keptId = Guid.CreateVersion7();
        var held = await UploadAsync(Held: true, Cave(keptId, "Readable"), Cave(Guid.CreateVersion7(), "Unreadable"));
        var batchId = held.GetProperty("importBatchId").GetGuid();
        var unreadableLine = held.GetProperty("rows")[1].GetProperty("reviewId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var line = await db.ImportBatchItems.SingleAsync(i => i.ReviewId == unreadableLine);
            line.Proposal = unreadable;
            await db.SaveChangesAsync();
        }

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var failed = await read.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == unreadableLine);
        failed.ReviewDecision.ShouldBe(ImportReviewDecision.Failed);
        failed.ReviewFailureCode.ShouldBe("review.proposal_unreadable");
        (await read.Features.AsNoTracking().AnyAsync(f => f.Id == keptId)).ShouldBeTrue();
    }

    /// <summary>
    /// An acceptance whose look at the row finds it as the phone read it, and whose write then
    /// waits for another upload that changes the row and commits, answers that the row changed
    /// since — as it would have had the other upload committed a moment earlier — and the line
    /// still waits, so the reviewer can choose to write over the current row.
    /// </summary>
    [Fact]
    public async Task A_row_changed_while_the_acceptance_waits_on_it_is_answered_changed_since()
    {
        await MarkAsync(false);
        var caveId = Guid.CreateVersion7();
        var created = await UploadAsync(Held: false, Cave(caveId, "Before"));
        var revision = created.GetProperty("rows")[0].GetProperty("revision").GetDateTimeOffset();
        await MarkAsync(true);
        var held = await UploadAsync(Held: true, Edit(caveId, revision, "After", "Held description"));
        var reviewId = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

        var answer = await AnotherWriterFirst.SendAsync(
            factory,
            $"UPDATE features SET description = 'Written by a phone since', updated_at = clock_timestamp() WHERE id = '{caveId}'",
            () => reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/accept", new { }));
        answer.StatusCode.ShouldBe(HttpStatusCode.Conflict, await answer.Content.ReadAsStringAsync());
        (await CodeAsync(answer)).ShouldBe("review.changed_since");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ReviewId == reviewId))
            .ReviewDecision.ShouldBe(ImportReviewDecision.Pending);
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == caveId)).Description.ShouldBe("Written by a phone since");
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> UploadAsync(bool Held, params object[] rows)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = Held ? new[] { "held" } : Array.Empty<string>(),
            rows,
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    // The undo's stamping of the lines it took back: the last statement before it settles the batch.
    private static bool UndoStampsItsLines(string sql) =>
        sql.Contains("UPDATE import_batch_items", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("undone_at", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private static object Cave(Guid id, string name, Guid? areaId = null) => new
    {
        id,
        kind = "cave",
        parentId = areaId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object Area(Guid id) => new
    {
        id,
        kind = "generic",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name = "Zona de test",
        featureTypeCode = "surface_area",
        isMain = false,
        geometry = new { type = "Point", coordinates = new[] { 22.70, 45.70 } },
    };

    private static object Edit(Guid id, DateTimeOffset revision, string name, string? description) => new
    {
        id,
        kind = "cave",
        baseRevision = revision,
        deleted = false,
        name,
        description,
        isMain = false,
    };

    private static object Entrance(
        Guid id, Guid caveId, DateTimeOffset? revision = null, double[]? at = null, string name = "Intrare") => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = revision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = at ?? [22.51, 45.61] },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    public void Dispose()
    {
        uploader?.Dispose();
        reviewer?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Holds the first review decision made after <see cref="Arm"/> at its commit — after it has
    /// claimed its lines and restated its batch — until <see cref="Release"/>. Bounded, so a test
    /// that never releases cannot hang the suite.
    /// </summary>
    private sealed class PauseFirstDecisionCommit : DbCommandInterceptor, IDbTransactionInterceptor
    {
        private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool armed;
        private DbTransaction? held;

        public Task Paused => paused.Task;

        public void Arm() => armed = true;

        public void Release() => released.TrySetResult();

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (armed
                && command.Transaction is { } transaction
                && command.CommandText.Contains("UPDATE import_batch_items", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("review_decision", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.CompareExchange(ref held, transaction, null);
            }

            return ValueTask.FromResult(result);
        }

        public async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (armed && ReferenceEquals(transaction, Volatile.Read(ref held)))
            {
                armed = false;
                paused.TrySetResult();
                await Task.WhenAny(released.Task, Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None));
            }

            return result;
        }
    }
}
