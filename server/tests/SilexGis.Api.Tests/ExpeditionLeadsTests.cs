// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a camp left open, and what it refuses to say it left open.
///
/// <para>
/// The board is a reading of the places the member trips already name, so the tests here are about
/// the reading rather than about any register of its own: that a place two trips both named is one
/// lead, that a way on recorded under a role nobody would guess is still a way on, that a lead the
/// caller may not place is not on the board at all, and that a camp with nothing open answers an
/// empty board rather than an error.
/// </para>
/// <para>
/// The protection half is the reason the endpoint exists on the server rather than being assembled
/// by whoever draws it: a list of undefended ways into caves, sorted by how promising they look, is
/// the same disclosure as a coordinate and a more inviting one.
/// </para>
/// </summary>
public sealed class ExpeditionLeadsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;

    // An Editor, and deliberately so for the protection test: the seeded Editors group holds the
    // read over every content domain but holds ViewExactLocation over nothing, so it is exactly a
    // caller who may read a way on and may not place it.
    private HttpClient reader = null!;
    private Guid readerId;

    // A plain Viewer for the visibility test: an Editor reads past visibility at the widest scope,
    // so an Editor who "cannot see" a camp or a trip proves nothing at all.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    private long continuationTypeId;

    public ExpeditionLeadsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xlead-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xlead-read-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xlead-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            // Resolved by code, never as a number: the same row is numbered differently on a fresh
            // installation than on one that grew into it.
            continuationTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.Continuation).Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"xlead-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"xlead-read-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xlead-out-{suffix}@t.local");
    }

    [Fact]
    public async Task The_board_takes_an_account_and_says_nothing_about_a_camp_that_is_not_the_callers()
    {
        var camp = await CreateCampAsync("Quiet camp");
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/api/v1/expeditions/{camp}/leads"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The Viewer holds nothing over camps, so this is genuinely a camp they may not read — and
        // the answer is the one a camp that does not exist gets, in the same words.
        var refused = await outsider.GetAsync($"/api/v1/expeditions/{camp}/leads");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        var missing = await owner.GetAsync($"/api/v1/expeditions/{Guid.NewGuid()}/leads");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("expedition.not_found");

        // The positive half: the very same camp opens for the person who may read it.
        (await owner.GetAsync($"/api/v1/expeditions/{camp}/leads"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_camp_whose_trips_left_nothing_open_has_an_empty_board_rather_than_no_board()
    {
        var camp = await CreateCampAsync("Nothing open");
        var trip = await CreateTripAsync("Walk out");
        await AddTripAsync(camp, trip);

        var board = await BoardAsync(owner, camp);
        board.GetProperty("leads").GetInt32().ShouldBe(0);
        board.GetProperty("groups").GetArrayLength().ShouldBe(0);
        board.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // And a camp with no trips at all, which is the same answer by a different road.
        var bare = await BoardAsync(owner, await CreateCampAsync("No trips"));
        bare.GetProperty("leads").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Two trips of one camp finding the same way on is one lead. The namings really do repeat —
    /// the unreduced count below is the fixture's own proof of that, because a board reporting one
    /// lead over a fixture that only ever held one naming proves nothing about reducing anything.
    /// </summary>
    [Fact]
    public async Task A_way_on_two_of_the_camps_trips_both_named_is_one_lead()
    {
        var camp = await CreateCampAsync("Twice found");
        var first = await CreateTripAsync("First push");
        var second = await CreateTripAsync("Second push");
        await AddTripAsync(camp, first);
        await AddTripAsync(camp, second);

        var lead = await CreateContinuationAsync("Draughting crawl", state: "open", grade: "B",
            note: "two hours of digging");
        await NameAsync(first, lead, "trip-lead");
        await NameAsync(second, lead, "trip-dug");

        // The fixture proof: the place is named twice over, by two trips and under two roles.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.FeatureId == lead)).ShouldBe(2);
        }

        var board = await BoardAsync(owner, camp);
        board.GetProperty("leads").GetInt32().ShouldBe(1);

        var groups = board.GetProperty("groups").EnumerateArray().ToList();
        groups.Count.ShouldBe(1);
        groups[0].GetProperty("state").GetString().ShouldBe("open");

        var rows = groups[0].GetProperty("leads").EnumerateArray().ToList();
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("id").GetGuid().ShouldBe(lead);
        rows[0].GetProperty("grade").GetString().ShouldBe("B");
        rows[0].GetProperty("note").GetString().ShouldBe("two hours of digging");
    }

    /// <summary>
    /// A club that wrote a continuation down as somewhere it merely visited has still written the
    /// continuation down. Only writers pick a role; a board that asked about the obvious one would
    /// drop this lead without saying so, and the only list that would have shown it is the one
    /// doing the dropping.
    /// </summary>
    [Fact]
    public async Task A_way_on_recorded_under_any_role_at_all_is_still_on_the_board()
    {
        var camp = await CreateCampAsync("Roles camp");
        var trip = await CreateTripAsync("Recce");
        await AddTripAsync(camp, trip);

        var visited = await CreateContinuationAsync("Visited rift", state: "open", grade: "C");
        var surveyed = await CreateContinuationAsync("Surveyed aven", state: "checked", grade: "D");
        var named = await CreateContinuationAsync("Named lead", state: "open", grade: "A");
        await NameAsync(trip, visited, "trip-visited");
        await NameAsync(trip, surveyed, "trip-surveyed");
        await NameAsync(trip, named, "trip-lead");

        var board = await BoardAsync(owner, camp);
        board.GetProperty("leads").GetInt32().ShouldBe(3);

        // Grouped by the state the place itself carries, in the vocabulary's own order, and
        // ordered inside a group by how promising each looked.
        var groups = board.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("state").GetString()).ShouldBe(["open", "checked"]);
        Names(groups[0]).ShouldBe(["Named lead", "Visited rift"]);
        Names(groups[1]).ShouldBe(["Surveyed aven"]);
    }

    /// <summary>
    /// A lead the reader may not place exactly is not on their board at all — not blurred, not
    /// named without its position, absent. The position is not what is being withheld; the place
    /// is, because a way on nobody has been through is worth exactly as much to somebody who
    /// should not have it as the coordinate would be.
    /// </summary>
    [Fact]
    public async Task A_lead_the_reader_may_not_place_is_off_the_board_until_they_may()
    {
        var camp = await CreateCampAsync("Guarded karst");
        var trip = await CreateTripAsync("Prospecting");
        await AddTripAsync(camp, trip);

        var open = await CreateContinuationAsync("Open shaft", state: "open", grade: "B");
        var guarded = await CreateContinuationAsync(
            "Guarded shaft", state: "open", grade: "A", locationProtected: true);
        await NameAsync(trip, open, "trip-lead");
        await NameAsync(trip, guarded, "trip-lead");

        // The reader may read both — an Editor reads every content domain — and may place neither
        // by any grant of their own. Only the protection rule separates the two answers.
        Names(await BoardAsync(reader, camp)).ShouldBe(["Open shaft"]);

        // The owner of the guarded lead sees it all along, which is what proves the absence above
        // is the rule rather than a missing link or an empty query.
        Names(await BoardAsync(owner, camp)).ShouldBe(["Guarded shaft", "Open shaft"], ignoreOrder: true);

        await GrantAsync("feature", guarded, readerId, "Read,ViewExactLocation");

        Names(await BoardAsync(reader, camp)).ShouldBe(["Guarded shaft", "Open shaft"], ignoreOrder: true);
    }

    /// <summary>
    /// The board is answered as the caller may see the camp, exactly as its map and its roll-up
    /// are: a lead found on a member trip this caller may not read is not theirs to be told about.
    /// </summary>
    [Fact]
    public async Task A_lead_found_on_a_trip_the_reader_may_not_read_is_not_on_their_board()
    {
        var camp = await CreateCampAsync("Shared camp");
        var shared = await CreateTripAsync("Shared trip");
        var hidden = await CreateTripAsync("Hidden trip");
        await AddTripAsync(camp, shared);
        await AddTripAsync(camp, hidden);

        var sharedLead = await CreateContinuationAsync("Shared crawl", state: "open", grade: "A");
        var hiddenLead = await CreateContinuationAsync("Hidden crawl", state: "open", grade: "A");
        await NameAsync(shared, sharedLead, "trip-lead");
        await NameAsync(hidden, hiddenLead, "trip-lead");

        // The Viewer is let into the camp, into one of its trips, and into both places — so the
        // only thing that can separate the two leads is which trip named them. A Viewer holds no
        // read over places at all, which is why both grants are needed for the test to mean this.
        await GrantAsync("expedition", camp, outsiderId, "Read");
        await GrantAsync("tripLog", shared, outsiderId, "Read");
        await GrantAsync("feature", sharedLead, outsiderId, "Read,ViewExactLocation");
        await GrantAsync("feature", hiddenLead, outsiderId, "Read,ViewExactLocation");

        Names(await BoardAsync(outsider, camp)).ShouldBe(["Shared crawl"]);

        // The positive half, on the same camp and the same rows: the person who may read both
        // trips is shown both leads.
        Names(await BoardAsync(owner, camp)).ShouldBe(["Hidden crawl", "Shared crawl"], ignoreOrder: true);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static List<string> Names(JsonElement boardOrGroup) =>
        [.. (boardOrGroup.TryGetProperty("groups", out var groups)
                ? groups.EnumerateArray().SelectMany(g => g.GetProperty("leads").EnumerateArray())
                : boardOrGroup.GetProperty("leads").EnumerateArray())
            .Select(lead => lead.GetProperty("name").GetString() ?? string.Empty)];

    private static async Task<JsonElement> BoardAsync(HttpClient client, Guid campId)
    {
        var response = await client.GetAsync($"/api/v1/expeditions/{campId}/leads");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<Guid> CreateCampAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            caveIds = Array.Empty<Guid>(),
            geom = (object?)null,
            visibility = "private",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddTripAsync(Guid campId, Guid tripId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/expeditions/{campId}/trips", new
        {
            tripLogId = tripId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateContinuationAsync(
        string name, string state, string? grade = null, string? note = null,
        bool locationProtected = false)
    {
        // Built rather than written out, because the schema says nothing may be a string and null:
        // a lead nobody has graded carries no grade key at all, which is how one really arrives.
        var properties = new Dictionary<string, string> { ["state"] = state };
        if (grade is not null)
        {
            properties["grade"] = grade;
        }

        if (note is not null)
        {
            properties["note"] = note;
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = continuationTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.4455, 45.5301 } },
            properties,
            locationProtected,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Records that a trip is about a place, under one of the trip roles.</summary>
    private async Task NameAsync(Guid tripId, Guid featureId, string roleCode)
    {
        var types = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        var roleId = types.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == roleCode)
            .GetProperty("id").GetInt64();

        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = roleId,
            description = (string?)null,
            members = new object[]
            {
                Member("tripLog", tripId, isMain: true),
                Member("feature", featureId, sortOrder: 1),
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static object Member(string targetType, Guid targetId, bool isMain = false, int sortOrder = 0) => new
    {
        targetType,
        targetId,
        isMain,
        sortOrder,
        note = (string?)null,
        anchorKind = "whole",
        anchor = (object?)null,
        anchorFileId = (Guid?)null,
    };

    private async Task GrantAsync(string routeType, Guid entityId, Guid subjectId, string actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/{routeType}/{entityId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId,
                    effect = "allow",
                    actions,
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
