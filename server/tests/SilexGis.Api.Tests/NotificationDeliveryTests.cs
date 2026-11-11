// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Notification delivery end to end: a real action over HTTP queues a notification, nothing is
/// sent inside the request, and draining produces the message the recipient would receive — or
/// does not, when they asked not to hear about it, in which case the notification is still there
/// to be read and simply never left the system.
/// </summary>
/// <remarks>
/// The worker never runs in tests (the factory sets its poll interval to zero, because every test
/// class shares one database). Drains are driven directly instead, which is also what makes the
/// digest testable without waiting a day.
/// </remarks>
public sealed class NotificationDeliveryTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient manager = null!;   // creates caving groups, does the granting
    private HttpClient recipient = null!;
    private Guid managerId;
    private Guid recipientId;

    /// <summary>
    /// A clock the test sets, so what the application stamps can be asserted as a value rather
    /// than as a range. It is also the clock a claim judges a delivery due by — the same one that
    /// stamped it — so a row is made due by backdating it against this clock, not the wall's.
    /// </summary>
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);

    /// <summary>Kept so one test can stand a second host up against the same database.</summary>
    private readonly string connectionString;

    public NotificationDeliveryTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "500" },
            services => services.AddSingleton<TimeProvider>(clock));
    }

    public async ValueTask InitializeAsync()
    {
        managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, ManagerEmail);
        recipientId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, RecipientEmail);

        // Leaving a caving group is refused outright when the installation would be left with no
        // live full administrator, so a class that never mints one can only pass while some other
        // class happens to have run first and left one behind in the shared database. This class
        // creates only a manager and an editor, so it seeds its own and depends on nobody.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"notify-adm-{suffix}@t.local");

        manager = await AuthHelper.BearerClientAsync(factory, ManagerEmail);
        recipient = await AuthHelper.BearerClientAsync(factory, RecipientEmail);

        // Notifications are one table for the whole database, and earlier test classes queue rows of
        // their own that nothing drains. A drain here is not scoped to a user, so those leftovers
        // would be claimed first, making the passes and their counts depend on whatever ran before
        // it. (The failures this class injects are aimed at its own recipient's address for the
        // same reason, and do not depend on this.)
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications.ExecuteDeleteAsync();
    }

    private string ManagerEmail => $"notif-mgr-{suffix}@t.local";

    private string RecipientEmail => $"notif-rcp-{suffix}@t.local";

    [Fact]
    public async Task Adding_someone_to_a_caving_group_queues_a_notification_without_sending_inside_the_request()
    {
        factory.Messages.Clear();

        await AddToCavingGroupAsync();

        // Nothing goes out on the request path — that is the point of queuing it.
        MineSent().ShouldBeEmpty();
        var queued = await RowsAsync();
        queued.Count.ShouldBe(1);
        queued[0].Category.ShouldBe(NotificationCategory.CavingGroupMembership);
        // Which channels this leaves on has not been decided yet — deciding it reads the
        // recipient, which the slice that queued it may not do.
        queued[0].RoutedAt.ShouldBeNull();
        (await DeliveriesAsync()).ShouldBeEmpty();

        // Routing and one send, so two rows moved.
        (await DrainAsync()).ShouldBe(2);

        var message = factory.Messages.LastTo(RecipientEmail);
        message.Channel.ShouldBe("email");
        message.Body.ShouldContain($"Notif caving group {suffix}");
        (await RowsAsync())[0].RoutedAt.ShouldNotBeNull();
        var delivery = (await DeliveriesAsync()).ShouldHaveSingleItem();
        delivery.Channel.ShouldBe(NotificationChannel.Email);
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_recipient_who_reads_Romanian_is_written_to_in_Romanian()
    {
        factory.Messages.Clear();

        // Set through the route the browser calls, not by writing the column: the whole defect
        // was that nothing ever called it, so a test that assigns the column directly would prove
        // the templates work and nothing about whether anyone can ever reach them.
        (await recipient.PutAsJsonAsync(
            "/api/v1/me/locale", new { language = "ro", timeZone = "Europe/Bucharest" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await AddToCavingGroupAsync("ro");
        (await DrainAsync()).ShouldBe(2);

        var message = factory.Messages.LastTo(RecipientEmail);
        message.Subject.ShouldBe($"Ai fost adăugat în Notif caving group {suffix} ro");
        message.Body.ShouldContain("te-a adăugat în grupul");

        // The negative half in the same test: the English wording of the same template is gone,
        // so this cannot pass on a message that merely happens to contain a Romanian word.
        message.Body.ShouldNotContain("added you to the caving group");
    }

    [Fact]
    public async Task A_second_drain_sends_nothing_more()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();
        var afterFirst = MineSent().Count;
        await DrainAsync();

        // The first pass terminated the row, so the second claims nothing.
        afterFirst.ShouldBe(1);
        MineSent().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nobody_is_told_about_their_own_action()
    {
        var cavingGroupId = await CreateCavingGroupAsync();
        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members",
            new { caverId = await RosterHelper.CaverIdForAsync(factory, recipientId), role = "Member" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterJoining = (await RowsAsync()).Count;

        // Leaving of your own accord: you already know, and mailing you about it is noise.
        var recipientCaverId = await RosterHelper.CaverIdForAsync(factory, recipientId);
        (await recipient.DeleteAsync($"/api/v1/caving-groups/{cavingGroupId}/members/{recipientCaverId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RowsAsync()).Count.ShouldBe(afterJoining);
    }

    [Fact]
    public async Task A_switched_off_category_produces_no_delivery_and_is_still_in_the_inbox()
    {
        await SetMailAsync("immediate", "cavingGroupMembership", "off");
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();

        // Nothing left the system, and there is no row saying so: not wanting an email about
        // something is a routing answer, not a failed delivery.
        (await DeliveriesAsync()).ShouldBeEmpty();
        MineSent().ShouldBeEmpty();

        // And the notification is still there to be read. This is the whole point of the split —
        // the events somebody switched email off for are exactly the ones an inbox exists for.
        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.RoutedAt.ShouldNotBeNull();
        row.ReadAt.ShouldBeNull();
    }

    [Fact]
    public async Task Mail_switched_off_on_every_ordinary_category_leaves_it_all_in_the_inbox()
    {
        await SetMailAsync("off");
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();

        (await DeliveriesAsync()).ShouldBeEmpty();
        MineSent().ShouldBeEmpty();
        (await RowsAsync()).ShouldHaveSingleItem().RoutedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_security_alert_is_sent_even_with_every_ordinary_category_muted()
    {
        await SetMailAsync("off");
        factory.Messages.Clear();

        // Changing the password is the alert: if it was not them, someone else knows it.
        (await recipient.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = AuthHelper.Password,
            newPassword = "a-new-long-password-1",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await DrainAsync();

        // Exactly one: the change once queued its warning both before and after the password was
        // written, and every holder was told twice.
        (await RowsAsync()).Where(r => r.Category == NotificationCategory.SecurityAlerts).ShouldHaveSingleItem();
        (await DeliveriesAsync()).ShouldContain(d => d.Status == NotificationDeliveryStatus.Sent);
        MineSent().Count.ShouldBe(1);
        factory.Messages.LastTo(RecipientEmail).Body.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_password_reset_warns_the_holder_once()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/password/reset", new
        {
            email = RecipientEmail,
            token = await ResetTokenAsync(recipientId),
            newPassword = "a-reset-long-password-1",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RowsAsync()).Where(r => r.TemplateKey == MessageTemplateCatalog.NotifySecurityPasswordChanged)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Choosing_the_first_password_of_an_invited_account_is_not_a_warning()
    {
        // An account with neither a password nor a linked provider is what an administrator's
        // invitation creates; the reset link is how its holder chooses a password.
        var email = $"notif-inv-{suffix}@t.local";
        Guid invitedId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var user = new SilexGisUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
            invitedId = user.Id;
        }

        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/password/reset", new
        {
            email,
            token = await ResetTokenAsync(invitedId),
            newPassword = "an-invited-long-password-1",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RowsAsync(invitedId)).ShouldNotContain(r => r.TemplateKey == MessageTemplateCatalog.NotifySecurityPasswordChanged);
    }

    private async Task<string> ResetTokenAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GeneratePasswordResetTokenAsync(user!);
    }

    [Fact]
    public async Task A_daily_digest_holds_events_back_and_then_sends_one_message_for_all_of_them()
    {
        await SetMailAsync("daily");
        await AddToCavingGroupAsync();
        await AddToCavingGroupAsync("second");
        factory.Messages.Clear();

        // The immediate pass routes them into the digest and sends nothing.
        await DrainAsync();
        var deferred = await DeliveriesAsync();
        deferred.Count.ShouldBe(2);
        deferred.ShouldAllBe(d => d.Status == NotificationDeliveryStatus.Deferred);
        MineSent().ShouldBeEmpty();

        // The summary hour is tomorrow morning on this class's clock; the window is brought to
        // the clock rather than the clock moved to it.
        await MakeDigestDueAsync();
        await RunDigestAsync();

        // One message, both events in it — and the same rows, so nothing can arrive twice.
        MineSent().Count.ShouldBe(1);
        var digest = factory.Messages.LastTo(RecipientEmail);
        digest.Body.ShouldContain($"Notif caving group {suffix}");
        digest.Body.ShouldContain($"Notif caving group {suffix} second");
        (await DeliveriesAsync()).ShouldAllBe(d => d.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_failed_send_backs_off_and_then_succeeds()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainWithTheirSendFailingAsync();

        var afterFailure = (await DeliveriesAsync()).ShouldHaveSingleItem();
        afterFailure.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        afterFailure.Attempts.ShouldBe(1);
        afterFailure.Error.ShouldNotBeNullOrWhiteSpace();

        // The exact step off the ladder, not merely "some time in the future": a backoff that
        // silently collapsed to a second would still be greater than now. The tolerance is the
        // column's own resolution — the database keeps microseconds and the clock keeps ticks —
        // and is six orders of magnitude tighter than the step being asserted.
        afterFailure.NotBefore.ShouldBe(
            clock.Now + NotificationRouting.RetryDelay(1), TimeSpan.FromMilliseconds(1));

        // Due again only once the backoff has passed; bring it forward rather than wait.
        await MakeDueAsync();
        await DrainAsync();

        MineSent().Count.ShouldBe(1);
        (await DeliveriesAsync()).ShouldHaveSingleItem().Status.ShouldBe(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_send_the_far_side_refuses_for_good_is_dead_at_once_with_its_reason_and_is_not_retried()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        clock.Now = DateTimeOffset.UtcNow;
        try
        {
            factory.Messages.RefuseSendsTo = RecipientEmail;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.RefuseSendsTo = null;
        }

        // One attempt, and no ladder: the row is where an operator looks for what failed, with
        // the refusal that was given, instead of being tried four more times over the next hours.
        var refused = (await DeliveriesAsync()).ShouldHaveSingleItem();
        refused.Status.ShouldBe(NotificationDeliveryStatus.Dead);
        refused.Attempts.ShouldBe(1);
        refused.Error.ShouldNotBeNullOrWhiteSpace();
        refused.Error.ShouldContain("refus");

        // Nothing is waiting to be sent: a later pass, with the mail server accepting everything
        // again, still leaves the message unsent — only an operator's retry re-queues a dead row.
        await MakeDueAsync();
        await DrainAsync();
        MineSent().ShouldBeEmpty();
        (await DeliveriesAsync()).ShouldHaveSingleItem().Status.ShouldBe(NotificationDeliveryStatus.Dead);
    }

    [Fact]
    public async Task A_row_naming_a_template_that_does_not_exist_dies_without_taking_the_batch_with_it()
    {
        await AddToCavingGroupAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            NotificationQueue.Enqueue(
                db, recipientId, NotificationCategory.CavingGroupMembership, "notify.does-not-exist",
                new Dictionary<string, string>());
            await db.SaveChangesAsync();
        }

        factory.Messages.Clear();
        await DrainAsync();

        var poison = (await RowsAsync()).Single(r => r.TemplateKey == "notify.does-not-exist");
        var itsDelivery = (await DeliveriesAsync()).Single(d => d.NotificationId == poison.Id);
        itsDelivery.Status.ShouldBe(NotificationDeliveryStatus.Dead);
        itsDelivery.Error.ShouldNotBeNull();
        // The good row in the same batch still went out.
        MineSent().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_send_that_times_out_backs_off_and_its_batch_mate_is_recorded_sent_once()
    {
        // The batch-mate is queued first, so it is handed over before the send that times out —
        // the order in which a single save after the whole batch used to lose its outcome.
        await QueueForTheManagerAsync();
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        try
        {
            factory.Messages.TimeOutSendsTo = RecipientEmail;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.TimeOutSendsTo = null;
        }

        // A timeout nobody asked for is a failed send like any other: it backs off one step.
        var timedOut = (await DeliveriesAsync()).ShouldHaveSingleItem();
        timedOut.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        timedOut.Attempts.ShouldBe(1);
        timedOut.Error.ShouldNotBeNullOrWhiteSpace();
        timedOut.NotBefore.ShouldBe(clock.Now + NotificationRouting.RetryDelay(1), TimeSpan.FromMilliseconds(1));

        (await DeliveriesAsync(managerId)).ShouldContain(d => d.Status == NotificationDeliveryStatus.Sent);
        await MakeDueAsync(managerId);
        await DrainAsync();
        BatchMateSent().Count.ShouldBe(1, "the batch-mate's outcome was recorded, so it is not sent again");
    }

    [Fact]
    public async Task A_send_that_throws_outside_the_transport_backs_off_without_taking_its_batch_mate_with_it()
    {
        await QueueForTheManagerAsync();
        await AddToCavingGroupAsync();
        await DrainRoutingAsync();
        factory.Messages.Clear();

        // A template renamed by an upgrade while a delivery was backing off: composing the message
        // throws before any transport is asked, which is outside what the dispatcher catches.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Notifications
                .Where(n => n.RecipientUserId == recipientId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.TemplateKey, "notify.renamed-since"));
        }

        await DrainAsync();

        var thrown = (await DeliveriesAsync()).ShouldHaveSingleItem();
        thrown.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        thrown.Attempts.ShouldBe(1);
        thrown.Error.ShouldNotBeNullOrWhiteSpace();
        BatchMateSent().Count.ShouldBe(1);
        (await DeliveriesAsync(managerId)).ShouldContain(d => d.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_delivery_whose_attempts_never_recorded_an_outcome_is_given_up_on_at_the_ceiling()
    {
        // A send that takes the pass down with it records nothing, and the lease brings the row
        // back. Each of those sends still counted its attempt as it started, so the ceiling
        // reaches a row that never once got as far as being failed.
        await AddToCavingGroupAsync();
        await DrainRoutingAsync();
        factory.Messages.Clear();

        for (var attempt = 0; attempt < NotificationRouting.MaxAttempts; attempt++)
        {
            await PassCancelledAtTheirSendAsync();
            (await DeliveriesAsync()).ShouldHaveSingleItem().Attempts.ShouldBe(attempt + 1);
            await MakeDueAsync();
        }

        await DrainAsync();

        var given = (await DeliveriesAsync()).ShouldHaveSingleItem();
        given.Status.ShouldBe(NotificationDeliveryStatus.Dead);
        given.Error.ShouldNotBeNullOrWhiteSpace();
        given.Attempts.ShouldBe(NotificationRouting.MaxAttempts, "the row given up on was not sent once more");
        MineSent().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pass_that_stops_on_one_send_charges_no_attempt_to_the_rows_it_never_reached()
    {
        // Queued in this order, so the recipient's row is the first of the batch and the manager's
        // waits behind it — the row a pass that dies on its first send never gets to.
        await AddToCavingGroupAsync();
        await DrainRoutingAsync();
        await QueueForTheManagerAsync();
        await DrainRoutingAsync();
        factory.Messages.Clear();

        await PassCancelledAtTheirSendAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().Attempts.ShouldBe(1);
        var waiting = (await DeliveriesAsync(managerId)).ShouldHaveSingleItem();
        waiting.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        waiting.Attempts.ShouldBe(0, "its send never started, so it has used no attempt");

        // And it is sent once its lease is over, with its whole budget still in hand.
        await MakeDueAsync(managerId);
        await DrainAsync();
        var sent = (await DeliveriesAsync(managerId)).ShouldHaveSingleItem();
        sent.Status.ShouldBe(NotificationDeliveryStatus.Sent);
        sent.Attempts.ShouldBe(1);
        BatchMateSent().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_summary_whose_passes_never_record_an_outcome_is_given_up_on_at_the_ceiling()
    {
        await SetMailAsync("daily");
        await AddToCavingGroupAsync();
        await DrainAsync();
        factory.Messages.Clear();

        // Each claim of a summary counts an attempt on its rows; a pass that claims and then dies
        // records nothing, which is what a bare claim leaves behind.
        for (var attempt = 0; attempt < NotificationRouting.MaxAttempts; attempt++)
        {
            await MakeDigestDueAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await NotificationDeliverySql.ClaimDueDigestAsync(db, clock.GetUtcNow(), CancellationToken.None))
                .ShouldNotBeEmpty();
        }

        await MakeDigestDueAsync();
        await RunDigestAsync();

        var given = (await DeliveriesAsync()).ShouldHaveSingleItem();
        given.Status.ShouldBe(NotificationDeliveryStatus.Dead);
        given.Error.ShouldNotBeNullOrWhiteSpace();
        MineSent().ShouldBeEmpty("a summary given up on is not sent");
    }

    [Fact]
    public async Task Two_summary_passes_running_at_once_send_one_recipient_one_summary()
    {
        await SetMailAsync("daily");
        for (var i = 0; i < 4; i++)
        {
            await QueueLinkedNotificationAsync($"/features/{Guid.NewGuid()}");
        }

        await DrainAsync();
        factory.Messages.Clear();
        await MakeDigestDueAsync();
        (await DeliveriesAsync()).Count.ShouldBe(4);

        // Both passes are released together, each on its own scope and connection, as two workers
        // polling the same table would be.
        using var start = new SemaphoreSlim(0, 2);
        var passes = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.WaitAsync();
            await RunDigestAsync();
        })).ToList();
        start.Release(2);
        await Task.WhenAll(passes);

        MineSent().Count.ShouldBe(1, "one recipient's due summary is one message, whichever pass took it");
        (await DeliveriesAsync()).ShouldAllBe(d => d.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_notification_that_cannot_be_routed_is_recorded_dead_and_its_batch_mate_is_still_routed()
    {
        // A second host whose mail channel throws while deciding for this test's recipient, which
        // stands in for anything about one notification that throws while routing it.
        await using var throwing = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "500" },
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                var email = services.Single(d =>
                    d.ServiceType == typeof(INotificationChannel)
                    && d.ImplementationType == typeof(EmailNotificationChannel));
                services.Remove(email);
                services.AddScoped<EmailNotificationChannel>();
                services.AddScoped<INotificationChannel>(sp => new ThrowsDecidingFor(
                    sp.GetRequiredService<EmailNotificationChannel>(), RecipientEmail));
            });

        await AddToCavingGroupAsync();
        await QueueForTheManagerAsync();

        await using (var scope = throwing.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
            while (await service.RouteAsync(CancellationToken.None) > 0)
            {
            }
        }

        var dead = (await DeliveriesAsync()).ShouldHaveSingleItem();
        dead.Status.ShouldBe(NotificationDeliveryStatus.Dead);
        dead.Error.ShouldNotBeNullOrWhiteSpace();
        (await DeliveriesAsync(managerId)).ShouldHaveSingleItem().Status
            .ShouldBe(NotificationDeliveryStatus.Pending, "the batch-mate was routed as usual");
    }

    [Fact]
    public async Task A_backoff_is_measured_on_the_clock_that_stamped_it()
    {
        // The application's clock ten minutes behind the database's. The first step of the ladder
        // is one minute, so measured against the database the retry would already be due and the
        // drain would climb the ladder at once.
        clock.Now = DateTimeOffset.UtcNow.AddMinutes(-10);

        await AddToCavingGroupAsync();
        factory.Messages.Clear();
        await DrainWithTheirSendFailingAsync();

        var afterFailure = (await DeliveriesAsync()).ShouldHaveSingleItem();
        afterFailure.Attempts.ShouldBe(1);
        afterFailure.NotBefore.ShouldBe(clock.Now + NotificationRouting.RetryDelay(1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task An_installation_with_no_mail_server_settles_the_row_instead_of_retrying_forever()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        try
        {
            factory.Messages.MailConfigured = false;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.MailConfigured = true;
        }

        // The message went to the log, which is what a mail-less installation does. Retrying it
        // would never succeed and the table would grow without bound.
        (await DeliveriesAsync()).ShouldHaveSingleItem().Status.ShouldBe(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task The_opt_out_link_in_a_message_switches_that_category_off_without_signing_in()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();
        await DrainAsync();

        var token = OptOutTokenInLastMessage();

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/notifications/unsubscribe", new { token });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The setting the page shows is the one that changed — and only that one. Somebody who
        // clicked a link in a mail client said where they do not want to be reached and said
        // nothing whatever about the inbox inside the application, so the inbox cell is the
        // positive half of the same assertion.
        (await CellAsync("cavingGroupMembership", "email")).ShouldBe("off");
        (await CellAsync("cavingGroupMembership", "inApp")).ShouldBe("immediate");
    }

    [Fact]
    public async Task A_link_in_a_message_is_a_whole_address_and_one_that_is_already_whole_is_left_alone()
    {
        // A producer writes the path to what it is reporting; only the sender knows where the
        // installation lives. Both arms in one test, because "it prefixed something" and "it
        // prefixed everything" look identical from a single relative link.
        await QueueLinkedNotificationAsync("/caves/42");
        await QueueLinkedNotificationAsync("https://elsewhere.example/caves/42");
        factory.Messages.Clear();
        await DrainAsync();

        var bodies = MineSent().Select(m => m.Body).ToList();
        bodies.Count.ShouldBe(2);

        // The factory configures the installation address, so the whole link is knowable here.
        bodies.ShouldContain(b => b.Contains("http://localhost/caves/42", StringComparison.Ordinal));
        bodies.ShouldContain(b => b.Contains("https://elsewhere.example/caves/42", StringComparison.Ordinal));

        // Nothing was made absolute twice, and no message still prints a bare path.
        bodies.ShouldAllBe(b => !b.Contains("http://localhosthttp", StringComparison.Ordinal));
        bodies.ShouldAllBe(b => !b.Contains("http://localhosthttps", StringComparison.Ordinal));
        bodies.ShouldAllBe(b => !b.Contains("\n/caves/42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_opt_out_link_in_a_daily_summary_stops_the_summary_and_not_one_category_in_it()
    {
        await SetMailAsync("daily");
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();
        await MakeDigestDueAsync();
        await RunDigestAsync();

        var token = OptOutTokenInLastMessage();

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/notifications/unsubscribe", new { token });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The link says what it switched off, and a summary names no category — the whole defect
        // was that it used to name one at random.
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        result.GetProperty("kind").GetString().ShouldBe("dailyDigest");
        result.GetProperty("category").ValueKind.ShouldBe(JsonValueKind.Null);

        // A summary collects whatever the reader still hears about by mail, so the only honest
        // reading of "stop sending me this" is to stop the mail — on every category that may be
        // muted, not on the one the summary happened to be holding.
        foreach (var category in MutableCategories)
        {
            (await CellAsync(category, "email")).ShouldBe("off", category);
        }

        // The positive half: nothing about the inbox moved. The events are still read there,
        // which is the whole reason stopping the mail is a safe thing for a link to do.
        (await CellAsync("cavingGroupMembership", "inApp")).ShouldBe("immediate");
    }

    [Fact]
    public async Task A_summary_already_waiting_to_go_out_is_not_sent_after_its_own_opt_out_link_is_used()
    {
        // The summary's landing page says the summary has stopped. Rows deferred before the click
        // were routed while it was still wanted, and nothing read the preferences again on the way
        // out — so tomorrow's summary went anyway, carrying another opt-out link.
        await SetMailAsync("daily");
        await AddToCavingGroupAsync();
        await DrainAsync();

        await SetMailAsync("off");

        await AddToCavingGroupAsync("after the switch was thrown");
        await DrainAsync();
        factory.Messages.Clear();

        await MakeDigestDueAsync();
        await RunDigestAsync();
        await RunDigestAsync();

        MineSent().ShouldBeEmpty();
        // The waiting deliveries are gone, not marked: there is nothing left that has to leave.
        (await DeliveriesAsync()).ShouldBeEmpty();
        // What was going to be summarised is still readable.
        (await RowsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_category_switched_off_after_it_was_held_back_is_dropped_from_the_summary()
    {
        await SetMailAsync("daily");
        await AddToCavingGroupAsync();
        await DrainAsync();

        await SetMailAsync("daily", "cavingGroupMembership", "off");
        factory.Messages.Clear();

        await MakeDigestDueAsync();
        await RunDigestAsync();

        MineSent().ShouldBeEmpty();
        (await DeliveriesAsync()).ShouldBeEmpty();
        (await RowsAsync()).ShouldHaveSingleItem().ReadAt.ShouldBeNull();
    }

    [Fact]
    public async Task An_invented_opt_out_token_is_refused()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/notifications/unsubscribe", new { token = "not-a-real-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("notification.unsubscribe_invalid");
    }

    [Fact]
    public async Task A_security_alert_carries_no_opt_out_link()
    {
        factory.Messages.Clear();
        await recipient.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = AuthHelper.Password,
            newPassword = "a-new-long-password-2",
        });
        await DrainAsync();

        // Offering a link that the endpoint would refuse would be a lie: the settings page does
        // not let this category be switched off either.
        factory.Messages.LastTo(RecipientEmail).Body.ShouldNotContain("unsubscribe");
    }

    [Fact]
    public async Task A_crash_between_the_claim_and_the_send_leaves_the_delivery_due_again()
    {
        // Claiming is a lease, not a status change, which is why there is no Claimed state and no
        // startup sweep: a sweep would re-send everything that was in flight during a restart.
        // Claiming and then doing nothing is exactly what a process dying mid-send leaves behind.
        await AddToCavingGroupAsync();
        await DrainRoutingAsync();
        factory.Messages.Clear();

        var claimed = await ClaimDueAsync();
        claimed.ShouldNotBeEmpty("routing should have left a delivery due");

        var leased = (await DeliveriesAsync()).ShouldHaveSingleItem();
        leased.Status.ShouldBe(NotificationDeliveryStatus.Pending, "a lease is not a status change");
        leased.Attempts.ShouldBe(0, "no send started, so no attempt was used");
        leased.NotBefore.ShouldBeGreaterThan(clock.Now, "the lease hides it while a send is in flight");

        // While the lease holds, a second worker passing by must not pick it up and send it twice.
        await DrainAsync();
        MineSent().ShouldBeEmpty("a leased delivery is nobody else's to send");

        // Once the lease expires the row is due again by itself, with nothing having swept it.
        await MakeDueAsync();
        await DrainAsync();

        MineSent().Count.ShouldBe(1);
        (await DeliveriesAsync()).ShouldHaveSingleItem().Status.ShouldBe(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task Two_drains_running_at_once_settle_no_delivery_twice()
    {
        // Both claims are FOR UPDATE SKIP LOCKED, and routing stamps the notification rather than
        // leasing it, so neither pass can be fanned out or sent by two workers at once. Without
        // that, the ordinary case — one grant to a caving group is one notification per member —
        // would mail everybody twice whenever two workers happened to overlap.
        const int Queued = 8;
        for (var i = 0; i < Queued; i++)
        {
            await QueueLinkedNotificationAsync($"/features/{Guid.NewGuid()}");
        }

        factory.Messages.Clear();

        await Task.WhenAll(DrainAsync(), DrainAsync());

        var mine = (await RowsAsync()).Where(r => r.TemplateKey == MessageTemplateCatalog.NotifyPermissionGranted).ToList();
        mine.Count.ShouldBe(Queued);

        // One delivery per notification, never two: the unique index would refuse a second, so a
        // fan-out that ran twice would have failed a save rather than quietly duplicated.
        var deliveries = await DeliveriesAsync();
        foreach (var notification in mine)
        {
            deliveries.Count(d => d.NotificationId == notification.Id)
                .ShouldBe(1, $"notification {notification.Id} should have been routed exactly once");
        }

        deliveries.ShouldAllBe(d => d.Status == NotificationDeliveryStatus.Sent);

        // And one message each actually left the system — the assertion the row counts cannot make.
        MineSent().Count.ShouldBe(Queued);
    }

    [Fact]
    public async Task Pruning_takes_whole_notifications_past_the_window_and_nothing_inside_it()
    {
        // The one thing in this pipeline that deletes data. One window over the notification
        // itself, taking read and unread alike and taking its deliveries with it, rather than the
        // old rule which kept only what had failed and deleted everything an inbox exists to show.
        // A row is seeded on both sides of the window, so a pass that deleted nothing and a pass
        // that deleted everything both fail.
        var expiredUnread = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-366), readAt: null);
        var expiredRead = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-366), readAt: DateTimeOffset.UtcNow);
        var recentUnread = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-364), readAt: null);
        var recentRead = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-364), readAt: DateTimeOffset.UtcNow);

        // A message that never arrived used to be kept forever, because a permanently failed send
        // was the only record an operator had of one. It is not any more: the notification itself
        // is that record now, it is readable in the inbox whatever happened to the outbound copy,
        // and an undeletable row is a table that only grows.
        var expiredDead = await SeedAgedAsync(
            DateTimeOffset.UtcNow.AddDays(-366), readAt: null, status: NotificationDeliveryStatus.Dead);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        var surviving = (await RowsAsync()).Select(r => r.Id).ToList();

        // Read and unread alike, once they are past the window.
        surviving.ShouldNotContain(expiredUnread);
        surviving.ShouldNotContain(expiredRead);
        surviving.ShouldNotContain(expiredDead);

        // Inside the window nothing is touched — least of all something nobody has read yet,
        // which is precisely what a bell is counting.
        surviving.ShouldContain(recentUnread);
        surviving.ShouldContain(recentRead);

        // The delivery went with its notification, and only that one.
        var deliveries = await DeliveriesAsync();
        deliveries.ShouldNotContain(d => d.NotificationId == expiredUnread);
        deliveries.ShouldNotContain(d => d.NotificationId == expiredDead);
        deliveries.ShouldContain(d => d.NotificationId == recentUnread);
    }

    [Fact]
    public async Task An_installation_can_say_how_long_notifications_are_kept()
    {
        // A configuration key that binds to nothing fails silently and looks exactly like one that
        // works, because the shipped default goes on doing the job and the test written to prove
        // the setting passes for the wrong reason. So this asks for a window a year shorter than
        // the default and seeds a row on each side of it: under the default both rows survive and
        // the assertion below fails, which is what makes it evidence that the value was read.
        var pastTheWindow = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-31), readAt: null);
        var insideTheWindow = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-29), readAt: null);

        await using (var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:RateLimitPerMinute"] = "500",
                ["Notifications:RetentionDays"] = "30",
            }))
        {
            await using var scope = configured.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        var surviving = (await RowsAsync()).Select(r => r.Id).ToList();
        surviving.ShouldNotContain(pastTheWindow);
        surviving.ShouldContain(insideTheWindow);
    }

    [Fact]
    public async Task A_retention_window_of_nothing_is_refused_rather_than_emptying_the_table()
    {
        // Zero and below are the two values a mistyped setting most easily produces, and either
        // read literally would delete the whole table on the next pass. The shipped default is
        // used instead, so a row inside it survives.
        var recent = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-1), readAt: null);

        await using (var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:RateLimitPerMinute"] = "500",
                ["Notifications:RetentionDays"] = "0",
            }))
        {
            await using var scope = configured.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        (await RowsAsync()).Select(r => r.Id).ShouldContain(recent);
    }

    [Fact]
    public async Task A_saved_retention_window_beats_the_one_the_deployment_configured()
    {
        // The point of moving this window into the admin page: an installation that has said one
        // thing in its environment and another on the page keeps what an administrator saved. The
        // environment here asks for ten years, so a row thirty-one days old survives unless the
        // saved thirty is the one being read.
        var pastTheSavedWindow = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-31), readAt: null);
        var insideIt = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-29), readAt: null);

        await SaveRetentionAsync(30);

        await using (var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:RateLimitPerMinute"] = "500",
                ["Notifications:RetentionDays"] = "3650",
            }))
        {
            await using var scope = configured.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        var surviving = (await RowsAsync()).Select(r => r.Id).ToList();
        surviving.ShouldNotContain(pastTheSavedWindow, "the saved window is the one in force");
        surviving.ShouldContain(insideIt);
    }

    [Fact]
    public async Task A_saved_retention_window_of_nothing_is_refused_like_a_configured_one()
    {
        // The guard used to sit on the configuration read alone. Routing the window through the
        // stored section would have left that guard standing and stepped around it, so the same
        // zero written the other way has to be refused the same way.
        //
        // Three days old, against a deployment that asks for two: the row survives only if the
        // stored zero was read and refused in favour of the default. Ignore the stored section
        // and the configured two days deletes it; read the zero literally and everything goes.
        // Neither wrong answer can leave this row standing, which is what makes it evidence.
        var recent = await SeedAgedAsync(DateTimeOffset.UtcNow.AddDays(-3), readAt: null);

        await SaveRetentionAsync(0);

        await using (var configured = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Auth:RateLimitPerMinute"] = "500",
                ["Notifications:RetentionDays"] = "2",
            }))
        {
            await using var scope = configured.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .PruneAsync(CancellationToken.None);
        }

        (await RowsAsync()).Select(r => r.Id).ShouldContain(recent);
    }

    /// <summary>
    /// Writes the stored section as the admin page would, through the service, so the process's
    /// cached copy of it is dropped and the next read is the value just written.
    /// </summary>
    private async Task SaveRetentionAsync(int days)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAppSettingsService>().SaveAsync(
            AppSettingSections.Notifications, new NotificationSettings { RetentionDays = days });
    }

    [Fact]
    public async Task Every_producer_here_names_what_its_notification_is_about()
    {
        // What the exemption list is pinned against. A domain test classifies every template key
        // as "must name a target" or "excused, and here is why", but nothing in it reaches a
        // producer — so deleting the target arguments from a queue call left that test green and
        // wrote rows that can never be re-checked against the reader's access. This is the other
        // half: the producers are driven over HTTP and the columns they wrote are read back.
        //
        // The permission-grant producer is pinned elsewhere, by the inbox test that revokes a
        // grant and asserts the row degrades — which it cannot do without a target.
        var cavingGroupId = await CreateCavingGroupAsync("targets");
        var caverId = await RosterHelper.CaverIdForAsync(factory, recipientId);

        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members",
            new { caverId, role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members",
            new { caverId, role = "Admin" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.DeleteAsync($"/api/v1/caving-groups/{cavingGroupId}/members/{caverId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rows = await RowsAsync();
        foreach (var key in new[]
        {
            MessageTemplateCatalog.NotifyCavingGroupJoined,
            MessageTemplateCatalog.NotifyCavingGroupRoleChanged,
            MessageTemplateCatalog.NotifyCavingGroupRemoved,
        })
        {
            var row = rows.Where(r => r.TemplateKey == key).ToList().ShouldHaveSingleItem(key);
            row.TargetKind.ShouldBe(NotificationTargetKind.CavingGroup, key);
            row.TargetId.ShouldBe(cavingGroupId, key);
        }

        // And the sweep, so a producer added later is caught without anybody remembering to come
        // back here: nothing stored may be missing a target unless its message is excused by name.
        foreach (var row in rows.Where(r => !NotificationTargetPolicy.Exemptions.ContainsKey(r.TemplateKey)))
        {
            row.TargetKind.ShouldNotBeNull(row.TemplateKey);
            row.TargetId.ShouldNotBeNull(row.TemplateKey);
        }
    }

    [Fact]
    public async Task A_category_switched_off_while_a_send_is_backing_off_is_never_sent()
    {
        // The immediate path's version of the question the daily summary already asks on its way
        // out. A failed send backs off over hours, and the message that failed carried an opt-out
        // link of its own — so the recipient can answer "stop telling me about this" in the gap,
        // and the retry must not answer them with one more of exactly that.
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainWithTheirSendFailingAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().Status.ShouldBe(NotificationDeliveryStatus.Pending);

        await SetMailAsync("immediate", "cavingGroupMembership", "off");

        await MakeDueAsync();
        await DrainAsync();

        MineSent().ShouldBeEmpty("the retry must read the preference as it stands now");
        // Nothing left that has to leave, exactly as a refusal at routing would have left it.
        (await DeliveriesAsync()).ShouldBeEmpty();
        // And what happened is still readable, which is the whole reason dropping it is safe.
        (await RowsAsync()).ShouldHaveSingleItem().ReadAt.ShouldBeNull();
    }

    /// <summary>
    /// A delivery written by something that never thought about its creation instant is stamped by
    /// the database and not left at year one, where it would sit outside every retention window and
    /// outside the day's spending count. An instant a writer does set is kept exactly.
    /// </summary>
    [Fact]
    public async Task A_delivery_written_without_a_creation_instant_is_stamped_by_the_database()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var row = new Notification
        {
            RecipientUserId = recipientId,
            Category = NotificationCategory.CavingGroupMembership,
            TemplateKey = MessageTemplateCatalog.NotifyCavingGroupJoined,
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync();

        var unstamped = new NotificationDelivery
        {
            NotificationId = row.Id,
            RecipientUserId = recipientId,
            Channel = NotificationChannel.Email,
            Status = NotificationDeliveryStatus.Sent,
            NotBefore = DateTimeOffset.UtcNow.AddYears(1),
        };
        db.NotificationDeliveries.Add(unstamped);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var stored = await db.NotificationDeliveries.AsNoTracking().SingleAsync(d => d.Id == unstamped.Id);
        stored.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        stored.CreatedAt.ShouldBeLessThan(DateTimeOffset.UtcNow.AddMinutes(5));

        // The positive counterpart: an instant the writer chose is not overwritten by the default.
        var chosen = DateTimeOffset.UtcNow.AddDays(-40);
        var other = new Notification
        {
            RecipientUserId = recipientId,
            Category = NotificationCategory.CavingGroupMembership,
            TemplateKey = MessageTemplateCatalog.NotifyCavingGroupJoined,
        };
        db.Notifications.Add(other);
        await db.SaveChangesAsync();
        var stamped = new NotificationDelivery
        {
            NotificationId = other.Id,
            RecipientUserId = recipientId,
            Channel = NotificationChannel.Email,
            Status = NotificationDeliveryStatus.Sent,
            CreatedAt = chosen,
            NotBefore = DateTimeOffset.UtcNow.AddYears(1),
        };
        db.NotificationDeliveries.Add(stamped);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var kept = await db.NotificationDeliveries.AsNoTracking().SingleAsync(d => d.Id == stamped.Id);
        kept.CreatedAt.ShouldBe(chosen, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// One notification of a given age, with one settled delivery hanging off it. Written straight
    /// to the tables because no producer can queue a row that is already old, and because the
    /// window is keyed on the notification's own age rather than on anything a drain would set.
    /// </summary>
    private async Task<long> SeedAgedAsync(
        DateTimeOffset createdAt,
        DateTimeOffset? readAt,
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Sent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var row = new Notification
        {
            RecipientUserId = recipientId,
            Category = NotificationCategory.CavingGroupMembership,
            TemplateKey = MessageTemplateCatalog.NotifyCavingGroupJoined,
            CreatedAt = createdAt,
            ReadAt = readAt,
            RoutedAt = createdAt,
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync();

        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationId = row.Id,
            RecipientUserId = recipientId,
            Channel = NotificationChannel.Email,
            Status = status,
            CreatedAt = createdAt,
            SentAt = status == NotificationDeliveryStatus.Sent ? createdAt : null,

            // Far enough out that a drain running in this class cannot claim it and change what
            // the prune is being measured against.
            NotBefore = DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<Guid> CreateCavingGroupAsync(string? tag = null)
    {
        var created = await manager.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = tag is null ? $"Notif caving group {suffix}" : $"Notif caving group {suffix} {tag}",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddToCavingGroupAsync(string? tag = null)
    {
        var cavingGroupId = await CreateCavingGroupAsync(tag);
        var added = await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members",
            new { caverId = await RosterHelper.CaverIdForAsync(factory, recipientId), role = "Member" });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
    }

    /// <summary>One cell of the matrix as the settings endpoint reports it.</summary>
    private async Task<string> CellAsync(string category, string channel)
    {
        var prefs = JsonDocument.Parse(
            await (await recipient.GetAsync("/api/v1/me/notifications/")).Content.ReadAsStringAsync()).RootElement;

        return prefs.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == category)
            .GetProperty("channels").EnumerateArray()
            .Single(c => c.GetProperty("channel").GetString() == channel)
            .GetProperty("choice").GetString()!;
    }

    /// <summary>Every category anybody may mute — the ones a write may name a choice for.</summary>
    private static readonly string[] MutableCategories =
    [
        "cavingGroupMembership", "permissionGranted", "tripParticipation", "jobCompleted", "tripPlanning",
    ];

    /// <summary>
    /// Sets the mail cell of every category anybody may mute, and optionally one of them
    /// differently. The inbox cells are left alone deliberately: nothing in this class is about
    /// where a notification is read back, only about what leaves the system.
    /// </summary>
    private async Task SetMailAsync(string choice, string? category = null, string? categoryChoice = null) =>
        (await recipient.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            categories = MutableCategories.Select(name => new
            {
                category = name,
                channels = new[]
                {
                    new { channel = "email", choice = name == category ? categoryChoice! : choice },
                },
            }),
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>
    /// What was sent to this test's own recipient. Notifications are one table for the whole
    /// database and the drain is not scoped to a user, so a pass here also settles rows queued by
    /// whichever other test class is running — counting everything captured would be counting
    /// their mail too.
    /// </summary>
    /// <summary>
    /// One notification for the manager, marked so its message can be told from anything else
    /// the manager is sent — a batch-mate for the recipient's own delivery.
    /// </summary>
    private async Task QueueForTheManagerAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        NotificationQueue.Enqueue(
            db,
            managerId,
            NotificationCategory.CavingGroupMembership,
            MessageTemplateCatalog.NotifyPermissionGranted,
            new Dictionary<string, string>
            {
                ["actorName"] = "Someone",
                ["objectName"] = BatchMateMarker,
                ["url"] = "http://localhost/records/1",
            });
        await db.SaveChangesAsync();
    }

    private string BatchMateMarker => $"Batch-mate {suffix}";

    private List<SentMessage> BatchMateSent() =>
        [.. factory.Messages.Messages.Where(m =>
            string.Equals(m.Recipient, ManagerEmail, StringComparison.OrdinalIgnoreCase)
            && (m.Body.Contains(BatchMateMarker, StringComparison.Ordinal)
                || (m.Subject ?? string.Empty).Contains(BatchMateMarker, StringComparison.Ordinal)))];

    private List<SentMessage> MineSent() =>
        [.. factory.Messages.Messages.Where(m =>
            string.Equals(m.Recipient, RecipientEmail, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Queues a notification that carries a link, without going through a producer: the two
    /// producers that write one belong to other slices, and what is under test is what the sender
    /// does with the value, not who wrote it.
    /// </summary>
    private async Task QueueLinkedNotificationAsync(string url)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        NotificationQueue.Enqueue(
            db,
            recipientId,
            NotificationCategory.PermissionGranted,
            MessageTemplateCatalog.NotifyPermissionGranted,
            new Dictionary<string, string>
            {
                ["actorName"] = "Someone",
                ["objectName"] = $"Linked record {suffix}",
                ["url"] = url,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The opt-out link out of the last message sent to this test's recipient. The body carries
    /// two links — where to go and how to stop; pick the second by name rather than by position.
    /// </summary>
    private string OptOutTokenInLastMessage()
    {
        var line = factory.Messages.LastTo(RecipientEmail).Body
            .Split((char[])['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.Contains("/unsubscribe?token=", StringComparison.Ordinal));
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(line).Query)["token"];
        token.ShouldNotBeNullOrWhiteSpace();
        return token!;
    }

    /// <summary>Routes what is queued without sending any of it, so a claim can be tested alone.</summary>
    private async Task DrainRoutingAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
        while (await service.RouteAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>
    /// Leases whatever is due and then does nothing with it — which is precisely what a process
    /// that dies mid-send leaves behind, and the only honest way to write that here: the lease is
    /// taken by the claim statement itself and committed before any send is attempted.
    /// </summary>
    private async Task<List<long>> ClaimDueAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await NotificationDeliverySql.ClaimDueAsync(db, clock.GetUtcNow(), 25, CancellationToken.None);
    }

    /// <summary>
    /// One drain during which every send to this test's recipient fails, so the delivery is left
    /// backing off with exactly one attempt on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aimed at this test's own recipient rather than at "the next send": the drain settles whatever
    /// is due in the shared database, so a one-shot failure could land on another class's message
    /// and leave this one delivered on the first pass.
    /// </para>
    /// <para>
    /// The drain loops until nothing moves, and still stops at one attempt whatever this class's
    /// clock says, because the retry is stamped and then judged due on the same clock. It used to
    /// be stamped on this clock and judged on the database's: once this class's setup outran the
    /// first step of the ladder on a loaded machine, the retry was already in the database's past,
    /// the next pass spent the second attempt, and a test asserting one attempt saw three.
    /// </para>
    /// </remarks>
    private async Task DrainWithTheirSendFailingAsync()
    {
        try
        {
            factory.Messages.FailSendsTo = RecipientEmail;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }
    }

    /// <summary>
    /// One drain that stops when it reaches this test's recipient's send — the host shutting down
    /// in the middle of a pass. The cancellation leaves the drain, as it does the worker's loop.
    /// </summary>
    private async Task PassCancelledAtTheirSendAsync()
    {
        using var pass = new CancellationTokenSource();
        try
        {
            factory.Messages.CancelPassAt = (RecipientEmail, pass);
            await using var scope = factory.Services.CreateAsyncScope();
            await Should.ThrowAsync<OperationCanceledException>(() => scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .RunOnceAsync(pass.Token));
        }
        finally
        {
            factory.Messages.CancelPassAt = null;
        }
    }

    /// <summary>The mail channel, except that it throws while deciding for one address.</summary>
    private sealed class ThrowsDecidingFor(INotificationChannel inner, string address) : INotificationChannel
    {
        public NotificationChannel Channel => inner.Channel;

        public NotificationChannelKind Kind => inner.Kind;

        public bool Carries(MessageTemplateDefinition template) => inner.Carries(template);

        public ValueTask<bool> IsUsableAsync(CancellationToken ct) => inner.IsUsableAsync(ct);

        public bool CanReach(SilexGisUser recipient) => inner.CanReach(recipient);

        public NotificationRoute Decide(SilexGisUser recipient, NotificationChannelChoice choice) =>
            string.Equals(recipient.Email, address, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("Simulated routing fault.")
                : inner.Decide(recipient, choice);

        public Task<MessageResult> SendAsync(
            SilexGisUser recipient, string templateKey, IReadOnlyDictionary<string, string> values, CancellationToken ct) =>
            inner.SendAsync(recipient, templateKey, values, ct);
    }

    private async Task<int> DrainAsync()
    {
        var settled = 0;
        int pass;
        do
        {
            await using var scope = factory.Services.CreateAsyncScope();
            pass = await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .RunOnceAsync(CancellationToken.None);
            settled += pass;
        }
        while (pass > 0);

        return settled;
    }

    private async Task RunDigestAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<NotificationDeliveryService>()
            .RunDigestAsync(CancellationToken.None);
    }

    /// <summary>
    /// Brings a delivery's due time to just before this class's clock, which is what a claim
    /// judges it by — rather than moving the clock, which would also move every stamp after it.
    /// </summary>
    private Task MakeDigestDueAsync() => BackdateAsync(NotificationDeliveryStatus.Deferred);

    private Task MakeDueAsync(Guid? userId = null) => BackdateAsync(NotificationDeliveryStatus.Pending, userId);

    private async Task BackdateAsync(NotificationDeliveryStatus status, Guid? userId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var whose = userId ?? recipientId;
        await db.NotificationDeliveries
            .Where(d => d.RecipientUserId == whose && d.Status == status)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NotBefore, clock.Now.AddMinutes(-1)));
    }

    /// <summary>This test's recipient's own inbox — what happened to them, whatever was sent.</summary>
    private async Task<List<Notification>> RowsAsync(Guid? userId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(r => r.RecipientUserId == (userId ?? recipientId))
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    /// <summary>What is actually leaving, or has left, the system for them.</summary>
    private async Task<List<NotificationDelivery>> DeliveriesAsync(Guid? userId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var whose = userId ?? recipientId;
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.RecipientUserId == whose)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    [Theory]
    [InlineData(550, "5.1.1 <a@b.test>: Recipient address rejected: User unknown", true)]
    [InlineData(553, "5.1.3 Bad recipient address syntax", true)]
    [InlineData(550, "Mailbox unavailable", true)]
    [InlineData(550, "5.7.1 Relaying denied", false)]
    [InlineData(554, "5.7.1 Service unavailable; client host blocked", false)]
    [InlineData(550, "Relay access denied", false)]
    [InlineData(552, "5.2.2 Mailbox full", false)]
    [InlineData(450, "4.2.0 Greylisted", false)]
    public void Only_a_refusal_that_is_about_the_address_settles_a_delivery_for_good(
        int status, string text, bool permanent) =>
        SilexGis.Infrastructure.Email.SmtpEmailSender.IsAddressRefusal(status, text).ShouldBe(permanent);

    public async ValueTask DisposeAsync()
    {
        // The table is global like app_settings, so a class cleans up after itself. Deliveries go
        // with their notifications: the cascade is on the constraint, not on the change tracker,
        // so a set-based delete takes them too.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications
            .Where(r => r.RecipientUserId == recipientId || r.RecipientUserId == managerId)
            .ExecuteDeleteAsync();

        // A retention window saved by one test would otherwise decide how long another class's
        // notifications are kept, against the one database they all share.
        await db.AppSettings.Where(a => a.Key == AppSettingSections.Notifications).ExecuteDeleteAsync();
        Dispose();
    }

    public void Dispose()
    {
        manager.Dispose();
        recipient.Dispose();
        factory.Dispose();
    }
}
