// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The upload half of the mobile protocol, played from the device's side against a real
/// database: rows arriving under the identifiers a phone chose, an answer that survives being
/// asked for twice, and the arbitration that decides whose version of a row stands.
/// </summary>
/// <remarks>
/// Every refusal here is asserted beside something that was accepted on the same call. An upload
/// that wrote nothing at all would satisfy any "must not be applied" assertion ever written, so a
/// test that only names what was refused would pass just as well against an endpoint that had
/// stopped working altogether.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SyncUploadTests : IAsyncLifetime, IDisposable
{
    private const double OpenLon = 25.11223;
    private const double OpenLat = 45.33445;
    private const double HiddenLon = 24.87654;
    private const double HiddenLat = 45.12345;

    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private long caveTypeId;
    private long entranceTypeId;
    private Guid set;
    private Guid viewerSet;
    private string marker = null!;

    public SyncUploadTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"up-own-{marker}@t.local");

        // A Viewer, deliberately: the seeded Editors group creates across the whole feature domain,
        // so an Editor asked to prove that creation is refused would be refused for no reason the
        // test could name.
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"up-view-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            // By code, never by whichever row happens to be first: the number behind a code is
            // assigned by the installation that seeded the table and is not the same on two servers.
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"up-own-{marker}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"up-view-{marker}@t.local");
        set = await CreateSetAsync(owner);
        viewerSet = await CreateSetAsync(viewer);
    }

    [Fact]
    public async Task A_batch_lands_under_the_identifiers_the_device_chose_and_is_recorded_as_one_import()
    {
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(caveId, $"Peștera {marker}"),
            Entrance(entranceId, caveId, 23.5, 45.5, altitude: 812));

        Row(answer, caveId).GetProperty("status").GetString().ShouldBe("created");
        Row(answer, entranceId).GetProperty("status").GetString().ShouldBe("created");
        Row(answer, caveId).GetProperty("revision").GetDateTimeOffset().ShouldNotBe(default);
        answer.GetProperty("written").GetInt32().ShouldBe(2);
        answer.GetProperty("refused").GetInt32().ShouldBe(0);

        // The identifier the phone minted is the identifier the server serves the row under.
        // Anything less means a translation table between the two sides, for ever.
        var cave = await owner.GetAsync($"/api/v1/caves/{caveId}");
        cave.StatusCode.ShouldBe(HttpStatusCode.OK);

        var batchId = answer.GetProperty("importBatchId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        batch.Source.ShouldBe(ImportSource.DeviceSync);
        batch.CreatedCount.ShouldBe(2);
        (await db.ImportBatchItems.CountAsync(i => i.ImportBatchId == batchId && i.FeatureId != null))
            .ShouldBe(2);

        // Recorded as a batch with no file behind it, which is what the list of imports has to be
        // able to say without claiming somebody deleted one.
        batch.GeofileId.ShouldBeNull();
    }

    [Fact]
    public async Task Sending_the_same_batch_again_answers_the_first_result_and_writes_nothing()
    {
        var batchId = Guid.CreateVersion7();
        var caveId = Guid.CreateVersion7();
        var first = await UploadAsync(owner, set, batchId, Cave(caveId, $"Avenul {marker}"));
        first.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        var again = await UploadAsync(owner, set, batchId, Cave(caveId, "A different name entirely"));

        again.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        again.GetProperty("importBatchId").GetGuid().ShouldBe(first.GetProperty("importBatchId").GetGuid());
        Row(again, caveId).GetProperty("status").GetString().ShouldBe("created");
        // Including the revision, which is what the device sends back as its base revision next
        // time: an answer that came back without one would leave a retrying phone unable to edit
        // the row it had just created.
        Row(again, caveId).GetProperty("revision").GetDateTimeOffset()
            .ShouldBe(Row(first, caveId).GetProperty("revision").GetDateTimeOffset());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // Not applied a second time, and not recorded a second time: the name the resend carried
        // never reached the row, and there is one batch to undo rather than two.
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == caveId)).Name
            .ShouldBe($"Avenul {marker}");
        (await db.ImportBatches.CountAsync(b => b.SyncBatchId == batchId)).ShouldBe(1);
    }

    [Fact]
    public async Task An_edit_applies_on_the_revision_the_device_holds_and_is_refused_on_a_stale_one()
    {
        var staleId = Guid.CreateVersion7();
        var freshId = Guid.CreateVersion7();
        var created = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "Stale"), Cave(freshId, "Fresh"));
        var stale = Row(created, staleId).GetProperty("revision").GetDateTimeOffset();
        var fresh = Row(created, freshId).GetProperty("revision").GetDateTimeOffset();

        // Somebody else moves the first row on, the way a colleague editing in the browser would.
        var edited = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "Moved on by somebody else", baseRevision: stale));
        Row(edited, staleId).GetProperty("status").GetString().ShouldBe("updated");

        // Now the device sends both: one on the revision it last saw, which has since moved, and
        // one on a revision that is still current. The stale row is refused and the other lands —
        // a batch is arbitrated row by row, not accepted or thrown away whole.
        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "What the phone had", baseRevision: stale),
            Cave(freshId, "Renamed from the phone", baseRevision: fresh));

        Row(answer, staleId).GetProperty("status").GetString().ShouldBe("conflict");
        Row(answer, staleId).GetProperty("code").GetString().ShouldBe("sync.conflict");
        Row(answer, freshId).GetProperty("status").GetString().ShouldBe("updated");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == staleId)).Name
            .ShouldBe("Moved on by somebody else");
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == freshId)).Name
            .ShouldBe("Renamed from the phone");
    }

    [Fact]
    public async Task A_delete_is_arbitrated_exactly_as_an_edit_is()
    {
        var goneId = Guid.CreateVersion7();
        var stayId = Guid.CreateVersion7();
        var created = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(goneId, "To be removed"), Cave(stayId, "To be kept"));
        var goneRevision = Row(created, goneId).GetProperty("revision").GetDateTimeOffset();
        var stayRevision = Row(created, stayId).GetProperty("revision").GetDateTimeOffset();

        await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(stayId, "Kept, and renamed here", baseRevision: stayRevision));

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(goneId, "To be removed", baseRevision: goneRevision, deleted: true),
            Cave(stayId, "To be kept", baseRevision: stayRevision, deleted: true));

        Row(answer, goneId).GetProperty("status").GetString().ShouldBe("deleted");
        Row(answer, stayId).GetProperty("status").GetString().ShouldBe("conflict");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.Id == goneId || f.Id == stayId)
            .ToDictionaryAsync(f => f.Id, f => f.DeletedAt);
        rows[goneId].ShouldNotBeNull();
        rows[stayId].ShouldBeNull();
    }

    [Fact]
    public async Task A_devices_own_clock_never_decides_which_version_stands()
    {
        var staleId = Guid.CreateVersion7();
        var freshId = Guid.CreateVersion7();
        var created = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "One"), Cave(freshId, "Two"));
        var stale = Row(created, staleId).GetProperty("revision").GetDateTimeOffset();
        var fresh = Row(created, freshId).GetProperty("revision").GetDateTimeOffset();

        await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "Moved on", baseRevision: stale));

        // A phone whose clock says the year 2099 is not thereby right about anything. The stale
        // row is refused however new its clock claims to be, and the row whose base revision is
        // current lands however old its clock claims to be.
        var future = DateTimeOffset.UtcNow.AddYears(75);
        var past = DateTimeOffset.UtcNow.AddYears(-20);
        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(staleId, "From a fast clock", baseRevision: stale, clientUpdatedAt: future),
            Cave(freshId, "From a slow clock", baseRevision: fresh, clientUpdatedAt: past));

        Row(answer, staleId).GetProperty("status").GetString().ShouldBe("conflict");
        Row(answer, freshId).GetProperty("status").GetString().ShouldBe("updated");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.Features.AsNoTracking().SingleAsync(f => f.Id == freshId);
        row.Name.ShouldBe("From a slow clock");
        // Kept, because two devices that were both offline compare it with each other. Never
        // consulted here, which is what the refusal above is about.
        row.ClientUpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Undoing_a_sync_batch_removes_exactly_the_rows_it_created()
    {
        var existing = await CreateCaveAsync($"Already here {marker}");
        var existingRevision = await RevisionAsync(existing);
        var madeId = Guid.CreateVersion7();

        var answer = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(madeId, "Made by the phone"),
            Cave(existing, "Renamed by the phone", baseRevision: existingRevision));
        Row(answer, madeId).GetProperty("status").GetString().ShouldBe("created");
        Row(answer, existing).GetProperty("status").GetString().ShouldBe("updated");

        var revert = await owner.PostAsJsonAsync(
            $"/api/v1/import-batches/{answer.GetProperty("importBatchId").GetGuid()}/revert", new { });
        revert.StatusCode.ShouldBe(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.Id == madeId || f.Id == existing)
            .ToDictionaryAsync(f => f.Id, f => f.DeletedAt);
        // What the batch made comes back out; what it merely edited stays, because undoing an
        // import has never meant deleting somebody else's cave that the import happened to touch.
        rows[madeId].ShouldNotBeNull();
        rows[existing].ShouldBeNull();
    }

    [Fact]
    public async Task A_caller_with_no_right_to_create_is_refused_and_one_with_it_is_not()
    {
        var refusedId = Guid.CreateVersion7();
        var refused = await UploadAsync(viewer, viewerSet, Guid.CreateVersion7(),
            Cave(refusedId, "From an account that may only look"));

        Row(refused, refusedId).GetProperty("status").GetString().ShouldBe("rejected");
        Row(refused, refusedId).GetProperty("code").GetString().ShouldBe("access.create_forbidden");

        // The same shape from an account that may create, so the refusal above is about the
        // account and not about a request the endpoint could never have accepted from anybody.
        var allowedId = Guid.CreateVersion7();
        var allowed = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(allowedId, "From an account that may write"));
        Row(allowed, allowedId).GetProperty("status").GetString().ShouldBe("created");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().AnyAsync(f => f.Id == refusedId)).ShouldBeFalse();
        (await db.Features.AsNoTracking().AnyAsync(f => f.Id == allowedId)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_upload_naming_a_protocol_this_server_does_not_speak_is_answered_before_anything_is_written()
    {
        var caveId = Guid.CreateVersion7();
        var response = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 99,
            rows = new[] { Cave(caveId, "From a future protocol") },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("sync.contract_unsupported");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().AnyAsync(f => f.Id == caveId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_set_the_caller_does_not_own_is_answered_like_one_that_is_not_there()
    {
        var response = await viewer.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { Cave(Guid.CreateVersion7(), "Into somebody else's set") },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // And the caller's own set answers, so the refusal above is about ownership rather than
        // about an endpoint that refuses everybody.
        var mine = await viewer.PostAsJsonAsync($"/api/v1/sync/sets/{viewerSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[] { Cave(Guid.CreateVersion7(), "Into my own set") },
        });
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The conflict answer is the second place this slice hands a device a position, and the
    /// route it sits on runs none of the download's filtering — so it is proved here against a
    /// real stale revision rather than against a download. Two caves lose the same conflict on
    /// the same call: the one this caller may place comes back with its coordinates, and the one
    /// it may not is simply not there. Both halves are asserted together on purpose — an echo
    /// that had stopped working altogether would satisfy the absence on its own.
    /// </summary>
    [Fact]
    public async Task A_conflict_hands_back_the_server_row_and_leaves_out_the_one_the_caller_may_not_place()
    {
        var openCave = await CreateCaveAsync($"Open {marker}", locationProtected: false);
        await AddEntranceAsync(openCave, OpenLon, OpenLat);
        var hiddenCave = await CreateCaveAsync($"Hidden {marker}", locationProtected: true);
        await AddEntranceAsync(hiddenCave, HiddenLon, HiddenLat);

        // Read and write on both, and exact view on neither. The account is not the owner of
        // either row — a row's own owner is always shown it exactly, whatever its protection says,
        // so an owner asked to prove withholding would be shown the position for a reason the
        // test could not name.
        await GrantAsync(openCave, AccessAction.Read | AccessAction.Write);
        await GrantAsync(hiddenCave, AccessAction.Read | AccessAction.Write);

        // A revision from before either cave existed: whatever the device once held, it is not
        // what the server holds now, which is the whole of what a conflict is.
        var stale = DateTimeOffset.UtcNow.AddDays(-1);
        var answer = await UploadAsync(viewer, viewerSet, Guid.NewGuid(),
            Cave(openCave, "Renamed open", baseRevision: stale),
            Cave(hiddenCave, "Renamed hidden", baseRevision: stale));

        Row(answer, openCave).GetProperty("status").GetString().ShouldBe("conflict");
        Row(answer, hiddenCave).GetProperty("status").GetString().ShouldBe("conflict");

        var echoed = answer.GetProperty("conflicts").EnumerateArray().ToList();

        // The row this caller may place comes back whole, coordinates and all, so the device has
        // something to merge against.
        var open = echoed.SingleOrDefault(f => f.GetProperty("id").GetGuid() == openCave);
        open.ValueKind.ShouldBe(JsonValueKind.Object, "the readable row must be echoed");
        open.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(OpenLon, 1e-9);

        // The other is absent rather than blurred. Absence is this channel's answer everywhere:
        // a snapped point written to a phone in cleartext outlives the moment it was looked at.
        echoed.ShouldNotContain(f => f.GetProperty("id").GetGuid() == hiddenCave);
    }

    /// <summary>
    /// The write-right rule, per field. A caller who was never shown a position submits a
    /// different one; the stored geometry does not move a byte, and the rest of the same edit
    /// lands. The name change is what stops this passing against an endpoint that had stopped
    /// writing anything, and the owner's identical edit at the end is what stops it passing
    /// against one that never writes a coordinate at all.
    /// </summary>
    [Fact]
    public async Task A_coordinate_from_a_caller_who_may_not_place_the_row_is_ignored_and_the_rest_of_their_edit_lands()
    {
        var caveId = await CreateCaveAsync($"Guarded {marker}", locationProtected: true);
        var entranceId = await AddEntranceAsync(caveId, HiddenLon, HiddenLat);
        await GrantAsync(caveId, AccessAction.Read | AccessAction.Write);
        await GrantAsync(entranceId, AccessAction.Read | AccessAction.Write);

        var (storedPoint, storedAltitude, storedQuality) = await EntranceStateAsync(entranceId);
        var storedCavePoint = await GeometryAsync(caveId);

        var answer = await UploadAsync(viewer, viewerSet, Guid.NewGuid(),
            Entrance(entranceId, caveId, HiddenLon + 0.01, HiddenLat + 0.01, 999,
                baseRevision: await RevisionAsync(entranceId), name: "Renamed by the phone"));
        Row(answer, entranceId).GetProperty("status").GetString().ShouldBe("updated");

        var (afterPoint, afterAltitude, afterQuality) = await EntranceStateAsync(entranceId);
        afterPoint.AsBinary().ShouldBe(storedPoint.AsBinary());
        afterAltitude.ShouldBe(storedAltitude);
        afterQuality.ShouldBe(storedQuality);

        // And the cave above it, whose own map point is this entrance's, has not been moved by
        // the back door either.
        (await GeometryAsync(caveId)).AsBinary().ShouldBe(storedCavePoint.AsBinary());

        // The half of the edit that was never in question stands. A rule that refused the whole
        // row would lose a caver's rename for no protection at all.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId))
                .Name.ShouldBe("Renamed by the phone");
        }

        // The same submission from an account that may place the row does move it — without this
        // the assertions above would hold just as well for a path that never writes a coordinate.
        var moved = await UploadAsync(owner, set, Guid.NewGuid(),
            Entrance(entranceId, caveId, HiddenLon + 0.01, HiddenLat + 0.01, 999,
                baseRevision: await RevisionAsync(entranceId), name: "Moved by its owner"));
        Row(moved, entranceId).GetProperty("status").GetString().ShouldBe("updated");
        (await EntranceStateAsync(entranceId)).Point.X.ShouldBe(HiddenLon + 0.01, 1e-9);
    }

    /// <summary>
    /// The create-side half of the write-right rule, which the update-side test above cannot
    /// reach. Creating an entrance writes a position onto the cave it hangs under — the cave's
    /// map point is its main entrance's — so a create is a way of moving a row that already
    /// exists, and the caller may never have been shown where that row is. The same caller
    /// creating under a cave they may place proves the refusal is about the protection.
    /// </summary>
    [Fact]
    public async Task An_entrance_created_under_a_cave_the_caller_may_not_place_is_refused_and_one_under_a_cave_they_may_lands()
    {
        var guarded = await CreateCaveAsync($"Guarded parent {marker}", locationProtected: true);
        var open = await CreateCaveAsync($"Open parent {marker}", locationProtected: false);
        foreach (var cave in new[] { guarded, open })
        {
            // Subtree, because creating under a row is decided against that row's own facts:
            // an object rule names the row itself and reaches nothing hung beneath it.
            await GrantAsync(
                cave, AccessAction.Read | AccessAction.Write | AccessAction.Create, scopeKind: "subtree");
        }

        var guardedBefore = await GeometryOrNullAsync(guarded);
        var refusedId = Guid.CreateVersion7();
        var allowedId = Guid.CreateVersion7();

        var answer = await UploadAsync(viewer, viewerSet, Guid.NewGuid(),
            Entrance(refusedId, guarded, HiddenLon + 0.05, HiddenLat + 0.05, 700, name: "From a phone"),
            Entrance(allowedId, open, OpenLon + 0.05, OpenLat + 0.05, 700, name: "From a phone"));

        Row(answer, refusedId).GetProperty("status").GetString().ShouldBe("rejected");
        Row(answer, refusedId).GetProperty("code").GetString().ShouldBe("sync.location_forbidden");
        Row(answer, allowedId).GetProperty("status").GetString().ShouldBe("created");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.AsNoTracking().AnyAsync(f => f.Id == refusedId)).ShouldBeFalse();

        // And the guarded cave did not move: the refusal is the whole of what happened.
        var guardedAfter = await GeometryOrNullAsync(guarded);
        (guardedAfter?.AsBinary()).ShouldBe(guardedBefore?.AsBinary());
    }

    /// <summary>
    /// Removing a row and editing one are different rights, and this channel asks for the one
    /// the act needs. A grant meant to let somebody correct a cave must not also let them take
    /// it — and its whole containment subtree — out of the registry, which is exactly what the
    /// web interface refuses the same account.
    /// </summary>
    [Fact]
    public async Task A_delete_needs_the_right_to_delete_and_not_merely_the_right_to_write()
    {
        var editable = await CreateCaveAsync($"Editable only {marker}", locationProtected: false);
        var removable = await CreateCaveAsync($"Removable {marker}", locationProtected: false);
        await GrantAsync(editable, AccessAction.Read | AccessAction.Write);
        await GrantAsync(removable, AccessAction.Read | AccessAction.Write | AccessAction.Delete);

        var answer = await UploadAsync(viewer, viewerSet, Guid.NewGuid(),
            Cave(editable, "Gone", baseRevision: await RevisionAsync(editable), deleted: true),
            Cave(removable, "Gone", baseRevision: await RevisionAsync(removable), deleted: true));

        Row(answer, editable).GetProperty("status").GetString().ShouldBe("rejected");
        Row(answer, editable).GetProperty("code").GetString().ShouldBe("sync.row_delete_forbidden");
        Row(answer, removable).GetProperty("status").GetString().ShouldBe("deleted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.Id == editable || f.Id == removable)
            .ToDictionaryAsync(f => f.Id, f => f.DeletedAt);
        rows[editable].ShouldBeNull();
        rows[removable].ShouldNotBeNull();
    }

    /// <summary>
    /// Deleting an entrance moves the cave above it. The count is a stored column and the cave's
    /// map point is a copy of its main entrance's, so a delete that left them alone would leave
    /// the cave plotting at a position that no longer exists — which the integrity check reports
    /// as two separate faults on its next run.
    /// </summary>
    [Fact]
    public async Task Deleting_an_entrance_leaves_the_cave_counting_and_plotting_what_is_left()
    {
        var caveId = await CreateCaveAsync($"Two ways in {marker}");
        var main = await AddEntranceAsync(caveId, OpenLon, OpenLat);
        var second = await AddEntranceAsync(caveId, OpenLon + 0.002, OpenLat + 0.002);

        // The second one arrived as the main; delete it and the survivor has to take over.
        var answer = await UploadAsync(owner, set, Guid.NewGuid(),
            Cave(second, "Gone", baseRevision: await RevisionAsync(second), deleted: true));
        Row(answer, second).GetProperty("status").GetString().ShouldBe("deleted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cave = await db.Caves.AsNoTracking().SingleAsync(c => c.Id == caveId);
        cave.EntranceCount.ShouldBe(1);
        (await db.CaveEntrances.AsNoTracking().SingleAsync(e => e.Id == main)).IsMain.ShouldBeTrue();

        var caveGeom = (Point)(await db.Features.AsNoTracking().SingleAsync(f => f.Id == caveId)).Geom!;
        var mainGeom = (Point)(await db.Features.AsNoTracking().SingleAsync(f => f.Id == main)).Geom!;
        caveGeom.AsBinary().ShouldBe(mainGeom.AsBinary());
    }

    /// <summary>
    /// The cave half of the write-right rule, driven with no <c>parentId</c> in the request.
    /// Whether the cave above an entrance may be moved is a question about a row this server
    /// holds, and it has to be asked of this server's own rows: an optional request field is
    /// something the caller chooses, and a guard whose inputs the caller chooses is a guard the
    /// caller can switch off by leaving a field out.
    /// </summary>
    [Fact]
    public async Task A_caller_who_may_place_an_entrance_but_not_its_cave_moves_neither_even_with_no_parent_named()
    {
        var caveId = await CreateCaveAsync($"Handed over {marker}", locationProtected: true);
        var entranceId = await AddEntranceAsync(caveId, HiddenLon, HiddenLat);
        await GrantAsync(caveId, AccessAction.Read | AccessAction.Write);
        await GrantAsync(entranceId, AccessAction.Read | AccessAction.Write);

        // The entrance changes hands while the cave does not. A row's own owner is always shown
        // it exactly, so this account may now place the entrance and still not the cave — which
        // is the one arrangement in which the two answers differ.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var entrance = await db.Features.SingleAsync(f => f.Id == entranceId);
            entrance.OwnerUserId = viewerId;
            await db.SaveChangesAsync();
        }

        var storedCavePoint = await GeometryAsync(caveId);
        var (storedPoint, _, _) = await EntranceStateAsync(entranceId);

        var answer = await UploadAsync(viewer, viewerSet, Guid.NewGuid(),
            EntranceWithNoParent(entranceId, HiddenLon + 0.02, HiddenLat + 0.02, 900,
                await RevisionAsync(entranceId), "Renamed with no parent named"));
        Row(answer, entranceId).GetProperty("status").GetString().ShouldBe("updated");

        // Neither row moved: the cave is the one this caller may not place, and the entrance's
        // position is the cave's.
        (await GeometryAsync(caveId)).AsBinary().ShouldBe(storedCavePoint.AsBinary());
        (await EntranceStateAsync(entranceId)).Point.AsBinary().ShouldBe(storedPoint.AsBinary());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId))
                .Name.ShouldBe("Renamed with no parent named");
        }

        // The owner of the cave sends the same shape and it does move, so the two assertions
        // above are not holding for a path that never writes a coordinate at all.
        var moved = await UploadAsync(owner, set, Guid.NewGuid(),
            EntranceWithNoParent(entranceId, HiddenLon + 0.02, HiddenLat + 0.02, 900,
                await RevisionAsync(entranceId), "Moved by an account that may place it"));
        Row(moved, entranceId).GetProperty("status").GetString().ShouldBe("updated");
        (await EntranceStateAsync(entranceId)).Point.X.ShouldBe(HiddenLon + 0.02, 1e-9);
    }

    /// <summary>
    /// A kind that sits at the top of the tree arrives with nothing above it, and is written
    /// rather than refused. The surface area is where a device's place codes are allocated from
    /// and what a sync set is rooted in, so a channel that demanded a container of every row
    /// would strand everything a phone numbers underneath one. The place inside a cave, on the
    /// same call, still needs its container — the requirement is a property of the kind.
    /// </summary>
    [Fact]
    public async Task A_kind_that_needs_no_container_lands_without_one_and_a_kind_that_needs_one_still_does()
    {
        var areaId = Guid.CreateVersion7();
        var strandedId = Guid.CreateVersion7();
        var caveId = await CreateCaveAsync($"Holds a place {marker}");
        var placeId = Guid.CreateVersion7();

        var answer = await UploadAsync(owner, set, Guid.NewGuid(),
            Place(areaId, $"Bihor {marker}", "surface_area", parentId: null),
            Place(strandedId, $"Sump with no cave {marker}", "cave_place", parentId: null),
            Place(placeId, $"Sump {marker}", "cave_place", parentId: caveId));

        Row(answer, areaId).GetProperty("status").GetString().ShouldBe("created");
        Row(answer, strandedId).GetProperty("code").GetString().ShouldBe("sync.parent_required");
        Row(answer, placeId).GetProperty("status").GetString().ShouldBe("created");

        // And a cave sent under that area keeps the edge, rather than being written loose: a
        // set rooted at the area has to be able to find the caves inside it.
        var caveUnderArea = Guid.CreateVersion7();
        var second = await UploadAsync(owner, set, Guid.NewGuid(),
            CaveUnder(caveUnderArea, $"Inside Bihor {marker}", areaId));
        Row(second, caveUnderArea).GetProperty("status").GetString().ShouldBe("created");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.FeatureHierarchyEdges.AsNoTracking()
            .AnyAsync(e => e.ChildId == caveUnderArea && e.ParentId == areaId)).ShouldBeTrue();
        (await db.Features.AsNoTracking().AnyAsync(f => f.Id == strandedId)).ShouldBeFalse();
    }

    /// <summary>
    /// A device is offline when it decides to add a cave, so the check a file import runs before
    /// committing has to run here afterwards. The row lands either way — the report never
    /// changes a verdict — and what it says is what the caver needs to decide whether they have
    /// just re-entered something the club already holds.
    /// </summary>
    [Fact]
    public async Task A_row_created_beside_something_already_here_is_written_and_reported()
    {
        var caveId = await CreateCaveAsync($"Already surveyed {marker}");
        await AddEntranceAsync(caveId, OpenLon, OpenLat);

        var nearId = Guid.CreateVersion7();
        var farCave = Guid.CreateVersion7();
        var farId = Guid.CreateVersion7();
        var mineId = Guid.CreateVersion7();

        var answer = await UploadAsync(owner, set, Guid.NewGuid(),
            Cave(mineId, $"From the phone {marker}"),
            // A few metres from the entrance already in the registry.
            Entrance(nearId, mineId, OpenLon + 0.0001, OpenLat, 800, name: "Near one already here"),
            Cave(farCave, $"Far from anything {marker}"),
            Entrance(farId, farCave, OpenLon + 0.5, OpenLat + 0.5, 800, name: "Far from anything"));

        Row(answer, nearId).GetProperty("status").GetString().ShouldBe("created");
        Row(answer, farId).GetProperty("status").GetString().ShouldBe("created");

        var duplicates = answer.GetProperty("duplicates").EnumerateArray().ToList();
        var reported = duplicates.SingleOrDefault(d => d.GetProperty("id").GetGuid() == nearId);
        reported.ValueKind.ShouldBe(JsonValueKind.Object, "the row beside an existing one must be reported");
        reported.GetProperty("nearby").EnumerateArray()
            .Select(n => n.GetProperty("caveFeatureId").GetGuid())
            .ShouldContain(caveId);

        // The row that landed nowhere near anything is not reported, so the report is about
        // proximity rather than about every row a batch writes. Nor are the batch's own rows
        // each other's duplicates.
        duplicates.ShouldNotContain(d => d.GetProperty("id").GetGuid() == farId);
    }

    private static object Place(Guid id, string name, string featureTypeCode, Guid? parentId) => new
    {
        id,
        kind = "generic",
        parentId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        featureTypeCode,
        isMain = false,
    };

    private static object CaveUnder(Guid id, string name, Guid parentId) => new
    {
        id,
        kind = "cave",
        parentId,
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    /// <summary>An entrance edit that names no container, which the shape permits.</summary>
    private static object EntranceWithNoParent(
        Guid id, double lon, double lat, double altitude, DateTimeOffset baseRevision, string name) => new
    {
        id,
        kind = "caveEntrance",
        baseRevision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude,
        positionQuality = "Gps",
    };

    /// <summary>
    /// Containment and kind are set when a row is created and are read by nothing afterwards. The
    /// documented catalogue of refusals says so, and this is what makes that statement checkable:
    /// the two are neither applied nor refused on an update, so the honest answer is
    /// <c>updated</c> for the fields that were written, with the edge where it was.
    /// </summary>
    /// <remarks>
    /// A test is here rather than only prose because the failure it describes is silent on both
    /// sides. A device that moved a row to another container is told the write succeeded, keeps
    /// its own model of where the row lives, and disagrees with the server for ever — and
    /// containment is the sole axis protection and visibility are inherited along, so the two
    /// sides then disagree about who may see it.
    /// </remarks>
    [Fact]
    public async Task An_update_naming_another_container_or_another_kind_is_answered_updated_and_moves_nothing()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();

        var created = await UploadAsync(owner, set, Guid.CreateVersion7(),
            Cave(first, $"Peștera dintâi {marker}"),
            Cave(second, $"Peștera a doua {marker}"),
            Entrance(entranceId, first, OpenLon, OpenLat, altitude: 800));
        Row(created, entranceId).GetProperty("status").GetString().ShouldBe("created");
        var revision = Row(created, entranceId).GetProperty("revision").GetDateTimeOffset();

        var moved = await UploadAsync(owner, set, Guid.CreateVersion7(), new
        {
            id = entranceId,
            kind = "caveEntrance",
            parentId = second,
            baseRevision = revision,
            deleted = false,
            name = "Intrare redenumită",
            entranceTypeCode = "excavated",
            isMain = true,
            geometry = new { type = "Point", coordinates = new[] { OpenLon, OpenLat } },
            altitude = 800.0,
            positionQuality = "Unknown",
        });

        // Not refused, and not a conflict: the fields a device owns on an existing row were
        // written, and the answer says so.
        var answered = Row(moved, entranceId);
        answered.GetProperty("status").GetString().ShouldBe("updated");
        (answered.TryGetProperty("code", out var code) && code.ValueKind is not JsonValueKind.Null)
            .ShouldBeFalse("an update carries no code, so nothing tells a device the move was dropped");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entrance = await db.CaveEntrances.AsNoTracking().SingleAsync(e => e.Id == entranceId);

        // The two inert fields. If either of these ever starts being applied, the catalogue of
        // refusals is wrong in the other direction and has to say so.
        entrance.CaveFeatureId.ShouldBe(first);
        entrance.EntranceTypeId.ShouldBe(entranceTypeId);

        // The rename did land, which is what makes `updated` an honest answer rather than a
        // no-op reported as a success.
        (await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId)).Name
            .ShouldBe("Intrare redenumită");
    }

    private static object Cave(
        Guid id,
        string name,
        DateTimeOffset? baseRevision = null,
        bool deleted = false,
        DateTimeOffset? clientUpdatedAt = null) => new
        {
            id,
            kind = "cave",
            baseRevision,
            deleted,
            name,
            caveTypeCode = "cave",
            isMain = false,
            clientUpdatedAt,
        };

    private static object Entrance(
        Guid id,
        Guid caveId,
        double lon,
        double lat,
        double altitude,
        DateTimeOffset? baseRevision = null,
        string name = "Intrare") => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude,
        positionQuality = "Unknown",
    };

    private static JsonElement Row(JsonElement answer, Guid id) =>
        answer.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == id);

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, Guid syncSet, Guid batchId, params object[] rows)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId,
            contractVersion = 1,
            rows,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateSetAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name, bool? locationProtected = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,

            // A protected cave is only worth testing against somebody who can reach it at all,
            // so the ones this fixture guards are readable to any account and guarded by their
            // protection rather than by being hidden.
            visibility = locationProtected is null ? "private" : "authenticated",
            locationProtected = locationProtected ?? false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>An entrance of the owner's cave, carrying a real position and a real quality.</summary>
    private async Task<Guid> AddEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat, 812.0 } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Gives the viewer the named rights on one row and nothing else. Exact view is never among
    /// them: this fixture's whole subject is an account that may edit a row it may not place.
    /// </summary>
    private async Task GrantAsync(Guid featureId, AccessAction actions, string scopeKind = "object")
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = viewerId,
                    effect = "allow",
                    actions = actions.ToString().Replace(" ", string.Empty),
                    scopeKind,
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<(Point Point, decimal? Altitude, PositionQuality Quality)> EntranceStateAsync(Guid entranceId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId);
        var entrance = await db.CaveEntrances.AsNoTracking().SingleAsync(e => e.Id == entranceId);
        return ((Point)feature.Geom!, entrance.Altitude, entrance.PositionQuality);
    }

    private async Task<Geometry?> GeometryOrNullAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId)).Geom;
    }

    private async Task<Geometry> GeometryAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId)).Geom!;
    }

    private async Task<DateTimeOffset> RevisionAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId)).UpdatedAt;
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        factory.Dispose();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
