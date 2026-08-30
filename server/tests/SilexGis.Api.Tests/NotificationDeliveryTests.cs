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
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
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
[Collection(PostgresCollection.Name)]
public sealed class NotificationDeliveryTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient manager = null!;   // creates caving groups, does the granting
    private HttpClient recipient = null!;
    private Guid managerId;
    private Guid recipientId;

    /// <summary>
    /// A clock the test sets, so what the application stamps can be asserted as a value rather
    /// than as a range. It cannot make a delivery due: that is decided by the database's own
    /// now(), which is why rows are still backdated below.
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

    public async Task InitializeAsync()
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
        // would be claimed first — taking the one-shot delivery failure this class injects, and
        // making its counts depend on whatever ran before it.
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
        message.Subject.ShouldBe($"Ați fost adăugat în Notif caving group {suffix} ro");
        message.Body.ShouldContain("v-a adăugat în grupul");

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

        (await RowsAsync()).ShouldContain(r => r.Category == NotificationCategory.SecurityAlerts);
        (await DeliveriesAsync()).ShouldContain(d => d.Status == NotificationDeliveryStatus.Sent);
        factory.Messages.LastTo(RecipientEmail).Body.ShouldNotBeNullOrWhiteSpace();
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

        // Whether a delivery is due is the database's own now(), which no clock here can move,
        // so the window is brought to us.
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

        try
        {
            factory.Messages.FailNextSend = true;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailNextSend = false;
        }

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
        leased.Attempts.ShouldBe(1);
        leased.NotBefore.ShouldBeGreaterThan(DateTimeOffset.UtcNow, "the lease hides it while a send is in flight");

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

        try
        {
            factory.Messages.FailNextSend = true;
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailNextSend = false;
        }

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
        return await NotificationDeliverySql.ClaimDueAsync(db, 25, CancellationToken.None);
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
    /// The only way to make a delivery due. Whether one is claimable is decided by the database's
    /// own now(), so the clock this class injects cannot reach it — backdating the row can.
    /// </summary>
    private Task MakeDigestDueAsync() => BackdateAsync(NotificationDeliveryStatus.Deferred);

    private Task MakeDueAsync() => BackdateAsync(NotificationDeliveryStatus.Pending);

    private async Task BackdateAsync(NotificationDeliveryStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationDeliveries
            .Where(d => d.RecipientUserId == recipientId && d.Status == status)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NotBefore, DateTimeOffset.UtcNow.AddMinutes(-1)));
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
    private async Task<List<NotificationDelivery>> DeliveriesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.RecipientUserId == recipientId)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    public async Task DisposeAsync()
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
    }

    public void Dispose()
    {
        manager.Dispose();
        recipient.Dispose();
        factory.Dispose();
    }
}
