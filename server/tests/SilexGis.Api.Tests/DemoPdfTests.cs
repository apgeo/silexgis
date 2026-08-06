// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Infrastructure.Documents.Extraction;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The demo dataset's report is a real PDF, and this is what says so.
///
/// It is hand-built rather than produced by a library, so the thing most likely to go wrong is
/// that somebody edits its structure and it quietly stops being readable — a viewer would still
/// show something, because readers repair broken files, while the reading that feeds the search
/// index and the offsets a text anchor is measured against would come back empty. Every claim
/// the demo makes about documents would then be a claim about nothing.
///
/// So the file is put through the same reader a genuine upload goes through, and the passage the
/// demo's own resource link points at is required to be exactly where the seeder says it is.
/// That last assertion is the one that matters: it is the only thing tying the anchor written at
/// seed time to the text produced later by a background job, and nothing else would notice them
/// drifting apart.
/// </summary>
public sealed class DemoPdfTests
{
    [Fact]
    public async Task The_demo_report_reads_as_two_pages_of_real_text()
    {
        var read = await ReadAsync();

        read.Extractor.ShouldBe("pdf");
        read.Pages.Count.ShouldBe(DemoPdf.Pages.Count);
        read.Pages.Select(p => p.PageNumber).ShouldBe([1, 2]);
        foreach (var page in read.Pages)
        {
            page.Text.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Every_line_the_report_was_built_from_comes_back_out_of_it()
    {
        var read = await ReadAsync();

        for (var i = 0; i < DemoPdf.Pages.Count; i += 1)
        {
            var text = read.Pages[i].Text!;
            foreach (var line in DemoPdf.Pages[i].Where(l => l.Length > 0))
            {
                text.ShouldContain(line);
            }
        }
    }

    [Fact]
    public async Task The_linked_passage_sits_where_the_seeder_says_it_does()
    {
        var read = await ReadAsync();
        var extracted = read.Pages[0].Text!;

        // The seeder computes the offsets from the lines it built the page out of, joined the
        // way the reader joins them. If either side changes its mind about that, the demo link
        // starts pointing at the wrong words — which is exactly the failure a reader cannot see.
        var expected = string.Join('\n', DemoPdf.Pages[0]).IndexOf(DemoPdf.LinkedQuote, StringComparison.Ordinal);
        expected.ShouldBeGreaterThanOrEqualTo(0);

        extracted.IndexOf(DemoPdf.LinkedQuote, StringComparison.Ordinal).ShouldBe(expected);
        extracted.Substring(expected, DemoPdf.LinkedQuote.Length).ShouldBe(DemoPdf.LinkedQuote);
    }

    private static async Task<ExtractedText> ReadAsync()
    {
        using var stream = new MemoryStream(DemoPdf.Build(), writable: false);
        return await new PdfTextExtractor().ExtractAsync(stream, CancellationToken.None);
    }
}
