// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using Shouldly;
using SilexGis.Infrastructure.Documents.Extraction;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The readers that stand on a library — the portable document format, the packaged Office
/// formats, and the compound files that predate them.
/// <para>
/// Every sample is written here by the same libraries that read it back, so each is a real file
/// of its format rather than a fixture whose provenance a checkout could change. The one
/// exception is the portable document, assembled byte by byte below because no writer for it is
/// present and because a page that carries a picture and no text — the ordinary condition of a
/// scanned archive — has to be constructed deliberately to be tested at all.
/// </para>
/// </summary>
public class PdfAndOfficeTextExtractorTests
{
    private static readonly TextExtractorSelector Selector = new(
        [
            new PlainTextExtractor(), new OpenDocumentTextExtractor(), new RichTextExtractor(),
            new PdfTextExtractor(), new OfficeOpenXmlTextExtractor(),
            new LegacyOfficeTextExtractor(),
        ]);

    [Theory]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "office-open-xml")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "office-open-xml")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "office-open-xml")]
    [InlineData("application/vnd.ms-excel", "legacy-office")]
    [InlineData("application/x-ole-storage", "legacy-office")]
    public void A_format_is_dispatched_to_the_reader_that_understands_it(
        string mimeType, string extractor)
    {
        Selector.For(mimeType)?.Name.ShouldBe(extractor);
        Selector.CanExtract(mimeType).ShouldBeTrue();
    }

    /// <summary>
    /// A format nothing reads must report itself as such rather than as a file with no text.
    /// The first two carry a text layer and no reader here implements their body format, which
    /// is the case worth keeping visible; the third carries none and is never queued at all.
    /// </summary>
    [Theory]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-powerpoint")]
    [InlineData("image/jpeg")]
    public void A_format_no_reader_handles_is_reported_rather_than_silently_empty(string mimeType)
    {
        Selector.For(mimeType).ShouldBeNull();
        Selector.CanExtract(mimeType).ShouldBeFalse();
    }

    [Fact]
    public async Task A_portable_document_yields_one_result_page_per_page_of_the_file()
    {
        var pdf = Pdf([TextPage("Pestera Sura Mare"), TextPage("Sala Mare")]);
        using var content = new MemoryStream(pdf);

        var result = await Selector.For("application/pdf")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("pdf");
        result.Version.ShouldBe(1);
        result.Pages.Count.ShouldBe(2);
        result.Pages[0].PageNumber.ShouldBe(1);
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Pestera Sura Mare");
        result.Pages[1].PageNumber.ShouldBe(2);
        result.Pages[1].Text.ShouldNotBeNull().ShouldContain("Sala Mare");
    }

    /// <summary>
    /// A page holding a picture of writing rather than writing is the ordinary condition of a
    /// scanned archive. It has to come back as a numbered page with nothing on it: a failure
    /// would be retried forever, and a missing page would make the file's own numbering wrong
    /// for every page after it.
    /// </summary>
    [Fact]
    public async Task A_page_that_is_only_a_picture_reports_no_text_rather_than_failing()
    {
        var pdf = Pdf([TextPage("Pestera Sura Mare"), PicturePage()]);
        using var content = new MemoryStream(pdf);

        var result = await Selector.For("application/pdf")!
            .ExtractAsync(content, CancellationToken.None);

        // The readable page proves the reading itself worked, so the empty one is a fact about
        // that page rather than about the reader.
        result.Pages.Count.ShouldBe(2);
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Pestera Sura Mare");
        result.Pages[1].PageNumber.ShouldBe(2);
        result.Pages[1].Text.ShouldBeNull();
    }

    /// <summary>
    /// Bytes that are not the format they were filed under are unreadable, which is a different
    /// answer from "has no text" — one is worth reading again once the file is replaced, the
    /// other never is.
    /// </summary>
    [Fact]
    public async Task Bytes_that_are_not_a_portable_document_are_reported_as_damaged()
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 and then nothing"));

        await Should.ThrowAsync<Exception>(
            () => Selector.For("application/pdf")!.ExtractAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task A_word_processing_document_yields_its_paragraphs_as_one_page()
    {
        using var content = new MemoryStream(WordDocument());

        var result = await Selector
            .For("application/vnd.openxmlformats-officedocument.wordprocessingml.document")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("office-open-xml");
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].PageNumber.ShouldBe(1);
        result.Pages[0].Text.ShouldBe("Peștera Șura Mare\nPrima vizită");
    }

    /// <summary>
    /// A cell holding a string keeps an index into the workbook's shared table rather than the
    /// characters, so a reader that took the cell at face value would return numbers.
    /// </summary>
    [Fact]
    public async Task A_workbook_yields_one_page_per_sheet_with_its_shared_strings_resolved()
    {
        using var content = new MemoryStream(SpreadsheetDocumentBytes());

        var result = await Selector
            .For("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")!
            .ExtractAsync(content, CancellationToken.None);

        result.Pages.Count.ShouldBe(2);
        result.Pages[0].PageNumber.ShouldBe(1);
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Galerii");
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Peștera");
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("1450");
        result.Pages[1].PageNumber.ShouldBe(2);
        result.Pages[1].Text.ShouldNotBeNull().ShouldContain("Note");
    }

    [Fact]
    public async Task A_presentation_yields_one_page_per_slide_in_presentation_order()
    {
        using var content = new MemoryStream(PresentationBytes());

        var result = await Selector
            .For("application/vnd.openxmlformats-officedocument.presentationml.presentation")!
            .ExtractAsync(content, CancellationToken.None);

        result.Pages.Count.ShouldBe(2);
        result.Pages[0].Text.ShouldBe("Peștera Șura Mare");
        result.Pages[1].Text.ShouldBe("Sala Mare");
    }

    [Fact]
    public async Task A_legacy_workbook_yields_its_cells()
    {
        using var content = new MemoryStream(LegacyWorkbook());

        var result = await Selector.For("application/vnd.ms-excel")!
            .ExtractAsync(content, CancellationToken.None);

        result.Extractor.ShouldBe("legacy-office");
        result.Version.ShouldBe(1);
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].PageNumber.ShouldBe(1);
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Galerii");
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Peștera Șura Mare");
    }

    /// <summary>
    /// A compound file whose name settled nothing is opened and asked what it is. A workbook is
    /// read; anything else is refused, because reporting it as a document with no text would
    /// hide a file that becomes readable the day something can read it.
    /// </summary>
    [Fact]
    public async Task A_compound_file_that_is_not_a_workbook_is_refused_rather_than_read_as_empty()
    {
        using var workbook = new MemoryStream(LegacyWorkbook());
        var readable = await Selector.For("application/x-ole-storage")!
            .ExtractAsync(workbook, CancellationToken.None);
        readable.Pages[0].Text.ShouldNotBeNull().ShouldContain("Galerii");

        using var wordProcessor = new MemoryStream(CompoundFile("WordDocument"));

        await Should.ThrowAsync<NotSupportedException>(
            () => Selector.For("application/x-ole-storage")!
                .ExtractAsync(wordProcessor, CancellationToken.None));
    }

    /// <summary>
    /// A scanned archive's documents are large — a hundred megabytes of photographed pages is
    /// ordinary, and the installation's own upload limit accepts far more than that. Both of
    /// these formats keep the part that says where everything is at the <em>end</em> of the
    /// file, so a reader handed only the beginning of one finds no pages at all and reports an
    /// intact document as damaged. Nothing here may cut a stored file short.
    /// </summary>
    [Fact]
    public async Task A_portable_document_larger_than_a_reader_may_hold_is_still_read()
    {
        using var content = LargePdf();
        content.Length.ShouldBeGreaterThan(InMemoryBoundBytes);

        var result = await Selector.For("application/pdf")!
            .ExtractAsync(content, CancellationToken.None);

        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldNotBeNull().ShouldContain("Pestera Sura Mare");
    }

    [Fact]
    public async Task A_package_larger_than_a_reader_may_hold_is_still_read()
    {
        using var content = LargeWordDocument();
        content.Length.ShouldBeGreaterThan(InMemoryBoundBytes);

        var result = await Selector
            .For("application/vnd.openxmlformats-officedocument.wordprocessingml.document")!
            .ExtractAsync(content, CancellationToken.None);

        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldBe("Peștera Șura Mare\nPrima vizită");
    }

    /// <summary>
    /// The most a reader takes into memory when it cannot read its source a second time. A file
    /// on disk can be read where it lies, so nothing about a stored document is bounded by this
    /// — which is exactly what the two tests above hold it to.
    /// </summary>
    private const long InMemoryBoundBytes = 64L * 1024 * 1024;

    /// <summary>
    /// A one-page document padded past the bound with a comment. The padding sits between the
    /// objects and the cross-reference table, so every recorded offset is still right and a
    /// reader that follows them never looks at it — the file is simply large.
    /// </summary>
    private static MemoryStream LargePdf() =>
        PdfStream([TextPage("Pestera Sura Mare")], InMemoryBoundBytes + (4 * 1024 * 1024));

    /// <summary>
    /// A word processing document padded past the bound by a picture, which is what makes a
    /// real scan large. The bytes are random because a picture does not compress and zeroes
    /// would leave the stored package small enough to prove nothing.
    /// </summary>
    private static MemoryStream LargeWordDocument()
    {
        var buffer = new MemoryStream(capacity: (int)InMemoryBoundBytes + (8 * 1024 * 1024));
        using (var document = WordprocessingDocument.Create(
            buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Word.Document(new Word.Body(
                Paragraph("Peștera Șura Mare"),
                Paragraph("Prima vizită")));

            var image = main.AddImagePart(ImagePartType.Png);
            using var part = image.GetStream(FileMode.Create, FileAccess.Write);
            var chunk = new byte[1024 * 1024];
            for (var written = 0L; written <= InMemoryBoundBytes; written += chunk.Length)
            {
                Random.Shared.NextBytes(chunk);
                part.Write(chunk);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>A word processing document with two paragraphs.</summary>
    private static byte[] WordDocument()
    {
        var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Word.Document(new Word.Body(
                Paragraph("Peștera Șura Mare"),
                Paragraph("Prima vizită")));
        }

        return buffer.ToArray();
    }

    private static Word.Paragraph Paragraph(string text) =>
        new(new Word.Run(new Word.Text(text)));

    /// <summary>A workbook of two sheets, its text held in the shared string table.</summary>
    private static byte[] SpreadsheetDocumentBytes()
    {
        var buffer = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedPart.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("Peștera")),
                new SharedStringItem(new Text("Note")));

            var first = workbookPart.AddNewPart<WorksheetPart>();
            first.Worksheet = new Worksheet(new SheetData(new Row(
                SharedCell("A1", 0),
                new Cell { CellReference = "B1", CellValue = new CellValue("1450") })));

            var second = workbookPart.AddNewPart<WorksheetPart>();
            second.Worksheet = new Worksheet(new SheetData(new Row(SharedCell("A1", 1))));

            workbookPart.Workbook.AppendChild(new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(first), SheetId = 1U, Name = "Galerii",
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(second), SheetId = 2U, Name = "Descrieri",
                }));
        }

        return buffer.ToArray();
    }

    private static Cell SharedCell(string reference, int index) => new()
    {
        CellReference = reference,
        DataType = CellValues.SharedString,
        CellValue = new CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };

    /// <summary>A presentation of two slides, listed in the order they are shown.</summary>
    private static byte[] PresentationBytes()
    {
        var buffer = new MemoryStream();
        using (var document = PresentationDocument.Create(
            buffer, PresentationDocumentType.Presentation))
        {
            var part = document.AddPresentationPart();
            part.Presentation = new Presentation.Presentation();

            var list = new Presentation.SlideIdList();
            var id = 256U;
            foreach (var line in new[] { "Peștera Șura Mare", "Sala Mare" })
            {
                var slidePart = part.AddNewPart<SlidePart>();
                slidePart.Slide = new Presentation.Slide(
                    new Presentation.CommonSlideData(
                        new Presentation.ShapeTree(
                            new Presentation.Shape(
                                new Presentation.TextBody(
                                    new Drawing.BodyProperties(),
                                    new Drawing.Paragraph(
                                        new Drawing.Run(new Drawing.Text(line))))))));

                list.Append(new Presentation.SlideId
                {
                    Id = id, RelationshipId = part.GetIdOfPart(slidePart),
                });
                id++;
            }

            part.Presentation.SlideIdList = list;
        }

        return buffer.ToArray();
    }

    /// <summary>A compound-file workbook of the kind written before the packaged formats.</summary>
    private static byte[] LegacyWorkbook()
    {
        using var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Galerii");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("Peștera Șura Mare");
        sheet.CreateRow(1).CreateCell(0).SetCellValue(1450d);

        var buffer = new MemoryStream();
        workbook.Write(buffer, leaveOpen: true);
        return buffer.ToArray();
    }

    /// <summary>A compound file holding one named stream and nothing a workbook reader wants.</summary>
    private static byte[] CompoundFile(string streamName)
    {
        var compound = new POIFSFileSystem();
        using (var payload = new MemoryStream(Encoding.ASCII.GetBytes("not a workbook")))
        {
            compound.Root.CreateDocument(streamName, payload);
        }

        var buffer = new MemoryStream();
        compound.WriteFileSystem(buffer);
        return buffer.ToArray();
    }

    /// <summary>A page whose content stream writes one line of text.</summary>
    private static byte[] TextPage(string line) =>
        Encoding.ASCII.GetBytes($"BT /F1 24 Tf 20 200 Td ({line}) Tj ET");

    /// <summary>
    /// A page whose content stream draws a small greyscale picture and nothing else — the
    /// shape a scanned page has. The sample values are chosen to be printable so this file
    /// stays reviewable as text.
    /// </summary>
    private static byte[] PicturePage() => Encoding.ASCII.GetBytes(
        "q 200 0 0 200 20 20 cm BI /W 2 /H 2 /CS /G /BPC 8 ID ABCD EI Q");

    /// <summary>
    /// A portable document of the given pages, with the cross-reference table its offsets
    /// actually require. Assembled here because nothing in the tree writes this format.
    /// </summary>
    private static byte[] Pdf(IReadOnlyList<byte[]> pageContents)
    {
        using var file = PdfStream(pageContents, padding: 0);
        return file.ToArray();
    }

    /// <summary>
    /// The same document as a stream, optionally padded with a comment of the given length
    /// between the last object and the cross-reference table.
    /// </summary>
    private static MemoryStream PdfStream(IReadOnlyList<byte[]> pageContents, long padding)
    {
        var kids = string.Join(
            ' ', Enumerable.Range(0, pageContents.Count).Select(i => $"{4 + (2 * i)} 0 R"));

        var objects = new List<byte[]>
        {
            Ascii("<</Type/Catalog/Pages 2 0 R>>"),
            Ascii($"<</Type/Pages/Kids[{kids}]/Count {pageContents.Count}>>"),
            Ascii("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
        };

        for (var i = 0; i < pageContents.Count; i++)
        {
            objects.Add(Ascii(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]"
                + $"/Resources<</Font<</F1 3 0 R>>>>/Contents {5 + (2 * i)} 0 R>>"));
            objects.Add(
            [
                .. Ascii($"<</Length {pageContents[i].Length}>>\nstream\n"),
                .. pageContents[i],
                .. Ascii("\nendstream"),
            ]);
        }

        var file = new MemoryStream(capacity: (int)padding + (64 * 1024));
        var offsets = new List<long>(objects.Count);
        Write(file, Ascii("%PDF-1.4\n"));
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Length);
            Write(file, Ascii($"{i + 1} 0 obj\n"));
            Write(file, objects[i]);
            Write(file, Ascii("\nendobj\n"));
        }

        if (padding > 0)
        {
            // A comment runs to the end of its line, so this is one very long comment: no
            // recorded offset points into it and a reader following the table never sees it.
            Write(file, Ascii("%"));
            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'A');
            for (var written = 0L; written < padding; written += chunk.Length)
            {
                file.Write(chunk, 0, (int)Math.Min(chunk.Length, padding - written));
            }

            Write(file, Ascii("\n"));
        }

        var startXref = file.Length;
        Write(file, Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            Write(file, Ascii($"{offset:D10} 00000 n \n"));
        }

        Write(file, Ascii(
            $"trailer\n<</Size {objects.Count + 1}/Root 1 0 R>>\nstartxref\n{startXref}\n%%EOF"));

        file.Position = 0;
        return file;
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
