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
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Writing to a whole caving group: who may, who is reached, and who is not.
///
/// Every account here is a Viewer holding nothing over caving groups beyond the read every
/// account has, and each right a test needs is written as one explicit rule against the one group
/// — an administrator would hold everything at the widest scope and would therefore prove nothing
/// about which right is being checked. Each negative asserts the matching positive in the same
/// test, so a fixture that quietly stopped producing anything cannot pass as a security
/// assertion.
/// </summary>
public sealed class CavingGroupAnnouncementTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;
    private readonly List<Guid> crowd = [];

    private HttpClient leader = null!;       // holds Execute on the group below
    private HttpClient rosterEditor = null!; // holds Write on it, and nothing else
    private Guid leaderId;
    private Guid rosterEditorId;
    private Guid memberId;
    private Guid strangerId;
    private Guid cavingGroupId;

    public CavingGroupAnnouncementTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        // The queue's own worker is stopped here as well as on the capped host below. It claims
        // anything claimable in the shared database every couple of seconds, and a tick landing
        // between a request and the assertions a few milliseconds later would turn "nobody has
        // been told yet" into a failure about code that is working.
        factory = new SilexGisApiFactory(connectionString, null, TestHostTweaks.WithoutJobWorker);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        leaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ga-lead-{suffix}@t.local");
        leader = await AuthHelper.BearerClientAsync(factory, $"ga-lead-{suffix}@t.local");

        rosterEditorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ga-edit-{suffix}@t.local");
        rosterEditor = await AuthHelper.BearerClientAsync(factory, $"ga-edit-{suffix}@t.local");

        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ga-mem-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ga-str-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var cavingGroup = new CavingGroup
        {
            Name = $"Announcement Club {suffix}",
            Slug = $"announcement-club-{suffix}",
            Type = CavingGroupType.CavingClub,
        };
        db.CavingGroups.Add(cavingGroup);
        await db.SaveChangesAsync();
        cavingGroupId = cavingGroup.Id;

        // The leader and the member are on the roster; the stranger has a roster row of their own
        // and is deliberately not on this group's. A stranger with no roster row at all would be
        // excluded by having no person to be, which is a different reason.
        await RosterHelper.AddMemberAsync(db, cavingGroupId, leaderId, CavingGroupRole.Owner);
        await RosterHelper.AddMemberAsync(db, cavingGroupId, memberId);
        _ = await RosterHelper.CaverForAsync(db, strangerId);

        await GrantAsync(db, leaderId, AccessAction.Execute);
        await GrantAsync(db, rosterEditorId, AccessAction.Write);
    }

    [Fact]
    public async Task Everybody_on_the_roster_is_told_and_nobody_off_it_is()
    {
        var response = await AnnounceAsync(leader, "The Sunday meet moves to 09:00.");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recipients").GetInt32().ShouldBe(1);

        var told = await NoticesForAsync(memberId);
        told.Count.ShouldBe(1);
        told[0].Category.ShouldBe(NotificationCategory.GroupAnnouncement);
        told[0].TemplateKey.ShouldBe(MessageTemplateCatalog.NotifyGroupAnnouncement);
        told[0].Placeholders.ShouldContain("The Sunday meet moves to 09:00.");

        // Where the message points, which is the whole of what a text message says: it carries
        // the announcement nowhere, only the link, so a link that landed on a page an
        // announcement is never shown on would be the message failing to keep its promise. The
        // inbox is that page; the group's own page is where an announcement is written.
        told[0].Placeholders.ShouldContain("/notifications");
        told[0].Placeholders.ShouldNotContain("/caving-groups");

        // The same announcement, in the same act, reached nobody off the roster — and the sender
        // is not told what they themselves just wrote.
        (await NoticesForAsync(strangerId)).ShouldBeEmpty();
        (await NoticesForAsync(leaderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Being_allowed_to_edit_a_roster_is_not_being_allowed_to_write_to_it()
    {
        // The decision this endpoint rests on. Whoever may add and remove members holds Write on
        // the group, and that is deliberately not enough: editing a list of names and sending two
        // hundred people a message are different acts, and an installation may want one person to
        // do the first without the second. Testing only that a stranger is refused would leave
        // this untested, because a stranger is refused under either rule.
        var refused = await AnnounceAsync(rosterEditor, "Roster edits are not announcements.");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("access.forbidden");
        (await NoticesForAsync(memberId)).ShouldBeEmpty();

        // And the positive half, so the refusal above is evidence about the right rather than
        // about a fixture that sends nothing: the same message from the account holding Execute
        // on the same group goes out.
        (await AnnounceAsync(leader, "This one is allowed.")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_account_with_no_rule_over_the_group_is_refused()
    {
        // A member of the club is not thereby someone who may write to it.
        using var ordinary = await AuthHelper.BearerClientAsync(factory, EmailOf(memberId));

        (await AnnounceAsync(ordinary, "Anyone can shout, apparently.")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await NoticesForAsync(memberId, leaderId, strangerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Whoever_starts_a_club_may_write_to_it()
    {
        // The decision seeding makes, written down as a decision rather than left implied by seed
        // data. Starting a club is something any signed-in account may do, and the ruleset the
        // creator is put in carries the right to write to the roster along with the right to edit
        // it — because withholding it would leave the club's own leader unable to do the thing a
        // club leader does, and only an installation administrator able to do it for them.
        //
        // The cost of that is real and is the reason this is asserted rather than assumed: it puts
        // the right to write to everybody a club contains in the hands of whoever creates the
        // club, so an installation that does not want that has to take it back deliberately.
        using var ordinary = await AuthHelper.BearerClientAsync(factory, EmailOf(strangerId));
        var created = await ordinary.PostAsJsonAsync(
            "/api/v1/caving-groups/",
            new { name = $"Founded Club {Guid.NewGuid():N}"[..40], type = "cavingClub" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var founded = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Asked of the count rather than of a send, because the count is behind the same right and
        // reaching for it does not write to anybody.
        (await ordinary.GetAsync($"/api/v1/caving-groups/{founded}/announcements/audience"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And it is that club only: founding one confers nothing over anybody else's.
        (await ordinary.GetAsync($"/api/v1/caving-groups/{cavingGroupId}/announcements/audience"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Taken away again, rulesets and all: this suite shares one database and a club left
        // behind carries a permission group nobody in a later test expects to find.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rulesets = await db.AccessEntries
            .Where(e => e.ScopeId == founded && e.PermissionGroupId != null)
            .Select(e => e.PermissionGroupId!.Value).Distinct().ToListAsync();
        await db.AccessEntries.Where(e => e.ScopeId == founded).ExecuteDeleteAsync();
        await db.PermissionGroupMembers.Where(m => rulesets.Contains(m.PermissionGroupId)).ExecuteDeleteAsync();
        await db.AccessEntries
            .Where(e => e.PermissionGroupId != null && rulesets.Contains(e.PermissionGroupId!.Value))
            .ExecuteDeleteAsync();
        await db.PermissionGroups.Where(g => rulesets.Contains(g.Id)).ExecuteDeleteAsync();
        await db.CavingGroups.Where(c => c.Id == founded).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Somebody_told_about_an_announcement_can_still_open_it()
    {
        // The producer decides who is told; the inbox decides again, when the row is read,
        // whether the reader may still see what it is about. Two decisions made by different code
        // over the same object disagree silently and only the affected member ever notices, so
        // this pins that they agree: what was sendable to this person is readable by them.
        (await AnnounceAsync(leader, "Kit check on Thursday.")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var reader = await AuthHelper.BearerClientAsync(factory, EmailOf(memberId));
        var id = (await NoticesForAsync(memberId)).Single().Id;

        var row = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/{id}");

        row.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();
        var title = row.GetProperty("title").GetString();
        title.ShouldNotBeNullOrWhiteSpace();
        title.ShouldContain("Kit check on Thursday.");
        row.GetProperty("url").GetString().ShouldBe("/caving-groups");
    }

    [Fact]
    public async Task An_announcement_to_a_caving_group_that_is_not_there_is_not_found()
    {
        var response = await leader.PostAsJsonAsync(
            $"/api/v1/caving-groups/{Guid.NewGuid()}/announcements", new { message = "Into the void." });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.not_found");
    }

    [Fact]
    public async Task Nobody_signed_out_may_announce()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.PostAsJsonAsync(
                $"/api/v1/caving-groups/{cavingGroupId}/announcements", new { message = "Hello." }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_announcement_with_nothing_in_it_is_refused(string message)
    {
        (await AnnounceAsync(leader, message)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await NoticesForAsync(memberId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_announcement_longer_than_the_line_it_arrives_on_is_refused()
    {
        // It arrives as a subject line and as one row of a list, and there is nowhere else to
        // read it, so a length that would be cut off is refused rather than silently trimmed.
        (await AnnounceAsync(leader, new string('x', 201))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AnnounceAsync(leader, new string('x', 200))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_announcement_of_two_paragraphs_is_refused_rather_than_flattened()
    {
        (await AnnounceAsync(leader, "First line.\nSecond line.")).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await NoticesForAsync(memberId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_account_cannot_write_to_a_roster_twice_in_a_row()
    {
        // Not the endpoint's per-address request budget. That budget is keyed on the caller's
        // address, it refills every minute, and a second replica of the application keeps a second
        // copy of it — while every call it lets through writes one row per member of a club. This
        // asserts the cooldown the application ships with, not one forced small for the test, and
        // nothing the caller can do clears it.
        (await AnnounceAsync(leader, "The meet is on.")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await AnnounceAsync(leader, "No, it is off.");

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_too_soon");

        // And the second one reached nobody, rather than being refused after the fact.
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_refusal_costs_nothing_and_leaves_the_next_announcement_free()
    {
        // The cooldown is stamped where the sending starts, so being turned away before that —
        // for a group that is not there — must not spend somebody's turn. Without this, one
        // mistyped address would lock a club leader out for the evening.
        await leader.PostAsJsonAsync(
            $"/api/v1/caving-groups/{Guid.NewGuid()}/announcements", new { message = "Into the void." });

        (await AnnounceAsync(leader, "The meet is on.")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_roster_small_enough_is_written_to_inside_the_request()
    {
        var response = await AnnounceAsync(leader, "Two of us on Sunday.");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("queued").GetBoolean().ShouldBeFalse();
        body.GetProperty("recipients").GetInt32().ShouldBe(1);

        // Nothing was recorded to be handed out later, and nothing was asked to hand it out: an
        // ordinary club is written to directly and leaves no trace behind.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.CavingGroupAnnouncements.CountAsync(a => a.CavingGroupId == cavingGroupId)).ShouldBe(0);
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_roster_too_large_to_write_to_inline_is_recorded_and_handed_out_by_a_background_pass()
    {
        // The limit is lowered rather than the club enlarged, and lowering it is the point: a
        // configuration key that binds to nothing fails silently and looks exactly like one that
        // works, because the shipped default goes on doing the job. A club of four against a
        // limit of two can only take the second path if the number was actually read.
        await using var capped = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["Notifications:AnnouncementFanOutLimit"] = "2" },
            // The queue's own worker is stopped for this factory too, so the pass runs once,
            // here, where the test can see it. Left running it would race the call below and the
            // duplicate that proves the guard works would look like the guard failing.
            TestHostTweaks.WithoutJobWorker);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using (var scope = capped.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var extra = await AuthHelper.CreateUserAsync(
                    capped, GlobalRoles.Viewer, $"ga-crowd-{i}-{suffix}@t.local");
                crowd.Add(extra);
                await RosterHelper.AddMemberAsync(db, cavingGroupId, extra);
            }
        }

        using var sender = await AuthHelper.BearerClientAsync(capped, EmailOf(leaderId));
        var response = await sender.PostAsJsonAsync(
            $"/api/v1/caving-groups/{cavingGroupId}/announcements",
            new { message = "Everyone: the entrance gate code has changed." });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("queued").GetBoolean().ShouldBeTrue();
        body.GetProperty("recipients").GetInt32().ShouldBe(4);

        // Nobody has been told yet, and what was written instead is one notice and one job asking
        // for it to be handed out. That is the whole of the difference: the request that sent it
        // wrote two rows rather than four hundred.
        var everyone = crowd.Append(memberId).ToArray();
        (await NoticesForAsync(everyone)).ShouldBeEmpty();

        Guid announcementId;
        await using (var scope = capped.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var pending = await db.CavingGroupAnnouncements
                .SingleAsync(a => a.CavingGroupId == cavingGroupId);
            pending.RecipientCount.ShouldBe(4);
            pending.ExpandedAt.ShouldBeNull();
            announcementId = pending.Id;

            var queuedJob = await db.ProcessingJobs
                .SingleAsync(j => j.Kind == ProcessingJobKinds.CavingGroupAnnouncement);
            queuedJob.Payload.ShouldContain(announcementId.ToString());
            // Nobody is named as having asked for it: this queue tells its requester how every job
            // it runs turned out, and the person who sent the announcement was already told.
            queuedJob.RequestedBy.ShouldBeNull();
        }

        await RunExpansionAsync(capped, announcementId);

        var told = await NoticesForAsync(everyone);

        // Counted over people rather than rows, and both, because those are different failures:
        // four rows spread over three members is one person told twice and one told not at all.
        told.Select(n => n.RecipientUserId).Distinct().Count().ShouldBe(4);
        told.Count.ShouldBe(4);
        told.ShouldAllBe(n => n.Category == NotificationCategory.GroupAnnouncement);
        told.ShouldAllBe(n => n.TargetKind == NotificationTargetKind.CavingGroup && n.TargetId == cavingGroupId);
        told.ShouldAllBe(n => n.Placeholders.Contains("the entrance gate code has changed"));

        // The sender is not told what they wrote, whichever path it took.
        (await NoticesForAsync(leaderId)).ShouldBeEmpty();

        // And the pass cannot happen twice. This queue re-runs anything it finds interrupted, from
        // the beginning, so without the stamp a restart would tell half a club a second time.
        await RunExpansionAsync(capped, announcementId);
        (await NoticesForAsync(everyone)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_recorded_notice_is_not_kept_longer_than_what_it_told_people()
    {
        // It holds a copy of the sender's own words. The notifications carrying those words are
        // deleted once the installation's window closes, so a copy that outlived them would be the
        // one place somebody's message survived an answer the operator gave deliberately — and
        // only for large clubs, which is the case nobody would think to look at.
        Guid old;
        Guid recent;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var aged = Recorded(DateTimeOffset.UtcNow.AddDays(-400));
            var fresh = Recorded(DateTimeOffset.UtcNow.AddDays(-1));
            db.CavingGroupAnnouncements.AddRange(aged, fresh);
            await db.SaveChangesAsync();
            old = aged.Id;
            recent = fresh.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var left = await db.CavingGroupAnnouncements
                .Where(a => a.CavingGroupId == cavingGroupId).Select(a => a.Id).ToListAsync();

            // Both halves: gone past the window, and untouched inside it — a pass that deleted the
            // table would satisfy the first assertion on its own.
            left.ShouldNotContain(old);
            left.ShouldContain(recent);
        }

        CavingGroupAnnouncement Recorded(DateTimeOffset when) => new()
        {
            CavingGroupId = cavingGroupId,
            SenderUserId = leaderId,
            Message = "Kept exactly as long as what it told people.",
            CavingGroupName = "Announcement Club",
            SenderName = "A leader",
            RecipientCount = 1,
            CreatedAt = when,
            ExpandedAt = when,
        };
    }

    private static async Task RunExpansionAsync(SilexGisApiFactory host, Guid announcementId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.CavingGroupAnnouncement);
        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.CavingGroupAnnouncement,
                Payload = JsonSerializer.Serialize(new CavingGroupAnnouncementPayload(announcementId)),
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task How_many_people_it_would_reach_is_the_number_it_reaches()
    {
        // The whole point of showing a count before the send is that it is the send's own count.
        // Two different reads of "who is on this roster" would each look right and disagree, and
        // the person told "3" while 47 were written to would be the only one who ever found out.
        var shown = await leader.GetAsync($"/api/v1/caving-groups/{cavingGroupId}/announcements/audience");
        shown.StatusCode.ShouldBe(HttpStatusCode.OK, await shown.Content.ReadAsStringAsync());
        var promised = (await shown.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recipients").GetInt32();

        var sent = await AnnounceAsync(leader, "The Sunday meet moves to 09:00.");
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var reached = (await sent.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recipients").GetInt32();

        // Positively one, not merely equal: two zeroes would agree with each other and prove
        // nothing about a roster nobody read.
        promised.ShouldBe(1);
        reached.ShouldBe(promised);
        (await NoticesForAsync(memberId)).Count.ShouldBe(promised);
    }

    [Fact]
    public async Task What_a_club_can_be_reached_with_is_only_shown_to_somebody_who_may_do_it()
    {
        // Counted for the account that may write to the roster; refused to the account that may
        // only edit it. Both assertions in one test, because a fixture where nobody could read
        // anything would pass the refusal on its own.
        var refused = await rosterEditor.GetAsync(
            $"/api/v1/caving-groups/{cavingGroupId}/announcements/audience");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await leader.GetAsync(
            $"/api/v1/caving-groups/{cavingGroupId}/announcements/audience");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_page_is_told_which_clubs_this_reader_may_write_to()
    {
        // A rule that names one club answers no question that names none, so the domain-wide
        // capability the rest of the interface gates on cannot answer this. The list says so per
        // club instead — and says it differently for two accounts reading the same list, which is
        // what makes it evidence rather than a constant.
        var mine = await ListedAsync(leader);
        mine.ShouldBeTrue();

        var theirs = await ListedAsync(rosterEditor);
        theirs.ShouldBeFalse();
    }

    /// <summary>Whether the club under test comes back from the list marked as writable-to.</summary>
    private async Task<bool> ListedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/caving-groups");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == cavingGroupId)
            .GetProperty("canAnnounce").GetBoolean();
    }

    private Task<HttpResponseMessage> AnnounceAsync(HttpClient client, string message) =>
        client.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/announcements", new { message });

    /// <summary>One explicit rule over this one caving group, written as the rule editor would.</summary>
    private async Task GrantAsync(SilexGisDbContext db, Guid userId, AccessAction actions)
    {
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.CavingGroups,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = cavingGroupId,
            GrantedBy = userId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<Notification>> NoticesForAsync(params Guid[] recipients)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => recipients.Contains(n.RecipientUserId))
            .OrderBy(n => n.Id)
            .ToListAsync();
    }

    private string EmailOf(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email!).Single();
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var mine = crowd.Append(memberId).Append(leaderId).Append(strangerId).Append(rosterEditorId).ToArray();
        await db.Notifications.Where(n => mine.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        await db.AccessEntries.Where(e => e.ScopeId == cavingGroupId).ExecuteDeleteAsync();
        await db.CavingGroups.Where(c => c.Id == cavingGroupId).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        leader.Dispose();
        rosterEditor.Dispose();
        factory.Dispose();
    }
}
