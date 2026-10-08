// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// A camp's surface log: who is underground on the camp's trips, answered once for the whole camp.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is one answer.</b> A camp runs several parties on the same day and the person at the
/// surface has one question. The per-trip watch answers it for one party at a time, so a
/// coordinator kept a tab open per trip and added the numbers up by eye. This is the sum, decided
/// here rather than assembled by a page out of as many requests as the camp has trips.
/// </para>
/// <para>
/// <b>Who may read what follows the rules that already exist; this route adds none.</b> The camp
/// by the camp's own read rule, and a camp the caller may not read answers as one that is not
/// there. Each trip by the trip's own read rule, composed into the query rather than filtered
/// afterwards, so a member trip the caller may not open is simply absent — it moves no count and
/// leaves no gap to notice. Names as the trip's own page gives them to this caller. Every row is
/// therefore a trip the caller could already open, saying what that trip's own watch would say:
/// nothing is told here that was not already told there.
/// </para>
/// <para>
/// <b>And it carries no place.</b> No station, depth, survey or cave is read from the database
/// for this answer, let alone sent, so there is nothing for location protection to withhold and
/// no branch that could forget to. A party in a cave whose position the reader may not be told
/// is counted like any other, which is what the trip's own watch does for the same reader.
/// </para>
/// <para>
/// <b>It is not the callout.</b> The hour a party planned to be out by is passed along as the
/// time the trip already tells its readers. Nothing here compares it with the clock, and nothing
/// of the callout's own state travels: a watch records, and it is the callout that raises alarms.
/// </para>
/// <para>
/// It lives beside the trip's watch rather than with the camp's other routes because everything
/// it computes is the watch's: the standing rule, the party, the log. The camp contributes one
/// question — which trips are its own.
/// </para>
/// </remarks>
public static class ExpeditionSurfaceLogEndpoints
{
    // The camp's own refusal, spelled here because it is a wire contract rather than something
    // the camp's handlers own: a caller told a camp is not there must be told so in the same
    // words at whichever door they knocked.
    private const string ExpeditionNotFoundCode = "expedition.not_found";

    public static RouteGroupBuilder MapExpeditionSurfaceLogEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/expeditions").WithTags("TripTracking")
            .MapGet("/{id:guid}/surface-log", GetAsync)
            .WithSummary(
                "Who is underground on one camp's trips: every member trip this caller may read "
                + "whose watch is armed or was closed recently, with its party counted in, out and "
                + "not heard from. No station, depth, survey or cave is on this answer.");

        return api;
    }

    private static async Task<Results<Ok<ExpeditionSurfaceLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptions<ExpeditionSurfaceLogOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var camp = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (camp is null || !(await access.DecideAsync(ctx, AccessAction.Read, camp, ct)).Allowed)
        {
            // One answer for a camp that is not there and a camp that is not the caller's: an
            // address that told the two apart could be asked which camps exist.
            return ApiProblems.NotFound(ExpeditionNotFoundCode);
        }

        var now = clock.GetUtcNow();
        var cap = options.Value.MaxRows;

        // The camp's trips out of the trips this caller may read, each with its watch. A trip
        // with no watch row has never been followed and drops out at the join.
        //
        // Read through the ordinary trip set and never around it, so that whatever takes a trip
        // away from its readers — its visibility today, anything else tomorrow — takes it off
        // this log by the same act, with nothing here to remember.
        //
        // The order does the capping's work and is what lets the rule stay in one place: running
        // watches, then closed ones that were once armed from the most recently closed, then
        // anything else — a watch that is off, and one filed as closed without ever having run,
        // which is what importing a device's recording writes. The last group holds nothing the
        // log lists, and it must stay last: a never-armed row sorted among the closed ones would
        // sit ahead of genuinely closed watches and could push one past the cap. Which of
        // them the log lists is decided below by the rule itself, not by a second spelling of it
        // in the query — and because the listed ones are always a leading run of this order, one
        // row more than the cap is enough to list them exactly and to know whether more exist.
        var memberTripIds = db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == id)
            .Select(m => m.TripLogId);
        var candidates = await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(trip => memberTripIds.Contains(trip.Id))
            .Join(
                db.TripTrackings.AsNoTracking(),
                trip => trip.Id,
                tracking => tracking.TripLogId,
                (trip, tracking) => new
                {
                    trip.Id,
                    trip.Title,
                    trip.TripDate,
                    trip.TripDateEnd,
                    trip.ExpectedReturnAt,
                    tracking.State,
                    tracking.ArmedAt,
                    tracking.ClosedAt,
                })
            .OrderBy(x =>
                x.State == TripTrackingState.Armed ? 0
                : x.State == TripTrackingState.Closed && x.ClosedAt != null && x.ArmedAt != null ? 1
                : 2)
            .ThenByDescending(x => x.State == TripTrackingState.Armed ? x.ArmedAt : x.ClosedAt)
            .ThenBy(x => x.Id)
            .Take(cap + 1)
            .ToListAsync(ct);

        var listed = candidates
            .Where(x => TripSurfaceLog.Lists(
                now, x.State, x.ArmedAt, x.ClosedAt, options.Value.RecentlyClosed))
            .ToList();
        var truncated = listed.Count > cap;
        var rows = listed.Take(cap).ToList();
        if (rows.Count == 0)
        {
            return TypedResults.Ok(new ExpeditionSurfaceLogDto { Trips = [], Truncated = truncated });
        }

        // From here on every read is keyed by the ids the query above answered — trips this
        // caller may read, of this camp — and each is one query for all of them, so the cost of
        // the log does not grow with the number of trips on it.
        var tripIds = rows.Select(x => x.Id).ToList();

        var rosterRows = await db.TripLogParticipants.AsNoTracking()
            .Where(p => tripIds.Contains(p.TripLogId))
            .Select(p => new { p.TripLogId, p.CaverId })
            .Distinct()
            .ToListAsync(ct);

        // Only what the count needs of a report: whose it is, what kind, and when. The place a
        // report names, the survey it was measured in, the cave, the team and the note are not
        // selected, so they are not in this process to be sent by mistake. Oldest first, in the
        // order the trip's own watch folds them.
        var reportRows = await db.TripPositionEvents.AsNoTracking()
            .Where(e => tripIds.Contains(e.TripLogId))
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { e.TripLogId, e.CaverId, e.Kind, e.RecordedAt })
            .ToListAsync(ct);

        // The name the trip's own page gives this caller for each person, from the one place
        // that decides it — an account's chosen label where there is an account, the roster's
        // name otherwise, and never an address.
        var user = await userAccessor.GetAsync(ct);
        var names = await CaverDirectory.ResolveLabelsAsync(
            db, user, rosterRows.Select(p => p.CaverId), ct);

        // Rebuilt as reports carrying only their kind and their time, because the standing rule
        // is asked of reports and those two are all it — and "last heard" — read. One rule for
        // this log and for the trip's own watch, rather than a second fold that could disagree
        // with it about who is still inside.
        var reportsByMember = reportRows
            .GroupBy(e => (e.TripLogId, e.CaverId))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new TripPositionEvent
                {
                    TripLogId = e.TripLogId,
                    CaverId = e.CaverId,
                    Kind = e.Kind,
                    RecordedAt = e.RecordedAt,
                }).ToList());
        var rosterByTrip = rosterRows.ToLookup(p => p.TripLogId, p => p.CaverId);

        var trips = new List<ExpeditionSurfaceLogTripDto>(rows.Count);
        foreach (var row in rows)
        {
            var party = new List<ExpeditionSurfaceLogPersonDto>();
            var underground = 0;
            var outOfCave = 0;
            var unheard = 0;
            DateTimeOffset? lastHeard = null;
            foreach (var caverId in PartyOf(rosterByTrip[row.Id]))
            {
                reportsByMember.TryGetValue((row.Id, caverId), out var own);
                var standing = TripTrackingRules.StandingOf(own);
                switch (standing)
                {
                    case TripStanding.Underground: underground++; break;
                    case TripStanding.Out: outOfCave++; break;
                    default: unheard++; break;
                }

                // The latest report of any kind. A note moves nobody inside or out, and it is
                // still word from them — the two are told separately for that reason.
                DateTimeOffset? last = own is { Count: > 0 } ? own[^1].RecordedAt : null;
                if (last is not null && (lastHeard is null || last > lastHeard)) lastHeard = last;

                party.Add(new ExpeditionSurfaceLogPersonDto
                {
                    CaverId = caverId,
                    Name = names.GetValueOrDefault(caverId) ?? string.Empty,
                    In = standing == TripStanding.Underground,
                    Out = standing == TripStanding.Out,
                    LastRecordedAt = last,
                });
            }

            trips.Add(new ExpeditionSurfaceLogTripDto
            {
                TripLogId = row.Id,
                Title = row.Title,
                TripDate = row.TripDate,
                TripDateEnd = row.TripDateEnd,
                State = row.State,
                ArmedAt = row.ArmedAt,
                ClosedAt = row.ClosedAt,
                ExpectedReturnAt = row.ExpectedReturnAt,
                Underground = underground,
                Out = outOfCave,
                Unheard = unheard,
                LastRecordedAt = lastHeard,
                Party = party,
            });
        }

        return TypedResults.Ok(new ExpeditionSurfaceLogDto { Trips = trips, Truncated = truncated });
    }

    /// <summary>
    /// Who a trip's party is, for this log: the people on the trip's list, in a stable order.
    /// </summary>
    /// <remarks>
    /// The one place this read decides it, on purpose. The trip's own watch lists the same people
    /// today, and a coordinator must never find one head count on the camp's log and another on
    /// the trip — so if the watch comes to list anybody else (somebody with reports who was later
    /// taken off the list), this is the single line that has to follow it, and the rows above
    /// already key reports by person so nothing else moves.
    /// </remarks>
    private static IEnumerable<Guid> PartyOf(IEnumerable<Guid> roster) => roster.Order();
}
