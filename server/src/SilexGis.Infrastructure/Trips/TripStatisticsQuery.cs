// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// The one place a trip total is worked out. Every surface that shows a figure — a person's page,
/// a cave's, a club's, a camp's, and the file any of them is saved as — asks here, so a number
/// saved to disk and the number on the screen above it cannot come to disagree: a second query
/// written for a second surface is how a report ends up answering a question the screen refuses.
/// </summary>
/// <remarks>
/// It lives below the surfaces that call it, and it has to: more than one of them adds up the same
/// trips, and the alternative to a shared home is either one surface reaching into another's
/// internals or a second copy of the arithmetic. A second copy is how a page comes to state a
/// figure another page declines to give — and the traps below are exactly the kind that a copy
/// reproduces incorrectly and silently.
///
/// The caller's visibility walk is composed into the statements rather than applied to their
/// results. That is the whole design: a count taken past the walk would state how many rows the
/// caller was not shown, and repeated over enough subjects it reconstructs them.
///
/// Two shapes of trap this avoids, both of which fail quietly rather than loudly:
/// the roster holds one row per person per job, so every count of people is distinct by person;
/// and a role link names a feature once per link and per role, so every count of trips or places
/// reduces the pairs before counting them.
/// </remarks>
public static class TripStatisticsQuery
{
    /// <summary>
    /// Adds up the trips a scope selects, out of the trips this caller may read.
    /// </summary>
    /// <param name="scope">Which of the readable trips belong to the subject being asked about.</param>
    /// <param name="subjectCaverId">
    /// Set when the subject is one person: their hours are theirs alone, and a first visit is
    /// theirs to a place rather than anybody's.
    /// </param>
    /// <param name="subjectCaveId">
    /// Set when the subject is one cave: the places and the first visits are then about that cave
    /// and no other. A trip commonly names more than one, so counting every place its trips reached
    /// would put another cave's figures on this cave's page — "two places reached, two first
    /// visits" printed beside a single name.
    /// </param>
    public static async Task<TripStatisticsDto> ComputeAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        Expression<Func<TripLog, bool>> scope,
        Guid? subjectCaverId,
        Guid? subjectCaveId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(protection);

        var visible = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);
        var scoped = visible.Where(scope);
        var scopedIds = scoped.Select(t => t.Id);

        // One grouped pass for every figure that is a property of the trips themselves. No rows
        // means no group, which is the honest answer for a subject with nothing readable on it.
        var totals = await scoped
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Trips = g.Count(),
                Incidents = g.Sum(t => t.HadIncident ? 1 : 0),
                LengthSurveyedM = g.Sum(t => t.LengthSurveyedM) ?? 0m,
                RopeMetresM = g.Sum(t => t.RopeMetres) ?? 0m,
                SurveyStations = g.Sum(t => t.SurveyStations) ?? 0,
                Earliest = g.Min(t => (DateOnly?)t.TripDate),
                Latest = g.Max(t => (DateOnly?)(t.TripDateEnd ?? t.TripDate)),
            })
            .FirstOrDefaultAsync(ct);
        if (totals is null)
        {
            return TripStatisticsDto.Empty;
        }

        // Distinct by person, not by row: the roster holds one row per job.
        var people = await db.TripLogParticipants.AsNoTracking()
            .Where(p => scopedIds.Contains(p.TripLogId))
            .Select(p => p.CaverId)
            .Distinct()
            .ToListAsync(ct);

        var (undergroundMinutes, personTrips, timedPersonTrips) =
            await HoursAsync(db, scoped, subjectCaverId, ct);
        var placeIds = await PlaceIdsAsync(db, protection, ctx, scopedIds, subjectCaveId, ct);
        var photographs = await PhotographsAsync(db, ctx, scopedIds, ct);
        var firstVisits = await FirstVisitsAsync(
            db,
            visible,
            await scopedIds.ToListAsync(ct),
            placeIds,
            subjectCaverId is { } only ? [only] : people,
            ct);

        return new TripStatisticsDto(
            totals.Trips,
            people.Count,
            placeIds.Count,
            firstVisits,
            totals.Incidents,
            undergroundMinutes,
            personTrips,
            timedPersonTrips,
            totals.LengthSurveyedM,
            totals.RopeMetresM,
            totals.SurveyStations,
            totals.Earliest,
            totals.Latest,
            photographs);
    }

    /// <summary>
    /// Person-hours, summed over one row per person per trip. The roster is reduced to that shape
    /// in the database first: somebody who was the leader and the surveyor has two rows on one
    /// trip, and summing the rows would give them their hours twice over.
    /// </summary>
    /// <remarks>
    /// A person's own times stand where their row gives them and the trip's stand otherwise, per
    /// time rather than per pair — a row saying only when somebody came out means they went in
    /// with everybody else. Where a person holds several jobs and the rows disagree, the widest
    /// reading wins: the earliest entry any of them claims and the latest exit, because a person
    /// was underground from the first moment any row says they were.
    ///
    /// The arithmetic runs over the grouped rows rather than in SQL. What it needs — a calendar
    /// day count and a midnight rule that applies only to a trip recorded as lasting one day — has
    /// one home shared with every other surface that states a duration, and a second expression of
    /// it in SQL is a second rule to keep in step.
    /// </remarks>
    private static async Task<(int Minutes, int PersonTrips, int Timed)> HoursAsync(
        SilexGisDbContext db,
        IQueryable<TripLog> scoped,
        Guid? subjectCaverId,
        CancellationToken ct)
    {
        var roster = db.TripLogParticipants.AsNoTracking();
        if (subjectCaverId is { } subject)
        {
            roster = roster.Where(p => p.CaverId == subject);
        }

        var spans = await (from participant in roster
                           join trip in scoped on participant.TripLogId equals trip.Id
                           group new { participant, trip } by new { participant.TripLogId, participant.CaverId }
                           into g
                           select new
                           {
                               TripDate = g.Min(x => x.trip.TripDate),
                               TripDateEnd = g.Min(x => x.trip.TripDateEnd),
                               Entry = g.Min(x => x.participant.EntryTime ?? x.trip.EntryTime),
                               Exit = g.Max(x => x.participant.ExitTime ?? x.trip.ExitTime),
                           })
            .ToListAsync(ct);

        var minutes = 0;
        var timed = 0;
        foreach (var span in spans)
        {
            if (TripDuration.UndergroundMinutes(span.TripDate, span.TripDateEnd, span.Entry, span.Exit)
                is { } counted)
            {
                minutes += counted;
                timed++;
            }
        }

        return (minutes, spans.Count, timed);
    }

    /// <summary>
    /// The places these trips name, reduced to the ones this caller may both read and locate. Both
    /// halves are needed: a trip hands back only the cave links whose position the caller may know,
    /// so a place counted here that is missing from those links would be a number the visible page
    /// cannot account for — and the count of it, repeated across subjects, is the position.
    /// </summary>
    /// <remarks>
    /// A place is a cave, matching every other trip surface that says "places": a trip role names
    /// any linkable target, and a trip is routinely tagged with the work area it fell in as well as
    /// the cave it entered. Counting those alongside would report two places and two first visits
    /// for one first visit to one cave, and would contradict the trip's own page, which lists its
    /// caves and nothing else.
    ///
    /// When the subject is itself a cave, that cave is the whole set. It still goes through the two
    /// gates below rather than being trusted for having been asked about — one gate, used the same
    /// way everywhere, is what keeps a second path from acquiring a different answer.
    /// </remarks>
    private static async Task<List<Guid>> PlaceIdsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IQueryable<Guid> scopedIds,
        Guid? subjectCaveId,
        CancellationToken ct)
    {
        List<Guid> named;
        if (subjectCaveId is { } only)
        {
            named = [only];
        }
        else
        {
            named = await TripRoleLinks.FeatureIdsNamedIn(db, scopedIds)
                .Distinct()
                .ToListAsync(ct);
        }

        if (named.Count == 0)
        {
            return [];
        }

        // The feature walk is a second question about a second kind of row, and the location rule
        // after it cannot be expressed in SQL at all, so this half is asked over the ids the trips
        // named rather than composed into their statement. The list is the caves a subject has
        // been to, which is why that is affordable — and the trips it came from were already cut
        // to what the caller may read, so nothing here widens anything.
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => named.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, readable, ct);
        return [.. readable.Where(id => !redacted.Contains(id))];
    }

    /// <summary>
    /// How many pictures hang on these trips, out of the pictures this caller may see.
    /// </summary>
    /// <remarks>
    /// Narrowed from the ordinary photograph read rule rather than assembled beside it, so the
    /// figure counts exactly what the galleries on those trips would show this caller. A count
    /// taken over the pins instead would state how many pictures were being withheld — the same
    /// disclosure every other figure here is composed to avoid.
    ///
    /// Photographs, not pins: one picture hanging on two of the trips in scope is one picture, and
    /// counting the pins would make a camp's figure grow by re-pinning rather than by photography.
    /// The read rule resolves reach-through-an-attachment before it can be composed on, so this is
    /// a statement of its own rather than a term of the grouped pass above.
    ///
    /// The trips are handed to the read rule rather than applied to its result, so the part of that
    /// rule which cannot be composed into a query is asked about the pictures on these trips rather
    /// than about every picture in the installation. Every page here would otherwise pay the same
    /// whole-registry walk whatever its subject was worth.
    /// </remarks>
    private static async Task<int> PhotographsAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IQueryable<Guid> scopedIds,
        CancellationToken ct)
    {
        var photographs = await PhotographReads.VisiblePhotographsOnTripsAsync(db, ctx, scopedIds, ct);
        return await photographs.CountAsync(ct);
    }

    /// <summary>
    /// How many of the (person, place) pairs this scope holds were that person's first time there.
    /// </summary>
    /// <remarks>
    /// Earliest wins, and "earliest" is judged over every trip the caller may read rather than over
    /// the scope alone — otherwise a club would be credited with a first visit for somebody who had
    /// been there years earlier with another club, and the credit would never be given up. Which is
    /// also why it can never be a stored flag: the older trip is usually typed up afterwards.
    ///
    /// Two trips on the same day are both firsts. There is nothing finer than a date to separate
    /// them by, and inventing an order out of when the rows were entered would make the answer
    /// depend on the sequence somebody digitised an archive in.
    ///
    /// Both reads are narrowed before they run — to the places this subject actually reached and to
    /// the people this subject is about — so the sweep over every readable trip is a sweep over the
    /// subject's own history rather than over the registry. The two are then matched up here rather
    /// than in one statement because the naming of a place and the roster of a trip meet through a
    /// link table, and a database join across it cannot be expressed over the shared query that
    /// knows which links are trip roles.
    /// </remarks>
    private static async Task<int> FirstVisitsAsync(
        SilexGisDbContext db,
        IQueryable<TripLog> visible,
        IReadOnlyCollection<Guid> scopedTripIds,
        IReadOnlyCollection<Guid> placeIds,
        IReadOnlyCollection<Guid> caverIds,
        CancellationToken ct)
    {
        if (placeIds.Count == 0 || caverIds.Count == 0)
        {
            return 0;
        }

        var namings = await TripRoleLinks.PairsIn(db, visible.Select(t => t.Id), placeIds)
            .Distinct()
            .ToListAsync(ct);
        if (namings.Count == 0)
        {
            return 0;
        }

        var tripIds = namings.Select(n => n.TripId).Distinct().ToList();
        var attendances = await (from participant in db.TripLogParticipants.AsNoTracking()
                                 join trip in visible on participant.TripLogId equals trip.Id
                                 where tripIds.Contains(trip.Id) && caverIds.Contains(participant.CaverId)
                                 select new { participant.CaverId, TripId = trip.Id, trip.TripDate })
            .Distinct()
            .ToListAsync(ct);

        var placesByTrip = namings
            .GroupBy(n => n.TripId)
            .ToDictionary(g => g.Key, g => g.Select(n => n.FeatureId).ToList());
        var inScope = scopedTripIds.ToHashSet();

        var earliest = new Dictionary<(Guid Caver, Guid Place), (DateOnly Anywhere, DateOnly? Here)>();
        foreach (var attendance in attendances)
        {
            foreach (var placeId in placesByTrip[attendance.TripId])
            {
                var key = (attendance.CaverId, placeId);
                var here = inScope.Contains(attendance.TripId) ? attendance.TripDate : (DateOnly?)null;
                if (!earliest.TryGetValue(key, out var seen))
                {
                    earliest[key] = (attendance.TripDate, here);
                    continue;
                }

                earliest[key] = (
                    attendance.TripDate < seen.Anywhere ? attendance.TripDate : seen.Anywhere,
                    Earlier(seen.Here, here));
            }
        }

        return earliest.Values.Count(reached => reached.Here == reached.Anywhere);
    }

    private static DateOnly? Earlier(DateOnly? left, DateOnly? right) =>
        left is null ? right
        : right is null ? left
        : left < right ? left : right;
}
