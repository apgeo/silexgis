// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The trip spreadsheet these files come from was read once before, by a generator whose
/// behaviour is known in detail — including everything it got wrong. Each test here names one of
/// those behaviours and pins the opposite, because the ways a reader of somebody else's
/// spreadsheet loses data are quiet ones: a name shredded into two people, a typo rolled into a
/// plausible wrong date, a line number pointing at the wrong row. None of them fails a build and
/// none of them shows up in a result; they show up years later as a club's history being wrong.
/// </summary>
public class TripCsvLegacyDefectTests
{
    private const string Header = "Nr crt.,Data inceput,Titlu,Masiv/zona,Pesteri,Propus de,Participanti,Detalii\r\n";

    private static TripCsvParseResult ParseRows(string rows, TripCsvOptions? options = null) =>
        TripCsvParser.Parse(Header + rows, options);

    [Fact]
    public void A_cell_reading_n_a_stays_one_value_and_names_nobody()
    {
        // Run with the slash actually separating this column, which is the configuration that
        // exposes the defect: without it the slash is not a separator at all and the test proves
        // nothing. Splitting before deciding what a value means turns the one thing that says
        // "nothing here" into two people called n and a, and the set that would have recognised
        // it never sees the cell whole.
        var options = TripCsvOptions.Default with
        {
            SlashSeparatedFields = new HashSet<TripCsvField> { TripCsvField.Proposers },
        };

        var row = ParseRows("1,2024-05-11,O tura,,,n/a,,\r\n", options).Rows.Single();

        row.Proposers.ShouldBeEmpty();
        row.Proposers.ShouldNotContain("n");
        row.Proposers.ShouldNotContain("a");

        // The positive half in the same column and the same options, so an empty list cannot pass
        // this test by the column never having been resolved at all.
        var named = ParseRows("1,2024-05-11,O tura,,,Ana Popescu,,\r\n", options).Rows.Single();
        named.Proposers.ShouldBe(["Ana Popescu"]);
    }

    [Fact]
    public void A_slash_inside_a_name_does_not_split_it_unless_that_column_says_so()
    {
        var row = ParseRows("1,2024-05-11,O tura,,P. Ursilor/Pestera Mare,,,\r\n").Rows.Single();

        // A cave column really is written both ways, so the column decides. What must never
        // happen is that the decision is made once, globally, in favour of splitting.
        row.Caves.ShouldBe(["P. Ursilor/Pestera Mare"]);
    }

    [Fact]
    public void A_name_carrying_a_comma_survives_a_separator_set_that_leaves_the_comma_out()
    {
        var options = TripCsvOptions.Default with { MultiValueSeparators = [';'] };

        var row = ParseRows("1,2024-05-11,O tura,,,,\"Popescu, Ion; Ana B.\",\r\n", options).Rows.Single();

        // A sheet that writes surnames first cannot be read with a comma separator at all, so the
        // separator set is the reviewer's to choose rather than the parser's to assume.
        row.Participants.ShouldBe(["Popescu, Ion", "Ana B."]);
    }

    [Fact]
    public void A_row_narrower_than_the_header_is_reconciled_and_warned_rather_than_thrown_on()
    {
        var result = ParseRows("1,2024-05-11,O tura\r\n2,2024-05-12,A doua,M. Bucegi,,,,extra,inca una\r\n");

        // One malformed line in a sheet of a thousand must not cost the other nine hundred and
        // ninety-nine, which is what an exception here does.
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].Title.ShouldBe("O tura");
        result.Rows[0].Diagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.RaggedRow && d.Severity == TripCsvSeverity.Warning && d.Line == 2);
        result.Rows[1].Diagnostics.ShouldContain(d => d.Code == TripCsvDiagnosticCode.RaggedRow && d.Line == 3);
        result.Rows[1].Massif.ShouldBe("M. Bucegi");
    }

    [Fact]
    public void A_diagnostic_names_the_physical_line_even_after_a_blank_line_shifted_the_rows()
    {
        var result = TripCsvParser.Parse(Header + "\r\n1,nu e o data,O tura,,,,,\r\n");

        // Counting the rows that survived parsing rather than the lines of the file sends the
        // reviewer to the line above the one they have to fix, every time.
        var row = result.Rows.Single();
        row.Line.ShouldBe(3);
        row.Diagnostics.ShouldContain(d => d.Code == TripCsvDiagnosticCode.DateUnreadable && d.Line == 3);
    }

    [Fact]
    public void Where_a_row_sits_in_the_file_changes_nothing_about_what_it_says()
    {
        const string one = "1,2024-05-11,Prima,,,,Ana,\r\n";
        const string two = "2,2024-05-12,A doua,,,,Bogdan,\r\n";

        var early = ParseRows(one + two).Rows[0];
        var late = ParseRows(two + one).Rows[1];

        // Numbering values by the order they first appear makes every stored reference depend on
        // nothing being inserted above it, and a spreadsheet is a thing people insert rows into.
        late.SourceId.ShouldBe(early.SourceId);
        late.Title.ShouldBe(early.Title);
        late.StartDate.ShouldBe(early.StartDate);
        late.Participants.ShouldBe(early.Participants);
        late.Diagnostics.ShouldBeEmpty();
        early.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_second_row_under_one_source_number_is_reported_and_names_the_first()
    {
        var result = ParseRows("9,2024-05-11,Prima,,,,,\r\n9,2024-05-12,A doua,,,,,\r\n");

        // Two rows under one number is how one of them disappears later, from whatever keeps rows
        // by that number, while still being counted everywhere else.
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].Diagnostics.ShouldBeEmpty();
        var duplicate = result.Rows[1].Diagnostics.Single();
        duplicate.Code.ShouldBe(TripCsvDiagnosticCode.DuplicateSourceId);
        duplicate.Line.ShouldBe(3);
        duplicate.Detail.ShouldNotBeNull().ShouldContain("line 2");
    }

    [Fact]
    public void A_source_number_that_is_not_a_number_does_not_stop_the_file()
    {
        var result = ParseRows("1a,2024-05-11,Prima,,,,,\r\n,2024-05-12,A doua,,,,,\r\n");

        // The sheet's own numbering is the sheet's business; parsing it as an integer means one
        // stray cell throws and nothing at all is imported.
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].SourceId.ShouldBe("1a");
        result.Rows[1].SourceId.ShouldBeNull();
        result.Rows.ShouldAllBe(r => !r.HasError);
    }

    [Fact]
    public void A_massif_nothing_recognises_is_carried_through_as_the_text_it_was()
    {
        var row = ParseRows("1,2024-05-11,O tura,M. Nicaieri,,,,\r\n").Rows.Single();

        // Resolving an unknown place to a default point puts every unrecognised trip on one spot
        // in the middle of the country, where it reads as a real cluster.
        row.Massif.ShouldBe("M. Nicaieri");
    }

    [Fact]
    public void The_parser_produces_no_position_of_any_kind()
    {
        var names = typeof(TripCsvRow).GetProperties().Select(p => p.Name).ToList();

        // A place name is not a position. Deriving one here would give every trip in a massif the
        // same marker and give a reader no way to tell that from a surveyed entrance.
        names.Any(n => n.Contains("Lat") || n.Contains("Lon") || n.Contains("Geom") || n.Contains("Coord"))
            .ShouldBeFalse();
    }

    [Fact]
    public void The_order_people_were_written_in_is_the_order_they_come_back_in()
    {
        var row = ParseRows("1,2024-05-11,O tura,,,,\"Gheorghe, Ana, Bogdan\",\r\n").Rows.Single();

        // Whoever is written first is usually whoever led the trip. Sorting the list by anything
        // else loses that, and nothing in the sheet records it twice.
        row.Participants.ShouldBe(["Gheorghe", "Ana", "Bogdan"]);
    }

    [Fact]
    public void A_second_details_column_and_an_errors_column_each_keep_their_own_meaning()
    {
        const string text = "Titlu,Data inceput,Detalii,Detalii2,Erori\r\n"
            + "O tura,2024-05-11,prima nota,a doua nota,data incerta\r\n";

        var row = TripCsvParser.Parse(text).Rows.Single();

        // The column a club uses to record what is wrong with a row is the most useful column in
        // the sheet, and it is the one most easily read as a duplicate of the notes.
        row.Details.ShouldBe("prima nota");
        row.Details2.ShouldBe("a doua nota");
        row.Errors.ShouldBe("data incerta");
    }

    [Fact]
    public void The_same_cell_never_means_two_different_days()
    {
        var result = ParseRows("1,5/25/2024,Prima,,,,,\r\n2,5/1/2024,A doua,,,,,\r\n");

        // A pipeline carrying two grammars sorts by one reading and displays another, and the two
        // disagree on every value written this way.
        result.DateOrder.ShouldBe(TripCsvDateOrder.MonthFirst);
        result.Rows[1].StartDate.ShouldBe(new DateOnly(2024, 5, 1));
        TripCsvDates.TryResolve(TripCsvDates.Read("5/1/2024"), result.DateOrder, out var again, out _)
            .ShouldBeTrue();
        result.Rows[1].StartDate.ShouldBe(again);
    }

    [Fact]
    public void A_month_that_is_not_a_month_is_refused_rather_than_rolled_into_the_next_year()
    {
        var row = ParseRows("1,13/45/2024,O tura,,,,,\r\n").Rows.Single();

        // Rolling 2024-13-45 forward gives 2025-02-14: a date that looks entirely plausible, sorts
        // correctly, and is not the one anybody typed.
        row.StartDate.ShouldBeNull();
        row.Diagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.DateOutOfRange && d.Severity == TripCsvSeverity.Error);
        row.StartDateText.ShouldBe("13/45/2024");
    }

    [Fact]
    public void A_year_written_with_two_digits_is_refused_rather_than_read_as_the_year_twenty_four()
    {
        var row = ParseRows("1,1/1/24,O tura,,,,,\r\n").Rows.Single();

        // Nothing in the cell says which century, and guessing produces either a trip in antiquity
        // or a silent hundred-year error, both of which sort perfectly well.
        row.StartDate.ShouldBeNull();
        row.Diagnostics.ShouldContain(d => d.Code == TripCsvDiagnosticCode.DateTwoDigitYear);
    }

    [Fact]
    public void The_day_month_order_is_decided_once_for_the_file_and_the_undecidable_rows_counted()
    {
        var result = ParseRows("1,5/1/2024,Prima,,,,,\r\n2,7/6/2024,A doua,,,,,\r\n");

        // No row can settle this for itself, so deciding per row means neighbouring rows quietly
        // read in different orders. Nothing here settles it at all, so the stated preference
        // stands and the reviewer is told how many rows ride on it.
        result.DateOrderSource.ShouldBe(TripCsvDateOrderSource.Stated);
        result.DateOrder.ShouldBe(TripCsvDateOrder.DayFirst);
        result.AmbiguousDateRows.ShouldBe(2);
        result.Rows[0].StartDate.ShouldBe(new DateOnly(2024, 1, 5));
        result.Rows.ShouldAllBe(r => r.Diagnostics.Any(d => d.Code == TripCsvDiagnosticCode.DateAmbiguous));

        // The positive half, with the one thing that differs changed and nothing else: a single
        // component above twelve anywhere in the column settles the whole file.
        var settled = ParseRows("1,5/1/2024,Prima,,,,,\r\n2,7/25/2024,A doua,,,,,\r\n");
        settled.DateOrderSource.ShouldBe(TripCsvDateOrderSource.File);
        settled.DateOrder.ShouldBe(TripCsvDateOrder.MonthFirst);
        settled.Rows[0].StartDate.ShouldBe(new DateOnly(2024, 5, 1));
    }

    [Fact]
    public void A_free_text_column_full_of_semicolons_does_not_change_how_the_file_is_split()
    {
        const string text = "Titlu,Data inceput,Detalii\r\n"
            + "O tura,2024-05-11,\"a;b;c;d;e;f;g;h;i;j\"\r\n";

        var result = TripCsvParser.Parse(text);

        // Scoring candidate delimiters by how often they occur hands the whole file to whichever
        // character one chatty column happens to be full of, and the wrong reading still looks
        // like a successful parse.
        result.Header.Count.ShouldBe(3);
        result.Rows.Single().Title.ShouldBe("O tura");
        result.Rows.Single().Details.ShouldBe("a;b;c;d;e;f;g;h;i;j");
    }

    [Fact]
    public void A_row_of_nothing_but_spaces_is_reported_rather_than_quietly_skipped()
    {
        var result = ParseRows("   ,  , , , , , , \r\n1,2024-05-11,O tura,,,,,\r\n");

        // A row of blanks is usually a row somebody meant to fill in, and dropping it without a
        // word is how a trip goes missing between the sheet and the import.
        result.Rows.Count.ShouldBe(1);
        result.FileDiagnostics.ShouldContain(d => d.Code == TripCsvDiagnosticCode.BlankRow && d.Line == 2);
    }

    [Fact]
    public void A_name_written_in_another_alphabet_is_kept_rather_than_deleted()
    {
        var row = ParseRows("1,2024-05-11,O tura,,\"Леденика, Σπήλαιο\",,,\r\n").Rows.Single();

        // Asking whether a value carries a Latin letter or an Arabic digit deletes a Bulgarian or
        // a Greek cave name outright, and clubs here go to both countries.
        row.Caves.ShouldBe(["Леденика", "Σπήλαιο"]);

        // The positive half of the same rule: something with no letter or digit in any script is
        // still dropped, and still said out loud.
        var punctuation = ParseRows("1,2024-05-11,O tura,,\"Pestera Mare, ---\",,,\r\n").Rows.Single();
        punctuation.Caves.ShouldBe(["Pestera Mare"]);
    }

    [Fact]
    public void A_header_the_mapping_names_but_the_file_lacks_yields_nothing_rather_than_another_column()
    {
        const string text = "Titlu,Data inceput,Membri\r\nO tura,2024-05-11,Ana; Bogdan\r\n";
        var mapping = TripCsvColumnMapping.Auto.With(TripCsvField.Participants, "Participanti");

        var result = TripCsvParser.Parse(text, TripCsvOptions.Default, mapping);

        // "Membri" is a spelling the parser would have recognised on its own. Once somebody has
        // said which column to read, substituting a different one for it is how a whole import
        // ends up filed against the wrong thing, and nothing in the result would say so.
        result.Rows.Single().Participants.ShouldBeEmpty();
        result.FileDiagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.MappedColumnMissing && d.Field == TripCsvField.Participants);
        result.Rows.Single().Unmapped["Membri"].ShouldBe("Ana; Bogdan");
    }

    [Fact]
    public void A_date_that_cannot_be_read_is_absent_rather_than_standing_in_for_one()
    {
        var row = ParseRows("1,cindva vara,O tura,,,,,\r\n").Rows.Single();

        // Substituting a far-future sentinel for an unreadable date makes it sort last instead of
        // making it visible, and the sentinel then has to be remembered everywhere afterwards.
        row.StartDate.ShouldBeNull();
        row.StartDateText.ShouldBe("cindva vara");
        row.HasError.ShouldBeTrue();
    }

    [Fact]
    public void Two_spellings_of_one_name_in_one_cell_are_one_person()
    {
        var row = ParseRows("1,2024-05-11,O tura,,,,\"Ion A., ION A., Ana Șureanu, Ana Şureanu\",\r\n")
            .Rows.Single();

        // Case-sensitive matching makes Ion A. and ION A. two people; and the two ways Romanian
        // writes a comma below make one name into two more, which is the same defect in a form
        // nobody notices until somebody else's alphabet is involved.
        row.Participants.ShouldBe(["Ion A.", "Ana Șureanu"]);
    }

    [Theory]
    [InlineData("99999999999/12/2024")]
    [InlineData("2024-99999999999-01")]
    public void A_date_component_too_long_to_be_a_number_costs_its_own_row_and_no_other(string date)
    {
        var result = ParseRows($"1,{date},O tura,,,,,\r\n2,2024-05-11,A doua,,,,,\r\n");

        // Parsing a component with no width check throws out of the whole parse, so one column of
        // reference numbers pointed at a date field returns no rows, no diagnostics and nothing
        // naming the cell. A malformed cell costs its own row; it never costs the file.
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].StartDate.ShouldBeNull();
        result.Rows[0].StartDateText.ShouldBe(date);
        result.Rows[0].Diagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.DateUnreadable && d.Severity == TripCsvSeverity.Error);
        result.Rows[1].StartDate.ShouldBe(new DateOnly(2024, 5, 11));
        result.Rows[1].HasError.ShouldBeFalse();
    }

    [Fact]
    public void A_header_the_mapping_names_is_not_lost_to_a_field_that_would_have_guessed_it()
    {
        const string text = "Nr crt.,Data,Data sfarsit,Titlu\r\n1,2024-05-11,2024-05-12,O tura\r\n";
        var mapping = TripCsvColumnMapping.Auto.With(TripCsvField.EndDate, "Data");

        var result = TripCsvParser.Parse(text, TripCsvOptions.Default, mapping);

        // Resolving one field at a time lets whichever field the enum happens to list first guess
        // its way onto the column somebody explicitly pointed a later field at — and then reports
        // the named header as missing from a file that plainly has it, which is the worst of both:
        // the column is read into the wrong field and the diagnostic sends the reviewer nowhere.
        result.ResolvedColumns[TripCsvField.EndDate].ShouldBe("Data");
        result.Rows.Single().EndDate.ShouldBe(new DateOnly(2024, 5, 11));
        result.FileDiagnostics.ShouldNotContain(d => d.Code == TripCsvDiagnosticCode.MappedColumnMissing);
        result.UnmappedColumns.ShouldContain("Data sfarsit");
    }

    [Fact]
    public void A_quote_the_file_never_closes_is_reported_rather_than_swallowing_the_rest_in_silence()
    {
        var result = ParseRows(
            "1,2024-05-11,\"O tura,,,,\r\n2,2024-05-12,A doua,,,,,\r\n3,2024-05-13,A treia,,,,,\r\n");

        // Everything after the opening quote becomes one cell, so two of the three rows are gone.
        // A reader that does not say so hands back a short result that looks like a parse which
        // worked, and nothing in it counts the rows that were lost.
        result.Rows.Count.ShouldBe(1);
        result.FileDiagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.UnterminatedQuote
            && d.Severity == TripCsvSeverity.Error
            && d.Line == 2);
    }

    [Fact]
    public void Reading_one_file_leaves_nothing_behind_that_changes_how_the_next_one_reads()
    {
        const string text = "1,5/1/2024,Prima,,,,Ana,\r\n2,5/2/2024,A doua,,,,Bogdan,\r\n";

        var before = ParseRows(text);
        ParseRows("1,5/25/2024,Alta,,,,Cristina,\r\n");
        var after = ParseRows(text);

        // The legacy reader kept state between the passes that read a sheet, so a stale file left
        // over from an earlier run rewrote the current one's values against the wrong names. This
        // parser holds nothing between reads, and that is the property worth pinning: the second
        // file settles its own day/month order and leaves no trace on a re-read of the first.
        after.DateOrder.ShouldBe(before.DateOrder);
        after.DateOrderSource.ShouldBe(before.DateOrderSource);
        after.AmbiguousDateRows.ShouldBe(before.AmbiguousDateRows);
        after.Rows.Select(Written).ShouldBe(before.Rows.Select(Written));
    }

    /// <summary>Everything a row says, in one string, so two reads can be compared whole.</summary>
    private static string Written(TripCsvRow row) => string.Join(
        '|',
        row.Line,
        row.SourceId,
        row.Title,
        row.StartDate?.ToString("O"),
        row.EndDate?.ToString("O"),
        string.Join(',', row.Participants),
        row.Diagnostics.Count);

    [Fact]
    public void A_row_with_no_title_is_returned_carrying_the_reason_it_cannot_be_imported()
    {
        var row = ParseRows("1,2024-05-11,,,,,,\r\n").Rows.Single();

        // Dropping the row instead would leave a reviewer counting the sheet's rows against the
        // preview's and finding no explanation for the difference.
        row.HasError.ShouldBeTrue();
        row.Diagnostics.ShouldContain(d =>
            d.Code == TripCsvDiagnosticCode.RequiredFieldEmpty && d.Field == TripCsvField.Title);
    }
}
