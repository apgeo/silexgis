// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Calendar;

/// <summary>
/// Turns the rows read from each dated source into one answer: merged, capped, and arranged.
/// </summary>
/// <remarks>
/// Separate from the handler so the two rules that are easy to get quietly wrong — what the cap
/// keeps when it bites, and what the shortfall it reports counts — can be exercised directly at a
/// size a test can reach, rather than only at the size the endpoint ships with.
/// </remarks>
public static class CalendarWindow
{
    /// <summary>
    /// Merges the sources' rows into one ordered answer, capped at <paramref name="maxRows"/>.
    /// </summary>
    /// <param name="entries">Every row the caller may read that fell in the window.</param>
    /// <param name="found">
    /// How many rows the sources held for this caller in this window, counted over the same
    /// already-narrowed queries the rows came from. It is a count of their answer and never of
    /// the tables: a figure taken before their visibility was applied would tell somebody how
    /// much they are not being shown.
    /// </param>
    /// <param name="maxRows">The backstop.</param>
    /// <param name="sort">The order asked for; anything unrecognised falls back to the calendar's own.</param>
    public static CalendarResultDto Merge(
        IEnumerable<CalendarEntryDto> entries, int found, int maxRows, string? sort)
    {
        // The cap keeps the window's chronological head whatever order was asked for, because a
        // calendar's own order is by day and a stable rule beats an arbitrary one: the same
        // window then answers with the same rows every time it is opened, and only the
        // arrangement of them changes. Ties break on the identifier — rows sharing a day are
        // ordinary, and an order that does not separate them lets the same row fall inside the
        // cap on one request and outside it on the next as the database chooses.
        var kept = entries
            .OrderBy(x => x.Start)
            .ThenBy(x => x.Id)
            .Take(maxRows)
            .ToList();

        // Said rather than left to be noticed. An answer quietly short of its last rows reads
        // exactly like a complete one, and a record of a month that is silently missing days is
        // worse than one that refuses outright.
        // Floored, so that the field can never carry a number that means nothing. The reads it
        // is computed from are taken from one snapshot, which is what makes the figure right;
        // this is the guard that keeps a future caller passing an inconsistent pair from turning
        // a shortfall into a negative one, which a client would have no way to read.
        return new CalendarResultDto(Sorted(kept, sort), Math.Max(0, found - kept.Count));
    }

    /// <summary>
    /// Arranges the merged rows by one of a fixed set of orders. The set is fixed here rather
    /// than assembled from whatever the caller sent, so a sort key is a word this code knows and
    /// never a fragment of a query somebody supplied. An unrecognised one falls back to the
    /// calendar's own order rather than being refused: nothing is at stake in the arrangement of
    /// rows the caller may already read.
    /// </summary>
    public static IReadOnlyList<CalendarEntryDto> Sorted(
        IReadOnlyList<CalendarEntryDto> entries, string? sort) => sort switch
    {
        // Ordinal rather than the running culture's collation: which rows come back must not
        // depend on which language the server happens to have been started in.
        "title" => [.. entries.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id)],
        "-title" => [.. entries.OrderByDescending(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id)],
        // Named although it is what the fallback already does, so that every word a client
        // may send is one this switch answers to rather than one it happens not to refuse.
        "start" => [.. entries.OrderBy(x => x.Start).ThenBy(x => x.Id)],
        "-start" => [.. entries.OrderByDescending(x => x.Start).ThenBy(x => x.Id)],
        _ => entries,
    };
}
