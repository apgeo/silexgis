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
/// The expedition slice: the write path with its own date rule, what a request's shape refuses,
/// who may read and write a camp, the version precondition, the lifecycle table behind one
/// endpoint, and a list that pages the same rows whatever order the database felt like.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader, and it has to be: the seeded Editors group holds every content domain at
    // the widest scope, so an Editor who "cannot see" a camp proves nothing about visibility.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    public ExpeditionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xp-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xp-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xp-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xp-out-{suffix}@t.local");
    }

    [Fact]
    public async Task A_camp_is_written_read_back_edited_and_deleted()
    {
        var body = Body("Bihor summer camp", "2026-07-18", end: "2026-08-01");
        var created = await CreateAsync(body);
        created.GetProperty("name").GetString().ShouldBe("Bihor summer camp");
        created.GetProperty("startDate").GetString().ShouldBe("2026-07-18");
        created.GetProperty("endDate").GetString().ShouldBe("2026-08-01");
        created.GetProperty("state").GetString().ShouldBe("draft");
        created.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = created.GetProperty("id").GetGuid();

        var read = await ReadAsync(owner, id);
        read.GetProperty("id").GetGuid().ShouldBe(id);
        read.GetProperty("geom").GetProperty("type").GetString().ShouldBe("Polygon");

        // A camp shortened to the day it starts stores no end at all: the field means "and it
        // ran on to", so a surface that can only offer a range picks the same day twice and the
        // write path is what turns that into "one day".
        var shortened = Body("Bihor recce", "2026-07-18", end: "2026-07-18");
        var updated = await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", shortened);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var payload = await Json(updated);
        payload.GetProperty("name").GetString().ShouldBe("Bihor recce");
        payload.GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);

        (await owner.DeleteAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_request_the_shape_refuses_never_reaches_the_table()
    {
        var noName = Body(string.Empty, "2026-07-18");
        (await owner.PostAsJsonAsync("/api/v1/expeditions/", noName))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // An end before its start is a mistyped date. It is refused rather than quietly stored
        // as "one day", which is what dropping it would amount to.
        var backwards = Body("Backwards", "2026-07-18", end: "2026-07-01");
        (await owner.PostAsJsonAsync("/api/v1/expeditions/", backwards))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A geometry cannot be judged by the request validator — it arrives as free JSON — so
        // the write path checks it and answers with a code of its own.
        var brokenArea = new
        {
            name = "Broken area",
            description = (string?)null,
            startDate = "2026-07-18",
            endDate = (string?)null,
            geom = new { type = "Polygon", coordinates = new[] { new[] { new[] { 25.4, 45.5 } } } },
            cavingGroupId = (Guid?)null,
            visibility = "private",
        };
        var broken = await owner.PostAsJsonAsync("/api/v1/expeditions/", brokenArea);
        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await broken.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(broken)).ShouldBe("expedition.geometry_invalid");
    }

    /// <summary>
    /// A body that names no state at all. The vocabulary's first member is the zero value and
    /// every live state has a legal move back to it, so a request specifying nothing would move
    /// a camp to the workshop and answer 200 — un-announcing it on a body that asked for nothing.
    /// The refusal has to come from the request's shape, because the transition table cannot
    /// tell an absent field from a deliberate one.
    /// </summary>
    [Fact]
    public async Task A_transition_that_names_no_state_is_refused_rather_than_read_as_the_first_one()
    {
        var id = (await CreateAsync(Body("Stateless move", "2027-02-01"))).GetProperty("id").GetGuid();
        (await MoveAsync(id, "proposed")).GetProperty("state").GetString().ShouldBe("proposed");

        var empty = await owner.PostWithIfMatchAsync($"/api/v1/expeditions/{id}/state", new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync());

        // An explicit null is the same request said another way, and is refused the same.
        var nulled = await owner.PostWithIfMatchAsync(
            $"/api/v1/expeditions/{id}/state", new { state = (string?)null });
        nulled.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nulled.Content.ReadAsStringAsync());

        (await ReadAsync(owner, id)).GetProperty("state").GetString().ShouldBe("proposed");
    }

    /// <summary>
    /// The two rules a create has to pass before a row exists: holding Create in the camps'
    /// domain at all, and being entitled to bind the camp to the club it names. Neither has a
    /// row to be evaluated against, so neither is covered by the read and write assertions.
    /// </summary>
    [Fact]
    public async Task Creating_a_camp_takes_the_create_right_and_binding_one_to_a_club_takes_more()
    {
        // A plain reader holds no Create in the camps' domain, so the create never reaches the
        // table — and is refused under the code that says which rule refused it.
        var refused = await outsider.PostAsJsonAsync("/api/v1/expeditions/", Body("Not mine", "2026-09-01"));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(refused)).ShouldBe("access.create_forbidden");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"xp-cadm-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"xp-cadm-{suffix}@t.local");
        var clubId = (await Json(await admin.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Camp club {suffix}",
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        }))).GetProperty("id").GetGuid();

        // The owner holds domain-wide Create and is refused anyway: binding a camp to a club
        // hands that club's members whatever their ruleset grants over its content, so it takes
        // the club and not merely the right to create.
        var bound = new
        {
            name = "Club camp",
            description = "A camp.",
            startDate = "2026-09-01",
            endDate = (string?)null,
            geom = (object?)null,
            cavingGroupId = clubId,
            visibility = "private",
        };
        var pushed = await owner.PostAsJsonAsync("/api/v1/expeditions/", bound);
        pushed.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await pushed.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(pushed)).ShouldBe("access.caving_group_binding_forbidden");
    }

    /// <summary>
    /// Who may read and who may write a camp, asserted in one test in both directions: a caller
    /// holding nothing sees nothing, a Read grant opens the list and no more, a Write grant opens
    /// the edit, and taking the grant away takes the camp away again.
    /// </summary>
    [Fact]
    public async Task A_private_camp_is_reachable_only_through_a_grant_and_a_read_grant_is_not_a_write()
    {
        var id = (await CreateAsync(Body("Private camp", "2026-09-01"))).GetProperty("id").GetGuid();

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/expeditions/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Nothing granted: the camp is not in the list and is not there to be edited either.
        (await ListIdsAsync(outsider)).ShouldNotContain(id);
        (await outsider.GetAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", Body("Renamed", "2026-09-01")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await GrantAsync(id, outsiderId, AccessAction.Read);

        (await ListIdsAsync(outsider)).ShouldContain(id);
        (await outsider.GetAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Read is not Write: the camp exists for the grantee now, so the refusal is 403, not 404.
        (await outsider.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", Body("Renamed", "2026-09-01")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PostWithIfMatchAsync($"/api/v1/expeditions/{id}/state", new { state = "proposed" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await GrantAsync(id, outsiderId, AccessAction.Read | AccessAction.Write);
        (await outsider.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", Body("Renamed", "2026-09-01")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await RevokeAsync(id, outsiderId);
        (await ListIdsAsync(outsider)).ShouldNotContain(id);
        (await outsider.GetAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editing_and_moving_a_camp_are_checked_against_the_version_the_caller_loaded()
    {
        var id = (await CreateAsync(Body("Versioned camp", "2026-09-10"))).GetProperty("id").GetGuid();
        var body = Body("Versioned camp", "2026-09-10");

        var noHeader = await owner.PutAsJsonAsync($"/api/v1/expeditions/{id}", body);
        noHeader.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await ProblemCodeAsync(noHeader)).ShouldBe("concurrency.if_match_required");

        var stale = await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", body, "\"0\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemCodeAsync(stale)).ShouldBe("concurrency.version_mismatch");

        var get = await owner.GetAsync($"/api/v1/expeditions/{id}");
        var etag = get.Headers.ETag!.ToString();
        (await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{id}", body, etag))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The move carries the same precondition as the edit: announcing a camp acts on the
        // version somebody read, and the header is what stops it acting on another one.
        var moveNoHeader = await owner.PostAsJsonAsync($"/api/v1/expeditions/{id}/state", new { state = "proposed" });
        moveNoHeader.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await ProblemCodeAsync(moveNoHeader)).ShouldBe("concurrency.if_match_required");

        var moveStale = await owner.PostWithIfMatchAsync(
            $"/api/v1/expeditions/{id}/state", new { state = "proposed" }, "\"0\"");
        moveStale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemCodeAsync(moveStale)).ShouldBe("concurrency.version_mismatch");
    }

    /// <summary>
    /// The lifecycle as one endpoint: the ladder a camp climbs, and every shape of refusal the
    /// table produces — a rung skipped, a state a camp cannot come back from, a state asked for
    /// twice — each under the one code that names the reason.
    /// </summary>
    [Fact]
    public async Task The_lifecycle_admits_the_planning_ladder_and_refuses_every_move_the_table_lacks()
    {
        var id = (await CreateAsync(Body("Planned camp", "2027-07-01", end: "2027-07-15")))
            .GetProperty("id").GetGuid();

        // Skipping a rung is refused: each rung is a decision somebody takes.
        await RefusedAsync(id, "confirmed");
        // So is the state it is already in — asking for it is not a move.
        await RefusedAsync(id, "draft");
        // And so is being put back, which only a camp with dates to put back can be.
        await RefusedAsync(id, "delayed");

        (await MoveAsync(id, "proposed")).GetProperty("state").GetString().ShouldBe("proposed");
        (await MoveAsync(id, "planned")).GetProperty("state").GetString().ShouldBe("planned");
        (await MoveAsync(id, "delayed")).GetProperty("state").GetString().ShouldBe("delayed");
        // Out of being put back the camp goes to being organised, never straight to going ahead:
        // new dates have to be settled before anybody is told it is on again.
        await RefusedAsync(id, "confirmed");
        (await MoveAsync(id, "planned")).GetProperty("state").GetString().ShouldBe("planned");
        (await MoveAsync(id, "confirmed")).GetProperty("state").GetString().ShouldBe("confirmed");
        (await MoveAsync(id, "done")).GetProperty("state").GetString().ShouldBe("done");

        // A camp that happened cannot be made not to have happened.
        await RefusedAsync(id, "cancelled");

        (await MoveAsync(id, "published")).GetProperty("state").GetString().ShouldBe("published");

        // Read back rather than trusting the value just returned: the column holds microseconds
        // and the value the write had in hand carries finer ticks than that, so comparing one
        // against the other would fail on storage precision rather than on the rule under test.
        var announced = (await ReadAsync(owner, id)).GetProperty("publishedAt").GetDateTimeOffset();

        // De-announcing and announcing again does not move the day it was first made known.
        (await MoveAsync(id, "draft")).GetProperty("state").GetString().ShouldBe("draft");
        (await MoveAsync(id, "published")).GetProperty("state").GetString().ShouldBe("published");
        (await ReadAsync(owner, id)).GetProperty("publishedAt").GetDateTimeOffset().ShouldBe(announced);
    }

    /// <summary>
    /// Paging over camps that start on the same day. The order has to distinguish them or a row
    /// appears on two pages, or on none, as the database chooses — and nothing on the page says
    /// so. Sorting on a timestamp instead only moves the tie: rows written in one tick tie again.
    /// </summary>
    [Fact]
    public async Task Camps_starting_the_same_day_page_without_losing_or_repeating_one()
    {
        var day = "2028-03-01";
        var made = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            made.Add((await CreateAsync(Body($"Same-day camp {i}", day))).GetProperty("id").GetGuid());
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 20; page++)
        {
            var body = await Json(await owner.GetAsync($"/api/v1/expeditions/?page={page}&pageSize=2"));
            var items = body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetGuid()).ToList();
            if (items.Count == 0)
            {
                break;
            }

            seen.AddRange(items);
        }

        var ours = seen.Where(made.Contains).ToList();
        ours.Distinct().Count().ShouldBe(ours.Count, "a row must not appear on two pages");
        ours.ShouldBe(made, ignoreOrder: true);
    }

    /// <summary>
    /// A rule scoped to one camp resolves its anchor against the camp table — which is the whole
    /// reason a camp has a resource domain of its own rather than riding the trips'. Under the
    /// trip domain the very same id would be looked for among trips, found nowhere, and the rule
    /// refused; so the acceptance below and the refusal beside it are the same assertion taken
    /// from both sides. Nothing here has compiler pressure behind it: an unlisted domain falls to
    /// the resolver's permissive default and every rule anchored on a camp would be accepted
    /// whether or not the camp existed.
    /// </summary>
    [Fact]
    public async Task A_rule_on_one_camp_is_anchored_against_the_camps_and_refused_when_there_is_none()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"xp-adm-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"xp-adm-{suffix}@t.local");

        var groupId = (await Json(await admin.PostAsJsonAsync("/api/v1/permission-groups/", new
        {
            name = $"Camp sharing {suffix}",
            description = (string?)null,
        }))).GetProperty("id").GetGuid();

        var id = (await CreateAsync(Body("Anchored camp", "2026-11-02"))).GetProperty("id").GetGuid();

        var accepted = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new
                {
                    effect = "allow", domain = "expeditions", actions = "read",
                    scopeKind = "object", scopeId = id,
                },
            },
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        // A rule anchored on a camp that does not exist would be one nobody could ever find again.
        var dangling = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new
                {
                    effect = "allow", domain = "expeditions", actions = "read",
                    scopeKind = "object", scopeId = Guid.CreateVersion7(),
                },
            },
        });
        dangling.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(dangling)).ShouldBe("access_entry.anchor_not_found");

        // Deleting the camp takes its rules with it. Left behind, they would be exactly what
        // the integrity check calls an orphan — and the rule that refuses to create one would
        // then refuse to save the very ruleset that already holds it.
        (await owner.DeleteAsync($"/api/v1/expeditions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.AccessEntries.AnyAsync(e => e.ScopeId == id)).ShouldBeFalse();
        }
    }

    private static object Body(string name, string startDate, string? end = null) => new
    {
        name,
        description = "A camp.",
        startDate,
        endDate = end,
        geom = new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { 25.40, 45.50 }, new[] { 25.50, 45.50 }, new[] { 25.50, 45.56 },
                    new[] { 25.40, 45.56 }, new[] { 25.40, 45.50 },
                },
            },
        },
        cavingGroupId = (Guid?)null,
        visibility = "private",
    };

    private async Task<JsonElement> CreateAsync(object body)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/expeditions/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private async Task<JsonElement> MoveAsync(Guid id, string state)
    {
        var response = await owner.PostWithIfMatchAsync($"/api/v1/expeditions/{id}/state", new { state });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>Asserts a move is refused, and refused under the one code that names the reason.</summary>
    private async Task RefusedAsync(Guid id, string state)
    {
        var response = await owner.PostWithIfMatchAsync($"/api/v1/expeditions/{id}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(response)).ShouldBe(ActivityStates.ExpeditionTransitionInvalidCode);
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/expeditions/?pageSize=500");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        return [.. body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    /// <summary>
    /// A rule on this one camp, written straight to the table. The per-object access route does
    /// not know expeditions yet — the shared entity vocabulary it parses gains its member with
    /// the rest of the polymorphic surfaces — so this is what an object-scoped rule looks like
    /// until then. Non-feature domains anchor an object scope in the plain scope id.
    /// </summary>
    private async Task GrantAsync(Guid expeditionId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AccessEntries
            .Where(e => e.SubjectId == userId && e.Domain == AccessDomain.Expeditions
                && e.ScopeKind == AccessScopeKind.Object && e.ScopeId == expeditionId)
            .ExecuteDeleteAsync();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Expeditions,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = expeditionId,
        });
        await db.SaveChangesAsync();
    }

    private async Task RevokeAsync(Guid expeditionId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AccessEntries
            .Where(e => e.SubjectId == userId && e.Domain == AccessDomain.Expeditions
                && e.ScopeKind == AccessScopeKind.Object && e.ScopeId == expeditionId)
            .ExecuteDeleteAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
