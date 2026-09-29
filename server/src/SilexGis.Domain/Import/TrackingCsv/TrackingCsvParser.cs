// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// Reads a sheet of tracking reports into rows, saying what it could not read rather than
/// throwing.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the trip sheet's parser, and on purpose: it reuses that one's record reader,
/// its cell tidying and its whole-file day/month decision, so a club that brings both files gets
/// the same answers to the same questions about quoting, blank cells and ambiguous dates. What
/// differs is what a row <em>is</em> — an instant about one or more people, rather than a trip —
/// and that is where every rule below lives.
/// </para>
/// <para>
/// <b>Nothing here resolves anything against a cave.</b> A caver is the name that was written, a
/// station is a string, a place label is a string, a depth is a number. Matching people to the
/// roster and places to a survey needs the trip, and both are done by the importer, which is also
/// where a reviewer is shown what matched. Keeping it out of the parser is what makes the parser
/// testable without a database and keeps one reading of the file behind both preview and commit.
/// </para>
/// </remarks>
public static class TrackingCsvParser
{
    public static TrackingCsvParseResult Parse(
        string? text,
        TrackingCsvOptions? options = null,
        TrackingCsvColumnMapping? mapping = null)
    {
        options ??= TrackingCsvOptions.Default;
        mapping ??= TrackingCsvColumnMapping.Auto;

        var fileDiagnostics = new List<TrackingCsvDiagnostic>();
        var read = TripCsvRecordReader.Read(text, options.Delimiter);

        if (read.UnterminatedQuoteLine is { } openedAt)
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.UnterminatedQuote, openedAt));
            return new TrackingCsvParseResult { FileDiagnostics = fileDiagnostics };
        }

        if (read.Records.Count == 0)
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.NoHeader, 0));
            return new TrackingCsvParseResult { FileDiagnostics = fileDiagnostics };
        }

        var headerRecord = read.Records[0];
        var header = headerRecord.Fields;
        if (header.Count > options.MaxColumns)
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.TooManyColumns, headerRecord.Line,
                Detail: header.Count.ToString(CultureInfo.InvariantCulture)));
            return new TrackingCsvParseResult { FileDiagnostics = fileDiagnostics, Header = header };
        }

        var columns = ResolveColumns(header, mapping, headerRecord.Line, fileDiagnostics);

        var unmapped = header
            .Where((h, i) => !columns.ContainsValue(i) && TripCsvValues.Tidy(h).Length > 0)
            .Select(TripCsvValues.Tidy)
            .ToList();
        foreach (var column in unmapped)
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.UnmappedColumn,
                headerRecord.Line, column));
        }

        // Two columns without which no row means anything. Refused for the file rather than row by
        // row: a sheet whose moment column was misnamed would otherwise report the same error a
        // thousand times and bury the one thing the reviewer has to fix.
        if (!columns.ContainsKey(TrackingCsvField.RecordedAt))
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.MomentColumnMissing, headerRecord.Line));
        }

        if (!columns.ContainsKey(TrackingCsvField.Cavers))
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.CaverColumnMissing, headerRecord.Line));
        }

        var data = read.Records.Skip(1).ToList();
        if (fileDiagnostics.Any(d => d.Severity == TrackingCsvSeverity.Error))
        {
            return new TrackingCsvParseResult
            {
                FileDiagnostics = fileDiagnostics,
                Header = header,
                ResolvedColumns = columns.ToDictionary(p => p.Key, p => header[p.Value]),
                UnmappedColumns = unmapped,
                DateOrder = options.DateOrder,
            };
        }

        // The whole moment column first, so day-first or month-first is answered once. A single
        // cell cannot answer it — 5/11 is a real date either way — and a reader that guessed per
        // row would let one unusual row import a different day from its neighbours.
        var momentIndex = columns[TrackingCsvField.RecordedAt];
        var readings = data.Select(r => TrackingCsvMoments.DatePartOf(Cell(r, momentIndex))).ToList();
        var (order, orderSource) = TripCsvDates.DecideOrder(readings, options.DateOrder);
        if (orderSource == TripCsvDateOrderSource.Conflict)
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.DateOrderConflict,
                headerRecord.Line, Detail: order.ToString()));
        }

        var rows = new List<TrackingCsvRow>(data.Count);
        foreach (var record in data)
        {
            var row = ReadRow(record, header, columns, options, order);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return new TrackingCsvParseResult
        {
            Rows = rows,
            FileDiagnostics = fileDiagnostics,
            Header = header,
            ResolvedColumns = columns.ToDictionary(p => p.Key, p => header[p.Value]),
            UnmappedColumns = unmapped,
            DateOrder = order,
            DateOrderSource = orderSource,
        };
    }

    /// <summary>
    /// Which header index carries which field — every hand-named header claimed first, across all
    /// fields, and detection run only over what is left.
    /// </summary>
    /// <remarks>
    /// Field by field instead, a field that happens to come earlier guesses its way onto the very
    /// column a later field was explicitly pointed at, and the reviewer is then told their column
    /// is missing while it sits there in the header row. Same two passes, and the same reason, as
    /// the trip sheet.
    /// </remarks>
    private static Dictionary<TrackingCsvField, int> ResolveColumns(
        IReadOnlyList<string> header,
        TrackingCsvColumnMapping mapping,
        int headerLine,
        List<TrackingCsvDiagnostic> diagnostics)
    {
        var folded = header.Select(h => FoldedText.Of(TripCsvValues.Tidy(h)).Value).ToList();
        var columns = new Dictionary<TrackingCsvField, int>();
        var taken = new HashSet<int>();

        foreach (var field in TrackingCsvColumnMapping.AllFields)
        {
            var named = mapping.NamedHeader(field);
            if (named is null)
            {
                continue;
            }

            var wanted = FoldedText.Of(TripCsvValues.Tidy(named)).Value;
            var index = folded.FindIndex(h => h == wanted);
            if (index < 0 || !taken.Add(index))
            {
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Warning, TrackingCsvProblem.NamedColumnMissing,
                    headerLine, named, field.ToString()));
                continue;
            }

            columns[field] = index;
        }

        foreach (var field in TrackingCsvColumnMapping.AllFields)
        {
            if (columns.ContainsKey(field) || mapping.NamedHeader(field) is not null)
            {
                continue;
            }

            foreach (var candidate in TrackingCsvColumnMapping.CandidatesFor(field))
            {
                var index = folded.FindIndex(h => h == candidate);
                if (index >= 0 && taken.Add(index))
                {
                    columns[field] = index;
                    break;
                }
            }
        }

        return columns;
    }

    private static TrackingCsvRow? ReadRow(
        TripCsvRecord record,
        IReadOnlyList<string> header,
        Dictionary<TrackingCsvField, int> columns,
        TrackingCsvOptions options,
        TripCsvDateOrder order)
    {
        if (record.Fields.All(f => TripCsvValues.Tidy(f).Length == 0))
        {
            return null;
        }

        var diagnostics = new List<TrackingCsvDiagnostic>();

        // A row narrower or wider than the header is reconciled to it and said so, never thrown
        // on: one malformed line in a thousand-row sheet must not cost the other rows.
        if (record.Fields.Count != header.Count)
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.RaggedRow, record.Line,
                Detail: $"{record.Fields.Count} of {header.Count}"));
        }

        string? Text(TrackingCsvField field) => columns.TryGetValue(field, out var index)
            ? TripCsvValues.Single(Cell(record, index), options.SkipTokens)
            : null;

        string Named(TrackingCsvField field) => columns.TryGetValue(field, out var index)
            ? TripCsvValues.Tidy(header.ElementAtOrDefault(index))
            : string.Empty;

        var momentText = Text(TrackingCsvField.RecordedAt);
        var moment = TrackingCsvMoments.Read(momentText, order);
        DateTimeOffset? at = null;
        switch (moment.Kind)
        {
            case TrackingCsvMomentKind.Read:
                at = moment.At;
                break;
            case TrackingCsvMomentKind.Empty:
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentMissing,
                    record.Line, Named(TrackingCsvField.RecordedAt)));
                break;
            case TrackingCsvMomentKind.DateWithoutTime:
                // Refused rather than filed at midnight. A day's reports would collapse onto one
                // instant and, with the caver, onto one upsert key, so re-importing the sheet
                // would overwrite each row with the next.
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentWithoutTime,
                    record.Line, Named(TrackingCsvField.RecordedAt), momentText));
                break;
            default:
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentUnreadable,
                    record.Line, Named(TrackingCsvField.RecordedAt), momentText));
                break;
        }

        var cavers = columns.TryGetValue(TrackingCsvField.Cavers, out var caverIndex)
            ? TripCsvValues.Split(
                Cell(record, caverIndex), options.MultiValueSeparators, options.SkipTokens).Values
            : [];
        if (cavers.Count == 0)
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.NoCavers,
                record.Line, Named(TrackingCsvField.Cavers)));
        }

        var stationName = Text(TrackingCsvField.Station);
        var placeLabel = Text(TrackingCsvField.Place);
        var (depth, depthProblem) = ReadDepth(Text(TrackingCsvField.Depth));
        if (depthProblem is { } problem)
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, problem, record.Line,
                Named(TrackingCsvField.Depth), Text(TrackingCsvField.Depth)));
        }

        var decides = stationName is not null ? TrackingCsvPlaceKind.Station
            : placeLabel is not null ? TrackingCsvPlaceKind.Place
            : depth is not null ? TrackingCsvPlaceKind.Depth
            : TrackingCsvPlaceKind.None;

        var stateText = Text(TrackingCsvField.State);
        var state = options.StateWords.KindOf(stateText);
        if (stateText is not null && state is null)
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.StateWordUnknown,
                record.Line, Named(TrackingCsvField.State), stateText));
        }

        if (state is not null && (stationName is not null || placeLabel is not null || depth is not 0 and not null))
        {
            // Quiet in the ordinary case: a club writes depth 0 beside "intrare", and nothing was
            // lost by dropping it. Said out loud only where the row claimed a real place as well,
            // because then the file disagrees with itself and somebody should know which half won.
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.StateOverridesPlace,
                record.Line, Named(TrackingCsvField.State), stateText));
        }

        if (state is null && decides == TrackingCsvPlaceKind.None)
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.NoPlaceAndNoState, record.Line));
        }

        return new TrackingCsvRow
        {
            Line = record.Line,
            At = at,
            Cavers = cavers,
            Team = Text(TrackingCsvField.Team),
            StationName = stationName,
            PlaceLabel = placeLabel,
            DepthM = depth,
            Decides = decides,
            State = state,
            Note = FoldNote(Text(TrackingCsvField.Note), Text(TrackingCsvField.Details)),
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// A depth in metres, positive down, as the column wrote it.
    /// </summary>
    /// <remarks>
    /// The sign is kept rather than taken away. A cave has passages above its entrance as well as
    /// below, the stored field is signed for that reason, and "-40" in a sheet that also writes
    /// "40" is two different places, not one written twice. A comma is accepted as the decimal
    /// mark because that is what a Romanian spreadsheet exports.
    /// </remarks>
    private static (decimal? Depth, TrackingCsvProblem? Problem) ReadDepth(string? text)
    {
        if (text is null)
        {
            return (null, null);
        }

        var written = text.Replace(',', '.');
        if (!decimal.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth))
        {
            return (null, TrackingCsvProblem.DepthUnreadable);
        }

        if (Math.Abs(depth) > TripTrackingRules.MaxDepthAbsM)
        {
            return (null, TrackingCsvProblem.DepthOutOfRange);
        }

        return (depth, null);
    }

    /// <summary>
    /// The note and any further-details column as one note.
    /// </summary>
    /// <remarks>
    /// Folded rather than stored apart, because a report has one free-text field and adding a
    /// second to the schema for a column only an importer ever fills would leave it invisible
    /// everywhere a note is shown. Joined with a dash so the two halves stay legible as two.
    /// </remarks>
    private static string? FoldNote(string? note, string? details)
    {
        if (note is null)
        {
            return details;
        }

        return details is null ? note : $"{note} — {details}";
    }

    private static string? Cell(TripCsvRecord record, int index) =>
        index >= 0 && index < record.Fields.Count ? record.Fields[index] : null;
}
