// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.Statistics;

/// <summary>Which of the three things is being added up.</summary>
internal enum StatisticsSubject
{
    Caver,
    Cave,
    CavingGroup,
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

        rows.AddRange(
        [
            Figure("Places", totals.Places),
            Figure("First visits", totals.FirstVisits),
            Figure("Trips with an incident", totals.Incidents),

            // Hours rather than minutes, and as a number rather than a written duration: a reader
            // adds a column of these up, and "12 h 30 m" is a word.
            Figure("Hours underground", totals.UndergroundMinutes / 60d),
            Figure("Times somebody went", totals.PersonTrips),
            Figure("Of those, with times recorded", totals.TimedPersonTrips),
            Figure("Metres surveyed", (double)totals.LengthSurveyedM),
            Figure("Metres of rope", (double)totals.RopeMetresM),
            Figure("Survey stations", totals.SurveyStations),

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

    private static string Heading(StatisticsSubject subject) => subject switch
    {
        StatisticsSubject.Caver => "Trip statistics for one person",
        StatisticsSubject.Cave => "Trip statistics for one cave",
        _ => "Trip statistics for one club",
    };
}
