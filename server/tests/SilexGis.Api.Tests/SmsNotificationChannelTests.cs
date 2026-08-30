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
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What decides whether a notification leaves as a text message, and what happens when it does.
/// </summary>
/// <remarks>
/// <para>
/// These are worth an integration test rather than a unit one, because each is a disagreement
/// waiting to happen between parts that are written separately: a number counts as an address only
/// once its owner has answered a code sent to it; an account with no number is an ordinary outcome
/// and not a fault; a gateway that refuses climbs the same retry ladder as anything else and stops
/// at the same rung; and an installation that has not agreed to pay per message creates no
/// outbound copy at all rather than one that fails.
/// </para>
/// <para>
/// The rest are about money, and they are here rather than beside the arithmetic because what is
/// being checked is that the arithmetic is fed by the real transport. Every hand-over is counted
/// where the ceiling is read from, so the operator's view and the refusal cannot disagree; the
/// ceiling that binds is the one the product ships with, nothing injected; and a text an operator
/// puts back asks the recipient's current answer again before it goes, because nothing about a
/// message that has left can be undone afterwards.
/// </para>
/// <para>
/// The numbers are confirmed through the routes a browser calls, never by assigning the columns.
/// The whole point of the first test is which column means "proved", so a fixture that set them
/// by hand would be asserting its own arrangement.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SmsNotificationChannelTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// What one copy of this fixture's notice is weighed at before anybody is handed it: the
    /// pieces the announcement wording splits into when it names this club.
    /// </summary>
    /// <remarks>
    /// Two, because the wording is weighed in every language the installation writes and the
    /// dearest answer stands — and the language whose marks fall outside the narrow alphabet packs
    /// 67 characters to a piece, where this notice needs a second one. The same notice really
    /// handed to an English reader costs one, which is what the tests above are about.
    /// </remarks>
    private const int WeighedPerCopy = 2;

    private readonly string connectionString;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<Guid> mine = [];

    private SilexGisApiFactory factory = null!;
    private Guid cavingGroupId;
    private Guid leaderId;

    public SmsNotificationChannelTests(PostgresFixture postgres) => connectionString = postgres.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_number_nobody_has_proved_is_theirs_is_never_texted()
    {
        await StartAsync();
        var account = await NewAccountAsync("proved");

        // Confirmed, then changed. The live number keeps its proof and the new one waits for a
        // code, so the account holds both at once — which is exactly the state in which reading
        // the wrong column texts a roster's business to somebody's typo, at the installation's
        // expense, with nothing downstream able to catch it because the message has left.
        var proved = await ConfirmAsync(account, Number(1));
        await ChooseTextAsync(account);
        var unproved = Number(2);
        (await account.Client.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = unproved }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Messages.Clear();
        await QueueAnnouncementAsync(account.Id);
        await DrainAsync();

        // The positive half, so this cannot pass by nothing being sent at all.
        var text = factory.Messages.LastTo(proved);
        text.Channel.ShouldBe("sms");
        text.Body.ShouldContain($"Sms Club {suffix}");

        factory.Messages.Messages
            .Any(m => string.Equals(m.Recipient, unproved, StringComparison.Ordinal))
            .ShouldBeFalse("a number awaiting confirmation is nobody's proved address");
    }

    [Fact]
    public async Task An_account_with_no_number_is_reached_by_mail_alone_and_that_is_not_a_fault()
    {
        await StartAsync();
        var reachable = await NewAccountAsync("has-number");
        var unreachable = await NewAccountAsync("no-number");
        var number = await ConfirmAsync(reachable, Number(3));
        await ChooseTextAsync(reachable);

        factory.Messages.Clear();
        await QueueAnnouncementAsync(reachable.Id);
        await QueueAnnouncementAsync(unreachable.Id);
        await DrainAsync();

        (await DeliveriesAsync(reachable.Id)).Select(d => d.Channel)
            .ShouldBe([NotificationChannel.Email, NotificationChannel.Sms], ignoreOrder: true);
        factory.Messages.LastTo(number).Channel.ShouldBe("sms");

        // Nothing to send to is a decision not to send, and a decision not to send is the absence
        // of a row — not a dead one. A dead row would put a permanent fault in the operator's
        // health view for every account that has never added a phone number.
        var nothingSent = (await DeliveriesAsync(unreachable.Id)).ShouldHaveSingleItem();
        nothingSent.Channel.ShouldBe(NotificationChannel.Email);
        nothingSent.Status.ShouldNotBe(NotificationDeliveryStatus.Dead);
    }

    [Fact]
    public async Task A_text_the_gateway_refuses_climbs_the_ladder_and_stops_at_its_top()
    {
        await StartAsync();
        var account = await NewAccountAsync("refused");
        var number = await ConfirmAsync(account, Number(4));
        await ChooseTextAsync(account);

        factory.Messages.Clear();
        try
        {
            // Aimed at the number rather than at whatever is sent next: a drain is not scoped to
            // a recipient, so an untargeted failure lands on whichever row the batch reached
            // first — including the mail copy of this same notification, which must keep going.
            factory.Messages.FailSendsTo = number;
            await QueueAnnouncementAsync(account.Id);

            for (var attempt = 1; attempt <= NotificationRouting.MaxAttempts; attempt++)
            {
                await DrainAsync();

                var text = await TextRowAsync(account.Id);
                text.Attempts.ShouldBe(attempt);
                text.Error.ShouldNotBeNullOrWhiteSpace();

                if (attempt < NotificationRouting.MaxAttempts)
                {
                    text.Status.ShouldBe(NotificationDeliveryStatus.Pending);

                    // Whether a row is due is decided by the database's own clock, so the only way
                    // forward is to bring the backoff's end into the past.
                    await MakeDueAsync(account.Id);
                }
                else
                {
                    text.Status.ShouldBe(NotificationDeliveryStatus.Dead);
                }
            }
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }

        // The mail copy is unaffected: one transport refusing is not the notification failing.
        (await DeliveriesAsync(account.Id))
            .Single(d => d.Channel == NotificationChannel.Email)
            .Status.ShouldBe(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task An_installation_that_has_not_agreed_to_pay_creates_no_text_at_all()
    {
        await StartAsync(payForMessages: false);
        var account = await NewAccountAsync("unpaid");
        var number = await ConfirmAsync(account, Number(5));

        // The switch being off does not merely stop the sending — it takes the channel out of what
        // this category may use at all, so the account cannot even choose it. Asserting the refusal
        // here rather than skipping the choice is what makes the rest of the test mean something:
        // nothing below is passing because somebody forgot to opt in.
        (await TryChooseTextAsync(account)).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        factory.Messages.Clear();
        await QueueAnnouncementAsync(account.Id);
        await DrainAsync();

        // Not a row that fails, and not a row that is dead — no row. A switch that is off masks
        // the channel out of what the category may use, so routing has nothing to write down and
        // the day's spending has nothing to count.
        var deliveries = await DeliveriesAsync(account.Id);
        deliveries.ShouldHaveSingleItem().Channel.ShouldBe(NotificationChannel.Email);

        // The positive half, in the same test: the account really is reachable by text — it has a
        // proved number — and the installation's answer is what keeps it from being one. Since a
        // paid cell also starts off, the two guards are independent, and this test pins the
        // installation's half by making the account's half unreachable rather than merely unset.
        factory.Messages.Messages
            .Any(m => string.Equals(m.Recipient, number, StringComparison.Ordinal))
            .ShouldBeFalse();
        (await ReachableByTextAsync(account.Id)).ShouldBeTrue();
    }


    [Fact]
    public async Task An_installation_with_no_gateway_creates_no_text_and_spends_nothing()
    {
        await StartAsync();
        var operatorClient = await OperatorClientAsync();
        var account = await NewAccountAsync("gatewayless");
        var number = await ConfirmAsync(account, Number(10));
        await ChooseTextAsync(account);

        // The installation says it will pay for texts and has nothing to send them with — an
        // operator who ticked the switch before filling the gateway in, or who cleared the gateway
        // afterwards. Being implemented is not being installed.
        factory.Messages.SmsConfigured = false;
        factory.Messages.Clear();
        await QueueAnnouncementAsync(account.Id);
        await DrainAsync();

        // No row, rather than a row that settles as sent having gone only to the log. A row would
        // be counted as a day's spending, so an installation in this state would refuse
        // announcements once it had spent a ceiling on messages nobody was ever billed for.
        (await DeliveriesAsync(account.Id)).ShouldHaveSingleItem()
            .Channel.ShouldBe(NotificationChannel.Email);
        factory.Messages.Messages.ShouldNotContain(m => string.Equals(m.Channel, "sms", StringComparison.Ordinal));
        (await HealthAsync(operatorClient)).GetProperty("paidMessagesToday").GetInt32().ShouldBe(0);

        // The positive half: the same account, the same switch, a gateway away from being texted.
        // Without it this would pass on an installation that had simply stopped sending.
        factory.Messages.SmsConfigured = true;
        await QueueAnnouncementAsync(account.Id);
        await DrainAsync();

        (await DeliveriesAsync(account.Id)).ShouldContain(d => d.Channel == NotificationChannel.Sms);
        factory.Messages.LastTo(number).Channel.ShouldBe("sms");
    }

    [Fact]
    public async Task What_a_day_of_texts_costs_counts_the_handover_and_never_the_refusal()
    {
        await StartAsync();
        var operatorClient = await OperatorClientAsync();

        // Two accounts either side of the line money is spent on. One has proved a number, so a
        // text is handed to the gateway for it; the other has none, so nothing is ever handed
        // over on its behalf.
        var proved = await NewAccountAsync("counted");
        var number = await ConfirmAsync(proved, Number(6));
        await ChooseTextAsync(proved);
        var mailOnly = await NewAccountAsync("uncounted");

        factory.Messages.Clear();
        try
        {
            // The gateway takes the message and then reports a failure. That is the harder half of
            // the line and the one worth pinning: the transport throws only after the request has
            // been made, so an installation that did not count this would be billed for messages
            // its own ledger says it never sent.
            factory.Messages.FailSendsTo = number;
            await QueueAnnouncementAsync(proved.Id);
            await QueueAnnouncementAsync(mailOnly.Id);
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }

        var text = await TextRowAsync(proved.Id);
        text.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        text.Error.ShouldNotBeNullOrWhiteSpace();

        // Nothing was handed over for the account with no proved number, and nothing was written
        // down either — the refusal happens while the row is still being decided on.
        (await DeliveriesAsync(mailOnly.Id)).ShouldHaveSingleItem()
            .Channel.ShouldBe(NotificationChannel.Email);

        var health = await HealthAsync(operatorClient);

        // One text, counted once, and counted through the same code the announcement endpoint
        // refuses with — so what an operator reads as headroom is the headroom the next
        // announcement will find. The two mail copies are not money and are not in it.
        health.GetProperty("paidMessagesToday").GetInt32().ShouldBe(1);
        health.GetProperty("dailyPaidMessageCap").GetInt32()
            .ShouldBe(AnnouncementSettings.DefaultDailyPaidMessageCap);

        var counts = health.GetProperty("counts").EnumerateArray().ToList();
        CountOf(counts, "sms", "pending").ShouldBe(1);
        CountOf(counts, "email", "sent").ShouldBe(2);
    }

    [Fact]
    public async Task A_text_in_romanian_costs_the_day_two_where_the_same_one_in_english_costs_one()
    {
        await StartAsync();
        var operatorClient = await OperatorClientAsync();

        // Two accounts sent the same announcement, differing in one thing: the language each of
        // them reads it in. Both halves are asserted here rather than in two tests, because
        // either alone is satisfied by a counter that never looks at the text — the English half
        // agrees with counting rows, and the Romanian half alone could be any constant.
        var english = await NewAccountAsync("gsm");
        var englishNumber = await ConfirmAsync(english, Number(20));
        await ChooseTextAsync(english);

        var romanian = await NewAccountAsync("wide");
        var romanianNumber = await ConfirmAsync(romanian, Number(21));
        await ChooseTextAsync(romanian);
        await ReadsRomanianAsync(romanian);

        await ClearTodaysSpendingAsync();
        factory.Messages.Clear();
        await QueueAnnouncementAsync(english.Id);
        await DrainAsync();

        // Every character of the English wording is in the seven-bit alphabet a carrier packs
        // 160 of into one piece, so the message travels in one and costs one.
        factory.Messages.LastTo(englishNumber).Channel.ShouldBe("sms");
        (await TextRowAsync(english.Id)).Segments.ShouldBe(1);
        (await HealthAsync(operatorClient)).GetProperty("paidMessagesToday").GetInt32().ShouldBe(1);

        await QueueAnnouncementAsync(romanian.Id);
        await DrainAsync();

        var wide = factory.Messages.LastTo(romanianNumber).Body;

        // The wording that actually left, in the language the account reads. Asserted on the
        // rendered text and not on the catalogue, because what is billed is what was handed over
        // — and asserted on a word carrying the marks, so that stripping them anywhere in the
        // wording would fail this test rather than quietly halve the bill.
        wide.ShouldContain("Citiți");

        // Not length. The Romanian message is well inside what a single seven-bit piece would
        // have held, so a counter that only divided by 160 would have answered one. What doubles
        // it is that one character is outside that alphabet, which re-encodes the whole message
        // into pieces of 70.
        wide.Length.ShouldBeLessThan(TextMessageSegments.SevenBitSegment);
        (await TextRowAsync(romanian.Id)).Segments.ShouldBe(2);

        // Three pieces for two messages — what the carrier bills, and what the ceiling has to be
        // counting for it to mean anything to an installation whose members read Romanian.
        (await HealthAsync(operatorClient)).GetProperty("paidMessagesToday").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_hand_over_the_gateway_refused_still_costs_and_a_delivery_dropped_gives_it_back()
    {
        await StartAsync();
        var operatorClient = await OperatorClientAsync();
        var account = await NewAccountAsync("refused");
        var number = await ConfirmAsync(account, Number(22));
        await ChooseTextAsync(account);

        await ClearTodaysSpendingAsync();
        factory.Messages.Clear();
        try
        {
            // Taken by the gateway and then refused. The only honest reading of that is that it
            // may well have gone, so it is charged — and charged what it weighed.
            //
            // This account reads English, and that is what makes the assertion below mean
            // anything: a row is written with what its message was weighed at before anybody was
            // handed anything, which is two here, and one piece is a number nothing but the
            // hand-over itself can have written. An account reading Romanian would be charged the
            // same two either way, and this test would pass over a failure path that reported no
            // amount at all.
            factory.Messages.FailSendsTo = number;
            await QueueAnnouncementAsync(account.Id);
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }

        var text = await TextRowAsync(account.Id);
        text.Status.ShouldBe(NotificationDeliveryStatus.Pending);
        text.Error.ShouldNotBeNullOrWhiteSpace();
        text.Segments.ShouldBe(1);
        (await HealthAsync(operatorClient)).GetProperty("paidMessagesToday").GetInt32().ShouldBe(1);

        // And the other side of the same line: the recipient answers differently before the retry
        // comes round, so the row goes and nothing was ever handed over on it again. The day gets
        // back what the row was charged — all of it, whatever that amount was — because there is
        // nothing left to be billed for.
        await StopTextsAsync(account);
        await MakeDueAsync(account.Id);
        await DrainAsync();

        (await DeliveriesAsync(account.Id)).ShouldNotContain(d => d.Channel == NotificationChannel.Sms);
        (await HealthAsync(operatorClient)).GetProperty("paidMessagesToday").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_shipped_ceiling_is_reached_by_what_the_carrier_bills_and_not_by_the_message_count()
    {
        await StartAsync();
        var leaderClient = await LeaderClientAsync();
        var member = await NewAccountAsync("halved");
        await ConfirmAsync(member, Number(23));
        await ChooseTextAsync(member);
        await JoinAsync(member.Id);

        // Half as many messages as the shipped ceiling allows, each of them two pieces — which is
        // what one text in Romanian weighs. Counted as messages this installation has spent half
        // its day and has room for fifty more; counted as the carrier bills, the day is gone.
        // Nothing about the ceiling is injected: the number that binds is the one that ships.
        await SpendTodayAsync(AnnouncementSettings.DefaultDailyPaidMessageCap / 2, segments: 2);

        var refused = await AnnounceAsync(leaderClient, "over the line");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_paid_cap_reached");

        // Refused before anything was written, so it cost nothing: no notice for anybody to read
        // and no row for the day to count.
        (await NoticesForAsync(member.Id)).ShouldBeEmpty();

        // The control that makes the refusal above mean something, and the reason this test is
        // not the shipped-ceiling one over again: exactly the same number of messages, each of
        // them one piece instead of two, and the announcement goes. What refused it was the
        // amount the carrier bills and not the number of rows in the day.
        await SpendTodayAsync(AnnouncementSettings.DefaultDailyPaidMessageCap / 2, segments: 1);

        var allowed = await AnnounceAsync(leaderClient, "under the line");

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await NoticesForAsync(member.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_ceiling_the_product_ships_with_refuses_the_announcement_that_would_pass_it()
    {
        await StartAsync();
        var leaderClient = await LeaderClientAsync();
        var member = await NewAccountAsync("roster");
        await ConfirmAsync(member, Number(7));
        await ChooseTextAsync(member);
        await JoinAsync(member.Id);

        // Nothing about the ceiling is injected here. The installation says only that it will pay
        // for messages; how much it will pay is the number the product ships with, and the whole
        // point of this test is that the shipped one binds. A test that set its own cap would
        // prove the arithmetic and leave the default untested — which is how this guard reached
        // a release having never refused anything.
        await SpendTodayAsync(AnnouncementSettings.DefaultDailyPaidMessageCap);

        var refused = await AnnounceAsync(leaderClient, "over the line");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("caving_group.announcement_paid_cap_reached");

        // Refused before anything was written, so it cost nothing: no notification for anybody to
        // read and no delivery row for the day to count.
        (await NoticesForAsync(member.Id)).ShouldBeEmpty();

        // The positive half, one notice's worth of headroom apart — which is what this club's
        // notice weighs, and not one: the wording is weighed in the language that costs most, and
        // that one needs the wide alphabet. The leader still has their turn because a refusal does
        // not spend the cooldown a sender gets between announcements.
        await SpendTodayAsync(AnnouncementSettings.DefaultDailyPaidMessageCap - WeighedPerCopy);

        var allowed = await AnnounceAsync(leaderClient, "under the line");

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await NoticesForAsync(member.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_dead_text_put_back_by_an_operator_cannot_arrive_after_it_was_switched_off()
    {
        await StartAsync();
        var operatorClient = await OperatorClientAsync();

        var kept = await NewAccountAsync("kept");
        await ConfirmAsync(kept, Number(8));
        await ChooseTextAsync(kept);
        var withdrawn = await NewAccountAsync("withdrawn");
        var withdrawnNumber = await ConfirmAsync(withdrawn, Number(9));
        await ChooseTextAsync(withdrawn);

        factory.Messages.Clear();
        try
        {
            factory.Messages.FailSendsTo = null;
            await QueueAnnouncementAsync(kept.Id);
            await QueueAnnouncementAsync(withdrawn.Id);
            await DrainAsync();
        }
        finally
        {
            factory.Messages.FailSendsTo = null;
        }

        // Put both texts where an operator finds them. How they died is not what is under test —
        // that the answer is asked again before one is sent a second time is.
        var keptText = await MakeDeadAsync(kept.Id);
        var withdrawnText = await MakeDeadAsync(withdrawn.Id);

        var switchedOff = await withdrawn.Client.PutAsJsonAsync(
            "/api/v1/me/notifications/",
            new
            {
                categories = new[]
                {
                    new
                    {
                        category = "groupAnnouncement",
                        channels = new[] { new { channel = "sms", choice = "off" } },
                    },
                },
            });
        switchedOff.StatusCode.ShouldBe(HttpStatusCode.OK, await switchedOff.Content.ReadAsStringAsync());

        factory.Messages.Clear();

        // A text cannot be recalled once it has left, so the answer has to be asked before it
        // goes rather than checked when somebody reads it. This is the one path in the system
        // that can send a message the ordinary routing pass already decided against.
        (await RetryAsync(operatorClient, withdrawnText))
            .GetProperty("outcome").GetString().ShouldBe("suppressed");
        (await DeliveriesAsync(withdrawn.Id)).ShouldNotContain(d => d.Channel == NotificationChannel.Sms);

        // And the other one, one stored choice apart, goes: the retry is not refusing everything.
        (await RetryAsync(operatorClient, keptText)).GetProperty("outcome").GetString().ShouldBe("queued");
        await DrainAsync();
        (await TextRowAsync(kept.Id)).Status.ShouldBe(NotificationDeliveryStatus.Sent);

        factory.Messages.Messages
            .Any(m => string.Equals(m.Recipient, withdrawnNumber, StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Stands up a host whose installation either will or will not pay for messages.
    /// </summary>
    /// <remarks>
    /// The switch and the resend interval are both read from configuration on purpose. A settings
    /// key that binds to nothing fails silently and looks exactly like one that works, because the
    /// shipped default goes on doing the job — a wrong name here would leave every text masked out
    /// and three of the four tests below would pass for the wrong reason. A saved row replaces
    /// configuration wholesale and this suite shares one database, so any answer another class
    /// left behind is cleared first.
    /// </remarks>
    private async Task StartAsync(bool payForMessages = true)
    {
        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Announcements:PaidChannelsEnabled"] = payForMessages ? "true" : "false",

                // Confirming a number and then changing it is two texted codes in a row, which the
                // shipped minute-long cooldown between them exists to prevent.
                ["Security:TwoFactorResendIntervalSeconds"] = "0",
                ["Auth:RateLimitPerMinute"] = "500",
            },
            TestHostTweaks.WithoutJobWorker);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AppSettings
            .Where(s => s.Key == AppSettingSections.Announcements || s.Key == AppSettingSections.Security)
            .ExecuteDeleteAsync();

        // Notifications are one table for the whole database and a drain is not scoped to anyone,
        // so leftovers queued by an earlier class would be claimed here — and this class injects a
        // targeted delivery failure that must land on its own row.
        await db.Notifications.ExecuteDeleteAsync();

        // And announcements another class recorded but never had handed out: what a day has
        // promised counts towards its ceiling as well as what it has already handed over.
        await db.CavingGroupAnnouncements.Where(a => a.ExpandedAt == null).ExecuteDeleteAsync();

        var cavingGroup = new CavingGroup
        {
            Name = $"Sms Club {suffix}",
            Slug = $"sms-club-{suffix}",
            Type = CavingGroupType.CavingClub,
        };
        db.CavingGroups.Add(cavingGroup);
        await db.SaveChangesAsync();
        cavingGroupId = cavingGroup.Id;
    }

    private async Task<Account> NewAccountAsync(string tag)
    {
        var email = $"sms-{tag}-{suffix}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        mine.Add(id);
        return new Account(id, email, await AuthHelper.BearerClientAsync(factory, email));
    }

    /// <summary>Adds a number and answers the code texted to it, exactly as the settings page does.</summary>
    /// <summary>
    /// Chooses text messages for the one category that may cost money.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from proving the number, and every test that expects a text calls
    /// both. Confirming a number proves whose it is; it is the sign-in number and is never offered
    /// as a contact address, so it says nothing about wanting to be texted. A paid channel
    /// therefore starts off and somebody has to choose it — which is why a test that forgot this
    /// call would see no text and be right to.
    /// </remarks>
    private async Task ChooseTextAsync(Account account) =>
        (await TryChooseTextAsync(account)).StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>Attempts the choice and hands back what the server said, refusal included.</summary>
    private async Task<HttpResponseMessage> TryChooseTextAsync(Account account)
    {
        var response = await account.Client.PutAsJsonAsync(
            "/api/v1/me/notifications",
            new
            {
                categories = new[]
                {
                    new
                    {
                        category = NotificationCategory.GroupAnnouncement,
                        channels = new[]
                        {
                            new
                            {
                                channel = NotificationChannelKind.Sms,
                                choice = NotificationChannelChoice.Immediate,
                            },
                        },
                    },
                },
            });

        return response;
    }

    /// <summary>Stores Romanian as the language this account reads, as its own settings page does.</summary>
    /// <remarks>
    /// Through the route rather than by writing the column, because what is under test downstream
    /// is the text that a real account's stored language produces — and the language a message is
    /// rendered in is the one thing that decides what it weighs.
    /// </remarks>
    private static async Task ReadsRomanianAsync(Account account)
    {
        var response = await account.Client.PutAsJsonAsync(
            "/api/v1/me/locale", new { language = "ro", timeZone = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Withdraws the choice that lets an announcement leave as a text.</summary>
    private static async Task StopTextsAsync(Account account)
    {
        var response = await account.Client.PutAsJsonAsync(
            "/api/v1/me/notifications/",
            new
            {
                categories = new[]
                {
                    new
                    {
                        category = "groupAnnouncement",
                        channels = new[] { new { channel = "sms", choice = "off" } },
                    },
                },
            });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> ConfirmAsync(Account account, string number)
    {
        var change = await account.Client.PostAsJsonAsync(
            "/api/v1/me/phone/change", new { phoneNumber = number });
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        var confirm = await account.Client.PostAsJsonAsync(
            "/api/v1/me/phone/confirm", new { code = factory.Messages.LastTo(number).Code });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return number;
    }

    /// <summary>
    /// Queues the one notification whose category may cost money, without going through the
    /// endpoint that composes it: what is under test is what the transport does with a routed
    /// notification, not who wrote one.
    /// </summary>
    private async Task QueueAnnouncementAsync(Guid recipientId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        NotificationQueue.Enqueue(
            db,
            recipientId,
            NotificationCategory.GroupAnnouncement,
            MessageTemplateCatalog.NotifyGroupAnnouncement,
            new Dictionary<string, string>
            {
                ["actorName"] = "Ana",
                ["cavingGroupName"] = $"Sms Club {suffix}",
                ["announcement"] = $"Meeting moved {suffix}",
                ["url"] = "/caving-groups",
            },
            NotificationTargetKind.CavingGroup,
            cavingGroupId);
        await db.SaveChangesAsync();
    }

    /// <summary>Whether the transport itself would reach this account, asked of the channel.</summary>
    private async Task<bool> ReachableByTextAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        return scope.ServiceProvider
            .GetRequiredService<NotificationChannels>()
            .Of(NotificationChannel.Sms)
            .CanReach(user);
    }

    private async Task DrainAsync()
    {
        int pass;
        do
        {
            await using var scope = factory.Services.CreateAsyncScope();
            pass = await scope.ServiceProvider
                .GetRequiredService<NotificationDeliveryService>()
                .RunOnceAsync(CancellationToken.None);
        }
        while (pass > 0);
    }

    private async Task<NotificationDelivery> TextRowAsync(Guid userId) =>
        (await DeliveriesAsync(userId)).Single(d => d.Channel == NotificationChannel.Sms);

    private async Task MakeDueAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationDeliveries
            .Where(d => d.RecipientUserId == userId && d.Status == NotificationDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NotBefore, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private async Task<List<NotificationDelivery>> DeliveriesAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.RecipientUserId == userId)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }


    /// <summary>An account that may look at what is leaving the installation and put one back.</summary>
    /// <remarks>
    /// Putting a delivery back is Execute rather than Read: it sends somebody else's message, and
    /// on this channel it spends somebody else's money.
    /// </remarks>
    private async Task<HttpClient> OperatorClientAsync()
    {
        var email = $"sms-adm-{suffix}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, email);
        mine.Add(id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = id,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Settings,
            Actions = AccessAction.Read | AccessAction.Execute,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();

        return await AuthHelper.BearerClientAsync(factory, email);
    }

    /// <summary>Somebody who may write one line to the whole club.</summary>
    private async Task<HttpClient> LeaderClientAsync()
    {
        var email = $"sms-lead-{suffix}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        mine.Add(id);
        leaderId = id;

        await using var scope = factory.Services.CreateAsyncScope();
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

        return await AuthHelper.BearerClientAsync(factory, email);
    }

    /// <summary>Puts an account on the club's roster, which is who an announcement reaches.</summary>
    private async Task JoinAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await RosterHelper.AddMemberAsync(db, cavingGroupId, userId);
    }

    private Task<HttpResponseMessage> AnnounceAsync(HttpClient leader, string message) =>
        leader.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/announcements", new { message });

    /// <summary>
    /// Puts <paramref name="count"/> charged messages into today and nothing into any other day.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than produced by a hundred announcements, and stamped by hand
    /// because a delivery's timestamp is written by whatever creates it rather than defaulted by
    /// the database — a row seeded without one lands in the year 1 and falls outside the day the
    /// ceiling counts over, which would leave every assertion around it passing for the wrong
    /// reason. What they carry is the real charged channel, because the set the day is counted on
    /// is derived from what is registered rather than named anywhere.
    /// </remarks>
    /// <param name="count">How many messages the day has already sent.</param>
    /// <param name="segments">
    /// What each of them weighed. One is the cheapest a text can be and is what a day of English
    /// wording costs; two is what the same wording costs an account reading a language whose
    /// letters are outside the seven-bit alphabet. Separating the two is the whole point of
    /// letting a caller say it: a helper that assumed one would be asserting that a message is a
    /// message, which is the belief under test.
    /// </param>
    private async Task SpendTodayAsync(int count, int segments = 1)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The day's history is hung off the sender, who is never one of their own announcement's
        // recipients — so what the club is about to be charged stays the only thing the roster
        // decides. Deleting the notifications afterwards is not an option: a delivery belongs to
        // its notification and the cascade would take the day's spending with it.
        await db.Notifications.Where(n => n.RecipientUserId == leaderId).ExecuteDeleteAsync();
        await db.NotificationDeliveries.ExecuteDeleteAsync();

        var notifications = Enumerable.Range(0, count).Select(_ => new Notification
        {
            RecipientUserId = leaderId,
            Category = NotificationCategory.GroupAnnouncement,
            TemplateKey = MessageTemplateCatalog.NotifyGroupAnnouncement,
            RoutedAt = DateTimeOffset.UtcNow,
        }).ToList();
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        db.NotificationDeliveries.AddRange(notifications.Select(n => new NotificationDelivery
        {
            NotificationId = n.Id,
            RecipientUserId = leaderId,
            Channel = NotificationChannel.Sms,
            Status = NotificationDeliveryStatus.Sent,
            CreatedAt = now,
            NotBefore = now,
            SentAt = now,

            // Written by hand for the same reason the timestamp is. A row costs what the carrier
            // split its text into, so a row seeded without an amount is a message that was sent
            // and charged for nothing — and every count around it would come out short while
            // reading as if the arithmetic worked. One apiece is the cheapest a text can be, which
            // is what keeps this helper meaning "this many messages" to the tests that use it.
            Segments = segments,
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Takes the day's spending back to nothing, leaving what this test then does as the whole of
    /// it.
    /// </summary>
    /// <remarks>
    /// Three deletes and not one, because what the day has committed to is not only what it has
    /// handed over: an announcement another class left unrouted, or one accepted and not yet
    /// handed out, is spending a test here would see and could not account for. Every class in
    /// this suite shares one database and none of them run at the same time as each other.
    /// </remarks>
    private async Task ClearTodaysSpendingAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.NotificationDeliveries.ExecuteDeleteAsync();
        await db.Notifications
            .Where(n => n.RoutedAt == null && n.Category == NotificationCategory.GroupAnnouncement)
            .ExecuteDeleteAsync();
        await db.CavingGroupAnnouncements.Where(a => a.ExpandedAt == null).ExecuteDeleteAsync();
    }

    /// <summary>Gives up on this account's text, and answers with the row an operator would click.</summary>
    private async Task<long> MakeDeadAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = (await TextRowAsync(userId)).Id;
        await db.NotificationDeliveries
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, NotificationDeliveryStatus.Dead)
                .SetProperty(d => d.Attempts, NotificationRouting.MaxAttempts)
                .SetProperty(d => d.Error, "Simulated delivery failure."));
        return id;
    }

    private async Task<List<Notification>> NoticesForAsync(Guid recipientId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == recipientId)
            .ToListAsync();
    }

    private static async Task<JsonElement> HealthAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/admin/notifications/health");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> RetryAsync(HttpClient client, long deliveryId)
    {
        var response = await client.PostAsync(
            $"/api/v1/admin/notifications/deliveries/{deliveryId}/retry", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int CountOf(List<JsonElement> counts, string channel, string status) =>
        counts
            .Where(c => c.GetProperty("channel").GetString() == channel
                && c.GetProperty("status").GetString() == status)
            .Select(c => c.GetProperty("count").GetInt32())
            .SingleOrDefault();

    /// <summary>
    /// A number unique to this class and this test. One number reaches exactly one account and
    /// every test class shares one database, so a fixed one would collide with a neighbour's.
    /// </summary>
    private string Number(int index) =>
        "+4" + ((uint)suffix.GetHashCode(StringComparison.Ordinal) % 100000000U)
            .ToString("D8", CultureInfo.InvariantCulture)
        + index.ToString("D2", CultureInfo.InvariantCulture);

    public async Task DisposeAsync()
    {
        if (factory is null)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Deliveries go with their notifications: the cascade is on the constraint, so a
        // set-based delete of the notifications takes them too.
        await db.Notifications.Where(n => mine.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        await db.AccessEntries
            .Where(e => e.SubjectId != null && mine.Contains(e.SubjectId.Value))
            .ExecuteDeleteAsync();
        await db.CavingGroups.Where(g => g.Id == cavingGroupId).ExecuteDeleteAsync();
        await db.AppSettings
            .Where(s => s.Key == AppSettingSections.Announcements)
            .ExecuteDeleteAsync();
    }

    public void Dispose() => factory?.Dispose();

    private sealed record Account(Guid Id, string Email, HttpClient Client);
}
