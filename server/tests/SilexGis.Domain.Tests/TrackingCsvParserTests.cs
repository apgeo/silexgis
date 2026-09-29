// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading a sheet of tracking reports: what the built-in Romanian spellings recognise, which of
/// three place columns wins, and what happens to a row that cannot be placed in time.
/// </summary>
public class TrackingCsvParserTests
{
    /// <summary>
    /// An invented sample in the shape these sheets arrive in: a byte-order mark, CRLF endings,
    /// unpadded dates, a party in one quoted cell, a comma for a decimal mark, and columns left
    /// empty. The names are placeholders, not a roster.
    /// </summary>
    private const string Sample =
        "﻿Data si ora,Adancime,Statie,Loc,Speologi,Echipa,Nota,Stare\r\n"
        + "12.09.2026 08:15,0,,,\"Ion A.; Maria P.\",Echipa 1,pornire,intrare\r\n"
        + "12.09.2026 09:40,96,,,Ion A.,Echipa 1,,\r\n"
        + "12.09.2026 10:05,,,Meandru,Maria P.,Echipa 1,apa mare,\r\n"
        + "12.09.2026 11:30,120,3.14,,Ion A.,Echipa 1,,\r\n"
        + "12.09.2026 14:00,-12,,,\"Ion A.; Maria P.\",Echipa 1,galerie fosila,\r\n"
        + "12.09.2026 16:45,0,,,\"Ion A.; Maria P.\",Echipa 1,,iesire\r\n";

    [Fact]
    public void A_club_sheet_of_reports_is_read_whole_by_the_spellings_it_shipped_with()
    {
        var result = TrackingCsvParser.Parse(Sample);

        result.Readable.ShouldBeTrue();
        result.Header.Count.ShouldBe(8);
        result.UnmappedColumns.ShouldBeEmpty();
        result.ResolvedColumns[TrackingCsvField.RecordedAt].ShouldBe("Data si ora");
        result.ResolvedColumns[TrackingCsvField.Cavers].ShouldBe("Speologi");
        result.ResolvedColumns[TrackingCsvField.Place].ShouldBe("Loc");
        result.ResolvedColumns[TrackingCsvField.State].ShouldBe("Stare");
        result.Rows.Count.ShouldBe(6);
        result.Rows.ShouldAllBe(r => r.Importable);

        // Every date here is 12.09, which is a real date read either way, so nothing in the file
        // settles the order and the caller's preference stands.
        result.DateOrder.ShouldBe(TripCsvDateOrder.DayFirst);
        result.DateOrderSource.ShouldBe(TripCsvDateOrderSource.Stated);
    }

    [Fact]
    public void One_date_that_can_only_be_read_one_way_settles_the_order_for_the_whole_file()
    {
        // Asked once of the column rather than per row: 12.09 is a real date either way, and a
        // reader that guessed row by row would import one row's day from a different calendar
        // than its neighbours'.
        var result = TrackingCsvParser.Parse(
            "Data si ora,Adancime,Speologi\r\n"
            + "12.09.2026 09:00,96,Ion\r\n"
            + "25.09.2026 09:00,96,Ion\r\n",
            TrackingCsvOptions.Default with { DateOrder = TripCsvDateOrder.MonthFirst });

        result.DateOrder.ShouldBe(TripCsvDateOrder.DayFirst);
        result.DateOrderSource.ShouldBe(TripCsvDateOrderSource.File);
        result.Rows[0].At!.Value.Month.ShouldBe(9);
        result.Rows[0].At!.Value.Day.ShouldBe(12);
    }

    [Fact]
    public void A_party_in_one_cell_is_one_moment_about_several_people()
    {
        var rows = TrackingCsvParser.Parse(Sample).Rows;

        rows[0].Cavers.ShouldBe(["Ion A.", "Maria P."]);
        rows[0].Team.ShouldBe("Echipa 1");
        rows[1].Cavers.ShouldBe(["Ion A."]);
    }

    [Fact]
    public void Going_in_and_coming_out_are_read_off_the_standing_column_not_off_a_depth_of_zero()
    {
        var rows = TrackingCsvParser.Parse(Sample).Rows;

        rows[0].State.ShouldBe(TripPositionEventKind.Entered);
        rows[0].Kind.ShouldBe(TripPositionEventKind.Entered);
        rows[5].State.ShouldBe(TripPositionEventKind.Exited);

        // Quiet about the zero beside it: that is the shape a club writes, and nothing was lost.
        rows[0].Diagnostics.ShouldBeEmpty();
        rows[5].Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_station_outranks_a_place_which_outranks_a_depth()
    {
        var rows = TrackingCsvParser.Parse(Sample).Rows;

        // Depth alone.
        rows[1].Decides.ShouldBe(TrackingCsvPlaceKind.Depth);
        rows[1].DepthM.ShouldBe(96m);
        rows[1].Kind.ShouldBe(TripPositionEventKind.AtDepth);

        // A declared place name, with no depth beside it.
        rows[2].Decides.ShouldBe(TrackingCsvPlaceKind.Place);
        rows[2].PlaceLabel.ShouldBe("Meandru");
        // A declared place is a depth and a station both, so it is recorded as the kind that can
        // carry the two.
        rows[2].Kind.ShouldBe(TripPositionEventKind.AtDepth);

        // Both a station and a depth: the station is what somebody read off the survey.
        rows[3].Decides.ShouldBe(TrackingCsvPlaceKind.Station);
        rows[3].StationName.ShouldBe("3.14");
        rows[3].DepthM.ShouldBe(120m);
        rows[3].Kind.ShouldBe(TripPositionEventKind.AtStation);
    }

    [Fact]
    public void A_depth_keeps_the_sign_it_was_written_with()
    {
        // A cave has passage above its entrance as well as below, the stored field is signed for
        // that reason, and -12 in a sheet that also writes 96 is a different place, not a typo.
        var rows = TrackingCsvParser.Parse(Sample).Rows;

        rows[4].DepthM.ShouldBe(-12m);
    }

    [Fact]
    public void A_comma_is_a_decimal_mark_because_that_is_what_a_local_spreadsheet_exports()
    {
        var rows = Parse("12.09.2026 09:00,\"96,5\",Ion").Rows;

        rows[0].DepthM.ShouldBe(96.5m);
        rows[0].Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_wall_clock_time_is_taken_at_face_value_and_never_shifted_into_the_servers_zone()
    {
        // The regression this reader exists for: asking the framework to parse an offset-less cell
        // hands back the server's own zone, so the same sheet would import hours out on one
        // installation and right on another.
        var rows = Parse("12.09.2026 14:30,96,Ion").Rows;

        rows[0].At.ShouldBe(new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_cell_that_states_its_own_offset_is_honoured()
    {
        var rows = Parse("2026-09-12T14:30:00+03:00,96,Ion").Rows;

        rows[0].At.ShouldBe(new DateTimeOffset(2026, 9, 12, 11, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_date_with_no_time_is_refused_rather_than_filed_at_midnight()
    {
        // Filed at midnight, a day's reports collapse onto one instant and — with the caver —
        // onto one upsert key, so re-importing the sheet would overwrite each row with the next.
        var row = Parse("12.09.2026,96,Ion").Rows[0];

        row.Importable.ShouldBeFalse();
        row.Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.MomentWithoutTime
            && d.Severity == TrackingCsvSeverity.Error);
    }

    [Fact]
    public void A_row_about_nobody_and_a_row_about_nowhere_are_both_refused_with_their_own_reason()
    {
        var rows = Parse(
            "12.09.2026 09:00,96,\r\n"
            + "12.09.2026 10:00,,Ion").Rows;

        rows[0].Importable.ShouldBeFalse();
        rows[0].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.NoCavers);

        rows[1].Importable.ShouldBeFalse();
        rows[1].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.NoPlaceAndNoState);
    }

    [Fact]
    public void A_standing_beside_a_real_place_is_reported_rather_than_silently_dropped()
    {
        var row = TrackingCsvParser.Parse(
            "Data,Adancime,Statie,Speologi,Stare\r\n"
            + "12.09.2026 09:00,150,3.14,Ion,intrare\r\n").Rows[0];

        row.Kind.ShouldBe(TripPositionEventKind.Entered);
        row.Importable.ShouldBeTrue();
        row.Diagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.StateOverridesPlace
            && d.Severity == TrackingCsvSeverity.Warning);
    }

    [Fact]
    public void A_word_neither_state_list_knows_is_reported_and_the_row_still_imports()
    {
        var row = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Stare\r\n"
            + "12.09.2026 09:00,96,Ion,bivuac\r\n").Rows[0];

        row.State.ShouldBeNull();
        row.Kind.ShouldBe(TripPositionEventKind.AtDepth);
        row.Importable.ShouldBeTrue();
        row.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.StateWordUnknown);
    }

    [Fact]
    public void A_second_free_text_column_is_folded_into_the_note_rather_than_lost()
    {
        var row = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Nota,Detalii\r\n"
            + "12.09.2026 09:00,96,Ion,apa mare,sifon inchis\r\n").Rows[0];

        row.Note.ShouldBe("apa mare — sifon inchis");
    }

    [Fact]
    public void A_sheet_with_no_moment_column_is_refused_once_for_the_file_and_not_once_per_row()
    {
        var result = TrackingCsvParser.Parse(
            "Adancime,Speologi\r\n96,Ion\r\n120,Ion\r\n");

        result.Readable.ShouldBeFalse();
        result.Rows.ShouldBeEmpty();
        result.FileDiagnostics.Count(d => d.Problem == TrackingCsvProblem.MomentColumnMissing)
            .ShouldBe(1);
    }

    [Fact]
    public void A_header_nothing_claimed_is_named_rather_than_dropped()
    {
        // A misspelled header is otherwise silent: the column simply never arrives.
        var result = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Adncime\r\n12.09.2026 09:00,96,Ion,120\r\n");

        result.UnmappedColumns.ShouldBe(["Adncime"]);
        result.FileDiagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.UnmappedColumn && d.Column == "Adncime");
    }

    [Fact]
    public void A_hand_named_column_is_looked_for_under_that_name_alone()
    {
        var mapping = TrackingCsvColumnMapping.Auto
            .With(TrackingCsvField.Cavers, "Cine")
            .With(TrackingCsvField.Depth, "Cota m");

        var result = TrackingCsvParser.Parse(
            "Data,Cota m,Cine\r\n12.09.2026 09:00,96,Ion\r\n", mapping: mapping);

        result.ResolvedColumns[TrackingCsvField.Cavers].ShouldBe("Cine");
        result.ResolvedColumns[TrackingCsvField.Depth].ShouldBe("Cota m");
        result.Rows[0].Cavers.ShouldBe(["Ion"]);
    }

    [Fact]
    public void A_named_column_the_file_does_not_have_is_said_out_loud()
    {
        var mapping = TrackingCsvColumnMapping.Auto.With(TrackingCsvField.Team, "Grupa");

        var result = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi\r\n12.09.2026 09:00,96,Ion\r\n", mapping: mapping);

        result.FileDiagnostics.ShouldContain(d =>
            d.Problem == TrackingCsvProblem.NamedColumnMissing && d.Column == "Grupa");
        result.Rows[0].Team.ShouldBeNull();
    }

    [Fact]
    public void A_blank_line_costs_no_row_and_a_ragged_one_costs_only_a_warning()
    {
        var result = Parse(
            "12.09.2026 09:00,96,Ion\r\n"
            + "\r\n"
            + "12.09.2026 10:00,120\r\n");

        result.Rows.Count.ShouldBe(2);
        result.Rows[1].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.RaggedRow);
    }

    [Fact]
    public void A_depth_no_cave_has_is_refused_as_a_depth_rather_than_imported()
    {
        var row = Parse("12.09.2026 09:00,99999,Ion").Rows[0];

        row.DepthM.ShouldBeNull();
        row.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.DepthOutOfRange);
        // Nothing is left saying where anybody was, so the row cannot be imported either.
        row.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.NoPlaceAndNoState);
    }

    [Fact]
    public void The_words_a_club_writes_its_standings_in_are_configured_not_guessed()
    {
        var options = TrackingCsvOptions.Default with
        {
            StateWords = new TrackingCsvStateWords { WentIn = ["down"], CameOut = ["up"] },
        };

        var rows = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Stare\r\n"
            + "12.09.2026 09:00,0,Ion,down\r\n"
            + "12.09.2026 17:00,0,Ion,intrare\r\n",
            options).Rows;

        rows[0].State.ShouldBe(TripPositionEventKind.Entered);
        // Named words replace the defaults rather than adding to them, so the shipped Romanian
        // spelling is no longer a standing here and is reported as a word nobody knows.
        rows[1].State.ShouldBeNull();
        rows[1].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.StateWordUnknown);
    }

    /// <summary>A three-column sheet: moment, depth, caver. Header supplied, rows given.</summary>
    private static TrackingCsvParseResult Parse(string rows) =>
        TrackingCsvParser.Parse("Data si ora,Adancime,Speologi\r\n" + rows + "\r\n");
}
