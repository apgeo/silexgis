// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static partial class TripLogEndpoints
{
    /// <summary>
    /// Arranges, changes or calls off the check that notices if a party does not come back.
    /// </summary>
    /// <remarks>
    /// Whoever may change the trip, because arranging one is part of planning the trip. Saying the
    /// party is out is the other half and is not this: it belongs to the people on the trip and
    /// has its own route. The precondition header is required as on every other write to a trip —
    /// this one edits the record, and two people arranging different hours is the lost update it
    /// exists to catch.
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> ArrangeCalloutAsync(
        Guid id,
        TripCalloutRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        ApplyCallout(trip, request);
        await db.SaveChangesAsync(ct);
        await hold.AnswerAsync(ct, TripPath(trip.Id));

        var arranged = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(arranged[0]);
    }

    /// <summary>
    /// Says the party is out: the overdue check stops, and stays on the trip as something that was
    /// arranged and stood down rather than being erased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Who may.</b> Anybody the trip names or has asked — not whoever may edit it. The two are
    /// different questions with different answers: the person who knows the party is out is on the
    /// trip, and making them wait for somebody with the right to change the record is exactly the
    /// delay a callout exists to avoid. Whoever may edit the trip is not left without a way: the
    /// arrangement itself is theirs to change, and clearing the alarm time on the trip calls the
    /// whole thing off. Reading the trip is required as well, because saying nothing about a trip
    /// you cannot read is the same refusal every other route gives.
    /// </para>
    /// <para>
    /// <b>Why it takes no precondition header.</b> Every other write to a trip requires one, to
    /// stop two people overwriting each other's edits. This one is not an edit: there is one value
    /// it can write, everybody who may call it is saying the same thing, and a stale header would
    /// refuse the message that says a party is safe. Two people tapping it at once is two people
    /// agreeing.
    /// </para>
    /// <para>
    /// A second tap answers 200 rather than a conflict, for the same reason: somebody who is not
    /// sure the first one went through will tap again, and telling them it failed is the one wrong
    /// answer available. A trip nobody arranged a check for is the refusal, because there is
    /// nothing to stand down and saying "done" would report a check that never existed as handled.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> StandDownCalloutAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null || ctx is null || user is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        // The same set the alarm itself is sent to, asked from the one place that knows it: the
        // people it would wake are the people who may say it is not needed. Two definitions of
        // "who this trip concerns" would be free to disagree, and the disagreement that mattered
        // would be somebody told a party is overdue with no way to say they are not.
        var concerns = await TripAudience.ConcerningAsync(db, [trip.Id], user.UserId, ct);
        if (!concerns.Contains(trip.Id))
        {
            return ApiProblems.Forbidden();
        }

        if (trip.CalloutState == TripCalloutState.None)
        {
            return ApiProblems.Conflict(
                "trip_log.callout_not_armed",
                "This trip has no overdue check to stand down.");
        }

        if (trip.CalloutState != TripCalloutState.StoodDown)
        {
            // The times stay exactly as they were. What was arranged is part of the record of the
            // trip — a search that was nearly called is worth being able to read afterwards — and
            // the state is the only thing that moves.
            trip.CalloutState = TripCalloutState.StoodDown;
            await db.SaveChangesAsync(ct);
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        // No precondition on the way in, but the version on the way out all the same: a page that
        // stood the check down and then saves the trip is saving over the version this produced.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, TripPath(trip.Id));
        return TypedResults.Ok(items[0]);
    }

    // ---- shared pieces ----

    /// <summary>
    /// The path a trip is read and written at — what a creation answers in <c>Location</c>, and
    /// what an action posted beside the trip names in <c>Content-Location</c> so the version it
    /// answers is filed where the trip's next write will look for it.
    /// </summary>
    internal static string TripPath(Guid id) => $"/api/v1/trip-logs/{id}";

    /// <summary>
    /// Moves a trip to another lifecycle state. Which moves exist is not decided here — the
    /// transition table is the one place that knows, so a state a trip may not hold and a move it
    /// may not make are refused by the same rule and with the same code.
    /// </summary>
    /// <remarks>
    /// The precondition is required exactly as it is on a full update: publishing announces the
    /// write-up somebody has read, and announcing a version that changed underneath them is the
    /// lost update the header exists to prevent.
    /// </remarks>
    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> TransitionAsync(
        Guid id,
        TripLogTransitionRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ITripRosterAnnouncer announcer,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        // One question, not two: a target the vocabulary admits but a trip may not hold appears in
        // no pair of the table, so asking the table refuses it for the same reason and under the
        // same code as an illegal move. Asking whether the state is an admitted one first would be
        // a second rule saying the same thing, free to drift from it.
        //
        // The state is present because the validator filter runs before this and requires it; a
        // body that names none is a 400 and never arrives here.
        var target = request.State!.Value;
        if (!ActivityStates.MayTripLogTransition(trip.State, target))
        {
            return ApiProblems.Conflict(
                ActivityStates.TripLogTransitionInvalidCode,
                $"A trip log does not move from {trip.State} to {target}.");
        }

        var stateBefore = trip.State;
        trip.State = target;
        if (target == ActivityState.Cancelled)
        {
            // A deliberate notice sent by the path that calls a trip off, not a state-entry one:
            // the switch that decides whether entering a state tells the roster the trip exists
            // still answers "no" for a cancelled trip, and should. Being called off is the one
            // thing the people expecting to go on it have to be told, and it is told here.
            await TripPlanNotifier.CancelledAsync(db, access, user, trip, stateBefore, ct);
        }
        else if (target != ActivityState.Published && TripPlanNotices.AnnouncesChanges(stateBefore))
        {
            // A move of a plan is a change to it, and the one people are most likely to need: a
            // trip put back to a date not yet chosen the evening before is exactly what somebody
            // expecting to go on it has to hear. Told only when they were already expecting it —
            // a plan leaving the workshop announces itself by other means, and the notice would
            // otherwise be the first they heard of it. Whether the state it lands in is still one
            // people are expecting anything from is the notifier's own question, so a plan going
            // back into the workshop or off to be written up stays silent.
            await TripPlanNotifier.ChangedAsync(db, access, user, trip, [], ct);
        }

        if (target == ActivityState.Published)
        {
            // The stamp records when the trip first went out and is never moved: withdrawing it and
            // putting it out again is the same announcement corrected, so "since when has this been
            // public" keeps one answer.
            //
            // Telling people is a separate question with a different answer, on purpose. Every
            // announcement tells whoever may read the trip at that moment, including one that
            // follows a withdrawal, because the alternative is worse: while a trip is back in draft
            // its edits notify nobody, so announcing only the first time would silently leave out
            // everybody added in between — and they are exactly the people the announcement is for.
            // A correction cycle therefore costs the roster a repeated message, which is the side
            // to err on.
            trip.PublishedAt ??= DateTimeOffset.UtcNow;
            await NotifyOnPublishAsync(db, announcer, trip, ct);
        }

        await db.SaveChangesAsync(ct);
        // The version the move produced, filed against the trip's own path: a page that moved the
        // trip and then edits it is editing the version the move made.
        await hold.AnswerAsync(ct, TripPath(trip.Id));

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Tells the people named on a trip that it has been announced.
    /// </summary>
    /// <remarks>
    /// The roster is re-read here rather than trusting a list assembled while the trip was a
    /// draft: a draft's roster is written and rewritten before anyone else sees it, so the people
    /// to tell at the moment of the announcement are whoever is on it then, not whoever was added
    /// by the last edit. Whether each of them may actually read the trip is decided by the
    /// notifier itself, for every caller alike.
    /// </remarks>
    private static async Task NotifyOnPublishAsync(
        SilexGisDbContext db, ITripRosterAnnouncer announcer, TripLog trip, CancellationToken ct)
    {
        var roster = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == trip.Id && caver.UserId != null
            select caver.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await announcer.AnnounceAsync(trip, roster, ct);
    }

    /// <summary>
    /// Arms, re-arms or calls off the overdue check from the two times the request states.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state is not a field anybody sends, because a surface that could post "armed" could
    /// post "stood down" too, and saying a party is out belongs to the people on the trip rather
    /// than to whoever may edit it. So it follows from the alarm time, by three readings:
    /// </para>
    /// <para>
    /// No alarm time means there is no arrangement — a callout is called off by clearing the hour
    /// it was set for, and there is nothing else an absent alarm on a trip somebody is editing
    /// could be asking for.
    /// </para>
    /// <para>
    /// An alarm time that differs from the stored one is a new arrangement and arms the check,
    /// whichever state it was in. A party who came back, stood the check down and then went in
    /// again for the evening has arranged a second callout, not repeated the first.
    /// </para>
    /// <para>
    /// The same alarm time leaves the state alone, and that reading is the load-bearing one: it is
    /// what stops an edit made for some entirely other reason — a note added, a person added,
    /// anything a form saves whole — from quietly re-arming a check that somebody has already
    /// stood down, which would raise an alarm about a party that is sitting in the pub.
    /// </para>
    /// </remarks>
    private static void ApplyCallout(TripLog trip, TripCalloutRequest request)
    {
        var armedFor = trip.CalloutAlarmAt;
        trip.ExpectedReturnAt = request.ExpectedReturnAt;
        trip.CalloutAlarmAt = request.CalloutAlarmAt;

        if (request.CalloutAlarmAt is null)
        {
            trip.CalloutState = TripCalloutState.None;
        }
        else if (request.CalloutAlarmAt != armedFor)
        {
            trip.CalloutState = TripCalloutState.Armed;
        }
    }

    /// <summary>
    /// Whether the scheduled pass would actually look at this trip's check, which is a narrower
    /// question than whether a check is arranged on it.
    /// </summary>
    /// <remarks>
    /// A check on a trip that was called off or put back stays on the record — clearing it is a
    /// person's decision, not a rule's — but no pass will ever raise it, because the arrangement it
    /// was set against no longer describes anything happening. Saying when the pass last ran would
    /// then be answering a question nobody asked: the pass did run, and it deliberately looked
    /// straight past this trip. Reporting that as <i>checked</i> is the one reading this whole
    /// value exists to prevent, so the answer is withheld and the surface falls back to saying the
    /// check is not being watched.
    /// </remarks>
    private static bool IsWatched(TripLog trip) =>
        trip.CalloutState is TripCalloutState.Armed or TripCalloutState.Overdue
            && TripCalloutRules.WatchesForOverdue(trip.State);
}
