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
        SettleTheMoment(columns, mapping);

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

        // Two things without which no row means anything. Refused for the file rather than row by
        // row: a sheet whose moment column was misnamed would otherwise report the same error a
        // thousand times and bury the one thing the reviewer has to fix.
        var moment = MomentColumns.Of(columns, header, options.Day);
        if (moment is null)
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

        // A sheet of times with no dates anywhere and no day named for it. Said once for the file,
        // for the reason above — the answer is one day for the whole sheet — and only where a cell
        // actually needs it: a column headed as a time whose every cell writes its date as well is
        // a moment column under a modest name, and is read as it always was.
        if (moment is { Day: null, Date: null, Time: { } timeColumn }
            && data.Any(r => TrackingCsvMoments.IsTimeAlone(Cell(r, timeColumn))))
        {
            fileDiagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.TimeColumnNeedsADay,
                headerRecord.Line, moment.Name));
        }

        if (moment is null || fileDiagnostics.Any(d => d.Severity == TrackingCsvSeverity.Error))
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

        // The whole of the column that carries the dates first, so day-first or month-first is
        // answered once. A single cell cannot answer it — 5/11 is a real date either way — and a
        // reader that guessed per row would let one unusual row import a different day from its
        // neighbours.
        var readings = data.Select(r => TrackingCsvMoments.DatePartOf(Cell(r, moment.DatesIn))).ToList();
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
            var row = ReadRow(record, header, columns, moment, options, order);
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
            NamedDay = rows.Any(r => r.OnNamedDay) ? options.Day : null,
        };
    }

    /// <summary>
    /// Leaves a sheet with one way of saying when a row happened, where its header offers two.
    /// </summary>
    /// <remarks>
    /// A column carrying the date and the time together is the moment, and a date or a time column
    /// beside it is then left unclaimed — and so reported, like any other column nothing read —
    /// rather than joined on as well: two readings of one row's moment that disagree have no
    /// winner. The one exception is a reviewer who pointed at the date or the time column by hand
    /// while the combined one was only detected: a choice somebody made outranks a guess.
    /// </remarks>
    private static void SettleTheMoment(
        Dictionary<TrackingCsvField, int> columns, TrackingCsvColumnMapping mapping)
    {
        if (!columns.ContainsKey(TrackingCsvField.RecordedAt)
            || !(columns.ContainsKey(TrackingCsvField.Date) || columns.ContainsKey(TrackingCsvField.Time)))
        {
            return;
        }

        var partsWereNamed = mapping.NamedHeader(TrackingCsvField.Date) is not null
            || mapping.NamedHeader(TrackingCsvField.Time) is not null;
        if (partsWereNamed && mapping.NamedHeader(TrackingCsvField.RecordedAt) is null)
        {
            columns.Remove(TrackingCsvField.RecordedAt);
            return;
        }

        columns.Remove(TrackingCsvField.Date);
        columns.Remove(TrackingCsvField.Time);
    }

    /// <summary>
    /// Where a sheet keeps the moment of a row: in one column, in a date column and a time column,
    /// or in either of those alone.
    /// </summary>
    /// <param name="Whole">The column carrying date and time together, when the sheet has one.</param>
    /// <param name="Date">The date column of a sheet that keeps the two apart.</param>
    /// <param name="Time">The time column of a sheet that keeps the two apart, or has only times.</param>
    /// <param name="Name">The header, or the two headers, as a finding about the moment names them.</param>
    /// <param name="Day">
    /// The day the importer named, kept only for a sheet with times and no dates. A sheet that has
    /// a date column says its own days, and a day named beside it is not allowed to fill its gaps.
    /// </param>
    private sealed record MomentColumns(int? Whole, int? Date, int? Time, string Name, DateOnly? Day)
    {
        public static MomentColumns? Of(
            Dictionary<TrackingCsvField, int> columns, IReadOnlyList<string> header, DateOnly? day)
        {
            string Named(int index) => TripCsvValues.Tidy(header[index]);

            if (columns.TryGetValue(TrackingCsvField.RecordedAt, out var whole))
            {
                return new MomentColumns(whole, null, null, Named(whole), null);
            }

            var hasDate = columns.TryGetValue(TrackingCsvField.Date, out var date);
            var hasTime = columns.TryGetValue(TrackingCsvField.Time, out var time);
            return (hasDate, hasTime) switch
            {
                (true, true) => new MomentColumns(null, date, time, $"{Named(date)} + {Named(time)}", null),
                (true, false) => new MomentColumns(null, date, null, Named(date), null),
                (false, true) => new MomentColumns(null, null, time, Named(time), day),
                _ => null,
            };
        }

        /// <summary>The column whose cells say how this sheet writes a date.</summary>
        public int DatesIn => Whole ?? Date ?? Time!.Value;

        /// <summary>
        /// What a row says about when it happened, as one text the moment reader takes.
        /// </summary>
        /// <remarks>
        /// The two halves of a split sheet are joined with a space and read as if they had been
        /// written in one cell, so there is one reader and one set of rules. A half that is blank
        /// is simply absent from the text: the reader then reports a date with no time or a time
        /// with no date, which is exactly what the row is.
        /// </remarks>
        public string? TextOf(TripCsvRecord record, TrackingCsvOptions options)
        {
            string? Written(int? index) => index is { } at
                ? TripCsvValues.Single(Cell(record, at), options.SkipTokens)
                : null;

            if (Whole is not null)
            {
                return Written(Whole);
            }

            var date = Written(Date);
            var time = Written(Time);
            return date is not null && time is not null ? $"{date} {time}" : date ?? time;
        }
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
        MomentColumns momentColumns,
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

        var momentText = momentColumns.TextOf(record, options);
        var moment = TrackingCsvMoments.Read(momentText, order, options.Zone, momentColumns.Day);
        DateTimeOffset? at = null;
        switch (moment.Kind)
        {
            case TrackingCsvMomentKind.Read:
                at = moment.At;
                if (moment.RepeatedByClockChange)
                {
                    // Imported, at the first of the two instants the reading names, and told to
                    // the reviewer on the row — who is the only one who can know which was meant,
                    // and can say so by writing the offset in the cell.
                    diagnostics.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.MomentRepeatedByClockChange,
                        record.Line, momentColumns.Name, momentText));
                }

                break;
            case TrackingCsvMomentKind.SkippedByClockChange:
                // Refused rather than moved to the nearest hour that did exist: the sheet did not
                // say that hour, and a report filed where nobody wrote it is worse than a row sent
                // back to be corrected.
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentSkippedByClockChange,
                    record.Line, momentColumns.Name, momentText));
                break;
            case TrackingCsvMomentKind.Empty:
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentMissing,
                    record.Line, momentColumns.Name));
                break;
            case TrackingCsvMomentKind.DateWithoutTime:
                // Refused rather than filed at midnight. A day's reports would collapse onto one
                // instant and, with the caver, onto one upsert key, so re-importing the sheet
                // would overwrite each row with the next.
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentWithoutTime,
                    record.Line, momentColumns.Name, momentText));
                break;
            case TrackingCsvMomentKind.TimeWithoutDate:
                // Refused rather than put on the day of the row above or on a day named for the
                // sheet: this sheet writes its own dates, and a blank among them is a question for
                // whoever kept it.
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentWithoutDate,
                    record.Line, momentColumns.Name, momentText));
                break;
            default:
                diagnostics.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentUnreadable,
                    record.Line, momentColumns.Name, momentText));
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
        var toStationName = Text(TrackingCsvField.ToStation);
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

        if (state is not null
            && (stationName is not null || toStationName is not null || placeLabel is not null
                || depth is not 0 and not null))
        {
            // Quiet in the ordinary case: a club writes depth 0 beside "intrare", and nothing was
            // lost by dropping it. Said out loud only where the row claimed a real place as well,
            // because then the file disagrees with itself and somebody should know which half won.
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Warning, TrackingCsvProblem.StateOverridesPlace,
                record.Line, Named(TrackingCsvField.State), stateText));
        }

        if (state is null && toStationName is not null && stationName is null)
        {
            // Half a stretch. Not read as a report at the one station it does name, and not
            // dropped in favour of a depth or a declared place beside it: the row said "between"
            // and the other end is missing, which is for whoever kept the sheet to say.
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.ToStationWithoutStation,
                record.Line, Named(TrackingCsvField.ToStation), toStationName));
        }

        var note = FoldNote(Text(TrackingCsvField.Note), Text(TrackingCsvField.Details));

        // A row with a note and nothing else is a note, and is imported as one: a call that said
        // "water rising" and no place is a report all the same, and refusing it would cost the
        // one thing that row carried. Refused only when the note is empty as well, because then
        // the row says nothing about anybody.
        //
        // "Nothing else" means the cells were empty, not that they could not be read. A depth
        // that is not a number, or a standing word nobody listed, is the sheet saying where
        // somebody was in a way this reading did not follow — and filing the row as its note
        // would put that guess into a safety log: "afara" beside a note would leave the person
        // recorded as still underground, and a depth with a slip in it would, on a re-import,
        // write a note over the place the log already holds for that moment. Such a row is sent
        // back, with the finding about the cell beside the refusal saying what to change.
        var unreadPlace = depthProblem is not null || (stateText is not null && state is null);
        if (state is null && decides == TrackingCsvPlaceKind.None && (note is null || unreadPlace))
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.NoPlaceAndNoState, record.Line));
        }

        // Bounded here, on the row, because the bound is the stored column's and a row that
        // overran it would otherwise be previewed as fine and then fail the whole file's write —
        // every other row of the sheet with it, and without a line to go and fix. Measured on
        // the folded note rather than on either column, since folding is what gets stored.
        if (note is { Length: > TripTrackingRules.MaxNoteLength })
        {
            diagnostics.Add(new TrackingCsvDiagnostic(
                TrackingCsvSeverity.Error, TrackingCsvProblem.NoteTooLong,
                record.Line, Named(TrackingCsvField.Note),
                $"{note.Length} > {TripTrackingRules.MaxNoteLength}"));
        }

        return new TrackingCsvRow
        {
            Line = record.Line,
            At = at,
            OnNamedDay = at is not null && moment.OnNamedDay,
            Cavers = cavers,
            Team = Text(TrackingCsvField.Team),
            StationName = stationName,
            // A standing word wins over any place on its row, and takes both ends with it.
            ToStationName = state is null && stationName is not null ? toStationName : null,
            PlaceLabel = placeLabel,
            DepthM = depth,
            Decides = decides,
            State = state,
            Note = note,
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

        var written = WithoutMetres(text).Replace(',', '.');
        if (!decimal.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth))
        {
            return (null, TrackingCsvProblem.DepthUnreadable);
        }

        if (Math.Abs(depth) > TripTrackingRules.MaxDepthAbsM)
        {
            return (null, TrackingCsvProblem.DepthOutOfRange);
        }

        // In the form the log stores it — one decimal — from the moment it is read, so the preview
        // shows the depth the write will keep and the row is placed on that depth rather than on
        // a finer one the column would round away afterwards.
        return (TripTrackingRules.RecordedDepthM(depth), null);
    }

    /// <summary>
    /// The ways a depth cell says "metres" after its number, longest first.
    /// </summary>
    /// <remarks>
    /// Metres and nothing else. The column is in metres whether or not a cell says so, and a cell
    /// that names another unit is not converted: it is left unreadable and reported, because a
    /// sheet kept in feet or one stray "cm" is a question for whoever kept it, not arithmetic for
    /// an importer to do quietly.
    /// </remarks>
    private static readonly string[] MetreWords =
        ["meters", "metres", "meter", "metre", "metri", "metru", "m"];

    /// <summary>
    /// A depth cell with a trailing "m", "m.", "metri" or "meters" taken off: "96 m" is 96.
    /// </summary>
    /// <remarks>
    /// Taken off only where a digit stands directly before it, spaces aside, so the only cells
    /// this touches are a number followed by the unit. "96 cm" and "96 km" end in the letter too
    /// and are not among them: what would be left of either is not a number, and the cell is
    /// reported as it was written rather than read as 96 metres. A stop is taken off only
    /// together with a unit, so "96." stays the number it already was.
    /// </remarks>
    private static string WithoutMetres(string text)
    {
        var trimmed = text.TrimEnd();
        var body = trimmed.EndsWith('.') ? trimmed[..^1].TrimEnd() : trimmed;

        foreach (var word in MetreWords)
        {
            if (!body.EndsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var number = body[..^word.Length].TrimEnd();
            if (number.Length > 0 && char.IsAsciiDigit(number[^1]))
            {
                return number;
            }
        }

        return text;
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
