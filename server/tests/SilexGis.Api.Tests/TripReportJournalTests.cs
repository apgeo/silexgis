// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Features.TripLogs;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a write-up prints for a trip's tracking journal, given a journal: the lines themselves,
/// with no database and no request behind them.
/// </summary>
/// <remarks>
/// Who is told which place is decided where the journal is read, and proved there and over the
/// bytes of real documents. What is proved here is the other half: that the part which turns a
/// journal into lines adds nothing to it — it prints a place only from a line that carries one
/// and is not marked, says what the block is before anything else, and says so when it stops
/// short of the whole log.
/// </remarks>
public sealed class TripReportJournalTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Bogdan = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    /// <summary>A closed watch over two people: one placed at a station and out, one at a depth and still in.</summary>
    private static TripTrackingJournal Told() => new(
        TripTrackingState.Closed,
        At(8, 55),
        At(13, 5),
        [
            new(Ana, "Ana Invented", true, 1, "Rigging", TripStanding.Out, At(12, 0), "gallery.upper.2", null, At(10, 0), false),
            new(Bogdan, "Bogdan Invented", true, 2, null, TripStanding.Underground, At(11, 0), null, 118m, At(11, 0), false),
        ],
        [
            new(At(9, 0), Ana, "Ana Invented", "Rigging", TripPositionEventKind.Entered, null, null, null, false),
            new(At(9, 0), Bogdan, "Bogdan Invented", null, TripPositionEventKind.Entered, null, null, null, false),
            new(At(10, 0), Ana, "Ana Invented", "Rigging", TripPositionEventKind.AtStation, "gallery.upper.2", null, null, false),
            new(At(11, 0), Bogdan, "Bogdan Invented", null, TripPositionEventKind.AtDepth, null, 118m, null, false),
            new(At(11, 30), Ana, "Ana Invented", "Rigging", TripPositionEventKind.Note, null, null, "Second pitch rigged", false),
            new(At(12, 0), Ana, "Ana Invented", "Rigging", TripPositionEventKind.Exited, null, null, null, false),
        ],
        AnyWithheld: false);

    private static List<DocumentBlock> Printed(TripTrackingJournal? journal, TripJournalWording? wording = null)
    {
        var blocks = new List<DocumentBlock>();
        TripReportJournal.Append(blocks, journal, wording ?? TripJournalWording.English);
        return blocks;
    }

    [Fact]
    public void A_journal_is_printed_as_what_it_is_then_the_watch_then_each_person_then_each_report_in_time_order()
    {
        var blocks = Printed(Told());

        // What it is, and what it is not, before a single hour or name.
        blocks[0].Kind.ShouldBe(DocumentBlockKind.Note);
        blocks[0].Text.ShouldContain("not a callout record");
        blocks[0].Text.ShouldContain("raised no alarm");

        var fields = blocks.Where(b => b.Kind == DocumentBlockKind.Field).ToDictionary(b => b.Label!, b => b.Text);
        fields["Tracking"].ShouldBe("Closed");
        // Universal time, and saying so, like every other instant a write-up prints.
        fields["First started"].ShouldBe("2026-09-12 08:55 UTC");
        fields["Tracking closed"].ShouldBe("2026-09-12 13:05 UTC");

        var lines = blocks.Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ToList();
        lines.ShouldBe(
        [
            // The place is dated where it is older than the last word about the person.
            "Ana Invented · Rigging · Out · last heard 2026-09-12 12:00 UTC · gallery.upper.2 (2026-09-12 10:00 UTC)",
            "Bogdan Invented · Underground · last heard 2026-09-12 11:00 UTC · 118 m",
            "2026-09-12 09:00 UTC · Ana Invented · Rigging · Went in",
            "2026-09-12 09:00 UTC · Bogdan Invented · Went in",
            "2026-09-12 10:00 UTC · Ana Invented · Rigging · At a station · gallery.upper.2",
            "2026-09-12 11:00 UTC · Bogdan Invented · At a depth · 118 m",
            "2026-09-12 11:30 UTC · Ana Invented · Rigging · Note · Second pitch rigged",
            "2026-09-12 12:00 UTC · Ana Invented · Rigging · Came out",
        ]);

        // Nothing was kept back, so nothing says anything was.
        blocks.ShouldNotContain(b => b.Text.Contains("withheld", StringComparison.OrdinalIgnoreCase));
        blocks.Count(b => b.Kind == DocumentBlockKind.Subheading).ShouldBe(2);
    }

    /// <summary>
    /// A trip nobody followed prints nothing at all — not the note, not an empty heading — so the
    /// heading a layout wrote above the word has nothing under it and is taken out with it.
    /// </summary>
    [Fact]
    public void A_trip_with_no_journal_prints_nothing_and_takes_the_layouts_heading_with_it()
    {
        Printed(null).ShouldBeEmpty();

        var layout = new List<DocumentBlock> { DocumentBlock.Title("A trip"), DocumentBlock.Heading("Tracking") };
        TripReportJournal.Append(layout, null, TripJournalWording.English);
        ReportComposition.Pruned(layout).Select(b => b.Kind).ShouldBe([DocumentBlockKind.Title]);

        // The same heading over a trip that was followed stays, with the journal under it.
        var followed = new List<DocumentBlock> { DocumentBlock.Title("A trip"), DocumentBlock.Heading("Tracking") };
        TripReportJournal.Append(followed, Told(), TripJournalWording.English);
        var kept = ReportComposition.Pruned(followed);
        kept[1].Kind.ShouldBe(DocumentBlockKind.Heading);
        kept.Count.ShouldBeGreaterThan(10);
    }

    /// <summary>
    /// A report whose place was kept back prints its hour, who and what, and one word — and a
    /// line that is marked prints that word even if a station and a depth were handed in with it,
    /// which the journal never does.
    /// </summary>
    [Fact]
    public void A_withheld_report_prints_its_hour_and_the_word_and_never_a_station_or_a_depth()
    {
        var told = Told();
        var kept = told with
        {
            AnyWithheld = true,
            People =
            [
                told.People[0] with { Station = null, PlaceAt = null, PlaceWithheld = true },
                // Marked and still carrying a place: the mark wins.
                told.People[1] with { Station = "gallery.deep.3", DepthM = 118m, PlaceWithheld = true },
            ],
            Entries =
            [
                .. told.Entries.Select(entry => entry.Kind switch
                {
                    TripPositionEventKind.AtStation => entry with { Station = null, Withheld = true },
                    // Marked and still carrying a place: the mark wins.
                    TripPositionEventKind.AtDepth => entry with { Station = "gallery.deep.3", Withheld = true },
                    _ => entry,
                }),
            ],
        };

        var text = string.Join("\n", Printed(kept).Select(b => $"{b.Label} {b.Text}"));

        text.ShouldNotContain("gallery.");
        text.ShouldNotContain("118");
        text.ShouldContain("2026-09-12 10:00 UTC · Ana Invented · Rigging · At a station · place withheld");
        text.ShouldContain("2026-09-12 11:00 UTC · Bogdan Invented · At a depth · place withheld");
        text.ShouldContain("Ana Invented · Rigging · Out · last heard 2026-09-12 12:00 UTC · place withheld");
        // Said once, in words, that this copy keeps places back and another may not.
        text.ShouldContain("Some places are withheld.");
        // What is not a place is all still there.
        text.ShouldContain("Second pitch rigged");

        // The fixture half: the very same journal unmarked prints both places.
        var open = string.Join("\n", Printed(told).Select(b => b.Text));
        open.ShouldContain("gallery.upper.2");
        open.ShouldContain("118 m");
    }

    /// <summary>
    /// A log longer than a write-up prints stops at a stated number and closes by saying how many
    /// reports it left out and where the whole log can be had.
    /// </summary>
    [Fact]
    public void A_long_log_is_cut_at_a_stated_number_with_a_closing_line_naming_the_sheet()
    {
        var told = Told();
        var many = Enumerable.Range(0, TripReportJournal.MaxReports + 7)
            .Select(i => new TripTrackingJournalEntry(
                At(9, 0).AddSeconds(i), Ana, "Ana Invented", null, TripPositionEventKind.Note, null, null, $"n{i:0000}", false))
            .ToList();

        var blocks = Printed(told with { Entries = many });

        var reports = blocks.Where(b => b.Kind == DocumentBlockKind.Bullet && b.Text.Contains("· Note ·")).ToList();
        reports.Count.ShouldBe(TripReportJournal.MaxReports);
        // The oldest are the ones printed: a journal is read from the beginning.
        reports[0].Text.ShouldEndWith("n0000");
        reports[^1].Text.ShouldEndWith($"n{TripReportJournal.MaxReports - 1:0000}");
        var closing = blocks[^1];
        closing.Kind.ShouldBe(DocumentBlockKind.Note);
        closing.Text.ShouldContain($"Reports printed: {TripReportJournal.MaxReports}.");
        closing.Text.ShouldContain("Reports not printed: 7.");
        closing.Text.ShouldContain("Download the log (CSV)");

        // Exactly at the number nothing is left out and nothing says so.
        var exact = Printed(told with { Entries = [.. many.Take(TripReportJournal.MaxReports)] });
        exact[^1].Kind.ShouldBe(DocumentBlockKind.Bullet);
    }

    /// <summary>
    /// Somebody the document is told no name for is still one person in it — by their number in
    /// the party — and somebody the trip no longer lists is said to be so.
    /// </summary>
    [Fact]
    public void Somebody_without_a_name_is_printed_by_their_number_and_somebody_off_the_roster_is_marked()
    {
        var told = Told();
        var unnamed = told with
        {
            People =
            [
                told.People[0] with { Name = null },
                told.People[1] with { Name = null, Number = null, OnRoster = false },
            ],
            Entries = [.. told.Entries.Select(entry => entry with { Name = null })],
        };

        var lines = Printed(unnamed).Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ToList();

        lines[0].ShouldStartWith("Caver 1 · Rigging · Out");
        lines[1].ShouldStartWith("A person not named here · no longer on the roster · Underground");
        lines.ShouldContain("2026-09-12 10:00 UTC · Caver 1 · Rigging · At a station · gallery.upper.2");
        lines.ShouldContain("2026-09-12 11:00 UTC · A person not named here · At a depth · 118 m");
    }

    /// <summary>
    /// A line about the cave and nobody in it — a hazard reported to the surface — is printed in
    /// the journal's own time order and says in words that it speaks of the cave, never as a
    /// report about a person the document was told no name for. Its place follows the one rule
    /// every place in a journal follows: the mark arrives with the line, and the words of the
    /// hazard are not a place and stay whatever is kept back.
    /// </summary>
    [Fact]
    public void A_line_about_the_cave_says_so_and_is_never_printed_as_somebodys_report()
    {
        var told = Told();
        var hazard = new TripTrackingJournalEntry(
            At(10, 30), null, null, null, TripPositionEventKind.CaveNote,
            "gallery.parallel.2", null, "Loose rock above the second pitch", false);
        var withHazard = told with { Entries = [.. told.Entries.Take(3), hazard, .. told.Entries.Skip(3)] };

        var lines = Printed(withHazard).Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ToList();

        // In its hour's place in the log, said to be about the cave, with its station and words.
        lines.ShouldContain(
            "2026-09-12 10:30 UTC · About the cave, not about a person · gallery.parallel.2 "
            + "· Loose rock above the second pitch");
        // Not somebody the document has no name for, and in nobody's number.
        lines.ShouldNotContain(line => line.Contains("A person not named here", StringComparison.Ordinal));
        lines.ShouldNotContain(line =>
            line.Contains("Caver ", StringComparison.Ordinal) && line.Contains("Loose rock", StringComparison.Ordinal));
        // The party's own lines are what they were: widening the journal added nothing to them.
        lines.ShouldContain("2026-09-12 10:00 UTC · Ana Invented · Rigging · At a station · gallery.upper.2");
        // It is in the list of reports and not among the people: nobody gained a line.
        Printed(withHazard).Count(b => b.Kind == DocumentBlockKind.Subheading).ShouldBe(2);

        // The place kept back from this reader: the station goes, the hazard's words stay.
        var kept = Printed(withHazard with
        {
            AnyWithheld = true,
            Entries = [.. withHazard.Entries.Select(entry =>
                entry.CaverId is null ? entry with { Station = null, Withheld = true } : entry)],
        }).Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ToList();
        kept.ShouldContain(
            "2026-09-12 10:30 UTC · About the cave, not about a person · place withheld "
            + "· Loose rock above the second pitch");
        kept.ShouldNotContain(line => line.Contains("gallery.parallel.2", StringComparison.Ordinal));

        // Both halves in Romanian, so neither phrase is English in the other language's document.
        var romanian = Printed(withHazard, TripJournalWording.Romanian)
            .Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ToList();
        romanian.ShouldContain(
            "2026-09-12 10:30 UTC · Despre peșteră, nu despre o persoană · gallery.parallel.2 "
            + "· Loose rock above the second pitch");
        romanian.ShouldNotContain(line => line.Contains("About the cave", StringComparison.Ordinal));
    }

    /// <summary>
    /// A trip nobody was reported on whose log holds a hazard is still written up, and the hazard
    /// is what it prints: the record of the cave does not depend on anybody having been placed.
    /// </summary>
    [Fact]
    public void A_journal_of_nothing_but_a_hazard_still_prints_the_hazard()
    {
        var hazard = new TripTrackingJournalEntry(
            At(10, 30), null, null, null, TripPositionEventKind.CaveNote,
            "gallery.parallel.2", null, "Sump has risen", false);
        var blocks = Printed(new TripTrackingJournal(
            TripTrackingState.Off, null, null, [], [hazard], AnyWithheld: false));

        blocks.ShouldNotBeEmpty();
        blocks.Where(b => b.Kind == DocumentBlockKind.Bullet).Select(b => b.Text).ShouldBe(
            ["2026-09-12 10:30 UTC · About the cave, not about a person · gallery.parallel.2 · Sump has risen"]);
    }

    /// <summary>
    /// The block's own words exist in both languages the application is written in, line for
    /// line, and anything else asks for English.
    /// </summary>
    [Fact]
    public void The_journals_own_words_are_written_in_romanian_and_in_english_and_default_to_english()
    {
        TripJournalWording.For("ro").ShouldBeSameAs(TripJournalWording.Romanian);
        TripJournalWording.For("RO").ShouldBeSameAs(TripJournalWording.Romanian);
        TripJournalWording.For("en").ShouldBeSameAs(TripJournalWording.English);
        TripJournalWording.For(null).ShouldBeSameAs(TripJournalWording.English);
        TripJournalWording.For("fr").ShouldBeSameAs(TripJournalWording.English);

        var told = Told();
        var romanian = Printed(told with { AnyWithheld = true }, TripJournalWording.Romanian);
        var english = Printed(told with { AnyWithheld = true }, TripJournalWording.English);

        // The same blocks in the same order, and no fixed line left in the other language.
        romanian.Select(b => b.Kind).ShouldBe(english.Select(b => b.Kind));
        romanian[0].Text.ShouldContain("apelului de urgență");
        romanian[0].Text.ShouldContain("nu a dat nicio alarmă");
        var text = string.Join("\n", romanian.Select(b => $"{b.Label} {b.Text}"));
        text.ShouldContain("2026-09-12 10:00 UTC · Ana Invented · Rigging · La o stație · gallery.upper.2");
        text.ShouldContain("Bogdan Invented · În peșteră · ultima veste 2026-09-12 11:00 UTC · 118 m");
        text.ShouldContain("Unele locuri sunt reținute.");
        foreach (var word in new[] { "Went in", "Came out", "Underground", "last heard", "Tracking", "withheld", "Reports" })
        {
            text.ShouldNotContain(word);
        }

        // The two lines that take a number take it in both languages.
        var lengthy = told with
        {
            People = [told.People[0] with { Name = null }],
            Entries =
            [
                .. Enumerable.Range(0, TripReportJournal.MaxReports + 7).Select(i => new TripTrackingJournalEntry(
                    At(9, 0).AddSeconds(i), Ana, null, null, TripPositionEventKind.Entered, null, null, null, false)),
            ],
        };
        var longRomanian = Printed(lengthy, TripJournalWording.Romanian);
        longRomanian.ShouldContain(b => b.Kind == DocumentBlockKind.Bullet && b.Text.StartsWith("Speolog 1 · "));
        longRomanian[^1].Text.ShouldContain($"Rapoarte tipărite: {TripReportJournal.MaxReports}. Rapoarte netipărite: 7.");
        longRomanian[^1].Text.ShouldContain("Descarcă jurnalul (CSV)");

        // Every wording carries every line: a word added to one language and not the other
        // would otherwise print as nothing in a document nobody proof-reads.
        foreach (var property in typeof(TripJournalWording).GetProperties()
            .Where(p => p.PropertyType == typeof(string)))
        {
            ((string?)property.GetValue(TripJournalWording.Romanian)).ShouldNotBeNullOrWhiteSpace(property.Name);
            ((string?)property.GetValue(TripJournalWording.English)).ShouldNotBeNullOrWhiteSpace(property.Name);
        }
    }
}
