// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

/// <summary>
/// One row of a trip spreadsheet, read but not resolved. Everything here is text, a date or a
/// list of names: no cave, no caver, no vocabulary row and no identifier of anything in this
/// installation. Matching those names against what exists is a separate act, done later and
/// under the reader's own permissions.
/// </summary>
public sealed record TripCsvRow
{
    /// <summary>The 1-based physical line of the file this row started on.</summary>
    public required int Line { get; init; }

    /// <summary>The sheet's own row number, kept as written so it can be shown back.</summary>
    public string? SourceId { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>The start date exactly as the cell had it, kept for anything the grammar refused.</summary>
    public string? StartDateText { get; init; }

    public string? EndDateText { get; init; }

    public string? Title { get; init; }

    public string? Country { get; init; }

    public string? Massif { get; init; }

    public string? SubArea { get; init; }

    public IReadOnlyList<string> Caves { get; init; } = [];

    public IReadOnlyList<string> Proposers { get; init; } = [];

    public IReadOnlyList<string> Participants { get; init; } = [];

    public string? Details { get; init; }

    public string? Details2 { get; init; }

    public string? TripType { get; init; }

    public string? Errors { get; init; }

    /// <summary>
    /// Cells under headers nothing claimed, by header name. Kept rather than discarded so that a
    /// column the mapping did not recognise can still be shown to the reviewer and carried into
    /// the trip's own text, instead of disappearing between the sheet and the import.
    /// </summary>
    public IReadOnlyDictionary<string, string> Unmapped { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<TripCsvDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>True when something about this row stops it being imported as it stands.</summary>
    public bool HasError => Diagnostics.Any(d => d.Severity == TripCsvSeverity.Error);
}

/// <summary>Everything a parse produced: the rows, what was decided about the file, and what went wrong.</summary>
public sealed record TripCsvParseResult
{
    /// <summary>Every data row read, including the ones carrying an error, in file order.</summary>
    public IReadOnlyList<TripCsvRow> Rows { get; init; } = [];

    /// <summary>Diagnostics about the file as a whole; the per-row ones live on their row.</summary>
    public IReadOnlyList<TripCsvDiagnostic> FileDiagnostics { get; init; } = [];

    /// <summary>The header cells, as written.</summary>
    public IReadOnlyList<string> Header { get; init; } = [];

    /// <summary>Which header ended up carrying each field.</summary>
    public IReadOnlyDictionary<TripCsvField, string> ResolvedColumns { get; init; } =
        new Dictionary<TripCsvField, string>();

    /// <summary>Headers nothing claimed.</summary>
    public IReadOnlyList<string> UnmappedColumns { get; init; } = [];

    /// <summary>The day/month order the whole file was read in.</summary>
    public TripCsvDateOrder DateOrder { get; init; }

    /// <summary>What settled that order.</summary>
    public TripCsvDateOrderSource DateOrderSource { get; init; }

    /// <summary>
    /// How many rows carry a date nothing in the file could settle. Counted in rows rather than
    /// cells, because a reviewer choosing the day/month order is judging how many trips ride on
    /// the choice, and a count of cells can exceed the number of rows the file has.
    /// </summary>
    public int AmbiguousDateRows { get; init; }

    /// <summary>Every diagnostic, the file's and the rows', in one sequence.</summary>
    public IEnumerable<TripCsvDiagnostic> AllDiagnostics =>
        FileDiagnostics.Concat(Rows.SelectMany(r => r.Diagnostics));
}
