// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Sharing a camp reaching the trips it gathered. Four things are driven here because each of
/// them is a way the obvious implementation goes wrong: the writer adds rather than replaces,
/// so a trip's own rules survive; every row still passes the no-amplification bound against its
/// own trip's facts; a camp holding one trip the organiser may not administer refuses the whole
/// sharing and says only how many; and exact location is refused rather than dropped.
/// </summary>
/// <remarks>
/// Driven through the writer rather than a route: the rules are what this proves, and they hold
/// whatever surface eventually calls them.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionSharingCascadeTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!;
    private Guid organiserId;

    // Owns a trip the organiser did not write. An Editor so they can create one at all; what
    // makes them a stranger here is ownership, since the seeded Editors ruleset carries no
    // right to administer anybody's rules.
    private HttpClient stranger = null!;
    private Guid strangerId;

    // The person being shared with: a plain reader, holding nothing anywhere.
    private HttpClient reader = null!;
    private Guid readerId;

    public ExpeditionSharingCascadeTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        organiserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xc-org-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xc-str-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xc-rdr-{suffix}@t.local");
        organiser = await AuthHelper.BearerClientAsync(factory, $"xc-org-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"xc-str-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"xc-rdr-{suffix}@t.local");
    }

    [Fact]
    public async Task Exact_location_is_refused_at_the_request_and_again_at_the_writer()
    {
        var reading = new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, readerId, AccessEffect.Allow, AccessAction.Read);
        var alsoExactLocation = reading with
        {
            Actions = AccessAction.Read | AccessAction.ViewExactLocation,
        };

        var validator = new ExpeditionShareRequestValidator();

        var refused = validator.Validate(new ExpeditionShareRequest([alsoExactLocation]));
        refused.IsValid.ShouldBeFalse();
        refused.Errors.Select(e => e.ErrorCode)
            .ShouldContain(AccessCascadeRules.ExactLocationRefusedCode);

        // The positive half: the same rule without that one flag is a perfectly ordinary ask.
        validator.Validate(new ExpeditionShareRequest([reading])).IsValid.ShouldBeTrue();

        // And again where the rows are built, so a caller written later that reaches the writer
        // by some other road is refused too rather than quietly writing what the route refuses.
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "Camp trip with a cave in it");
        await JoinAsync(camp, trip);

        var writerRefusal = await CascadeAsync(camp, organiserId, alsoExactLocation);
        writerRefusal.Problem.ShouldNotBeNull();
        writerRefusal.Problem!.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        CodeOf(writerRefusal).ShouldBe(AccessCascadeRules.ExactLocationRefusedCode);
        (await RulesOnAsync(trip)).ShouldBeEmpty();

        var allowed = await CascadeAsync(camp, organiserId, reading);
        allowed.Problem.ShouldBeNull();
        (await RulesOnAsync(trip)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_trips_own_rules_survive_a_camps_sharing_and_a_second_application_adds_nothing()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "A trip with rules of its own");
        await JoinAsync(camp, trip);

        // Authored by hand on the trip's own permissions tab, through the route that owns that
        // surface — the full-replace writer whose reach is exactly what this test is about.
        var authored = await organiser.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        authored.StatusCode.ShouldBe(HttpStatusCode.OK, await authored.Content.ReadAsStringAsync());

        var shared = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, strangerId, AccessEffect.Allow, AccessAction.Read));
        shared.Problem.ShouldBeNull();
        shared.Written.ShouldBe(1);

        var rules = await RulesOnAsync(trip);
        rules.Count.ShouldBe(2, "the camp's rule is added beside the trip's own, never instead of it");
        rules.Single(r => r.SubjectId == readerId).GrantedViaExpeditionId
            .ShouldBeNull("a rule somebody wrote by hand is nobody's cascade");
        rules.Single(r => r.SubjectId == strangerId).GrantedViaExpeditionId
            .ShouldBe(camp, "the camp's own rows carry the marker that lets it find them again");

        // Applied twice — the shape a re-apply takes — writes one rule, not two.
        var again = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, strangerId, AccessEffect.Allow, AccessAction.Read));
        again.Problem.ShouldBeNull();
        again.Written.ShouldBe(0);
        again.Unchanged.ShouldBe(1);
        (await RulesOnAsync(trip)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_member_trip_the_organiser_may_not_administer_refuses_the_whole_sharing_by_count()
    {
        var camp = await CreateCampAsync();
        var mine = await CreateTripAsync(organiser, "The organiser's own trip");
        const string TheirTitle = "Somebody else's trip entirely";
        var theirs = await CreateTripAsync(stranger, TheirTitle);
        await JoinAsync(camp, mine);
        await JoinAsync(camp, theirs);

        var refused = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, readerId, AccessEffect.Allow, AccessAction.Read));

        refused.Problem.ShouldNotBeNull();
        refused.Problem!.StatusCode.ShouldBe((int)HttpStatusCode.Forbidden);
        CodeOf(refused).ShouldBe(AccessCascadeRules.IncompleteCode);
        refused.Problem.ProblemDetails.Extensions["refusedTripCount"].ShouldBe(1);

        var detail = refused.Problem.ProblemDetails.Detail ?? string.Empty;
        detail.ShouldContain("1");
        detail.ShouldNotContain(TheirTitle, Case.Insensitive);
        detail.ShouldNotContain(theirs.ToString(), Case.Insensitive);
        detail.ShouldNotContain(mine.ToString(), Case.Insensitive);

        // All or nothing: the trip that would have accepted the rule has none either.
        (await RulesOnAsync(mine)).ShouldBeEmpty();
        (await RulesOnAsync(theirs)).ShouldBeEmpty();

        // The positive half, and it is the same camp, the same organiser and the same rule —
        // only the trip nobody gave them authority over has gone.
        var evicted = await organiser.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{theirs}");
        evicted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await evicted.Content.ReadAsStringAsync());

        var applied = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, readerId, AccessEffect.Allow, AccessAction.Read));
        applied.Problem.ShouldBeNull();
        (await RulesOnAsync(mine)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_camp_may_not_pass_on_more_than_its_organiser_holds_on_the_trip_itself()
    {
        var camp = await CreateCampAsync();
        var theirs = await CreateTripAsync(stranger, "A trip lent to the camp");
        await JoinAsync(camp, theirs);

        // Authority to administer that one trip's rules, and nothing else. Written straight to
        // the table because no route offers a stranger's trip to somebody who cannot yet
        // administer it — which is the whole point of the arrangement.
        await GrantOnTripAsync(theirs, organiserId, AccessAction.ManagePermissions);

        // Delete is the flag the seeded editor ruleset does not carry, so the organiser holds
        // it nowhere: not on this trip, not by owning it, not by any rule of theirs.
        var overreach = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, readerId, AccessEffect.Allow,
            AccessAction.Read | AccessAction.Delete));

        overreach.Problem.ShouldNotBeNull();
        CodeOf(overreach).ShouldBe(AccessCascadeRules.IncompleteCode);
        overreach.Problem!.ProblemDetails.Extensions["refusedTripCount"].ShouldBe(1);
        (await RulesOnAsync(theirs)).Count.ShouldBe(1, "only the rule that arranged this remains");

        // The positive half on the very same trip: the bound refused the extra action, not the
        // trip. Ask for only what the organiser holds there and the same cascade goes through.
        var within = await CascadeAsync(camp, organiserId, new ExpeditionShareEntryWrite(
            AccessSubjectKind.User, readerId, AccessEffect.Allow, AccessAction.Read));
        within.Problem.ShouldBeNull();

        var rules = await RulesOnAsync(theirs);
        rules.Count.ShouldBe(2, "the arranged rule stands and the camp's is added beside it");
        var written = rules.Single(r => r.GrantedViaExpeditionId is not null);
        written.Actions.ShouldBe(AccessAction.Read);
        written.SubjectId.ShouldBe(readerId);
        written.GrantedViaExpeditionId.ShouldBe(camp);
    }

    [Fact]
    public async Task Sharing_a_camp_is_refused_to_somebody_who_cannot_administer_it()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "A trip in a camp somebody else runs");
        await JoinAsync(camp, trip);

        // Somebody who cannot see the camp at all is told it is not there: a refusal that
        // admits it exists is a disclosure of its own. A Viewer, deliberately — the seeded
        // editor ruleset reads every camp whoever owns it, so an editor proves nothing here.
        var unseen = await reader.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        unseen.StatusCode.ShouldBe(HttpStatusCode.NotFound, await unseen.Content.ReadAsStringAsync());

        // And somebody who may read the camp but not administer its rules is refused rather
        // than lied to — they already know it is there.
        var refused = await stranger.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await RulesOnAsync(trip)).ShouldBeEmpty();

        var allowed = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await RulesOnAsync(trip)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_re_apply_covers_a_trip_that_joined_later_and_records_an_act_of_its_own()
    {
        var camp = await CreateCampAsync();
        var first = await CreateTripAsync(organiser, "The trip that was there");
        await JoinAsync(camp, first);

        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());

        // Joining afterwards carries nothing with it: a grant nobody performed is a grant
        // nobody can be asked about, so the trip that arrives late arrives unshared.
        var late = await CreateTripAsync(organiser, "The trip that joined afterwards");
        await JoinAsync(camp, late);
        (await RulesOnAsync(late)).ShouldBeEmpty("a trip joining a camp does not inherit its sharing");

        var sharing = await ReadSharingAsync(camp);
        sharing.MemberTrips.ShouldBe(2);
        sharing.Rules.Single().Trips.ShouldBe(1, "one of the two trips carries the rule");

        var reapplied = await organiser.PostAsync($"/api/v1/expeditions/{camp}/sharing/re-apply", null);
        reapplied.StatusCode.ShouldBe(HttpStatusCode.OK, await reapplied.Content.ReadAsStringAsync());

        (await RulesOnAsync(late)).Count.ShouldBe(1, "the deliberate act is what covers it");
        (await RulesOnAsync(first)).Count.ShouldBe(1, "and it writes nothing twice");
        (await ReadSharingAsync(camp)).Rules.Single().Trips.ShouldBe(2);

        // The act itself is on the camp's trail, and so are the rules it wrote: forty rows
        // nobody authored one at a time are found together by pointing at the camp.
        var trail = await TrailOfAsync(camp);
        var acts = trail.Where(a => a.EntityType == nameof(Expedition)
            && a.Action == AuditActions.PermissionChanged).ToList();
        acts.Count.ShouldBe(2, "applying and re-applying are two acts, each with a row");
        acts[^1].Changes.ShouldNotBeNull();
        acts[^1].Changes!.ShouldContain("re-applied");

        var rules = trail.Where(a => a.EntityType == nameof(AccessEntry)).ToList();
        rules.Count.ShouldBe(2, "one rule per member trip, both rooted at the camp");
        rules.ShouldAllBe(a => a.RootEntityId == camp.ToString());
    }

    [Fact]
    public async Task A_withdrawal_takes_the_camps_own_rules_and_leaves_an_identical_one_alone()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "A trip shared twice over");
        await JoinAsync(camp, trip);

        // The same subject, the same effect, the same actions, on the same trip — authored by
        // hand on the trip's own tab. Matching on what a rule says rather than on who wrote it
        // would take this one away too, and its author would never learn why.
        var authored = await organiser.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        authored.StatusCode.ShouldBe(HttpStatusCode.OK, await authored.Content.ReadAsStringAsync());

        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await RulesOnAsync(trip)).Count.ShouldBe(2);

        var withdrawn = await organiser.DeleteAsync($"/api/v1/expeditions/{camp}/sharing");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());

        var left = await RulesOnAsync(trip);
        left.Count.ShouldBe(1, "the hand-authored rule is nobody's cascade to withdraw");
        left.Single().GrantedViaExpeditionId.ShouldBeNull();
        left.Single().SubjectId.ShouldBe(readerId);
        (await ReadSharingAsync(camp)).Rules.ShouldBeEmpty();

        // And the withdrawal is an act on the trail like the granting was.
        var acts = (await TrailOfAsync(camp))
            .Where(a => a.EntityType == nameof(Expedition) && a.Action == AuditActions.PermissionChanged)
            .ToList();
        acts[^1].Changes!.ShouldContain("withdrawn");
    }

    [Fact]
    public async Task A_deleted_trip_leaves_no_rule_the_integrity_check_calls_an_orphan()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "A trip that will not be here long");
        await JoinAsync(camp, trip);

        await GrantOnTripAsync(trip, strangerId, AccessAction.Read);
        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await RulesOnAsync(trip)).Count.ShouldBe(2);

        var deleted = await organiser.DeleteAsync($"/api/v1/trip-logs/{trip}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        (await RulesOnAsync(trip)).ShouldBeEmpty(
            "a rule anchored on a trip that is gone reads as a live grant and resolves to nothing");
        var orphans = (await VerifyAsync())
            .Where(p => p.Check == "access_scope_orphan" && p.Detail.Contains(trip.ToString()))
            .ToList();
        orphans.ShouldBeEmpty(string.Join("; ", orphans.Select(p => p.Detail)));
    }

    /// <summary>
    /// A trip taken out of a camp takes the camp's grants with it, and keeps its own.
    /// </summary>
    /// <remarks>
    /// The grant existed because the trip was in the camp. Left behind it would be permanent and
    /// invisible: it is anchored on the trip and marked with a camp the trip has left, so the
    /// trip's own permissions page does not offer it to be rewritten, and the only route that
    /// withdraws it asks for authority over a camp the trip's owner need not hold.
    /// </remarks>
    [Fact]
    public async Task A_trip_taken_out_of_a_camp_keeps_its_own_rules_and_loses_the_camps()
    {
        var camp = await CreateCampAsync();
        var leaving = await CreateTripAsync(organiser, "The trip that is evicted");
        var staying = await CreateTripAsync(organiser, "The trip that stays");
        await JoinAsync(camp, leaving);
        await JoinAsync(camp, staying);

        await GrantOnTripAsync(leaving, strangerId, AccessAction.Read);
        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await RulesOnAsync(leaving)).Count.ShouldBe(2);

        var evicted = await organiser.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{leaving}");
        evicted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await evicted.Content.ReadAsStringAsync());

        var left = await RulesOnAsync(leaving);
        left.Count.ShouldBe(1, "what the camp granted goes; what somebody wrote on the trip stays");
        left.Single().GrantedViaExpeditionId.ShouldBeNull();
        left.Single().SubjectId.ShouldBe(strangerId);

        // The camp's remaining member is untouched, so the sweep is about the trip that left.
        (await RulesOnAsync(staying)).Count.ShouldBe(1);
        (await ReadSharingAsync(camp)).Rules.Single().Trips
            .ShouldBe(1, "and the camp no longer counts a trip it does not gather");
    }

    /// <summary>
    /// A camp that gathers nothing is told its sharing has nowhere to go, rather than answered
    /// successfully for a grant that was never stored.
    /// </summary>
    /// <remarks>
    /// The rules of a camp's sharing are the rows on its member trips and live nowhere else, so
    /// the natural order of work — make the camp, share it with the partner club, then gather the
    /// trips — would lose the grant entirely, and re-applying could not recover it: there would be
    /// nothing to read back.
    /// </remarks>
    [Fact]
    public async Task A_camp_that_gathers_no_trips_is_told_its_sharing_has_nowhere_to_go()
    {
        var camp = await CreateCampAsync();

        var refused = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        JsonDocument.Parse(await refused.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString()
            .ShouldBe(ExpeditionAccessCascade.NoMemberTripsCode);

        // The positive half over the same camp: gather a trip and the same request goes through.
        var trip = await CreateTripAsync(organiser, "The trip that arrives");
        await JoinAsync(camp, trip);
        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await RulesOnAsync(trip)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A trip's own permissions page shows what a camp granted on it, marked as the camp's, and a
    /// rewrite of that page leaves it exactly where it was.
    /// </summary>
    /// <remarks>
    /// Two different questions, and this batch has to answer them differently. The page may not
    /// rewrite the camp's rule — it replaces the whole set it reads, so the first edit of the
    /// trip's own rules would silently revoke the camp's. But it must show it: a trip whose
    /// permissions page said nobody had access while a partner club held Read on it would leave
    /// its owner with no surface anywhere that could tell them.
    /// </remarks>
    [Fact]
    public async Task A_trips_page_shows_the_camps_rule_and_a_rewrite_there_leaves_it_alone()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync(organiser, "A trip a camp shared");
        await JoinAsync(camp, trip);

        var applied = await organiser.PostAsJsonAsync($"/api/v1/expeditions/{camp}/sharing", Body(readerId));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());

        var shown = await ObjectRulesAsync(trip);
        shown.Count.ShouldBe(1);
        shown.Single().GetProperty("grantedViaExpeditionId").GetGuid()
            .ShouldBe(camp, "shown, and shown as the camp's rather than as this page's own");

        // A rewrite of the trip's own rules — the full-replace writer — leaves the camp's alone.
        var rewritten = await organiser.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = strangerId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        rewritten.StatusCode.ShouldBe(HttpStatusCode.OK, await rewritten.Content.ReadAsStringAsync());

        var after = await RulesOnAsync(trip);
        after.Count.ShouldBe(2);
        after.Single(r => r.SubjectId == readerId).GrantedViaExpeditionId.ShouldBe(camp);
        after.Single(r => r.SubjectId == strangerId).GrantedViaExpeditionId.ShouldBeNull();
    }

    // ---- helpers -------------------------------------------------------------------------

    /// <summary>The rules a trip's own permissions page shows, as it shows them.</summary>
    private async Task<List<JsonElement>> ObjectRulesAsync(Guid tripId)
    {
        var response = await organiser.GetAsync($"/api/v1/objects/tripLog/{tripId}/access");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray()];
    }

    private static object Body(Guid subjectId) => new
    {
        entries = new[]
        {
            new { subjectKind = "user", subjectId, effect = "allow", actions = "Read" },
        },
    };

    /// <summary>What the camp says it shares: the subject and how many of its trips carry it.</summary>
    private sealed record SharingRule(Guid SubjectId, int Trips);

    private sealed record SharingRead(int MemberTrips, IReadOnlyList<SharingRule> Rules);

    private async Task<SharingRead> ReadSharingAsync(Guid campId)
    {
        var response = await organiser.GetAsync($"/api/v1/expeditions/{campId}/sharing");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonSerializer.Deserialize<SharingRead>(
            payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>Everything on the camp's trail, its children's rows included, oldest first.</summary>
    private async Task<List<AuditEntry>> TrailOfAsync(Guid campId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => (a.EntityType == nameof(Expedition) && a.EntityId == campId.ToString())
                || (a.RootEntityType == nameof(Expedition) && a.RootEntityId == campId.ToString()))
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<IntegrityProblem>> VerifyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
    }

    private static object? CodeOf(ExpeditionCascadeOutcome outcome) =>
        outcome.Problem is null ? null : outcome.Problem.ProblemDetails.Extensions["code"];

    /// <summary>
    /// Runs the camp's sharing as the named account and saves it when it was not refused —
    /// the writer stages, so the unit of work belongs to whoever called it.
    /// </summary>
    private async Task<ExpeditionCascadeOutcome> CascadeAsync(
        Guid campId, Guid actorId, params ExpeditionShareEntryWrite[] entries)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var ctx = await AccessContextResolver.ResolveAsync(db, actorId);

        var outcome = await ExpeditionAccessCascade.StageAsync(
            db, access, ctx, campId, entries, CancellationToken.None);
        if (outcome.Problem is null)
        {
            await db.SaveChangesAsync();
        }

        return outcome;
    }

    private async Task<List<AccessEntry>> RulesOnAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AccessEntries.AsNoTracking()
            .Where(e => e.Domain == AccessDomain.TripLogs
                && e.ScopeKind == AccessScopeKind.Object
                && e.ScopeId == tripId)
            .OrderBy(e => e.Id)
            .ToListAsync();
    }

    private async Task GrantOnTripAsync(Guid tripId, Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateCampAsync()
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Cascade camp {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = (string?)null,
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTripAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            visibility = "private",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task JoinAsync(Guid campId, Guid tripId)
    {
        var response = await organiser.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        stranger?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
