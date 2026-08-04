// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SilexGis.Domain.Documents;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads the modern Microsoft Office formats — <c>.docx</c>, <c>.xlsx</c> and <c>.pptx</c>.
/// <para>
/// Each of the three has a different idea of what a page is, and the reading follows the format
/// rather than imposing one shape on all three. A presentation's slides and a workbook's sheets
/// are numbered divisions the file itself declares, so they become numbered pages. A word
/// processor's pages are not: where they fall is settled when the document is laid out for a
/// particular paper size and set of fonts, and a stored file has not been laid out — so it is
/// one page, and page numbers here mean position in the file rather than sheet of paper.
/// </para>
/// </summary>
public sealed class OfficeOpenXmlTextExtractor : ITextExtractor
{
    private const string WordType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string ExcelType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PowerPointType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private const string WordMainPart = "word/document.xml";
    private const string ExcelMainPart = "xl/workbook.xml";
    private const string PowerPointMainPart = "ppt/presentation.xml";

    /// <summary>The parts that identify a package's kind, each unique to one of the three.</summary>
    private static readonly string[] MainParts =
        [WordMainPart, ExcelMainPart, PowerPointMainPart];

    /// <inheritdoc />
    public string Name => "office-open-xml";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        mimeType is WordType or ExcelType or PowerPointType;

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The package reader seeks; an upload stream need not. A stored file already can, and
        // is read where it lies — a package's directory of entries sits at the end of it, so a
        // reader given anything less than the whole file finds no entries at all.
        using var source = await ExtractionStreams.SeekableAsync(content, ct);

        var pages = ReadPackage(source.Stream, ct);
        return new ExtractedText(this.Name, this.Version, pages);
    }

    /// <summary>
    /// Which of the three a package is, told by the part that only that kind has. The three
    /// share one container and one reader is registered for all of them, but each has to be
    /// opened as the kind it is — so the question is settled here from the package's own
    /// contents rather than from the media type recorded at upload, which this is not handed.
    /// </summary>
    private static IReadOnlyList<ExtractedPage> ReadPackage(Stream buffer, CancellationToken ct)
    {
        string? mainPart;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            mainPart = MainParts.FirstOrDefault(p => archive.GetEntry(p) is not null);
        }

        buffer.Position = 0;
        switch (mainPart)
        {
            case WordMainPart:
                using (var word = WordprocessingDocument.Open(buffer, isEditable: false))
                {
                    return [new ExtractedPage(1, PageText.Normalize(WordText(word)))];
                }

            case ExcelMainPart:
                using (var workbook = SpreadsheetDocument.Open(buffer, isEditable: false))
                {
                    return SheetPages(workbook, ct);
                }

            case PowerPointMainPart:
                using (var presentation = PresentationDocument.Open(buffer, isEditable: false))
                {
                    return SlidePages(presentation, ct);
                }

            default:
                throw new InvalidDataException(
                    "The package holds none of the parts an Office document is built around.");
        }
    }

    /// <summary>The body of a word processing document, its paragraphs on separate lines.</summary>
    private static string WordText(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            Append(text, paragraph.InnerText);
        }

        return text.ToString();
    }

    /// <summary>
    /// One page per worksheet, in the order the workbook lists them. A cell holding a string
    /// stores an index into the workbook's shared table rather than the characters, so the
    /// table has to be resolved or every text cell reads as a number.
    /// </summary>
    private static IReadOnlyList<ExtractedPage> SheetPages(
        SpreadsheetDocument workbook, CancellationToken ct)
    {
        var workbookPart = workbook.WorkbookPart;
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
        var shared = workbookPart?.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];

        var pages = new List<ExtractedPage>(sheets.Count);
        var number = 0;
        foreach (var sheet in sheets)
        {
            ct.ThrowIfCancellationRequested();
            number++;

            if (sheet.Id?.Value is not { } partId
                || workbookPart!.GetPartById(partId) is not WorksheetPart part)
            {
                pages.Add(new ExtractedPage(number, null));
                continue;
            }

            var text = new StringBuilder();
            Append(text, sheet.Name?.Value);
            foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
            {
                var cells = row.Elements<Cell>()
                    .Select(c => CellText(c, shared))
                    .Where(v => !string.IsNullOrEmpty(v));
                Append(text, string.Join('\t', cells));
            }

            pages.Add(new ExtractedPage(number, PageText.Normalize(text.ToString())));
        }

        return pages;
    }

    /// <summary>One cell's characters, with a shared-table index resolved to the string it names.</summary>
    private static string? CellText(Cell cell, IReadOnlyList<string> shared)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            return int.TryParse(cell.CellValue?.InnerText, out var index)
                && index >= 0 && index < shared.Count
                ? shared[index]
                : null;
        }

        // An inline string keeps its characters in the cell; anything else — a number, a date,
        // a formula's cached result — reads as the text it was written as.
        return cell.DataType?.Value == CellValues.InlineString
            ? cell.InnerText
            : cell.CellValue?.InnerText;
    }

    /// <summary>
    /// One page per slide, in presentation order. The order comes from the slide list rather
    /// than from the parts, which a package may store in any order at all.
    /// </summary>
    private static IReadOnlyList<ExtractedPage> SlidePages(
        PresentationDocument presentation, CancellationToken ct)
    {
        var part = presentation.PresentationPart;
        var slideIds = part?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .ToList() ?? [];

        var pages = new List<ExtractedPage>(slideIds.Count);
        var number = 0;
        foreach (var slideId in slideIds)
        {
            ct.ThrowIfCancellationRequested();
            number++;

            if (slideId.RelationshipId?.Value is not { } relationship
                || part!.GetPartById(relationship) is not SlidePart slide)
            {
                pages.Add(new ExtractedPage(number, null));
                continue;
            }

            var text = new StringBuilder();
            foreach (var paragraph in slide.Slide?.Descendants<Drawing.Paragraph>() ?? [])
            {
                Append(text, paragraph.InnerText);
            }

            foreach (var note in slide.NotesSlidePart?.NotesSlide
                ?.Descendants<Drawing.Paragraph>() ?? [])
            {
                Append(text, note.InnerText);
            }

            pages.Add(new ExtractedPage(number, PageText.Normalize(text.ToString())));
        }

        return pages;
    }

    /// <summary>Adds a block of text on its own line, skipping the ones that hold nothing.</summary>
    private static void Append(StringBuilder text, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(value).Append('\n');
        }
    }
}
