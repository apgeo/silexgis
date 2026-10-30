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
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What an expedition tells the people on its roster when it changes or is called off.
/// <para>
/// Every camp is created private and opened to exactly the people a test needs, one explicit grant
/// at a time: the seeded Editors group reads past visibility at the widest scope, so an account
/// that "cannot see" a camp while holding that membership proves nothing. Everybody left out here is
/// a plain reader holding nothing, and the person who is told is asserted in the same test as the
/// person who is not.
/// </para>
/// </summary>
public sealed class ExpeditionNoticeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;   // Editor; owns the camps, so writes them
    private Guid ownerId;
    private Guid ownerCaver;
    private Guid mateId;                // Viewer; on the roster, given the read on the camp
    private Guid mateCaver;
    private Guid strangerId;            // Viewer; on the roster, given nothing on the camp
    private Guid strangerCaver;
    private Guid bystanderId;           // Viewer; may read the camp but is on no roster
    private long roleId;

    public ExpeditionNoticeTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xn-own-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xn-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xn-str-{suffix}@t.local");
        bystanderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xn-by-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xn-own-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        ownerCaver = await db.Cavers.Where(c => c.UserId == ownerId).Select(c => c.Id).SingleAsync();
        mateCaver = await db.Cavers.Where(c => c.UserId == mateId).Select(c => c.Id).SingleAsync();
        strangerCaver = await db.Cavers.Where(c => c.UserId == strangerId).Select(c => c.Id).SingleAsync();
        roleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == ExpeditionRosterRoleSeeds.MemberCode).Select(r => r.Id).SingleAsync();
    }

    [Fact]
    public async Task A_change_to_a_camp_people_expect_tells_the_roster_who_may_read_it_and_nobody_else()
    {
        var camp = await CreateCampAsync("Bihor camp");
        await GrantReadAsync(camp, mateId);
        await GrantReadAsync(camp, bystanderId);
        await PutOnRosterAsync(camp, ownerCaver, mateCaver, strangerCaver);
        await MoveAsync(camp, "proposed");

        var edited = await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{camp}", Body("Bihor camp, later"));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        var told = (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionUpdate)).ShouldHaveSingleItem();
        told.Category.ShouldBe(NotificationCategory.ExpeditionUpdate);
        told.TargetKind.ShouldBe(NotificationTargetKind.Expedition);
        told.TargetId.ShouldBe(camp);

        // On the roster but unable to read the camp; able to read it but on no roster; and the
        // person who made the change.
        (await NoticesAsync(strangerId, MessageTemplateCatalog.NotifyExpeditionUpdate)).ShouldBeEmpty();
        (await NoticesAsync(bystanderId, MessageTemplateCatalog.NotifyExpeditionUpdate)).ShouldBeEmpty();
        (await NoticesAsync(ownerId, MessageTemplateCatalog.NotifyExpeditionUpdate)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_camp_still_a_draft_and_a_write_that_changes_nothing_tell_nobody()
    {
        var draft = await CreateCampAsync("Quiet camp");
        await GrantReadAsync(draft, mateId);
        await PutOnRosterAsync(draft, mateCaver);

        (await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{draft}", Body("Quiet camp, edited")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NoticesAsync(mateId)).ShouldBeEmpty();

        // The same camp once people are expecting it: the first write changes it and is announced,
        // the second carries back exactly what it was given and is not.
        await MoveAsync(draft, "proposed");
        var body = Body("Quiet camp, announced");
        (await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{draft}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionUpdate)).Count.ShouldBe(1);

        (await owner.PutWithIfMatchAsync($"/api/v1/expeditions/{draft}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionUpdate)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Calling_a_camp_off_tells_the_roster_it_is_off_and_calling_off_a_draft_tells_nobody()
    {
        var camp = await CreateCampAsync("Called off");
        await GrantReadAsync(camp, mateId);
        await PutOnRosterAsync(camp, mateCaver, strangerCaver);
        await MoveAsync(camp, "planned");
        await MoveAsync(camp, "cancelled");

        var told = (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionCancelled)).ShouldHaveSingleItem();
        told.TargetKind.ShouldBe(NotificationTargetKind.Expedition);
        told.TargetId.ShouldBe(camp);
        (await NoticesAsync(strangerId, MessageTemplateCatalog.NotifyExpeditionCancelled)).ShouldBeEmpty();

        var draft = await CreateCampAsync("Never announced");
        await GrantReadAsync(draft, mateId);
        await PutOnRosterAsync(draft, mateCaver);
        await MoveAsync(draft, "cancelled");
        (await NoticesAsync(mateId))
            .Where(n => n.TargetId == draft).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_camp_moving_into_the_workshop_or_to_be_written_up_tells_nobody_as_a_trip_would_not()
    {
        var camp = await CreateCampAsync("Moves");
        await GrantReadAsync(camp, mateId);
        await PutOnRosterAsync(camp, mateCaver);
        await MoveAsync(camp, "planned");

        // Planned to delayed is a change people are expecting: told.
        await MoveAsync(camp, "delayed");
        (await UpdatesAboutAsync(mateId, camp)).ShouldBe(1);

        // Delayed back to planned is told as well; then planned back into the workshop is not,
        // and neither is a confirmed camp going on to be done.
        await MoveAsync(camp, "planned");
        var afterPlanned = await UpdatesAboutAsync(mateId, camp);
        afterPlanned.ShouldBe(2);
        await MoveAsync(camp, "draft");
        (await UpdatesAboutAsync(mateId, camp)).ShouldBe(afterPlanned);

        var other = await CreateCampAsync("Ends");
        await GrantReadAsync(other, mateId);
        await PutOnRosterAsync(other, mateCaver);
        await MoveAsync(other, "planned");
        await MoveAsync(other, "confirmed");
        var afterConfirmed = await UpdatesAboutAsync(mateId, other);
        afterConfirmed.ShouldBe(1);
        await MoveAsync(other, "done");
        (await UpdatesAboutAsync(mateId, other)).ShouldBe(afterConfirmed);
    }

    [Fact]
    public async Task Putting_an_account_on_a_camps_roster_tells_that_account_only_and_not_while_a_draft()
    {
        var camp = await CreateCampAsync("Adds");
        await GrantReadAsync(camp, mateId);
        await GrantReadAsync(camp, bystanderId);

        // A draft tells nobody.
        await PostRosterAsync(camp, mateCaver);
        (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldBeEmpty();

        await MoveAsync(camp, "planned");
        await PostRosterAsync(camp, ownerCaver);
        await PostRosterAsync(camp, strangerCaver);

        // The mate is already on it: a second stay is not news.
        await PostRosterAsync(camp, mateCaver);
        (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldBeEmpty();

        var camp2 = await CreateCampAsync("Adds two");
        await GrantReadAsync(camp2, mateId);
        await GrantReadAsync(camp2, bystanderId);
        await MoveAsync(camp2, "planned");
        await PostRosterAsync(camp2, mateCaver);
        await PostRosterAsync(camp2, strangerCaver);
        await PostRosterAsync(camp2, ownerCaver);

        var told = (await NoticesAsync(mateId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldHaveSingleItem();
        told.TargetKind.ShouldBe(NotificationTargetKind.Expedition);
        told.TargetId.ShouldBe(camp2);

        // Added but unable to read the camp; able to read it but not added; and the actor.
        (await NoticesAsync(strangerId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldBeEmpty();
        (await NoticesAsync(bystanderId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldBeEmpty();
        (await NoticesAsync(ownerId, MessageTemplateCatalog.NotifyExpeditionParticipation)).ShouldBeEmpty();
    }

    private async Task<int> UpdatesAboutAsync(Guid userId, Guid camp) =>
        (await NoticesAsync(userId, MessageTemplateCatalog.NotifyExpeditionUpdate)).Count(n => n.TargetId == camp);

    private async Task PostRosterAsync(Guid camp, Guid caver)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/expeditions/{camp}/roster", new
        {
            caverId = caver,
            roleId,
            fromDate = "2026-07-18",
            toDate = "2026-08-01",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<Notification>> NoticesAsync(Guid userId, string? templateKey = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientUserId == userId);
        if (templateKey is not null)
        {
            query = query.Where(x => x.TemplateKey == templateKey);
        }

        return await query.OrderBy(x => x.Id).ToListAsync();
    }

    private async Task MoveAsync(Guid camp, string state)
    {
        var response = await owner.PostWithIfMatchAsync($"/api/v1/expeditions/{camp}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object Body(string name) => new
    {
        name,
        description = "A camp.",
        startDate = "2026-07-18",
        endDate = "2026-08-01",
        geom = (object?)null,
        cavingGroupId = (Guid?)null,
        visibility = "private",
    };

    private async Task<Guid> CreateCampAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", Body($"{name} {Guid.NewGuid():N}"));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task PutOnRosterAsync(Guid camp, params Guid[] cavers)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        foreach (var caver in cavers)
        {
            db.ExpeditionRoster.Add(new ExpeditionRosterEntry
            {
                ExpeditionId = camp,
                CaverId = caver,
                RoleId = roleId,
                FromDate = new DateOnly(2026, 7, 18),
                ToDate = new DateOnly(2026, 8, 1),
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task GrantReadAsync(Guid camp, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Expeditions,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = camp,
        });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
    }
}
