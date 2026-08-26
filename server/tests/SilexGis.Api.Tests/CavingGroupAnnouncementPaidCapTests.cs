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
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A charging transport that is never asked to send anything.
/// </summary>
/// <remarks>
/// <para>
/// It exists so the day's spending ceiling can be made to refuse something without any of it
/// depending on who has a phone number. The real charging transport reaches only an account that
/// has proved a number is its own, so a roster of accounts that have not would spend nothing and
/// every refusal below would stop happening for a reason that has nothing to do with the ceiling.
/// This one reaches everybody, which is what leaves the arithmetic as the only variable.
/// </para>
/// <para>
/// It replaces every registered channel rather than joining them — two implementations claiming
/// one delivery value is itself refused, and a second real channel alongside it would make a
/// roster cost two rows a head instead of one. What the ceiling reads is the channel's preference
/// cell, so which delivery value carries it does not change what is exercised: the set is derived,
/// the day's rows are counted on it, and the arithmetic is the shipped one.
/// </para>
/// </remarks>
internal sealed class ChargingTestChannel : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public NotificationChannelKind Kind => NotificationChannelKind.Sms;

    public bool Carries(MessageTemplateDefinition template) => true;

    public ValueTask<bool> IsUsableAsync(CancellationToken ct) => ValueTask.FromResult(true);

    public bool CanReach(SilexGisUser recipient) => true;

    public NotificationRoute Decide(SilexGisUser recipient, NotificationChannelChoice choice) =>
        NotificationRouting.Decide(choice, hasAddress: true);

    public Task<MessageResult> SendAsync(
        SilexGisUser recipient,
        string templateKey,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct) =>
        throw new NotSupportedException("Nothing in these tests sends; the sending worker is stopped.");
}

/// <summary>
/// The ceiling on what a day of announcements may cost, made to refuse.
/// </summary>
/// <remarks>
/// <para>
/// The other announcement tests run in the shipped configuration, where nothing charges per
/// message and the ceiling therefore never binds. That is the honest state of the product and it
/// is also why the ceiling needed a class of its own: counted, refused and let through are three
/// behaviours nothing else in the suite can reach, and thirty lines of money guard that has never
/// executed is the sort of thing whose first proof is an invoice.
/// </para>
/// <para>
/// The ceiling under test is <b>the one the application ships with</b> — a hundred a day, from no
/// stored setting and no configured one — not a number forced small for the test. A number that
/// only ever refuses when a test injects it says nothing about what an installation would do.
/// The installation's own lower ceiling is proved separately, below.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CavingGroupAnnouncementPaidCapTests : IAsyncLifetime, IDisposable
{
    private readonly string connectionString;
    private readonly List<Guid> mine = [];

    private SilexGisApiFactory factory = null!;
    private HttpClient leader = null!;
    private Guid leaderId;
    private Guid memberId;
    private Guid cavingGroupId;

    public CavingGroupAnnouncementPaidCapTests(PostgresFixture postgres) =>
        connectionString = postgres.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// A host with a charging channel wired in, and the installation's answer about paying for one
    /// coming from its configuration rather than from a saved form.
    /// </summary>
    /// <remarks>
    /// The switch is read from configuration on purpose: a settings key that binds to nothing
    /// fails silently and looks exactly like one that works, because the shipped default goes on
    /// doing the job. Here a wrong key name would leave the charging channel masked out and every
    /// refusal below would stop happening.
    /// </remarks>
    private async Task StartAsync(bool payForMessages = true)
    {
        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Announcements:PaidChannelsEnabled"] = payForMessages ? "true" : "false",
            },
            services =>
            {
                TestHostTweaks.WithoutJobWorker(services);
                foreach (var registered in services
                    .Where(d => d.ServiceType == typeof(INotificationChannel))
                    .ToList())
                {
                    services.Remove(registered);
                }

                services.AddScoped<INotificationChannel, ChargingTestChannel>();
            });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        leaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cap-lead-{suffix}@t.local");
        leader = await AuthHelper.BearerClientAsync(factory, $"cap-lead-{suffix}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cap-mem-{suffix}@t.local");
        mine.Add(leaderId);
        mine.Add(memberId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Any answer another class saved is cleared, so the configured one above is the one that
        // is read. A saved row replaces configuration wholesale, and this suite shares one
        // database.
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Announcements).ExecuteDeleteAsync();

        var cavingGroup = new CavingGroup
        {
            Name = $"Paid Cap Club {suffix}",
            Slug = $"paid-cap-club-{suffix}",
            Type = CavingGroupType.CavingClub,
        };
        db.CavingGroups.Add(cavingGroup);
        await db.SaveChangesAsync();
        cavingGroupId = cavingGroup.Id;

        await RosterHelper.AddMemberAsync(db, cavingGroupId, leaderId, CavingGroupRole.Owner);
        await RosterHelper.AddMemberAsync(db, cavingGroupId, memberId);

        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = leaderId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.CavingGroups,
            Actions = AccessAction.Execute,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = cavingGroupId,
            GrantedBy = leaderId,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task What_the_installation_will_spend_in_a_day_refuses_the_announcement_that_would_pass_it()
    {
        await StartAsync();

        // The day is already at the shipped ceiling: a hundred messages committed on a charging
        // channel, nothing injected, nothing configured down.
        await SpendAsync(AnnouncementSettings.DefaultDailyPaidMessageCap);

        var refused = await AnnounceAsync("One more and the bill goes up.");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_paid_cap_reached");

        // Refused outright rather than partly: nobody was told, and nothing was recorded to tell
        // them later either.
        (await NoticesForAsync(memberId)).ShouldBeEmpty();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.CavingGroupAnnouncements.CountAsync(a => a.CavingGroupId == cavingGroupId)).ShouldBe(0);
        }

        // And the positive half in the same test, so the refusal is evidence about the ceiling
        // rather than about a fixture that sends nothing: raising what the installation will spend
        // lets the same message through — which also says being refused did not spend the sender's
        // turn under the cooldown.
        await SetCeilingAsync(AnnouncementSettings.DefaultDailyPaidMessageCap * 10);

        var allowed = await AnnounceAsync("One more and the bill goes up.");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_announcement_that_stays_under_the_ceiling_goes_out()
    {
        await StartAsync();

        // One short of the shipped ceiling, and one person to reach: exactly at it, which is not
        // past it. An off-by-one here would refuse the hundredth message an installation said it
        // would pay for.
        await SpendAsync(AnnouncementSettings.DefaultDailyPaidMessageCap - 1);

        var response = await AnnounceAsync("The meet is on.");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_installation_that_will_spend_less_is_refused_sooner()
    {
        await StartAsync();

        // Nothing spent today at all, and still refused: the ceiling is the installation's own
        // answer, not a fixed hundred, and the count of what this one announcement would cost is
        // part of the sum rather than something checked afterwards.
        await SpendAsync(0);
        await SetCeilingAsync(1);
        await AddCrowdAsync(1);

        var refused = await AnnounceAsync("Two of you, one message allowed.");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_paid_cap_reached");

        await SetCeilingAsync(2);
        (await AnnounceAsync("Two of you, two messages allowed.")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_announcement_still_waiting_to_be_handed_out_is_headroom_the_next_one_cannot_have()
    {
        await StartAsync();
        await SpendAsync(0);
        await SetCeilingAsync(2);

        // A second sender, because what is being pinned is two announcements racing each other
        // and not one sender's own cooldown, which would refuse the second with a different
        // answer entirely.
        using var other = await SecondSenderAsync();

        // Accepted: two people to reach and two messages allowed. Nothing has routed — the
        // sending worker is stopped here exactly as it is briefly stopped in life, between the
        // request that accepts an announcement and the pass that turns it into outbound copies.
        (await AnnounceAsync("The meet is on.")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The window the ceiling used to be blind in. Counting only what has been handed over
        // reads the same empty headroom the first announcement already took, and lets a second
        // one through — four charged messages against a ceiling of two.
        var refused = await AnnounceAsAsync(other, "And so is the other one.");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_paid_cap_reached");

        // And the positive half: the refusal is arithmetic about the ceiling rather than a second
        // sender being unable to announce at all.
        await SetCeilingAsync(4);
        (await AnnounceAsAsync(other, "And so is the other one."))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_installation_that_pays_for_nothing_counts_nothing()
    {
        // The default, and the shape of every installation today: the switch is off, so the
        // charging channel is masked out of what an announcement may use, the day's spending is
        // zero however many messages went out, and no announcement is ever refused for cost.
        await StartAsync(payForMessages: false);
        await SpendAsync(AnnouncementSettings.DefaultDailyPaidMessageCap * 2);

        var response = await AnnounceAsync("Nothing here costs anything.");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await NoticesForAsync(memberId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_operator_sees_what_the_day_has_cost_and_what_it_may()
    {
        await StartAsync();
        await SpendAsync(7);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"cap-admin-{suffix}@t.local";
        mine.Add(await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail));
        using var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);

        var health = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/notifications/health");

        // The same count the send refuses against. An operator shown a different number from the
        // one the guard uses would read headroom on the day announcements started being turned
        // away.
        health.GetProperty("paidMessagesToday").GetInt32().ShouldBe(7);
        health.GetProperty("dailyPaidMessageCap").GetInt32()
            .ShouldBe(AnnouncementSettings.DefaultDailyPaidMessageCap);
    }

    /// <summary>Puts <paramref name="count"/> charged messages into today, and nothing into any other day.</summary>
    /// <remarks>
    /// Written straight into the outbox rather than produced by a hundred announcements, because
    /// what is under test here is the arithmetic and not the transport, and the day's rows left by
    /// other test classes are cleared first so the count under test is this test's own. Every class in this suite shares one database and they do not run at the same
    /// time as each other.
    /// </remarks>
    private async Task SpendAsync(int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationDeliveries.ExecuteDeleteAsync();

        // What a day has promised counts as well as what it has handed over, so an announcement
        // another class left waiting to be routed would be spending this test cannot see.
        await db.Notifications
            .Where(n => n.RoutedAt == null && n.Category == NotificationCategory.GroupAnnouncement)
            .ExecuteDeleteAsync();
        await db.CavingGroupAnnouncements.Where(a => a.ExpandedAt == null).ExecuteDeleteAsync();

        if (count == 0)
        {
            return;
        }

        var notifications = Enumerable.Range(0, count).Select(_ => new Notification
        {
            RecipientUserId = memberId,
            Category = NotificationCategory.GroupAnnouncement,
            TemplateKey = MessageTemplateCatalog.NotifyGroupAnnouncement,
            RoutedAt = DateTimeOffset.UtcNow,
        }).ToList();
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync();

        // Written by hand, including when: a delivery row's timestamp is stamped by whatever
        // creates it rather than by the database, so a row seeded without one lands in the year 1
        // and falls outside the day the ceiling counts over — which would leave every assertion
        // below passing for the wrong reason.
        var now = DateTimeOffset.UtcNow;
        db.NotificationDeliveries.AddRange(notifications.Select(n => new NotificationDelivery
        {
            NotificationId = n.Id,
            RecipientUserId = memberId,
            Channel = NotificationChannel.Email,
            Status = NotificationDeliveryStatus.Pending,
            CreatedAt = now,
            NotBefore = now,
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>Saves the installation's own ceiling, as the administrator's form would.</summary>
    private async Task SetCeilingAsync(int cap)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(
            AppSettingSections.Announcements,
            new AnnouncementSettings { PaidChannelsEnabled = true, DailyPaidMessageCap = cap });
    }

    /// <summary>Adds more account-holding members to the club under test.</summary>
    private async Task AddCrowdAsync(int count)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        for (var i = 0; i < count; i++)
        {
            var extra = await AuthHelper.CreateUserAsync(
                factory, GlobalRoles.Viewer, $"cap-crowd-{i}-{suffix}@t.local");
            mine.Add(extra);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, extra);
        }
    }

    private Task<HttpResponseMessage> AnnounceAsync(string message) => AnnounceAsAsync(leader, message);

    private Task<HttpResponseMessage> AnnounceAsAsync(HttpClient client, string message) =>
        client.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/announcements", new { message });

    /// <summary>
    /// A second account on the roster that may also announce to it, so two announcements can be
    /// made one after the other without the per-sender cooldown being what stops the second.
    /// </summary>
    private async Task<HttpClient> SecondSenderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"cap-lead2-{suffix}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        mine.Add(id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await RosterHelper.AddMemberAsync(db, cavingGroupId, id, CavingGroupRole.Owner);
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = id,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.CavingGroups,
                Actions = AccessAction.Execute,
                ScopeKind = AccessScopeKind.Object,
                ScopeId = cavingGroupId,
                GrantedBy = id,
            });
            await db.SaveChangesAsync();
        }

        return await AuthHelper.BearerClientAsync(factory, email);
    }

    private async Task<List<Notification>> NoticesForAsync(params Guid[] recipients)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => recipients.Contains(n.RecipientUserId) && n.TargetId == cavingGroupId)
            .ToListAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is null)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationDeliveries.ExecuteDeleteAsync();
        await db.Notifications.Where(n => mine.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        await db.AppSettings.Where(s => s.Key == AppSettingSections.Announcements).ExecuteDeleteAsync();
        await db.CavingGroupAnnouncements.Where(a => a.CavingGroupId == cavingGroupId).ExecuteDeleteAsync();
        await db.AccessEntries.Where(e => e.ScopeId == cavingGroupId).ExecuteDeleteAsync();
        await db.CavingGroups.Where(c => c.Id == cavingGroupId).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        if (leader is not null)
        {
            leader.Dispose();
        }

        if (factory is not null)
        {
            factory.Dispose();
        }
    }
}
