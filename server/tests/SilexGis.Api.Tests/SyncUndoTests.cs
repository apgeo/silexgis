// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Taking a phone upload back, truly: what it created goes, what it deleted returns, and what it
/// edited is put back field by field — unless somebody has changed the field since. Played against
/// a real database through the same revert route a file import uses.
/// </summary>
/// <remarks>
/// Every "left alone" here is asserted beside something the same undo did put back, so an undo
/// that had stopped writing anything cannot pass for one that respects a newer edit.
/// </remarks>
public sealed class SyncUndoTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;
    private Guid readerId;
    private long caveTypeId;
    private long entranceTypeId;
    private string marker = null!;

    public SyncUndoTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"su-own-{marker}@t.local");

        // A Viewer that owns none of the rows, deliberately: a row's owner always sees it exactly,
        // and the seeded Editors group reads past visibility, so either would make the protection
        // test below pass for a reason it does not name.
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"su-read-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"su-own-{marker}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"su-read-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Undoing_an_upload_takes_back_what_it_created_edited_and_deleted_and_a_device_reads_it_back()
    {
        var cave = await CreateCaveAsync($"Undo cave {marker}");
        var edited = await CreatePlaceAsync(cave, $"Before {marker}", 25.10, 45.10);
        var removed = await CreatePlaceAsync(cave, $"Removed {marker}", 25.20, 45.20);
        var set = await CreateSetAsync(owner, [cave]);

        var made = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(made, cave, $"Made {marker}", 25.30, 45.30),
            Place(edited, cave, $"After {marker}", 25.11, 45.11, await RevisionAsync(edited), description: "From the phone",
                clientUpdatedAt: new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero)),
            Removal(removed, await RevisionAsync(removed)));
        answer.GetProperty("written").GetInt32().ShouldBe(3);
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        // Where a device that has read everything stands just after the upload.
        var cursor = (await DownloadAsync(owner, set)).GetProperty("nextCursor").GetString();

        var undo = await RevertAsync(owner, batchId, new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var result = await undo.Content.ReadFromJsonAsync<JsonElement>();
        var lines = Lines(result);
        lines[made].GetProperty("change").GetString().ShouldBe("created");
        lines[edited].GetProperty("change").GetString().ShouldBe("updated");
        lines[removed].GetProperty("change").GetString().ShouldBe("deleted");
        lines.Values.ShouldAllBe(l => l.GetProperty("outcome").GetString() == "undone");
        lines[edited].GetProperty("applied").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["Name", "Description", "Geom", "ClientUpdatedAt"], ignoreOrder: true);

        // Every line taken back, so the batch is: it says so, and may not be taken back twice.
        result.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.String);
        result.GetProperty("batch").GetProperty("canRevert").GetBoolean().ShouldBeFalse();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
                .Where(f => f.Id == made || f.Id == edited || f.Id == removed)
                .ToDictionaryAsync(f => f.Id);
            rows[made].DeletedAt.ShouldNotBeNull();
            rows[removed].DeletedAt.ShouldBeNull();
            rows[edited].Name.ShouldBe($"Before {marker}");
            rows[edited].Description.ShouldBeNull();

            // The phone's own edit time goes back too: devices are handed it, and an undone row
            // that kept it would claim an edit nobody's copy holds any more.
            rows[edited].ClientUpdatedAt.ShouldBeNull();
            ((Point)rows[edited].Geom!).X.ShouldBe(25.10, 1e-9);

            // The undo is a change set of its own, filed under the batch it took back.
            var undoRows = await db.AuditEntries.AsNoTracking()
                .Where(a => a.ImportBatchId == batchId && a.Source == ChangeSource.Undo)
                .ToListAsync();
            undoRows.Select(a => a.EntityId).ShouldContain(edited.ToString());
            undoRows.Select(a => a.EntityId).ShouldContain(removed.ToString());
            undoRows.Select(a => a.EntityId).ShouldContain(made.ToString());
            undoRows.Select(a => a.ChangeSetId).Distinct().Count().ShouldBe(1);
        }

        // A device reading on from where it stood is handed the undone state by the ordinary
        // cursor: the edited row as it was, the deleted one back, the created one as gone.
        var after = await DownloadAsync(owner, set, cursor);
        var features = after.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid());
        features[edited].GetProperty("name").GetString().ShouldBe($"Before {marker}");
        features.ShouldContainKey(removed);
        after.GetProperty("tombstones").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid()).ShouldContain(made);
    }

    [Fact]
    public async Task A_field_changed_after_the_upload_is_left_and_reported_and_force_puts_it_back()
    {
        var cave = await CreateCaveAsync($"Superseded cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Original {marker}", 25.40, 45.40);
        var set = await CreateSetAsync(owner, [cave]);

        var first = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Phone one {marker}", 25.40, 45.40, await RevisionAsync(place), description: "One"));
        var batchId = first.GetProperty("importBatchId").GetGuid();

        // A second device renames it afterwards; the description it leaves as the first one wrote it.
        _ = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Phone two {marker}", 25.40, 45.40, await RevisionAsync(place)));

        var undo = await RevertAsync(owner, batchId, new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var line = Lines(await undo.Content.ReadFromJsonAsync<JsonElement>())[place];
        line.GetProperty("outcome").GetString().ShouldBe("partly");
        line.GetProperty("applied").EnumerateArray().Select(f => f.GetString()).ShouldBe(["Description"]);
        var skipped = line.GetProperty("skipped").EnumerateArray().Single();
        skipped.GetProperty("field").GetString().ShouldBe("Name");
        skipped.GetProperty("reason").GetString().ShouldBe("superseded");

        var row = await FeatureAsync(place);
        row.Name.ShouldBe($"Phone two {marker}");
        row.Description.ShouldBeNull();

        // The same situation with force: the newer name goes too, and the row is as it was before
        // the upload that is being taken back.
        var third = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Phone three {marker}", 25.40, 45.40, await RevisionAsync(place)));
        _ = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Phone four {marker}", 25.40, 45.40, await RevisionAsync(place)));
        var forced = await RevertAsync(owner, third.GetProperty("importBatchId").GetGuid(), new { force = true });
        forced.StatusCode.ShouldBe(HttpStatusCode.OK, await forced.Content.ReadAsStringAsync());
        Lines(await forced.Content.ReadFromJsonAsync<JsonElement>())[place]
            .GetProperty("outcome").GetString().ShouldBe("undone");
        (await FeatureAsync(place)).Name.ShouldBe($"Phone two {marker}");
    }

    [Fact]
    public async Task The_detail_of_an_upload_names_the_row_an_updating_line_changed()
    {
        var cave = await CreateCaveAsync($"Named cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Named place {marker}", 25.40, 45.40);
        var set = await CreateSetAsync(owner, [cave]);
        var upload = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Named place {marker}", 25.40, 45.40, await RevisionAsync(place), description: "Edited"));
        var batchId = upload.GetProperty("importBatchId").GetGuid();

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/import-batches/{batchId}");
        var line = detail.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("attachedToFeatureId").ValueKind == JsonValueKind.String);
        line.GetProperty("attachedToFeatureId").GetGuid().ShouldBe(place);
        line.GetProperty("attachedToFeatureName").GetString().ShouldBe($"Named place {marker}");
        line.GetProperty("attachedToFeatureDeleted").GetBoolean().ShouldBeFalse();

        // After the row is deleted, the line still names it and says it is deleted.
        (await owner.DeleteAsync($"/api/v1/features/{place}")).EnsureSuccessStatusCode();
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/import-batches/{batchId}");
        var deleted = after.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("attachedToFeatureId").ValueKind == JsonValueKind.String);
        deleted.GetProperty("attachedToFeatureDeleted").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_detail_of_an_upload_names_the_row_only_to_a_reader_who_may_see_it()
    {
        var cave = await CreateCaveAsync($"Gated cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Gated place {marker}", 25.41, 45.41);
        var set = await CreateSetAsync(owner, [cave]);
        var upload = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Gated place {marker}", 25.41, 45.41, await RevisionAsync(place), description: "Edited"));
        var batchId = upload.GetProperty("importBatchId").GetGuid();

        // The reader is made the batch's confirmer, which is how a batch reader loses sight of a
        // row: they may read the batch, and the row is private to somebody else.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            _ = await db.ImportBatches.Where(b => b.Id == batchId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.ConfirmedByUserId, readerId));
        }

        async Task<JsonElement> LineAsync(HttpClient client)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/import-batches/{batchId}");
            return detail.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("attachedToFeatureId").ValueKind == JsonValueKind.String);
        }

        // Without a right on the row the line still points at it, and says nothing about it.
        var unseen = await LineAsync(reader);
        unseen.GetProperty("attachedToFeatureId").GetGuid().ShouldBe(place);
        unseen.GetProperty("attachedToFeatureName").ValueKind.ShouldBe(JsonValueKind.Null);

        // The same reader, once the row is theirs to read, is told its name: the null above is the
        // visibility rule at work and not a line that never had a name.
        await GrantAsync(place, "read");
        (await LineAsync(reader)).GetProperty("attachedToFeatureName").GetString().ShouldBe($"Gated place {marker}");
    }

    [Fact]
    public async Task The_history_of_a_row_can_be_narrowed_to_one_upload()
    {
        var cave = await CreateCaveAsync($"Narrow cave {marker}");
        var place = await CreatePlaceAsync(cave, $"Narrow place {marker}", 25.42, 45.42);
        var set = await CreateSetAsync(owner, [cave]);
        var upload = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(place, cave, $"Narrow place {marker}", 25.42, 45.42, await RevisionAsync(place), description: "Edited"));
        var batchId = upload.GetProperty("importBatchId").GetGuid();

        // Later uploads fill the row's history with events that are not this upload's.
        for (var i = 0; i < 3; i++)
        {
            _ = await UploadAsync(owner, set, Guid.CreateVersion7(),
                Place(place, cave, $"Narrow place {marker} {i}", 25.42, 45.42, await RevisionAsync(place), description: $"Later {i}"));
        }

        var all = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/history?entityType=feature&entityId={place}");
        var narrowed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/history?entityType=feature&entityId={place}&importBatchId={batchId}");
        var other = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/history?entityType=feature&entityId={place}&importBatchId={Guid.NewGuid()}");

        narrowed.GetProperty("totalItems").GetInt32().ShouldBeGreaterThan(0);
        narrowed.GetProperty("totalItems").GetInt32().ShouldBeLessThan(all.GetProperty("totalItems").GetInt32());
        narrowed.GetProperty("items").EnumerateArray()
            .ShouldAllBe(e => e.GetProperty("importBatchId").GetGuid() == batchId);
        other.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Undoing_some_lines_leaves_the_batch_open_and_the_rest_in_force()
    {
        var cave = await CreateCaveAsync($"Partial cave {marker}");
        var set = await CreateSetAsync(owner, [cave]);
        var kept = Guid.CreateVersion7();
        var taken = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Place(kept, cave, $"Kept {marker}", 25.50, 45.50),
            Place(taken, cave, $"Taken {marker}", 25.51, 45.51));
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        var items = await ItemIdsAsync(batchId);

        // A line of another batch is not a line of this one, and the whole request is refused
        // rather than quietly narrowed.
        var foreign = await RevertAsync(owner, batchId, new { itemIds = new[] { items[taken], long.MaxValue } });
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(foreign)).ShouldBe("import_batch.items_unknown");

        var partial = await RevertAsync(owner, batchId, new { itemIds = new[] { items[taken] } });
        partial.StatusCode.ShouldBe(HttpStatusCode.OK, await partial.Content.ReadAsStringAsync());
        var result = await partial.Content.ReadFromJsonAsync<JsonElement>();
        Lines(result).Keys.ShouldBe([taken]);
        result.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("batch").GetProperty("canRevert").GetBoolean().ShouldBeTrue();

        (await FeatureOrNullAsync(taken)).ShouldBeNull();
        (await FeatureOrNullAsync(kept)).ShouldNotBeNull();

        // The rest, later: the line already taken back is said to be, the other goes, and only now
        // is the batch reverted.
        var rest = await RevertAsync(owner, batchId, null);
        rest.StatusCode.ShouldBe(HttpStatusCode.OK, await rest.Content.ReadAsStringAsync());
        var restResult = await rest.Content.ReadFromJsonAsync<JsonElement>();
        var lines = Lines(restResult);
        lines[taken].GetProperty("reason").GetString().ShouldBe("already_undone");
        lines[kept].GetProperty("outcome").GetString().ShouldBe("undone");
        restResult.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.String);
        (await FeatureOrNullAsync(kept)).ShouldBeNull();
    }

    [Fact]
    public async Task An_undo_puts_a_position_back_only_for_a_caller_who_may_see_it_exactly()
    {
        var cave = await CreateCaveAsync($"Guarded cave {marker}", visibility: "authenticated");
        var guarded = await CreatePlaceAsync(
            cave, $"Guarded {marker}", 25.60, 45.60, visibility: "authenticated", locationProtected: true);
        await GrantAsync(guarded, "read, write, viewExactLocation");
        var set = await CreateSetAsync(reader, [cave]);

        // With exact view the reader moves the row and renames it; then exact view is taken away.
        var moved = await UploadAsync(reader, set, Guid.CreateVersion7(),
            Place(guarded, cave, $"Guarded moved {marker}", 25.61, 45.61, await RevisionAsync(guarded)));
        Row(moved, guarded).GetProperty("status").GetString().ShouldBe("updated");
        ((Point)(await FeatureAsync(guarded)).Geom!).X.ShouldBe(25.61, 1e-9);
        await GrantAsync(guarded, "read, write");

        var undo = await RevertAsync(reader, moved.GetProperty("importBatchId").GetGuid(), new { force = true });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var line = Lines(await undo.Content.ReadFromJsonAsync<JsonElement>())[guarded];
        line.GetProperty("outcome").GetString().ShouldBe("partly");
        line.GetProperty("applied").EnumerateArray().Select(f => f.GetString()).ShouldBe(["Name"]);
        var skipped = line.GetProperty("skipped").EnumerateArray().Single();
        skipped.GetProperty("field").GetString().ShouldBe("Geom");
        skipped.GetProperty("reason").GetString().ShouldBe("forbidden");

        var row = await FeatureAsync(guarded);
        row.Name.ShouldBe($"Guarded {marker}");
        ((Point)row.Geom!).X.ShouldBe(25.61, 1e-9);

        // The same undo by the same account holding exact view puts the position back too.
        await GrantAsync(guarded, "read, write, viewExactLocation");
        var again = await UploadAsync(reader, set, Guid.CreateVersion7(),
            Place(guarded, cave, $"Guarded moved again {marker}", 25.62, 45.62, await RevisionAsync(guarded)));
        var exact = await RevertAsync(reader, again.GetProperty("importBatchId").GetGuid(), new { });
        exact.StatusCode.ShouldBe(HttpStatusCode.OK, await exact.Content.ReadAsStringAsync());
        Lines(await exact.Content.ReadFromJsonAsync<JsonElement>())[guarded]
            .GetProperty("outcome").GetString().ShouldBe("undone");
        ((Point)(await FeatureAsync(guarded)).Geom!).X.ShouldBe(25.61, 1e-9);
    }

    [Fact]
    public async Task Only_the_confirmer_may_undo_an_upload_and_a_malformed_request_is_refused()
    {
        var cave = await CreateCaveAsync($"Owner cave {marker}");
        var set = await CreateSetAsync(owner, [cave]);
        var made = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(), Place(made, cave, $"Mine {marker}", 25.70, 45.70));
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        (await RevertAsync(anonymous, batchId, new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Somebody else's upload is not theirs to see, let alone take back.
        var stranger = await RevertAsync(reader, batchId, new { });
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(stranger)).ShouldBe("import_batch.not_found");

        var invalid = await RevertAsync(owner, batchId, new { itemIds = new[] { 0L } });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(invalid)).ShouldBe("validation.failed");

        // Neither refusal wrote anything: the row is still there for its own confirmer to take back.
        (await FeatureOrNullAsync(made)).ShouldNotBeNull();
        var mine = await RevertAsync(owner, batchId, new { });
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());
        (await FeatureOrNullAsync(made)).ShouldBeNull();
    }

    /// <summary>
    /// The entrance a promotion took the main role from has no line of its own, and the role still
    /// goes back to it — not to the cave's fallback, its oldest entrance — so the cave plots where
    /// it plotted before the upload.
    /// </summary>
    [Fact]
    public async Task Undoing_a_promotion_gives_the_main_role_back_to_the_entrance_that_held_it()
    {
        var cave = await CreateCaveAsync($"Promotion cave {marker}");
        var oldest = await CreateEntranceAsync(cave, $"Oldest {marker}", 25.01, 45.01, isMain: false);
        var held = await CreateEntranceAsync(cave, $"Held {marker}", 25.02, 45.02, isMain: true);
        var promoted = await CreateEntranceAsync(cave, $"Promoted {marker}", 25.03, 45.03, isMain: false);
        (await MainRoleAsync(cave)).ShouldBe((held, 25.02));
        var set = await CreateSetAsync(owner, [cave]);

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            EntranceRow(promoted, cave, $"Promoted {marker}", 25.03, 45.03, await RevisionAsync(promoted), isMain: true));
        Row(answer, promoted).GetProperty("status").GetString().ShouldBe("updated");
        (await MainRoleAsync(cave)).ShouldBe((promoted, 25.03));

        var undo = await RevertAsync(owner, answer.GetProperty("importBatchId").GetGuid(), new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var line = Lines(await undo.Content.ReadFromJsonAsync<JsonElement>())[promoted];
        line.GetProperty("outcome").GetString().ShouldBe("undone");
        line.GetProperty("applied").EnumerateArray().Select(f => f.GetString()).ShouldContain("IsMain");

        (await MainRoleAsync(cave)).ShouldBe((held, 25.02));
        oldest.ShouldNotBe(held);
    }

    /// <summary>
    /// Deleting the main entrance hands the role to the oldest survivor. Bringing the entrance back
    /// gives it the role again, which an ordinary restore deliberately does not do — the undo is
    /// asking for the cave as it was, not for one entrance back.
    /// </summary>
    [Fact]
    public async Task Undoing_the_deletion_of_the_main_entrance_gives_it_the_main_role_again()
    {
        var cave = await CreateCaveAsync($"Deletion cave {marker}");
        var oldest = await CreateEntranceAsync(cave, $"Oldest {marker}", 25.11, 45.11, isMain: false);
        var held = await CreateEntranceAsync(cave, $"Held {marker}", 25.12, 45.12, isMain: true);
        _ = await CreateEntranceAsync(cave, $"Third {marker}", 25.13, 45.13, isMain: false);
        var set = await CreateSetAsync(owner, [cave]);

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(), EntranceRemoval(held, await RevisionAsync(held)));
        Row(answer, held).GetProperty("status").GetString().ShouldBe("deleted");
        (await MainRoleAsync(cave)).ShouldBe((oldest, 25.11));

        var undo = await RevertAsync(owner, answer.GetProperty("importBatchId").GetGuid(), new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        Lines(await undo.Content.ReadFromJsonAsync<JsonElement>())[held]
            .GetProperty("outcome").GetString().ShouldBe("undone");

        (await FeatureOrNullAsync(held)).ShouldNotBeNull();
        (await MainRoleAsync(cave)).ShouldBe((held, 25.12));
    }

    /// <summary>
    /// A role somebody moved after the upload is theirs: the undo takes back the upload's own
    /// promotion and leaves the role where they put it, saying so on the line.
    /// </summary>
    [Fact]
    public async Task A_main_role_moved_after_the_upload_is_left_where_it_was_put()
    {
        var cave = await CreateCaveAsync($"Moved role cave {marker}");
        var held = await CreateEntranceAsync(cave, $"Held {marker}", 25.21, 45.21, isMain: true);
        var promoted = await CreateEntranceAsync(cave, $"Promoted {marker}", 25.22, 45.22, isMain: false);
        var chosen = await CreateEntranceAsync(cave, $"Chosen {marker}", 25.23, 45.23, isMain: false);
        var set = await CreateSetAsync(owner, [cave]);

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            EntranceRow(promoted, cave, $"Promoted {marker}", 25.22, 45.22, await RevisionAsync(promoted), isMain: true));

        // A second device then picks another entrance as the main one.
        _ = await UploadAsync(owner, set, Guid.CreateVersion7(),
            EntranceRow(chosen, cave, $"Chosen {marker}", 25.23, 45.23, await RevisionAsync(chosen), isMain: true));

        var undo = await RevertAsync(owner, answer.GetProperty("importBatchId").GetGuid(), new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var line = Lines(await undo.Content.ReadFromJsonAsync<JsonElement>())[promoted];
        line.GetProperty("outcome").GetString().ShouldBe("not_undone");
        line.GetProperty("skipped").EnumerateArray()
            .Single(f => f.GetProperty("field").GetString() == "IsMain")
            .GetProperty("reason").GetString().ShouldBe("superseded");
        (await MainRoleAsync(cave)).ShouldBe((chosen, 25.23));
        held.ShouldNotBe(chosen);
    }

    /// <summary>
    /// A device re-sending a row exactly as it stands is answered "updated" and leaves a line, but
    /// writes nothing; there is nothing of it to take back, and the upload still closes.
    /// </summary>
    [Fact]
    public async Task A_row_the_upload_sent_back_unchanged_counts_as_undone_and_the_upload_closes()
    {
        var name = $"Unchanged cave {marker}";
        var cave = await CreateCaveAsync(name);
        var set = await CreateSetAsync(owner, [cave]);

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(), new
        {
            id = cave,
            kind = "cave",
            baseRevision = await RevisionAsync(cave),
            deleted = false,
            name,
            caveTypeCode = "cave",
            isMain = false,
        });
        Row(answer, cave).GetProperty("status").GetString().ShouldBe("updated");
        var batchId = answer.GetProperty("importBatchId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // The case this is about: the upload's line, and no history row of the upload for it.
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var key = cave.ToString();
            (await db.AuditEntries.CountAsync(a => a.ImportBatchId == batchId && a.EntityId == key)).ShouldBe(0);
        }

        var undo = await RevertAsync(owner, batchId, new { });
        undo.StatusCode.ShouldBe(HttpStatusCode.OK, await undo.Content.ReadAsStringAsync());
        var result = await undo.Content.ReadFromJsonAsync<JsonElement>();
        var line = Lines(result)[cave];
        line.GetProperty("outcome").GetString().ShouldBe("undone");
        line.GetProperty("applied").GetArrayLength().ShouldBe(0);
        result.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.String);
        (await FeatureAsync(cave)).Name.ShouldBe(name);
    }

    /// <summary>
    /// Bringing back a row the upload deleted asks the right a restore asks — the right to delete
    /// it — of the caller now, both ways round.
    /// </summary>
    [Fact]
    public async Task A_deletion_is_undone_only_for_a_caller_who_may_still_delete_the_row()
    {
        var cave = await CreateCaveAsync($"Rights cave {marker}", visibility: "authenticated");
        var place = await CreatePlaceAsync(cave, $"Rights place {marker}", 25.80, 45.80, visibility: "authenticated");
        await GrantSubtreeAsync(cave, "read, write, delete");
        var set = await CreateSetAsync(reader, [cave]);

        var answer = await UploadAsync(reader, set, Guid.CreateVersion7(), Removal(place, await RevisionAsync(place)));
        Row(answer, place).GetProperty("status").GetString().ShouldBe("deleted");
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        await GrantSubtreeAsync(cave, "read, write");
        var refused = await RevertAsync(reader, batchId, new { });
        refused.StatusCode.ShouldBe(HttpStatusCode.OK, await refused.Content.ReadAsStringAsync());
        var refusedResult = await refused.Content.ReadFromJsonAsync<JsonElement>();
        var line = Lines(refusedResult)[place];
        line.GetProperty("outcome").GetString().ShouldBe("not_undone");
        line.GetProperty("reason").GetString().ShouldBe("forbidden");
        refusedResult.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await FeatureOrNullAsync(place)).ShouldBeNull();

        await GrantSubtreeAsync(cave, "read, write, delete");
        var allowed = await RevertAsync(reader, batchId, new { });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        Lines(await allowed.Content.ReadFromJsonAsync<JsonElement>())[place]
            .GetProperty("outcome").GetString().ShouldBe("undone");
        (await FeatureOrNullAsync(place)).ShouldNotBeNull();
    }

    // ---------- helpers ----------

    private static Dictionary<Guid, JsonElement> Lines(JsonElement result) =>
        result.GetProperty("items").EnumerateArray()
            .ToDictionary(l => l.GetProperty("featureId").GetGuid());

    private static JsonElement Row(JsonElement answer, Guid id) =>
        answer.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static Task<HttpResponseMessage> RevertAsync(HttpClient client, Guid batchId, object? body) =>
        body is null
            ? client.PostAsync($"/api/v1/import-batches/{batchId}/revert", null)
            : client.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", body);

    private static object Place(
        Guid id,
        Guid caveId,
        string name,
        double lon,
        double lat,
        DateTimeOffset? baseRevision = null,
        string? description = null,
        DateTimeOffset? clientUpdatedAt = null) => new
        {
            id,
            kind = "generic",
            baseRevision,
            deleted = false,
            parentId = caveId,
            name,
            description,
            featureTypeCode = "cave_place",
            isMain = false,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            clientUpdatedAt,
        };

    /// <summary>An entrance as a device sends it back: the stored position, the main mark as given.</summary>
    private static object EntranceRow(Guid id, Guid caveId, string name, double lon, double lat, DateTimeOffset baseRevision, bool isMain) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain,
        geometry = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude = 500.0,
        positionQuality = "Gps",
    };

    private static object EntranceRemoval(Guid id, DateTimeOffset baseRevision) => new
    {
        id,
        kind = "caveEntrance",
        baseRevision,
        deleted = true,
        isMain = false,
    };

    /// <summary>The cave's entrances in the order made, which is also the cave's fallback order for its main one.</summary>
    private async Task<Guid> CreateEntranceAsync(Guid cave, string name, double lon, double lat, bool isMain)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            name,
            entranceTypeId,
            isMain,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            altitude = 500m,
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>The cave's main entrance and the longitude of the cave's own point.</summary>
    private async Task<(Guid? Main, double? CaveLon)> MainRoleAsync(Guid cave)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var mains = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveFeatureId == cave && e.IsMain && db.Features.Any(f => f.Id == e.Id))
            .Select(e => e.Id)
            .ToListAsync();
        mains.Count.ShouldBeLessThanOrEqualTo(1, "a cave has one main entrance at most");
        var point = (await db.Features.AsNoTracking().SingleAsync(f => f.Id == cave)).Geom as Point;
        return (mains.Count == 0 ? null : mains[0], point?.X);
    }

    /// <summary>Gives the reader exactly these rights over the cave and everything inside it, replacing what it held there.</summary>
    private async Task GrantSubtreeAsync(Guid caveId, string actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions,
                    scopeKind = "subtree",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object Removal(Guid id, DateTimeOffset baseRevision) => new
    {
        id,
        kind = "generic",
        baseRevision,
        deleted = true,
        isMain = false,
    };

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid set, Guid batchId, params object[] rows)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId,
            contractVersion = 1,
            rows,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> DownloadAsync(HttpClient client, Guid set, string? cursor = null)
    {
        var url = cursor is null
            ? $"/api/v1/sync/sets/{set}/download"
            : $"/api/v1/sync/sets/{set}/download?cursor={Uri.EscapeDataString(cursor)}";
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// A place under the cave, made through the web application, so that the upload whose undo a
    /// test is about starts from a row that upload did not create.
    /// </summary>
    private async Task<Guid> CreatePlaceAsync(
        Guid cave, string name, double lon, double lat, string visibility = "private", bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = await CavePlaceTypeAsync(),
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility,
            locationProtected,
            parents = new[] { new { parentId = cave, isPrimary = true } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<long> CavePlaceTypeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).SingleAsync();
    }

    private async Task<Guid> CreateCaveAsync(string name, string visibility = "private")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Gives the reader exactly these rights on one row, replacing whatever it held there.</summary>
    private async Task GrantAsync(Guid featureId, string actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions,
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Dictionary<Guid, long>> ItemIdsAsync(Guid batchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == batchId)
            .ToDictionaryAsync(i => (i.FeatureId ?? i.AttachedToFeatureId)!.Value, i => i.Id);
    }

    private async Task<DateTimeOffset> RevisionAsync(Guid featureId) => (await FeatureAsync(featureId)).UpdatedAt;

    private async Task<Feature> FeatureAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.AsNoTracking().IgnoreQueryFilters().SingleAsync(f => f.Id == featureId);
    }

    /// <summary>The live row, or null when it is deleted.</summary>
    private async Task<Feature?> FeatureOrNullAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == featureId);
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
