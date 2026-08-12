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
/// The roster of people over real HTTP: the two-tier contact disclosure rule (linked
/// cavers follow their account's own settings, account-less cavers are roster-keeper
/// business, remarks always are), merge as the remedy for duplicates that trip history
/// pins in place, and the invariant the people model exists to hold — an account-less
/// caver's presence changes no authorization decision anywhere.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaverRosterTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private string suffix = null!;

    private HttpClient keeper = null!;   // Manager role → Caving Group Managers → roster keeper
    private HttpClient editor = null!;   // Editor role — content rights are NOT roster rights
    private HttpClient viewer = null!;   // regular account (All Users only)
    private HttpClient admin = null!;
    private Guid keeperId;
    private Guid editorId;
    private Guid viewerId;

    public CaverRosterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        keeperId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"ros-keep-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ros-edit-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ros-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ros-adm-{suffix}@t.local");

        keeper = await AuthHelper.BearerClientAsync(factory, $"ros-keep-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"ros-edit-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ros-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"ros-adm-{suffix}@t.local");
    }

    // ---- the disclosure matrix ----

    [Fact]
    public async Task An_account_less_cavers_contact_is_roster_keeper_business_only()
    {
        var caverId = await CreateCaverAsync(
            $"Maria Beles {suffix}", email: "maria@village.example", phone: "+40 700 000 001",
            notes: "prefers morning trips");

        // The keeper reads everything they wrote.
        var mine = await GetCaverAsync(keeper, caverId);
        mine.GetProperty("email").GetString().ShouldBe("maria@village.example");
        mine.GetProperty("phone").GetString().ShouldBe("+40 700 000 001");
        mine.GetProperty("notes").GetString().ShouldBe("prefers morning trips");

        // Any signed-in caller reads the name — a club member list has always been
        // readable — but nobody consented to contact disclosure on this person's behalf,
        // so a regular account and even a content Editor get nulls.
        foreach (var client in new[] { viewer, editor })
        {
            var theirs = await GetCaverAsync(client, caverId);
            theirs.GetProperty("name").GetString().ShouldStartWith("Maria Beles");
            theirs.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
            theirs.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
            theirs.GetProperty("notes").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        // Never served anonymously — not the contact, not even the name.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/cavers/{caverId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_linked_cavers_contact_follows_the_account_holders_own_settings()
    {
        // The subject shares their phone with any signed-in user and keeps the email
        // private. The roster must serve exactly what the profile would.
        (await viewer.PutAsJsonAsync("/api/v1/me", new
        {
            firstName = (string?)null,
            lastName = (string?)null,
            displayName = $"Vio {suffix}",
            bio = (string?)null,
            phoneNumber = "+40 700 000 002",
            cavingClub = (string?)null,
            locale = "en",
            visibility = new
            {
                realName = "private",
                bio = "private",
                email = "private",
                phone = "authenticated",
                cavingClub = "private",
                address = "private",
                addressPoint = "private",
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A roster keeper once typed contact details onto the row; after linking, those
        // columns must never be served for a linked person — the roster is not a side
        // channel around the account's choices, not even for the keeper who wrote them.
        Guid caverId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caver = await db.Cavers.SingleAsync(c => c.UserId == viewerId);
            caver.Email = "stale-roster@keeper.example";
            caver.Phone = "+40 799 999 999";
            await db.SaveChangesAsync();
            caverId = caver.Id;
        }

        foreach (var (client, who) in new[] { (keeper, "keeper"), (editor, "editor") })
        {
            var row = await GetCaverAsync(client, caverId);
            row.GetProperty("phone").GetString().ShouldBe("+40 700 000 002", who);
            row.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null, who);
        }

        // The subject reads their own contact in full through the self relation.
        var self = await GetCaverAsync(viewer, caverId);
        self.GetProperty("email").GetString().ShouldBe($"ros-view-{suffix}@t.local");
        self.GetProperty("phone").GetString().ShouldBe("+40 700 000 002");
    }

    [Fact]
    public async Task Roster_writes_are_roster_keeper_business_and_self_edit_never_touches_notes()
    {
        var caverId = await CreateCaverAsync($"Edit Target {suffix}");

        // Content rights are not roster rights: a Viewer cannot create, and neither the
        // Viewer nor an Editor may edit somebody else's entry, link accounts or merge.
        (await viewer.PostAsJsonAsync("/api/v1/cavers/", new { fullName = "Nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.PutAsJsonAsync($"/api/v1/cavers/{caverId}", new { fullName = "Hijacked" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/v1/cavers/{caverId}/account-link", new { userId = viewerId }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/v1/cavers/{caverId}/merge", new { sourceCaverId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Editing your own entry is allowed — it is your name on trips — but remarks are
        // written about a person, not by them, so a self-edit cannot rewrite the notes.
        Guid ownCaverId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var own = await db.Cavers.SingleAsync(c => c.UserId == editorId);
            own.Notes = "kept by the roster";
            await db.SaveChangesAsync();
            ownCaverId = own.Id;
        }

        (await editor.PutAsJsonAsync($"/api/v1/cavers/{ownCaverId}", new
        {
            fullName = $"Radu Editor {suffix}",
            notes = "self-praise",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var own = await db.Cavers.AsNoTracking().SingleAsync(c => c.Id == ownCaverId);
            own.FullName.ShouldBe($"Radu Editor {suffix}");
            own.Notes.ShouldBe("kept by the roster");
        }
    }

    // ---- merge: the remedy the RESTRICT points at ----

    [Fact]
    public async Task Trip_history_blocks_deletion_and_merge_is_the_remedy()
    {
        // A trip author names a guest — the guest becomes a roster row (no account).
        var tripId = await CreateTripWithGuestAsync(editor, $"Dup Guest {suffix}");
        Guid guestId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            guestId = await db.Cavers.Where(c => c.FullName == $"Dup Guest {suffix}")
                .Select(c => c.Id).SingleAsync();
        }

        // Somebody enters the same person a second time, with contact details and a club.
        var duplicateId = await CreateCaverAsync(
            $"Dup Guest {suffix} (2)", email: "dup@village.example");
        var groupId = await CreateCavingGroupAsync(keeper, $"Merge Club {suffix}");
        (await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
        {
            caverId = duplicateId,
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deleting the person named on the trip is refused with the remedy in the answer —
        // even for a full administrator, because the refusal is about history, not rights.
        var refused = await admin.DeleteAsync($"/api/v1/cavers/{guestId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("caver.referenced_by_trips");

        // The merge folds the duplicate in: contact and membership move, the source dies.
        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{guestId}/merge", new
        {
            sourceCaverId = duplicateId,
        });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());

        var survivor = await GetCaverAsync(keeper, guestId);
        survivor.GetProperty("email").GetString().ShouldBe("dup@village.example");
        survivor.GetProperty("cavingGroups").EnumerateArray()
            .Select(g => g.GetProperty("cavingGroupId").GetGuid()).ShouldContain(groupId);
        (await keeper.GetAsync($"/api/v1/cavers/{duplicateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The trip still names the survivor — history never lost the person.
        var trip = await editor.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        trip.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid()).ShouldContain(guestId);
    }

    /// <summary>
    /// A merge folds one person's rows onto another's, and a trip's roster is unique on the trip,
    /// the role and the person together. So the fold has to be decided per role: the duplicate row
    /// for a job the survivor already did on that trip goes, because keeping it would collide, and
    /// the row for a job the survivor did not do is kept, because dropping it would quietly erase
    /// that somebody led the trip.
    /// </summary>
    [Fact]
    public async Task Merging_two_entries_for_one_person_keeps_every_job_and_collapses_only_the_repeats()
    {
        var tripId = await CreateTripWithGuestAsync(editor, $"Two Jobs {suffix}");
        var duplicateId = await CreateCaverAsync($"Two Jobs {suffix} (2)");

        Guid survivorId;
        long leaderRoleId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            survivorId = await db.Cavers.Where(c => c.FullName == $"Two Jobs {suffix}")
                .Select(c => c.Id).SingleAsync();
            var participantRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "participant").Select(r => r.Id).SingleAsync();
            leaderRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "leader").Select(r => r.Id).SingleAsync();

            // The duplicate entry is on the same trip twice: once for the job the survivor is
            // already recorded doing, once for a job nobody else on the trip holds.
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                RoleId = participantRoleId,
                CaverId = duplicateId,
            });
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                RoleId = leaderRoleId,
                CaverId = duplicateId,
            });
            await db.SaveChangesAsync();
        }

        var merged = await keeper.PostAsJsonAsync($"/api/v1/cavers/{survivorId}/merge", new
        {
            sourceCaverId = duplicateId,
        });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var rows = await db.TripLogParticipants.AsNoTracking()
                .Where(p => p.TripLogId == tripId)
                .ToListAsync();

            rows.ShouldAllBe(p => p.CaverId == survivorId);
            // Two rows, not three and not one: the repeated attendance collapsed, the leading
            // survived, and one person doing two jobs on one trip is two rows by design.
            rows.Count.ShouldBe(2);
            rows.Count(p => p.RoleId == leaderRoleId).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Two_accounts_are_two_people_and_refuse_to_merge()
    {
        Guid editorCaverId;
        Guid viewerCaverId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            editorCaverId = await db.Cavers.Where(c => c.UserId == editorId).Select(c => c.Id).SingleAsync();
            viewerCaverId = await db.Cavers.Where(c => c.UserId == viewerId).Select(c => c.Id).SingleAsync();
        }

        var response = await keeper.PostAsJsonAsync($"/api/v1/cavers/{editorCaverId}/merge", new
        {
            sourceCaverId = viewerCaverId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("caver.merge_two_accounts");
    }

    // ---- the X1 invariant ----

    [Fact]
    public async Task An_account_less_cavers_presence_changes_no_decision_anywhere()
    {
        // A club that is a trustee of a ruleset: members with accounts inherit through it.
        var groupId = await CreateCavingGroupAsync(keeper, $"X1 Club {suffix}");
        (await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
        {
            caverId = await RosterHelper.CaverIdForAsync(factory, editorId),
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rulesetId = await CreatePermissionGroupAsync($"X1 Rules {suffix}");
        (await admin.PostAsJsonAsync($"/api/v1/permission-groups/{rulesetId}/members", new
        {
            memberKind = "cavingGroup",
            memberId = groupId,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{rulesetId}/entries", new
        {
            entries = new[]
            {
                new { effect = "allow", domain = "audit", actions = "read", scopeKind = "all" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var before = await RosterHelper.AccessContextOfAsync(db, editorId);
        before.Entries.Select(e => e.EntryId).ShouldContain(
            await db.AccessEntries.Where(e => e.PermissionGroupId == rulesetId).Select(e => e.Id).SingleAsync());

        // Enroll a person who has never signed in — and never will.
        var accountlessId = await CreateCaverAsync($"X1 Ghost {suffix}");
        (await keeper.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
        {
            caverId = accountlessId,
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The linked member's context is bit-identical: same groups, same entries, same
        // full-admin answer. There is no path from a caver row to a decision.
        var after = await RosterHelper.AccessContextOfAsync(db, editorId);
        after.IsFullAdmin.ShouldBe(before.IsFullAdmin);
        after.CavingGroupIds.Order().ShouldBe(before.CavingGroupIds.Order());
        after.Entries.Select(e => e.EntryId).Order()
            .ShouldBe(before.Entries.Select(e => e.EntryId).Order());

        // And a grant to the club notifies exactly the members who exist as accounts —
        // the account-less member contributes no recipient, and no phantom row appears.
        var lastOutboxId = await db.NotificationOutbox.AsNoTracking()
            .MaxAsync(n => (long?)n.Id) ?? 0;
        var caveId = await CreateCaveAsync(editor, $"X1 Cave {suffix}");
        (await editor.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "cavingGroup", subjectId = groupId, effect = "allow",
                    actions = "read", scopeKind = "object",
                },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var newRows = await db.NotificationOutbox.AsNoTracking()
            .Where(n => n.Id > lastOutboxId && n.Category == NotificationCategory.PermissionGranted)
            .ToListAsync();
        // The granter themselves is not notified; the keeper (creator-member) is the
        // only other linked member. One account-less member, zero extra messages.
        newRows.Select(n => n.UserId).ShouldBe([keeperId]);
    }

    // ---- the catalogue reads every account holds ----

    [Fact]
    public async Task A_regular_account_reads_the_catalogues_through_all_users()
    {
        // These reads used to be the absence of a rule; now they are the All Users seed.
        foreach (var url in new[]
        {
            "/api/v1/map-layers",
            "/api/v1/cave-types",
            "/api/v1/entrance-types",
            "/api/v1/feature-types",
            "/api/v1/tags",
            "/api/v1/caving-groups/",
            "/api/v1/cavers/",
        })
        {
            (await viewer.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        }

        // And none of them is served anonymously — public read stays an endpoint-level
        // switch, and no such endpoint exists on these routes.
        using var anonymous = factory.CreateClient();
        foreach (var url in new[] { "/api/v1/map-layers", "/api/v1/tags", "/api/v1/cavers/" })
        {
            (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, url);
        }
    }

    // ---- helpers ----

    private async Task<Guid> CreateCaverAsync(
        string fullName, string? email = null, string? phone = null, string? notes = null)
    {
        var response = await keeper.PostAsJsonAsync("/api/v1/cavers/", new { fullName, email, phone, notes });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetCaverAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/cavers/{id}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<Guid> CreateCavingGroupAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name,
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePermissionGroupAsync(string name)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/permission-groups/", new
        {
            name,
            description = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTripWithGuestAsync(HttpClient author, string guestName)
    {
        var response = await author.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Roster Trip {Guid.NewGuid():N}"[..30],
            tripDate = "2026-05-01",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = (Guid?)null, newCaverName = (string?)guestName } },
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(HttpClient author, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "private",
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
