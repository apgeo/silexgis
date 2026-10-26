// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Features.TripLogs;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a write-up prints for the stations of the survey a trip was followed in, given a list:
/// the lines themselves, with no database and no request behind them.
/// </summary>
/// <remarks>
/// Who is told the stations is decided where the list is read, and proved there and over the bytes
/// of real documents. What is proved here is the other half: that the part which turns a list into
/// lines adds nothing to it — it prints a station only from a list that carries stations, says
/// what the block is before any of them, says in words when a reader is not told them, and says
/// so when it stops short of the whole survey.
/// </remarks>
public sealed class TripReportStationListTests
{
    private static TripSurveyStationList Told() => new(
        Shown: true,
        Datum: "cave.ent.0",
        Stations:
        [
            new("cave.ent.0", 0m, true),
            new("cave.upper.1", 10m, false),
            new("cave.deep.3", 120m, false),
        ],
        Total: 3);

    private static List<DocumentBlock> Printed(
        TripSurveyStationList? stations, TripStationListWording? wording = null)
    {
        var blocks = new List<DocumentBlock>();
        TripReportStations.Append(blocks, stations, wording ?? TripStationListWording.English);
        return blocks;
    }

    [Fact]
    public void A_station_list_is_printed_as_what_it_is_then_its_datum_then_a_line_for_each_station()
    {
        var blocks = Printed(Told());

        // What it is, and what it is not, before a single station.
        blocks[0].Kind.ShouldBe(DocumentBlockKind.Note);
        blocks[0].Text.ShouldContain("nothing about where anybody was");

        blocks[1].Kind.ShouldBe(DocumentBlockKind.Field);
        blocks[1].Label.ShouldBe("Depths measured below");
        blocks[1].Text.ShouldBe("cave.ent.0");

        // One line per station, in the order it was handed them, with the depth as metres down
        // and the survey's own mark on an entrance.
        blocks.Skip(2).Select(block => (block.Kind, block.Text)).ShouldBe(
        [
            (DocumentBlockKind.Bullet, "cave.ent.0 · entrance · 0 m"),
            (DocumentBlockKind.Bullet, "cave.upper.1 · 10 m"),
            (DocumentBlockKind.Bullet, "cave.deep.3 · 120 m"),
        ]);
    }

    /// <summary>
    /// A reader who may not be told where the cave is gets one line saying the list is not shown,
    /// and no part of the survey at all — not a station, not the datum, not how many there are.
    /// A heading a layout wrote above the word stands over that line, because the alternative
    /// reads as a cave whose survey holds nothing.
    /// </summary>
    [Fact]
    public void A_list_this_reader_may_not_be_told_is_one_line_saying_so_and_nothing_of_the_survey()
    {
        var blocks = Printed(new TripSurveyStationList(
            Shown: false, Datum: null, Stations: [], Total: 0));

        var said = blocks.ShouldHaveSingleItem();
        said.Kind.ShouldBe(DocumentBlockKind.Note);
        said.Text.ShouldContain("not shown");

        // And the list beside it, printed the same way, does carry the survey — so the line above
        // is narrow because of what it was handed and not because nothing is ever printed.
        Printed(Told()).ShouldContain(block => block.Text == "cave.deep.3 · 120 m");
    }

    /// <summary>
    /// No list at all prints nothing, so a heading a layout wrote above the word is taken out with
    /// it — a trip that was never followed, or whose watch names no survey.
    /// </summary>
    [Fact]
    public void No_list_prints_nothing_at_all()
    {
        Printed(null).ShouldBeEmpty();

        // A survey with no datum to measure from still lists its stations, without depths.
        var blocks = Printed(new TripSurveyStationList(
            Shown: true, Datum: null, Stations: [new("cave.ent.0", null, true)], Total: 1));
        blocks.ShouldNotContain(block => block.Kind == DocumentBlockKind.Field);
        blocks.Last().Text.ShouldBe("cave.ent.0 · entrance");
    }

    /// <summary>
    /// A survey longer than the document prints says how many lines it carries and how many the
    /// survey holds, and where the whole of it is — rather than stopping without a word.
    /// </summary>
    [Fact]
    public void A_list_cut_short_says_how_many_it_printed_and_how_many_there_are()
    {
        var blocks = Printed(Told() with { Total = 1240 });

        var closing = blocks.Last();
        closing.Kind.ShouldBe(DocumentBlockKind.Note);
        closing.Text.ShouldContain("Stations printed: 3");
        closing.Text.ShouldContain("Stations in the survey: 1240");

        // A list that holds the whole survey says nothing of the kind.
        Printed(Told()).Last().Kind.ShouldBe(DocumentBlockKind.Bullet);
    }

    /// <summary>
    /// The document's own lines are written in the language it was asked in; the station names and
    /// the depths are the survey's and are the same in both.
    /// </summary>
    [Fact]
    public void The_lines_the_document_words_itself_are_written_in_the_language_it_was_asked_in()
    {
        TripStationListWording.For("ro").ShouldBeSameAs(TripStationListWording.Romanian);
        TripStationListWording.For("RO").ShouldBeSameAs(TripStationListWording.Romanian);
        TripStationListWording.For("en").ShouldBeSameAs(TripStationListWording.English);
        TripStationListWording.For(null).ShouldBeSameAs(TripStationListWording.English);

        var blocks = Printed(Told(), TripStationListWording.Romanian);
        blocks[0].Text.ShouldContain("Stațiile topografiei");
        blocks[1].Label.ShouldBe("Adâncimi măsurate sub");
        blocks.Last().Text.ShouldBe("cave.deep.3 · 120 m");

        Printed(new TripSurveyStationList(Shown: false, null, [], 0), TripStationListWording.Romanian)
            .ShouldHaveSingleItem().Text.ShouldContain("nu este inclusă");
    }
}
