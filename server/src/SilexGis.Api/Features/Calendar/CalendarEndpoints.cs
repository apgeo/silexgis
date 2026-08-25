// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Calendar;

/// <summary>
/// The club's dated records, read as one list over a window of days.
/// </summary>
/// <remarks>
/// <para>
/// It is a projection and holds nothing of its own: the rows are read from the tables that own
/// them, each narrowed by its reader's ordinary visibility first, and merged in memory. A
/// database view over the three would be a second — and for the two SQL forms of the visibility
/// walk, a third — place expressing who may read what, which is how two answers to that question
/// come to exist.
/// </para>
/// <para>
/// <b>The window is what makes the merge exact, which is why it is required rather than
/// convenient.</b> Two shipped surfaces merge several sources in memory and both have to fudge
/// it: the recent-activity feed reads each source up to the final limit before merging because
/// its feed is unbounded in time, and the search answer caps each source separately because a
/// text query is unbounded in matches. Neither fudge is needed here — within a bounded window
/// each source's readable rows are a finite set that can be read whole, so concatenating and
/// ordering them yields exactly the rows of that window in exactly that order, with no source
/// able to push another's rows out.
/// </para>
/// <para>
/// A row cap survives as a backstop against a window nobody expected, and when it bites the
/// answer says by how much. A silently short answer is indistinguishable from a complete one,
/// and a record of a month that is quietly missing days is worse than one that refuses.
/// </para>
/// </remarks>
public static class CalendarEndpoints
{
    /// <summary>
    /// The widest window one request may ask for. Long enough for a year's planning read in one
    /// go, short enough that the whole of it can be read and merged without the answer becoming
    /// something nobody looks at.
    /// </summary>
    private const int MaxWindowDays = 400;

    /// <summary>
    /// Backstop on the rows one answer carries. It is not a page size — the window is what bounds
    /// an ordinary answer — and it is not expected to bite; when it does, the answer says how many
    /// rows it could not carry rather than ending quietly.
    /// </summary>
    private const int MaxRows = 2000;

    private const string WindowRequiredCode = "calendar.window_required";
    private const string WindowInvertedCode = "calendar.window_inverted";
    private const string WindowTooWideCode = "calendar.window_too_wide";
    private const string SourceInvalidCode = "calendar.source_invalid";
    private const string StateInvalidCode = "calendar.state_invalid";

    /// <summary>
    /// The lifecycle states that reach a calendar, taken whole from the rule that decides it so
    /// the query narrows on the same answer the rows are marked with. Materialised as an array
    /// because the narrowing runs in the database.
    /// </summary>
    private static readonly ActivityState[] ShownStates = [.. CalendarMembership.ShownStates];

    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/calendar", GetAsync)
            .WithTags("Calendar")
            .WithSummary("The trips and camps a caller may read whose days fall in a window.");
        return api;
    }

    /// <param name="from">The first day of the window, inclusive. Required.</param>
    /// <param name="to">The last day of the window, inclusive. Required.</param>
    /// <param name="source">
    /// Narrows to one family of record. Absent means all of them, which is the point of the
    /// surface.
    /// </param>
    /// <param name="state">
    /// Narrows to one lifecycle state. A state that reaches no calendar is refused rather than
    /// answered with an empty list, because a caller who asked for something that cannot appear
    /// wants to be told.
    /// </param>
    /// <param name="cavingGroupId">
    /// Narrows to one group's calendar — the trips that group is running and the camps it owns.
    /// It is applied on top of the reader's visibility and never instead of it, so it can only
    /// ever narrow what they could already read.
    /// </param>
    /// <param name="mine">
    /// Narrows to the trips the caller is on. It takes no argument naming a person and never
    /// will: who "mine" is comes from the request's own account, so there is no question here
    /// about where somebody else has been.
    /// </param>
    /// <param name="includePast">
    /// When false, the window is narrowed to begin no earlier than today. Days are the rows' own
    /// calendar days and today is read in UTC, which is the only clock this application stores.
    /// </param>
    /// <param name="includeCancelled">
    /// When false, rows called off are left out. They are in by default, and that is a decision:
    /// a trip somebody was going on that has been called off is exactly the row they most need to
    /// find, and a calendar that hid it would quietly disagree with the trip's own page.
    /// </param>
    /// <param name="sort">
    /// One of a fixed set of orders, applied after the merge. Anything else falls back to the
    /// calendar's own order rather than being refused, and every order breaks its ties on the
    /// identifier — rows sharing a day are ordinary, and an order that does not separate them
    /// lets the same row move between answers as the database chooses.
    /// </param>
    private static async Task<Results<Ok<CalendarResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        DateOnly? from,
        DateOnly? to,
        string? source,
        string? state,
        Guid? cavingGroupId,
        bool? mine,
        bool? includePast,
        bool? includeCancelled,
        string? sort,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (from is not { } windowStart || to is not { } windowEnd)
        {
            return ApiProblems.BadRequest(
                WindowRequiredCode, "Both 'from' and 'to' are required.");
        }

        if (windowEnd < windowStart)
        {
            return ApiProblems.BadRequest(
                WindowInvertedCode, "'to' falls before 'from'.");
        }

        // Counted in days rather than in rows, and refused rather than clamped: a caller who
        // asked for five years and silently got fourteen months has an answer that looks whole.
        var days = windowEnd.DayNumber - windowStart.DayNumber + 1;
        if (days > MaxWindowDays)
        {
            return ApiProblems.BadRequest(
                WindowTooWideCode, $"The window may span at most {MaxWindowDays} days.");
        }

        // Enum query parameters arrive as the camelCase words the rest of the contract spells
        // them with, parsed here rather than by route binding: a bad word bound by the framework
        // answers with a bare 400 carrying no code, which a client cannot tell from any other
        // refusal.
        CalendarSource? sourceFilter = null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (!Enum.TryParse<CalendarSource>(source, ignoreCase: true, out var sourceValue)
                || !Enum.IsDefined(sourceValue))
            {
                return ApiProblems.BadRequest(SourceInvalidCode, $"Unknown source '{source}'.");
            }

            sourceFilter = sourceValue;
        }

        ActivityState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<ActivityState>(state, ignoreCase: true, out var stateValue)
                || !Enum.IsDefined(stateValue)
                || !CalendarMembership.ShowsOnCalendar(stateValue))
            {
                return ApiProblems.BadRequest(
                    StateInvalidCode, $"State '{state}' does not appear on a calendar.");
            }

            stateFilter = stateValue;
        }

        var effectiveStart = includePast == false
            ? Max(windowStart, DateOnly.FromDateTime(DateTime.UtcNow))
            : windowStart;

        // Every read below — each source's count and each source's rows — is taken from one
        // snapshot. The shortfall the answer reports is the difference between a count and a
        // page, and two statements outside a transaction see two different states of the
        // database: a row written between them makes that difference smaller than it should be
        // and, once the page overtakes the count, negative. The answer would then either warn a
        // reader about rows that are all present or hand them a nonsensical number, and the
        // shortfall is the field the whole cap exists to make honest. Read-only and at repeatable
        // read, so it takes no locks and blocks nobody.
        await using var snapshot = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);

        var entries = new List<CalendarEntryDto>();
        var found = 0;

        if (sourceFilter is null or CalendarSource.TripLog)
        {
            // The reader's own visibility, unconditionally and first. Everything below narrows
            // what is left of it and nothing below can widen it, which is what keeps a calendar
            // from becoming a second door onto rows their owner's page would not open.
            var trips = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

            // The membership rule sits here, after that walk, and it is a display rule: it
            // decides what a calendar shows, never what anybody may read. A draft is kept off
            // the calendar and stays exactly as readable as it was — on its own page, on the
            // trip list, in search — because who may read a row is settled by its visibility and
            // its access entries alone, and a second rule answering that question is how the two
            // come to disagree.
            trips = trips.Where(x => ShownStates.Contains(x.State));

            trips = trips.OverlappingDays(x => x.TripDate, x => x.TripDateEnd, effectiveStart, windowEnd);

            if (stateFilter is { } wantedTripState)
            {
                trips = trips.Where(x => x.State == wantedTripState);
            }

            if (includeCancelled == false)
            {
                trips = trips.Where(x => x.State != ActivityState.Cancelled);
            }

            // A group's calendar means the trips that group is running, which is what the
            // organising column records — not the audience the trip is bound to.
            if (cavingGroupId is { } tripGroup)
            {
                trips = trips.Where(x => x.OrganizingCavingGroupId == tripGroup);
            }

            if (mine == true)
            {
                var onThese = TripAudience.TripIdsTheAccountIsOn(db, user.UserId);
                trips = trips.Where(x => onThese.Contains(x.Id));
            }

            // Counted over the narrowed, already-visibility-filtered query and nothing wider, so
            // the figure describes this caller's answer rather than the table behind it. A count
            // taken any earlier would tell an outsider how much they cannot see.
            found += await trips.CountAsync(ct);

            var tripRows = await trips
                .OrderBy(x => x.TripDate).ThenBy(x => x.Id)
                .Take(MaxRows)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    Start = x.TripDate,
                    End = x.TripDateEnd,
                    StartTime = x.EntryTime,
                    EndTime = x.ExitTime,
                    x.State,
                    GroupId = x.OrganizingCavingGroupId,
                    HasPosition = x.Geom != null || x.MeetingGeom != null,
                })
                .ToListAsync(ct);

            entries.AddRange(tripRows.Select(x => new CalendarEntryDto(
                CalendarSource.TripLog,
                x.Id,
                x.Title,
                x.Start,
                x.End,
                x.StartTime,
                x.EndTime,
                x.State,
                CalendarMembership.PlacementOf(x.State),
                x.GroupId,
                x.HasPosition)));
        }

        // A camp is a source of its own rather than something derived from the trips inside it:
        // a camp exists, and is planned around, before any of its trips does.
        if (sourceFilter is null or CalendarSource.Expedition)
        {
            var camps = db.Expeditions.AsNoTracking().VisibleTo(ctx, AccessDomain.Expeditions);

            camps = camps.Where(x => ShownStates.Contains(x.State));

            camps = camps.OverlappingDays(x => x.StartDate, x => x.EndDate, effectiveStart, windowEnd);

            if (stateFilter is { } wantedCampState)
            {
                camps = camps.Where(x => x.State == wantedCampState);
            }

            if (includeCancelled == false)
            {
                camps = camps.Where(x => x.State != ActivityState.Cancelled);
            }

            // A camp carries one group column, which is both whose camp it is and who it is bound
            // to, so the group calendar asks that one. Mildly asymmetric with a trip, and said
            // rather than smoothed over.
            if (cavingGroupId is { } campGroup)
            {
                camps = camps.Where(x => x.CavingGroupId == campGroup);
            }

            // Nothing records who is on a camp, so there is nothing here for "mine" to mean and
            // the source contributes nothing rather than guessing from the trips inside it.
            if (mine == true)
            {
                camps = camps.Where(_ => false);
            }

            found += await camps.CountAsync(ct);

            var campRows = await camps
                .OrderBy(x => x.StartDate).ThenBy(x => x.Id)
                .Take(MaxRows)
                .Select(x => new
                {
                    x.Id,
                    Title = x.Name,
                    Start = x.StartDate,
                    End = x.EndDate,
                    x.State,
                    GroupId = x.CavingGroupId,
                    HasPosition = x.Geom != null,
                })
                .ToListAsync(ct);

            entries.AddRange(campRows.Select(x => new CalendarEntryDto(
                CalendarSource.Expedition,
                x.Id,
                x.Title,
                x.Start,
                x.End,
                null,
                null,
                x.State,
                CalendarMembership.PlacementOf(x.State),
                x.GroupId,
                x.HasPosition)));
        }

        var answer = CalendarWindow.Merge(entries, found, MaxRows, sort);
        await snapshot.CommitAsync(ct);
        return TypedResults.Ok(answer);
    }

    private static DateOnly Max(DateOnly first, DateOnly second) => first > second ? first : second;
}
