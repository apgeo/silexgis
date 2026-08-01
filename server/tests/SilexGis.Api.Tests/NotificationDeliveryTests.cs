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
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Notification delivery end to end: a real action over HTTP queues a row, nothing is sent inside
/// the request, and draining the outbox produces the message the recipient would receive — or
/// does not, when they asked not to hear about it.
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

    public NotificationDeliveryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = "500",
        });

    public async Task InitializeAsync()
    {
        managerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, ManagerEmail);
        recipientId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, RecipientEmail);

        manager = await AuthHelper.BearerClientAsync(factory, ManagerEmail);
        recipient = await AuthHelper.BearerClientAsync(factory, RecipientEmail);

        // The outbox is one table for the whole database, and earlier test classes queue rows of
        // their own that nothing drains. A drain here is not scoped to a user, so those leftovers
        // would be claimed first — taking the one-shot delivery failure this class injects, and
        // making its counts depend on whatever ran before it.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationOutbox.ExecuteDeleteAsync();
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
        queued[0].Status.ShouldBe(NotificationOutboxStatus.Pending);
        queued[0].Category.ShouldBe(NotificationCategory.CavingGroupMembership);

        (await DrainAsync()).ShouldBe(1);

        var message = factory.Messages.LastTo(RecipientEmail);
        message.Channel.ShouldBe("email");
        message.Body.ShouldContain($"Notif caving group {suffix}");
        (await RowsAsync())[0].Status.ShouldBe(NotificationOutboxStatus.Sent);
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
            new { userId = recipientId, role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterJoining = (await RowsAsync()).Count;

        // Leaving of your own accord: you already know, and mailing you about it is noise.
        (await recipient.DeleteAsync($"/api/v1/caving-groups/{cavingGroupId}/members/{recipientId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RowsAsync()).Count.ShouldBe(afterJoining);
    }

    [Fact]
    public async Task A_switched_off_category_is_suppressed_rather_than_sent()
    {
        await SetPreferencesAsync(emailEnabled: true, digest: "immediate", off: "cavingGroupMembership");
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();

        (await RowsAsync())[0].Status.ShouldBe(NotificationOutboxStatus.Suppressed);
        MineSent().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_master_switch_suppresses_everything_ordinary()
    {
        await SetPreferencesAsync(emailEnabled: false, digest: "immediate");
        await AddToCavingGroupAsync();
        factory.Messages.Clear();

        await DrainAsync();

        (await RowsAsync())[0].Status.ShouldBe(NotificationOutboxStatus.Suppressed);
        MineSent().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_security_alert_is_sent_even_with_the_master_switch_off()
    {
        await SetPreferencesAsync(emailEnabled: false, digest: "daily");
        factory.Messages.Clear();

        // Changing the password is the alert: if it was not them, someone else knows it.
        (await recipient.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = AuthHelper.Password,
            newPassword = "a-new-long-password-1",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await DrainAsync();

        var rows = await RowsAsync();
        rows.ShouldContain(r => r.Category == NotificationCategory.SecurityAlerts
            && r.Status == NotificationOutboxStatus.Sent);
        factory.Messages.LastTo(RecipientEmail).Body.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_daily_digest_holds_events_back_and_then_sends_one_message_for_all_of_them()
    {
        await SetPreferencesAsync(emailEnabled: true, digest: "daily");
        await AddToCavingGroupAsync();
        await AddToCavingGroupAsync("second");
        factory.Messages.Clear();

        // The immediate pass routes them into the digest and sends nothing.
        await DrainAsync();
        var deferred = await RowsAsync();
        deferred.Count.ShouldBe(2);
        deferred.ShouldAllBe(r => r.Status == NotificationOutboxStatus.Deferred);
        MineSent().ShouldBeEmpty();

        // There is no clock to move, so the window is brought to us.
        await MakeDigestDueAsync();
        await RunDigestAsync();

        // One message, both events in it — and the same rows, so nothing can arrive twice.
        MineSent().Count.ShouldBe(1);
        var digest = factory.Messages.LastTo(RecipientEmail);
        digest.Body.ShouldContain($"Notif caving group {suffix}");
        digest.Body.ShouldContain($"Notif caving group {suffix} second");
        (await RowsAsync()).ShouldAllBe(r => r.Status == NotificationOutboxStatus.Sent);
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

        var afterFailure = (await RowsAsync())[0];
        afterFailure.Status.ShouldBe(NotificationOutboxStatus.Pending);
        afterFailure.Attempts.ShouldBe(1);
        afterFailure.Error.ShouldNotBeNullOrWhiteSpace();
        afterFailure.NotBefore.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // Due again only once the backoff has passed; bring it forward rather than wait.
        await MakeDueAsync();
        await DrainAsync();

        MineSent().Count.ShouldBe(1);
        (await RowsAsync())[0].Status.ShouldBe(NotificationOutboxStatus.Sent);
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

        var rows = await RowsAsync();
        rows.ShouldContain(r => r.TemplateKey == "notify.does-not-exist"
            && r.Status == NotificationOutboxStatus.Dead
            && r.Error != null);
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
        (await RowsAsync())[0].Status.ShouldBe(NotificationOutboxStatus.Sent);
    }

    [Fact]
    public async Task The_opt_out_link_in_a_message_switches_that_category_off_without_signing_in()
    {
        await AddToCavingGroupAsync();
        factory.Messages.Clear();
        await DrainAsync();

        // The body carries two links — where to go and how to stop; pick the second by name
        // rather than by position.
        var unsubscribeUrl = factory.Messages.LastTo(RecipientEmail).Body
            .Split((char[])['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.Contains("/unsubscribe?token=", StringComparison.Ordinal));
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(unsubscribeUrl).Query)["token"];
        token.ShouldNotBeNullOrWhiteSpace();

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/notifications/unsubscribe", new { token });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The setting the page shows is the one that changed.
        var prefs = JsonDocument.Parse(
            await (await recipient.GetAsync("/api/v1/me/notifications/")).Content.ReadAsStringAsync()).RootElement;
        prefs.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == "cavingGroupMembership")
            .GetProperty("enabled").GetBoolean().ShouldBeFalse();
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
            new { userId = recipientId, role = "Member" });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
    }

    private async Task SetPreferencesAsync(bool emailEnabled, string digest, string? off = null) =>
        (await recipient.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            emailEnabled,
            digest,
            categories = off is null
                ? Array.Empty<object>()
                : [new { category = off, enabled = false }],
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>
    /// What was sent to this test's own recipient. The outbox is one table for the whole
    /// database and the drain is not scoped to a user, so a pass here also settles rows queued by
    /// whichever other test class is running — counting everything captured would be counting
    /// their mail too.
    /// </summary>
    private List<SentMessage> MineSent() =>
        [.. factory.Messages.Messages.Where(m =>
            string.Equals(m.Recipient, RecipientEmail, StringComparison.OrdinalIgnoreCase))];

    private async Task<int> DrainAsync()
    {
        var settled = 0;
        int pass;
        do
        {
            await using var scope = factory.Services.CreateAsyncScope();
            pass = await scope.ServiceProvider
                .GetRequiredService<NotificationOutboxService>()
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
            .GetRequiredService<NotificationOutboxService>()
            .RunDigestAsync(CancellationToken.None);
    }

    /// <summary>The only way to move time: there is no clock abstraction anywhere in the solution.</summary>
    private Task MakeDigestDueAsync() => BackdateAsync(NotificationOutboxStatus.Deferred);

    private Task MakeDueAsync() => BackdateAsync(NotificationOutboxStatus.Pending);

    private async Task BackdateAsync(NotificationOutboxStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationOutbox
            .Where(r => r.UserId == recipientId && r.Status == status)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.NotBefore, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private async Task<List<NotificationOutboxEntry>> RowsAsync(Guid? userId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationOutbox.AsNoTracking()
            .Where(r => r.UserId == (userId ?? recipientId))
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task DisposeAsync()
    {
        // The table is global like app_settings, so a class cleans up after itself.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationOutbox
            .Where(r => r.UserId == recipientId || r.UserId == managerId)
            .ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        manager.Dispose();
        recipient.Dispose();
        factory.Dispose();
    }
}
