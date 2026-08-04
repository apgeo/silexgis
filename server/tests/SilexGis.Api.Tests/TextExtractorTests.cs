// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using Shouldly;
using SilexGis.Infrastructure.Documents.Extraction;

namespace SilexGis.Api.Tests;

/// <summary>
/// The readers that need no dependency beyond the framework, exercised over files built here.
/// <para>
/// Built byte by byte rather than committed as fixtures so nothing depends on how a checkout
/// treats an unfamiliar extension, and so the encoding of each sample is a decision this test
/// made rather than one it had to trust.
/// </para>
/// </summary>
public class TextExtractorTests
{
    private static readonly TextExtractorSelector Selector = new(
        [new PlainTextExtractor(), new OpenDocumentTextExtractor(), new RichTextExtractor()]);

    [Theory]
    [InlineData("text/plain", "plain-text")]
    [InlineData("text/csv", "plain-text")]
    [InlineData("application/json", "plain-text")]
    [InlineData("application/rtf", "rich-text")]
    [InlineData("application/vnd.oasis.opendocument.text", "opendocument")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", "opendocument")]
    public void A_format_is_dispatched_to_the_reader_that_understands_it(
        string mimeType, string extractor)
    {
        Selector.For(mimeType)?.Name.ShouldBe(extractor);
        Selector.CanExtract(mimeType).ShouldBeTrue();
    }

    [Fact]
    public async Task A_text_file_in_a_legacy_code_page_keeps_its_diacritics()
    {
        // "Peştera" with the s-cedilla written as the single byte a Central European code page
        // gives it, which is not valid UTF-8 and would otherwise be lost.
        byte[] bytes = [0x50, 0x65, 0xBA, 0x74, 0x65, 0x72, 0x61];
        using var content = new MemoryStream(bytes);

        var result = await Selector.For("text/plain")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("plain-text");
        result.Version.ShouldBe(1);
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].PageNumber.ShouldBe(1);
        result.Pages[0].Text.ShouldBe("Peştera");
    }

    [Fact]
    public async Task An_empty_text_file_is_a_page_with_no_text_rather_than_no_page()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("   \r\n  "));

        var result = await Selector.For("text/plain")!
            .ExtractAsync(content, CancellationToken.None);

        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldBeNull();
    }

    [Fact]
    public async Task An_open_document_yields_its_paragraphs()
    {
        using var content = OpenDocument(
            "<office:document-content xmlns:office=\"urn:office\" xmlns:text=\"urn:text\">"
            + "<office:body><office:text>"
            + "<text:h>Peștera Șura Mare</text:h>"
            + "<text:p>Prima<text:tab/>vizită</text:p>"
            + "<text:p>A doua<text:line-break/>vizită</text:p>"
            + "<office:annotation><text:p>not the document</text:p></office:annotation>"
            + "</office:text></office:body></office:document-content>");

        var extractor = Selector.For("application/vnd.oasis.opendocument.text")!;
        var result = await extractor.ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("opendocument");
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldBe("Peștera Șura Mare\nPrima\tvizită\nA doua\nvizită");
    }

    /// <summary>
    /// A damaged file is not a file with no text: one is worth reading again, the other is not,
    /// and a reader that returned nothing for both would make them indistinguishable.
    /// </summary>
    [Fact]
    public async Task An_archive_without_a_content_part_is_reported_as_damaged()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("mimetype");
        }

        buffer.Position = 0;
        var extractor = Selector.For("application/vnd.oasis.opendocument.text")!;

        await Should.ThrowAsync<InvalidDataException>(
            () => extractor.ExtractAsync(buffer, CancellationToken.None));
    }

    [Fact]
    public async Task A_rich_text_file_loses_its_markup_and_keeps_its_text()
    {
        var rtf = "{\\rtf1\\ansi\\ansicpg1250{\\fonttbl{\\f0 Times;}}"
            + "Pe\\'BAtera\\par Sala Mare}";
        using var content = new MemoryStream(Encoding.Latin1.GetBytes(rtf));

        var result = await Selector.For("application/rtf")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("rich-text");
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldBe("Peştera\nSala Mare");
    }

    /// <summary>An OpenDocument file is a ZIP whose <c>content.xml</c> is the document.</summary>
    private static MemoryStream OpenDocument(string contentXml)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("content.xml").Open();
            var bytes = Encoding.UTF8.GetBytes(contentXml);
            entry.Write(bytes, 0, bytes.Length);
        }

        buffer.Position = 0;
        return buffer;
    }
}
