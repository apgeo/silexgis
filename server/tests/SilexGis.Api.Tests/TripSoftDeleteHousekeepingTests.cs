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
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the rest of the application does about rows a deleted trip still holds.
/// </summary>
/// <remarks>
/// <para>
/// A deleted trip keeps its roster, its answers, its purpose and everything hung on it, so that
/// putting it back is exact — and the model hides all of it from ordinary reads. That is right
/// for a reader and wrong for housekeeping: the database still holds a person in place by the
/// roster row of a deleted trip, still refuses to drop a purpose a deleted trip names, and a
/// check that reads through the filter answers "nothing uses it" one statement before the
/// constraint says otherwise. Each test here is one such place, and each asserts the answer is a
/// reason somebody can act on rather than a fault.
/// </para>
/// </remarks>
public sealed class TripSoftDeleteHousekeepingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient admin = null!;
    private HttpClient reader = null!;
    private Guid readerId;

    public TripSoftDeleteHousekeepingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tsh-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tsh-adm-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tsh-rdr-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tsh-own-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tsh-adm-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tsh-rdr-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        admin.Dispose();
        reader.Dispose();
        factory.Dispose();
    }

    /// <summary>
    /// The roster of a deleted trip still holds its people in place, so removing one of them is
    /// refused with the remedy rather than failing on the constraint.
    /// </summary>
    [Fact]
    public async Task A_person_named_only_on_a_deleted_trip_cannot_be_deleted_and_is_told_why()
    {
        var person = await CreateCaverAsync($"Named Once {suffix}");
        var trip = await CreateTripAsync("The only trip they were on", person);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refused = await admin.DeleteAsync($"/api/v1/cavers/{person}");
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("caver.referenced_by_trips");

        // Once the trip has gone for good nothing holds them, and the same request goes through:
        // the refusal was the deleted trip's, not something else about this person.
        await DeletedTrips.AgePastTheWindowAsync(factory, trip);
        await DeletedTrips.RunPurgeAsync(factory);
        (await admin.DeleteAsync($"/api/v1/cavers/{person}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Merging a duplicate entry moves everything it holds onto the survivor, on deleted trips as
    /// on live ones. Left behind, the roster row would refuse the removal of the duplicate, and
    /// the answer would be taken by it without a word — so the restored trip would name somebody
    /// who no longer exists, or have lost an answer nobody withdrew.
    /// </summary>
    [Fact]
    public async Task Merging_a_duplicate_carries_their_place_on_a_deleted_trip_to_the_survivor()
    {
        var duplicate = await CreateCaverAsync($"Dup Entry {suffix}");
        var survivor = await CreateCaverAsync($"Real Entry {suffix}");
        var asked = await CreateCaverAsync($"Asked Entry {suffix}");
        var trip = await CreateTripAsync("A trip the duplicate was on", duplicate);
        // An answer held by a second duplicate, so both kinds of row are in play.
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/invitations/", new { caverId = asked }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var source in new[] { duplicate, asked })
        {
            var merged = await admin.PostAsJsonAsync(
                $"/api/v1/cavers/{survivor}/merge", new { sourceCaverId = source });
            merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());
            (await admin.GetAsync($"/api/v1/cavers/{source}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = await JsonAsync(owner, $"/api/v1/trip-logs/{trip}");
        restored.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("caverId").GetGuid()).ShouldBe([survivor]);
        var answers = await JsonAsync(owner, $"/api/v1/trip-logs/{trip}/invitations/");
        answers.GetProperty("invitations").EnumerateArray()
            .Select(i => i.GetProperty("caverId").GetGuid()).ShouldBe([survivor]);
    }

    /// <summary>
    /// A purpose or a role that only a deleted trip still holds is in use, and the refusal says
    /// which kind of trip is in the way — because retyping a trip nobody can open is not a remedy
    /// anybody can carry out.
    /// </summary>
    [Fact]
    public async Task A_purpose_and_a_role_held_only_by_a_deleted_trip_are_in_use_until_it_is_removed()
    {
        var typeId = await CreateVocabularyRowAsync("/api/v1/trip-types", $"bats_{suffix}", "Bat count");
        var roleId = await CreateVocabularyRowAsync("/api/v1/trip-participant-roles", $"cook_{suffix}", "Cook");
        var person = await CreateCaverAsync($"Camp Cook {suffix}");

        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"A trip with a purpose and a cook {suffix}",
            tripTypeId = typeId,
            tripDate = "2026-07-20",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = person, roleId } },
            visibility = "private",
            hadIncident = false,
        });
        var trip = await CreatedIdAsync(created);

        // While it is live the refusal is the ordinary one, with the ordinary remedy.
        (await ProblemAsync(await admin.DeleteAsync($"/api/v1/trip-types/{typeId}"), HttpStatusCode.Conflict, "trip_type.in_use"))
            .ShouldContain("retype them first");

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ProblemAsync(await admin.DeleteAsync($"/api/v1/trip-types/{typeId}"), HttpStatusCode.Conflict, "trip_type.in_use"))
            .ShouldContain("Deleted trips");
        (await ProblemAsync(
                await admin.DeleteAsync($"/api/v1/trip-participant-roles/{roleId}"),
                HttpStatusCode.Conflict,
                "trip_participant_role.in_use"))
            .ShouldContain("deleted trips");

        await DeletedTrips.AgePastTheWindowAsync(factory, trip);
        await DeletedTrips.RunPurgeAsync(factory);

        (await admin.DeleteAsync($"/api/v1/trip-types/{typeId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/trip-participant-roles/{roleId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The nightly check reads what points at nothing. A deleted trip is not nothing: everything
    /// hung on it is kept on purpose, and reporting it would fill the report for a month with
    /// rows that are exactly where they should be.
    /// </summary>
    [Fact]
    public async Task The_integrity_check_reports_nothing_about_what_a_deleted_trip_still_holds()
    {
        var person = await CreateCaverAsync($"Checked {suffix}");
        var trip = await CreateTripAsync("A trip with things hung on it", person);
        (await owner.PostAsJsonAsync(
                "/api/v1/taggings/", new { tagName = $"kept {suffix}", entityType = "tripLog", entityId = trip }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var granted = await owner.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = readerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The rows are there to be found — a check that passed over an empty table would say
        // nothing about the one it is meant to describe.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Taggings.CountAsync(t => t.EntityType == AttachedEntityType.TripLog && t.EntityId == trip)).ShouldBe(1);
        (await db.AccessEntries.CountAsync(e => e.ScopeId == trip)).ShouldBe(1);

        var problems = (await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync())
            .Where(p => p.Detail.Contains(trip.ToString()))
            .ToList();
        problems.ShouldBeEmpty(string.Join("; ", problems.Select(p => $"{p.Check}: {p.Detail}")));
    }

    /// <summary>
    /// A notice about a trip is re-decided when it is read. While the trip is deleted there is
    /// nothing to lead to, and the line says neither its name nor where it was; restored, the
    /// same stored row leads to it again.
    /// </summary>
    [Fact]
    public async Task A_notice_about_a_deleted_trip_names_nothing_until_the_trip_is_back()
    {
        var person = await CreateCaverAsync($"Noticed {suffix}");
        var title = $"A trip somebody was told about {suffix}";
        var trip = await CreateTripAsync(title, person);
        var granted = await owner.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = readerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());

        var before = await InboxAsync();
        before.ShouldContain($"/trip-logs/{trip}");
        before.ShouldContain(title);

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var during = await InboxAsync();
        during.ShouldNotContain($"/trip-logs/{trip}");
        during.ShouldNotContain(title);
        // The line is still there: that something happened is not what is withheld.
        JsonDocument.Parse(during).RootElement.GetProperty("totalItems").GetInt32()
            .ShouldBe(JsonDocument.Parse(before).RootElement.GetProperty("totalItems").GetInt32());

        (await owner.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await InboxAsync();
        after.ShouldContain($"/trip-logs/{trip}");
        after.ShouldContain(title);
    }

    // ---- fixtures

    private async Task<string> InboxAsync()
    {
        var response = await reader.GetAsync("/api/v1/notifications/");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    private async Task<Guid> CreateCaverAsync(string fullName)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/v1/cavers/", new { fullName, email = (string?)null, phone = (string?)null, notes = (string?)null });
        return await CreatedIdAsync(created);
    }

    private async Task<long> CreateVocabularyRowAsync(string route, string code, string name)
    {
        var created = await admin.PostAsJsonAsync(
            route, new { code, name, description = (string?)null, sortOrder = 500 });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
    }

    private async Task<Guid> CreateTripAsync(string title, Guid onIt)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}"[..Math.Min(100, title.Length + 33)],
            tripDate = "2026-07-20",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = onIt } },
            visibility = "private",
            hadIncident = false,
        });
        return await CreatedIdAsync(created);
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

    /// <summary>The refusal's own sentence, once its status and code have been checked.</summary>
    private static async Task<string> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }
}
