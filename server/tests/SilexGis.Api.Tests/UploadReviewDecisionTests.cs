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
/// A reviewer deciding what an account under review sent from its phone: accepting writes what the
/// same upload would have written unheld, with the uploader's rights and in a change set of its
/// own; a subset of an edit's fields; what a decision pulls in and cascades to; an edit whose row
/// changed since; and who may read the queue, who may decide, and who may do neither.
/// </summary>
public sealed class UploadReviewDecisionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid reviewerId;
    private Guid set;
    private string marker = null!;

    public UploadReviewDecisionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

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
    public async Task A_held_edit_of_a_row_the_viewer_may_not_place_is_shown_without_its_position()
    {
        // A protected entrance the uploader edits with its true point. The reviewer (a full
        // administrator) sees the point; a reader of the queue without exact view does not.
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

        // The reader may read the row — only placing it is withheld; a reader who may not read
        // it at all is the next test's.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-pos-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-pos-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features, AccessAction.Read);

        var exact = await reviewer.GetStringAsync($"/api/v1/review/batches/{batchId}");
        exact.ShouldContain("22.6111");
        var blind = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        blind.ShouldNotContain("22.6111");
        blind.ShouldNotContain("45.6222");
        JsonDocument.Parse(blind).RootElement.GetProperty("lines")[0].GetProperty("proposal")
            .GetProperty("row").GetProperty("id").GetGuid().ShouldBe(entranceId);
    }

    [Fact]
    public async Task A_held_point_under_a_held_cave_in_a_protected_area_is_shown_only_to_who_may_place_it()
    {
        // A protected area the uploader made earlier; then, held, a new cave under it and an
        // entrance under that cave with its true point. The entrance names only the held cave, so
        // only following the chain up to the area tells whether its point may be shown.
        var areaId = Guid.CreateVersion7();
        await UploadAsync(Held: false, Area(areaId));
        await ProtectAsync(areaId);

        await MarkAsync(true);
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var held = await UploadAsync(
            Held: true, Cave(caveId, "Under the guarded area", areaId), Entrance(entranceId, caveId, at: [22.7333, 45.7444]));
        var batchId = held.GetProperty("importBatchId").GetGuid();

        // A reader of the queue who may read the area but not place anything inside it.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rev-chain-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rev-chain-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features, AccessAction.Read);

        var exact = await reviewer.GetStringAsync($"/api/v1/review/batches/{batchId}");
        exact.ShouldContain("22.7333");
        var blind = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        blind.ShouldNotContain("22.7333");
        blind.ShouldNotContain("45.7444");
        var entrance = JsonDocument.Parse(blind).RootElement.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("rowId").GetGuid() == entranceId);
        entrance.GetProperty("proposal").GetProperty("row").TryGetProperty("geometry", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_held_edit_of_a_row_the_viewer_may_not_read_shows_nothing_of_the_row()
    {
        // An unprotected entrance kept private to its owner. Nothing about protection hides its
        // point; only that the reader may not read the row at all.
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

        var exact = await reviewer.GetStringAsync($"/api/v1/review/batches/{batchId}");
        exact.ShouldContain("22.8111");
        exact.ShouldContain("Intrare ascunsa privata");

        var blind = await reader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        blind.ShouldNotContain("22.8111");
        blind.ShouldNotContain("Intrare ascunsa privata");
        blind.ShouldNotContain(entranceId.ToString());
        blind.ShouldNotContain(caveId.ToString());
        var line = JsonDocument.Parse(blind).RootElement.GetProperty("lines")[0];
        line.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("decision").GetString().ShouldBe("pending");
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
}
