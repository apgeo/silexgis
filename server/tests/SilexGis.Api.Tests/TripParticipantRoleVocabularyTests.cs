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
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What somebody did on a trip is a row an installation extends, not a value fixed when the
/// software was built — a club with a job nobody thought of adds it themselves.
/// <para>
/// The rows that ship are the exchange vocabulary: clients translate their labels by code and
/// installations exchange records under them, so their codes cannot move and the rows cannot be
/// deleted. Each refusal is asserted beside the edit it does <em>not</em> refuse, because a guard
/// that refused everything would pass a test that only checked the refusals.
/// </para>
/// </summary>
public sealed class TripParticipantRoleVocabularyTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;  // Editor — writes trips, does not keep the vocabulary
    private HttpClient viewer = null!;  // Viewer — reads it, as every roster on screen must
    private HttpClient admin = null!;

    public TripParticipantRoleVocabularyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpr-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpr-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tpr-adm-{suffix}@t.local");

        editor = await AuthHelper.BearerClientAsync(factory, $"tpr-ed-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"tpr-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tpr-adm-{suffix}@t.local");
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
    public async Task The_participant_role_vocabulary_ships_seeded_and_grows_by_admin_hand()
    {
        // Every signed-in caller reads it: a roster renders a job from this list wherever a trip
        // is shown, so a reader who could not fetch it would see an identity where a word belongs.
        var listed = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/trip-participant-roles");
        var byCode = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);
        foreach (var seed in TripParticipantRoleSeeds.All)
        {
            byCode.ContainsKey(seed.Code).ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("isSeeded").GetBoolean().ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("name").GetString().ShouldBe(seed.Name, seed.Code);
        }

        // Keeping the vocabulary is administration: writing trips does not come with it.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            code = $"cook_{suffix}",
            name = "Cook",
            description = (string?)null,
            sortOrder = 500,
        };
        (await editor.PostAsJsonAsync("/api/v1/trip-participant-roles", request))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var created = await admin.PostAsJsonAsync("/api/v1/trip-participant-roles", request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var custom = await ReadJsonAsync(created);
        custom.GetProperty("isSeeded").GetBoolean().ShouldBeFalse();
        var customId = custom.GetProperty("id").GetInt64();

        // A club's own row is installation-local: renamed and re-coded at will.
        var updated = await admin.PutAsJsonAsync($"/api/v1/trip-participant-roles/{customId}", new
        {
            code = $"camp_cook_{suffix}",
            name = "Camp cook",
            description = "Fed everybody.",
            sortOrder = 510,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        // Codes are a namespace: a second row cannot take a live one.
        var taken = await admin.PostAsJsonAsync(
            "/api/v1/trip-participant-roles", request with { code = "surveyor" });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(taken)).ShouldBe("trip_participant_role.code_taken");

        (await admin.DeleteAsync($"/api/v1/trip-participant-roles/{customId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Anonymous callers get nowhere, read or write.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-participant-roles"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/trip-participant-roles", request))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync("/api/v1/trip-participant-roles/1"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Being_there_and_proposing_it_are_shipped_rows_that_cannot_be_re_coded_or_deleted()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-participant-roles");
        var rows = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);

        foreach (var code in new[] { TripParticipantRoleSeeds.ParticipantCode, TripParticipantRoleSeeds.ProposerCode })
        {
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
            var recoded = await admin.PutAsJsonAsync(
                $"/api/v1/trip-participant-roles/{id}", body with { code = $"{code}_renamed" });
            recoded.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadCodeAsync(recoded)).ShouldBe("trip_participant_role.seeded_immutable");

            // And the row itself stays: attendance could not be recorded at all without the first,
            // and whoever may edit a trip they put forward is decided by the second.
            var deleted = await admin.DeleteAsync($"/api/v1/trip-participant-roles/{id}");
            deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ReadCodeAsync(deleted)).ShouldBe("trip_participant_role.seeded_immutable");

            // The refusals are about the code and the row's existence, nothing else: an
            // installation rewording and reordering a shipped row is an ordinary allowed edit.
            var reworded = await admin.PutAsJsonAsync($"/api/v1/trip-participant-roles/{id}", body with
            {
                name = "Participă",
                description = "Wording an installation chose.",
                sortOrder = 5,
            });
            reworded.StatusCode.ShouldBe(HttpStatusCode.OK, await reworded.Content.ReadAsStringAsync());
            (await ReadJsonAsync(reworded)).GetProperty("name").GetString().ShouldBe("Participă");

            // Put the shipped wording back so the rest of the suite reads what it seeded.
            (await admin.PutAsJsonAsync($"/api/v1/trip-participant-roles/{id}", body))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_role_somebody_is_still_recorded_in_cannot_be_deleted_until_nobody_holds_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/v1/trip-participant-roles", new
        {
            code = $"digger_{suffix}",
            name = "Digger",
            description = (string?)null,
            sortOrder = 600,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var roleId = (await ReadJsonAsync(created)).GetProperty("id").GetInt64();

        var trip = await editor.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Dig {suffix}",
            tripDate = "2026-06-01",
            participants = new[] { new { caverId = (Guid?)null, newCaverName = $"Digger Guest {suffix}" } },
            visibility = "private",
        });
        trip.StatusCode.ShouldBe(HttpStatusCode.Created, await trip.Content.ReadAsStringAsync());
        var tripId = (await ReadJsonAsync(trip)).GetProperty("id").GetGuid();

        long participantRowId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caverId = await db.Cavers.Where(c => c.FullName == $"Digger Guest {suffix}")
                .Select(c => c.Id).SingleAsync();
            // A second job for the same person on the same trip: one row each, which is what the
            // roster's uniqueness is now on. The write path names only the two shipped roles, so
            // this is entered directly.
            var row = new TripLogParticipant { TripLogId = tripId, RoleId = roleId, CaverId = caverId };
            db.TripLogParticipants.Add(row);
            await db.SaveChangesAsync();
            participantRowId = row.Id;

            (await db.TripLogParticipants.CountAsync(p => p.TripLogId == tripId && p.CaverId == caverId))
                .ShouldBe(2);
        }

        // The database refuses this too, but a request deserves a stable code and a remedy rather
        // than a constraint violation.
        var refused = await admin.DeleteAsync($"/api/v1/trip-participant-roles/{roleId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("trip_participant_role.in_use");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.TripLogParticipants.Remove(
                await db.TripLogParticipants.SingleAsync(p => p.Id == participantRowId));
            await db.SaveChangesAsync();
        }

        (await admin.DeleteAsync($"/api/v1/trip-participant-roles/{roleId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();
}
