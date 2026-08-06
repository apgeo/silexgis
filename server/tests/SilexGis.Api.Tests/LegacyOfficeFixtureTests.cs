// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Infrastructure.Documents.Extraction;

namespace SilexGis.Api.Tests;

/// <summary>
/// The pre-2007 binary Office formats, read out of files an office suite actually wrote.
/// <para>
/// Everywhere else these formats are exercised with containers this test project builds
/// itself, which can say which stream a container holds and what its header declares, but
/// cannot say that a real report yields real sentences: nothing follows those headers but
/// padding. The two fixtures beside this file close that gap. They were written by
/// LibreOffice from the flat-ODF sources committed next to them
/// (<c>soffice --headless --convert-to doc:"MS Word 97"</c>, and the presentation
/// equivalent), so the bytes come from a suite that writes the format rather than from the
/// library that reads it — which is the only arrangement in which "the reader understood a
/// real file" means anything.
/// </para>
/// <para>
/// The sentences are deliberately accented Romanian, because folding accents is the archive's
/// normal case and a reader that mangles the encoding would still pass an ASCII assertion.
/// </para>
/// </summary>
public class LegacyOfficeFixtureTests
{
    private static readonly TextExtractorSelector Selector = new([new LegacyOfficeTextExtractor()]);

    private static Stream Fixture(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task A_word_document_written_by_an_office_suite_gives_up_its_sentences()
    {
        using var content = Fixture("legacy-report.doc");

        var result = await Selector.For("application/msword")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("legacy-office");

        // A word-processor document has no pages until something lays it out, so the reader
        // returns the body as one page rather than guessing where a printer would break it.
        result.Pages.Count.ShouldBe(1);
        var text = result.Pages[0].Text.ShouldNotBeNull();
        text.ShouldContain("Peștera Demo Mare");
        text.ShouldContain("412 metri");
    }

    [Fact]
    public async Task A_presentation_written_by_an_office_suite_gives_up_its_slides()
    {
        using var content = Fixture("legacy-slides.ppt");

        var result = await Selector.For("application/vnd.ms-powerpoint")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("legacy-office");
        result.Pages.Count.ShouldBeGreaterThanOrEqualTo(1);

        var text = string.Join('\n', result.Pages.Select(page => page.Text));
        text.ShouldContain("Raport de explorare");
        text.ShouldContain("412 metri");
    }
}
