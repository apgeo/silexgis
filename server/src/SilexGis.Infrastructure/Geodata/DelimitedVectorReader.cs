// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SilexGis.Domain.Import;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Reads delimited text — a spreadsheet of positions — into vector rows.
///
/// <para>
/// The one import format whose geometry is assembled rather than read, which is why it is
/// here rather than behind OGR: the CSV driver needs to be told which columns to use before
/// it will produce a geometry at all, and the whole difficulty of the format is working that
/// out from a file somebody exported from a phone app. Everything the file carries beyond the
/// coordinates becomes a property, so the same rules and the same review apply to it as to a
/// GPX — the parser differs, the staging does not.
/// </para>
/// </summary>
public static class DelimitedVectorReader
{
    /// <summary>Guard on a hand-made file: a row this wide is a parse gone wrong, not data.</summary>
    private const int MaxColumns = 512;

    public static VectorDataset Read(string absolutePath, DelimitedSourceOptions? options)
    {
        var lines = ReadRows(absolutePath, options?.Delimiter);
        if (lines.Header.Count == 0)
        {
            throw new VectorIOException("The file has no header row.");
        }

        var wktIndex = Resolve(lines.Header, options?.WktColumn, DelimitedSourceOptions.WktCandidates);
        var latIndex = Resolve(lines.Header, options?.LatitudeColumn, DelimitedSourceOptions.LatitudeCandidates);
        var lonIndex = Resolve(lines.Header, options?.LongitudeColumn, DelimitedSourceOptions.LongitudeCandidates);
        var elevationIndex = Resolve(
            lines.Header, options?.ElevationColumn, ImportAttributeMapping.ElevationCandidates);

        if (wktIndex < 0 && (latIndex < 0 || lonIndex < 0))
        {
            // Naming the columns the file does have is what turns this from "it did not work"
            // into something the importer can act on without opening the file in a text editor.
            throw new VectorIOException(
                "No coordinate columns were recognised. Name the latitude and longitude columns and read the "
                + $"file again. Columns found: {string.Join(", ", lines.Header)}.");
        }

        var wktReader = new WKTReader();
        var features = new List<VectorFeature>();
        var skipped = 0;

        foreach (var row in lines.Rows)
        {
            var geometry = wktIndex >= 0
                ? ParseWkt(wktReader, Value(row, wktIndex))
                : ParsePoint(Value(row, lonIndex), Value(row, latIndex), Value(row, elevationIndex));
            if (geometry is null)
            {
                skipped++;
                continue;
            }

            geometry.SRID = 4326;

            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Header.Count && i < row.Count; i++)
            {
                var key = lines.Header[i];
                if (key.Length > 0 && !properties.ContainsKey(key))
                {
                    properties[key] = row[i];
                }
            }

            features.Add(new VectorFeature(geometry, properties));
        }

        if (features.Count == 0)
        {
            throw new VectorIOException(
                skipped == 0
                    ? "The file holds no data rows."
                    : $"None of the {skipped} data rows carried a readable position.");
        }

        return new VectorDataset(features, 4326);
    }

    /// <summary>The header of a delimited file, for a screen that asks which column is which.</summary>
    public static IReadOnlyList<string> ReadHeader(string absolutePath, string? delimiter) =>
        ReadRows(absolutePath, delimiter, headerOnly: true).Header;

    private static Geometry? ParseWkt(WKTReader reader, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var geometry = reader.Read(value);
            return geometry.IsEmpty || !geometry.IsValid ? null : geometry;
        }
        catch (Exception e) when (e is NetTopologySuite.IO.ParseException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static Geometry? ParsePoint(string? longitude, string? latitude, string? elevation)
    {
        var x = CoordinateText.Parse(longitude);
        var y = CoordinateText.Parse(latitude);
        if (x is null || y is null || x is < -180 or > 180 || y is < -90 or > 90)
        {
            return null;
        }

        var z = CoordinateText.ParseNumber(elevation);
        Coordinate coordinate = z is null ? new Coordinate(x.Value, y.Value) : new CoordinateZ(x.Value, y.Value, z.Value);
        return new Point(coordinate);
    }

    private static string? Value(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    /// <summary>
    /// The column index for a role: the explicitly named one if it is there, else the first
    /// candidate name the header carries. Comparison is folded, so <c>Latitudine</c> and
    /// <c>LAT</c> are found as readily as <c>lat</c>.
    /// </summary>
    private static int Resolve(IReadOnlyList<string> header, string? chosen, IReadOnlyList<string> candidates)
    {
        var folded = header.Select(h => FoldedText.Of(h).Value.Trim()).ToList();

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var wanted = FoldedText.Of(chosen).Value.Trim();
            return folded.FindIndex(h => h == wanted);
        }

        foreach (var candidate in candidates)
        {
            var index = folded.FindIndex(h => h == candidate);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static (List<string> Header, List<List<string>> Rows) ReadRows(
        string absolutePath, string? delimiter, bool headerOnly = false)
    {
        // Detected rather than assumed: UTF-8 is the common case, but a file saved out of a
        // Romanian Excel is often Windows-1250 or -1252, and reading those as UTF-8 turns every
        // diacritic into a replacement character — which the term folding would then quietly
        // fail to match.
        using var reader = new StreamReader(absolutePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var firstLine = reader.ReadLine();
        if (firstLine is null)
        {
            return ([], []);
        }

        // The separator is sniffed from the first physical line, before anything knows how to
        // read a record — which is the only ordering available, and is right in practice: a
        // column name containing a newline is not a thing that happens.
        var separator = ResolveDelimiter(delimiter, firstLine);
        var header = SplitRow(CompleteRecord(reader, firstLine, separator), separator).Fields;

        if (header.Count > MaxColumns)
        {
            throw new VectorIOException($"The header has {header.Count} columns; at most {MaxColumns} are read.");
        }

        var rows = new List<List<string>>();
        if (!headerOnly)
        {
            string? line;
            while ((line = ReadLogicalLine(reader, separator)) is not null)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                rows.Add(SplitRow(line, separator).Fields);
            }
        }

        return (header, rows);
    }

    /// <summary>
    /// One record, which may span several physical lines: a quoted field is allowed to contain
    /// newlines, and a description field routinely does. Completeness is decided by parsing
    /// what has been read rather than by counting quote characters — a coordinate written
    /// <c>45°42'36"N</c> carries one quote mark in an unquoted field, and counting would read
    /// it as an unterminated string and swallow the next row.
    /// </summary>
    private static string? ReadLogicalLine(StreamReader reader, char separator) =>
        reader.ReadLine() is { } line ? CompleteRecord(reader, line, separator) : null;

    /// <summary>Reads on until the record is closed, or the file ends.</summary>
    private static string CompleteRecord(StreamReader reader, string firstLine, char separator)
    {
        var builder = new StringBuilder(firstLine);
        while (!SplitRow(builder.ToString(), separator).Complete)
        {
            var continuation = reader.ReadLine();
            if (continuation is null)
            {
                break; // an unterminated quote ends the file; take what there is
            }

            builder.Append('\n').Append(continuation);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The separator: the one that was asked for, or the candidate that appears most often
    /// outside quoted fields in the header. Ties break in the order the candidates are listed,
    /// so a header with one comma and one semicolon is read as comma-separated.
    /// </summary>
    private static char ResolveDelimiter(string? requested, string headerLine)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            return requested == "\\t" ? '\t' : requested[0];
        }

        var best = DelimitedSourceOptions.CandidateDelimiters[0];
        var bestCount = 0;
        foreach (var candidate in DelimitedSourceOptions.CandidateDelimiters)
        {
            // Counted through the same splitter that will do the real work, so the sniff and
            // the parse cannot disagree about what is inside a quoted field.
            var count = SplitRow(headerLine, candidate).Fields.Count - 1;
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// RFC 4180 field splitting. A field is quoted only when its <em>first</em> character is a
    /// quote; anywhere else a quote is an ordinary character, which is what lets a bare
    /// <c>45°42'36"N</c> through a column of coordinates. Inside a quoted field <c>""</c> is one
    /// quote. <c>Complete</c> is false when the record ended inside a quoted field, which is how
    /// a value containing a newline is recognised.
    /// </summary>
    private static (List<string> Fields, bool Complete) SplitRow(string line, char separator)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quotedField = false;
        var atFieldStart = true;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
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

            if (atFieldStart && ch == '"')
            {
                inQuotes = true;
                quotedField = true;
                atFieldStart = false;
                continue;
            }

            if (ch == separator)
            {
                fields.Add(Finish(field, quotedField));
                field.Clear();
                quotedField = false;
                atFieldStart = true;
                continue;
            }

            atFieldStart = false;
            field.Append(ch);
        }

        fields.Add(Finish(field, quotedField));
        return (fields, !inQuotes);
    }

    /// <summary>
    /// A quoted field is taken exactly as written — leading and trailing spaces inside the
    /// quotes were put there on purpose. An unquoted one is trimmed, because a space after a
    /// separator is formatting rather than data.
    /// </summary>
    private static string Finish(StringBuilder field, bool quoted) =>
        quoted ? field.ToString() : field.ToString().Trim();
}
