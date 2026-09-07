// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The hours nobody is interrupted in, end to end: an installation names a window, a recipient
/// names the zone they live in, and what leaves the system becomes due after their night rather
/// than in the middle of it — while the notification itself is readable the moment it happens.
/// </summary>
/// <remarks>
/// <para>
/// A class of its own because the window is installation configuration, and a class that set it
/// for every one of its tests would move times other tests assert as "now".
/// </para>
/// <para>
/// The clock is fixed at an instant inside the window, so every assertion here is an equality on
/// a value rather than a range — a window computed an hour wrong is still in the future, and a
/// range assertion would pass straight over exactly the defect these tests exist to catch. What
/// the fixed clock cannot do is make a delivery claimable: that is decided by the database's own
/// now(), which is why nothing here drains.
/// </para>
/// </remarks>
public sealed class NotificationQuietHoursTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>02:30 UTC on an ordinary summer night — 05:30 in Bucharest, deep inside a window
    /// that opened at 22:00 and closes at 07:00.</summary>
    private static readonly DateTimeOffset MiddleOfTheNight =
        new(2026, 7, 1, 2, 30, 0, TimeSpan.Zero);

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Starts at the real present and is moved to the night only once the test has signed in:
    /// signing in stamps a session and a token, and a host whose clock says July would issue both
    /// as long expired. Every test therefore does its HTTP first and its routing after.
    /// </summary>
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);

    private HttpClient recipient = null!;
    private Guid recipientId;

    public NotificationQuietHoursTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // Non-default on purpose: with no window named, none of these tests could
                // distinguish quiet hours working from quiet hours never having been read.
                ["Notifications:QuietHoursFrom"] = "22:00",
                ["Notifications:QuietHoursTo"] = "07:00",
                ["Notifications:TimeZone"] = "Europe/Bucharest",
            },
            services => services.AddSingleton<TimeProvider>(clock));

    public async Task InitializeAsync()
    {
        recipientId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, RecipientEmail);
        recipient = await AuthHelper.BearerClientAsync(factory, RecipientEmail);
    }

    private string RecipientEmail => $"quiet-{suffix}@t.local";

    [Fact]
    public async Task A_message_queued_in_the_night_leaves_when_the_night_ends()
    {
        await SetZoneAsync("Europe/Bucharest");
        clock.Now = MiddleOfTheNight;
        await QueueAsync(NotificationCategory.PermissionGranted, MessageTemplateCatalog.NotifyPermissionGranted);

        await RouteAsync();

        // 07:00 in Bucharest on the first of July, which is summer time: 04:00 UTC. Nothing is
        // sent here — the row simply is not due until then.
        var delivery = (await DeliveriesAsync()).ShouldHaveSingleItem();
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        delivery.NotBefore.ShouldBe(new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.Zero));
        factory.Messages.Messages.ShouldBeEmpty();

        // And the reader's own list has it now, because an inbox interrupts nobody and one that
        // hid overnight events would be lying to somebody who woke at four and opened the page.
        (await RowsAsync()).ShouldHaveSingleItem().RoutedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Somebody_reading_further_west_waits_longer()
    {
        // 02:30 UTC is 22:30 in New York — the same night, four hours behind, so their morning
        // is four hours after the Bucharest reader's. Nothing about the installation changed;
        // only whose night it is.
        await SetZoneAsync("America/New_York");
        clock.Now = MiddleOfTheNight;
        await QueueAsync(NotificationCategory.PermissionGranted, MessageTemplateCatalog.NotifyPermissionGranted);

        await RouteAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().NotBefore
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_reader_who_has_never_said_where_they_are_gets_the_installation_s_night()
    {
        // No zone is stored for this account at all, and the installation's own is Bucharest.
        clock.Now = MiddleOfTheNight;
        await QueueAsync(NotificationCategory.PermissionGranted, MessageTemplateCatalog.NotifyPermissionGranted);

        await RouteAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().NotBefore
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_category_nobody_may_switch_off_wakes_them()
    {
        // The warning that somebody else is changing your account is the one message worth
        // arriving at three in the morning — for the same reason it refuses to wait for a daily
        // summary. Asked of the category vocabulary, not of the category's name.
        await SetZoneAsync("Europe/Bucharest");
        clock.Now = MiddleOfTheNight;
        await QueueAsync(
            NotificationCategory.SecurityAlerts, MessageTemplateCatalog.NotifySecurityPasswordChanged);

        await RouteAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().NotBefore.ShouldBe(MiddleOfTheNight);
    }

    [Fact]
    public async Task Daytime_is_left_alone()
    {
        await SetZoneAsync("Europe/Bucharest");
        clock.Now = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero); // midday in Bucharest
        await QueueAsync(NotificationCategory.PermissionGranted, MessageTemplateCatalog.NotifyPermissionGranted);

        await RouteAsync();

        (await DeliveriesAsync()).ShouldHaveSingleItem().NotBefore.ShouldBe(clock.Now);
    }

    [Fact]
    public async Task A_send_that_fails_in_the_evening_is_retried_after_their_night_rather_than_in_it()
    {
        // The second writer of the due instant. A mail server that refuses connections in the
        // evening is an entirely ordinary condition, and the back-off steps to hours — so a retry
        // computed as "now plus the next step" walks straight into the window the installation
        // promised nothing would leave in. Half a minute before the window opens, so the first
        // retry step of one minute lands inside it and nothing else has to move.
        await SetZoneAsync("Europe/Bucharest");
        clock.Now = new DateTimeOffset(2026, 7, 1, 18, 59, 30, TimeSpan.Zero); // 21:59:30 in Bucharest
        await QueueAsync(NotificationCategory.PermissionGranted, MessageTemplateCatalog.NotifyPermissionGranted);

        await RouteAsync();

        // Routed for now, because the evening is not the night.
        (await DeliveriesAsync()).ShouldHaveSingleItem().NotBefore.ShouldBe(clock.Now);

        await FailOurSendAsync();

        var delivery = (await DeliveriesAsync()).ShouldHaveSingleItem();
        delivery.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        delivery.Attempts.ShouldBe(1);

        // 07:00 in Bucharest the next morning, which in summer is 04:00 UTC — not 22:00:30 that
        // same evening, which is what "now plus one minute" would have been.
        delivery.NotBefore.ShouldBe(new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Drives one failed attempt at this recipient's own delivery, and stops the moment it has
    /// happened.
    /// </summary>
    /// <remarks>
    /// Two things make this less direct than it looks. A drain is claimed in batches and is not
    /// scoped to a recipient, so this one's row may not be in the first batch — hence a pass at a
    /// time until the attempt lands, rather than one call and an assertion on how many rows moved.
    /// And the retry instant this test exists to check is in the fixed clock's future but the
    /// database's past, so the row is immediately claimable again: the loop has to stop on the
    /// first attempt or the next pass would spend the second.
    /// </remarks>
    private async Task FailOurSendAsync()
    {
        factory.Messages.FailSendsTo = RecipientEmail;
        try
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
                await service.DeliverAsync(CancellationToken.None);

                if ((await DeliveriesAsync()).Any(d => d.Attempts > 0))
                {
                    return;
                }
            }
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }

        throw new InvalidOperationException("The recipient's delivery was never attempted.");
    }

    private async Task SetZoneAsync(string zone) =>
        (await recipient.PutAsJsonAsync("/api/v1/me/locale", new { language = "en", timeZone = zone }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    private async Task QueueAsync(NotificationCategory category, string templateKey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        NotificationQueue.Enqueue(
            db,
            recipientId,
            category,
            templateKey,
            new Dictionary<string, string>
            {
                ["actorName"] = "Someone",
                ["objectName"] = $"Quiet record {suffix}",
                ["url"] = "http://localhost/records/1",
            });
        await db.SaveChangesAsync();
    }

    /// <summary>Routes what is queued without sending any of it: what is under test is when a
    /// delivery becomes due, not what happens once it is.</summary>
    private async Task RouteAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
        while (await service.RouteAsync(CancellationToken.None) > 0)
        {
        }
    }

    private async Task<List<NotificationDelivery>> DeliveriesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.RecipientUserId == recipientId).OrderBy(d => d.Id).ToListAsync();
    }

    private async Task<List<Notification>> RowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(r => r.RecipientUserId == recipientId).OrderBy(r => r.Id).ToListAsync();
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Notifications.Where(r => r.RecipientUserId == recipientId).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        recipient?.Dispose();
        factory.Dispose();
    }
}
