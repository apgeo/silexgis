// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// One report of a tracking log, as a sheet is to say it.
/// </summary>
/// <remarks>
/// Textual about the person and the team, as a sheet is, and already decided about the place: a
/// station or a depth that the reader of the sheet may not be told arrives here as null, and the
/// writer then says so in a way the importer refuses. Nothing here is an id — a sheet leaves the
/// installation, and an id means something only inside it.
/// </remarks>
/// <param name="At">The instant the report is about.</param>
/// <param name="Caver">The person's name, as the reader of the sheet is shown it.</param>
/// <param name="Team">The team's title, where the report names one.</param>
/// <param name="Kind">What the report is.</param>
/// <param name="StationName">The station of a station report; null where it is kept back.</param>
/// <param name="DepthM">The depth of a depth report; null where it is kept back.</param>
/// <param name="Note">The report's note.</param>
public sealed record TrackingCsvExportRow(
    DateTimeOffset At,
    string Caver,
    string? Team,
    TripPositionEventKind Kind,
    string? StationName,
    decimal? DepthM,
    string? Note,
    string? ToStationName = null);

/// <summary>
/// A tracking log written as the sheet the importer reads: the importer's own columns, in its own
/// spellings, one report on each row.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one home of the sheet's header.</b> The sample sheet offered for download and a log
/// written out are the same layout because both take their first line from here, and the columns
/// are spelled in the first spelling the detector lists for each — so a sheet written by this
/// installation is read back by it without a word of configuration.
/// </para>
/// <para>
/// <b>One report on a row, never a party.</b> The importer splits the people cell, so a row could
/// carry several names; it would buy nothing and would make every name carrying a comma a row that
/// reads back as two people.
/// </para>
/// <para>
/// <b>The moment is an instant with its offset, at full precision.</b> A report is found again by
/// the person and the exact instant, so a moment written to the minute would come back as a second
/// report beside the first instead of as the first. And an instant that states its offset is read
/// the same whatever zone the importer is later told the sheet was kept in.
/// </para>
/// <para>
/// <b>A station for a station report, a depth for a depth report, and never both.</b> On reading,
/// a station outranks a depth: a depth report written with the station it was resolved to would
/// come back as a station report and lose its depth.
/// </para>
/// <para>
/// <b>A place that is not written is said to be kept back, not left blank.</b> A blank place beside
/// a note reads as a note, and re-importing that over the log would replace a position with
/// nothing. Such a row carries <see cref="TrackingCsvStateWords.Withheld"/> in its state column,
/// which the importer refuses whatever word lists it is given.
/// </para>
/// </remarks>
public static class TrackingCsvWriter
{
    private const string LineEnd = "\r\n";

    /// <summary>The columns of the sheet, in the order they are written.</summary>
    public static IReadOnlyList<TrackingCsvField> Columns { get; } =
    [
        TrackingCsvField.RecordedAt, TrackingCsvField.Depth, TrackingCsvField.Station,
        TrackingCsvField.Place, TrackingCsvField.Cavers, TrackingCsvField.Team,
        TrackingCsvField.Note, TrackingCsvField.State,
    ];

    /// <summary>What each of <see cref="Columns"/> is headed, in the same order.</summary>
    /// <remarks>
    /// Without diacritics on purpose: these are the forms the detector compares in, and a file
    /// opened by a tool that guesses the wrong code page still has a header it can be read by.
    /// </remarks>
    public static IReadOnlyList<string> Header { get; } =
        ["Data si ora", "Adancime", "Statie", "Loc", "Speologi", "Echipa", "Nota", "Stare"];

    /// <summary>The header as the first line of a sheet, line end included.</summary>
    public static string HeaderLine { get; } = string.Join(',', Header) + LineEnd;

    /// <summary>
    /// The heading of the column that carries the far end of a stretch, written straight after the
    /// station's own column and only into a sheet that has a stretch to say.
    /// </summary>
    /// <remarks>
    /// Not one of the fixed columns, on purpose. Nearly every log holds no stretch, and a column
    /// that is empty on every row of nearly every sheet is one more thing for somebody keeping the
    /// sheet by hand to wonder about. The reader recognises the heading whenever it is there, and
    /// a sheet without it says nothing about stretches — so a log written out without the column
    /// reads back exactly as one written out with it and left empty would.
    /// </remarks>
    public const string ToStationHeader = "Pana la statia";

    /// <summary>
    /// Where the station's column stands among <see cref="Columns"/>: the far end's column is
    /// written straight after it.
    /// </summary>
    private static readonly int StationColumn = Columns.ToList().IndexOf(TrackingCsvField.Station);

    /// <summary>
    /// The reports as a sheet, in the order given, under <see cref="HeaderLine"/>.
    /// </summary>
    /// <remarks>
    /// The standing words are the shipped ones, first spelling of each list: a sheet written here
    /// has to be readable by an importer that was told nothing.
    /// </remarks>
    public static string Write(IEnumerable<TrackingCsvExportRow> rows)
    {
        var words = TrackingCsvStateWords.Default;
        var log = rows as IReadOnlyCollection<TrackingCsvExportRow> ?? [.. rows];
        var stretches = log.Any(IsStretch);
        var sheet = new StringBuilder(
            stretches
                ? string.Join(',', Header.Take(StationColumn + 1).Append(ToStationHeader).Concat(Header.Skip(StationColumn + 1)))
                    + LineEnd
                : HeaderLine);
        foreach (var row in log)
        {
            string? depth = null;
            string? station = null;
            string? toStation = null;
            string? state = null;
            switch (row.Kind)
            {
                case TripPositionEventKind.Entered:
                    state = words.WentIn[0];
                    break;
                case TripPositionEventKind.Exited:
                    state = words.CameOut[0];
                    break;
                case TripPositionEventKind.Note:
                    state = words.Noted[0];
                    break;
                case TripPositionEventKind.AtStation when !string.IsNullOrWhiteSpace(row.StationName):
                    station = row.StationName;
                    toStation = IsStretch(row) ? row.ToStationName : null;
                    break;
                case TripPositionEventKind.AtDepth when row.DepthM is { } metres:
                    depth = metres.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    // A placed report with no place to write — kept back from this reader, or a
                    // kind this writer was never taught. Either way the row must not read back as
                    // something it is not.
                    state = TrackingCsvStateWords.Withheld;
                    break;
            }

            sheet.Append(Moment(row.At)).Append(',')
                // A number, written bare: "-40" is a depth above the entrance and has to stay one.
                .Append(depth).Append(',')
                .Append(TextCell(station)).Append(',');
            if (stretches) sheet.Append(TextCell(toStation)).Append(',');
            sheet
                // The place column stays empty: a report keeps the depth a declared name stood
                // for, not the name, and the depth is what is written.
                .Append(',')
                .Append(TextCell(row.Caver)).Append(',')
                .Append(TextCell(row.Team)).Append(',')
                .Append(TextCell(row.Note)).Append(',')
                .Append(state)
                .Append(LineEnd);
        }

        return sheet.ToString();
    }

    /// <summary>
    /// Whether a row is a stretch this sheet can write: a station report with both ends. A far end
    /// beside no station — which is what a place kept back from the reader looks like — is not.
    /// </summary>
    private static bool IsStretch(TrackingCsvExportRow row) =>
        row.Kind == TripPositionEventKind.AtStation
        && !string.IsNullOrWhiteSpace(row.StationName)
        && !string.IsNullOrWhiteSpace(row.ToStationName);

    /// <summary>An instant as the sheet writes it: UTC, to the last digit stored, marked as UTC.</summary>
    public static string Moment(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Free text as one cell: guarded against being run as a formula, and quoted where it must be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is opened in a spreadsheet by somebody who did not type the notes in it, and a
    /// spreadsheet runs a cell that begins with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c>. Such a
    /// cell is written with a tab in front, inside quotes, which makes it text to the spreadsheet.
    /// A tab rather than an apostrophe because the importer drops leading white space from every
    /// cell and keeps an apostrophe: the guarded text reads back as exactly what was stored, where
    /// an apostrophe would come back as part of a name that then matches nobody.
    /// </para>
    /// <para>
    /// Quoted where the text holds the delimiter, a quote or a line break, and where it begins or
    /// ends with white space — an unquoted cell is trimmed by most readers, this one included.
    /// </para>
    /// </remarks>
    public static string TextCell(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var guarded = text[0] is '=' or '+' or '-' or '@' ? "\t" + text : text;
        var quote = char.IsWhiteSpace(guarded[0]) || char.IsWhiteSpace(guarded[^1])
            || guarded.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return quote ? "\"" + guarded.Replace("\"", "\"\"") + "\"" : guarded;
    }
}
