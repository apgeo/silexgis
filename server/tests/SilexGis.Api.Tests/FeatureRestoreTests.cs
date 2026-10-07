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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The list of deleted caves, entrances and surface features and putting one back: what counts as
/// one deletion, who is shown it, who is refused how, and what comes back.
/// </summary>
/// <remarks>
/// <para>
/// A deletion stamps a feature and everything contained in it with one moment, and that moment is
/// all that says what went together. So the tests here delete in two steps wherever the order
/// matters — something inside first, its container after — because a restore that brought back
/// "everything under it" would pass every test that deleted once.
/// </para>
/// <para>
/// Four accounts hold four different answers to "may you undo this", and each refusal is shown
/// beside an account for which the same request succeeds, so that none passes for a reason other
/// than the one under test.
/// </para>
/// </remarks>
public sealed class FeatureRestoreTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;     // Editor; creates and deletes
    private HttpClient reader = null!;    // Viewer; reads what its audience admits, deletes nothing
    private HttpClient deputy = null!;    // Viewer handed rights on single features
    private HttpClient admin = null!;
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private Guid deputyId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;
    private long sinkholeTypeId;

    public FeatureRestoreTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"frs-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"frs-rdr-{suffix}@t.local");
        deputyId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"frs-dep-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"frs-adm-{suffix}@t.local");

        owner = await AuthHelper.BearerClientAsync(factory, $"frs-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"frs-rdr-{suffix}@t.local");
        deputy = await AuthHelper.BearerClientAsync(factory, $"frs-dep-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"frs-adm-{suffix}@t.local");
        anonymous = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        reader.Dispose();
        deputy.Dispose();
        admin.Dispose();
        anonymous.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task The_deleted_list_holds_one_row_per_deletion_the_caller_may_undo()
    {
        var open = await CreateCaveAsync("Open to everybody", "authenticated");
        await CreateEntranceAsync(open, 25.501, 45.601, isMain: true);
        await CreateEntranceAsync(open, 25.502, 45.602, isMain: false);
        var shut = await CreateCaveAsync("Private to its owner", "private");
        var handed = await CreateSinkholeAsync("Handed to a deputy", "private");
        await GrantAsync(handed, deputyId, AccessAction.Read | AccessAction.Delete);
        var live = await CreateCaveAsync("Never deleted", "authenticated");

        // The reader really can open the open one while it is live. Without this the empty list
        // below would be the list of somebody who could never see any of them.
        (await reader.GetAsync($"/api/v1/caves/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await DeleteAsync(owner, $"/api/v1/caves/{open}");
        await DeleteAsync(owner, $"/api/v1/caves/{shut}");
        await DeleteAsync(owner, $"/api/v1/features/{handed}");

        // The owner: three deletions, most recent first — not five rows, though five were
        // stamped: the two entrances went with their cave and are counted on its row.
        var ours = new[] { open, shut, handed };
        var mine = (await DeletedAsync(owner)).Where(f => ours.Contains(f.Id)).ToList();
        mine.Select(f => f.Id).ShouldBe([handed, shut, open]);
        (await DeletedAsync(owner)).ShouldNotContain(f => f.Id == live);

        var cave = mine.Single(f => f.Id == open);
        cave.Kind.ShouldBe("cave");
        cave.Name.ShouldStartWith("Open to everybody");
        cave.EntranceCount.ShouldBe(2);
        cave.OtherCount.ShouldBe(0);
        cave.DeletedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-10));
        var sinkhole = mine.Single(f => f.Id == handed);
        sinkhole.Kind.ShouldBe("generic");
        sinkhole.FeatureTypeCode.ShouldBe("sinkhole");
        sinkhole.EntranceCount.ShouldBe(0);

        // The reader could read one of them and could delete none: nothing is theirs to put
        // back, so nothing is listed — not even the one they used to open.
        (await DeletedAsync(reader)).ShouldBeEmpty();

        // The deputy holds read and delete on exactly one, by a rule on the feature that the
        // delete left standing.
        (await DeletedAsync(deputy)).Select(f => f.Id).Where(id => ours.Contains(id)).ShouldBe([handed]);

        // A full administrator may delete anything, and so may restore anything — which, in a
        // database the tests of this class share, includes what the others left deleted.
        (await DeletedAsync(admin)).Select(f => f.Id).Where(id => ours.Contains(id)).ShouldBe([handed, shut, open]);

        // Paged, with a total that counts what this caller may put back rather than what exists.
        var all = await DeletedAsync(owner);
        var page = await JsonAsync(owner, "/api/v1/features/deleted?page=2&pageSize=1");
        page.GetProperty("totalItems").GetInt32().ShouldBe(all.Count);
        page.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(all[1].Id);

        // Nobody without an account is told anything.
        (await anonymous.GetAsync("/api/v1/features/deleted")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cave_comes_back_with_what_was_deleted_with_it_and_not_with_what_went_before()
    {
        var cave = await CreateCaveAsync("Two deletions", "authenticated");
        var stays = await CreateEntranceAsync(cave, 25.511, 45.611, isMain: true);
        var wentEarlier = await CreateEntranceAsync(cave, 25.512, 45.612, isMain: false);

        await DeleteAsync(owner, $"/api/v1/cave-entrances/{wentEarlier}");
        await DeleteAsync(owner, $"/api/v1/caves/{cave}");
        var writtenBefore = await UpdatedAtAsync(cave);

        // While the cave is deleted, the entrance deleted before it is not offered: restoring it
        // would stand it inside a cave nobody can see. The cave's row counts the one entrance
        // that went with it, not the one that had already gone.
        var listed = await DeletedAsync(owner);
        listed.ShouldNotContain(f => f.Id == wentEarlier);
        listed.Single(f => f.Id == cave).EntranceCount.ShouldBe(1);

        var restored = await RestoreAsync(owner, cave);
        var body = await restored.Content.ReadAsStringAsync();
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        // Answered as the feature's own address answers it, with the version the restore
        // produced filed against that address.
        var envelope = JsonDocument.Parse(body).RootElement;
        envelope.GetProperty("kind").GetString().ShouldBe("cave");
        envelope.GetProperty("feature").GetProperty("id").GetGuid().ShouldBe(cave);
        envelope.GetProperty("cave").GetProperty("entranceCount").GetInt32().ShouldBe(1);
        restored.Headers.ETag.ShouldNotBeNull();
        restored.Content.Headers.ContentLocation!.ToString().ShouldBe($"/api/v1/features/{cave}");
        var read = await owner.GetAsync($"/api/v1/features/{cave}");
        read.Headers.ETag.ShouldBe(restored.Headers.ETag);

        // The cave and the entrance that went with it are back; the earlier one is not.
        (await EntranceIdsAsync(cave)).ShouldBe([stays]);
        (await owner.GetAsync($"/api/v1/features/{wentEarlier}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A device that keeps a copy was sent the removal and reads on from the last change it
        // holds, so what comes back has to count as changed now.
        (await UpdatedAtAsync(cave)).ShouldBeGreaterThan(writtenBefore);

        // Now that its cave stands, the earlier deletion is one that can be undone by itself.
        var offered = (await DeletedAsync(owner)).Single(f => f.Id == wentEarlier);
        offered.Kind.ShouldBe("caveEntrance");
        offered.Parents.Select(p => p.Id).ShouldBe([cave]);
        (await RestoreAsync(owner, wentEarlier)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EntranceIdsAsync(cave)).ShouldBe([stays, wentEarlier], ignoreOrder: true);
        (await JsonAsync(owner, $"/api/v1/caves/{cave}/summary")).GetProperty("entranceCount").GetInt32().ShouldBe(2);

        // Neither is on the list any more, and everything the write service keeps in step —
        // the entrance count, the cave's point, the ancestry — still is.
        (await DeletedAsync(owner)).ShouldNotContain(f => f.Id == cave || f.Id == wentEarlier);
        (await IntegrityProblemsAsync(cave, stays, wentEarlier)).ShouldBeEmpty();
    }

    /// <summary>
    /// Deleting a cave's main entrance hands the role on and leaves the flag on the row that went.
    /// Back among the others that row must not make two mains of one cave.
    /// </summary>
    [Fact]
    public async Task A_restored_main_entrance_does_not_take_the_role_back_from_the_one_that_took_over()
    {
        var cave = await CreateCaveAsync("One main", "authenticated");
        var first = await CreateEntranceAsync(cave, 25.521, 45.621, isMain: true);
        var second = await CreateEntranceAsync(cave, 25.522, 45.622, isMain: false);

        await DeleteAsync(owner, $"/api/v1/cave-entrances/{first}");
        (await MainEntranceIdsAsync(cave)).ShouldBe([second]);

        (await RestoreAsync(owner, first)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await EntranceIdsAsync(cave)).ShouldBe([first, second], ignoreOrder: true);
        (await MainEntranceIdsAsync(cave)).ShouldBe([second]);
        // The cave is still drawn where it has been since: at the entrance that took over.
        (await JsonAsync(owner, $"/api/v1/caves/{cave}"))
            .GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(25.522, 1e-9);
        (await IntegrityProblemsAsync(cave, first, second)).ShouldBeEmpty();

        // An only entrance keeps the role it had: there is nobody to have taken it over.
        var lone = await CreateCaveAsync("Only entrance", "authenticated");
        var only = await CreateEntranceAsync(lone, 25.523, 45.623, isMain: true);
        await DeleteAsync(owner, $"/api/v1/cave-entrances/{only}");
        (await JsonAsync(owner, $"/api/v1/caves/{lone}/summary")).GetProperty("entranceCount").GetInt32().ShouldBe(0);
        (await RestoreAsync(owner, only)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MainEntranceIdsAsync(lone)).ShouldBe([only]);
        (await IntegrityProblemsAsync(lone, only)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Restoring_inside_something_deleted_is_refused_and_says_what_to_restore_first()
    {
        var area = await CreateAreaAsync("Deleted whole", "authenticated");
        var cave = await CreateCaveAsync("Inside the area", "private", parentId: area);
        var entrance = await CreateEntranceAsync(cave, 25.531, 45.631, isMain: true);
        // The deputy may read and delete the cave by a rule on the cave, and reads the area by
        // its audience — so the area can be named to them.
        await GrantAsync(cave, deputyId, AccessAction.Read | AccessAction.Delete);

        await DeleteAsync(owner, $"/api/v1/features/{area}");

        // One deletion, counted: the area's row says a cave and an entrance went with it, and
        // neither of those is a row of its own.
        var listed = await DeletedAsync(owner);
        var row = listed.Single(f => f.Id == area);
        row.OtherCount.ShouldBe(1);
        row.EntranceCount.ShouldBe(1);
        listed.ShouldNotContain(f => f.Id == cave || f.Id == entrance);
        (await DeletedAsync(deputy)).ShouldNotContain(f => f.Id == cave);

        // The cave and the entrance are both refused, and both are pointed at the area — the
        // outermost thing in the way, since the cave between would be refused in its turn.
        foreach (var inside in new[] { cave, entrance })
        {
            var refused = await RestoreAsync(owner, inside);
            var problem = await ProblemAsync(refused, HttpStatusCode.Conflict, FeatureDeletionRules.ContainerDeletedCode);
            problem.GetProperty("restoreFirst").GetProperty("id").GetGuid().ShouldBe(area);
            problem.GetProperty("restoreFirst").GetProperty("name").GetString()!.ShouldStartWith("Deleted whole");
        }

        var forDeputy = await ProblemAsync(
            await RestoreAsync(deputy, cave), HttpStatusCode.Conflict, FeatureDeletionRules.ContainerDeletedCode);
        forDeputy.GetProperty("restoreFirst").GetProperty("id").GetGuid().ShouldBe(area);

        // Nothing came back by being refused.
        (await owner.GetAsync($"/api/v1/features/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await RestoreAsync(owner, area)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync($"/api/v1/features/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EntranceIdsAsync(cave)).ShouldBe([entrance]);
        (await IntegrityProblemsAsync(area, cave, entrance)).ShouldBeEmpty();
    }

    /// <summary>
    /// A container the caller could not read is in the way all the same, and is not named to them
    /// for being so.
    /// </summary>
    [Fact]
    public async Task A_container_the_caller_could_not_read_is_in_the_way_without_being_named()
    {
        var area = await CreateAreaAsync("Nobody else's area", "private");
        var cave = await CreateCaveAsync("Handed out of a private area", "private", parentId: area);
        await GrantAsync(cave, deputyId, AccessAction.Read | AccessAction.Delete);
        (await deputy.GetAsync($"/api/v1/features/{area}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await deputy.GetAsync($"/api/v1/features/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await DeleteAsync(owner, $"/api/v1/features/{area}");

        (await DeletedAsync(deputy)).ShouldNotContain(f => f.Id == cave || f.Id == area);
        var problem = await ProblemAsync(
            await RestoreAsync(deputy, cave), HttpStatusCode.Conflict, FeatureDeletionRules.ContainerDeletedCode);
        problem.GetProperty("restoreFirst").ValueKind.ShouldBe(JsonValueKind.Null);
        var body = problem.GetRawText();
        body.ShouldNotContain(area.ToString());
        body.ShouldNotContain("Nobody else's area");
    }

    [Fact]
    public async Task The_restore_ladder_answers_missing_unreadable_forbidden_and_not_deleted_apart()
    {
        var open = await CreateCaveAsync("Readable, not deletable", "authenticated");
        var shut = await CreateCaveAsync("Not readable", "private");
        var live = await CreateCaveAsync("Still here", "authenticated");
        var handed = await CreateCaveAsync("Handed over", "private");
        await GrantAsync(handed, deputyId, AccessAction.Read | AccessAction.Delete);
        var readOnly = await CreateCaveAsync("Read by grant only", "private");
        await GrantAsync(readOnly, deputyId, AccessAction.Read);

        foreach (var cave in new[] { open, shut, handed, readOnly })
        {
            await DeleteAsync(owner, $"/api/v1/caves/{cave}");
        }

        // No such feature, and one the caller could never read: the same answer, deleted or not.
        await ProblemAsync(await RestoreAsync(owner, Guid.NewGuid()), HttpStatusCode.NotFound, "feature.not_found");
        await ProblemAsync(await RestoreAsync(reader, shut), HttpStatusCode.NotFound, "feature.not_found");
        await ProblemAsync(await RestoreAsync(deputy, shut), HttpStatusCode.NotFound, "feature.not_found");

        // Readable but not theirs to delete: a refusal, deleted or live.
        (await RestoreAsync(reader, open)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RestoreAsync(deputy, readOnly)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RestoreAsync(reader, live)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Theirs to delete and not deleted: there is nothing to restore.
        await ProblemAsync(await RestoreAsync(owner, live), HttpStatusCode.Conflict, FeatureDeletionRules.NotDeletedCode);

        // Nobody without an account.
        (await RestoreAsync(anonymous, open)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the same request from somebody who may: by a rule on the feature, by ownership,
        // and — a second time — the conflict of something already back.
        (await RestoreAsync(deputy, handed)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RestoreAsync(owner, open)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProblemAsync(await RestoreAsync(owner, open), HttpStatusCode.Conflict, FeatureDeletionRules.NotDeletedCode);
        (await RestoreAsync(admin, shut)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// An entrance is deleted from its cave's page by whoever may edit the cave, without holding
    /// any right to delete on the entrance itself. The door that deleted it restores it.
    /// </summary>
    [Fact]
    public async Task Whoever_may_edit_a_cave_restores_the_entrance_they_could_delete_from_it()
    {
        var cave = await CreateCaveAsync("Edited by a deputy", "authenticated");
        var entrance = await CreateEntranceAsync(cave, 25.541, 45.641, isMain: true);
        await GrantAsync(cave, deputyId, AccessAction.Write);

        await DeleteAsync(deputy, $"/api/v1/cave-entrances/{entrance}");

        var offered = (await DeletedAsync(deputy)).Single(f => f.Id == entrance);
        offered.Parents.Select(p => p.Id).ShouldBe([cave]);
        // The reader reads the same cave and may edit nothing in it.
        (await DeletedAsync(reader)).ShouldBeEmpty();
        (await RestoreAsync(reader, entrance)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await RestoreAsync(deputy, entrance)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EntranceIdsAsync(cave)).ShouldBe([entrance]);
    }

    /// <summary>
    /// A protected cave's position is no more visible in the list of deleted features, or in the
    /// answer to restoring it, than on its own page.
    /// </summary>
    [Fact]
    public async Task Neither_the_list_nor_the_restore_says_where_a_protected_cave_is()
    {
        const double exactLon = 25.4567891;
        const double exactLat = 45.6543219;
        var cave = await CreateCaveAsync("Protected and handed over", "authenticated", locationProtected: true);
        var entrance = await CreateEntranceAsync(cave, exactLon, exactLat, isMain: true);
        // Read and delete, and not the right to place it.
        await GrantAsync(cave, deputyId, AccessAction.Read | AccessAction.Delete);

        var before = await JsonAsync(deputy, $"/api/v1/features/{cave}");
        before.GetProperty("feature").GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        var exactForOwner = await JsonAsync(owner, $"/api/v1/features/{cave}");
        exactForOwner.GetProperty("feature").GetProperty("geometry").GetProperty("coordinates")[0]
            .GetDouble().ShouldBe(exactLon, 1e-9);

        await DeleteAsync(owner, $"/api/v1/caves/{cave}");

        // The list: the deputy's row, the owner's row, and no position on either — the list
        // holds none to give.
        foreach (var client in new[] { deputy, owner })
        {
            var response = await client.GetAsync("/api/v1/features/deleted?pageSize=500");
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
            text.ShouldContain(cave.ToString());
            text.ShouldNotContain("coordinates");
            text.ShouldNotContain("geom");
            text.ShouldNotContain("25.45");
            text.ShouldNotContain("45.65");
        }

        // The restore answers the deputy what the cave's own address answered them before.
        var restored = await RestoreAsync(deputy, cave);
        var body = await restored.Content.ReadAsStringAsync();
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain("25.4567891");
        body.ShouldNotContain("45.6543219");
        var answered = JsonDocument.Parse(body).RootElement.GetProperty("feature");
        answered.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        answered.GetProperty("geometry").GetRawText()
            .ShouldBe(before.GetProperty("feature").GetProperty("geometry").GetRawText());

        // And the entrance, restored on its own by the same deputy, the same way.
        await DeleteAsync(owner, $"/api/v1/cave-entrances/{entrance}");
        await GrantAsync(entrance, deputyId, AccessAction.Read | AccessAction.Delete);
        var entranceBack = await RestoreAsync(deputy, entrance);
        var entranceBody = await entranceBack.Content.ReadAsStringAsync();
        entranceBack.StatusCode.ShouldBe(HttpStatusCode.OK, entranceBody);
        entranceBody.ShouldNotContain("25.4567891");
        entranceBody.ShouldNotContain("45.6543219");
        JsonDocument.Parse(entranceBody).RootElement.GetProperty("feature")
            .GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// What went with a deletion is counted over what the caller could read, so the number says no
    /// more than the container's own page did before.
    /// </summary>
    [Fact]
    public async Task The_count_of_what_went_with_a_deletion_leaves_out_what_the_caller_could_not_read()
    {
        var area = await CreateAreaAsync("Counted for two readers", "authenticated");
        var seen = await CreateCaveAsync("Seen by the deputy", "private", parentId: area);
        var hidden = await CreateCaveAsync("Kept from the deputy", "private", parentId: area);
        await CreateEntranceAsync(hidden, 25.551, 45.651, isMain: true);
        await GrantAsync(area, deputyId, AccessAction.Read | AccessAction.Delete);
        // Over the cave and what is in it: a rule on the cave alone would leave its entrance
        // readable through the area's audience.
        await DenyReadBelowAsync(hidden, deputyId);
        (await deputy.GetAsync($"/api/v1/features/{seen}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await deputy.GetAsync($"/api/v1/features/{hidden}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await DeleteAsync(owner, $"/api/v1/features/{area}");

        var forOwner = (await DeletedAsync(owner)).Single(f => f.Id == area);
        forOwner.OtherCount.ShouldBe(2);
        forOwner.EntranceCount.ShouldBe(1);

        var forDeputy = (await DeletedAsync(deputy)).Single(f => f.Id == area);
        forDeputy.OtherCount.ShouldBe(1);
        forDeputy.EntranceCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_trip_names_its_cave_again_once_the_cave_is_restored()
    {
        var kept = await CreateCaveAsync("Stays", "authenticated");
        var goes = await CreateCaveAsync("Deleted and restored", "authenticated");
        var trip = await CreateTripAsync("A trip to both", [kept, goes]);
        (await TripCaveIdsAsync(trip)).ShouldBe([kept, goes], ignoreOrder: true);

        await DeleteAsync(owner, $"/api/v1/caves/{goes}");
        (await TripCaveIdsAsync(trip)).ShouldBe([kept]);

        (await RestoreAsync(owner, goes)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TripCaveIdsAsync(trip)).ShouldBe([kept, goes], ignoreOrder: true);
    }

    /// <summary>
    /// Nothing about a feature is unique across features — not its name, not a register number —
    /// so a cave restored after its name was given to another stands beside its namesake.
    /// </summary>
    [Fact]
    public async Task A_cave_restored_after_its_name_was_reused_stands_beside_its_namesake()
    {
        var name = $"Namesake {suffix} {Guid.NewGuid():N}"[..40];
        var first = await CreateCaveNamedAsync(name);
        await DeleteAsync(owner, $"/api/v1/caves/{first}");
        var second = await CreateCaveNamedAsync(name);

        (await RestoreAsync(owner, first)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var found = await JsonAsync(owner, $"/api/v1/caves?search={Uri.EscapeDataString(name)}&pageSize=50");
        found.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .ShouldBe([first, second], ignoreOrder: true);
    }

    /// <summary>
    /// A survey line returns with its cave when the two went together, and is not put back alone.
    /// </summary>
    [Fact]
    public async Task A_survey_line_comes_back_with_its_cave_and_is_not_restored_alone()
    {
        var cave = await CreateCaveAsync("With a survey line", "authenticated");
        var line = await CreateCenterlineAsync(cave);
        var retired = await CreateCenterlineAsync(cave);

        await DeleteAsync(owner, $"/api/v1/centerlines/{retired}");
        (await DeletedAsync(owner)).ShouldNotContain(f => f.Id == retired);
        await ProblemAsync(
            await RestoreAsync(owner, retired), HttpStatusCode.Conflict, FeatureDeletionRules.KindNotRestorableCode);

        await DeleteAsync(owner, $"/api/v1/caves/{cave}");
        (await DeletedAsync(owner)).Single(f => f.Id == cave).OtherCount.ShouldBe(0);
        (await RestoreAsync(owner, cave)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lines = await JsonAsync(owner, $"/api/v1/caves/{cave}/centerlines");
        lines.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldBe([line]);
        (await IntegrityProblemsAsync(cave, line)).ShouldBeEmpty();
    }

    /// <summary>
    /// The state a restore is refused in order to prevent, written by hand, is the state the
    /// integrity check reports: an entrance that is not deleted inside a cave that is.
    /// </summary>
    [Fact]
    public async Task The_integrity_check_reports_a_feature_left_standing_inside_a_deleted_one()
    {
        var cave = await CreateCaveAsync("Deleted around its entrance", "authenticated");
        var entrance = await CreateEntranceAsync(cave, 25.561, 45.661, isMain: true);
        await DeleteAsync(owner, $"/api/v1/caves/{cave}");
        (await IntegrityProblemsAsync(cave, entrance)).ShouldBeEmpty();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.IgnoreQueryFilters()
                .Where(f => f.Id == entrance)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, (DateTimeOffset?)null));
        }

        var problem = (await IntegrityProblemsAsync(cave, entrance)).ShouldHaveSingleItem();
        problem.Check.ShouldBe("live_under_deleted");
        problem.FeatureId.ShouldBe(entrance);

        // Put right the way a person would: the cave comes back, and the row is consistent again.
        (await RestoreAsync(owner, cave)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IntegrityProblemsAsync(cave, entrance)).ShouldBeEmpty();
    }

    // ---- fixtures

    private sealed record Crumb(Guid Id, string? Name);

    private sealed record DeletedFeature(
        Guid Id,
        string Kind,
        string? FeatureTypeCode,
        string? Name,
        DateTimeOffset DeletedAt,
        int EntranceCount,
        int OtherCount,
        List<Crumb> Parents);

    private static async Task<List<DeletedFeature>> DeletedAsync(HttpClient client)
    {
        var page = await JsonAsync(client, "/api/v1/features/deleted?pageSize=500");
        var items = page.GetProperty("items").Deserialize<List<DeletedFeature>>(Web)!;
        page.GetProperty("totalItems").GetInt32().ShouldBe(items.Count);
        return items;
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid feature) =>
        client.PostAsync($"/api/v1/features/{feature}/restore", null);

    private static async Task DeleteAsync(HttpClient client, string url)
    {
        var deleted = await client.DeleteAsync(url);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"{url}: {await deleted.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        var problem = JsonDocument.Parse(body).RootElement.Clone();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }

    private Task<Guid> CreateCaveAsync(
        string name, string visibility, Guid? parentId = null, bool locationProtected = false) =>
        CreateCaveNamedAsync(
            $"{name} {suffix} {Guid.NewGuid():N}"[..Math.Min(80, name.Length + 42)], visibility, parentId, locationProtected);

    private async Task<Guid> CreateCaveNamedAsync(
        string name, string visibility = "authenticated", Guid? parentId = null, bool locationProtected = false)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            locationProtected,
            parentId,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateEntranceAsync(Guid cave, double lon, double lat, bool isMain)
    {
        var created = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            entranceTypeId,
            isMain,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "gps",
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateAreaAsync(string name, string visibility)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {suffix} {Guid.NewGuid():N}"[..Math.Min(80, name.Length + 42)],
            featureTypeId = karstAreaTypeId,
            visibility,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateSinkholeAsync(string name, string visibility)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {suffix} {Guid.NewGuid():N}"[..Math.Min(80, name.Length + 42)],
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.56, 45.66 } },
            visibility,
        });
        return await CreatedIdAsync(created);
    }

    /// <summary>
    /// A survey line of the cave, written through the service that keeps a cave's lines in step:
    /// what is under test is what a delete and a restore do to it, not the upload that draws one.
    /// </summary>
    private async Task<Guid> CreateCenterlineAsync(Guid cave)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var line = new LineString([new Coordinate(25.57, 45.67), new Coordinate(25.58, 45.68)]) { SRID = 4326 };
        var feature = await writer.CreateCenterlineAsync(
            new Feature
            {
                Name = $"Line {Guid.NewGuid():N}",
                Geom = new MultiLineString([line]) { SRID = 4326 },
                OwnerUserId = ownerId,
            },
            new Centerline { CaveFeatureId = cave, PathCount = 1 });
        await db.SaveChangesAsync();
        return feature.Id;
    }

    private async Task<Guid> CreateTripAsync(string title, Guid[] caveIds)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {suffix} {Guid.NewGuid():N}"[..Math.Min(80, title.Length + 42)],
            tripDate = "2026-07-20",
            caveIds,
            participants = Array.Empty<object>(),
            visibility = "private",
            hadIncident = false,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<List<Guid>> TripCaveIdsAsync(Guid trip) =>
        [.. (await JsonAsync(owner, $"/api/v1/trip-logs/{trip}")).GetProperty("caveIds")
            .EnumerateArray().Select(id => id.GetGuid())];

    private async Task<List<Guid>> EntranceIdsAsync(Guid cave) =>
        [.. (await JsonAsync(owner, $"/api/v1/caves/{cave}/entrances"))
            .EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];

    private async Task<List<Guid>> MainEntranceIdsAsync(Guid cave) =>
        [.. (await JsonAsync(owner, $"/api/v1/caves/{cave}/entrances"))
            .EnumerateArray()
            .Where(e => e.GetProperty("isMain").GetBoolean())
            .Select(e => e.GetProperty("id").GetGuid())];

    private async Task<DateTimeOffset> UpdatedAtAsync(Guid feature)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.Id == feature).Select(f => f.UpdatedAt).SingleAsync();
    }

    /// <summary>
    /// What the integrity check reports about these features. The check reads the whole database,
    /// which the tests of this class share, so only the rows a test made are its to answer for.
    /// </summary>
    private async Task<List<IntegrityProblem>> IntegrityProblemsAsync(params Guid[] features)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var verifier = scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>();
        return [.. (await verifier.VerifyAsync()).Where(p => features.Contains(p.FeatureId))];
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    /// A rule on one feature, written straight to the table: what is under test is that the row
    /// survives a delete and still decides, not the route that authors it.
    /// </summary>
    private Task GrantAsync(Guid feature, Guid userId, AccessAction actions) =>
        EntryAsync(feature, userId, actions, AccessEffect.Allow, AccessScopeKind.Object);

    private Task DenyReadBelowAsync(Guid feature, Guid userId) =>
        EntryAsync(feature, userId, AccessAction.Read, AccessEffect.Deny, AccessScopeKind.Subtree);

    private async Task EntryAsync(
        Guid feature, Guid userId, AccessAction actions, AccessEffect effect, AccessScopeKind reach)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.Features,
            Actions = actions,
            ScopeKind = reach,
            // A rule about a feature is anchored on the feature itself, object or subtree.
            ScopeFeatureId = feature,
        });
        await db.SaveChangesAsync();
    }
}
