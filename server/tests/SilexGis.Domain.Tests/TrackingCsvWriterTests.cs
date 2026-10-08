// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A tracking log written as a sheet, and that sheet read by the importer: the two are inverses,
/// and every test here goes through the real reader rather than describing the text.
/// </summary>
public class TrackingCsvWriterTests
{
    private static readonly DateTimeOffset Nine = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    private static TrackingCsvExportRow Row(
        TripPositionEventKind kind, string? station = null, decimal? depth = null, string? note = null,
        DateTimeOffset? at = null, string caver = "Ion Popescu", string? team = "Echipa 1") =>
        new(at ?? Nine, caver, team, kind, station, depth, note);

    private static TrackingCsvParseResult Read(params TrackingCsvExportRow[] rows) =>
        TrackingCsvParser.Parse(TrackingCsvWriter.Write(rows));

    [Fact]
    public void Every_column_is_headed_by_a_spelling_the_detector_claims_for_that_column()
    {
        TrackingCsvWriter.Header.Count.ShouldBe(TrackingCsvWriter.Columns.Count);
        for (var i = 0; i < TrackingCsvWriter.Columns.Count; i++)
        {
            TrackingCsvColumnMapping.CandidatesFor(TrackingCsvWriter.Columns[i])
                .ShouldContain(TripImportNames.Key(TrackingCsvWriter.Header[i]));
        }

        // And the reader, told nothing, resolves each of them to the column it was written as.
        var read = Read(Row(TripPositionEventKind.Entered));
        read.Readable.ShouldBeTrue();
        read.UnmappedColumns.ShouldBeEmpty();
        foreach (var (field, header) in TrackingCsvWriter.Columns.Zip(TrackingCsvWriter.Header))
        {
            read.ResolvedColumns[field].ShouldBe(header);
        }
    }

    [Fact]
    public void A_stretch_adds_its_column_beside_the_station_and_reads_back_as_the_stretch_it_was()
    {
        var stretch = Row(TripPositionEventKind.AtStation, station: "upper.2", at: Nine) with { ToStationName = "deep.3" };
        var sheet = TrackingCsvWriter.Write(
        [
            stretch,
            Row(TripPositionEventKind.AtStation, station: "deep.3", at: Nine.AddMinutes(10)),
            Row(TripPositionEventKind.AtDepth, depth: 96m, at: Nine.AddMinutes(20)),
        ]);

        sheet.Split("\r\n")[0].ShouldBe("Data si ora,Adancime,Statie,Pana la statia,Loc,Speologi,Echipa,Nota,Stare");
        TrackingCsvColumnMapping.CandidatesFor(TrackingCsvField.ToStation)
            .ShouldContain(TripImportNames.Key(TrackingCsvWriter.ToStationHeader));

        var read = TrackingCsvParser.Parse(sheet);
        read.FileDiagnostics.ShouldBeEmpty();
        read.UnmappedColumns.ShouldBeEmpty();
        read.ResolvedColumns[TrackingCsvField.ToStation].ShouldBe(TrackingCsvWriter.ToStationHeader);
        read.Rows.ShouldAllBe(r => r.Diagnostics.Count == 0);
        read.Rows.Select(r => (r.Kind, r.StationName, r.ToStationName, r.DepthM)).ShouldBe(
        [
            (TripPositionEventKind.AtStation, "upper.2", "deep.3", null),
            (TripPositionEventKind.AtStation, "deep.3", null, null),
            (TripPositionEventKind.AtDepth, null, null, 96m),
        ]);

        // The twin: a log with no stretch is written exactly as it always was, column for column.
        var plain = TrackingCsvWriter.Write([stretch with { ToStationName = null }]);
        plain.ShouldStartWith(TrackingCsvWriter.HeaderLine);
        plain.ShouldNotContain(TrackingCsvWriter.ToStationHeader);
        TrackingCsvParser.Parse(plain).ResolvedColumns.ShouldNotContainKey(TrackingCsvField.ToStation);
    }

    [Fact]
    public void A_far_end_beside_a_place_kept_back_writes_no_stretch()
    {
        // What a withheld station report looks like to the writer: its kind and no place. The far
        // end is withheld with the first, so there is none here either — and were one handed over
        // by mistake, it is not written beside a station that is not.
        var sheet = TrackingCsvWriter.Write(
            [Row(TripPositionEventKind.AtStation) with { ToStationName = "deep.3" }]);

        sheet.ShouldStartWith(TrackingCsvWriter.HeaderLine);
        sheet.ShouldNotContain("deep.3");
        sheet.ShouldContain(TrackingCsvStateWords.Withheld);
    }

    [Fact]
    public void Each_kind_of_report_reads_back_as_the_report_it_was()
    {
        var read = Read(
            Row(TripPositionEventKind.Entered, note: "pornire", at: Nine),
            Row(TripPositionEventKind.AtStation, station: "deep.3", note: "la baza puitului", at: Nine.AddMinutes(10)),
            Row(TripPositionEventKind.AtDepth, depth: 96.5m, at: Nine.AddMinutes(20)),
            Row(TripPositionEventKind.AtDepth, depth: -12m, at: Nine.AddMinutes(30)),
            Row(TripPositionEventKind.Note, note: "apa in crestere", at: Nine.AddMinutes(40)),
            Row(TripPositionEventKind.Note, at: Nine.AddMinutes(50)),
            Row(TripPositionEventKind.Exited, at: Nine.AddMinutes(60), team: null));

        read.FileDiagnostics.ShouldBeEmpty();
        read.Rows.ShouldAllBe(r => r.Diagnostics.Count == 0);
        read.Rows.Select(r => r.Kind).ShouldBe(
        [
            TripPositionEventKind.Entered, TripPositionEventKind.AtStation, TripPositionEventKind.AtDepth,
            TripPositionEventKind.AtDepth, TripPositionEventKind.Note, TripPositionEventKind.Note,
            TripPositionEventKind.Exited,
        ]);
        read.Rows.Select(r => r.At).ShouldBe(Enumerable.Range(0, 7).Select(i => (DateTimeOffset?)Nine.AddMinutes(10 * i)));
        read.Rows.ShouldAllBe(r => r.Cavers.Count == 1 && r.Cavers[0] == "Ion Popescu");

        // A station for a station report and a depth for a depth report, and never both: a depth
        // report written with a station beside it would come back as a station report.
        read.Rows[1].StationName.ShouldBe("deep.3");
        read.Rows[1].DepthM.ShouldBeNull();
        read.Rows[2].DepthM.ShouldBe(96.5m);
        read.Rows[2].StationName.ShouldBeNull();
        read.Rows[3].DepthM.ShouldBe(-12m);

        read.Rows.Select(r => r.Note).ShouldBe(
            ["pornire", "la baza puitului", null, null, "apa in crestere", null, null]);
        read.Rows.Take(6).ShouldAllBe(r => r.Team == "Echipa 1");
        read.Rows[6].Team.ShouldBeNull();
    }

    [Fact]
    public void A_moment_keeps_every_digit_it_was_stored_with_and_says_its_own_offset()
    {
        // A report is found again by the person and the exact instant, so a digit lost on the way
        // out is a second report on the way back in. Read here in a zone three hours off, which
        // would move a moment that did not state its offset — and does not move this one.
        var stored = new DateTimeOffset(2026, 9, 12, 14, 30, 7, TimeSpan.Zero).AddTicks(1_234_560);
        var elsewhere = new DateTimeOffset(2026, 9, 12, 17, 30, 0, TimeSpan.FromHours(3));

        TrackingCsvWriter.Moment(stored).ShouldBe("2026-09-12T14:30:07.123456Z");
        TrackingCsvWriter.Moment(elsewhere).ShouldBe("2026-09-12T14:30:00Z");

        TrackingCsvZones.TryFind("Europe/Bucharest", out var bucharest).ShouldBeTrue();
        var read = TrackingCsvParser.Parse(
            TrackingCsvWriter.Write([Row(TripPositionEventKind.Entered, at: stored)]),
            TrackingCsvOptions.Default with { Zone = bucharest });

        read.Rows.ShouldHaveSingleItem().At.ShouldBe(stored);
        read.Rows[0].Diagnostics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://example.invalid\")")]
    [InlineData("+40 de metri")]
    [InlineData("-12 fata de intrare")]
    [InlineData("@echipa de sus")]
    public void Text_a_spreadsheet_would_run_is_written_as_text_and_reads_back_unchanged(string text)
    {
        // The guard is in the bytes: the cell does not begin with the character a spreadsheet
        // acts on. And it costs nothing on the way back: the importer reads the stored text.
        var cell = TrackingCsvWriter.TextCell(text);
        cell.ShouldStartWith("\"\t");

        var read = Read(Row(TripPositionEventKind.Note, note: text, caver: text, team: text));
        var row = read.Rows.ShouldHaveSingleItem();
        row.Note.ShouldBe(text);
        row.Team.ShouldBe(text);
        row.Cavers.ShouldBe([text]);

        // Text that begins with anything else is left exactly as it is.
        TrackingCsvWriter.TextCell("la -12 m").ShouldBe("la -12 m");
    }

    [Fact]
    public void A_depth_above_the_entrance_is_written_as_the_number_it_is()
    {
        // Not guarded: "-40" is a place, and a tab in front of it would be a depth nobody can read.
        TrackingCsvWriter.Write([Row(TripPositionEventKind.AtDepth, depth: -40m)])
            .ShouldContain("Z,-40,,,");
    }

    [Fact]
    public void Text_holding_the_delimiter_a_quote_or_a_line_break_stays_one_cell()
    {
        var read = Read(Row(
            TripPositionEventKind.AtStation, station: "deep.3",
            note: "a zis \"apa\", apoi a inchis", team: "Echipa 1, de sus"));

        var row = read.Rows.ShouldHaveSingleItem();
        row.Diagnostics.ShouldBeEmpty();
        row.Note.ShouldBe("a zis \"apa\", apoi a inchis");
        row.Team.ShouldBe("Echipa 1, de sus");
        row.StationName.ShouldBe("deep.3");

        // A line break inside a note does not start a new row, though the importer folds it to a
        // space as it does in every cell.
        var broken = Read(
            Row(TripPositionEventKind.Note, note: "apa in crestere\r\nne intoarcem"),
            Row(TripPositionEventKind.Exited, at: Nine.AddMinutes(5)));
        broken.Rows.Count.ShouldBe(2);
        broken.Rows[0].Note.ShouldBe("apa in crestere ne intoarcem");
    }

    [Theory]
    [InlineData(TripPositionEventKind.AtStation)]
    [InlineData(TripPositionEventKind.AtDepth)]
    public void A_placed_report_whose_place_is_not_given_is_written_so_that_reading_it_back_refuses_it(
        TripPositionEventKind kind)
    {
        // With a note, which is the dangerous row: a blank place beside a note reads as a note.
        var sheet = TrackingCsvWriter.Write([Row(kind, note: "la baza puitului")]);
        sheet.ShouldEndWith($",la baza puitului,{TrackingCsvStateWords.Withheld}\r\n");

        var row = TrackingCsvParser.Parse(sheet).Rows.ShouldHaveSingleItem();
        row.Importable.ShouldBeFalse();
        row.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.NoPlaceAndNoState);

        // The same report with its place is read, so it is the missing place that was refused.
        var whole = kind == TripPositionEventKind.AtStation
            ? Row(kind, station: "deep.3", note: "la baza puitului")
            : Row(kind, depth: 96m, note: "la baza puitului");
        Read(whole).Rows.ShouldHaveSingleItem().Importable.ShouldBeTrue();
    }

    [Fact]
    public void A_log_with_nothing_in_it_is_a_header_and_nothing_else()
    {
        TrackingCsvWriter.Write([]).ShouldBe("Data si ora,Adancime,Statie,Loc,Speologi,Echipa,Nota,Stare\r\n");
    }
}
