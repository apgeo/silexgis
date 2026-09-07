// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Turns a narrowed trip listing into the rows of a spreadsheet.
/// </summary>
/// <remarks>
/// <para>
/// The file is the filter, not the page: every trip the narrowing leaves, in the order the
/// listing would hand them back, rather than whichever fifty the reader happens to be looking at.
/// It is bounded all the same, and says so in its own first lines when the bound was reached —
/// a file that stopped at a limit and a file that ended look identical once it is saved, and this
/// one is kept long after the screen that produced it is gone.
/// </para>
/// <para>
/// The columns are the ones the listing itself shows, and every value comes from the same
/// mapping the screen is drawn from, so a trip discloses exactly as much in a file as it does on
/// a page. Nothing here resolves a cave or a coordinate: the listing does not name them either,
/// and an export is the last place to start.
/// </para>
/// <para>
/// The headings are English, as every other export here is. The application's translations live
/// with the pages that use them and the server holds no catalogue of its own.
/// </para>
/// </remarks>
internal static class TripLogExportWorkbook
{
    public const string SheetName = "Trips";

    /// <summary>
    /// Counted over the trips you may read. Somebody with different access exports a different
    /// set for the same filter, and both are right.
    /// </summary>
    private const string VisibilityStatement =
        "Trips you may read, narrowed by the filter this file was exported from. "
        + "Somebody with different access exports a different set for the same filter, and both are right.";

    public static IReadOnlyList<IReadOnlyList<SheetCell>> Rows(
        IReadOnlyList<TripLogDto> trips, bool truncated, int limit)
    {
        var rows = new List<IReadOnlyList<SheetCell>>();
        rows.Add([SheetCell.Title("Trips")]);
        rows.Add([SheetCell.Of(VisibilityStatement)]);

        if (truncated)
        {
            rows.Add([SheetCell.Of(
                $"The filter matched more than {limit} trips and this file holds the first {limit}. "
                + "Narrow the filter to export the rest.")]);
        }

        rows.Add([]);
        rows.Add(
        [
            SheetCell.Title("Date"),
            SheetCell.Title("End date"),
            SheetCell.Title("Title"),
            SheetCell.Title("State"),
            SheetCell.Title("Location"),
            SheetCell.Title("People"),
            SheetCell.Title("Something went wrong"),
            SheetCell.Title("Audience"),
        ]);

        foreach (var trip in trips)
        {
            rows.Add(
            [
                SheetCell.Of(trip.TripDate.ToString("yyyy-MM-dd")),
                SheetCell.Of(trip.TripDateEnd?.ToString("yyyy-MM-dd")),
                SheetCell.Of(trip.Title),
                SheetCell.Of(trip.State.ToString()),
                SheetCell.Of(trip.LocationText),
                // Distinct by person: the roster holds one row per job, so somebody who led and
                // surveyed one trip is one person who went once.
                SheetCell.Of((double)trip.Participants.Select(p => p.CaverId).Distinct().Count()),
                SheetCell.Of(trip.HadIncident ? "yes" : "no"),
                SheetCell.Of(trip.Visibility.ToString()),
            ]);
        }

        return rows;
    }
}
