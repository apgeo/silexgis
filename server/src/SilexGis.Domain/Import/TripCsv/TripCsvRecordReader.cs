// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Import.TripCsv;

/// <summary>One record of a delimited file, and the physical line of the file it started on.</summary>
/// <param name="Line">1-based, counted in the file itself.</param>
/// <param name="Fields">The record's cells, unquoted.</param>
public sealed record TripCsvRecord(int Line, IReadOnlyList<string> Fields);

/// <summary>What reading the text produced: the records, and what was wrong with the text itself.</summary>
/// <param name="Records">Every record, including blank ones.</param>
/// <param name="UnterminatedQuoteLine">
/// The line a quoted field opened on that the text never closed, or null. Everything after that
/// quote was swallowed into the one cell, so the rest of the sheet is gone; the caller has to say
/// so rather than present what is left as a parse that worked.
/// </param>
public sealed record TripCsvReadResult(
    List<TripCsvRecord> Records,
    int? UnterminatedQuoteLine);

/// <summary>
/// Splitting delimited text into records, counting the lines of the file as the file has them.
///
/// <para>
/// A field is quoted only when its first character is a quote; anywhere else a quote is an
/// ordinary character, which is what lets a bare 45°42'36"N through a column of coordinates.
/// Inside a quoted field a doubled quote is one quote, and a newline is part of the value.
/// Carriage return, line feed and the pair of them all end a record. Quoted fields keep the
/// spaces they were written with; unquoted ones are trimmed.
/// </para>
///
/// <para>
/// The line number is carried through rather than derived from a record count. A blank line, or
/// a value carrying a newline, moves the two apart, and a number that does not point at the line
/// the reviewer sees in their editor sends them to the wrong row.
/// </para>
/// </summary>
public static class TripCsvRecordReader
{
    /// <summary>
    /// Reads every record, including blank ones — deciding that a blank line means nothing is a
    /// judgement for the caller, who is the one that has to report it.
    /// </summary>
    public static TripCsvReadResult Read(string? text, char delimiter)
    {
        var records = new List<TripCsvRecord>();
        if (string.IsNullOrEmpty(text))
        {
            return new TripCsvReadResult(records, null);
        }

        // The byte-order mark is stripped once, here. A reader that leaves it in place carries it
        // into the first header's name, which then matches no mapping and fails every row of the
        // file for a reason that is invisible in an editor.
        var span = text;
        if (span.Length > 0 && span[0] == '\uFEFF')
        {
            span = span[1..];
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;
        var any = false;
        var quoteOpenedOn = 0;

        void EndField()
        {
            fields.Add(quoted ? field.ToString() : field.ToString().Trim());
            field.Clear();
            quoted = false;
            fieldStarted = false;
        }

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < span.Length && span[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }

                    continue;
                }

                if (ch == '\n')
                {
                    line++;
                }
                else if (ch == '\r')
                {
                    line++;
                    if (i + 1 < span.Length && span[i + 1] == '\n')
                    {
                        i++;
                    }

                    field.Append('\n');
                    continue;
                }

                field.Append(ch);
                continue;
            }

            if (ch == '"' && !fieldStarted)
            {
                inQuotes = true;
                quoted = true;
                quoteOpenedOn = line;
                fieldStarted = true;
                any = true;
                continue;
            }

            if (ch == delimiter)
            {
                EndField();
                any = true;
                continue;
            }

            if (ch is '\r' or '\n')
            {
                EndField();
                records.Add(new TripCsvRecord(recordLine, fields));
                fields = [];
                any = false;
                line++;
                if (ch == '\r' && i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i++;
                }

                recordLine = line;
                continue;
            }

            fieldStarted = true;
            any = true;
            field.Append(ch);
        }

        // A file that does not end with a newline still ends with a record; one that does must not
        // gain an empty record for the newline itself.
        if (any || field.Length > 0 || fields.Count > 0)
        {
            EndField();
            records.Add(new TripCsvRecord(recordLine, fields));
        }

        // A quote nobody closed is the one malformation that costs the whole rest of the file
        // rather than one row: every line after it becomes part of a single cell. It is also the
        // one that leaves behind a result which looks like a short but successful parse, so it is
        // reported rather than inferred from a row count nobody is comparing against anything.
        return new TripCsvReadResult(records, inQuotes ? quoteOpenedOn : null);
    }
}
