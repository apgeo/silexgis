// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Domain.Trips;

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
    public void A_sheet_kept_on_a_zones_clocks_is_read_at_the_offset_that_zone_kept_on_each_rows_date()
    {
        // One sheet running from summer into winter has two offsets in it, and a row that wrote
        // its own offset keeps it. Nothing is reported: reading in a zone is not a finding.
        var parsed = TrackingCsvParser.Parse(
            "Data si ora,Adancime,Speologi\r\n"
            + "12.07.2026 14:05,96,Ion\r\n"
            + "12.01.2026 14:05,96,Ion\r\n"
            + "2026-07-12T14:05:00+01:00,96,Ion\r\n",
            InBucharest);

        parsed.Rows.Select(r => r.At).ShouldBe(
        [
            new DateTimeOffset(2026, 7, 12, 11, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 12, 12, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 13, 5, 0, TimeSpan.Zero),
        ]);
        parsed.Rows.ShouldAllBe(r => r.Diagnostics.Count == 0);
        parsed.FileDiagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_row_at_a_time_the_clocks_skipped_is_refused_and_one_at_a_time_they_repeated_is_imported_with_a_warning()
    {
        var parsed = TrackingCsvParser.Parse(
            "Data si ora,Adancime,Speologi\r\n"
            + "29.03.2026 03:30,96,Ion\r\n"
            + "25.10.2026 03:30,96,Ion\r\n"
            + "25.10.2026 14:05,96,Ion\r\n",
            InBucharest);

        // Never on any clock in the zone: no instant, and the row is sent back naming its cell.
        var skipped = parsed.Rows[0];
        skipped.At.ShouldBeNull();
        skipped.Importable.ShouldBeFalse();
        var refusal = skipped.Diagnostics.ShouldHaveSingleItem();
        refusal.Problem.ShouldBe(TrackingCsvProblem.MomentSkippedByClockChange);
        refusal.Severity.ShouldBe(TrackingCsvSeverity.Error);
        refusal.Column.ShouldBe("Data si ora");
        refusal.Detail.ShouldBe("29.03.2026 03:30");

        // Twice on the clocks: imported as the first, and said so on the row.
        var repeated = parsed.Rows[1];
        repeated.At.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
        repeated.Importable.ShouldBeTrue();
        var warning = repeated.Diagnostics.ShouldHaveSingleItem();
        warning.Problem.ShouldBe(TrackingCsvProblem.MomentRepeatedByClockChange);
        warning.Severity.ShouldBe(TrackingCsvSeverity.Warning);
        warning.Detail.ShouldBe("25.10.2026 03:30");

        // The ordinary row beside them says nothing, and the same sheet read with no zone says
        // nothing about any of the three.
        parsed.Rows[2].Diagnostics.ShouldBeEmpty();
        TrackingCsvParser.Parse(
                "Data si ora,Adancime,Speologi\r\n29.03.2026 03:30,96,Ion\r\n25.10.2026 03:30,96,Ion\r\n")
            .Rows.ShouldAllBe(r => r.Importable && r.Diagnostics.Count == 0);
    }

    private static TrackingCsvOptions InBucharest
    {
        get
        {
            TrackingCsvZones.TryFind("Europe/Bucharest", out var zone).ShouldBeTrue();
            return new TrackingCsvOptions { Zone = zone };
        }
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
    public void A_note_longer_than_a_report_may_carry_is_refused_on_its_row_rather_than_failing_the_write()
    {
        // The bound is the stored column's. A row that overran it used to preview as fine and then
        // fail the whole sheet's write — every other row with it, and with nothing naming the line.
        var tooLong = new string('x', TripTrackingRules.MaxNoteLength + 1);
        var row = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Nota\r\n"
            + $"12.09.2026 09:00,96,Ion,{tooLong}\r\n").Rows[0];

        row.Importable.ShouldBeFalse();
        var problem = row.Diagnostics.ShouldHaveSingleItem();
        problem.Problem.ShouldBe(TrackingCsvProblem.NoteTooLong);
        problem.Severity.ShouldBe(TrackingCsvSeverity.Error);
        problem.Column.ShouldBe("Nota");

        // Exactly at the bound is still a note a report may carry.
        TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Nota\r\n"
            + $"12.09.2026 09:00,96,Ion,{tooLong[..^1]}\r\n").Rows[0].Importable.ShouldBeTrue();
    }

    [Fact]
    public void The_note_bound_is_measured_on_the_folded_note_because_that_is_what_gets_stored()
    {
        // Two columns each inside the bound, together past it: the fold is what is written, so the
        // fold is what is measured.
        var half = new string('y', TripTrackingRules.MaxNoteLength / 2 + 10);
        var row = TrackingCsvParser.Parse(
            "Data,Adancime,Speologi,Nota,Detalii\r\n"
            + $"12.09.2026 09:00,96,Ion,{half},{half}\r\n").Rows[0];

        row.Importable.ShouldBeFalse();
        row.Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.NoteTooLong);
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
    public void A_date_column_beside_a_time_column_is_joined_into_one_moment()
    {
        // The layout a hand-kept sheet often has, on whichever side of the sheet the two stand.
        var result = TrackingCsvParser.Parse(
            "Data,Ora,Adancime,Speologi\r\n12.09.2026,08:15,96,Ion\r\n13.09.2026,0940,120,Ion\r\n");

        result.Readable.ShouldBeTrue();
        result.ResolvedColumns[TrackingCsvField.Date].ShouldBe("Data");
        result.ResolvedColumns[TrackingCsvField.Time].ShouldBe("Ora");
        result.ResolvedColumns.ShouldNotContainKey(TrackingCsvField.RecordedAt);
        result.UnmappedColumns.ShouldBeEmpty();
        result.Rows.Select(r => r.At).ShouldBe(
        [
            new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 9, 40, 0, TimeSpan.Zero),
        ]);
        result.Rows.ShouldAllBe(r => r.Importable && !r.OnNamedDay);
        result.NamedDay.ShouldBeNull();

        TrackingCsvParser.Parse("Ora,Data,Speologi,Stare\r\n08:15,12.09.2026,Ion,intrare\r\n")
            .Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_joined_moment_is_read_under_the_same_order_offset_and_zone_rules_as_a_single_cell()
    {
        // One reader behind both layouts. The day order is settled over the date column, an offset
        // written beside the time is the cell's own, and a time that writes none is on the sheet's
        // zone.
        var bucharest = TrackingCsvZones.TryFind("Europe/Bucharest", out var zone) ? zone : null;
        bucharest.ShouldNotBeNull();

        var result = TrackingCsvParser.Parse(
            "Date,Time,Depth,Cavers\r\n"
            + "09/13/2026,14:05,96,Ion\r\n"
            + "09/05/2026,14:05 +01:00,96,Ion\r\n",
            TrackingCsvOptions.Default with { Zone = bucharest });

        result.DateOrder.ShouldBe(TripCsvDateOrder.MonthFirst);
        result.DateOrderSource.ShouldBe(TripCsvDateOrderSource.File);
        result.Rows[0].At.ShouldBe(new DateTimeOffset(2026, 9, 13, 11, 5, 0, TimeSpan.Zero));
        result.Rows[1].At.ShouldBe(new DateTimeOffset(2026, 9, 5, 13, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void In_a_two_column_sheet_a_missing_half_is_refused_on_its_row_and_no_day_is_carried_down()
    {
        // A day named for the sheet is not allowed to fill the gap either: this sheet writes its
        // own dates, and a blank among them is a question for whoever kept it.
        var result = TrackingCsvParser.Parse(
            "Data,Ora,Adancime,Speologi\r\n"
            + "12.09.2026,08:15,96,Ion\r\n"
            + ",09:40,120,Ion\r\n"
            + "12.09.2026,,150,Ion\r\n"
            + ",,150,Ion\r\n",
            TrackingCsvOptions.Default with { Day = new DateOnly(2026, 9, 12) });

        result.Readable.ShouldBeTrue();
        result.NamedDay.ShouldBeNull();
        result.Rows[0].Importable.ShouldBeTrue();
        var withoutDate = result.Rows[1].Diagnostics.Single(d => d.Severity == TrackingCsvSeverity.Error);
        withoutDate.Problem.ShouldBe(TrackingCsvProblem.MomentWithoutDate);
        withoutDate.Column.ShouldBe("Data + Ora");
        withoutDate.Detail.ShouldBe("09:40");
        result.Rows[2].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentWithoutTime);
        result.Rows[3].Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentMissing);
    }

    [Fact]
    public void A_column_carrying_both_is_the_moment_and_a_date_or_time_column_beside_it_is_left_unread()
    {
        // Two readings of one row's moment that disagree have no winner, so only one is taken —
        // and the other is named as unread rather than dropped in silence.
        var result = TrackingCsvParser.Parse(
            "Data si ora,Data,Ora,Speologi,Stare\r\n12.09.2026 08:15,01.01.2020,23:59,Ion,intrare\r\n");

        result.ResolvedColumns[TrackingCsvField.RecordedAt].ShouldBe("Data si ora");
        result.ResolvedColumns.ShouldNotContainKey(TrackingCsvField.Date);
        result.ResolvedColumns.ShouldNotContainKey(TrackingCsvField.Time);
        result.UnmappedColumns.ShouldBe(["Data", "Ora"]);
        result.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Pointing_at_the_date_and_time_columns_by_hand_outranks_a_combined_column_that_was_only_detected()
    {
        var mapping = TrackingCsvColumnMapping.Auto
            .With(TrackingCsvField.Date, "Ziua")
            .With(TrackingCsvField.Time, "Ceas");

        var result = TrackingCsvParser.Parse(
            "Timestamp,Ziua,Ceas,Speologi,Stare\r\n2020-01-01 23:59,12.09.2026,08:15,Ion,intrare\r\n",
            mapping: mapping);

        result.ResolvedColumns.ShouldNotContainKey(TrackingCsvField.RecordedAt);
        result.UnmappedColumns.ShouldBe(["Timestamp"]);
        result.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_lone_date_column_is_read_whole_as_the_moment_and_refused_row_by_row_when_it_has_no_time()
    {
        // A sheet whose one moment column happens to be headed "Data" reads as it always did.
        var whole = TrackingCsvParser.Parse("Data,Adancime,Speologi\r\n12.09.2026 09:00,96,Ion\r\n");
        whole.ResolvedColumns[TrackingCsvField.Date].ShouldBe("Data");
        whole.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));

        // And one with a date and no time anywhere is told so on each row, with the true reason.
        var dateOnly = TrackingCsvParser.Parse("Data,Adancime,Speologi\r\n12.09.2026,96,Ion\r\n");
        dateOnly.Readable.ShouldBeTrue();
        dateOnly.Rows.Single().Diagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentWithoutTime);
    }

    [Fact]
    public void A_sheet_of_times_with_no_dates_is_refused_once_for_the_file_until_its_day_is_named()
    {
        const string sheet = "Ora,Adancime,Speologi\r\n08:15,96,Ion\r\n0940,120,Ion\r\n";

        var refused = TrackingCsvParser.Parse(sheet);

        refused.Readable.ShouldBeFalse();
        refused.Rows.ShouldBeEmpty();
        var needsADay = refused.FileDiagnostics.Single(d => d.Severity == TrackingCsvSeverity.Error);
        needsADay.Problem.ShouldBe(TrackingCsvProblem.TimeColumnNeedsADay);
        needsADay.Column.ShouldBe("Ora");
        // Still answered, so a mapping screen can show which column was taken for the times.
        refused.ResolvedColumns[TrackingCsvField.Time].ShouldBe("Ora");

        var day = new DateOnly(2026, 9, 12);
        var read = TrackingCsvParser.Parse(sheet, TrackingCsvOptions.Default with { Day = day });

        read.Readable.ShouldBeTrue();
        read.NamedDay.ShouldBe(day);
        read.Rows.Select(r => r.At).ShouldBe(
        [
            new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 9, 40, 0, TimeSpan.Zero),
        ]);
        read.Rows.ShouldAllBe(r => r.OnNamedDay);
    }

    [Fact]
    public void A_named_day_is_read_in_the_sheets_zone_like_any_other()
    {
        TrackingCsvZones.TryFind("Europe/Bucharest", out var zone).ShouldBeTrue();

        var read = TrackingCsvParser.Parse(
            "Ora,Adancime,Speologi\r\n00:30,96,Ion\r\n",
            TrackingCsvOptions.Default with { Day = new DateOnly(2026, 9, 12), Zone = zone });

        // Half past midnight on the 12th in Bucharest is still the 11th in UTC.
        read.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 11, 21, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_time_column_whose_cells_write_their_own_dates_needs_no_day_and_ignores_one()
    {
        // A moment column under a modest header: it was read before a day could be named, and it
        // still is. A day named beside it changes nothing about a cell that says its own.
        const string sheet = "Ora,Adancime,Speologi\r\n12.09.2026 08:15,96,Ion\r\n";

        var read = TrackingCsvParser.Parse(sheet);
        read.Readable.ShouldBeTrue();
        read.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));

        var withADay = TrackingCsvParser.Parse(
            sheet, TrackingCsvOptions.Default with { Day = new DateOnly(2020, 1, 1) });
        withADay.Rows.Single().At.ShouldBe(new DateTimeOffset(2026, 9, 12, 8, 15, 0, TimeSpan.Zero));
        withADay.Rows.Single().OnNamedDay.ShouldBeFalse();
        withADay.NamedDay.ShouldBeNull();
    }

    [Fact]
    public void A_sheet_with_no_column_for_the_moment_in_any_layout_is_refused_as_missing_it()
    {
        var result = TrackingCsvParser.Parse("Adancime,Speologi\r\n96,Ion\r\n");

        result.Readable.ShouldBeFalse();
        result.FileDiagnostics.ShouldContain(d => d.Problem == TrackingCsvProblem.MomentColumnMissing);
        result.FileDiagnostics.ShouldNotContain(d => d.Problem == TrackingCsvProblem.TimeColumnNeedsADay);
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

    [Fact]
    public void Rows_copied_out_of_a_spreadsheet_are_read_as_a_sheet()
    {
        // What a spreadsheet puts on the clipboard, after a text box has held it: a tab between
        // cells and a bare line feed between rows — no commas, no carriage returns. Pasting rows
        // in place of choosing a file rests on this being a sheet like any other, so it is held
        // here rather than assumed from the two halves separately.
        var read = TrackingCsvParser.Parse(
            "Data si ora\tAdancime\tSpeologi\tObservatii\n"
            + "12.09.2026 09:00\t96\tIon\tapa mare, la sifon\n"
            + "12.09.2026 10:30\t120\tMaria Pop\t\n",
            TrackingCsvOptions.Default with { Delimiter = '\t' });

        read.Readable.ShouldBeTrue();
        read.Rows.Count.ShouldBe(2);
        read.Rows[0].At.ShouldBe(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));
        read.Rows[0].DepthM.ShouldBe(96);
        // A comma inside a cell is the cell's own: nothing but the tab divides a pasted row.
        read.Rows[0].Note.ShouldBe("apa mare, la sifon");
        read.Rows[1].At.ShouldBe(new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero));
        read.Rows[1].Cavers.ShouldBe(["Maria Pop"]);
    }

    /// <summary>A three-column sheet: moment, depth, caver. Header supplied, rows given.</summary>
    private static TrackingCsvParseResult Parse(string rows) =>
        TrackingCsvParser.Parse("Data si ora,Adancime,Speologi\r\n" + rows + "\r\n");
}
