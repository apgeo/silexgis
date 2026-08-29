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
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The doors a person actually authors an event through — the list, the create, the edit, the
/// delete — and the one that hands a single event to somebody by name.
/// </summary>
/// <remarks>
/// The accounts every negative case is about are plain readers. The seeded editing membership
/// reads and writes past visibility at the widest scope by design, so "an editor could not see
/// it" would prove nothing about the audience — only that the account held no editing rights.
/// Each negative assertion sits beside the positive one it is the shadow of.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class EventAuthoringTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient clubmate = null!;
    private HttpClient outsider = null!;
    private HttpClient anonymous = null!;

    private Guid ownerId;
    private Guid outsiderId;
    private Guid cavingGroupId;

    public EventAuthoringTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"eva-own-{suffix}@t.local");
        var clubmateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"eva-club-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"eva-out-{suffix}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Authoring Club {suffix}", Slug = $"auth-club-{suffix}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"eva-own-{suffix}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"eva-club-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"eva-out-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Nobody_signed_out_reaches_any_door_into_an_event()
    {
        (await anonymous.GetAsync("/api/v1/events")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/events/defaults")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/events", Body($"Anon {suffix}", new DateOnly(2054, 1, 1))))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the same doors answer a signed-in reader, so the assertions above are about being
        // signed out rather than about the routes being unreachable.
        (await outsider.GetAsync("/api/v1/events")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync("/api/v1/events/defaults")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The audience a form shows before the event exists is the audience the create applies,
    /// because both ask the same rule. One club means that club; no single club means private —
    /// the narrow answer, never "every signed-in account".
    /// </summary>
    [Fact]
    public async Task An_event_created_without_an_audience_gets_the_one_its_form_was_shown()
    {
        var shown = await ReadJsonAsync(owner, "/api/v1/events/defaults");
        shown.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        shown.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        var created = await CreateAsync(owner, Body($"Silent {suffix}", new DateOnly(2054, 2, 2)));
        created.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        created.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        // The author who belongs to no single club gets the narrow answer rather than the wide
        // one: a row nobody but its author can read is merely useless, while handing it to the
        // whole installation is a widening nobody asked for.
        var lonely = await ReadJsonAsync(outsider, "/api/v1/events/defaults");
        lonely.GetProperty("visibility").GetString().ShouldBe("private");
        lonely.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_list_shows_a_reader_the_events_their_audience_admits_and_no_others()
    {
        var clubs = Id(await CreateAsync(owner, Body($"Club night {suffix}", new DateOnly(2054, 3, 3))));
        var mine = Id(await CreateAsync(owner, Body($"Private note {suffix}", new DateOnly(2054, 3, 4),
            visibility: "private", cavingGroupId: null)));

        (await ListIdsAsync(owner)).ShouldBe([mine, clubs], ignoreOrder: true);

        // The clubmate is a plain reader in the club the first event names, so she reaches that
        // one and not the author's private note. Both halves in one assertion: a walk returning
        // nothing would satisfy the negative on its own.
        (await ListIdsAsync(clubmate)).ShouldBe([clubs]);

        // In no club and granted nothing, so neither is there for her at all.
        (await ListIdsAsync(outsider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_list_refuses_a_kind_or_a_state_this_application_does_not_have()
    {
        var id = Id(await CreateAsync(owner, Body($"Filtered {suffix}", new DateOnly(2054, 4, 5),
            kind: "training")));

        // The filters that exist narrow rather than empty the list.
        (await ListIdsAsync(owner, "kind=training")).ShouldContain(id);
        (await ListIdsAsync(owner, "state=draft")).ShouldContain(id);
        (await ListIdsAsync(owner, "kind=clubMeeting")).ShouldNotContain(id);

        // A word the application does not have is a mistake to be told about, not a reason to
        // hand back an empty page that reads as "there are none".
        await RefusedAsync(owner, "/api/v1/events?kind=banana", "event.kind_invalid");
        await RefusedAsync(owner, "/api/v1/events?state=banana", "event.state_invalid");
    }

    [Fact]
    public async Task An_event_is_edited_and_deleted_by_who_may_write_it_and_by_nobody_else()
    {
        var id = Id(await CreateAsync(owner, Body($"Working day {suffix}", new DateOnly(2054, 5, 6),
            kind: "maintenanceDay")));

        var renamed = Body($"Working day moved {suffix}", new DateOnly(2054, 5, 7),
            kind: "maintenanceDay", place: "The hut");
        var edited = await owner.PutWithIfMatchAsync($"/api/v1/events/{id}", renamed);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var afterEdit = await ReadJsonAsync(owner, $"/api/v1/events/{id}");
        afterEdit.GetProperty("place").GetString().ShouldBe("The hut");

        // The edit named no audience, so the audience is exactly where it was — both halves of
        // it. Writing the binding on its own would leave a club-visible event naming no club,
        // which admits nobody, and it would do it silently from any surface that drew neither
        // field.
        afterEdit.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        afterEdit.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        // A clubmate may read it, so she is told she may not change it rather than that it is
        // missing; the outsider may not read it at all, so she is told nothing beyond that.
        (await clubmate.PutAsJsonAsync($"/api/v1/events/{id}", renamed))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PutAsJsonAsync($"/api/v1/events/{id}", renamed))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clubmate.DeleteAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await owner.DeleteAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A full edit carries the version it was written against, exactly as a trip's and a camp's
    /// do. Without it the second of two people correcting the same evening writes their form on
    /// top of a version they never saw, and the first person's change disappears with neither of
    /// them told.
    /// </summary>
    [Fact]
    public async Task An_edit_written_against_no_version_is_refused_and_one_written_against_a_stale_one_is_too()
    {
        var id = Id(await CreateAsync(owner, Body($"Committee night {suffix}", new DateOnly(2054, 5, 20))));
        var moved = Body($"Committee night {suffix}", new DateOnly(2054, 5, 21));

        var blind = await owner.PutAsJsonAsync($"/api/v1/events/{id}", moved);
        blind.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired,
            await blind.Content.ReadAsStringAsync());
        (await blind.Content.ReadAsStringAsync()).ShouldContain("concurrency.if_match_required");

        // The version the detail read hands back is the one the edit is accepted against.
        var read = await owner.GetAsync($"/api/v1/events/{id}");
        var version = read.Headers.ETag!.ToString();
        var accepted = await owner.PutWithIfMatchAsync($"/api/v1/events/{id}", moved, version);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        // And that version is spent: the same token replayed is the stale-write case.
        var replayed = await owner.PutWithIfMatchAsync($"/api/v1/events/{id}", moved, version);
        replayed.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed,
            await replayed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_write_that_names_no_title_or_an_end_before_its_start_is_refused()
    {
        var noTitle = await owner.PostAsJsonAsync("/api/v1/events", Body(string.Empty, new DateOnly(2054, 6, 1)));
        noTitle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noTitle.Content.ReadAsStringAsync()).ShouldContain("validation.failed");

        var backwards = await owner.PostAsJsonAsync("/api/v1/events",
            Body($"Backwards {suffix}", new DateOnly(2054, 6, 10), endDate: new DateOnly(2054, 6, 9)));
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The same request with the dates the right way round is accepted, so the refusals above
        // are about the dates rather than about the route rejecting everything.
        var forwards = await CreateAsync(owner,
            Body($"Forwards {suffix}", new DateOnly(2054, 6, 10), endDate: new DateOnly(2054, 6, 12)));
        forwards.GetProperty("endDate").GetString().ShouldBe("2054-06-12");

        // An end equal to the start is one day rather than a range of itself: a date-range
        // control has no other way to say "one day", so it is accepted and stored as nothing.
        var oneDay = await CreateAsync(owner,
            Body($"One day {suffix}", new DateOnly(2054, 6, 20), endDate: new DateOnly(2054, 6, 20)));
        oneDay.GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The door the domain exists for: one entry naming one event, authored through the same
    /// route every other shareable object is shared through.
    /// </summary>
    [Fact]
    public async Task One_event_is_handed_to_one_person_by_name_through_the_object_access_route()
    {
        var id = Id(await CreateAsync(owner, Body($"Shared night {suffix}", new DateOnly(2054, 7, 7),
            visibility: "private", cavingGroupId: null)));

        // Before the grant: the target parses, the route answers, and the reader is not admitted.
        (await outsider.GetAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var granted = await owner.PutAsJsonAsync($"/api/v1/objects/event/{id}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());

        (await outsider.GetAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListIdsAsync(outsider)).ShouldContain(id);

        // Reading is not writing: the entry named one action and confers that one only.
        (await outsider.PutAsJsonAsync($"/api/v1/events/{id}",
            Body($"Renamed by grantee {suffix}", new DateOnly(2054, 7, 7))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var effective = await ReadJsonAsync(outsider, $"/api/v1/objects/event/{id}/effective-access");
        (effective.GetProperty("actions").GetString() ?? string.Empty).ShouldContain("Read");

        // Withdrawing the entry withdraws the reach, so the grant above is what admitted her
        // rather than anything the account already held.
        var withdrawn = await owner.PutAsJsonAsync($"/api/v1/objects/event/{id}/access",
            new { entries = Array.Empty<object>() });
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_features_contain_other_objects_so_an_event_takes_no_subtree_rule()
    {
        var id = Id(await CreateAsync(owner, Body($"No subtree {suffix}", new DateOnly(2054, 8, 8))));

        var subtree = await owner.PutAsJsonAsync($"/api/v1/objects/event/{id}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "subtree",
                },
            },
        });
        subtree.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The object-scoped form of the very same entry is accepted, so the refusal is about the
        // reach asked for and not about the target.
        var scoped = await owner.PutAsJsonAsync($"/api/v1/objects/event/{id}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        scoped.StatusCode.ShouldBe(HttpStatusCode.OK, await scoped.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_an_event_takes_the_rules_anchored_on_it_with_it()
    {
        var id = Id(await CreateAsync(owner, Body($"Doomed {suffix}", new DateOnly(2054, 9, 9))));
        (await owner.PutAsJsonAsync($"/api/v1/objects/event/{id}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AccessEntries.CountAsync(e => e.ScopeId == id)).ShouldBe(1);

        (await owner.DeleteAsync($"/api/v1/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A rule whose anchor no longer resolves is exactly what the integrity check reports as
        // an orphan, so an ordinary delete must not leave one behind.
        (await db.AccessEntries.CountAsync(e => e.ScopeId == id)).ShouldBe(0);
    }

    private object Body(
        string title,
        DateOnly startDate,
        DateOnly? endDate = null,
        string kind = "clubMeeting",
        string? visibility = null,
        Guid? cavingGroupId = null,
        string? place = null) => new
        {
            title,
            kind,
            startDate = startDate.ToString("yyyy-MM-dd"),
            endDate = endDate?.ToString("yyyy-MM-dd"),
            place,
            visibility,
            cavingGroupId,
        };

    private async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/events", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task RefusedAsync(HttpClient client, string url, string code)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }

    /// <summary>
    /// The events this caller reaches, narrowed to the ones this class made — the fixture shares
    /// one database with every other suite, so an unnarrowed list would be the afternoon's other
    /// tests.
    /// </summary>
    private async Task<List<Guid>> ListIdsAsync(HttpClient client, string? query = null)
    {
        var url = $"/api/v1/events?pageSize=100&search={suffix}"
            + (query is null ? string.Empty : $"&{query}");
        var page = await ReadJsonAsync(client, url);
        return [.. page.GetProperty("items").EnumerateArray().Select(Id)];
    }

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
