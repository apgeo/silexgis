// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

public class AnnotatedTextTests
{
    private static AnnotatedBlock P(string text, params AnnotatedMark[] marks) =>
        new(AnnotatedBlockType.Paragraph, text, marks.Length == 0 ? null : marks);

    [Fact]
    public void The_canonical_stream_is_the_blocks_joined_by_a_blank_line()
    {
        AnnotatedText.CanonicalText([P("First."), P("Second.")]).ShouldBe("First.\n\nSecond.");
    }

    [Fact]
    public void Block_starts_index_the_stream_they_describe()
    {
        var blocks = new[] { P("First."), P("Second."), P("Third.") };
        var stream = AnnotatedText.CanonicalText(blocks);
        var starts = AnnotatedText.BlockStarts(blocks);

        for (var i = 0; i < blocks.Length; i++)
        {
            // The whole offset contract in one assertion: the characters at a block's recorded
            // start are that block's own text. Everything the browser does with an anchor —
            // painting a highlight, turning a selection into offsets — is this arithmetic run
            // the other way, so a change here that this does not catch is a change that puts
            // every highlight in the application on the wrong words.
            stream.Substring(starts[i], blocks[i].Text.Length).ShouldBe(blocks[i].Text);
        }
    }

    [Fact]
    public void The_canonical_stream_survives_being_normalised()
    {
        // Extracted text is stored normalised. If normalising changed the stream, anchors
        // computed against the stream would address a different string than the one they are
        // resolved against — which is precisely the class of defect no reader could detect.
        var blocks = new[]
        {
            new AnnotatedBlock(AnnotatedBlockType.Heading1, "Galeria Mare"),
            P("From the second sump the passage widens to a chamber."),
            new AnnotatedBlock(AnnotatedBlockType.BulletItem, "Șaua Mică — 1974"),
            new AnnotatedBlock(AnnotatedBlockType.Code, "1.2\t3.4\t5.6"),
        };

        var stream = AnnotatedText.CanonicalText(blocks);
        PageText.Normalize(stream).ShouldBe(stream);
    }

    [Fact]
    public void Offsets_are_utf16_code_units_on_both_sides_of_the_wire()
    {
        // The browser counts a string's length in UTF-16 code units and so does this; an emoji
        // is two on both sides. Anything that "corrected" either side to count runes would
        // move every anchor past the first astral character, in some documents only.
        var blocks = new[] { P("Sala 🜃 mare"), P("after") };
        var stream = AnnotatedText.CanonicalText(blocks);

        blocks[0].Text.Length.ShouldBe(12);
        AnnotatedText.BlockStarts(blocks)[1].ShouldBe(14);
        stream[14..].ShouldBe("after");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("two\nlines")]
    [InlineData("carriage\rreturn")]
    public void A_block_whose_text_would_not_survive_normalising_is_refused(string text)
    {
        // Each of these is refused because of what it would do to the stream, not for tidiness:
        // an empty block or a stray newline produces a run of blank lines that normalising
        // collapses, and surrounding space is trimmed off the ends of the whole page.
        AnnotatedText.BodyProblem(new AnnotatedTextBody([P(text)])).ShouldNotBeNull();
    }

    [Fact]
    public void A_body_holds_at_least_one_block()
    {
        AnnotatedText.BodyProblem(new AnnotatedTextBody([])).ShouldNotBeNull();
        AnnotatedText.BodyProblem(null).ShouldNotBeNull();
    }

    [Fact]
    public void Emphasis_must_lie_inside_its_block_and_not_double_up()
    {
        AnnotatedText.BodyProblem(new AnnotatedTextBody([P("word", new AnnotatedMark(0, 4, AnnotatedMarkKind.Bold))]))
            .ShouldBeNull();

        AnnotatedText.BodyProblem(new AnnotatedTextBody([P("word", new AnnotatedMark(0, 9, AnnotatedMarkKind.Bold))]))
            .ShouldNotBeNull();
        AnnotatedText.BodyProblem(new AnnotatedTextBody([P("word", new AnnotatedMark(2, 2, AnnotatedMarkKind.Bold))]))
            .ShouldNotBeNull();

        // Different kinds may overlap — bold running through italic is ordinary typography.
        AnnotatedText.BodyProblem(new AnnotatedTextBody([
            P("a longer word", new AnnotatedMark(0, 8, AnnotatedMarkKind.Bold), new AnnotatedMark(2, 13, AnnotatedMarkKind.Italic)),
        ])).ShouldBeNull();

        // The same kind twice over one stretch is not; the client normalises before sending.
        AnnotatedText.BodyProblem(new AnnotatedTextBody([
            P("a longer word", new AnnotatedMark(0, 8, AnnotatedMarkKind.Bold), new AnnotatedMark(2, 13, AnnotatedMarkKind.Bold)),
        ])).ShouldNotBeNull();
    }

    [Fact]
    public void A_body_round_trips_through_the_bytes_that_get_stored()
    {
        var body = new AnnotatedTextBody([
            new AnnotatedBlock(AnnotatedBlockType.Heading2, "Descoperire"),
            P("Găsită în 1974.", new AnnotatedMark(0, 6, AnnotatedMarkKind.Italic)),
        ]);

        var read = AnnotatedText.Deserialize(AnnotatedText.Serialize(body));

        read.ShouldNotBeNull();
        AnnotatedText.CanonicalText(read.Blocks).ShouldBe(AnnotatedText.CanonicalText(body.Blocks));
        read.Blocks[0].Type.ShouldBe(AnnotatedBlockType.Heading2);
        read.Blocks[1].Marks!.Single().Kind.ShouldBe(AnnotatedMarkKind.Italic);
    }

    [Fact]
    public void Block_types_are_spelled_out_in_the_stored_bytes()
    {
        // The body is a file that outlives this source tree. A file of "type": 4 is a file
        // nobody can read without it.
        var json = System.Text.Encoding.UTF8.GetString(
            AnnotatedText.Serialize(new AnnotatedTextBody([new AnnotatedBlock(AnnotatedBlockType.Heading2, "T")])));

        json.ShouldContain("\"h2\"");
    }

    [Fact]
    public void Bytes_that_are_not_a_body_read_back_as_nothing_rather_than_throwing()
    {
        AnnotatedText.Deserialize("not json"u8).ShouldBeNull();
        AnnotatedText.Deserialize("{\"blocks\":null}"u8).ShouldBeNull();
    }

    [Fact]
    public void Plain_text_becomes_paragraphs_that_the_rules_accept()
    {
        var body = AnnotatedText.FromPlainText("  First para,\nwrapped.\n\n\nSecond para.  \n");

        AnnotatedText.BodyProblem(body).ShouldBeNull();
        body.Blocks.Count.ShouldBe(2);

        // A newline inside a paragraph is wrapping rather than structure, so it becomes a
        // space: a block is one line by contract, and keeping it would make the body invalid.
        body.Blocks[0].Text.ShouldBe("First para, wrapped.");
        body.Blocks[1].Text.ShouldBe("Second para.");
    }
}
