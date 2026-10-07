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
using SilexGis.Domain.Profiles;
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

    [Fact]
    public async Task A_stay_may_name_somebody_the_directory_does_not_hold_and_that_adds_exactly_one_person()
    {
        // The cook who never signs in and never goes underground is the person a camp's roster
        // exists to record, and has no entry until somebody writes one. Naming them on the stay
        // is that act: one entry is made, and it is the one the stay points at.
        var camp = await CreateCampAsync("A camp with a cook");
        var name = $"Camp cook {Guid.NewGuid():N}";
        (await PeopleNamedAsync(name)).ShouldBe(0);

        var created = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(name, cookRoleId, "2026-07-18", "2026-08-01"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var stay = await Json(created);
        stay.GetProperty("caverName").GetString().ShouldBe(name);
        var caverId = stay.GetProperty("caverId").GetGuid();

        (await PeopleNamedAsync(name)).ShouldBe(1);
        var person = await owner.GetAsync($"/api/v1/cavers/{caverId}");
        person.StatusCode.ShouldBe(HttpStatusCode.OK, await person.Content.ReadAsStringAsync());
        (await Json(person)).GetProperty("name").GetString().ShouldBe(name);

        // Written again — on another camp, in another role, with the spaces a hurried hand
        // leaves round a name — it is the same person and not a second one. This is the half
        // that matters: a name that made somebody new every time it was typed would give a club
        // one entry per stay.
        var other = await CreateCampAsync("The next camp");
        var again = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{other}/roster/",
            NamedBody($"  {name} ", memberRoleId, "2026-07-18", null));
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        (await Json(again)).GetProperty("caverId").GetGuid().ShouldBe(caverId);
        (await PeopleNamedAsync(name)).ShouldBe(1);

        // And a second stay on the first camp under the same name is one person there twice.
        var second = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(name, memberRoleId, "2026-07-18", "2026-07-20"));
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var roster = await RosterAsync(owner, camp);
        roster.GetProperty("entries").GetArrayLength().ShouldBe(2);
        roster.GetProperty("people").GetInt32().ShouldBe(1);
        (await PeopleNamedAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task A_name_two_people_share_means_the_older_of_them_on_a_camp_exactly_as_on_a_trip()
    {
        // Two entries under one name is the state the directory's merge exists to resolve, and a
        // typed name has to pick one of them — the same one every time, and the same one whether
        // it was typed on a camp or on a trip. That last clause is what this asserts and what a
        // second copy of the rule would sooner or later break: the two records name the same
        // club's people.
        var sharedName = $"Ion Popescu {Guid.NewGuid():N}";
        Guid olderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var newer = new Caver { FullName = sharedName };
            var older = new Caver { FullName = sharedName };
            db.Cavers.AddRange(newer, older);
            await db.SaveChangesAsync();
            // Both rows are stamped with the same instant on insert, so which of them is the
            // older is set afterwards — and it is the one made second that is aged, so the
            // answer below cannot simply be the order the two were written in.
            older.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
            olderId = older.Id;
        }

        var camp = await CreateCampAsync("Shared names");
        var created = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(sharedName, memberRoleId, "2026-07-18", null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await Json(created)).GetProperty("caverId").GetGuid().ShouldBe(olderId);

        // Reused rather than made a third time.
        (await PeopleNamedAsync(sharedName)).ShouldBe(2);

        var trip = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Shared name {Guid.NewGuid():N}",
            tripDate = "2026-07-19",
            caveIds = Array.Empty<Guid>(),
            participants = new object[] { new { caverId = (Guid?)null, newCaverName = sharedName } },
            visibility = "private",
        });
        trip.StatusCode.ShouldBe(HttpStatusCode.Created, await trip.Content.ReadAsStringAsync());
        (await Json(trip)).GetProperty("participants").EnumerateArray().Single()
            .GetProperty("caverId").GetGuid().ShouldBe(olderId);
        (await PeopleNamedAsync(sharedName)).ShouldBe(2);
    }

    [Fact]
    public async Task Naming_the_person_both_ways_or_neither_is_refused_as_a_trip_refuses_it()
    {
        var camp = await CreateCampAsync("Ambiguous");
        var caver = await CreateCaverAsync("Known already");
        var stray = $"Also named {Guid.NewGuid():N}";

        // An entry and a name together: two ways of saying who, and no rule for which one wins.
        var both = await owner.PostAsJsonAsync($"/api/v1/expeditions/{camp}/roster/", new
        {
            caverId = (Guid?)caver,
            newCaverName = stray,
            roleId = memberRoleId,
            fromDate = "2026-07-18",
        });
        both.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await both.Content.ReadAsStringAsync());

        // Neither: nobody is named. A name of nothing but spaces is no name, and must not become
        // a person with no name whom nobody could find again to merge away.
        var neither = await owner.PostAsJsonAsync($"/api/v1/expeditions/{camp}/roster/", new
        {
            caverId = (Guid?)null,
            newCaverName = (string?)null,
            roleId = memberRoleId,
            fromDate = "2026-07-18",
        });
        neither.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await neither.Content.ReadAsStringAsync());
        var blank = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody("   ", memberRoleId, "2026-07-18", null));
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await blank.Content.ReadAsStringAsync());

        // The same mistake made on a trip, so that "as a trip refuses it" is read off a trip
        // rather than off a constant in this file: the same status, the same code, and the same
        // list of what was wrong beside it.
        var onATrip = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Ambiguous {Guid.NewGuid():N}",
            tripDate = "2026-07-19",
            caveIds = Array.Empty<Guid>(),
            participants = new object[] { new { caverId = (Guid?)caver, newCaverName = stray } },
            visibility = "private",
        });
        onATrip.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await onATrip.Content.ReadAsStringAsync());
        var tripCode = await ProblemCodeAsync(onATrip);
        tripCode.ShouldBe("validation.failed");
        foreach (var refused in new[] { both, neither, blank })
        {
            var problem = await Json(refused);
            problem.GetProperty("code").GetString().ShouldBe(tripCode);
            problem.GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Object);
        }

        // The correction path refuses the same shapes: it is the same request.
        var entryId = await AddAsync(camp, caver, memberRoleId, "2026-07-18", null);
        var corrected = await owner.PutAsJsonAsync($"/api/v1/expeditions/{camp}/roster/{entryId}", new
        {
            caverId = (Guid?)caver,
            newCaverName = stray,
            roleId = memberRoleId,
            fromDate = "2026-07-18",
        });
        corrected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(corrected)).ShouldBe(tripCode);

        // Nothing came of any of it: the one stay recorded properly, and nobody under the name.
        (await RosterAsync(owner, camp)).GetProperty("entries").GetArrayLength().ShouldBe(1);
        (await PeopleNamedAsync(stray)).ShouldBe(0);
    }

    [Fact]
    public async Task A_name_is_as_long_as_the_directory_can_hold_and_no_longer()
    {
        var camp = await CreateCampAsync("Long names");

        // The limit a record accepts and the length the directory stores are the same number,
        // read here off the two places that hold it: a record accepting a longer name would
        // accept a person it then cannot make an entry for.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.Model.FindEntityType(typeof(Caver))!.FindProperty(nameof(Caver.FullName))!.GetMaxLength()
                .ShouldBe(CaverReferenceRules.NameMaxLength);
        }

        var marker = Guid.NewGuid().ToString("N");
        var longest = marker + new string('x', CaverReferenceRules.NameMaxLength - marker.Length);
        longest.Length.ShouldBe(CaverReferenceRules.NameMaxLength);

        var tooLong = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(longest + "y", memberRoleId, "2026-07-18", null));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await tooLong.Content.ReadAsStringAsync());
        (await ProblemCodeAsync(tooLong)).ShouldBe("validation.failed");

        var fits = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(longest, memberRoleId, "2026-07-18", null));
        fits.StatusCode.ShouldBe(HttpStatusCode.Created, await fits.Content.ReadAsStringAsync());
        (await Json(fits)).GetProperty("caverName").GetString().ShouldBe(longest);
        (await PeopleNamedAsync(longest)).ShouldBe(1);

        // The directory's own door states that length for itself, as a number of its own, so it
        // is asked too — through somebody who keeps the directory, since nobody else may add to
        // it there. A name it accepts and a record refuses, or the other way about, would be two
        // surfaces disagreeing about what a person may be called.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"xrs-keep-{suffix}@t.local");
        using var keeper = await AuthHelper.BearerClientAsync(factory, $"xrs-keep-{suffix}@t.local");
        var other = Guid.NewGuid().ToString("N");
        var atTheLimit = other + new string('z', CaverReferenceRules.NameMaxLength - other.Length);
        (await keeper.PostAsJsonAsync("/api/v1/cavers/", new { fullName = atTheLimit + "z" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var kept = await keeper.PostAsJsonAsync("/api/v1/cavers/", new { fullName = atTheLimit });
        kept.StatusCode.ShouldBe(HttpStatusCode.Created, await kept.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_request_that_is_turned_away_leaves_nobody_behind_in_the_directory()
    {
        // Resolving a name may add a person, so it has to come after everything that can refuse
        // the request. Each refusal below carries a name nobody holds, and after each of them
        // nobody holds it still. The reader is a plain Viewer: an Editor reads and writes past a
        // camp's visibility by design, and would prove nothing here.
        var hidden = await CreateCampAsync("Not the reader's camp");
        var shared = await CreateCampAsync("A camp the reader may read");
        await GrantAsync(AccessDomain.Expeditions, shared, outsiderId, AccessAction.Read);
        var name = $"Never written {Guid.NewGuid():N}";

        // Reading a camp is not writing it, by name any more than by entry.
        var asReader = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/roster/", NamedBody(name, cookRoleId, "2026-07-19", null));
        asReader.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PeopleNamedAsync(name)).ShouldBe(0);

        // A camp they cannot read is a camp that is not there.
        var unseen = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{hidden}/roster/", NamedBody(name, cookRoleId, "2026-07-19", null));
        unseen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unseen)).ShouldBe("expedition.not_found");
        (await PeopleNamedAsync(name)).ShouldBe(0);

        // Somebody who may write the camp, refused for a role nothing ships: still nobody.
        var badRole = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/roster/", NamedBody(name, cookRoleId + 100_000, "2026-07-19", null));
        badRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(badRole)).ShouldBe("expedition_roster.role_unknown");
        (await PeopleNamedAsync(name)).ShouldBe(0);

        // And a correction aimed at a stay the camp does not have.
        var noSuchStay = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{shared}/roster/{long.MaxValue}",
            NamedBody(name, cookRoleId, "2026-07-19", null));
        noSuchStay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(noSuchStay)).ShouldBe("expedition_roster.not_found");
        (await PeopleNamedAsync(name)).ShouldBe(0);

        // The same request, from somebody who may make it and with nothing wrong with it, is
        // what does add them — so the zeros above are refusals and not a name that never works.
        var accepted = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/roster/", NamedBody(name, cookRoleId, "2026-07-19", null));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        (await PeopleNamedAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Writing_the_camp_is_the_only_right_that_naming_a_new_person_on_it_takes()
    {
        // A trip asks nothing over the directory of whoever names a new person on it: recording
        // who was there is part of writing the record. A camp's roster asks the same, and this
        // holds it there. The writer is a plain Viewer holding Write on this one camp — the
        // directory's own door is closed to them, which is asserted rather than assumed, so the
        // stay below is admitted by the camp's rule alone.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var writerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xrs-wr-{suffix}@t.local");
        var camp = await CreateCampAsync("A camp with a keeper");
        await GrantAsync(AccessDomain.Expeditions, camp, writerId, AccessAction.Read | AccessAction.Write);
        using var writer = await AuthHelper.BearerClientAsync(factory, $"xrs-wr-{suffix}@t.local");

        var name = $"Base camp keeper {Guid.NewGuid():N}";
        (await writer.PostAsJsonAsync("/api/v1/cavers/", new { fullName = name }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PeopleNamedAsync(name)).ShouldBe(0);

        var created = await writer.PostAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/", NamedBody(name, memberRoleId, "2026-07-18", "2026-08-01"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await PeopleNamedAsync(name)).ShouldBe(1);

        // The same keeper on a camp they hold nothing on is refused, as anybody would be: the
        // right is over the camp, and it is the camp that is asked.
        var elsewhere = await CreateCampAsync("Somebody else's camp");
        var refused = await writer.PostAsJsonAsync(
            $"/api/v1/expeditions/{elsewhere}/roster/",
            NamedBody($"Never there {Guid.NewGuid():N}", memberRoleId, "2026-07-18", null));
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Correcting_a_stay_may_say_it_was_somebody_else_by_name_or_by_entry()
    {
        var camp = await CreateCampAsync("Written down wrong");
        var first = await CreateCaverAsync("Not the one who came");
        var entryId = await AddAsync(camp, first, memberRoleId, "2026-07-18", "2026-08-01");

        // By a name nobody holds: the stay is repointed at somebody new, in the one save.
        var name = $"The one who came {Guid.NewGuid():N}";
        var renamed = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/{entryId}",
            NamedBody(name, cookRoleId, "2026-07-18", "2026-08-01", "Came in place of the first."));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var body = await Json(renamed);
        var cameId = body.GetProperty("caverId").GetGuid();
        cameId.ShouldNotBe(first);
        body.GetProperty("caverName").GetString().ShouldBe(name);
        body.GetProperty("roleId").GetInt64().ShouldBe(cookRoleId);
        (await PeopleNamedAsync(name)).ShouldBe(1);

        // Saved again unchanged, as a form does: the name now means the person it made, and
        // nobody further is added.
        var again = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/{entryId}",
            NamedBody(name, cookRoleId, "2026-07-18", "2026-08-01", "Came in place of the first."));
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await Json(again)).GetProperty("caverId").GetGuid().ShouldBe(cameId);
        (await PeopleNamedAsync(name)).ShouldBe(1);

        // And back to an entry, by the entry. The person the correction added stays in the
        // directory: taking a stay away from somebody does not unmake them.
        var back = await owner.PutAsJsonAsync(
            $"/api/v1/expeditions/{camp}/roster/{entryId}",
            Body(first, memberRoleId, "2026-07-18", "2026-08-01", null));
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
        (await Json(back)).GetProperty("caverId").GetGuid().ShouldBe(first);
        (await PeopleNamedAsync(name)).ShouldBe(1);

        var roster = await RosterAsync(owner, camp);
        roster.GetProperty("entries").GetArrayLength().ShouldBe(1);
        roster.GetProperty("people").GetInt32().ShouldBe(1);
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

    /// <summary>A stay that says who by name rather than by their entry in the directory.</summary>
    private static object NamedBody(
        string name, long roleId, string from, string? to, string? note = null) => new
        {
            caverId = (Guid?)null,
            newCaverName = name,
            roleId,
            fromDate = from,
            toDate = to,
            note,
        };

    /// <summary>
    /// How many entries the directory holds under exactly this name — read off the table, so
    /// that "one person was added" is a count and not an inference from an answer.
    /// </summary>
    private async Task<int> PeopleNamedAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cavers.CountAsync(c => c.FullName == name);
    }

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
