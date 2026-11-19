// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Shouldly;
using SilexGis.Infrastructure.Documents.Extraction;
using SilexGis.Infrastructure.Files;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace SilexGis.Api.Tests;

/// <summary>
/// An office or OpenDocument package holding more entries than any document has is judged from
/// its own directory records before the runtime builds an object for each of them — by the upload
/// request that sniffs its format and by the readers that extract its text.
/// <para>
/// Every package here is generated: an ordinary document, and the same document with one more
/// empty entry than the ceiling beside it. That is a few hundred kilobytes; the same shape scaled
/// to millions of entries costs gigabytes of directory objects once opened, which is the cost the
/// check exists to refuse. The ordinary document is read in each test, so a check that refused
/// every package could not pass.
/// </para>
/// </summary>
public sealed class CrowdedPackageTests
{
    private const string WordMediaType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string OpenDocumentMediaType = "application/vnd.oasis.opendocument.text";

    [Fact]
    public async Task The_upload_request_names_an_ordinary_package_and_leaves_a_crowded_one_a_plain_zip()
    {
        using (var ordinary = WordDocument())
        {
            (await ContentSniffer.DetectAsync(ordinary, "application/octet-stream", "raport.docx",
                TestContext.Current.CancellationToken)).MimeType.ShouldBe(WordMediaType);
        }

        using var crowded = Crowd(WordDocument());
        (await ContentSniffer.DetectAsync(crowded, "application/octet-stream", "raport.docx",
            TestContext.Current.CancellationToken)).MimeType.ShouldBe("application/zip");
    }

    [Fact]
    public async Task A_crowded_word_package_is_refused_before_it_is_opened()
    {
        using (var ordinary = WordDocument())
        {
            var read = await new OfficeOpenXmlTextExtractor().ExtractAsync(ordinary, TestContext.Current.CancellationToken);
            read.Pages.ShouldContain(page => page.Text != null && page.Text.Contains("Peștera Șura Mare"));
        }

        using var crowded = Crowd(WordDocument());
        await Should.ThrowAsync<ContentTooLargeException>(
            () => new OfficeOpenXmlTextExtractor().ExtractAsync(crowded, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_crowded_open_document_is_refused_before_it_is_opened()
    {
        using (var ordinary = OpenDocument())
        {
            var read = await new OpenDocumentTextExtractor().ExtractAsync(ordinary, TestContext.Current.CancellationToken);
            read.Pages[0].Text.ShouldBe("Peștera Șura Mare");
        }

        using var crowded = Crowd(OpenDocument());
        await Should.ThrowAsync<ContentTooLargeException>(
            () => new OpenDocumentTextExtractor().ExtractAsync(crowded, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_upload_sniffer_and_the_readers_share_one_ceiling()
    {
        // Packages the sniffer names are packages the readers open: one figure, not two.
        ExtractionStreams.MaxPackageEntries.ShouldBe(10_000);
    }

    /// <summary>The package with one more empty entry than the ceiling added to it.</summary>
    private static MemoryStream Crowd(MemoryStream package)
    {
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            for (var i = 0; i <= ExtractionStreams.MaxPackageEntries; i++)
            {
                archive.CreateEntry($"extra/{i}");
            }
        }

        package.Length.ShouldBeLessThan(2 * 1024 * 1024);
        package.Position = 0;
        return package;
    }

    private static MemoryStream WordDocument()
    {
        var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            buffer, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Word.Document(new Word.Body(
                new Word.Paragraph(new Word.Run(new Word.Text("Peștera Șura Mare")))));
        }

        buffer.Position = 0;
        return buffer;
    }

    private static MemoryStream OpenDocument()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("content.xml").Open();
            var bytes = Encoding.UTF8.GetBytes(
                "<office:document-content xmlns:office=\"urn:office\" xmlns:text=\"urn:text\">"
                + "<office:body><office:text><text:p>Peștera Șura Mare</text:p></office:text></office:body>"
                + "</office:document-content>");
            entry.Write(bytes, 0, bytes.Length);
        }

        buffer.Position = 0;
        return buffer;
    }
}
