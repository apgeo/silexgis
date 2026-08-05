// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

public class LanguageDetectionTests
{
    private const string RomanianProse =
        "Peștera se află în versantul nordic al masivului și a fost explorată prin galeria "
        + "principală. Sala mare este acoperită cu formațiuni, iar cursul de apă se pierde "
        + "sub peretele de calcar. Cartarea a fost făcută de echipa clubului.";

    private const string EnglishProse =
        "The cave is located on the northern slope of the massif and was explored through the "
        + "main gallery. The large chamber is covered with formations, and the stream sinks "
        + "under the limestone wall. The survey was made by the club team.";

    [Fact]
    public void Reads_romanian_prose_as_romanian()
    {
        LanguageDetection.Detect(RomanianProse).ShouldBe(LanguageDetection.Romanian);
    }

    [Fact]
    public void Reads_english_prose_as_english()
    {
        LanguageDetection.Detect(EnglishProse).ShouldBe(LanguageDetection.English);
    }

    [Fact]
    public void Reads_romanian_typed_without_diacritics_as_romanian()
    {
        // Older material here was typed on keyboards that had no Romanian letters at all, and
        // it is no less Romanian for it. The word signal is what has to carry this case, so it
        // is asserted against the same paragraph stripped of every diacritic.
        const string stripped =
            "Pestera se afla in versantul nordic al masivului si a fost explorata prin galeria "
            + "principala. Sala mare este acoperita cu formatiuni, iar cursul de apa se pierde "
            + "sub peretele de calcar. Cartarea a fost facuta de echipa clubului.";

        LanguageDetection.Detect(stripped).ShouldBe(LanguageDetection.Romanian);
    }

    [Fact]
    public void A_single_romanian_sentence_is_enough()
    {
        LanguageDetection.Detect("Galeria principală este închisă în timpul iernii.")
            .ShouldBe(LanguageDetection.Romanian);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("IMG_20240712_113355.jpg")]
    [InlineData("1 2 3 4 5 6 7 8 9 10")]
    [InlineData("X Y Z 145,3 -12,8 2026-08-04")]
    public void Says_nothing_when_there_is_nothing_to_read(string? text)
    {
        // Silence is the right answer for a filename, a column of numbers or an empty page:
        // the neutral configuration matches the words as they were written, whereas a guessed
        // stemmer rewrites them into forms no query will produce.
        LanguageDetection.Detect(text).ShouldBeNull();
    }

    [Fact]
    public void Says_nothing_when_the_two_languages_are_evenly_matched()
    {
        // A bilingual abstract must not be stemmed as whichever language happened to be ahead
        // by a word. The positive cases above prove the same detector does answer when the
        // evidence is one-sided, so this is a refusal, not an inability.
        var bilingual = EnglishProse + "\n\n" + EnglishProse + "\n\n" + RomanianProse;

        LanguageDetection.Detect(bilingual).ShouldBeNull();
    }

    [Fact]
    public void Skips_pages_that_carry_no_text()
    {
        // A scanned plate inside an otherwise readable report is a null page, not a page of
        // nothing, and it must not be allowed to end the reading early.
        string?[] pages = [null, "", RomanianProse];

        LanguageDetection.Detect(pages).ShouldBe(LanguageDetection.Romanian);
    }

    [Fact]
    public void Detected_codes_are_already_in_the_stored_form()
    {
        // Whatever the detector answers is written straight to the document row, so it has to
        // survive the normaliser unchanged - otherwise the column and the search configuration
        // lookup would disagree about the same document.
        DocumentLanguage.Normalize(LanguageDetection.Romanian).ShouldBe(LanguageDetection.Romanian);
        DocumentLanguage.Normalize(LanguageDetection.English).ShouldBe(LanguageDetection.English);
        LanguageDetection.Detect(RomanianProse).ShouldBe(LanguageDetection.Romanian);
    }

    [Fact]
    public void Reads_a_bounded_sample_rather_than_the_whole_document()
    {
        // A survey archive holds documents of a million characters and the question is settled
        // by the first page or two, so only a bounded head is read. Proven by putting enough
        // English in front to fill the sample and far more Romanian behind it: if the whole
        // text were read, Romanian would win outright.
        var head = string.Concat(Enumerable.Repeat(EnglishProse + "\n", 400));
        var tail = string.Concat(Enumerable.Repeat(RomanianProse + "\n", 4000));

        LanguageDetection.Detect(head + tail).ShouldBe(LanguageDetection.English);
    }
}
