// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Looks for parties past the hour they said they would be back and tells whoever the trip names,
/// and reminds the people on a trip that is nearly here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pass rather than an alarm set in advance.</b> A message written onto the queue ahead of
/// time cannot be recalled: its wording is fixed the moment it is written, and nothing outside the
/// delivery machinery can find it again by the trip it is about. A party home early, a trip put
/// back and a trip called off would all still raise the alarm, quoting a time that is no longer
/// true. This reads the trip as it stands on every run, which is the whole of what makes standing
/// an alarm down possible.
/// </para>
/// <para>
/// <b>Why the trip row is the idempotence.</b> The queue has no key to ask "was one already sent
/// about this" with. So each trip's own check state is moved out of <i>armed</i> in the same
/// transaction that queues the messages about it, and the next pass no longer selects it.
/// </para>
/// <para>
/// <b>Why the transaction is opened here rather than left to the save.</b> Two reasons, and both
/// are about a lost alarm rather than about tidiness. First, this handler does not own the context
/// it writes through: the worker that runs jobs resolves the handler from the same scope as its own
/// context, and after a handler throws it records the failure on the job row and saves — which
/// would flush whatever this pass had already tracked. A pass that failed half way would then
/// commit trips moved out of <i>armed</i> whose messages were never written, and no later pass
/// would select them again, because leaving <i>armed</i> is precisely what takes a trip out of the
/// selection. That is a party nobody is ever told about. Owning the transaction, rolling it back,
/// and discarding this pass's tracked work on the way out is what stops that save from committing
/// half a pass. Second, each trip's move is claimed with a guarded update rather than written
/// blind, so somebody tapping <i>the party is out</i> while the pass is mid-flight is not
/// overwritten by it — see <see cref="ClaimAsync"/>.
/// </para>
/// <para>
/// <b>What it does not say.</b> No message names a cave. Where a trip is going is readable by
/// fewer people than the trip's own list of people, and the temptation is at its strongest here —
/// an overdue party feels like the one message that ought to say where to look. It still does not:
/// whoever runs a search reads the trip, where the answer is already kept for them under the rules
/// that decide who may have it. Each recipient's right to read the <i>trip</i> is decided from
/// their own access, freshly, by the same rule every other notification uses.
/// </para>
/// <para>
/// Delivery from here on is the ordinary outbox's, which is at-least-once and gives up after
/// several attempts by marking a message dead in a table no route reads. That is a stated limit of
/// this alarm, not an accident: the trip's own overdue state is the durable half, and a page that
/// shows an armed alarm is expected to say when this pass last completed, so that a check nobody
/// ran reads as unchecked rather than as nothing wrong.
/// </para>
/// </remarks>
public sealed class TripCalloutSweepHandler(
    SilexGisDbContext db,
    IAccessService access,
    IOptions<TripCalloutOptions> options,
    ILogger<TripCalloutSweepHandler> logger) : IProcessingJobHandler
{
    /// <summary>
    /// How many trips one pass moves. A bound rather than a policy: the selection is normally a
    /// handful of rows, and a pass that somehow found thousands should still finish, commit what it
    /// did, and let the next one take the rest — which it can, because what it did is on the rows.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>
    /// The states in which a trip's own date is still a date somebody is going on, resolved once
    /// from the rule that decides it rather than listed again here.
    /// </summary>
    /// <remarks>
    /// Deliberately not the rule about whether a change is worth mailing about. A trip that has
    /// been put back is worth telling people about and its date is not one anybody is going on, so
    /// reminding them of it would quote a date that is no longer true — which is the same mistake
    /// this whole design exists to avoid.
    /// </remarks>
    private static readonly ActivityState[] RemindedStates =
        [.. Enum.GetValues<ActivityState>().Where(TripPlanNotices.RemindsOfDate)];

    private static readonly ActivityState[] WatchedStates =
        [.. Enum.GetValues<ActivityState>().Where(TripCalloutRules.WatchesForOverdue)];

    public string Kind => ProcessingJobKinds.TripCalloutSweep;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // The pass's own transaction. See the remarks on this type for why it is not left to the
        // save: the context is shared with the worker, whose failure bookkeeping saves too.
        await using var pass = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var overdue = await RaiseOverdueAlarmsAsync(now, ct);
            var reminded = await SendRemindersAsync(now, ct);

            if (overdue > 0 || reminded > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            await pass.CommitAsync(ct);

            if (overdue > 0 || reminded > 0)
            {
                logger.LogInformation(
                    "Trip callout pass: {Overdue} overdue, {Reminded} reminded", overdue, reminded);
            }
        }
        catch
        {
            // Rolled back explicitly rather than left to the dispose, and then emptied of anything
            // this pass had queued, because the caller's own save is still to come on this very
            // context. A message left tracked here would be written outside the transaction that
            // was supposed to carry it, about a trip whose move has just been undone.
            await pass.RollbackAsync(CancellationToken.None);
            DiscardQueuedMessages();
            throw;
        }
    }

    /// <summary>
    /// Detaches everything this pass had queued, so that a save made by whoever called it cannot
    /// write messages belonging to a pass that did not commit.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose: clearing the whole change tracker would also detach the job row the
    /// caller is in the middle of recording the failure on, and the failure would then be lost as
    /// well. The state moves need no undoing here — they are made by guarded updates that the
    /// rollback above has already taken back, and never by tracked edits.
    /// </remarks>
    /// <remarks>
    /// The queued row is the notification itself. What carries it out of the installation is a
    /// separate row the worker writes later, so nothing of that kind exists to detach here — a
    /// sweep that rolls back has queued events and has sent nothing.
    /// </remarks>
    private void DiscardQueuedMessages()
    {
        foreach (var entry in db.ChangeTracker.Entries<Notification>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Moves every trip whose alarm time has gone by out of <see cref="TripCalloutState.Armed"/>
    /// and queues a message to each person the trip concerns who may read it.
    /// </summary>
    /// <remarks>
    /// A trip that was called off or put back is left alone: nobody went on the first, and on the
    /// second the hour the check was armed against belongs to a date that has moved. It stays armed
    /// rather than being quietly cleared, because clearing it here would be this pass deciding
    /// something only a person should — and a trip put back that is put back on keeps the check
    /// somebody set for it.
    /// </remarks>
    private async Task<int> RaiseOverdueAlarmsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.TripLogs
            .AsNoTracking()
            .Where(t => t.CalloutState == TripCalloutState.Armed
                && t.CalloutAlarmAt != null
                && t.CalloutAlarmAt <= now
                && WatchedStates.Contains(t.State))
            .OrderBy(t => t.CalloutAlarmAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var raised = 0;
        foreach (var trip in due)
        {
            if (!await ClaimAsync(trip.Id, ct))
            {
                continue;
            }

            var expected = trip.ExpectedReturnAt ?? trip.CalloutAlarmAt;
            await QueueAsync(
                trip,
                NotificationCategory.TripCallout,
                MessageTemplateCatalog.NotifyTripCalloutOverdue,
                new Dictionary<string, string>
                {
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = FormatDate(trip.TripDate),
                    ["expectedReturn"] = FormatInstant(expected),
                    ["url"] = TripUrl(trip.Id),
                },
                ct);
            raised++;
        }

        return raised;
    }

    /// <summary>
    /// Takes one trip out of <see cref="TripCalloutState.Armed"/>, and answers whether this pass is
    /// the one that took it.
    /// </summary>
    /// <remarks>
    /// Written as an update carrying the state it expects to find, rather than as an edit to the
    /// row this pass read a moment ago, because between the reading and the writing there is real
    /// work — the trip's people, and one access decision each — and the moment stand-downs cluster
    /// is exactly the alarm hour this pass is running at. An edit written blind would put a trip
    /// back to <i>overdue</i> seconds after somebody said the party was out, and then mail everyone
    /// about it. Answering false means somebody got there first, and there is nothing to say.
    /// <para>
    /// Inside this pass's transaction, so the claim and the messages it leads to stand or fall
    /// together, and so the row is held against a second writer until the pass commits.
    /// </para>
    /// </remarks>
    private async Task<bool> ClaimAsync(Guid tripId, CancellationToken ct)
    {
        var moved = await db.TripLogs
            .Where(t => t.Id == tripId && t.CalloutState == TripCalloutState.Armed)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.CalloutState, TripCalloutState.Overdue), ct);
        return moved == 1;
    }

    /// <summary>
    /// Tells the people on a trip that it is nearly here, once.
    /// </summary>
    /// <remarks>
    /// It rides this pass rather than being written onto the queue when the trip is arranged, for
    /// the same reason the alarm does: a reminder set weeks ahead would still arrive after the trip
    /// was called off or moved, saying a date nobody is going on any more. The stamp it leaves on
    /// the trip is the whole of what stops it sending again on the next pass, and it is never
    /// cleared: a trip already reminded about, then put back and planned again on a new date, is
    /// not reminded a second time, which is a deliberate reading of "once".
    /// <para>
    /// The states it selects are the ones whose date is still a date somebody is going on, which is
    /// deliberately not the same set as the states whose changes are worth mailing about. A trip
    /// that has been put back keeps the date it was going to happen on until a new one is settled,
    /// and a reminder quoting that date would be the reminder saying something untrue. Left
    /// unstamped rather than skipped-and-stamped, so the reminder is still owed on the real date.
    /// </para>
    /// </remarks>
    private async Task<int> SendRemindersAsync(DateTimeOffset now, CancellationToken ct)
    {
        var lead = options.Value.ReminderLead;
        if (lead <= TimeSpan.Zero)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var horizon = DateOnly.FromDateTime((now + lead).UtcDateTime);
        var comingUp = await db.TripLogs
            .AsNoTracking()
            .Where(t => t.PlanReminderSentAt == null
                && t.TripDate >= today
                && t.TripDate <= horizon
                && RemindedStates.Contains(t.State))
            .OrderBy(t => t.TripDate)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var trip in comingUp)
        {
            var stamped = await db.TripLogs
                .Where(t => t.Id == trip.Id && t.PlanReminderSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanReminderSentAt, now), ct);
            if (stamped != 1)
            {
                continue;
            }

            await QueueAsync(
                trip,
                NotificationCategory.TripPlanning,
                MessageTemplateCatalog.NotifyTripPlanReminder,
                new Dictionary<string, string>
                {
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = FormatDate(trip.TripDate),
                    ["url"] = TripUrl(trip.Id),
                },
                ct);
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Queues one message for everybody the trip concerns whose own access lets them read it.
    /// </summary>
    /// <remarks>
    /// Nobody is excluded as the actor, because there is no actor: a clock is not a person, so
    /// unlike an edit there is nobody here who already knows what the message says.
    /// </remarks>
    private async Task QueueAsync(
        TripLog trip,
        NotificationCategory category,
        string templateKey,
        Dictionary<string, string> placeholders,
        CancellationToken ct)
    {
        var concerned = await TripAudience.PeopleConcernedAsync(db, trip.Id, ct);
        var recipients = await NotificationRecipients.WhoMayReadAsync(
            db, access, trip, concerned, excluding: null, ct);

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(db, recipient, category, templateKey, placeholders);
        }
    }

    private static string TripUrl(Guid tripId) => $"/trip-logs/{tripId}";

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// An instant, said in UTC and labelled as such. No account carries a time zone anywhere in
    /// this system, so the alternative is not a local time — it is an unlabelled number the reader
    /// has to guess the zone of, in the one message where guessing an hour wrong matters most.
    /// </summary>
    private static string FormatInstant(DateTimeOffset? instant) =>
        instant is { } at
            ? at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : string.Empty;
}
