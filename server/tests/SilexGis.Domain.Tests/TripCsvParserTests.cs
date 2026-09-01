// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading a club's trip spreadsheet: what the built-in Romanian profile recognises, what the
/// dialect accepts, and what the parser does with a file that is exactly as a club keeps it.
/// </summary>
public class TripCsvParserTests
{
    /// <summary>
    /// A four-row sheet in the shape these files arrive in: a byte-order mark, CRLF endings,
    /// unpadded numeric dates, quoted participant lists, and columns left empty.
    /// </summary>
    private const string Sample =
        "﻿Nr crt.,Data inceput,Data sfarsit,Titlu,Tara,Masiv/zona,Subzona,Pesteri,Propus de,Participanti,Detalii,Detalii2,Tip,Erori\r\n"
        + "1,5/1/2024,,Tabara Zona 2024,Romania,M. Capatanii,V. Cernei,,,\"Ion A., Adrian Baritiu, Ioana Camioana\",very nice trip,,tabara,various\r\n"
        + "2,5/11/2024,5/11/2024,pestera 1 titlu,Romania,M. Gutai,V. Verde,pestera 1 titlu,,\"Ion A., Adrian Baritiu, Ioana Campioana\",?,,pestera,\r\n"
        + "3,5/25/2024,5/25/2024,pestera 2,Romania,M. Tibles,V. Mare,pestera 2,,\"Ion A., Adrian Baritiu, Alta Ioana\",,,pestera,\r\n"
        + "4,7/6/2024,7/6/2024,pestera 1,Romania,M. Apuseni,M. Apuseni,pestera 1,,\"Gheorghe, Ioana Camioana\",drumuire suprafata intre P. Lanovici si P. Smaltuita,,suprafata,\r\n";

    [Fact]
    public void A_club_sheet_is_read_whole_by_the_profile_it_shipped_with()
    {
        var result = TripCsvParser.Parse(Sample);

        result.Header.Count.ShouldBe(14);
        result.Header[0].ShouldBe("Nr crt.");
        result.UnmappedColumns.ShouldBeEmpty();
        result.ResolvedColumns[TripCsvField.Participants].ShouldBe("Participanti");
        result.ResolvedColumns[TripCsvField.Massif].ShouldBe("Masiv/zona");
        result.Rows.Count.ShouldBe(4);
        result.Rows.ShouldAllBe(r => !r.HasError);

        // 5/25 cannot be a month, so the file settles its own order and the wizard's preference
        // never comes into it.
        result.DateOrder.ShouldBe(TripCsvDateOrder.MonthFirst);
        result.DateOrderSource.ShouldBe(TripCsvDateOrderSource.File);
        // Three of the four rows carry a date that could go either way; the fourth's 5/25 cannot.
        // Counted in rows, not cells, so the number never exceeds the rows the file has.
        result.AmbiguousDateRows.ShouldBe(3);

        var first = result.Rows[0];
        first.Line.ShouldBe(2);
        first.SourceId.ShouldBe("1");
        first.StartDate.ShouldBe(new DateOnly(2024, 5, 1));
        first.EndDate.ShouldBeNull();
        first.Title.ShouldBe("Tabara Zona 2024");
        first.Country.ShouldBe("Romania");
        first.Massif.ShouldBe("M. Capatanii");
        first.SubArea.ShouldBe("V. Cernei");
        first.Caves.ShouldBeEmpty();
        first.Proposers.ShouldBeEmpty();
        first.Participants.ShouldBe(["Ion A.", "Adrian Baritiu", "Ioana Camioana"]);
        first.TripType.ShouldBe("tabara");
        first.Errors.ShouldBe("various");

        // A question mark is the sheet's way of writing "nothing recorded", so it becomes nothing
        // rather than a note reading "?".
        result.Rows[1].Details.ShouldBeNull();
        result.Rows[1].StartDate.ShouldBe(new DateOnly(2024, 5, 11));
        result.Rows[2].StartDate.ShouldBe(new DateOnly(2024, 5, 25));

        var last = result.Rows[3];
        last.Line.ShouldBe(5);
        last.StartDate.ShouldBe(new DateOnly(2024, 7, 6));
        last.Participants.ShouldBe(["Gheorghe", "Ioana Camioana"]);
        last.Details.ShouldBe("drumuire suprafata intre P. Lanovici si P. Smaltuita");
    }

    [Theory]
    [InlineData("2024-05-11")]        // ISO, the only unambiguous written form
    [InlineData("2024/05/11")]        // year first fixes the rest; nobody writes yyyy/dd/MM
    [InlineData("20240511")]          // basic ISO, as some exports write it
    [InlineData("11-05-2024")]        // day first, under a file read day first
    [InlineData("11.05.2024")]
    [InlineData("11/5/2024")]
    public void One_grammar_reads_every_written_form_of_the_same_day(string text)
    {
        var reading = TripCsvDates.Read(text);

        TripCsvDates.TryResolve(reading, TripCsvDateOrder.DayFirst, out var date, out _).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2024, 5, 11));
    }

    [Theory]
    [InlineData("2024-06")]
    [InlineData("2024")]
    public void A_month_or_a_year_on_its_own_is_read_as_its_first_day(string text)
    {
        // A sheet that records only "summer 2024" still has a usable date; refusing it would send
        // an otherwise perfect row to the reviewer for nothing.
        TripCsvDates.TryResolve(TripCsvDates.Read(text), TripCsvDateOrder.DayFirst, out var date, out _)
            .ShouldBeTrue();
        date.Year.ShouldBe(2024);
        date.Day.ShouldBe(1);
    }

    [Fact]
    public void A_column_the_mapping_points_at_is_the_one_it_reads()
    {
        const string text = "Titlu,Data inceput,Cine a fost\r\nO tura,2024-05-11,Ana; Bogdan\r\n";
        var mapping = TripCsvColumnMapping.Auto.With(TripCsvField.Participants, "Cine a fost");

        var result = TripCsvParser.Parse(text, TripCsvOptions.Default, mapping);

        result.Rows.Single().Participants.ShouldBe(["Ana", "Bogdan"]);
        result.UnmappedColumns.ShouldBeEmpty();
    }

    [Fact]
    public void Two_fields_pointed_at_one_header_leave_the_second_empty_and_say_which()
    {
        const string text = "Titlu,Data inceput,Note\r\nO tura,2024-05-11,data incerta\r\n";
        var mapping = TripCsvColumnMapping.Auto
            .With(TripCsvField.Details, "Note")
            .With(TripCsvField.Errors, "Note");

        var result = TripCsvParser.Parse(text, TripCsvOptions.Default, mapping);

        // Neither of the two is a guess, so nothing in the file can decide between them. The
        // second is refused and named, rather than silently reading the same column twice or
        // being reported as a header the file does not have.
        result.Rows.Single().Details.ShouldBe("data incerta");
        result.Rows.Single().Errors.ShouldBeNull();
        result.FileDiagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.MappedColumnTaken
            && d.Field == TripCsvField.Errors
            && d.Column == "Note");
    }

    [Fact]
    public void A_column_nothing_claimed_is_kept_on_the_row_and_reported()
    {
        const string text = "Titlu,Data inceput,Cost transport\r\nO tura,2024-05-11,120 lei\r\n";

        var result = TripCsvParser.Parse(text);

        // Nothing in the sheet is thrown away for want of a home: an unrecognised column is
        // reported once and its values travel with the row, so a reviewer can still see them.
        result.UnmappedColumns.ShouldBe(["Cost transport"]);
        result.FileDiagnostics.ShouldContain(d => d.Code == TripCsvDiagnosticCode.UnmappedColumn && d.Line == 1);
        result.Rows.Single().Unmapped["Cost transport"].ShouldBe("120 lei");
    }

    [Fact]
    public void A_column_that_says_a_slash_separates_it_is_the_only_one_that_splits_on_one()
    {
        const string text = "Titlu,Data inceput,Pesteri,Masiv/zona\r\n"
            + "O tura,2024-05-11,P. Ialomitei / P. Ursilor,Piatra Craiului/Bucegi\r\n";
        var options = TripCsvOptions.Default with
        {
            SlashSeparatedFields = new HashSet<TripCsvField> { TripCsvField.Caves },
        };

        var row = TripCsvParser.Parse(text, options).Rows.Single();

        row.Caves.ShouldBe(["P. Ialomitei", "P. Ursilor"]);
        // The massif column was not opted in, so its slash is the ordinary character it is in the
        // column's own name.
        row.Massif.ShouldBe("Piatra Craiului/Bucegi");
    }

    [Fact]
    public void A_value_carrying_a_newline_keeps_it_and_the_lines_after_it_still_count()
    {
        const string text = "Titlu,Data inceput,Detalii\r\n"
            + "Prima,2024-05-11,\"doua\r\nrandurI\"\r\n"
            + "A doua,nu e o data,\r\n";

        var result = TripCsvParser.Parse(text);

        result.Rows[0].Details.ShouldBe("doua randurI");
        // The second row is physically the fourth line, because the first row's note spent two.
        result.Rows[1].Line.ShouldBe(4);
        result.Rows[1].Diagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.DateUnreadable && d.Line == 4);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Every_way_of_ending_a_line_ends_a_row(string ending)
    {
        var text = string.Join(ending, "Titlu,Data inceput", "Prima,2024-05-11", "A doua,2024-05-12") + ending;

        var result = TripCsvParser.Parse(text);

        result.Rows.Count.ShouldBe(2);
        result.Rows[1].Line.ShouldBe(3);
    }
}
