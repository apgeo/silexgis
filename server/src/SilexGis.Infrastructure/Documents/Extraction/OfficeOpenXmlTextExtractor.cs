// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Xml;
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
            RefuseOversizedMarkup(archive);
            mainPart = MainParts.FirstOrDefault(p => archive.GetEntry(p) is not null);
        }

        buffer.Position = 0;
        var settings = new OpenSettings { MaxCharactersInPart = MaxMarkupPartBytes };
        switch (mainPart)
        {
            case WordMainPart:
                using (var word = WordprocessingDocument.Open(buffer, isEditable: false, settings))
                {
                    return [new ExtractedPage(1, PageText.Normalize(WordText(word)))];
                }

            case ExcelMainPart:
                using (var workbook = SpreadsheetDocument.Open(buffer, isEditable: false, settings))
                {
                    return SheetPages(workbook, ct);
                }

            case PowerPointMainPart:
                using (var presentation = PresentationDocument.Open(buffer, isEditable: false, settings))
                {
                    return SlidePages(presentation, ct);
                }

            default:
                throw new InvalidDataException(
                    "The package holds none of the parts an Office document is built around.");
        }
    }

    /// <summary>
    /// Most bytes one markup part may expand to. The document library builds a whole part into
    /// an object tree several times the size of its text before anything here can stop it, so
    /// the bound has to be applied before the part is opened rather than to the text it yields.
    /// It is the same ceiling every other reader here holds a single entry of a container to,
    /// and far above what the two million characters a page keeps could need.
    /// </summary>
    internal const int MaxMarkupPartBytes = ExtractionStreams.MaxBytes;

    /// <summary>
    /// Most bytes all the markup parts of one package may expand to together. The library keeps
    /// every part it has opened until the package is closed, so a workbook of many sheets each
    /// just under the single-part ceiling would otherwise be held all at once.
    /// </summary>
    internal const long MaxMarkupPackageBytes = 4L * ExtractionStreams.MaxBytes;

    /// <summary>
    /// Refuses a package whose markup would expand past what may be held, judged from the sizes
    /// the package's directory declares and before a single part is parsed. A few megabytes of
    /// upload can declare gigabytes of repetitive markup — deflate reaches about a thousand to
    /// one — so a part is also refused when it expands far more than ordinary text does, by the
    /// rule every archive this application unpacks is held to.
    /// <para>
    /// Only markup counts, and what is markup is decided the way the document library decides
    /// what to parse — by the content type the package declares for a part, not by the part's
    /// name. A part may be called anything: a worksheet stored as <c>xl/worksheets/s1.dat</c>
    /// and declared a worksheet is parsed as one. So the one thing read past is a part the
    /// package declares to be something other than markup, such as a picture or an embedded
    /// file; the library never parses those as markup, so a large scan inside a report is read
    /// past as before. Anything not declared at all counts, and so does every relationships part
    /// and the content-type list itself, which are found and parsed by their names.
    /// </para>
    /// <para>
    /// The declared size is binding rather than a hint: the runtime's archive reader stops a
    /// deflated entry at the length the directory gives it, and the part limit passed to the
    /// library holds the parse to the same ceiling should that ever change.
    /// </para>
    /// </summary>
    /// <exception cref="ContentTooLargeException">The markup would expand past a ceiling.</exception>
    private static void RefuseOversizedMarkup(ZipArchive archive)
    {
        var notMarkup = PartsDeclaredNotMarkup(archive);

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (notMarkup.Contains(entry.FullName))
            {
                continue;
            }

            total += entry.Length;
            RefuseOversizedPart(entry, total);
        }
    }

    /// <summary>Refuses one markup part, given what the markup comes to with it included.</summary>
    private static void RefuseOversizedPart(ZipArchiveEntry entry, long total)
    {
        if (entry.Length > MaxMarkupPartBytes || total > MaxMarkupPackageBytes)
        {
            throw new ContentTooLargeException(
                "The document's text would expand past what may be read.");
        }

        if (ArchiveExpansionRules.AbandonReason(
                ArchiveLimits.Default, entry.Length, entry.CompressedLength, entry.Length)
            is not null)
        {
            throw new ContentTooLargeException(
                "The document's text is compressed far more than any ordinary document, "
                + "so it is not expanded.");
        }
    }

    private const string ContentTypesEntry = "[Content_Types].xml";

    /// <summary>
    /// The entries the package's content-type list declares to be something other than markup,
    /// by their names in the archive. The list is itself markup, so it is measured before it is
    /// read; a package without one is not a package the library opens, and nothing is exempt.
    /// </summary>
    private static HashSet<string> PartsDeclaredNotMarkup(ZipArchive archive)
    {
        var notMarkup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = archive.GetEntry(ContentTypesEntry);
        if (list is null)
        {
            return notMarkup;
        }

        RefuseOversizedPart(list, list.Length);

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = list.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element
                    || reader.GetAttribute("ContentType") is not { } type)
                {
                    continue;
                }

                if (reader.LocalName == "Default" && reader.GetAttribute("Extension") is { } extension)
                {
                    defaults[extension] = type;
                }
                else if (reader.LocalName == "Override" && reader.GetAttribute("PartName") is { } part)
                {
                    overrides[part.TrimStart('/')] = type;
                }
            }
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("The package's list of content types is not readable.", e);
        }

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (IsReadByName(name))
            {
                continue;
            }

            var declared = overrides.TryGetValue(name, out var byPart)
                ? byPart
                : defaults.GetValueOrDefault(Path.GetExtension(name).TrimStart('.'));
            if (declared is not null && !IsMarkupType(declared))
            {
                notMarkup.Add(name);
            }
        }

        return notMarkup;
    }

    /// <summary>
    /// The parts the package reader finds and parses by name, whatever content type is declared
    /// for them: the content-type list and the relationships.
    /// </summary>
    private static bool IsReadByName(string name) =>
        name.Equals(ContentTypesEntry, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A markup content type: <c>application/xml</c>, <c>text/xml</c> or any of the
    /// <c>…+xml</c> types every Office part is declared with. Parameters are ignored.
    /// </summary>
    private static bool IsMarkupType(string contentType)
    {
        var media = contentType.Split(';')[0].Trim();
        return media.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || media.EndsWith("/xml", StringComparison.OrdinalIgnoreCase);
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
