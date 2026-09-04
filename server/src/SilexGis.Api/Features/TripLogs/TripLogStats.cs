// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// What a filtered trip listing adds up to: how many trips by year, what they were for, where
/// they went, who went on them, and how much of the ground being covered was new.
/// </summary>
/// <remarks>
/// <para>
/// Cut from the same composition the page, the option counts and the slices are, so a figure on
/// the insights page and the rows the list hands back under the same filter are two readings of
/// one query. Every count is composed <em>into</em> the audience walk and nothing is counted
/// after it: two callers legitimately get different totals and both are right, and no figure here
/// can ever state how many rows the caller was not shown.
/// </para>
/// <para>
/// The distinct rules are the same two the listing carries, and both are load-bearing. A roster
/// row is one person doing one job, so somebody who led and surveyed one trip is one person who
/// went once — every count over the roster is reduced to one row per person per trip before it is
/// counted. And a trip role names a feature once per link and per role, so one trip can name one
/// place twice — every count over the role links is reduced by trip before it is counted.
/// </para>
/// <para>
/// Areas and people hold more than one value per trip, so those breakdowns legitimately add up to
/// more than the number of trips. The answer says so with its own flag rather than leaving the
/// reader to notice, because a total that quietly exceeds the population is the one figure nobody
/// double-checks.
/// </para>
/// <para>
/// The cumulative curve is the figure no other one gives: how many distinct areas the filtered
/// trips had reached by the end of each year. A curve still climbing says the club is finding new
/// ground; a flattening one says it is going back to ground it already knows. Neither the trip
/// count nor the per-area breakdown says that, because both are about volume and this is about
/// novelty.
/// </para>
/// </remarks>
internal static class TripLogStats
{
    /// <summary>
    /// How many values a breakdown hands back, longest first. The same bound the slices take: a
    /// club's roster runs to hundreds and a bar per person is not a chart anybody reads. The
    /// answer also says how many distinct values there were, so a page can say it is showing the
    /// first forty of two hundred rather than implying there are forty.
    /// </summary>
    private const int MaxValues = 40;

    /// <summary>
    /// How wide a run of years is filled in. The year axis is filled between its ends so a year
    /// nobody went anywhere is a flat stretch of the curve rather than a gap the chart closes up
    /// — but one mistyped year turns a decade of trips into a run of empty centuries, so past
    /// this only the years that hold trips are handed back.
    /// </summary>
    private const int MaxYearSpan = 200;

    /// <summary>A breakdown value for the trips that hold nothing at all under a dimension.</summary>
    private const string Unassigned = TripLogGrouping.Unassigned;

    public static async Task<TripStatsDto> BuildAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        UserContext user,
        TripLogListing listing,
        CancellationToken ct)
    {
        var narrowed = listing.Narrowed();
        var tripIds = narrowed.Select(x => x.Id);

        // The whole filtered set rather than a bounded head of it. A cumulative curve read off
        // the first two thousand trips would be a curve of a different archive, and it is the
        // shape of the whole thing that the page exists to show.
        var dated = await narrowed
            .Select(x => new { x.Id, x.TripDate })
            .ToListAsync(ct);
        var matching = dated.Count;
        var overall = await listing.Visible.CountAsync(ct);

        var yearOfTrip = dated.ToDictionary(x => x.Id, x => x.TripDate.Year);

        // The year the trip started, which is the year its bar is drawn on. A trip is one trip in
        // one year, so the year the span ends in is not a second bar: the bars sum to the number
        // of trips, which is the figure printed above them.
        //
        // The one place this differs from the list: a date window keeps a trip whose span merely
        // overlaps it, so a trip running from one New Year's Eve into the next is returned by a
        // window over the later year while its bar stands over the earlier one. Reading the
        // overlap here instead would put that trip in two bars and make the bars total more than
        // the trips, which is the worse of the two.
        var tripsPerYear = dated
            .GroupBy(x => x.TripDate.Year)
            .ToDictionary(g => g.Key, g => g.Count());

        var typeCounts = await narrowed
            .GroupBy(x => x.TripTypeId)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var types = Breakdown(
            [.. typeCounts.Select(x => (
                Value: x.Value?.ToString() ?? Unassigned,
                Label: (string?)null,
                x.Count))],
            overlapping: false);

        // Reduced to one row per person per trip inside the statement: the roster holds one row
        // per job, so counting rows would count a leader who also surveyed twice.
        var rosterCounts = await db.TripLogParticipants.AsNoTracking()
            .Where(participant => tripIds.Contains(participant.TripLogId))
            .Select(participant => new { participant.CaverId, participant.TripLogId })
            .Distinct()
            .GroupBy(x => x.CaverId)
            .Select(g => new { CaverId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var peopled = await db.TripLogParticipants.AsNoTracking()
            .Where(participant => tripIds.Contains(participant.TripLogId))
            .Select(participant => participant.TripLogId)
            .Distinct()
            .CountAsync(ct);
        // Resolved for every counted person rather than for a guessed head of the list. Which
        // forty come back is decided by the ordering below, and that ordering reads the label —
        // so labelling a set chosen before it leaves whoever the two sets disagree about carrying
        // a bare identifier, and an unlabelled value sorts by its identifier and displaces a
        // person with a name. One statement over the roster of the filtered trips either way.
        var caverLabels = await CaverDirectory.ResolveLabelsAsync(
            db, user, rosterCounts.Select(x => x.CaverId), ct);
        var participants = Breakdown(
            [
                .. rosterCounts.Select(x => (
                    Value: x.CaverId.ToString(),
                    Label: caverLabels.GetValueOrDefault(x.CaverId),
                    x.Count)),
                .. Nobody(matching - peopled),
            ],
            overlapping: true);

        // The same walk the area narrowing, the area counts and the area slices make — the
        // containment hierarchy, with the audience and the position gate on every feature it
        // passes through — so an area's bar and the page selecting that area are one question.
        var reach = await TripAreaReach.BuildAsync(db, protection, ctx, tripIds, ct);
        var areas = Breakdown(
            [
                .. reach.Counts().Select(x => (
                    Value: x.AreaId.ToString(),
                    Label: reach.Names.GetValueOrDefault(x.AreaId),
                    x.Count)),
                .. Nobody(matching - reach.AreasOfTrip.Count),
            ],
            overlapping: true);

        return new TripStatsDto(
            matching,
            overall,
            Years(tripsPerYear, FirstYearOfArea(reach, yearOfTrip)),
            types,
            areas,
            participants);
    }

    /// <summary>
    /// The year each area was first reached by one of the filtered trips. The area set is already
    /// deduplicated per trip by the walk, so an area a trip named twice is one arrival.
    /// </summary>
    private static Dictionary<Guid, int> FirstYearOfArea(
        TripAreaReach reach, IReadOnlyDictionary<Guid, int> yearOfTrip)
    {
        var first = new Dictionary<Guid, int>();
        foreach (var (tripId, areaIds) in reach.AreasOfTrip)
        {
            if (!yearOfTrip.TryGetValue(tripId, out var year))
            {
                continue;
            }

            foreach (var areaId in areaIds)
            {
                if (!first.TryGetValue(areaId, out var seen) || year < seen)
                {
                    first[areaId] = year;
                }
            }
        }

        return first;
    }

    /// <summary>
    /// One row per year, carrying both what was done that year and how much of the ground was new
    /// by the end of it. One series rather than two, because they share an axis and two arrays
    /// keyed on the same years is two things to keep aligned.
    /// </summary>
    private static IReadOnlyList<TripStatsYearDto> Years(
        IReadOnlyDictionary<int, int> tripsPerYear, IReadOnlyDictionary<Guid, int> firstYearOfArea)
    {
        if (tripsPerYear.Count == 0)
        {
            return [];
        }

        var newAreas = firstYearOfArea.Values
            .GroupBy(year => year)
            .ToDictionary(g => g.Key, g => g.Count());

        var from = tripsPerYear.Keys.Min();
        var to = tripsPerYear.Keys.Max();
        var span = to - from + 1;
        IEnumerable<int> years = span <= MaxYearSpan
            ? Enumerable.Range(from, span)
            : tripsPerYear.Keys.OrderBy(year => year);

        var soFar = 0;
        var rows = new List<TripStatsYearDto>();
        foreach (var year in years)
        {
            soFar += newAreas.GetValueOrDefault(year);
            rows.Add(new TripStatsYearDto(
                year, tripsPerYear.GetValueOrDefault(year), newAreas.GetValueOrDefault(year), soFar));
        }

        return rows;
    }

    /// <summary>The trips holding nothing under a dimension, as a value of their own, or nothing.</summary>
    private static IEnumerable<(string Value, string? Label, int Count)> Nobody(int count) =>
        count > 0 ? [(Unassigned, null, count)] : [];

    /// <summary>
    /// The values of one dimension, longest first, bounded — with the trips holding nothing under
    /// it sorted last however many they are, because it is the absence of an answer rather than
    /// the most popular one.
    /// </summary>
    private static TripStatsBreakdownDto Breakdown(
        IReadOnlyList<(string Value, string? Label, int Count)> counted, bool overlapping) =>
        new(
            overlapping,
            counted.Count,
            [
                .. counted
                    .OrderBy(x => x.Value.Length == 0)
                    .ThenByDescending(x => x.Count)
                    .ThenBy(x => x.Label ?? x.Value, StringComparer.CurrentCulture)
                    .Take(MaxValues)
                    .Select(x => new TripFacetValueDto(x.Value, x.Label, x.Count)),
            ]);
}
