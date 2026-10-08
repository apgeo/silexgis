// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The little language a club writes its trip write-ups in. The person editing one is a club
/// secretary with a text editor, so a fault in it has to be caught where they can still see the
/// file — with the line it is on, and with all the other faults beside it, because a refusal that
/// surfaces one fault per attempt turns a five-minute edit into an afternoon.
/// </summary>
public class ReportTemplateFormatTests
{
    [Fact]
    public void The_layout_the_system_hands_out_reads_back_as_the_document_it_describes()
    {
        var read = ReportTemplateFormat.Parse(ReportTemplateFormat.Default);

        read.Ok.ShouldBeTrue(string.Join(" ", read.Errors));
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Title);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Roster);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Photographs);
        read.Parts
            .Where(p => p.Directive == ReportTemplateDirective.Section)
            .Select(p => p.Text)
            .ShouldBe(ReportTemplateFormat.Sections, ignoreOrder: true);

        // Its own comments are the whole documentation of the language, and they are the copy
        // the person editing it is holding.
        ReportTemplateFormat.Default.ShouldContain("field: <name> = <text>");
        foreach (var name in ReportTemplateFormat.Placeholders)
        {
            ReportTemplateFormat.Default.ShouldContain($"{{{name}}}");
        }
    }

    [Fact]
    public void A_word_no_layout_begins_with_is_refused_by_line_number_while_the_word_beside_it_is_kept()
    {
        var read = ReportTemplateFormat.Parse("title: {title}\nphoto: {title}\nnote: {dates}");

        read.Ok.ShouldBeFalse();
        read.Errors.ShouldHaveSingleItem().ShouldContain("Line 2");
        read.Errors[0].ShouldContain("'photo'");
        // And it says what may be written instead, because the person reading it is not holding
        // the source.
        read.Errors[0].ShouldContain("heading");

        // The same file with that one line corrected is a layout, which is what makes the
        // refusal above about the line rather than about the file.
        ReportTemplateFormat.Parse("title: {title}\ntext: {title}\nnote: {dates}").Ok.ShouldBeTrue();
    }

    [Fact]
    public void A_name_nothing_about_a_trip_answers_to_is_refused_where_it_is_written()
    {
        var read = ReportTemplateFormat.Parse("field: Position = {cave_coordinates}");

        read.Ok.ShouldBeFalse();
        read.Parts.ShouldBeEmpty();
        read.Errors.ShouldHaveSingleItem().ShouldContain("cave_coordinates");
        read.Errors[0].ShouldContain("{caves}".Trim('{', '}'));

        ReportTemplateFormat.Parse("field: Caves = {caves}").Ok.ShouldBeTrue();
    }

    /// <summary>
    /// One answer out of one part of the trip's form may be asked for by the name the form gave
    /// it, and the part has to be one of the three the form has.
    /// </summary>
    /// <remarks>
    /// The answer's own name is not checked: a club names its own questions, and a name no
    /// answer was recorded under simply comes back empty when the document is produced. What is
    /// checked is the part, because that is the half that decides whether the name can be
    /// answered out of what its reader was given at all.
    /// </remarks>
    [Fact]
    public void An_answer_may_be_asked_for_out_of_a_part_of_the_form_and_only_out_of_one_of_those()
    {
        ReportTemplateFormat.Parse("field: What happened = {safety.incident_account}").Ok.ShouldBeTrue();
        ReportTemplateFormat.Parse("field: Weight = {logistics.load_kg}").Ok.ShouldBeTrue();
        ReportTemplateFormat.Parse("field: Depth = {observations.depth_m}").Ok.ShouldBeTrue();

        var read = ReportTemplateFormat.Parse("field: Who = {roster.leader_address}");
        read.Ok.ShouldBeFalse();
        read.Errors.ShouldHaveSingleItem().ShouldContain("roster.leader_address");
    }

    [Fact]
    public void A_labelled_value_says_what_it_is_labelled_and_what_to_print()
    {
        ReportTemplateFormat.Parse("field: Weather is nice").Errors
            .ShouldHaveSingleItem().ShouldContain("no '='");
        ReportTemplateFormat.Parse("field:  = {weather}").Errors
            .ShouldHaveSingleItem().ShouldContain("no name");

        var read = ReportTemplateFormat.Parse("field: Weather = {weather}");
        read.Ok.ShouldBeTrue();
        var part = read.Parts.ShouldHaveSingleItem();
        part.Label.ShouldBe("Weather");
        part.Text.ShouldBe("{weather}");
    }

    [Fact]
    public void Everything_wrong_with_a_layout_is_said_at_once()
    {
        var read = ReportTemplateFormat.Parse(
            "title: {title}\nfield: Where = {gps}\nphoto: x\nsection: gear\nroster: everybody");

        read.Errors.Count.ShouldBe(4);
        read.Errors.ShouldContain(e => e.Contains("Line 2") && e.Contains("gps"));
        read.Errors.ShouldContain(e => e.Contains("Line 3"));
        read.Errors.ShouldContain(e => e.Contains("Line 4") && e.Contains("gear"));
        read.Errors.ShouldContain(e => e.Contains("Line 5"));

        // Refused whole: half a layout would produce half a document, and a document missing the
        // half nobody looked at is worse than one that was never produced.
        read.Parts.ShouldBeEmpty();
    }

    [Fact]
    public void A_layout_that_says_nothing_is_not_a_layout()
    {
        ReportTemplateFormat.Parse(null).Ok.ShouldBeFalse();
        ReportTemplateFormat.Parse("   ").Ok.ShouldBeFalse();
        ReportTemplateFormat.Parse("# only a note to whoever edits this").Errors
            .ShouldHaveSingleItem().ShouldContain("says nothing");
        ReportTemplateFormat.Parse(new string('x', ReportTemplateFormat.MaxLength + 1)).Ok.ShouldBeFalse();
    }

    /// <summary>
    /// Words and names are kept apart, because what a line looks like when one of its names has
    /// nothing behind it is a question about the pieces.
    /// </summary>
    [Fact]
    public void A_line_is_read_as_the_words_written_and_the_names_to_fill_in()
    {
        var tokens = ReportTemplateFormat.Tokens("{purpose} · {dates} on foot");

        tokens.Select(t => (t.IsPlaceholder, t.Text)).ShouldBe(
        [
            (true, "purpose"),
            (false, " · "),
            (true, "dates"),
            (false, " on foot"),
        ]);
    }

    /// <summary>
    /// A trip's layout may ask for the trip's tracking journal with one word that stands alone,
    /// like the roster — and a camp's layout may not, because a camp has no watch of its own.
    /// </summary>
    [Fact]
    public void The_tracking_journal_is_a_word_of_a_trips_layout_that_stands_alone_and_a_camp_has_none()
    {
        var asked = ReportTemplateFormat.Parse("title: {title}\nheading: Tracking\ntracking");
        asked.Ok.ShouldBeTrue(string.Join(" ", asked.Errors));
        var word = asked.Parts.Single(p => p.Directive == ReportTemplateDirective.Tracking);
        word.Line.ShouldBe(3);
        word.Label.ShouldBeNull();
        word.Text.ShouldBeEmpty();

        // It takes nothing after it: not a colon and a choice, not a name in braces.
        foreach (var trailing in new[] { "tracking: all", "tracking: {title}" })
        {
            var refused = ReportTemplateFormat.Parse($"title: {{title}}\n{trailing}");
            refused.Ok.ShouldBeFalse(trailing);
            refused.Errors.ShouldHaveSingleItem().ShouldContain("Line 2");
            refused.Errors[0].ShouldContain("'tracking' stands on its own");
        }

        // The same line that is a layout for a trip is refused for a camp, by its line — so the
        // refusal is about the kind and not about the word being unreadable.
        var onACamp = ReportTemplateFormat.Parse("title: {title}\ntracking", ReportTemplateKind.Expedition);
        onACamp.Ok.ShouldBeFalse();
        onACamp.Errors.ShouldHaveSingleItem().ShouldContain("Line 2");
        onACamp.Errors[0].ShouldContain("'tracking' is not something a template can ask for");
        ReportTemplateFormat.Parse("title: {title}\ntracking", ReportTemplateKind.Trip).Ok.ShouldBeTrue();

        // And a refusal on a trip's layout now offers it among the words that may be written.
        ReportTemplateFormat.Parse("title: {title}\nphoto: x").Errors.ShouldHaveSingleItem().ShouldContain("tracking");
    }

    /// <summary>
    /// The layout the system ships says how to ask for the journal and does not ask for it: what
    /// it prints is, line for line, what it printed before the word existed.
    /// </summary>
    /// <remarks>
    /// Whether a write-up carries the journal by default is a decision about what every club's
    /// documents say, not something to arrive with a new word. So the printed half of the shipped
    /// layout — every line that is not a note to its editor — is pinned by its digest, taken
    /// before the word was added. A deliberate change to what the shipped layout prints changes
    /// this number on purpose; nothing else should.
    /// </remarks>
    [Fact]
    public void The_shipped_layout_documents_the_tracking_word_and_prints_exactly_what_it_printed_before()
    {
        var lines = ReportTemplateFormat.Default.Split('\n');

        // Documented where the person editing a layout reads the vocabulary…
        lines.ShouldContain(line => line.StartsWith("#   tracking ", StringComparison.Ordinal));
        // …and asked for nowhere, in the text or in what it parses to.
        lines.ShouldNotContain(line => line.TrimStart().StartsWith("tracking", StringComparison.Ordinal));
        ReportTemplateFormat.Parse(ReportTemplateFormat.Default).Parts
            .ShouldNotContain(p => p.Directive == ReportTemplateDirective.Tracking);

        var printed = string.Join('\n', lines.Where(line => !line.StartsWith('#')));
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(printed)))
            .ShouldBe("73c1f6e1d543403eac7cee5cb80cdb52aca9ed372f0dfaf9e7b0ae7eccca69cf");

        // A camp's layout neither documents the word nor could use it.
        ReportTemplateFormat.ExpeditionDefault.Split('\n')
            .ShouldNotContain(line => line.StartsWith("#   tracking ", StringComparison.Ordinal));
    }
}
