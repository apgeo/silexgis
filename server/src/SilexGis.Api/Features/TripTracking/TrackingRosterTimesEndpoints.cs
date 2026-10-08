// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Entry and exit times for a trip's roster, taken from its tracking log — as a proposal that
/// somebody reviews, and then as one write of exactly what they ticked.
/// </summary>
/// <remarks>
/// <para>
/// A followed trip ends with two records of when each person was underground: the log, which has
/// the moments somebody reported, and the roster, which has whatever was typed on the trip form —
/// usually nothing. The log is the better source and the roster is what every total reads, so the
/// times are offered across. They are never moved silently: a time on a roster is something a
/// person wrote, and the log can be wrong in ways only a person can see (a report filed late, an
/// exit nobody recorded).
/// </para>
/// <para>
/// <b>This writes the roster, not the log</b>, so the gate is the right to write the trip and
/// nothing about the watch's state: it is most useful on a watch that has been closed, and it
/// works on one still running for whoever has already come out.
/// </para>
/// <para>
/// <b>Whose clocks.</b> A roster time is a reading of a clock with no zone attached; a report is
/// an instant. Turning one into the other needs a zone, and the server does not have one to
/// assume — it is named by the caller, resolved by the same rule that reads a sheet's times, and
/// echoed back so the screen can state it. Nothing about the choice is stored.
/// </para>
/// </remarks>
public static class TrackingRosterTimesEndpoints
{
    private const string ZoneUnknown =
        "Name the time zone the roster's times are kept in, as an IANA zone this server knows, such as 'Europe/Bucharest' or 'UTC'.";

    /// <summary>
    /// The proposal: per person, what the log says, what the roster holds, and whether taking
    /// one for the other would overwrite something typed.
    /// </summary>
    /// <remarks>
    /// Answers with the trip's version, so the write that follows a review is refused when the
    /// trip itself was saved in between. That version does not move for a roster row changed on
    /// its own, which is why the write also repeats what each person's rows were shown to hold.
    /// </remarks>
    internal static async Task<Results<Ok<TrackingRosterTimesDto>, ProblemHttpResult>> PreviewAsync(
        Guid tripLogId, string? timeZone, HttpContext http, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        if (!TrackingCsvZones.TryFind(timeZone, out var zone))
        {
            return ApiProblems.BadRequest(TrackingProblemCodes.RosterTimesZoneUnknown, ZoneUnknown);
        }

        var plan = await PlanAsync(db, trip!, zone, tracked: false, ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, tripLogId, ct);
        return TypedResults.Ok(ToDto(plan, ZoneAsAsked(timeZone!, zone)));
    }

    /// <summary>
    /// Writes the reviewed times of the people named to every roster row of each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every row of the person.</b> The roster is one row per job, and the hours a person is
    /// credited with are read from the earliest entry and the latest exit across their rows. A
    /// write to one row would leave the other holding the old time, and the total would go on
    /// following whichever was wider.
    /// </para>
    /// <para>
    /// <b>The trip's own row is stamped, so its version moves.</b> The roster is rewritten whole
    /// by the trip's form, times included, and that write's precondition is the trip's version.
    /// Left where it was, somebody who opened the form before this ran could save it afterwards,
    /// pass the precondition, and put back every time this replaced — with nobody told.
    /// </para>
    /// <para>
    /// The changed rows land on the trip's history by themselves, each with its old and new
    /// times. One line more is written on the trip's own trail saying where the times came from
    /// and on whose clocks, because a row's history shows that a time changed and cannot show
    /// that nobody typed it.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<TrackingRosterTimesTakenDto>, ProblemHttpResult>> TakeAsync(
        Guid tripLogId, TrackingRosterTimesTakeRequest request, HttpContext http, SilexGisDbContext db,
        IAccessService access, IAccessContextAccessor accessAccessor, IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null || user is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, tripLogId, ct, required: true) is { } stale)
        {
            return stale;
        }

        if (!TrackingCsvZones.TryFind(request.TimeZone, out var zone))
        {
            return ApiProblems.BadRequest(TrackingProblemCodes.RosterTimesZoneUnknown, ZoneUnknown);
        }

        var plan = await PlanAsync(db, trip!, zone, tracked: true, ct);
        var byCaver = plan.ToDictionary(p => p.CaverId);

        var people = 0;
        var rows = 0;
        foreach (var asked in request.People!)
        {
            // Refused whole, before anything is written, when one of the people is no longer as
            // reviewed: gone from the roster, no longer something the roster can hold, or holding
            // different moments on the log. Writing the rest would leave the reviewer with a
            // result that is neither what they ticked nor what they would tick now.
            if (!byCaver.TryGetValue(asked.CaverId!.Value, out var person)
                || person.Problem is not null
                || !SameMinute(person.Times.Entry, asked.Entry)
                || !SameMinute(person.Times.Exit, asked.Exit))
            {
                return ApiProblems.Conflict(TrackingProblemCodes.RosterTimesChanged,
                    "What the log says about one of these people is no longer what was reviewed. Read the times again.");
            }

            // The same for the roster's side of the review. The trip's version covers a save of
            // the trip's form, which moves it whenever it changes a roster row; it does not cover
            // a writer that holds the version of now beside a review read earlier, and it says
            // nothing about which rows the reviewer was shown. So the request repeats what the
            // roster was shown to hold, and a time typed since — one the reviewer was never shown
            // as about to be replaced — is a refusal rather than a quiet overwrite.
            if (!SameMinute(person.CurrentEntry, asked.CurrentEntry)
                || !SameMinute(person.CurrentExit, asked.CurrentExit)
                || person.Overwrites != asked.Overwrites!.Value)
            {
                return ApiProblems.Conflict(TrackingProblemCodes.RosterTimesChanged,
                    "What the roster holds for one of these people is no longer what was reviewed. Read the times again.");
            }

            var changed = 0;
            foreach (var row in person.Rows)
            {
                if (SameMinute(row.EntryTime, person.Times.Entry) && SameMinute(row.ExitTime, person.Times.Exit))
                {
                    continue;
                }

                row.EntryTime = person.Times.Entry;
                row.ExitTime = person.Times.Exit;
                changed++;
            }

            rows += changed;
            if (changed > 0) people++;
        }

        if (rows > 0)
        {
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = user!.UserId,
                Action = AuditActions.Updated,
                EntityType = nameof(TripLog),
                EntityId = tripLogId.ToString(),
                // What the act was, with no "before" beside any of it: these are not fields of
                // the trip that held an earlier value, and a prior value written here would be
                // read as an offer to put one back. The times themselves, old and new, are on
                // the roster rows' own entries.
                Changes = JsonSerializer.Serialize(
                    new Dictionary<string, Dictionary<string, object?>>
                    {
                        ["RosterTimes"] = new() { ["new"] = "fromTracking" },
                        ["TimeZone"] = new() { ["new"] = ZoneAsAsked(request.TimeZone!, zone) },
                        ["People"] = new() { ["new"] = people },
                        ["Rows"] = new() { ["new"] = rows },
                    }),
            });

            // One transaction for the times and the stamp: times written under a version that
            // did not move are exactly the state the stamp exists to prevent.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await db.TripLogs
                .Where(x => x.Id == tripLogId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
            await transaction.CommitAsync(ct);
        }

        var after = await PlanAsync(db, trip!, zone, tracked: false, ct);
        return TypedResults.Ok(new TrackingRosterTimesTakenDto(
            people, rows, ToDto(after, ZoneAsAsked(request.TimeZone!, zone))));
    }

    // ---- the proposal --------------------------------------------------------------------

    /// <summary>One person of the proposal, with the roster rows a write would go to.</summary>
    private sealed record Person(
        Guid CaverId,
        IReadOnlyList<TripLogParticipant> Rows,
        TrackingEntryExit Watch,
        TrackingRosterTimes Times)
    {
        public bool OnRoster => Rows.Count > 0;

        /// <summary>The entry time the roster holds for them: the earliest across their rows.</summary>
        public TimeOnly? CurrentEntry => Rows.Min(r => r.EntryTime);

        /// <summary>The exit time the roster holds for them: the latest across their rows.</summary>
        public TimeOnly? CurrentExit => Rows.Max(r => r.ExitTime);

        /// <summary>
        /// Whether taking the log's times would replace a time already on one of their rows with
        /// a different one. A row with no time of its own is filled, not overwritten.
        /// </summary>
        public bool Overwrites => Problem is null && Rows.Any(r =>
            (r.EntryTime is not null && !SameMinute(r.EntryTime, Times.Entry))
            || (r.ExitTime is not null && !SameMinute(r.ExitTime, Times.Exit)));

        /// <summary>
        /// Why nothing can be taken for them. Having no row comes first: it is the reason no
        /// tick can be offered whatever the log says, and the one a person can do something
        /// about on the trip's own form.
        /// </summary>
        public TrackingRosterTimesProblemKind? Problem => !OnRoster
            ? TrackingRosterTimesProblemKind.NotOnRoster
            : Times.Problem switch
            {
                null => null,
                TrackingRosterTimesProblem.NoEntry => TrackingRosterTimesProblemKind.NoEntry,
                TrackingRosterTimesProblem.NoExit => TrackingRosterTimesProblemKind.NoExit,
                TrackingRosterTimesProblem.ExitBeforeEntry => TrackingRosterTimesProblemKind.ExitBeforeEntry,
                TrackingRosterTimesProblem.EntryOffTripDate => TrackingRosterTimesProblemKind.EntryOffTripDate,
                TrackingRosterTimesProblem.ExitOffTripDate => TrackingRosterTimesProblemKind.ExitOffTripDate,
                TrackingRosterTimesProblem.ClockChanged => TrackingRosterTimesProblemKind.ClockChanged,
                _ => throw new InvalidOperationException($"No wire name for {Times.Problem}."),
            };
    }

    /// <summary>
    /// Everybody on the roster in the party's order, then everybody the log speaks of who is not
    /// on it, each with what the log says about their coming and going.
    /// </summary>
    /// <remarks>
    /// Both reads go through the model's ordinary filters, so a report taken off the log says
    /// nothing here — it is kept so that it can be put back, not so that it can be counted.
    /// </remarks>
    private static async Task<List<Person>> PlanAsync(
        SilexGisDbContext db, TripLog trip, TimeZoneInfo zone, bool tracked, CancellationToken ct)
    {
        var roster = db.TripLogParticipants.Where(p => p.TripLogId == trip.Id);
        var rows = (await (tracked ? roster : roster.AsNoTracking()).OrderBy(p => p.Id).ToListAsync(ct))
            .ToLookup(p => p.CaverId);
        var party = await TripTrackingPublicationEndpoints.RosterOrderAsync(db, trip.Id, ct);

        // The log's own order — by the instant a report speaks of, then by when it was written —
        // which is the order the reading of entries and exits is defined over.
        var reports = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip.Id)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { e.CaverId, e.Kind, e.RecordedAt })
            .ToListAsync(ct);
        var passages = reports.ToLookup(r => r.CaverId, r => new TrackingPassage(r.Kind, r.RecordedAt));

        var listed = party.Roster.Select(place => place.CaverId).Where(rows.Contains).ToList();
        // A roster row the party's order does not know cannot exist, but a person silently left
        // out of a review is the one failure this screen must not have: anybody with a row is in.
        listed.AddRange(rows.Select(g => g.Key).Except(listed).ToList());
        var mentioned = reports.Select(r => r.CaverId).Distinct().Where(id => !rows.Contains(id));

        return listed.Concat(mentioned)
            .Select(caverId =>
            {
                var watch = TrackingAttendance.EntryExitOf(passages[caverId]);
                return new Person(
                    caverId,
                    [.. rows[caverId]],
                    watch,
                    TrackingAttendance.RosterTimesOf(watch, trip.TripDate, trip.TripDateEnd, zone));
            })
            .ToList();
    }

    private static TrackingRosterTimesDto ToDto(IReadOnlyList<Person> plan, string zoneName) =>
        new(zoneName, plan.Select(person =>
        {
            var takeable = person.Problem is null;
            return new TrackingRosterTimesPersonDto(
                person.CaverId,
                person.OnRoster,
                person.Watch.Entered,
                person.Watch.Exited,
                person.Watch.Stays,
                person.Times.Entry,
                person.Times.Exit,
                person.CurrentEntry,
                person.CurrentExit,
                Changes: takeable && person.Rows.Any(r =>
                    !SameMinute(r.EntryTime, person.Times.Entry) || !SameMinute(r.ExitTime, person.Times.Exit)),
                person.Overwrites,
                person.Problem);
        }).ToList());

    /// <summary>
    /// Whether two roster times are the same reading. To the minute, as the roster's own
    /// arithmetic reads them: a stored time that happens to carry seconds is not a different time.
    /// </summary>
    private static bool SameMinute(TimeOnly? a, TimeOnly? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.Value.Hour == b.Value.Hour && a.Value.Minute == b.Value.Minute;

    /// <summary>
    /// The zone under the name the caller chose it by — the spelling their screen can format
    /// with, which for a respelled place may not be the one this host carries.
    /// </summary>
    private static string ZoneAsAsked(string asked, TimeZoneInfo zone) =>
        ReferenceEquals(zone, TimeZoneInfo.Utc) ? zone.Id : asked;
}
