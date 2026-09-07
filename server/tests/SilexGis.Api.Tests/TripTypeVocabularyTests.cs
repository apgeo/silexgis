// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a trip was for is a row an installation extends, not a value fixed when the software was
/// built — a club that runs a kind of trip nobody thought of adds it themselves.
/// <para>
/// The rows that ship are the exchange vocabulary: clients translate their labels by code and
/// installations exchange records under them, so their codes cannot move and the rows cannot be
/// deleted. Everything else about them is ordinary — an installation renames and reorders them
/// freely — and each refusal is asserted beside the edit it does <em>not</em> refuse, because a
/// guard that refused everything would pass a test that only checked the refusals.
/// </para>
/// </summary>
public sealed class TripTypeVocabularyTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;  // Editor — writes trips, does not keep the vocabulary
    private HttpClient viewer = null!;  // Viewer — reads it, as every screen showing a trip must
    private HttpClient admin = null!;

    public TripTypeVocabularyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tty-ed-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tty-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tty-adm-{suffix}@t.local");

        editor = await AuthHelper.BearerClientAsync(factory, $"tty-ed-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"tty-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tty-adm-{suffix}@t.local");
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
    public async Task The_trip_purpose_vocabulary_ships_seeded_and_grows_by_admin_hand()
    {
        // Every signed-in caller reads it: a trip's purpose is rendered from this list on every
        // screen that shows a trip, so a reader who could not fetch it would see an identity.
        var listed = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        var byCode = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);
        foreach (var seed in TripTypeSeeds.All)
        {
            byCode.ContainsKey(seed.Code).ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("isSeeded").GetBoolean().ShouldBeTrue(seed.Code);
            byCode[seed.Code].GetProperty("name").GetString().ShouldBe(seed.Name, seed.Code);
        }

        // Keeping the vocabulary is administration: writing trips does not come with it.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            code = $"custom_{suffix}",
            name = "Bat count",
            description = (string?)null,
            sortOrder = 500,
        };
        (await editor.PostAsJsonAsync("/api/v1/trip-types", request))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var created = await admin.PostAsJsonAsync("/api/v1/trip-types", request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var custom = await ReadJsonAsync(created);
        custom.GetProperty("isSeeded").GetBoolean().ShouldBeFalse();
        var customId = custom.GetProperty("id").GetInt64();

        // A club's own row is installation-local: renamed and re-coded at will.
        var updated = await admin.PutAsJsonAsync($"/api/v1/trip-types/{customId}", new
        {
            code = $"custom2_{suffix}",
            name = "Bat survey",
            description = "Counting the colony.",
            sortOrder = 510,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        // Codes are a namespace: a second row cannot take a live one.
        var taken = await admin.PostAsJsonAsync("/api/v1/trip-types", request with { code = "survey" });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(taken)).ShouldBe("trip_type.code_taken");

        (await admin.DeleteAsync($"/api/v1/trip-types/{customId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Anonymous callers get nowhere, read or write.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-types")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/trip-types", request))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync("/api/v1/trip-types/1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Shipped_trip_purposes_keep_their_code_and_never_leave_but_are_otherwise_ordinary()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        var seeded = listed.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "survey");
        var id = seeded.GetProperty("id").GetInt64();
        // The section schemas travel back with the edit, the way the surface that edits a purpose
        // sends them: an omitted schema means "this section has none", so a body that left them
        // out would empty all three as a side effect of renaming the row.
        var body = new
        {
            code = seeded.GetProperty("code").GetString(),
            name = seeded.GetProperty("name").GetString(),
            description = (string?)null,
            sortOrder = seeded.GetProperty("sortOrder").GetInt32(),
            fieldDataSchema = seeded.GetProperty("fieldDataSchema").GetString(),
            logisticsSchema = seeded.GetProperty("logisticsSchema").GetString(),
            safetySchema = seeded.GetProperty("safetySchema").GetString(),
        };

        // The code is what other installations exchange records under and what a client looks up
        // its own wording by, so it does not move.
        var recoded = await admin.PutAsJsonAsync($"/api/v1/trip-types/{id}", body with { code = "mapping" });
        recoded.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(recoded)).ShouldBe("trip_type.seeded_immutable");

        var deleted = await admin.DeleteAsync($"/api/v1/trip-types/{id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(deleted)).ShouldBe("trip_type.seeded_immutable");

        // The refusals are about the code and the row's existence, nothing else: an installation
        // renaming and reordering a shipped row is an ordinary allowed edit.
        var renamed = await admin.PutAsJsonAsync($"/api/v1/trip-types/{id}", body with
        {
            name = "Cartare",
            description = "Wording an installation chose.",
            sortOrder = 15,
        });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var afterRename = await ReadJsonAsync(renamed);
        afterRename.GetProperty("name").GetString().ShouldBe("Cartare");
        // Re-sending an unchanged schema is not a schema change: every report already stamped
        // with the old version would otherwise be marked stale because somebody fixed a label.
        afterRename.GetProperty("fieldDataSchemaVersion").GetInt32()
            .ShouldBe(seeded.GetProperty("fieldDataSchemaVersion").GetInt32());

        // Put the shipped wording back so the rest of the suite reads what it seeded.
        (await admin.PutAsJsonAsync($"/api/v1/trip-types/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_purpose_a_trip_still_names_cannot_be_deleted_until_nothing_names_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/v1/trip-types", new
        {
            code = $"digging_{suffix}",
            name = "Digging weekend",
            description = (string?)null,
            sortOrder = 600,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var typeId = (await ReadJsonAsync(created)).GetProperty("id").GetInt64();

        var trip = await editor.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Dig {suffix}",
            tripTypeId = typeId,
            tripDate = "2026-06-01",
            participants = Array.Empty<object>(),
            visibility = "private",
        });
        trip.StatusCode.ShouldBe(HttpStatusCode.Created, await trip.Content.ReadAsStringAsync());
        var tripId = (await ReadJsonAsync(trip)).GetProperty("id").GetGuid();

        // The database refuses this too, but a request deserves a stable code rather than a
        // constraint violation — and the trip keeps the purpose it was recorded under.
        var refused = await admin.DeleteAsync($"/api/v1/trip-types/{typeId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("trip_type.in_use");
        (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}"))
            .GetProperty("tripTypeId").GetInt64().ShouldBe(typeId);

        // Retyped to nothing, the row is nobody's and goes.
        var retyped = await editor.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Dig {suffix}",
            tripTypeId = (long?)null,
            tripDate = "2026-06-01",
            participants = Array.Empty<object>(),
            visibility = "private",
        });
        retyped.StatusCode.ShouldBe(HttpStatusCode.OK, await retyped.Content.ReadAsStringAsync());

        (await admin.DeleteAsync($"/api/v1/trip-types/{typeId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A purpose may name the list its trips settle before setting off — but only a list that is
    /// there and that the administrator naming it may read, and the identity it names is told
    /// only to callers who may read that list.
    /// </summary>
    /// <remarks>
    /// Reading this vocabulary is open to every account, so a purpose that published its list's
    /// identity to everyone would hand out the one thing the trip's own checklist route withholds
    /// — and knowing a list exists is enough to go and ask for it. The two surfaces answer the
    /// same question, so they answer it the same way.
    /// </remarks>
    [Fact]
    public async Task A_purpose_names_a_list_only_where_the_caller_may_read_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var list = await admin.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = $"Before we go {suffix}",
            description = (string?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
            items = new[] { new { text = "Permit obtained" } },
        });
        list.StatusCode.ShouldBe(HttpStatusCode.Created, await list.Content.ReadAsStringAsync());
        var listId = (await ReadJsonAsync(list)).GetProperty("id").GetGuid();

        // A reference to a list that is not there is refused with an answer rather than left to
        // the foreign key, which would reach the caller as a fault with nothing to act on.
        var missing = await admin.PostAsJsonAsync("/api/v1/trip-types", new
        {
            code = $"absent_{suffix}",
            name = "Names nothing",
            description = (string?)null,
            sortOrder = 700,
            defaultChecklistId = Guid.CreateVersion7(),
        });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await missing.Content.ReadAsStringAsync());
        (await ReadCodeAsync(missing)).ShouldBe("trip_type.checklist_not_found");

        // The same request naming a list this administrator may read is not refused, so the
        // refusal above is the reference and not the field.
        var body = new
        {
            code = $"prepared_{suffix}",
            name = "Prepared trip",
            description = (string?)null,
            sortOrder = 700,
            defaultChecklistId = listId,
        };
        var created = await admin.PostAsJsonAsync("/api/v1/trip-types", body);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var purpose = await ReadJsonAsync(created);
        purpose.GetProperty("defaultChecklistId").GetGuid().ShouldBe(listId);
        var typeId = purpose.GetProperty("id").GetInt64();

        // The owner of the list, reading the vocabulary, is told which list the purpose names.
        var toAdmin = await FindPurposeAsync(admin, body.code);
        toAdmin.GetProperty("defaultChecklistId").GetGuid().ShouldBe(listId);

        // A caller who may not read the list is told the purpose names none — the same answer a
        // purpose that names none gives, which is what keeps the two from being told apart.
        var toViewer = await FindPurposeAsync(viewer, body.code);
        toViewer.GetProperty("defaultChecklistId").ValueKind.ShouldBe(JsonValueKind.Null);
        toViewer.GetProperty("name").GetString().ShouldBe("Prepared trip");

        // Opened to any account, the same reader is told it — so what was withheld was the
        // audience and not the field.
        var opened = await admin.PutAsJsonAsync($"/api/v1/checklists/{listId}", new
        {
            title = $"Before we go {suffix}",
            description = (string?)null,
            cavingGroupId = (Guid?)null,
            visibility = "authenticated",
            items = new[] { new { text = "Permit obtained" } },
        });
        opened.StatusCode.ShouldBe(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        (await FindPurposeAsync(viewer, body.code)).GetProperty("defaultChecklistId").GetGuid()
            .ShouldBe(listId);

        // The same refusal on the update path, which is the one a stale form actually takes.
        var stale = await admin.PutAsJsonAsync($"/api/v1/trip-types/{typeId}", new
        {
            body.code,
            body.name,
            body.description,
            body.sortOrder,
            defaultChecklistId = Guid.CreateVersion7(),
        });
        stale.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await stale.Content.ReadAsStringAsync());
        (await ReadCodeAsync(stale)).ShouldBe("trip_type.checklist_not_found");

        (await admin.DeleteAsync($"/api/v1/trip-types/{typeId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<JsonElement> FindPurposeAsync(HttpClient client, string code)
    {
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/trip-types");
        return listed.EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();
}
