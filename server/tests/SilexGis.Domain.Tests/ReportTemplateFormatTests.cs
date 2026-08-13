// SPDX-License-Identifier: AGPL-3.0-or-later
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
}
