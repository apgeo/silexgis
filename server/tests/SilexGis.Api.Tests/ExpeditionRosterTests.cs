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
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The camp's own roster over HTTP: recording a stay, correcting it, removing it, and what the
/// listing says about how many people were there. The camp governs every one of these routes, so
/// what a caller may do with a stay is what they may do with the camp.
/// </summary>
public sealed class ExpeditionRosterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader, and it has to be: the seeded Editors group holds every content domain at
    // the widest scope, so an Editor who "cannot see" a camp proves nothing about visibility.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    private long memberRoleId;
    private long cookRoleId;

    public ExpeditionRosterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xrs-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xrs-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xrs-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xrs-out-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // By code, the way every seeded vocabulary is reached: the identity is an installation
        // detail and the code is what ships.
        memberRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == ExpeditionRosterRoleSeeds.MemberCode).Select(r => r.Id).SingleAsync();
        cookRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == "cook").Select(r => r.Id).SingleAsync();
    }

    [Fact]
    public async Task Somebody_who_was_there_twice_over_is_two_stays_and_one_person()
    {
        // The mistake this guards is a count of rows: the cook who also went as a member holds two
        // rows over days that overlap, and a camp reporting two people would be reporting one
        // person twice. Nothing raises when that happens — the number is simply too big.
        var camp = await CreateCampAsync("Bihor summer camp");
        var caver = await CreateCaverAsync("Cooked and stayed");

        var whole = await AddAsync(camp, caver, memberRoleId, "2026-07-18", "2026-08-01");
        var cooking = await AddAsync(camp, caver, cookRoleId, "2026-07-20", "2026-07-26");

        var roster = await RosterAsync(owner, camp);
        var entries = roster.GetProperty("entries").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);
        entries.Select(x => x.GetProperty("id").GetInt64()).ShouldBe([whole, cooking], ignoreOrder: true);
        roster.GetProperty("people").GetInt32().ShouldBe(1);

        // Overlapping stays are accepted rather than refused: the two above cover the same week,
        // and both are recorded. A person may also leave and come back, so two stays in one role
        // on one camp are ordinary too.
        var second = await AddAsync(camp, caver, memberRoleId, "2026-08-04", "2026-08-06");
        var again = await RosterAsync(owner, camp);
        again.GetProperty("entries").GetArrayLength().ShouldBe(3);
        again.GetProperty("people").GetInt32().ShouldBe(1);
        second.ShouldNotBe(whole);

        // A second person is a second person, so the count is of people and not simply of one.
        var other = await CreateCaverAsync("Drove the van");
        _ = await AddAsync(camp, other, memberRoleId, "2026-07-18", "2026-07-18");
        (await RosterAsync(owner, camp)).GetProperty("people").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_stay_of_one_day_comes_back_with_no_end_and_a_longer_one_keeps_its_own()
    {
        // A surface offering a range has no way to say "one day" other than by picking the same
        // day twice, so that is accepted and stored as nothing — one day never reads as a range of
        // itself. The pair is asserted together because a writer that dropped every end would pass
        // a test that only looked at the single day.
        var camp = await CreateCampAsync("Weekend");
        var caver = await CreateCaverAsync("Came for the day");

        var oneDay = await CreateResponseAsync(camp, caver, memberRoleId, "2026-07-20", "2026-07-20");
        (await Json(oneDay)).GetProperty("toDate").ValueKind.ShouldBe(JsonValueKind.Null);

        var ranOn = await CreateResponseAsync(camp, caver, cookRoleId, "2026-07-20", "2026-07-22");
        (await Json(ranOn)).GetProperty("toDate").GetString().ShouldBe("2026-07-22");

        var stored = (await RosterAsync(owner, camp)).GetProperty("entries").EnumerateArray()
            .ToDictionary(x => x.GetProperty("roleId").GetInt64(), x => x.GetProperty("toDate"));
        stored[memberRoleId].ValueKind.ShouldBe(JsonValueKind.Null);
        stored[cookRoleId].GetString().ShouldBe("2026-07-22");

        // A stay recorded with no end at all is the same row: nothing ran on past the first day.
        var open = await CreateResponseAsync(camp, caver, memberRoleId, "2026-07-25", null);
        (await Json(open)).GetProperty("toDate").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_stay_is_corrected_and_removed_and_a_role_nothing_ships_is_refused()
    {
        var camp = await CreateCampAsync("Corrections");
        var caver = await CreateCaverAsync("Arrived late");
        var entryId = await AddAsync(camp, caver, memberRoleId, "2026-07-18", "2026-08-01");

        var corrected = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/{entryId}",
            Body(caver, cookRoleId, "2026-07-21", "2026-08-01", "Arrived three days late."));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var body = await Json(corrected);
        body.GetProperty("fromDate").GetString().ShouldBe("2026-07-21");
        body.GetProperty("roleId").GetInt64().ShouldBe(cookRoleId);
        body.GetProperty("note").GetString().ShouldBe("Arrived three days late.");
        body.GetProperty("caverName").GetString().ShouldNotBeNullOrWhiteSpace();

        // A role id the vocabulary does not carry is a stable refusal and not a constraint
        // violation, and it is refused on the correction path exactly as on the recording one.
        var badRole = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/{entryId}",
            Body(caver, cookRoleId + 100_000, "2026-07-21", null, null));
        badRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(badRole)).ShouldBe("expedition_roster.role_unknown");

        var badCaver = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/",
            Body(Guid.NewGuid(), memberRoleId, "2026-07-21", null, null));
        badCaver.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(badCaver)).ShouldBe("expedition_roster.caver_unknown");

        // An end before the first day is a mistyped date, refused by the shape rather than quietly
        // turned into one day.
        var backwards = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/",
            Body(caver, memberRoleId, "2026-07-21", "2026-07-19", null));
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var removed = await owner.DeleteAsync($"/api/v1/expeditions/{camp}/roster/{entryId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
        (await RosterAsync(owner, camp)).GetProperty("entries").GetArrayLength().ShouldBe(0);

        var gone = await owner.DeleteAsync($"/api/v1/expeditions/{camp}/roster/{entryId}");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(gone)).ShouldBe("expedition_roster.not_found");
    }

    [Fact]
    public async Task A_camp_a_reader_may_not_reach_has_no_roster_for_them_and_reading_is_not_writing()
    {
        // The unreadable state is built rather than assumed: a private camp with no rule naming
        // the reader at all, and a reader who is a plain Viewer — an Editor would read past the
        // visibility by design and prove nothing. The camp the same reader may reach is asserted
        // in the same test, so a route that answered nothing to everybody would fail here.
        var hidden = await CreateCampAsync("Nobody else's camp");
        var shared = await CreateCampAsync("A camp with a reader");
        var caver = await CreateCaverAsync("Was at both");
        _ = await AddAsync(hidden, caver, memberRoleId, "2026-07-18", "2026-08-01");
        _ = await AddAsync(shared, caver, memberRoleId, "2026-07-18", "2026-08-01");

        var refused = await outsider.GetAsync($"/api/v1/expeditions/{hidden}/roster/");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(refused)).ShouldBe("expedition.not_found");

        await GrantAsync(AccessDomain.Expeditions, shared, outsiderId, AccessAction.Read);

        var allowed = await RosterAsync(outsider, shared);
        allowed.GetProperty("entries").GetArrayLength().ShouldBe(1);
        allowed.GetProperty("people").GetInt32().ShouldBe(1);

        // Reading a camp is not writing it: the same reader may not record, correct or remove a
        // stay, and is told so as a refusal rather than as a camp that does not exist.
        var write = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/roster/", Body(caver, cookRoleId, "2026-07-19", null, null));
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // On the camp they cannot read, a write is a camp that does not exist — a refusal must
        // never tell somebody that a camp they may not read is there.
        var writeHidden = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{hidden}/roster/", Body(caver, cookRoleId, "2026-07-19", null, null));
        writeHidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(writeHidden)).ShouldBe("expedition.not_found");

        // Nobody without an account is told where people were, whatever the camp's visibility
        // says. The whole API requires a sign-in today, so this holds the line for the day a
        // route here is opened to a token.
        using var anonymous = factory.CreateClient();
        var unauthenticated = await anonymous.GetAsync($"/api/v1/expeditions/{shared}/roster/");
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reading_a_camp_is_not_enough_to_be_told_who_was_at_it_and_for_which_days()
    {
        // A camp reaches a wider audience than the trips gathered into it, and a stay is a
        // fortnight where a trip is an afternoon — so who was at a camp takes the read over people
        // as well as the read over the camp, which is narrower than the rule a trip's own list of
        // people follows. Every account holds the read over people as shipped, so this is asserted
        // against an installation that has taken it away.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xrs-np-{suffix}@t.local");
        var camp = await CreateCampAsync("A camp anybody may read", "authenticated");
        var caver = await CreateCaverAsync("Was there");
        _ = await AddAsync(camp, caver, memberRoleId, "2026-07-18", "2026-08-01");

        using var reader = await AuthHelper.BearerClientAsync(factory, $"xrs-np-{suffix}@t.local");

        // As shipped: the camp is readable and so is its roster.
        (await RosterAsync(reader, camp)).GetProperty("people").GetInt32().ShouldBe(1);

        await DenyAsync(AccessDomain.Cavers, readerId, AccessAction.Read);

        // The camp itself is still readable — the roster alone is withheld, which is what makes
        // this a narrowing of the roster's gate and not simply a reader who lost the camp.
        (await reader.GetAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withheld = await reader.GetAsync($"/api/v1/expeditions/{camp}/roster/");
        withheld.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(withheld)).ShouldBe("expedition_roster.people_unreadable");
    }

    private async Task<long> AddAsync(Guid campId, Guid caverId, long roleId, string from, string? to)
    {
        var response = await CreateResponseAsync(campId, caverId, roleId, from, to);
        return (await Json(response)).GetProperty("id").GetInt64();
    }

    private async Task<HttpResponseMessage> CreateResponseAsync(
        Guid campId, Guid caverId, long roleId, string from, string? to)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/roster/", Body(caverId, roleId, from, to, null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response;
    }

    private static object Body(Guid caverId, long roleId, string from, string? to, string? note) => new
    {
        caverId,
        roleId,
        fromDate = from,
        toDate = to,
        note,
    };

    private static async Task<JsonElement> RosterAsync(HttpClient client, Guid campId)
    {
        var response = await client.GetAsync($"/api/v1/expeditions/{campId}/roster/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private async Task<Guid> CreateCampAsync(string name, string visibility = "private")
    {
        var body = new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = "2026-07-18",
            endDate = "2026-08-01",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility,
        };
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A person in the club's directory, written straight to the table: recording who was at a
    /// camp takes no authority over the directory, so these tests need none to make somebody to
    /// record.
    /// </summary>
    private async Task<Guid> CreateCaverAsync(string what)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = $"Camp {what} {Guid.NewGuid().ToString("N")[..6]}" };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    /// <summary>
    /// A rule on one row, written straight to the table: the per-object access route does not know
    /// expeditions yet, so this is what an object-scoped rule looks like until then.
    /// </summary>
    private async Task GrantAsync(AccessDomain domain, Guid objectId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = objectId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// An installation-wide refusal of one domain to one account, written straight to the table:
    /// what ships grants every account the read over people, so taking it away is the only way to
    /// stand where an installation that narrowed its directory stands.
    /// </summary>
    private async Task DenyAsync(AccessDomain domain, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
