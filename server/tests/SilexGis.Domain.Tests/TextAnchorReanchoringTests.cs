// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Domain.Tests;

public class TextAnchorReanchoringTests
{
    private static TextRangeAnchor Anchor(string stream, string quote, string? prefix = null, string? suffix = null)
    {
        var at = stream.IndexOf(quote, StringComparison.Ordinal);
        return new TextRangeAnchor(at, at + quote.Length, quote, prefix, suffix, null);
    }

    [Fact]
    public void An_untouched_passage_keeps_the_offsets_it_was_written_with()
    {
        const string stream = "The entrance is above the scree. The passage widens after the sump.";
        var anchor = Anchor(stream, "The passage widens");

        var located = TextAnchorReanchoring.Locate(anchor, stream);

        located.Outcome.ShouldBe(ReanchorOutcome.Unmoved);
        located.Start.ShouldBe(anchor.Start);
        located.End.ShouldBe(anchor.End);
    }

    [Fact]
    public void Text_inserted_above_a_passage_moves_it_rather_than_breaking_it()
    {
        const string before = "The passage widens after the sump.";
        var anchor = Anchor(before, "widens after the sump");

        // This is the defect the whole module exists for. Without re-measuring, the anchor
        // still resolves and still renders — over the wrong words, silently.
        const string after = "A new opening paragraph.\n\nThe passage widens after the sump.";
        var located = TextAnchorReanchoring.Locate(anchor, after);

        located.Outcome.ShouldBe(ReanchorOutcome.Moved);
        after[located.Start..located.End].ShouldBe("widens after the sump");
    }

    [Fact]
    public void A_passage_that_is_gone_is_reported_lost_and_never_guessed_at()
    {
        var anchor = Anchor("The passage widens after the sump.", "widens after the sump");

        var located = TextAnchorReanchoring.Locate(anchor, "Nothing of the kind is written here at all.");

        located.Outcome.ShouldBe(ReanchorOutcome.Lost);

        // The offsets come back exactly as authored. Clamping them into the new stream would
        // turn "this pointed at something that is gone" into "this points at these other
        // words", which is the one outcome a reader cannot detect.
        located.Start.ShouldBe(anchor.Start);
        located.End.ShouldBe(anchor.End);
    }

    [Fact]
    public void Recorded_context_tells_two_copies_of_the_same_sentence_apart()
    {
        const string stream =
            "In the north branch the roof lowers. Take the left fork. "
            + "In the south branch the roof lowers. Take the right fork.";

        // Authored against the second occurrence, with what ran up to it.
        var second = stream.LastIndexOf("the roof lowers", StringComparison.Ordinal);
        var anchor = new TextRangeAnchor(
            second,
            second + "the roof lowers".Length,
            "the roof lowers",
            Prefix: "In the south branch ",
            Suffix: ". Take the right",
            Page: null);

        // An edit above pushes everything down, so the offsets no longer name it and both
        // occurrences are candidates. The context is what decides.
        const string edited = "A preface.\n\n" + stream;
        var located = TextAnchorReanchoring.Locate(anchor, edited);

        located.Outcome.ShouldBe(ReanchorOutcome.Moved);
        edited[..located.Start].ShouldEndWith("In the south branch ");
    }

    [Fact]
    public void With_nothing_to_tell_them_apart_the_nearest_occurrence_wins()
    {
        const string stream = "the sump. " + "filler. " + "the sump. " + "filler. " + "the sump.";

        // Authored against the last occurrence, with no context recorded at all.
        var last = stream.LastIndexOf("the sump", StringComparison.Ordinal);
        var anchor = new TextRangeAnchor(last, last + 8, "the sump", null, null, null);

        // One character inserted at the front: every occurrence shifts by one, and the last is
        // still the one nearest to where the anchor was.
        var located = TextAnchorReanchoring.Locate(anchor, " " + stream);

        located.Outcome.ShouldBe(ReanchorOutcome.Moved);
        located.Start.ShouldBe(last + 1);
    }

    [Fact]
    public void Reading_a_payload_that_is_not_a_text_range_gives_nothing_rather_than_throwing()
    {
        // Payloads are free-form JSON written by whichever client wrote them, so every read is
        // a question.
        TextAnchorReanchoring.Read(null).ShouldBeNull();
        TextAnchorReanchoring.Read("not json").ShouldBeNull();
        TextAnchorReanchoring.Read("[]").ShouldBeNull();
        TextAnchorReanchoring.Read("{\"start\":1}").ShouldBeNull();
        TextAnchorReanchoring.Read("{\"start\":1,\"end\":4,\"quote\":\"\"}").ShouldBeNull();
        TextAnchorReanchoring.Read("{\"start\":\"1\",\"end\":4,\"quote\":\"abc\"}").ShouldBeNull();
    }

    [Fact]
    public void Rewriting_a_payload_moves_the_offsets_and_leaves_everything_else_as_written()
    {
        const string payload =
            "{\"start\":10,\"end\":18,\"quote\":\"the sump\",\"prefix\":\"before \",\"suffix\":\" after\",\"page\":3}";

        var anchor = TextAnchorReanchoring.Read(payload);
        anchor.ShouldNotBeNull();

        var rewritten = TextAnchorReanchoring.Read(TextAnchorReanchoring.Write(anchor.Value, 40, 48));

        rewritten.ShouldNotBeNull();
        rewritten.Value.Start.ShouldBe(40);
        rewritten.Value.End.ShouldBe(48);
        rewritten.Value.Quote.ShouldBe("the sump");
        rewritten.Value.Prefix.ShouldBe("before ");
        rewritten.Value.Suffix.ShouldBe(" after");
        rewritten.Value.Page.ShouldBe(3);
    }

    [Fact]
    public void A_quote_longer_than_the_new_text_is_lost_without_scanning_for_it()
    {
        var anchor = Anchor("a long passage of prose", "long passage of prose");
        TextAnchorReanchoring.Locate(anchor, "short").Outcome.ShouldBe(ReanchorOutcome.Lost);
    }
}
