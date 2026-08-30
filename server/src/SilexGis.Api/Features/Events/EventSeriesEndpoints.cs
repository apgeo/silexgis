// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// The two acts that reach past one occurrence of a repeating event: changing this one and every
/// later one with it, and calling off the rest of the run.
/// </summary>
/// <remarks>
/// <para>
/// A series is not a thing here — it is the set of ordinary events that share a grouping key, and
/// every one of them is read, answered, moved and deleted by the routes that were already there.
/// These two routes exist for exactly the acts a person means to aim at more than one evening at a
/// time, and they are the only ones: everything else about an occurrence is done to the occurrence.
/// </para>
/// <para>
/// <b>Both are all-or-nothing, and that is the whole of their design.</b> The right to act is
/// settled once, for the set, before anything is written; a set holding one occurrence this caller
/// may not touch is refused entire, with nothing changed. A bulk act that half-applies is worse
/// than one that refuses — the caller is told it worked, the calendar disagrees with itself, and
/// nobody finds out until somebody turns up to an evening that was supposed to have moved.
/// </para>
/// <para>
/// Neither route ever reaches outside the set it names. The occurrences are selected by the
/// grouping key of the occurrence the caller addressed, so an event belonging to another series,
/// or to none, is not reachable from here however it is asked for.
/// </para>
/// </remarks>
public static class EventSeriesEndpoints
{
    /// <summary>
    /// An act aimed at a series, addressed to an event that is part of none. Distinct from a
    /// missing event: the caller is looking at something real and is asking it a question it
    /// cannot answer, and a surface told this can offer the single-occurrence act instead.
    /// </summary>
    public const string NotInSeriesCode = "event.not_in_series";

    /// <summary>
    /// The caller may act on the occurrence they addressed but not on every occurrence the act
    /// would reach. Nothing was changed. It is deliberately one code rather than a list of the
    /// occurrences that refused: naming them would describe rows the caller may not read.
    /// </summary>
    public const string SeriesPartlyForbiddenCode = "event.series_partly_forbidden";

    /// <summary>
    /// Moving this occurrence by the distance asked for would carry a later one off the end of the
    /// calendar. Nothing was changed. A refusal rather than a clamped date, because a series whose
    /// last occurrences all piled up on the final day of the calendar is not what anybody asked
    /// for and there is no honest day to put them on instead.
    /// </summary>
    public const string SeriesMoveOutOfRangeCode = "event.series_move_out_of_range";

    public static RouteGroupBuilder MapEventSeriesEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");

        events.MapPut("/{id:guid}/series/following", EditFollowingAsync)
            .WithValidation<EventWriteRequest>()
            .WithSummary(
                "Applies one edit to this occurrence and every later one of its series (Write "
                + "permission on all of them, settled before anything is written). The days keep "
                + "the spacing they had: moving this occurrence moves the rest by the same "
                + "number of days. Answers with how many occurrences were changed.");
        events.MapDelete("/{id:guid}/series/following", DeleteFollowingAsync)
            .WithSummary(
                "Calls off the rest of a repeating event: this occurrence and every later one, "
                + "except any that has already begun. Answers with how many were removed and how "
                + "many were kept.");

        return api;
    }

    /// <summary>
    /// Makes every occurrence from this one on match the one being edited, except for which day
    /// it falls on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The days are shifted rather than overwritten. An edit that wrote its start date onto every
    /// occurrence would collapse the whole run onto a single day, which is never what anybody
    /// moving a weekly meeting from Tuesday to Wednesday is asking for — so the difference between
    /// the day the caller was shown and the day they asked for is applied to all of them, and the
    /// spacing the series was generated with survives. The length of each occurrence is taken from
    /// the edit, because "this and following" means the later ones become this one.
    /// </para>
    /// <para>
    /// The precondition is over the occurrence the caller addressed and is required, exactly as it
    /// is on the edit of one event. It is honestly less than it looks: one version token cannot
    /// speak for a set, so it says that the occurrence on the screen has not moved underneath the
    /// author, and says nothing about the later ones. Requiring it anyway is the stronger of the
    /// two available answers — the alternative is a bulk edit with no precondition at all.
    /// </para>
    /// <para>
    /// Unlike the delete beside it, this reaches back over occurrences that have already happened
    /// when the caller anchors on one. Correcting the place a meeting was held is a correction to a
    /// record; removing the record is not, which is why only one of the two stops at today.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<EventSeriesEditResultDto>, ProblemHttpResult>> EditFollowingAsync(
        Guid id,
        EventWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var anchor = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (anchor is null)
        {
            return ApiProblems.NotFound(EventEndpoints.NotFoundCode);
        }

        if (await EventEndpoints.RefuseUnlessWritableAsync(access, ctx, anchor, ct) is { } refusal)
        {
            return refusal;
        }

        if (anchor.SeriesId is not { } seriesId)
        {
            return ApiProblems.BadRequest(
                NotInSeriesCode,
                "This event is not one of a repeating run, so there is nothing following it to "
                + "change with it.");
        }

        if (EventEndpoints.ValidateReferences(ctx!, request) is { } problem)
        {
            return problem;
        }

        if (request.Recurrence is not null)
        {
            return ApiProblems.BadRequest(
                EventEndpoints.RecurrenceCreateOnlyCode,
                "A repetition is settled when an event is created. Changing occurrences changes "
                + "the occurrences.");
        }

        if (await Concurrency.CheckIfMatchAsync(
                http, db, VersionedTable.Events, anchor.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        // Ordered so that the answer is stable and the first row is the one the caller addressed;
        // the tie-break is the primary key for the same reason it is on the list — two occurrences
        // of one series on the same day are unusual but not impossible once somebody has moved one.
        var rows = await db.Events
            .Where(x => x.SeriesId == seriesId && x.StartDate >= anchor.StartDate)
            .OrderBy(x => x.StartDate).ThenBy(x => x.Id)
            .ToListAsync(ct);

        if (await RefuseUnlessAllPermittedAsync(access, ctx, rows, AccessAction.Write, ct) is { } partly)
        {
            return partly;
        }

        // Asked over the whole set rather than the anchor alone, and before anything is written:
        // an edit that moves the kind to one nobody is asked to would leave answers on every
        // occurrence with no door onto them, and a refusal that counted one row's answers while
        // changing a dozen rows' would be telling the caller about the wrong thing.
        if (!EventKinds.AcceptsResponses(request.Kind))
        {
            var ids = rows.Where(x => EventKinds.AcceptsResponses(x.Kind)).Select(x => x.Id).ToList();
            var answers = ids.Count == 0
                ? 0
                : await db.TripInvitations.CountAsync(x => x.EventId != null && ids.Contains(x.EventId.Value), ct);
            if (answers > 0)
            {
                return ApiProblems.Conflict(
                    EventEndpoints.KindHasResponsesCode,
                    $"{answers} answer(s) are on file about these occurrences, and an event of "
                    + "that kind is not answered. Remove them first, or leave the kind as it is.");
            }
        }

        // Read before anything is applied, because applying to the anchor is what would destroy
        // the day the shift is measured from.
        var shift = request.StartDate.DayNumber - anchor.StartDate.DayNumber;
        var storedEnd = DayRange.EndForStorage(request.StartDate, request.EndDate);
        var span = storedEnd is { } finish ? finish.DayNumber - request.StartDate.DayNumber : 0;

        // Worked out in day numbers and checked before a single date is built. Nothing upstream
        // bounds the day an edit may name, so a shift of several thousand years is a request
        // somebody can send, and DateOnly answers arithmetic past its own range by throwing —
        // which would surface as a failure instead of as the refusal it is. Checked over the
        // whole set first, so this route stays all-or-nothing here as it is everywhere else.
        var moved = new List<(Event Row, DateOnly Start)>(rows.Count);
        foreach (var row in rows)
        {
            var number = (long)row.StartDate.DayNumber + shift;
            if (number < DateOnly.MinValue.DayNumber || number + span > DateOnly.MaxValue.DayNumber)
            {
                return ApiProblems.BadRequest(
                    SeriesMoveOutOfRangeCode,
                    "Moving this occurrence that far would carry a later one of the run off the "
                    + "end of the calendar, so none of them were moved. Pick a nearer day.");
            }

            moved.Add((row, DateOnly.FromDayNumber((int)number)));
        }

        foreach (var (row, start) in moved)
        {
            // The whole edit is applied first — title, kind, times, place, room, audience — and
            // then the dates are put back to this occurrence's own day. Going through the same
            // apply the single edit goes through is what stops a field added to an event tomorrow
            // from reaching the anchor and none of the rest.
            EventEndpoints.Apply(row, request);
            row.StartDate = start;
            row.EndDate = DayRange.EndForStorage(start, start.AddDays(span));
        }

        // One save for the whole set, so the change is one act in the trail as it is to the person
        // who asked for it, and a failure anywhere in it leaves the series exactly as it was.
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new EventSeriesEditResultDto
        {
            SeriesId = seriesId,
            Changed = rows.Count,
            Anchor = EventEndpoints.Map(anchor),
        });
    }

    /// <summary>
    /// Calls off the remainder of a repeating event, and leaves what has already happened alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An occurrence that has already begun is never removed by this.</b> It is a record of
    /// something that happened — who was asked, who said they would come — and calling off the
    /// evenings still to come is not a statement about the ones that already were. So the set is
    /// this occurrence and the later ones, minus any whose day is behind us, and the answer says
    /// how many were kept as well as how many went. A caller who really means to erase one past
    /// evening deletes that event, one row, on the route that does exactly that.
    /// </para>
    /// <para>
    /// The rules anchored on each occurrence go with it, loaded and removed one row at a time
    /// rather than deleted in a single statement — a rule disappearing is a change to who may
    /// reach what, and a set-based delete never reaches the change tracker, so the withdrawal
    /// would happen with nothing in the trail to say it had. The answers people gave follow the
    /// occurrences they were about, by the cascade the answer table already declares.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<EventSeriesDeleteResultDto>, ProblemHttpResult>> DeleteFollowingAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var anchor = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (anchor is null)
        {
            return ApiProblems.NotFound(EventEndpoints.NotFoundCode);
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, anchor, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, anchor, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(EventEndpoints.NotFoundCode);
        }

        if (anchor.SeriesId is not { } seriesId)
        {
            return ApiProblems.BadRequest(
                NotInSeriesCode,
                "This event is not one of a repeating run, so there is nothing following it to "
                + "call off with it.");
        }

        // Offered but not required, the way it is on every delete here: a list deletes a row it
        // never loaded a version of.
        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Events, anchor.Id, ct) is { } stale)
        {
            return stale;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = anchor.StartDate > today ? anchor.StartDate : today;

        // Deliberately not visibility-filtered: this is the set the act would reach, and an
        // occurrence the caller may not read is exactly the one the all-or-nothing refusal below
        // exists to catch. Filtering here would drop it out of the set and delete the rest.
        var doomed = await db.Events
            .Where(x => x.SeriesId == seriesId && x.StartDate >= from)
            .ToListAsync(ct);

        if (await RefuseUnlessAllPermittedAsync(access, ctx, doomed, AccessAction.Delete, ct) is { } partly)
        {
            return partly;
        }

        // Counted over the occurrences this caller may read, and asked as its own question rather
        // than taken as the remainder of the set: the set is loaded unfiltered so that a future
        // occurrence hidden from the caller still refuses the whole act above, and a remainder of
        // an unfiltered set would report how many past evenings exist to somebody who may not open
        // one of them. Asked before the removal, while the past rows are still all that match.
        var kept = await db.Events
            .AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Events)
            .CountAsync(x => x.SeriesId == seriesId && x.StartDate < from, ct);

        var doomedIds = doomed.Select(x => x.Id).ToHashSet();
        var anchored = await db.AccessEntries
            .Where(e => e.Domain == AccessDomain.Events
                && e.ScopeKind == AccessScopeKind.Object
                && e.ScopeId != null
                && doomedIds.Contains(e.ScopeId.Value))
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(anchored);
        db.Events.RemoveRange(doomed);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new EventSeriesDeleteResultDto
        {
            SeriesId = seriesId,
            Deleted = doomed.Count,
            Kept = kept,
        });
    }

    /// <summary>
    /// Nothing, when this caller may do <paramref name="action"/> to every one of the rows; a
    /// refusal naming the whole set when they may not do it to one.
    /// </summary>
    /// <remarks>
    /// The answer is about the set and not about the rows: it is asked before any of them is
    /// touched, and one refusal refuses all of it. Occurrences of one series are generated
    /// identically and almost always answer alike, so this is one question in practice — but a
    /// rule can be written on a single occurrence after the fact, and a bulk act that skipped the
    /// occurrence it was written on would be the way round that rule.
    /// </remarks>
    private static async Task<ProblemHttpResult?> RefuseUnlessAllPermittedAsync(
        IAccessService access,
        AccessContext? ctx,
        IReadOnlyList<Event> rows,
        AccessAction action,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (ctx is null || !(await access.DecideAsync(ctx, action, row, ct)).Allowed)
            {
                return ApiProblems.Forbidden(
                    SeriesPartlyForbiddenCode,
                    "At least one occurrence of this series is not yours to change, so none of "
                    + "them were. Act on the occurrences one at a time instead.");
            }
        }

        return null;
    }
}
