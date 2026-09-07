// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The operator's view of whether messages are getting out: counts by channel and status, how long
/// the oldest unsent delivery has been waiting, the rows worth looking at — without ever naming a
/// recipient by their address — and putting one of them back by hand.
/// </summary>
/// <remarks>
/// Deliveries are seeded straight into the table rather than produced by draining something. What
/// is under test is the reading, and a seeded row is the only way to hold a delivery in a chosen
/// status at a chosen age; the paths that put a row into each status are covered where they are
/// written.
/// </remarks>
public sealed class NotificationHealthTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string HealthUrl = "/api/v1/admin/notifications/health";
    private const string DeliveriesUrl = "/api/v1/admin/notifications/deliveries";

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);

    private HttpClient operatorClient = null!;
    private HttpClient editorClient = null!;
    private Guid operatorId;
    private Guid recipientId;
    private Guid editorId;

    public NotificationHealthTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Auth:RateLimitPerMinute"] = "500",
                // A night to be kept out of, so the instant a hand-driven retry writes can be
                // asserted against it rather than against a feature that is switched off.
                ["Notifications:QuietHoursFrom"] = "22:00",
                ["Notifications:QuietHoursTo"] = "07:00",
            },
            services => services.AddSingleton<TimeProvider>(clock));

    private string OperatorEmail => $"health-adm-{suffix}@t.local";

    private string EditorEmail => $"health-edt-{suffix}@t.local";

    private string RecipientEmail => $"health-rcp-{suffix}@t.local";

    public async Task InitializeAsync()
    {
        operatorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, OperatorEmail);
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, EditorEmail);
        recipientId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, RecipientEmail);

        operatorClient = await AuthHelper.BearerClientAsync(factory, OperatorEmail);
        editorClient = await AuthHelper.BearerClientAsync(factory, EditorEmail);

        // The notification table is one table for the whole database and earlier classes leave
        // rows in it. This view is unscoped by construction — it is everybody's deliveries — so
        // its counts would otherwise depend on whatever ran before it.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task The_counts_say_how_many_deliveries_sit_in_each_status()
    {
        await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddMinutes(-5));
        await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddMinutes(-4));
        await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));
        await SeedAsync(NotificationDeliveryStatus.Sent, clock.Now.AddHours(-3));

        var health = await GetJsonAsync(operatorClient, HealthUrl);
        var counts = health.GetProperty("counts").EnumerateArray().ToList();

        CountOf(counts, "email", "pending").ShouldBe(2);
        CountOf(counts, "email", "dead").ShouldBe(1);
        CountOf(counts, "email", "sent").ShouldBe(1);
        // A pair with nothing in it is absent rather than reported as zero.
        counts.ShouldNotContain(c => c.GetProperty("status").GetString() == "deferred");
    }

    [Fact]
    public async Task The_oldest_pending_delivery_reports_how_long_it_has_been_waiting()
    {
        // The number that says the mail server has stopped answering. A pending count alone is
        // healthy at any size as long as it keeps moving.
        var stuckSince = clock.Now.AddHours(-3);
        await SeedAsync(NotificationDeliveryStatus.Pending, stuckSince);
        await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddMinutes(-1));
        // Neither of these is waiting, however old it is.
        await SeedAsync(NotificationDeliveryStatus.Sent, clock.Now.AddDays(-9));
        await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddDays(-8));

        var health = await GetJsonAsync(operatorClient, HealthUrl);

        health.GetProperty("oldestPendingCreatedAt").GetDateTimeOffset()
            .ShouldBe(stuckSince, TimeSpan.FromSeconds(1));
        health.GetProperty("oldestPendingAgeSeconds").GetInt64().ShouldBe(3 * 3600);
    }

    [Fact]
    public async Task Nothing_waiting_reports_no_age_rather_than_zero()
    {
        await SeedAsync(NotificationDeliveryStatus.Sent, clock.Now.AddHours(-1));

        var health = await GetJsonAsync(operatorClient, HealthUrl);

        // Zero would read as "something has been waiting no time at all", which is a different
        // and much more reassuring statement than "nothing is waiting".
        health.GetProperty("oldestPendingCreatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        health.GetProperty("oldestPendingAgeSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_dead_delivery_names_its_recipient_by_label_and_never_by_address()
    {
        // A mail server routinely quotes the address it refuses, so the failure text is the second
        // way an address could walk out of here — and the account has no display name, which is
        // exactly the case a naive "display name or user name" fallback publishes.
        await SeedAsync(
            NotificationDeliveryStatus.Dead,
            clock.Now.AddHours(-1),
            error: $"550 5.1.1 <{RecipientEmail}>: Recipient address rejected");

        var response = await operatorClient.GetAsync(DeliveriesUrl);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(RecipientEmail);
        body.ShouldNotContain("@");

        var row = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("recipientLabel").GetString().ShouldBe($"user-{recipientId:N}"[..13]);
        row.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("550");
        row.GetProperty("status").GetString().ShouldBe("dead");
        row.GetProperty("templateKey").GetString().ShouldBe(MessageTemplateCatalog.NotifyCavingGroupJoined);
    }

    [Fact]
    public async Task The_list_leaves_out_what_is_still_moving_normally()
    {
        var fresh = await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddMinutes(-2));
        var stuck = await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddHours(-48));
        var dead = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddMinutes(-2));
        var sent = await SeedAsync(NotificationDeliveryStatus.Sent, clock.Now.AddDays(-40));

        var attention = await ListedIdsAsync(DeliveriesUrl);
        // Dead however recent; unsent once it is older than the overdue window; never a message
        // that left, however long ago.
        attention.ShouldBe([dead, stuck], ignoreOrder: true);

        // The same window, said explicitly: an hour makes the two-minute row overdue too.
        (await ListedIdsAsync($"{DeliveriesUrl}?overdueHours=0")).ShouldContain(fresh);

        // And the whole table when the operator asks for it.
        (await ListedIdsAsync($"{DeliveriesUrl}?attentionOnly=false"))
            .ShouldBe([fresh, stuck, dead, sent], ignoreOrder: true);

        (await ListedIdsAsync($"{DeliveriesUrl}?attentionOnly=false&status=sent")).ShouldBe([sent]);
    }

    [Fact]
    public async Task An_unrecognised_status_is_a_refusal_a_caller_can_read()
    {
        var response = await operatorClient.GetAsync($"{DeliveriesUrl}?status=exploded");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("notification.delivery_status_unknown");

        var window = await operatorClient.GetAsync($"{DeliveriesUrl}?overdueHours=-1");
        window.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await window.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("notification.overdue_window_invalid");
    }

    [Fact]
    public async Task A_caller_with_no_token_is_refused_before_anything_is_counted()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(HealthUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(DeliveriesUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_a_holder_of_the_settings_domain_may_look()
    {
        await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-1));

        // An editor holds every content domain at the widest scope and still cannot open this: the
        // right to read everybody's deliveries is the right to run the installation's mail server,
        // and the seeded administrators group is deliberately not given it either.
        (await editorClient.GetAsync(HealthUrl)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editorClient.GetAsync(DeliveriesUrl)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await GrantSettingsAsync(editorId, AccessAction.Read);

        // The same caller, the same routes, one entry apart — so what the refusal turned on is the
        // Settings domain and not the account's rank.
        (await editorClient.GetAsync(HealthUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await GetJsonAsync(editorClient, DeliveriesUrl);
        listed.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_delivery_dead_for_something_the_recipient_has_since_switched_off_is_dropped()
    {
        // The same delivery, the same operator, the same request — one stored preference apart. So
        // what decides the second answer is the recipient's own choice and nothing else.
        var wanted = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));
        var queued = await RetryAsync(wanted);
        queued.GetProperty("outcome").GetString().ShouldBe("queued");

        await SwitchOffEmailAsync(NotificationCategory.CavingGroupMembership);
        var unwanted = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));

        var answer = await RetryAsync(unwanted);

        // Told what happened, rather than told it worked. A message that has been asked not to be
        // sent is not a message that was sent, and an operator reading "queued" would go on to
        // wonder why it never arrived.
        answer.GetProperty("outcome").GetString().ShouldBe("suppressed");
        answer.GetProperty("dueAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // The delivery is gone rather than waiting: routing writes no row for a channel somebody
        // has switched off, so a retry that has just asked the same question again must not leave
        // one behind. The notification itself stays in the inbox, which is where somebody who
        // switched email off reads it.
        (await StatusOfAsync(unwanted)).ShouldBeNull();
        (await StatusOfAsync(wanted)).ShouldBe(NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public async Task A_delivery_for_someone_who_now_wants_a_daily_summary_goes_back_to_waiting_for_one()
    {
        // Nine in the morning, so nothing here turns on quiet hours: the digest window is the only
        // thing that can move the instant, and it is the thing under test.
        clock.Now = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);

        // Immediate first, as the control. The same delivery, the same operator, the same
        // request — one stored choice apart, so what decides the second answer is the choice.
        var immediate = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));
        (await RetryAsync(immediate)).GetProperty("outcome").GetString().ShouldBe("queued");
        (await StatusOfAsync(immediate)).ShouldBe(NotificationDeliveryStatus.Pending);

        await StoreEmailChoiceAsync(
            NotificationCategory.CavingGroupMembership, NotificationChannelChoice.Daily);
        var held = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));

        var answer = await RetryAsync(held);

        // Somebody who asked for one message a day has asked for one message a day. A backlog of
        // dead rows put back as immediate mail would answer that with one message per row, and the
        // hand-driven path would be the only writer in the system that can do it.
        answer.GetProperty("outcome").GetString().ShouldBe("queued");
        (await StatusOfAsync(held)).ShouldBe(NotificationDeliveryStatus.Deferred);
        (await NotBeforeOfAsync(held)).ShouldBe(
            new DateTimeOffset(2026, 3, 12, 7, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1));
        answer.GetProperty("dueAt").GetDateTimeOffset()
            .ShouldBe(new DateTimeOffset(2026, 3, 12, 7, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_delivery_dead_because_nothing_here_sends_that_kind_of_message_is_refused()
    {
        // The other reason routing kills a row the moment it writes it: the wording exists, but it
        // was written for a transport this installation does not send on. The send path never asks
        // that question again, so a retry that did not ask it would hand the message to the very
        // channel that refused to carry it.
        var id = await SeedAsync(
            NotificationDeliveryStatus.Dead,
            clock.Now.AddHours(-2),
            templateKey: MessageTemplateCatalog.SmsTwoFactorCode);

        var response = await operatorClient.PostAsync(RetryUrl(id), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("notification.delivery_template_unknown");
        (await StatusOfAsync(id)).ShouldBe(NotificationDeliveryStatus.Dead);
    }

    [Fact]
    public async Task Putting_a_delivery_back_records_who_asked_for_it()
    {
        var id = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));
        var notificationId = await NotificationOfAsync(id);

        (await RetryAsync(id)).GetProperty("outcome").GetString().ShouldBe("queued");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.Action == AuditActions.NotificationRetried
                && a.EntityId == id.ToString(CultureInfo.InvariantCulture))
            .SingleAsync();

        // Who, and what — a trail that only said something had been retried would not answer
        // either question anybody asks of it afterwards.
        entry.UserId.ShouldBe(operatorId);
        entry.EntityType.ShouldBe(nameof(NotificationDelivery));
        entry.RootEntityType.ShouldBe(nameof(Notification));
        entry.RootEntityId.ShouldBe(notificationId.ToString(CultureInfo.InvariantCulture));
        var changes = JsonDocument.Parse(entry.Changes.ShouldNotBeNull()).RootElement;
        changes.GetProperty("Status").GetProperty("old").GetString().ShouldBe("dead");
        changes.GetProperty("Status").GetProperty("new").GetString().ShouldBe("queued");
        changes.GetProperty("TemplateKey").GetProperty("new").GetString()
            .ShouldBe(MessageTemplateCatalog.NotifyCavingGroupJoined);
    }

    [Fact]
    public async Task A_dropped_delivery_is_recorded_as_plainly_as_a_queued_one()
    {
        // The act removed a row that was somebody's message. Recording only the happy outcome
        // would leave the one case where a delivery disappears under an operator's hand untraced.
        await SwitchOffEmailAsync(NotificationCategory.CavingGroupMembership);
        var id = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));

        (await RetryAsync(id)).GetProperty("outcome").GetString().ShouldBe("suppressed");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.Action == AuditActions.NotificationRetried
                && a.EntityId == id.ToString(CultureInfo.InvariantCulture))
            .SingleAsync();

        entry.UserId.ShouldBe(operatorId);
        JsonDocument.Parse(entry.Changes.ShouldNotBeNull()).RootElement
            .GetProperty("Status").GetProperty("new").GetString().ShouldBe("suppressed");
    }

    [Fact]
    public async Task A_delivery_dead_because_nothing_can_write_its_message_is_refused()
    {
        // It would die again on the same line. An operator who is told that can go and restore the
        // wording; one whose retry appeared to work learns nothing at all.
        var id = await SeedAsync(
            NotificationDeliveryStatus.Dead,
            clock.Now.AddHours(-2),
            templateKey: "notify.no-such-template");

        var response = await operatorClient.PostAsync(RetryUrl(id), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("notification.delivery_template_unknown");
        (await StatusOfAsync(id)).ShouldBe(NotificationDeliveryStatus.Dead);
    }

    [Fact]
    public async Task A_delivery_put_back_is_not_due_during_the_recipients_night()
    {
        // Half past eleven at night where the recipient is. The hand-driven path is the third
        // writer of the instant a delivery becomes due, and an operator working through a backlog
        // in the evening must not be the one path that wakes people up.
        clock.Now = new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero);
        var id = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));

        var answer = await RetryAsync(id);

        answer.GetProperty("outcome").GetString().ShouldBe("queued");
        answer.GetProperty("dueAt").GetDateTimeOffset()
            .ShouldBe(new DateTimeOffset(2026, 3, 11, 7, 0, 0, TimeSpan.Zero));

        // And the same instant on the row itself, not only in the answer.
        (await NotBeforeOfAsync(id)).ShouldBe(
            new DateTimeOffset(2026, 3, 11, 7, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1));

        // Outside the window it is due at once, so what moved the first one was the hour and not
        // an unconditional delay.
        clock.Now = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var daytime = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));
        (await RetryAsync(daytime)).GetProperty("dueAt").GetDateTimeOffset()
            .ShouldBe(clock.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_delivery_put_back_starts_its_attempts_again_and_drops_the_old_failure()
    {
        var id = await SeedAsync(
            NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2), error: "550 mailbox unavailable");

        (await RetryAsync(id)).GetProperty("outcome").GetString().ShouldBe("queued");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.NotificationDeliveries.AsNoTracking().SingleAsync(d => d.Id == id);

        // The count was the pipeline's budget for this message and it was spent. Leaving it spent
        // would let the first failure kill the row again immediately, with no back-off at all.
        row.Attempts.ShouldBe(0);
        row.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        row.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Only_a_delivery_that_has_given_up_may_be_put_back()
    {
        var waiting = await SeedAsync(NotificationDeliveryStatus.Pending, clock.Now.AddHours(-2));

        var response = await operatorClient.PostAsync(RetryUrl(waiting), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("notification.delivery_not_dead");

        var missing = await operatorClient.PostAsync(RetryUrl(waiting + 100_000), content: null);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_the_backlog_is_not_permission_to_send_from_it()
    {
        var id = await SeedAsync(NotificationDeliveryStatus.Dead, clock.Now.AddHours(-2));

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync(RetryUrl(id), content: null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        (await editorClient.PostAsync(RetryUrl(id), content: null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // The right that opens the page is not the right that acts on it: diagnosing a backlog and
        // sending somebody else's message out of it are different acts, and an installation may
        // well want an operator who can do the first without the second.
        await GrantSettingsAsync(editorId, AccessAction.Read);
        (await editorClient.GetAsync(DeliveriesUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editorClient.PostAsync(RetryUrl(id), content: null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        await GrantSettingsAsync(editorId, AccessAction.Execute);
        var allowed = await editorClient.PostAsync(RetryUrl(id), content: null);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    private static string RetryUrl(long id) => $"{DeliveriesUrl}/{id}/retry";

    private async Task<JsonElement> RetryAsync(long id)
    {
        var response = await operatorClient.PostAsync(RetryUrl(id), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task SwitchOffEmailAsync(NotificationCategory category) =>
        StoreEmailChoiceAsync(category, NotificationChannelChoice.Off);

    private async Task StoreEmailChoiceAsync(
        NotificationCategory category, NotificationChannelChoice choice)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            UserId = recipientId,
            Category = category,
            Channel = NotificationChannelKind.Email,
            Choice = choice,
        });
        await db.SaveChangesAsync();
    }

    private async Task<NotificationDeliveryStatus?> StatusOfAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.Id == id).Select(d => (NotificationDeliveryStatus?)d.Status)
            .FirstOrDefaultAsync();
    }

    private async Task<DateTimeOffset> NotBeforeOfAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.Id == id).Select(d => d.NotBefore).SingleAsync();
    }

    private async Task<long> NotificationOfAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.Id == id).Select(d => d.NotificationId).SingleAsync();
    }

    /// <summary>
    /// Writes the one entry shape the Settings domain accepts: it carries no per-object identity,
    /// so a grant over it is global or it is nothing.
    /// </summary>
    private async Task GrantSettingsAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Settings,
            Actions = actions,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private async Task<long> SeedAsync(
        NotificationDeliveryStatus status,
        DateTimeOffset createdAt,
        string? error = null,
        string? templateKey = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var notification = new Notification
        {
            RecipientUserId = recipientId,
            Category = NotificationCategory.CavingGroupMembership,
            TemplateKey = templateKey ?? MessageTemplateCatalog.NotifyCavingGroupJoined,
            CreatedAt = createdAt,
            RoutedAt = createdAt,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            RecipientUserId = recipientId,
            Channel = NotificationChannel.Email,
            Status = status,
            Attempts = status is NotificationDeliveryStatus.Dead ? NotificationRouting.MaxAttempts : 0,
            // Far enough out that nothing can claim it while the assertions run.
            NotBefore = createdAt.AddYears(1),
            Error = error,
            CreatedAt = createdAt,
            SentAt = status is NotificationDeliveryStatus.Sent ? createdAt : null,
        };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<long>> ListedIdsAsync(string url)
    {
        var page = await GetJsonAsync(operatorClient, url);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64())];
    }

    private static int CountOf(IEnumerable<JsonElement> counts, string channel, string status) =>
        counts.Where(c => c.GetProperty("channel").GetString() == channel
                && c.GetProperty("status").GetString() == status)
            .Select(c => c.GetProperty("count").GetInt32())
            .SingleOrDefault();

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications.ExecuteDeleteAsync();
        await db.UserNotificationPreferences.Where(p => p.UserId == recipientId).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        operatorClient?.Dispose();
        editorClient?.Dispose();
        factory.Dispose();
    }
}
