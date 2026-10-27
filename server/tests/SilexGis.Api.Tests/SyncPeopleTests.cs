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
/// The people directory a phone attributes its users from, and the upload's <c>people</c>
/// section that records them and their links: who is listed and who is not, that nothing but
/// names leaves, that a link to somebody unlisted is refused, that a link made in the browser
/// outlasts a phone's different choice, the order and the ceiling a mixed upload is applied
/// under, and that undoing an upload takes its links and names back.
/// </summary>
/// <remarks>
/// The uploader is in one caving group with a clubmate; a stranger is on the roster and in no
/// group the uploader shares, and has contact details that must never be answered. The item
/// ceiling is set low so a mixed upload can cross it with a handful of items.
/// </remarks>
public sealed class SyncPeopleTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int Ceiling = 4;

    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private Guid ownerId;
    private Guid ownerCaver;
    private Guid clubmate;
    private Guid secondClubmate;
    private Guid stranger;
    private Guid set;
    private string marker = null!;

    public SyncPeopleTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Sync:UploadRowsMax"] = Ceiling.ToString() });

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ppl-own-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"People club {marker}", Slug = $"ppl-club-{marker}" };
            var other = new CavingGroup { Name = $"Other club {marker}", Slug = $"ppl-other-{marker}" };
            db.CavingGroups.AddRange(club, other);
            var mate = new Caver { FullName = $"Mate {marker}", ShortName = "Mate", Email = $"mate-{marker}@t.local", Phone = "+40700000001", Notes = "roster remark" };
            var mate2 = new Caver { FullName = $"Second mate {marker}" };
            var outsider = new Caver { FullName = $"Stranger {marker}", Email = $"stranger-{marker}@t.local", Phone = "+40700000002" };
            db.Cavers.AddRange(mate, mate2, outsider);
            await db.SaveChangesAsync();
            db.CavingGroupMemberships.AddRange(
                new CavingGroupMembership { CavingGroupId = club.Id, CaverId = mate.Id, Role = CavingGroupRole.Member },
                new CavingGroupMembership { CavingGroupId = club.Id, CaverId = mate2.Id, Role = CavingGroupRole.Member },
                new CavingGroupMembership { CavingGroupId = other.Id, CaverId = outsider.Id, Role = CavingGroupRole.Member });
            await db.SaveChangesAsync();
            ownerCaver = (await RosterHelper.AddMemberAsync(db, club.Id, ownerId)).Id;
            clubmate = mate.Id;
            secondClubmate = mate2.Id;
            stranger = outsider.Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ppl-own-{marker}@t.local");
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
    public async Task The_directory_lists_the_callers_clubmates_and_themselves_and_never_a_stranger_or_a_contact_detail()
    {
        // The caller's own entry is shown under its account's label, which the test host makes up
        // for every account it creates; it is read first so the recording can name it.
        var label = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/sync/people")).GetProperty("people")
            .EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == ownerCaver)
            .GetProperty("fullName").GetString()!;
        var answer = await new ContractFixture()
            .Literal(marker, "marker").Literal(label, "my-label").Name(ownerCaver, "me").Name(clubmate, "clubmate")
            .Name(secondClubmate, "second-clubmate")
            .RecordGetAsync("23-people", owner, "/api/v1/sync/people");
        var payload = answer.GetRawText();
        var people = answer.GetProperty("people").EnumerateArray().ToList();

        var ids = people.Select(p => p.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(clubmate);
        ids.ShouldContain(secondClubmate);
        ids.ShouldContain(ownerCaver);
        ids.ShouldNotContain(stranger);

        var me = people.Single(p => p.GetProperty("id").GetGuid() == ownerCaver);
        me.GetProperty("isMe").GetBoolean().ShouldBeTrue();
        me.GetProperty("hasAccount").GetBoolean().ShouldBeTrue();
        var mate = people.Single(p => p.GetProperty("id").GetGuid() == clubmate);
        mate.GetProperty("isMe").GetBoolean().ShouldBeFalse();
        mate.GetProperty("hasAccount").GetBoolean().ShouldBeFalse();
        mate.GetProperty("shortName").GetString().ShouldBe("Mate");

        // Names only: the members an entry has are exactly these, and no contact value is anywhere.
        foreach (var person in people)
        {
            person.EnumerateObject().Select(p => p.Name).Order()
                .ShouldBe(["fullName", "hasAccount", "id", "isMe", "shortName"]);
        }

        payload.ShouldNotContain($"mate-{marker}@t.local");
        payload.ShouldNotContain("+40700000001");
        payload.ShouldNotContain("roster remark");

        // Announced while it is served, and asked of an account.
        (await FeaturesAsync(owner)).ShouldContain("people");
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/sync/people")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_account_denied_reading_people_is_refused_the_directory_and_not_offered_it()
    {
        var deniedEmail = $"ppl-deny-{marker}@t.local";
        var deniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, deniedEmail);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = deniedId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Cavers,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        using var denied = await AuthHelper.BearerClientAsync(factory, deniedEmail);
        var refused = await denied.GetAsync("/api/v1/sync/people");
        await ShouldBeRefusedAsync(refused, HttpStatusCode.Forbidden, "access.forbidden");
        (await FeaturesAsync(denied)).ShouldNotContain("people");

        // The same route, the same moment, an account that holds the right.
        (await owner.GetAsync("/api/v1/sync/people")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FeaturesAsync(owner)).ShouldContain("people");
    }

    [Fact]
    public async Task A_link_to_a_caver_the_directory_does_not_list_is_refused_and_a_listed_one_lands_beside_it()
    {
        var strangerUser = Guid.CreateVersion7();
        var mateUser = Guid.CreateVersion7();
        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new object[]
            {
                Person(strangerUser, "outsider", stranger),
                Person(mateUser, "mate", clubmate),
            },
        });

        var results = answer.GetProperty("people").EnumerateArray().ToList();
        results[0].GetProperty("id").GetGuid().ShouldBe(strangerUser);
        results[0].GetProperty("status").GetString().ShouldBe("rejected");
        results[0].GetProperty("code").GetString().ShouldBe("sync.person_not_listed");
        results[1].GetProperty("status").GetString().ShouldBe("created");
        answer.GetProperty("written").GetInt32().ShouldBe(1);
        answer.GetProperty("refused").GetInt32().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.DevicePersons.AnyAsync(p => p.DeviceUserId == strangerUser)).ShouldBeFalse();
        var linked = await db.DevicePersons.SingleAsync(p => p.DeviceUserId == mateUser);
        linked.CaverId.ShouldBe(clubmate);
        linked.LinkSource.ShouldBe(DevicePersonLinkSource.Device);
        linked.UploadedByUserId.ShouldBe(ownerId);

        // The directory hands the link back, and only for phone users this account sent.
        var links = await LinksAsync();
        links.ShouldContainKeyAndValue(mateUser, clubmate);
        links.ShouldNotContainKey(strangerUser);
    }

    [Fact]
    public async Task A_link_made_in_the_browser_outlasts_a_phones_different_choice_and_a_phones_own_link_does_not()
    {
        var keptUser = Guid.CreateVersion7();
        var movedUser = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.DevicePersons.AddRange(
                new DevicePerson { DeviceUserId = keptUser, Username = "kept", CaverId = clubmate, LinkSource = DevicePersonLinkSource.Administrator, UploadedByUserId = ownerId },
                new DevicePerson { DeviceUserId = movedUser, Username = "moved", CaverId = clubmate, LinkSource = DevicePersonLinkSource.Device, UploadedByUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var answer = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new object[]
            {
                Person(keptUser, "kept renamed", secondClubmate),
                Person(movedUser, "moved", secondClubmate),
            },
        });

        // Not an error: the names are taken, the link stands, and the directory says what stands.
        var results = answer.GetProperty("people").EnumerateArray().ToList();
        results[0].GetProperty("status").GetString().ShouldBe("updated");
        results[1].GetProperty("status").GetString().ShouldBe("updated");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var kept = await db.DevicePersons.AsNoTracking().SingleAsync(p => p.DeviceUserId == keptUser);
            kept.CaverId.ShouldBe(clubmate);
            kept.LinkSource.ShouldBe(DevicePersonLinkSource.Administrator);
            kept.Username.ShouldBe("kept renamed");
            var moved = await db.DevicePersons.AsNoTracking().SingleAsync(p => p.DeviceUserId == movedUser);
            moved.CaverId.ShouldBe(secondClubmate);
        }

        var links = await LinksAsync();
        links.ShouldContainKeyAndValue(keptUser, clubmate);
        links.ShouldContainKeyAndValue(movedUser, secondClubmate);
    }

    [Fact]
    public async Task A_mixed_upload_applies_rows_before_people_answers_both_and_is_bounded_over_both()
    {
        // One item over the ceiling, spread over the two arrays: refused whole, nothing kept.
        var overBatch = Guid.CreateVersion7();
        var overUser = Guid.CreateVersion7();
        var refused = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = overBatch,
            contractVersion = 1,
            people = new[] { Person(overUser, "over", null), Person(Guid.CreateVersion7(), "over two", null) },
            rows = new[] { Cave(Guid.CreateVersion7(), "over a"), Cave(Guid.CreateVersion7(), "over b"), Cave(Guid.CreateVersion7(), "over c") },
        });
        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "sync.batch_too_large");

        // At the ceiling, people sent first in the body: still applied after the rows.
        var caveId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var request = new
        {
            batchId,
            contractVersion = 1,
            people = new[] { Person(userId, "caver on the phone", ownerCaver) },
            rows = new[] { Cave(caveId, "mixed a"), Cave(Guid.CreateVersion7(), "mixed b"), Cave(Guid.CreateVersion7(), "mixed c") },
        };
        var answer = await UploadAsync(request);
        answer.GetProperty("written").GetInt32().ShouldBe(Ceiling);
        answer.GetProperty("rows").GetArrayLength().ShouldBe(3);
        answer.GetProperty("rows")[0].GetProperty("id").GetGuid().ShouldBe(caveId);
        answer.GetProperty("people").GetArrayLength().ShouldBe(1);
        answer.GetProperty("people")[0].GetProperty("status").GetString().ShouldBe("created");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ImportBatches.IgnoreQueryFilters().CountAsync(b => b.SyncBatchId == overBatch)).ShouldBe(0);
            (await db.DevicePersons.AnyAsync(p => p.DeviceUserId == overUser)).ShouldBeFalse();

            // Lines are recorded in the order things were applied: every row's before the phone user's.
            var importBatchId = answer.GetProperty("importBatchId").GetGuid();
            var lines = await db.ImportBatchItems.AsNoTracking()
                .Where(i => i.ImportBatchId == importBatchId)
                .OrderBy(i => i.Id)
                .Select(i => new { i.FeatureId, i.DevicePersonId })
                .ToListAsync();
            lines.Count.ShouldBe(4);
            lines.Take(3).ShouldAllBe(l => l.FeatureId != null && l.DevicePersonId == null);
            lines[3].DevicePersonId.ShouldBe(userId);
        }

        // A resend answers both arrays from the record and writes nothing.
        var resent = await UploadAsync(request);
        resent.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        resent.GetProperty("people")[0].GetProperty("status").GetString().ShouldBe("created");
        resent.GetProperty("rows").GetArrayLength().ShouldBe(3);

        // An upload of rows alone is answered without the people array at all.
        var rowsOnly = await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, rows = new[] { Cave(Guid.CreateVersion7(), "alone") } });
        rowsOnly.TryGetProperty("people", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Undoing_uploads_takes_back_the_names_and_links_they_made_and_leaves_what_was_changed_since()
    {
        var userId = Guid.CreateVersion7();
        var first = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { Person(userId, "first name", clubmate) },
        });
        var second = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { Person(userId, "second name", secondClubmate) },
        });
        second.GetProperty("people")[0].GetProperty("status").GetString().ShouldBe("updated");

        // The second upload taken back: its name and its link go back to the first's.
        var undoSecond = await RevertAsync(second.GetProperty("importBatchId").GetGuid());
        var line = undoSecond.GetProperty("items")[0];
        line.GetProperty("devicePersonId").GetGuid().ShouldBe(userId);
        line.GetProperty("outcome").GetString().ShouldBe("undone");
        line.GetProperty("change").GetString().ShouldBe("updated");
        var person = await PersonAsync(userId);
        person.Username.ShouldBe("first name");
        person.CaverId.ShouldBe(clubmate);

        // Somebody keeping the roster relinks the phone user; undoing the first upload leaves that
        // link, and says so, while a phone user nobody touched since loses the upload's link.
        var untouched = Guid.CreateVersion7();
        var third = await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { Person(untouched, "untouched", clubmate) },
        });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var relinked = await db.DevicePersons.SingleAsync(p => p.DeviceUserId == userId);
            relinked.CaverId = ownerCaver;
            relinked.LinkSource = DevicePersonLinkSource.Administrator;
            await db.SaveChangesAsync();
        }

        var undoFirst = await RevertAsync(first.GetProperty("importBatchId").GetGuid());
        var kept = undoFirst.GetProperty("items")[0];
        kept.GetProperty("outcome").GetString().ShouldBe("not_undone");
        kept.GetProperty("reason").GetString().ShouldBe("superseded");
        (await PersonAsync(userId)).CaverId.ShouldBe(ownerCaver);

        var undoThird = await RevertAsync(third.GetProperty("importBatchId").GetGuid());
        undoThird.GetProperty("items")[0].GetProperty("outcome").GetString().ShouldBe("undone");
        undoThird.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.String);
        var unlinked = await PersonAsync(untouched);
        unlinked.CaverId.ShouldBeNull();
        unlinked.Username.ShouldBe("untouched");
    }

    /// <summary>
    /// A phone moving its own link from one caver to another leaves who made the link as it was,
    /// so the history of that upload says nothing about the source. Undoing it must still treat
    /// the source as part of the link: a link somebody has since confirmed in the browser is
    /// theirs and stays, and a forced undo that puts the phone's earlier caver back puts it back
    /// as the phone's, not under the authority of whoever relinked the row since.
    /// </summary>
    [Fact]
    public async Task Undoing_a_phones_relink_respects_a_link_confirmed_since_and_a_forced_undo_restores_it_as_the_phones()
    {
        // Confirmed since, same caver: not undone, the confirmed link stands.
        var confirmedUser = Guid.CreateVersion7();
        await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, people = new[] { Person(confirmedUser, "confirmed", clubmate) } });
        var relink = await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, people = new[] { Person(confirmedUser, "confirmed", secondClubmate) } });
        await SetLinkAsync(confirmedUser, secondClubmate, DevicePersonLinkSource.Administrator);

        var undo = await RevertAsync(relink.GetProperty("importBatchId").GetGuid());
        var line = undo.GetProperty("items")[0];
        line.GetProperty("outcome").GetString().ShouldBe("not_undone");
        line.GetProperty("reason").GetString().ShouldBe("superseded");
        var confirmed = await PersonAsync(confirmedUser);
        confirmed.CaverId.ShouldBe(secondClubmate);
        confirmed.LinkSource.ShouldBe(DevicePersonLinkSource.Administrator);

        // Relinked since by an import to somebody else, then a forced undo: the phone's earlier
        // caver comes back, and as the phone's.
        var forcedUser = Guid.CreateVersion7();
        await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, people = new[] { Person(forcedUser, "forced", clubmate) } });
        var moved = await UploadAsync(new { batchId = Guid.CreateVersion7(), contractVersion = 1, people = new[] { Person(forcedUser, "forced", secondClubmate) } });
        await SetLinkAsync(forcedUser, ownerCaver, DevicePersonLinkSource.ArchiveImport);

        var forced = await RevertAsync(moved.GetProperty("importBatchId").GetGuid(), new { force = true });
        forced.GetProperty("items")[0].GetProperty("outcome").GetString().ShouldBe("undone");
        var restored = await PersonAsync(forcedUser);
        restored.CaverId.ShouldBe(clubmate);
        restored.LinkSource.ShouldBe(DevicePersonLinkSource.Device);
    }

    /// <summary>
    /// The archive import writes its reviewer's choices back as links, and a review can be older
    /// than the roster: a caver chosen then may have been merged away or deleted since. Such a
    /// choice is dropped rather than recorded, because a link to a missing caver fails the whole
    /// save on its foreign key — and with it every position the confirmation was recording.
    /// </summary>
    [Fact]
    public async Task A_choice_naming_a_caver_no_longer_on_the_roster_is_not_recorded_and_does_not_fail_the_save()
    {
        var goneUser = Guid.CreateVersion7();
        var keptUser = Guid.CreateVersion7();
        Guid gone;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caver = new Caver { FullName = $"Merged away {marker}" };
            db.Cavers.Add(caver);
            await db.SaveChangesAsync();
            gone = caver.Id;
            db.Cavers.Remove(caver);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await SilexGis.Infrastructure.Import.DevicePersonMapping.RecordAsync(
                db,
                new Dictionary<Guid, Guid> { [goneUser] = gone, [keptUser] = clubmate },
                DevicePersonLinkSource.ArchiveImport,
                ownerId,
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.DevicePersons.AnyAsync(p => p.DeviceUserId == goneUser)).ShouldBeFalse();
            var kept = await db.DevicePersons.AsNoTracking().SingleAsync(p => p.DeviceUserId == keptUser);
            kept.CaverId.ShouldBe(clubmate);
            kept.LinkSource.ShouldBe(DevicePersonLinkSource.ArchiveImport);
        }
    }

    /// <summary>
    /// Somebody keeping the roster lists the phone users with the unlinked first, links one, and
    /// the link they made is the one a phone's different choice then leaves alone; unlinking keeps
    /// the phone user. Listing needs the right to be told who people are, linking the roster
    /// keeper's; an unknown phone user or caver is refused by name.
    /// </summary>
    [Fact]
    public async Task The_roster_keeper_lists_phone_users_unlinked_first_and_their_link_outlasts_a_phones_choice()
    {
        var linkedUser = Guid.CreateVersion7();
        var unlinkedUser = Guid.CreateVersion7();
        await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { Person(linkedUser, "linked", clubmate), Person(unlinkedUser, "unlinked", null) },
        });

        var keeperEmail = $"ppl-keep-{marker}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, keeperEmail);
        using var keeper = await AuthHelper.BearerClientAsync(factory, keeperEmail);

        var listed = await keeper.GetAsync("/api/v1/phone-users/?pageSize=500");
        var payload = await listed.Content.ReadAsStringAsync();
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var items = JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray().ToList();
        var order = items.Select(i => i.GetProperty("deviceUserId").GetGuid()).ToList();
        order.IndexOf(unlinkedUser).ShouldBeLessThan(order.IndexOf(linkedUser));
        items.TakeWhile(i => i.GetProperty("caverId").ValueKind == JsonValueKind.Null).Count()
            .ShouldBe(items.Count(i => i.GetProperty("caverId").ValueKind == JsonValueKind.Null));
        var linkedRow = items.Single(i => i.GetProperty("deviceUserId").GetGuid() == linkedUser);
        linkedRow.GetProperty("caverId").GetGuid().ShouldBe(clubmate);
        linkedRow.GetProperty("caverName").GetString().ShouldBe($"Mate {marker}");
        linkedRow.GetProperty("linkSource").GetString().ShouldBe("device");
        linkedRow.GetProperty("username").GetString().ShouldBe("linked");

        // Linked in the browser, then the phone sends a different choice: the browser's stands.
        var linked = await keeper.PutAsJsonAsync($"/api/v1/phone-users/{unlinkedUser}/link", new { caverId = secondClubmate });
        linked.StatusCode.ShouldBe(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("linkSource").GetString().ShouldBe("administrator");
        await UploadAsync(new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { Person(unlinkedUser, "unlinked", clubmate) },
        });
        var stood = await PersonAsync(unlinkedUser);
        stood.CaverId.ShouldBe(secondClubmate);
        stood.LinkSource.ShouldBe(DevicePersonLinkSource.Administrator);

        // Unlinked: the phone user stays, without a caver.
        var unlinked = await keeper.DeleteAsync($"/api/v1/phone-users/{linkedUser}/link");
        unlinked.StatusCode.ShouldBe(HttpStatusCode.OK, await unlinked.Content.ReadAsStringAsync());
        (await PersonAsync(linkedUser)).CaverId.ShouldBeNull();

        // Refused by name.
        await ShouldBeRefusedAsync(
            await keeper.PutAsJsonAsync($"/api/v1/phone-users/{Guid.CreateVersion7()}/link", new { caverId = clubmate }),
            HttpStatusCode.NotFound, "sync.phone_user_not_found");
        await ShouldBeRefusedAsync(
            await keeper.PutAsJsonAsync($"/api/v1/phone-users/{linkedUser}/link", new { caverId = Guid.CreateVersion7() }),
            HttpStatusCode.NotFound, "sync.caver_not_found");
        (await keeper.PutAsJsonAsync($"/api/v1/phone-users/{linkedUser}/link", new { caverId = Guid.Empty }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Rights: a viewer may list but not link; an account denied people may not list; nobody
        // signed out reaches any of it.
        var viewerEmail = $"ppl-view-{marker}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, viewerEmail);
        using var viewer = await AuthHelper.BearerClientAsync(factory, viewerEmail);
        (await viewer.GetAsync("/api/v1/phone-users/")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShouldBeRefusedAsync(
            await viewer.PutAsJsonAsync($"/api/v1/phone-users/{linkedUser}/link", new { caverId = clubmate }),
            HttpStatusCode.Forbidden, "access.forbidden");
        await ShouldBeRefusedAsync(
            await viewer.DeleteAsync($"/api/v1/phone-users/{unlinkedUser}/link"),
            HttpStatusCode.Forbidden, "access.forbidden");
        (await PersonAsync(unlinkedUser)).CaverId.ShouldBe(secondClubmate);

        var deniedEmail = $"ppl-nolist-{marker}@t.local";
        var deniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, deniedEmail);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = deniedId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Cavers,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        using var denied = await AuthHelper.BearerClientAsync(factory, deniedEmail);
        await ShouldBeRefusedAsync(await denied.GetAsync("/api/v1/phone-users/"), HttpStatusCode.Forbidden, "access.forbidden");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/phone-users/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/phone-users/{linkedUser}/link", new { caverId = clubmate }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task SetLinkAsync(Guid deviceUserId, Guid caverId, DevicePersonLinkSource source)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var person = await db.DevicePersons.SingleAsync(p => p.DeviceUserId == deviceUserId);
        person.CaverId = caverId;
        person.LinkSource = source;
        await db.SaveChangesAsync();
    }

    private static object Person(Guid deviceUserId, string username, Guid? caverId) => new
    {
        deviceUserId,
        username,
        firstName = "Ana",
        lastName = (string?)null,
        caverId,
    };

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        name,
        geometry = new { type = "Point", coordinates = new[] { 22.5, 45.5 } },
        caveTypeCode = "cave",
        isMain = false,
    };

    private async Task<JsonElement> UploadAsync(object request)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> RevertAsync(Guid importBatchId, object? body = null)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", body ?? new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Dictionary<Guid, Guid?>> LinksAsync()
    {
        var directory = await owner.GetFromJsonAsync<JsonElement>("/api/v1/sync/people");
        return directory.GetProperty("links").EnumerateArray().ToDictionary(
            l => l.GetProperty("deviceUserId").GetGuid(),
            l => l.GetProperty("caverId").ValueKind == JsonValueKind.Null ? (Guid?)null : l.GetProperty("caverId").GetGuid());
    }

    private async Task<DevicePerson> PersonAsync(Guid deviceUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.DevicePersons.AsNoTracking().SingleAsync(p => p.DeviceUserId == deviceUserId);
    }

    private static async Task<List<string>> FeaturesAsync(HttpClient client)
    {
        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/sync/capabilities");
        return [.. capabilities.GetProperty("features").EnumerateArray().Select(f => f.GetString()!)];
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code, payload);
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
