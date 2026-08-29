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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The reminder a club event gets from the pass that already reminds people about trips: who it
/// reaches, what stops it arriving twice, and what it will not say.
/// <para>
/// Every event here is created private and opened to exactly the people a test needs, one explicit
/// grant at a time. The group every ordinary account is put in reads past visibility at the widest
/// scope, so an account that "cannot see" an event while holding that membership proves nothing —
/// everybody refused here is a plain reader holding nothing, and the person who is told is
/// asserted in the same test as the person who is not.
/// </para>
/// <para>
/// The run-up window is shut wherever this suite runs, because one pass reads every row in the
/// database every class shares. Each test that needs it open builds a host of its own that opens
/// it, which is also the assertion that the setting is what governs the window.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EventReminderSweepTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// What the pass takes in one go. Held here as the number this suite drives against rather
    /// than read from the handler, so that changing the bound has to be a decision somebody makes
    /// in both places.
    /// </summary>
    private const int PassBatchSize = 200;

    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    private HttpClient secretary = null!; // Editor; owns the events
    private HttpClient member = null!;    // Viewer; reads their own inbox and nothing else

    // The other two never make a request: what is asserted about them is what the pass decides
    // from their own access, which is exactly the point — a reminder reaches somebody who has
    // opened nothing.
    private Guid secretaryId;
    private Guid memberId;
    private Guid strangerId;

    private Guid memberCaver;
    private Guid strangerCaver;

    public EventReminderSweepTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        secretaryId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ers-sec-{suffix}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ers-mem-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ers-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            memberCaver = (await db.Cavers.FirstAsync(c => c.UserId == memberId)).Id;
            strangerCaver = (await db.Cavers.FirstAsync(c => c.UserId == strangerId)).Id;
        }

        secretary = await AuthHelper.BearerClientAsync(factory, $"ers-sec-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"ers-mem-{suffix}@t.local");
    }

    /// <summary>
    /// The reminder rides the pass rather than being written onto the queue when the evening is
    /// arranged, and the stamp it leaves on the event is the whole of what makes it one message
    /// rather than one per pass — a pass runs every quarter of an hour through the whole run-up.
    /// </summary>
    [Fact]
    public async Task An_event_that_is_nearly_here_is_mentioned_once()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Nearly here", Tomorrow);
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Nothing has been said about it yet, which is what makes the stamp below mean something.
        (await ReminderStampOfAsync(evening)).ShouldBeNull();

        await SweepAsync(reminding);
        await SweepAsync(reminding);

        var reminders = await NoticesAboutAsync(memberId, evening);
        reminders.Count.ShouldBe(1);

        // An event carries no cave and the message declares no placeholder one could arrive in;
        // this is the same fact asserted against what was actually written down.
        reminders[0].Placeholders.ShouldNotContain("cave", Case.Insensitive);

        (await ReminderStampOfAsync(evening)).ShouldNotBeNull();
    }

    /// <summary>
    /// Being asked about an evening is not being allowed to read it. Both of these answered the
    /// same event; only one of them holds a grant on it, and the reminder is decided from that
    /// grant and from nothing on the answer.
    /// </summary>
    [Fact]
    public async Task Somebody_who_may_not_open_the_event_is_not_reminded_about_it()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Opened to one of them", Tomorrow);
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(evening, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await SweepAsync(reminding);

        // The reader who was given the event is told...
        (await NoticesAboutAsync(memberId, evening)).Count.ShouldBe(1);

        // ...and the one who was asked and given nothing is not, though both answers sit in the
        // same table and the pass read them both.
        (await NoticesAboutAsync(strangerId, evening)).ShouldBeEmpty();
    }

    /// <summary>
    /// The window is the operator's setting and nothing else. An evening past the horizon waits;
    /// the same evening is reminded about once the horizon reaches it.
    /// </summary>
    [Fact]
    public async Task An_event_beyond_the_lead_waits_until_the_window_reaches_it()
    {
        var soon = await CreateEventAsync("Just inside", Tomorrow);
        var later = await CreateEventAsync("Well past the horizon", Tomorrow.AddDays(20));
        foreach (var id in new[] { soon, later })
        {
            await MoveToAsync(id, "proposed");
            await GrantReadAsync(id, memberId);
            (await InviteAsync(id, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var twoDays = RemindingHost())
        {
            await SweepAsync(twoDays);
        }

        (await NoticesAboutAsync(memberId, soon)).Count.ShouldBe(1);
        (await NoticesAboutAsync(memberId, later)).ShouldBeEmpty();

        // Not skipped-and-stamped: the reminder is still owed, and a wider window pays it.
        (await ReminderStampOfAsync(later)).ShouldBeNull();

        using (var aMonth = RemindingHost("30.00:00:00"))
        {
            await SweepAsync(aMonth);
        }

        (await NoticesAboutAsync(memberId, later)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A shut window is a real operator setting: an installation may want the overdue check and no
    /// reminders at all, and this suite depends on it, because one pass reads every row in the
    /// database every class shares.
    /// </summary>
    [Fact]
    public async Task A_shut_window_reminds_nobody()
    {
        var evening = await CreateEventAsync("Nobody is told", Tomorrow);
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The suite's own host, whose lead is zero.
        await SweepAsync(factory);

        (await NoticesAboutAsync(memberId, evening)).ShouldBeEmpty();
        (await ReminderStampOfAsync(evening)).ShouldBeNull();
    }

    /// <summary>
    /// An evening that has already gone by is not news. It is left out of the selection rather
    /// than selected and skipped, so nothing is spent on it.
    /// </summary>
    [Fact]
    public async Task An_event_that_has_gone_by_is_not_reminded_about()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Last week", Today.AddDays(-7));
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await SweepAsync(reminding);

        (await NoticesAboutAsync(memberId, evening)).ShouldBeEmpty();
        (await ReminderStampOfAsync(evening)).ShouldBeNull();
    }

    /// <summary>
    /// A date something is due by has nobody to remind: no answer can be given about one, so there
    /// is no audience. It is left out of the selection rather than selected and skipped, because
    /// the stamp is never cleared — spending it on a send that reached nobody would mean an event
    /// later changed to a kind people answer about is never reminded at all.
    /// </summary>
    [Fact]
    public async Task A_deadline_is_neither_reminded_about_nor_marked_as_having_been()
    {
        using var reminding = RemindingHost();

        var due = await CreateEventAsync("Permit renewal", Tomorrow, kind: "deadline");
        await MoveToAsync(due, "proposed");
        await GrantReadAsync(due, memberId);

        await SweepAsync(reminding);

        (await NoticesAboutAsync(memberId, due)).ShouldBeEmpty();
        (await ReminderStampOfAsync(due)).ShouldBeNull();
    }

    /// <summary>
    /// An evening that has been put back still carries the date it was going to happen on, and
    /// nobody is going on that date. Reminding people of it would be the reminder saying something
    /// untrue, and the stamp is not spent, so the reminder is still owed once a real date is
    /// settled.
    /// </summary>
    [Fact]
    public async Task An_event_that_was_put_back_is_not_reminded_about_on_the_date_it_no_longer_has()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Put back", Tomorrow);
        await MoveToAsync(evening, "planned");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await MoveToAsync(evening, "delayed");

        await SweepAsync(reminding);

        (await NoticesAboutAsync(memberId, evening)).ShouldBeEmpty();
        (await ReminderStampOfAsync(evening)).ShouldBeNull();
    }

    /// <summary>
    /// One pass takes a bounded number of events and the next takes the rest. The bound is on the
    /// pass rather than on the calendar: what it did is written on the rows, so nothing is lost by
    /// stopping — which is the whole reason it is safe to stop.
    /// </summary>
    [Fact]
    public async Task A_pass_takes_no_more_than_its_batch_and_the_next_one_takes_the_rest()
    {
        using var reminding = RemindingHost();

        // Written straight to the table: this is about how many rows one pass claims, and driving
        // two hundred and five of them through the write route would measure the write route.
        // None of them is answered about, so none of them mails anybody — the stamp is what is
        // being counted, and it is spent on selection rather than on delivery.
        var crowd = await ManyEventsAsync(PassBatchSize + 5);

        await SweepAsync(reminding);

        var afterOne = await StampedCountAsync(crowd);
        afterOne.ShouldBeGreaterThan(0);
        afterOne.ShouldBeLessThanOrEqualTo(PassBatchSize);
        afterOne.ShouldBeLessThan(crowd.Count, "a pass that took the lot is not bounded by anything");

        await SweepAsync(reminding);

        (await StampedCountAsync(crowd)).ShouldBe(crowd.Count);
    }

    /// <summary>
    /// The reminder names the evening it is about, so that what a reader may be shown can be
    /// decided again when they open it rather than only when it was written.
    /// </summary>
    /// <remarks>
    /// A reminder sits in a mailbox for the whole of the run-up, and the title and the link in it
    /// were frozen when the pass ran. The reference is the only thing on the row a reader's
    /// present access can be tested against, so a row without one keeps showing what it froze
    /// however the grants move afterwards.
    /// </remarks>
    [Fact]
    public async Task A_reminder_names_the_event_it_is_about()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Named on the row", Tomorrow);
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await SweepAsync(reminding);

        var reminder = (await NoticesAboutAsync(memberId, evening)).ShouldHaveSingleItem();
        reminder.TargetKind.ShouldBe(NotificationTargetKind.Event);
        reminder.TargetId.ShouldBe(evening);
    }

    /// <summary>
    /// Once the grant that let somebody read the evening is taken away, the reminder already in
    /// their inbox stops saying what it was about.
    /// </summary>
    /// <remarks>
    /// This is the half the pass cannot decide. The pass asked the right question at the right
    /// moment and wrote a row somebody was entitled to; what makes the row safe afterwards is that
    /// the same question is asked again at the moment it is read. Both halves are asserted here —
    /// the line is first shown whole, so that the withheld line below is a change and not the only
    /// thing this route was ever capable of.
    /// </remarks>
    [Fact]
    public async Task A_reminder_stops_naming_the_event_once_the_reader_may_no_longer_open_it()
    {
        using var reminding = RemindingHost();

        var evening = await CreateEventAsync("Opened and then shut", Tomorrow);
        await MoveToAsync(evening, "proposed");
        await GrantReadAsync(evening, memberId);
        (await InviteAsync(evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await SweepAsync(reminding);

        var whole = await InboxLineAsync(evening);
        whole.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();
        whole.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        whole.GetProperty("url").GetString().ShouldBe($"/events/{evening}");

        await RevokeReadAsync(evening, memberId);

        // The unreadable state, constructed rather than assumed.
        (await member.GetAsync($"/api/v1/events/{evening}")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        var shut = await InboxLineAsync(evening);
        shut.GetProperty("targetWithheld").GetBoolean().ShouldBeTrue();
        shut.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
        shut.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- fixture ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly Tomorrow => Today.AddDays(1);

    /// <summary>
    /// A host whose run-up window is open. Built per test rather than shared, because the setting
    /// is read at start-up and the suite's own host deliberately keeps it shut.
    /// </summary>
    private SilexGisApiFactory RemindingHost(string lead = "2.00:00:00") =>
        new(connectionString, new Dictionary<string, string?> { ["TripCallout:ReminderLead"] = lead });

    private static async Task SweepAsync(SilexGisApiFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TripCalloutSweep);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);
    }

    /// <summary>
    /// An event written through the door that leaves it private, so that everybody who can read it
    /// below can read it because a grant says so and for no other reason.
    /// </summary>
    private async Task<Guid> CreateEventAsync(string title, DateOnly start, string kind = "clubMeeting")
    {
        var response = await secretary.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            kind,
            startDate = start.ToString("yyyy-MM-dd"),
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Rows written straight to the table, owned by the secretary and answered about by nobody.
    /// </summary>
    private async Task<List<Guid>> ManyEventsAsync(int howMany)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rows = Enumerable.Range(0, howMany)
            .Select(i => new Event
            {
                Title = $"Crowd {suffix} {i}",
                Kind = EventKind.ClubMeeting,
                StartDate = Tomorrow,
                OwnerUserId = secretaryId,
                Visibility = Visibility.Private,
                State = ActivityState.Proposed,
            })
            .ToList();
        db.Events.AddRange(rows);
        await db.SaveChangesAsync();
        return [.. rows.Select(r => r.Id)];
    }

    private async Task<int> StampedCountAsync(List<Guid> eventIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Events.AsNoTracking()
            .CountAsync(e => eventIds.Contains(e.Id) && e.PlanReminderSentAt != null);
    }

    private async Task MoveToAsync(Guid eventId, string state)
    {
        var response = await secretary.PostWithIfMatchAsync($"/api/v1/events/{eventId}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> InviteAsync(Guid eventId, Guid caverId) =>
        secretary.PostAsJsonAsync($"/api/v1/events/{eventId}/invitations/", new { caverId });

    /// <summary>
    /// The member's own inbox line for one event, found by the notification the sweep wrote for
    /// them rather than by position, because the suite shares a database with every other class.
    /// </summary>
    private async Task<JsonElement> InboxLineAsync(Guid eventId)
    {
        var wanted = (await NoticesAboutAsync(memberId, eventId)).ShouldHaveSingleItem().Id;

        var response = await member.GetAsync($"/api/v1/notifications/{wanted}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task RevokeReadAsync(Guid eventId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AccessEntries
            .Where(e => e.SubjectKind == AccessSubjectKind.User
                && e.SubjectId == userId
                && e.Domain == AccessDomain.Events
                && e.ScopeId == eventId)
            .ExecuteDeleteAsync();
    }

    private async Task GrantReadAsync(Guid eventId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Events,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = eventId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One person's reminders about one event. Narrowed in memory on the stored placeholders,
    /// which are jsonb and have no text-matching operator.
    /// </summary>
    private async Task<List<Notification>> NoticesAboutAsync(Guid userId, Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var sent = await db.Notifications.AsNoTracking()
            .Where(x => x.RecipientUserId == userId
                && x.TemplateKey == MessageTemplateCatalog.NotifyEventReminder)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return [.. sent.Where(x => x.Placeholders.Contains(eventId.ToString(), StringComparison.Ordinal))];
    }

    private async Task<DateTimeOffset?> ReminderStampOfAsync(Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId)).PlanReminderSentAt;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        secretary?.Dispose();
        member?.Dispose();
        factory.Dispose();
    }
}
