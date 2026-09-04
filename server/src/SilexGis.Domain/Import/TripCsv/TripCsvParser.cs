// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

/// <summary>
/// Reads a club's trip spreadsheet into neutral rows. Pure: it is handed text and gives back
/// rows and diagnostics, and it knows nothing about caves, cavers, vocabularies or this
/// installation's data. What it produces still has to be matched against what exists, which is
/// deliberately somebody else's job — a parser that also resolves cannot be re-run when the
/// reviewer changes a mapping, and cannot be tested without a database.
/// </summary>
public static class TripCsvParser
{
    public static TripCsvParseResult Parse(string? text, TripCsvOptions? options = null, TripCsvColumnMapping? mapping = null)
    {
        options ??= TripCsvOptions.Default;
        mapping ??= TripCsvColumnMapping.Auto;

        var read = TripCsvRecordReader.Read(text, options.Delimiter);
        var records = read.Records;
        var fileDiagnostics = new List<TripCsvDiagnostic>();
        if (read.UnterminatedQuoteLine is { } quoteLine)
        {
            fileDiagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Error, TripCsvDiagnosticCode.UnterminatedQuote, quoteLine));
        }

        if (records.Count == 0)
        {
            fileDiagnostics.Add(new TripCsvDiagnostic(TripCsvSeverity.Error, TripCsvDiagnosticCode.NoHeader, 0));
            return new TripCsvParseResult { FileDiagnostics = fileDiagnostics };
        }

        var headerRecord = records[0];
        var header = headerRecord.Fields;
        if (header.Count > options.MaxColumns)
        {
            fileDiagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Error,
                TripCsvDiagnosticCode.TooManyColumns,
                headerRecord.Line,
                Detail: header.Count.ToString()));
            return new TripCsvParseResult { FileDiagnostics = fileDiagnostics, Header = header };
        }

        var columns = ResolveColumns(header, mapping, headerRecord.Line, fileDiagnostics);
        var claimed = columns.Values.ToHashSet();
        var unmapped = new List<string>();
        for (var i = 0; i < header.Count; i++)
        {
            if (claimed.Contains(i) || TripCsvValues.Tidy(header[i]).Length == 0)
            {
                continue;
            }

            unmapped.Add(header[i]);
            fileDiagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Warning,
                TripCsvDiagnosticCode.UnmappedColumn,
                headerRecord.Line,
                Column: header[i]));
        }

        var data = records.Skip(1).ToList();

        // The day/month order is a property of the file, so the date columns are read once in
        // full before any row is turned into a date.
        var readings = new List<TripCsvDateReading>();
        var ambiguousRows = 0;
        foreach (var record in data)
        {
            // A row is counted once however many of its date cells are undecidable: the number
            // shown beside the day/month choice says how many rows ride on it, and a number
            // larger than the file has rows tells the reviewer nothing they can act on.
            var rowIsAmbiguous = false;
            foreach (var field in (ReadOnlySpan<TripCsvField>)[TripCsvField.StartDate, TripCsvField.EndDate])
            {
                if (columns.TryGetValue(field, out var index))
                {
                    var reading = TripCsvDates.Read(Cell(record, index));
                    readings.Add(reading);
                    rowIsAmbiguous |= reading.IsAmbiguous;
                }
            }

            if (rowIsAmbiguous)
            {
                ambiguousRows++;
            }
        }

        var (order, orderSource) = TripCsvDates.DecideOrder(readings, options.DateOrder);
        if (orderSource == TripCsvDateOrderSource.Conflict)
        {
            fileDiagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Warning,
                TripCsvDiagnosticCode.DateOrderConflict,
                headerRecord.Line,
                Detail: order.ToString()));
        }

        var rows = new List<TripCsvRow>(data.Count);
        var firstSeenId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in data)
        {
            var row = ReadRow(record, header, columns, options, order, firstSeenId);
            if (row is not null)
            {
                rows.Add(row);
            }
            else
            {
                fileDiagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning, TripCsvDiagnosticCode.BlankRow, record.Line));
            }
        }

        return new TripCsvParseResult
        {
            Rows = rows,
            FileDiagnostics = fileDiagnostics,
            Header = header,
            ResolvedColumns = columns.ToDictionary(p => p.Key, p => header[p.Value]),
            UnmappedColumns = unmapped,
            DateOrder = order,
            DateOrderSource = orderSource,
            AmbiguousDateRows = ambiguousRows,
        };
    }

    /// <summary>
    /// Which header index carries which field. A field the mapping names is looked for under that
    /// name alone; only a field left to detection falls back to the built-in spellings.
    ///
    /// <para>
    /// Resolved in two passes: every header somebody named by hand is claimed first, across all
    /// fields, and detection then runs over whatever indices are left. Doing it field by field
    /// instead lets a field that happens to come earlier guess its way onto the very column a
    /// later field was explicitly pointed at — and the reviewer is then told their column is not
    /// in the file, while it sits there in the header row.
    /// </para>
    /// </summary>
    private static Dictionary<TripCsvField, int> ResolveColumns(
        IReadOnlyList<string> header,
        TripCsvColumnMapping mapping,
        int headerLine,
        List<TripCsvDiagnostic> diagnostics)
    {
        var folded = header.Select(h => FoldedText.Of(TripCsvValues.Tidy(h)).Value).ToList();
        var columns = new Dictionary<TripCsvField, int>();
        var taken = new HashSet<int>();

        foreach (var field in TripCsvColumnMapping.AllFields)
        {
            var named = mapping.NamedHeader(field);
            if (named is null)
            {
                continue;
            }

            var wanted = FoldedText.Of(TripCsvValues.Tidy(named)).Value;
            var index = folded.FindIndex(h => h == wanted);
            if (index < 0)
            {
                diagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning,
                    TripCsvDiagnosticCode.MappedColumnMissing,
                    headerLine,
                    field,
                    named));
                continue;
            }

            if (!taken.Add(index))
            {
                // Two fields named the same header. Neither is a guess, so nothing here can
                // decide between them; the second is refused, and told apart from a header the
                // file does not have at all, because the two need different things done to them.
                diagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning,
                    TripCsvDiagnosticCode.MappedColumnTaken,
                    headerLine,
                    field,
                    named));
                continue;
            }

            columns[field] = index;
        }

        foreach (var field in TripCsvColumnMapping.AllFields)
        {
            if (columns.ContainsKey(field) || mapping.NamedHeader(field) is not null)
            {
                continue;
            }

            foreach (var candidate in TripCsvColumnMapping.CandidatesFor(field))
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

    private static TripCsvRow? ReadRow(
        TripCsvRecord record,
        IReadOnlyList<string> header,
        Dictionary<TripCsvField, int> columns,
        TripCsvOptions options,
        TripCsvDateOrder order,
        Dictionary<string, int> firstSeenId)
    {
        if (record.Fields.All(f => TripCsvValues.Tidy(f).Length == 0))
        {
            return null;
        }

        var diagnostics = new List<TripCsvDiagnostic>();

        // A row that is not as wide as the header is reconciled to it and said so, never thrown
        // on: one malformed line in a thousand-row sheet must not cost the other rows.
        if (record.Fields.Count != header.Count)
        {
            diagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Warning,
                TripCsvDiagnosticCode.RaggedRow,
                record.Line,
                Detail: $"{record.Fields.Count} of {header.Count}"));
        }

        string? Text(TripCsvField field) => columns.TryGetValue(field, out var index)
            ? TripCsvValues.Single(Cell(record, index), options.SkipTokens)
            : null;

        IReadOnlyList<string> List(TripCsvField field)
        {
            if (!columns.TryGetValue(field, out var index))
            {
                return [];
            }

            var separators = options.MultiValueSeparators.ToList();
            if (options.SlashSeparatedFields.Contains(field))
            {
                separators.Add('/');
            }

            var split = TripCsvValues.Split(Cell(record, index), separators, options.SkipTokens);
            foreach (var lost in split.Dropped)
            {
                diagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning,
                    TripCsvDiagnosticCode.ValueDropped,
                    record.Line,
                    field,
                    columns.TryGetValue(field, out var i) ? header[i] : null,
                    lost));
            }

            return split.Values;
        }

        DateOnly? Date(TripCsvField field, string? raw)
        {
            if (raw is null)
            {
                if (options.RequiredFields.Contains(field))
                {
                    diagnostics.Add(new TripCsvDiagnostic(
                        TripCsvSeverity.Error, TripCsvDiagnosticCode.RequiredFieldEmpty, record.Line, field));
                }

                return null;
            }

            var reading = TripCsvDates.Read(raw);
            if (reading.IsAmbiguous)
            {
                diagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning, TripCsvDiagnosticCode.DateAmbiguous, record.Line, field, Detail: raw));
            }

            if (TripCsvDates.TryResolve(reading, order, out var date, out var refusal))
            {
                return date;
            }

            diagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Error,
                refusal ?? TripCsvDiagnosticCode.DateUnreadable,
                record.Line,
                field,
                Detail: raw));
            return null;
        }

        var startText = Text(TripCsvField.StartDate);
        var endText = Text(TripCsvField.EndDate);
        var title = Text(TripCsvField.Title);
        if (title is null && options.RequiredFields.Contains(TripCsvField.Title))
        {
            diagnostics.Add(new TripCsvDiagnostic(
                TripCsvSeverity.Error, TripCsvDiagnosticCode.RequiredFieldEmpty, record.Line, TripCsvField.Title));
        }

        var sourceId = Text(TripCsvField.SourceId);
        if (sourceId is not null)
        {
            var key = FoldedText.Of(sourceId).Value;
            if (firstSeenId.TryGetValue(key, out var first))
            {
                // Two rows under one number is how one of them disappears later, silently, in
                // whatever keeps the rows by that number.
                diagnostics.Add(new TripCsvDiagnostic(
                    TripCsvSeverity.Warning,
                    TripCsvDiagnosticCode.DuplicateSourceId,
                    record.Line,
                    TripCsvField.SourceId,
                    Detail: $"{sourceId} first seen on line {first}"));
            }
            else
            {
                firstSeenId[key] = record.Line;
            }
        }

        var unmapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var claimed = columns.Values.ToHashSet();
        for (var i = 0; i < header.Count; i++)
        {
            if (claimed.Contains(i))
            {
                continue;
            }

            var value = TripCsvValues.Tidy(Cell(record, i));
            var name = TripCsvValues.Tidy(header[i]);
            if (value.Length > 0 && name.Length > 0)
            {
                unmapped[name] = value;
            }
        }

        // Dates last of the mapped fields, so a required-field error and a grammar error on the
        // same row are both reported rather than one hiding the other.
        var start = Date(TripCsvField.StartDate, startText);
        var end = Date(TripCsvField.EndDate, endText);

        return new TripCsvRow
        {
            Line = record.Line,
            SourceId = sourceId,
            StartDate = start,
            EndDate = end,
            StartDateText = startText,
            EndDateText = endText,
            Title = title,
            Country = Text(TripCsvField.Country),
            Massif = Text(TripCsvField.Massif),
            SubArea = Text(TripCsvField.SubArea),
            Caves = List(TripCsvField.Caves),
            Proposers = List(TripCsvField.Proposers),
            Participants = List(TripCsvField.Participants),
            Details = Text(TripCsvField.Details),
            Details2 = Text(TripCsvField.Details2),
            TripType = Text(TripCsvField.TripType),
            Errors = Text(TripCsvField.Errors),
            Unmapped = unmapped,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>A cell, or empty where the row was narrower than the header.</summary>
    private static string Cell(TripCsvRecord record, int index) =>
        index >= 0 && index < record.Fields.Count ? record.Fields[index] : string.Empty;
}
