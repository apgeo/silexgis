// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What somebody was at a camp as: its own vocabulary, kept the way the other seeded vocabularies
/// are kept. Cooking for thirty people and keeping the base camp are not jobs underground, so they
/// live here and not among the roles a trip records.
/// <para>
/// The rows that ship are the exchange vocabulary: clients translate their labels by code and
/// installations exchange records under them, so their codes cannot move and the rows cannot be
/// deleted. Every shipped code is asserted rather than a chosen two, because the shipped list is
/// the single input to both the startup seeder and the guard here — a code dropped or reordered
/// there would otherwise leave the guard's own list silently shorter than the seeder's.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionRosterRoleVocabularyTests : IAsyncLifetime, IDisposable
{
    private const string Route = "/api/v1/expedition-roster-roles";

    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;  // Editor — writes camps, does not keep the vocabulary
    private HttpClient viewer = null!;  // Viewer — reads it, as every camp roster on screen must
    private HttpClient admin = null!;
    private Guid adminId;

    public ExpeditionRosterRoleVocabularyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"err-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"err-view-{suffix}@t.local");
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"err-adm-{suffix}@t.local");

        editor = await AuthHelper.BearerClientAsync(factory, $"err-ed-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"err-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"err-adm-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        viewer?.Dispose();
        admin?.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task The_camp_roster_vocabulary_ships_seeded_and_grows_by_admin_hand()
    {
        // Every signed-in caller reads it: a camp's roster renders a role from this list, so a
        // reader who could not fetch it would see an identity where a word belongs.
        var listed = await viewer.GetFromJsonAsync<JsonElement>(Route);
        var byCode = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);
        foreach (var seed in ExpeditionRosterRoleSeeds.All)
        {
            byCode.ContainsKey(seed.Code).ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("isSeeded").GetBoolean().ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("name").GetString().ShouldBe(seed.Name, seed.Code);
        }

        // The two the camp's roster exists for are shipped and are not trip jobs — they are the
        // reason this vocabulary is separate at all.
        byCode.ShouldContainKey("cook");
        byCode.ShouldContainKey("base_camp");

        // Keeping the vocabulary is administration: writing camps does not come with it.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            code = $"radio_{suffix}",
            name = "Radio watch",
            description = (string?)null,
            sortOrder = 500,
        };
        (await editor.PostAsJsonAsync(Route, request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var created = await admin.PostAsJsonAsync(Route, request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var custom = await ReadJsonAsync(created);
        custom.GetProperty("isSeeded").GetBoolean().ShouldBeFalse();
        var customId = custom.GetProperty("id").GetInt64();

        // A club's own row is installation-local: renamed and re-coded at will.
        var updated = await admin.PutAsJsonAsync($"{Route}/{customId}", new
        {
            code = $"surface_radio_{suffix}",
            name = "Surface radio",
            description = "Sat by the radio.",
            sortOrder = 510,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        // Codes are a namespace: a second row cannot take a live one.
        var taken = await admin.PostAsJsonAsync(
            Route, request with { code = ExpeditionRosterRoleSeeds.MemberCode });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(taken)).ShouldBe("expedition_roster_role.code_taken");

        (await admin.DeleteAsync($"{Route}/{customId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Anonymous callers get nowhere, read or write.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Route, request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Route}/1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_shipped_camp_role_keeps_its_code_and_its_row_while_its_wording_stays_free()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>(Route);
        var rows = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);

        foreach (var seed in ExpeditionRosterRoleSeeds.All)
        {
            var code = seed.Code;
            rows.ShouldContainKey(code);
            var row = rows[code];
            var id = row.GetProperty("id").GetInt64();
            var body = new
            {
                code,
                name = row.GetProperty("name").GetString(),
                description = (string?)null,
                sortOrder = row.GetProperty("sortOrder").GetInt32(),
            };

            // The code is what other installations exchange records under and what a client looks
            // up its own wording by, so it does not move.
            var recoded = await admin.PutAsJsonAsync($"{Route}/{id}", body with { code = $"{code}_renamed" });
            recoded.StatusCode.ShouldBe(HttpStatusCode.BadRequest, code);
            (await ReadCodeAsync(recoded)).ShouldBe("expedition_roster_role.seeded_immutable", code);

            // And the row itself stays. One of them carries more than the exchange argument: a
            // camp's roster with no "member" row could not record that somebody was simply there.
            var deleted = await admin.DeleteAsync($"{Route}/{id}");
            deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict, code);
            (await ReadCodeAsync(deleted)).ShouldBe("expedition_roster_role.seeded_immutable", code);

            // The refusals are about the code and the row's existence, nothing else: an
            // installation rewording and reordering a shipped row is an ordinary allowed edit.
            var reworded = await admin.PutAsJsonAsync($"{Route}/{id}", body with
            {
                name = "Bucătar",
                description = "Wording an installation chose.",
                sortOrder = 5,
            });
            reworded.StatusCode.ShouldBe(HttpStatusCode.OK, await reworded.Content.ReadAsStringAsync());
            (await ReadJsonAsync(reworded)).GetProperty("name").GetString().ShouldBe("Bucătar");

            // Put the shipped wording back so the rest of the suite reads what it seeded.
            (await admin.PutAsJsonAsync($"{Route}/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_camp_role_somebody_is_still_recorded_in_cannot_be_deleted_until_nobody_holds_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync(Route, new
        {
            code = $"quartermaster_{suffix}",
            name = "Quartermaster",
            description = (string?)null,
            sortOrder = 600,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var roleId = (await ReadJsonAsync(created)).GetProperty("id").GetInt64();

        long entryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var camp = new Expedition
            {
                Name = $"Camp holding a role {suffix}",
                StartDate = new DateOnly(2026, 7, 18),
                EndDate = new DateOnly(2026, 8, 1),
                OwnerUserId = adminId,
            };
            var caver = new Caver { FullName = $"Quartermaster {suffix}" };
            db.Expeditions.Add(camp);
            db.Cavers.Add(caver);
            await db.SaveChangesAsync();

            var entry = new ExpeditionRosterEntry
            {
                ExpeditionId = camp.Id,
                CaverId = caver.Id,
                RoleId = roleId,
                FromDate = new DateOnly(2026, 7, 18),
            };
            db.ExpeditionRoster.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;
        }

        // The database refuses this too, but a request deserves a stable code and a remedy rather
        // than a constraint violation.
        var refused = await admin.DeleteAsync($"{Route}/{roleId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("expedition_roster_role.in_use");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.ExpeditionRoster.Remove(await db.ExpeditionRoster.SingleAsync(r => r.Id == entryId));
            await db.SaveChangesAsync();
        }

        (await admin.DeleteAsync($"{Route}/{roleId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();
}
