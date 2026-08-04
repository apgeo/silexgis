// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.HSSF.Extractor;
using NPOI.POIFS.FileSystem;
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads the Microsoft Office formats that predate the packaged ones — the compound files a
/// cave archive's older material is written in.
/// <para>
/// A compound file is a small filesystem inside one file, and which application wrote it is
/// decided by the streams at its root rather than by its name. That matters because an upload
/// whose name carried no extension is recorded only as a compound file: the reading is what
/// settles which application it came from, so the stored format only has to have got that far.
/// </para>
/// <para>
/// Only the legacy spreadsheet is readable here. The legacy word processor and presentation
/// files use the same container but a different, undocumented-in-practice body format that the
/// reader behind this does not implement, so they are refused rather than silently returned
/// empty — an empty result would be indistinguishable from a document that genuinely has
/// nothing in it, and only one of the two is worth reading again once something can.
/// </para>
/// </summary>
public sealed class LegacyOfficeTextExtractor : ITextExtractor
{
    /// <summary>The workbook streams: the first is current, the second is Excel 5 and older.</summary>
    private static readonly string[] WorkbookStreams = ["Workbook", "Book"];

    /// <summary>
    /// A compound file whose sub-type the upload's name did not settle. It is opened and asked
    /// what it is, which is the only place that question can be answered.
    /// </summary>
    private const string UnrefinedCompoundFile = "application/x-ole-storage";

    /// <inheritdoc />
    public string Name => "legacy-office";

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        mimeType is "application/vnd.ms-excel" or UnrefinedCompoundFile;

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A compound file is a filesystem: its directory names sectors anywhere in the file, so
        // it is read whole, where it lies, rather than as a prefix of itself.
        using var source = await ExtractionStreams.SeekableAsync(content, ct);

        var compound = new POIFSFileSystem(source.Stream);
        var entries = compound.Root.EntryNames;
        if (!WorkbookStreams.Any(entries.Contains))
        {
            throw new NotSupportedException(
                "The compound file holds no workbook, and no other legacy Office body format "
                + "is readable here.");
        }

        ct.ThrowIfCancellationRequested();

        // The whole workbook is one page: its sheets are divisions of a grid rather than of a
        // document, and the reader behind this hands back their text as one run in sheet order,
        // labelled with the sheet names so a match can still be placed.
        var extractor = new ExcelExtractor(compound)
        {
            IncludeSheetNames = true,
            IncludeHeadersFooters = true,
            IncludeCellComments = true,
            FormulasNotResults = false,
        };

        return new ExtractedText(this.Name, this.Version,
            [new ExtractedPage(1, PageText.Normalize(extractor.Text))]);
    }
}
