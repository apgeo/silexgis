// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// What a set of trips adds up to, for the caller asking. Every figure is counted over the trips
/// this caller may read, so two people legitimately see different totals for the same person, the
/// same cave and the same club — the surface showing them has to say so, or the difference is
/// reported as a defect and repaired by removing the filter.
/// </summary>
/// <remarks>
/// The alternative — one true total over every trip in the installation — was considered and
/// refused. A per-person total is a summary of where a named person has been and a per-cave total
/// is a statement of how much attention a cave gets; those are exactly the questions the row-level
/// rules decline to answer, and a count that moved while the row stayed hidden would answer them
/// anyway, repeatedly and reliably.
///
/// Nothing here is stored. Every figure is derived when it is asked for, so a corrected trip
/// corrects the totals in the same moment, and an older trip typed up years later takes its place
/// in the history rather than contradicting a flag written before it existed.
///
/// It sits beside the query that fills it rather than in the surface that returns it, because more
/// than one surface returns it and the answer has to be the same object each time.
///
/// Positional, and appended to rather than inserted into: it carries runs of same-typed members
/// that would absorb one another silently if anything were put between them.
/// </remarks>
/// <param name="Trips">Trips in this scope the caller may read.</param>
/// <param name="People">
/// How many people were on them — people, not roster rows. Somebody who led a trip and surveyed it
/// holds two jobs and is one person, so a count of rows would report more people underground than
/// were there.
/// </param>
/// <param name="Places">
/// Distinct places those trips name, counted only where the caller may both read the place and
/// know where it is. A place whose position is kept from this caller is missing from the trips'
/// own link lists too, so counting it here would print a number the visible links cannot account
/// for.
/// </param>
/// <param name="FirstVisits">
/// How many of those (person, place) pairs were first visits: the earliest trip on which that
/// person reached that place, out of every trip the caller may read. Derived, never a flag — an
/// older trip entered next week moves the first visit to it, which is the ordinary case in a club
/// digitising its archive. Where two trips share the earliest date, both count.
/// </param>
/// <param name="Incidents">Trips on which something went wrong. What went wrong is a different
/// question with a narrower audience and is not answered here.</param>
/// <param name="UndergroundMinutes">
/// Person-minutes underground, summed over each person's own times where their row gives them and
/// the trip's own times otherwise, across the days the trip actually spans.
/// </param>
/// <param name="PersonTrips">(person, trip) pairs in scope — the number of times somebody went.</param>
/// <param name="TimedPersonTrips">
/// How many of those carried usable times. Stated rather than left to be inferred: the hours above
/// are a sum over these, and a reader comparing a big trip count with a small hours figure is owed
/// the reason, which is missing times rather than withheld rows.
/// </param>
/// <param name="LengthSurveyedM">Metres of passage surveyed, summed over the trips.</param>
/// <param name="RopeMetresM">Metres of rope used, summed over the trips.</param>
/// <param name="SurveyStations">Survey stations set, summed over the trips.</param>
/// <param name="EarliestTripDate">The first day any of these trips began.</param>
/// <param name="LatestTripDate">The last day any of them ended.</param>
/// <param name="Photographs">
/// Pictures hanging on those trips that this caller may see — counted through the same rule the
/// galleries obey, and counted as photographs rather than as the pins that hold them, so one
/// picture on two of the trips is one picture. A figure assembled from the pins instead would
/// state how many pictures were being withheld.
/// </param>
public sealed record TripStatisticsDto(
    int Trips,
    int People,
    int Places,
    int FirstVisits,
    int Incidents,
    int UndergroundMinutes,
    int PersonTrips,
    int TimedPersonTrips,
    decimal LengthSurveyedM,
    decimal RopeMetresM,
    int SurveyStations,
    DateOnly? EarliestTripDate,
    DateOnly? LatestTripDate,
    int Photographs)
{
    /// <summary>What a scope with nothing readable in it adds up to.</summary>
    public static TripStatisticsDto Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0m, 0m, 0, null, null, 0);
}
