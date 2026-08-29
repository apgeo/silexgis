// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Who may read a trip, and where that answer comes from.
/// <para>
/// Two doors create a trip and they differ in exactly one thing: the audience a request that
/// names none falls back to. A trip being prepared is a proposal to the people who might come on
/// it, so it starts visible to its author's caving group; a trip written up afterwards is one
/// person's account of something already over, so it starts private. Everything else about the
/// two is identical, the state included.
/// </para>
/// <para>
/// The other half of the file is the negative: where an activity has got to never decides who may
/// read it. A draft is not a security boundary — a trip marked public is public while it is still
/// being written, and one marked private stays shut after it is announced — and announcing never
/// widens an audience on the way past.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripAudienceTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient planner = null!;  // Editor, in exactly one caving group
    private HttpClient loner = null!;    // Editor, in no caving group
    private HttpClient mate = null!;     // Viewer, in the planner's caving group
    private HttpClient stranger = null!; // Viewer, in no group and holding nothing
    private Guid strangerId;
    private Guid cavingGroupId;
    private string cavingGroupName = null!;

    public TripAudienceTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        cavingGroupName = $"Audience Club {suffix}";

        var plannerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ta-plan-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ta-lone-{suffix}@t.local");
        var mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ta-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ta-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = cavingGroupName, Slug = $"audience-club-{suffix}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;

            await RosterHelper.AddMemberAsync(db, cavingGroupId, plannerId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, mateId);
        }

        planner = await AuthHelper.BearerClientAsync(factory, $"ta-plan-{suffix}@t.local");
        loner = await AuthHelper.BearerClientAsync(factory, $"ta-lone-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"ta-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"ta-str-{suffix}@t.local");
    }

    /// <summary>
    /// The two doors, side by side, with the same body through both. A proposal only its author
    /// can read is a proposal to nobody, and an account of a trip already over is nobody else's
    /// business until its author says so.
    /// </summary>
    [Fact]
    public async Task A_trip_being_planned_starts_with_the_author_s_group_and_one_written_up_starts_private()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var plan = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Plan {marker}");
        plan.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        plan.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        var report = await CreateAsync(planner, "/api/v1/trip-logs/", $"Report {marker}");
        report.GetProperty("visibility").GetString().ShouldBe("private");
        report.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);

        // What the two audiences mean, read by people who hold nothing of their own: a member of
        // the author's club may read the plan, and the same member may not read the report. The
        // stranger belongs to no group and holds no entry, so neither is theirs.
        var planId = plan.GetProperty("id").GetGuid();
        var reportId = report.GetProperty("id").GetGuid();
        (await mate.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mate.GetAsync($"/api/v1/trip-logs/{reportId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/trip-logs/{reportId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The fallback is the narrow answer. An author who belongs to no club would otherwise have
    /// their plan handed to every account on the installation, which is a widening nobody asked
    /// for; a plan only its author can read is merely useless, and naming an audience fixes it.
    /// </summary>
    [Fact]
    public async Task An_author_with_no_caving_group_plans_privately_rather_than_to_everybody()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var alone = await CreateAsync(loner, "/api/v1/trip-logs/plans", $"Alone {marker}");
        alone.GetProperty("visibility").GetString().ShouldBe("private");
        alone.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await mate.GetAsync($"/api/v1/trip-logs/{alone.GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The positive half, with the one thing that differs changed and nothing else: the same
        // request, through the same door, from an author who does belong to a club.
        var shared = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Shared {marker}");
        shared.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        (await mate.GetAsync($"/api/v1/trip-logs/{shared.GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A default only answers a question nobody else answered. An audience the request states is
    /// the author's own decision and is taken as it stands, through either door.
    /// </summary>
    [Fact]
    public async Task An_audience_the_request_names_is_the_one_the_trip_gets()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var narrowed = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Kept {marker}", "private");
        narrowed.GetProperty("visibility").GetString().ShouldBe("private");
        narrowed.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);

        var opened = await CreateAsync(planner, "/api/v1/trip-logs/", $"Opened {marker}", "authenticated");
        opened.GetProperty("visibility").GetString().ShouldBe("authenticated");
    }

    /// <summary>
    /// Answered before the trip exists, so a form can say who will see it rather than recite the
    /// rule — and it reports only the caller's own membership, and only when that membership is
    /// the single one that decides the answer.
    /// </summary>
    [Fact]
    public async Task The_planning_door_says_who_will_see_the_trip_before_it_exists()
    {
        var mine = await planner.GetFromJsonAsync<JsonElement>("/api/v1/trip-logs/plan-default");
        mine.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        mine.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);
        mine.GetProperty("cavingGroupName").GetString().ShouldBe(cavingGroupName);

        var none = await loner.GetFromJsonAsync<JsonElement>("/api/v1/trip-logs/plan-default");
        none.GetProperty("visibility").GetString().ShouldBe("private");
        none.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
        none.GetProperty("cavingGroupName").ValueKind.ShouldBe(JsonValueKind.Null);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-logs/plan-default"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Announcing a trip tells the people named on it; it does not hand the trip to anybody new.
    /// Widening an audience is a separate act somebody takes deliberately, which is why the
    /// control that announces asks first and names the audience it is announcing to.
    /// </summary>
    [Fact]
    public async Task Announcing_a_trip_never_widens_who_may_read_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var plan = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Announced {marker}");
        var planId = plan.GetProperty("id").GetGuid();

        await MoveAsync(planId, "published");

        var after = await planner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{planId}");
        after.GetProperty("state").GetString().ShouldBe("published");
        after.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        after.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        // The audience is the same set of people it was before the announcement, on both sides.
        (await stranger.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await mate.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A save that says nothing about the audience is not a save that narrows it. A surface may
    /// draw part of a trip without drawing who may read it, and sending back what it did draw
    /// must not quietly shut the trip.
    /// </summary>
    [Fact]
    public async Task An_edit_that_names_no_audience_leaves_the_one_the_trip_has()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var plan = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Edited {marker}");
        var planId = plan.GetProperty("id").GetGuid();

        var edited = await planner.PutWithIfMatchAsync($"/api/v1/trip-logs/{planId}", new
        {
            title = $"Edited {marker} again",
            tripDate = "2026-07-01",
            participants = Array.Empty<object>(),
            cavingGroupId,
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        var after = await planner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{planId}");
        after.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        (await mate.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The same save with the group binding left out too, which is the shape a surface that never
    /// drew the audience actually sends. The two values are one answer — a trip that says "the
    /// caving group" while naming no group admits nobody — so an edit that names neither must
    /// leave both where they are rather than half of them.
    /// </summary>
    [Fact]
    public async Task An_edit_that_names_neither_half_of_the_audience_leaves_both()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var plan = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Untouched {marker}");
        var planId = plan.GetProperty("id").GetGuid();
        plan.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        var edited = await planner.PutWithIfMatchAsync($"/api/v1/trip-logs/{planId}", new
        {
            title = $"Untouched {marker} again",
            tripDate = "2026-07-01",
            participants = Array.Empty<object>(),
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        var after = await planner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{planId}");
        after.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        after.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);

        // The half that matters to a person rather than to a column: the club still reads it.
        (await mate.GetAsync($"/api/v1/trip-logs/{planId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A request that names a group and no audience is still naming a group. The default answers
    /// only what the request left unstated, because dropping the binding would take the trip out
    /// of every rule written about that group's content — a refusal aimed at the group included.
    /// </summary>
    [Fact]
    public async Task A_group_the_request_names_survives_an_audience_it_leaves_to_the_default()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var response = await planner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Bound {marker}",
            tripDate = "2026-07-01",
            participants = Array.Empty<object>(),
            cavingGroupId,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);

        var created = JsonDocument.Parse(payload).RootElement;
        // The report door's default still decides the audience, which the request did not name.
        created.GetProperty("visibility").GetString().ShouldBe("private");
        created.GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);
    }

    /// <summary>
    /// The negative the whole design rests on: where a trip has got to is never consulted when
    /// deciding who may read it. A public trip is readable while it is still a draft, and a
    /// private one stays shut after it has been announced — and the same private trip opens the
    /// moment somebody is given an entry for it, which is the only thing that opens it.
    /// </summary>
    [Fact]
    public async Task Which_state_a_trip_is_in_never_decides_who_may_read_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // A draft is not a security boundary: marked public, it is public while it is being
        // written, and it stays that way through every state the trip passes.
        var open = await CreateAsync(planner, "/api/v1/trip-logs/plans", $"Open {marker}", "public");
        var openId = open.GetProperty("id").GetGuid();
        open.GetProperty("state").GetString().ShouldBe("draft");
        (await stranger.GetAsync($"/api/v1/trip-logs/{openId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var state in new[] { "proposed", "planned", "confirmed", "done", "published" })
        {
            await MoveAsync(openId, state);
            (await stranger.GetAsync($"/api/v1/trip-logs/{openId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The other way round, with a subject who genuinely holds nothing: a private trip is shut
        // to them in every state, announcement included.
        var shut = await CreateAsync(planner, "/api/v1/trip-logs/", $"Shut {marker}", "private");
        var shutId = shut.GetProperty("id").GetGuid();
        (await stranger.GetAsync($"/api/v1/trip-logs/{shutId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await MoveAsync(shutId, "published");
        (await stranger.GetAsync($"/api/v1/trip-logs/{shutId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the positive half: what opens a private trip is an entry naming the reader, not the
        // state the trip is in. The trip does not move for this.
        await GrantReadAsync(shutId, strangerId);
        (await stranger.GetAsync($"/api/v1/trip-logs/{shutId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(shutId, "draft");
        (await stranger.GetAsync($"/api/v1/trip-logs/{shutId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers ----

    private static async Task<JsonElement> CreateAsync(
        HttpClient client, string url, string title, string? visibility = null)
    {
        // The audience is left out of the body entirely when the test is about the default: a
        // request that names none is the only thing a default can answer.
        object body = visibility is null
            ? new { title, tripDate = "2026-07-01", participants = Array.Empty<object>() }
            : new { title, tripDate = "2026-07-01", participants = Array.Empty<object>(), visibility };

        var response = await client.PostAsJsonAsync(url, body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task MoveAsync(Guid tripId, string state)
    {
        var response = await planner.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task GrantReadAsync(Guid tripId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        planner?.Dispose();
        loner?.Dispose();
        mate?.Dispose();
        stranger?.Dispose();
        factory.Dispose();
    }
}
