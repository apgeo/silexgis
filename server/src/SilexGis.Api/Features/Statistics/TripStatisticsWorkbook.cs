// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.Statistics;

/// <summary>Which thing is being added up.</summary>
internal enum StatisticsSubject
{
    Caver,
    Cave,
    CavingGroup,

    /// <summary>
    /// A camp, added up over the trips gathered into it. The same arithmetic as the three above
    /// and deliberately so: a camp asked through a surface of its own would be a second body of
    /// counting rules, and the day the two disagree is the day one page states a figure another
    /// declines to give.
    /// </summary>
    Expedition,
}

/// <summary>
/// The figures laid out as a sheet. It is the same object the screen is given, turned into rows —
/// there is no second question asked of the database on the way to a file, so a saved copy states
/// exactly what the page above it stated and to exactly the same person.
/// </summary>
/// <remarks>
/// The sheet is a fixed list of figures and its size does not depend on how many trips were
/// counted: whether the scope holds four trips or four thousand, the file is the same handful of
/// rows. Which figures those are depends on the subject and on nothing else.
/// So it carries no cap, and no cap would mean anything here — the bound that other exports need,
/// because they stream a row per record, is supplied in this one by the shape of the answer. What
/// bounds the *work* is the query behind it, which the caller's own visibility already narrows.
///
/// The sentence about whose figures these are is written into the file, not only onto the page.
/// A spreadsheet is forwarded, printed and read months later by somebody who never saw the screen,
/// and two copies made by two people legitimately disagree — without the sentence, one of them is
/// simply wrong, and somebody eventually "fixes" the difference by removing the filter.
///
/// Labels are English. The application's translations live with the pages that use them, the
/// server holds no catalogue of its own, and every other export here names its columns in English
/// for the same reason.
/// </remarks>
internal static class TripStatisticsWorkbook
{
    /// <summary>The name of the single sheet in the workbook.</summary>
    internal const string SheetName = "Trip statistics";

    /// <summary>
    /// The statement that the figures are one reader's own. It says the same thing as the caption
    /// on the screen, in the same voice.
    /// </summary>
    private const string VisibilityStatement =
        "Counted over the trips you may read. Somebody with different access sees different totals "
        + "for the same subject, and both are right.";

    /// <summary>
    /// The label of the hours read from tracking logs. It names its source and says it is not a
    /// part of the roster's hours, because the file is read with nobody beside the reader.
    /// </summary>
    internal const string WatchHoursLabel =
        "Hours underground, from tracking (a second count, not added to the hours above)";

    internal static IReadOnlyList<IReadOnlyList<SheetCell>> Rows(
        StatisticsSubject subject, TripStatisticsDto totals)
    {
        List<IReadOnlyList<SheetCell>> rows =
        [
            [SheetCell.Title(Heading(subject))],
            [SheetCell.Of(VisibilityStatement)],
            [],
            [SheetCell.Title("Figure"), SheetCell.Title("Value")],
            Figure("Trips", totals.Trips),
        ];

        // Withheld from a person's own sheet, exactly as the screen withholds it: how many people
        // were on somebody's trips is a fact about their company rather than about them, and a
        // saved copy that states a figure the page above it declined to show is the file coming
        // to answer a question the surface refuses. The file outlives the page, so it is the
        // copy that has to be right.
        if (subject != StatisticsSubject.Caver)
        {
            rows.Add(Figure("People", totals.People));
        }

        // Left off a cave's sheet, exactly as the screen leaves them off its panel. Every trip
        // counted for a cave names that cave and its places are narrowed to that cave alone, so
        // "places" can only read one, or none while nobody has been; and everybody on those
        // trips reached the cave for the first time on one of them, so "first visits" can only
        // repeat the people figure in the row above. A row that cannot differ from its neighbour
        // still reads as a second fact — more so in a file, where nobody is beside the reader to
        // say that it is the same number twice. Both stay wherever they can vary: for a person,
        // a club and a camp.
        if (subject != StatisticsSubject.Cave)
        {
            rows.Add(Figure("Places", totals.Places));
            rows.Add(Figure("First visits", totals.FirstVisits));
        }

        rows.AddRange(
        [
            Figure("Trips with an incident", totals.Incidents),

            // Hours rather than minutes, and as a number rather than a written duration: a reader
            // adds a column of these up, and "12 h 30 m" is a word.
            Figure("Hours underground", totals.UndergroundMinutes / 60d),
            Figure("Times somebody went", totals.PersonTrips),
            Figure("Of those, with times recorded", totals.TimedPersonTrips),
            Figure("Metres surveyed", (double)totals.LengthSurveyedM),
            Figure("Metres of rope", (double)totals.RopeMetresM),
            Figure("Survey stations", totals.SurveyStations),

            // The pictures this reader may see, which is why it belongs above the sentence's
            // reach as much as any other figure here: two people saving this file legitimately
            // get two different numbers.
            Figure("Photographs", totals.Photographs),

            // What the tracking logs say, kept in rows of their own below everything the trips
            // and their rosters say. The hours are a second count of time underground, taken from
            // each person's reported entries and exits, and the label says in so many words that
            // they are not to be added to the row above: a trip that has both a roster with times
            // and a log describes the same hours twice, and somebody summing a column of this
            // file would otherwise double them. The third row is to these hours what "with times
            // recorded" is to the roster's — how many of the times somebody went they cover.
            //
            // Where the logs cover nobody the hours cell is left empty rather than written as a
            // nought. A log whose entries were never closed does not say the party spent no time
            // underground; it says nothing about how long, and an empty cell is how a sheet says
            // nothing. The count beside it, which is a count and may honestly be nought, stays.
            Figure("Tracked trips", totals.TrackedTrips),
            [
                SheetCell.Of(WatchHoursLabel),
                SheetCell.Of(totals.WatchTimedPersonTrips == 0
                    ? (double?)null
                    : totals.WatchUndergroundMinutes / 60d),
            ],
            Figure("Times somebody went, timed by tracking", totals.WatchTimedPersonTrips),

            // Written out rather than left as date cells: a date cell with no format applied opens
            // as a five-digit number, and this one written form is read the same way everywhere.
            [SheetCell.Of("First trip"), SheetCell.Of(Day(totals.EarliestTripDate))],
            [SheetCell.Of("Last trip"), SheetCell.Of(Day(totals.LatestTripDate))],
        ]);

        return rows;
    }

    private static SheetCell[] Figure(string label, double value) =>
        [SheetCell.Of(label), SheetCell.Of(value)];

    private static string? Day(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Every subject named, and an unnamed one refused loudly rather than labelled as whichever
    // arm happened to be last. A file headed "for one club" over a camp's figures is wrong in a
    // way nobody notices until the file has been forwarded.
    private static string Heading(StatisticsSubject subject) => subject switch
    {
        StatisticsSubject.Caver => "Trip statistics for one person",
        StatisticsSubject.Cave => "Trip statistics for one cave",
        StatisticsSubject.CavingGroup => "Trip statistics for one club",
        StatisticsSubject.Expedition => "Trip statistics for one expedition",
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };
}
