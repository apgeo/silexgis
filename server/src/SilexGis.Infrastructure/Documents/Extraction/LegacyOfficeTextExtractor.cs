// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.HSSF.Extractor;
using NPOI.POIFS.FileSystem;
using SilexGis.Domain.Documents;
using DocSharpPresentation = DocSharp.Binary.OpenXmlLib.PresentationML.PresentationDocument;
using DocSharpWord = DocSharp.Binary.OpenXmlLib.WordprocessingML.WordprocessingDocument;
using PptConverter = DocSharp.Binary.PresentationMLMapping.Converter;
using PptDocument = DocSharp.Binary.PptFileFormat.PowerpointDocument;
using StorageReader = DocSharp.Binary.StructuredStorage.Reader.StructuredStorageReader;
using WordConverter = DocSharp.Binary.WordprocessingMLMapping.Converter;
using WordDocument = DocSharp.Binary.DocFileFormat.WordDocument;

namespace SilexGis.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads the Microsoft Office formats that predate the packaged ones — the compound files a
/// cave archive's older material is written in.
/// <para>
/// A compound file is a small filesystem inside one file, and which application wrote it is
/// decided by the streams at its root rather than by its name. That matters because an upload
/// whose name carried no extension is recorded only as a compound file: the reading is what
/// settles which application it came from, so the stored format only has to have got that far.
/// One reader answers for the whole family so that question has a single home.
/// </para>
/// <para>
/// The spreadsheet is read directly. The word-processor and presentation bodies are not read
/// here at all: they are converted, in memory, into the modern packaged form of the same
/// application and handed to the reader that already understands it. That keeps every decision
/// about what counts as text — which paragraphs, which slides, which speaker notes, how a page
/// is divided — in one place, and leaves this class responsible only for recognising the
/// container and refusing what it must.
/// </para>
/// <para>
/// The conversion is best-effort by nature: these formats were never published in a form that
/// makes a complete reader practical, and text recovered imperfectly from an old report is worth
/// far more than a document that cannot be found at all. What is <em>not</em> acceptable is
/// plausible nonsense, which is why an encrypted document is refused outright rather than parsed
/// into whatever its scrambled bytes happen to decode as.
/// </para>
/// </summary>
public sealed class LegacyOfficeTextExtractor : ITextExtractor
{
    /// <summary>The workbook streams: the first is current, the second is Excel 5 and older.</summary>
    private static readonly string[] WorkbookStreams = ["Workbook", "Book"];

    /// <summary>The body stream of a word-processor document, from Word 6 onwards.</summary>
    private const string WordStream = "WordDocument";

    /// <summary>The body stream of a presentation, from PowerPoint 97 onwards.</summary>
    private const string PresentationStream = "PowerPoint Document";

    /// <summary>
    /// Where a presentation records which revision is current — and, in one documented value,
    /// whether the document is encrypted at all.
    /// </summary>
    private const string CurrentUserStream = "Current User";

    private const string WordType = "application/msword";
    private const string ExcelType = "application/vnd.ms-excel";
    private const string PresentationType = "application/vnd.ms-powerpoint";

    /// <summary>
    /// A compound file whose sub-type the upload's name did not settle. It is opened and asked
    /// what it is, which is the only place that question can be answered.
    /// </summary>
    private const string UnrefinedCompoundFile = "application/x-ole-storage";

    /// <summary>
    /// The reader the converted bytes are handed to. Stateless, and deliberately constructed here
    /// rather than injected: what this class converts to is part of how it reads, not a choice an
    /// installation makes, and the pages it produces are re-stamped with this reader's own name
    /// below so a page still records the reader that was actually asked for it.
    /// </summary>
    private static readonly OfficeOpenXmlTextExtractor Packaged = new();

    /// <inheritdoc />
    public string Name => "legacy-office";

    /// <summary>
    /// Left at one although this reader now answers for two more formats. The stamp says which
    /// text a page was produced from, and no page has ever carried this name for a word-processor
    /// or presentation file — those were refused, so there is nothing stale to find. Spreadsheet
    /// text is produced by exactly the code that produced it before, so re-reading every workbook
    /// in the installation would cost the same answer twice.
    /// </summary>
    public int Version => 1;

    /// <inheritdoc />
    public bool Handles(string mimeType) =>
        mimeType is ExcelType or WordType or PresentationType or UnrefinedCompoundFile;

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A compound file is a filesystem: its directory names sectors anywhere in the file, so
        // it is read whole, where it lies, rather than as a prefix of itself.
        using var source = await ExtractionStreams.SeekableAsync(content, ct);

        // The compound reader closes whatever stream it is handed, and the word-processor and
        // presentation bodies need a second reader over the same bytes. Each gets its own view
        // so neither can end the other's reading.
        var compound = new POIFSFileSystem(new SharedReadStream(source.Stream));
        var entries = compound.Root.EntryNames;

        if (WorkbookStreams.Any(entries.Contains))
        {
            ct.ThrowIfCancellationRequested();
            return this.ReadWorkbook(compound);
        }

        if (entries.Contains(WordStream))
        {
            RefuseUnreadableWordDocument(compound);
            RefuseOversizedWordText(compound);
            ct.ThrowIfCancellationRequested();
            return await this.ReadConvertedAsync(source.Stream, ConvertWord, ct);
        }

        if (entries.Contains(PresentationStream))
        {
            RefuseEncryptedPresentation(compound, entries);
            ct.ThrowIfCancellationRequested();
            return await this.ReadConvertedAsync(source.Stream, ConvertPresentation, ct);
        }

        throw new NotSupportedException(
            "The compound file holds none of the legacy Office body formats readable here.");
    }

    /// <summary>
    /// The whole workbook is one page: its sheets are divisions of a grid rather than of a
    /// document, and the reader behind this hands back their text as one run in sheet order,
    /// labelled with the sheet names so a match can still be placed.
    /// </summary>
    private ExtractedText ReadWorkbook(POIFSFileSystem compound)
    {
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

    /// <summary>
    /// Converts the compound file into the packaged form of the same application and reads that.
    /// <para>
    /// The pages come back stamped by the packaged reader and are re-stamped here, because the
    /// stamp answers "which reader would have to run again to change this text", and that reader
    /// is this one: the packaged reader is never handed these bytes on its own.
    /// </para>
    /// </summary>
    private async Task<ExtractedText> ReadConvertedAsync(
        Stream source, Action<StorageReader, Stream, CancellationToken> convert, CancellationToken ct)
    {
        // The compound reader used for the conversion is not the one used for the dispatch above,
        // and each starts from the beginning of the file rather than from wherever the other left
        // off — a compound file's directory is not at a fixed offset, so a reader handed a
        // part-consumed stream finds nothing at all.
        var view = new SharedReadStream(source) { Position = 0 };

        // The converted package is held to the same ceiling the packaged reader holds a package's
        // markup to, while it is being written: the converter builds it from whatever the old
        // file's own tables declare, and those are written by whoever made the file.
        using var converted = new CappedMemoryStream(OfficeOpenXmlTextExtractor.MaxMarkupPackageBytes);
        using (var reader = new StorageReader(view))
        {
            convert(reader, converted, ct);
        }

        converted.Position = 0;
        var packaged = await Packaged.ExtractAsync(converted, ct);
        return new ExtractedText(this.Name, this.Version, packaged.Pages);
    }

    /// <summary>Word 97-2003 into the packaged word-processing format, in memory.</summary>
    private static void ConvertWord(StorageReader reader, Stream output, CancellationToken ct)
    {
        try
        {
            var document = new WordDocument(reader);
            ct.ThrowIfCancellationRequested();

            using var docx = DocSharpWord.Create(output, WordConverter.DetectOutputType(document));
            WordConverter.Convert(document, docx);
        }
        catch (Exception e) when (IsUnreadableVariant(e))
        {
            throw Unreadable(e);
        }
    }

    /// <summary>PowerPoint 97-2003 into the packaged presentation format, in memory.</summary>
    private static void ConvertPresentation(StorageReader reader, Stream output, CancellationToken ct)
    {
        try
        {
            var presentation = new PptDocument(reader);
            ct.ThrowIfCancellationRequested();

            using var pptx = DocSharpPresentation.Create(output, PptConverter.DetectOutputType(presentation));
            PptConverter.Convert(presentation, pptx);
        }
        catch (Exception e) when (IsUnreadableVariant(e))
        {
            throw Unreadable(e);
        }
    }

    /// <summary>
    /// Whether the converter is saying "this is a variant I do not implement" rather than "these
    /// bytes are broken". The two are recorded against the file differently and only one of them
    /// is worth re-reading once the file itself changes, so the distinction is made here while
    /// the converter's own vocabulary is still visible.
    /// <para>
    /// The commonest such case — a word processor older than any format the converter reaches —
    /// never arrives here, because the header is checked before the converter is handed anything:
    /// the converter reports that case with the same exception it uses for damaged bytes, told
    /// apart only by the wording of a message. What is left are the variants the converter itself
    /// declares it has not implemented, and those it does say plainly.
    /// </para>
    /// </summary>
    private static bool IsUnreadableVariant(Exception e) =>
        e is DocSharp.Binary.DocFileFormat.UnspportedFileVersionException or NotImplementedException;

    /// <summary>
    /// Restates an unimplemented variant in the vocabulary the caller records states from. The
    /// original is kept as the inner exception so a diagnostic log still names the format.
    /// </summary>
    private static NotSupportedException Unreadable(Exception cause) =>
        new("The document is a variant of its format that the converter behind this reader does "
            + "not implement. Nothing is wrong with the file.", cause);

    /// <summary>
    /// Reads the two things the head of a word-processor document says about itself that decide
    /// whether it is worth converting at all, and refuses it if either says no.
    /// <para>
    /// Both are read here rather than left to the converter, and for the same reason in each
    /// case: the converter's own answer is not one this can act on. It reads the encryption flag
    /// and carries on regardless, so an encrypted body does not fail — it yields whatever its
    /// scrambled bytes decode as, which is text-shaped rubbish that would be indexed and searched
    /// as though it meant something. And it reports a version it cannot handle with the same
    /// exception it reports damaged bytes with, distinguishable only by the wording of a message,
    /// which is not a contract worth depending on: those two outcomes are recorded against the
    /// file differently and only one of them is the file's fault.
    /// </para>
    /// <para>
    /// The header's layout has been fixed since Word 6: a two-byte identifier, the format version
    /// at offset two, and a sixteen-bit flag field at offset ten whose 0x0100 bit is set when the
    /// body is encrypted or obfuscated. Twelve bytes reach all of it.
    /// </para>
    /// </summary>
    private static void RefuseUnreadableWordDocument(POIFSFileSystem compound)
    {
        const int headerBytes = 12;
        const int versionOffset = 2;
        const int flagsOffset = 10;
        const ushort encryptedFlag = 0x0100;

        // The oldest format version the converter behind this reader implements. Word 95 and
        // everything before it declares a lower one and is not readable here at all.
        const ushort oldestConvertible = 0x006A;

        Span<byte> header = stackalloc byte[headerBytes];
        using (var stream = compound.Root.CreateDocumentInputStream(WordStream))
        {
            stream.ReadExactly(header);
        }

        if ((BitConverter.ToUInt16(header[flagsOffset..]) & encryptedFlag) != 0)
        {
            throw new ProtectedContentException(
                "The word-processor document is password-protected.");
        }

        if (BitConverter.ToUInt16(header[versionOffset..]) < oldestConvertible)
        {
            throw new NotSupportedException(
                "The document was written by a word processor older than any format the "
                + "converter behind this reader implements. Nothing is wrong with the file.");
        }
    }

    /// <summary>
    /// The most characters a word-processor document's text tables may declare, all of its
    /// parts together — the body, notes, headers, comments and text boxes. Some four million
    /// characters is well over a thousand dense pages.
    /// </summary>
    /// <remarks>
    /// The converter builds a per-character index of the whole text before it writes anything:
    /// two dictionary entries and a list slot per character, some fifty bytes each. Its count
    /// comes from the piece table, which says which runs of the file make up the text and may
    /// point any number of runs at the same few bytes, so a file of a megabyte can declare
    /// billions of characters. The declaration is therefore checked before the converter is
    /// handed the file.
    /// </remarks>
    internal const int MaxWordCharacters = 4 * 1024 * 1024;

    /// <summary>
    /// Refuses a word-processor document whose tables declare more text than
    /// <see cref="MaxWordCharacters"/>, from two figures the file states about itself: the
    /// character counts in its header, and the total length of the runs its piece table lists.
    /// <para>
    /// The header's layout is fixed from Word 97, which is all the converter reads: a 32-byte
    /// base, then three arrays each preceded by its own length — sixteen-bit values, thirty-two
    /// bit values (the character counts start twelve bytes in), and offset/length pairs, of which
    /// the thirty-fourth locates the piece table in the table stream. A header or table too short
    /// to hold what is looked for is left to the converter, which reports such a file as damaged.
    /// </para>
    /// </summary>
    /// <exception cref="ContentTooLargeException">The document declares too much text.</exception>
    private static void RefuseOversizedWordText(POIFSFileSystem compound)
    {
        const int baseLength = 32;
        const int flagsOffset = 10;
        const ushort secondTableFlag = 0x0200;
        const int characterCountsOffset = 12;
        const int characterCounts = 8;
        const int clxPair = 33;

        var header = ReadPrefix(compound, WordStream, 1024);
        if (header.Length < baseLength + 2)
        {
            return;
        }

        var position = baseLength;
        var shorts = BitConverter.ToUInt16(header, position);
        position += 2 + (shorts * 2);
        if (position + 2 > header.Length)
        {
            return;
        }

        var longs = BitConverter.ToUInt16(header, position);
        var longsStart = position + 2;
        position = longsStart + (longs * 4);
        if (longs >= (characterCountsOffset / 4) + characterCounts
            && longsStart + characterCountsOffset + (characterCounts * 4) <= header.Length)
        {
            long declared = 0;
            for (var i = 0; i < characterCounts; i++)
            {
                declared += Math.Max(0, BitConverter.ToInt32(header, longsStart + characterCountsOffset + (i * 4)));
            }

            RefuseIfPast(declared);
        }

        if (position + 2 > header.Length)
        {
            return;
        }

        var pairs = BitConverter.ToUInt16(header, position);
        var pairsStart = position + 2;
        var clxAt = pairsStart + (clxPair * 8);
        if (pairs <= clxPair || clxAt + 8 > header.Length)
        {
            return;
        }

        var clxOffset = BitConverter.ToUInt32(header, clxAt);
        var clxLength = BitConverter.ToUInt32(header, clxAt + 4);
        if (clxLength == 0)
        {
            return;
        }

        if (clxLength > ExtractionStreams.MaxBytes)
        {
            throw new ContentTooLargeException("The document's piece table is larger than a document's can be.");
        }

        var tableStream = (BitConverter.ToUInt16(header, flagsOffset) & secondTableFlag) != 0 ? "1Table" : "0Table";
        if (!compound.Root.HasEntry(tableStream))
        {
            return;
        }

        var clx = ReadRange(compound, tableStream, clxOffset, (int)clxLength);
        RefuseIfPast(PieceCharacters(clx));

        static void RefuseIfPast(long characters)
        {
            if (characters > MaxWordCharacters)
            {
                throw new ContentTooLargeException(
                    $"The document declares more than {MaxWordCharacters:N0} characters of text, more than a reader may take.");
            }
        }
    }

    /// <summary>
    /// The total length of every run the piece table lists, counted as the converter counts them:
    /// each run's own span, however many runs share the same bytes. Formatting records ahead of
    /// the table are stepped over; anything unrecognised ends the count with what it has.
    /// </summary>
    internal static long PieceCharacters(ReadOnlySpan<byte> clx)
    {
        const byte formatting = 0x01;
        const byte pieceTable = 0x02;

        var i = 0;
        while (i < clx.Length)
        {
            if (clx[i] == formatting && i + 3 <= clx.Length)
            {
                i += 3 + Math.Max(0, (int)BitConverter.ToInt16(clx[(i + 1)..]));
                continue;
            }

            if (clx[i] != pieceTable || i + 5 > clx.Length)
            {
                return 0;
            }

            var length = BitConverter.ToInt32(clx[(i + 1)..]);
            var plc = clx[(i + 5)..];
            if (length < 4 || length > plc.Length)
            {
                return 0;
            }

            var pieces = (length - 4) / 12;
            long total = 0;
            for (var k = 0; k < pieces; k++)
            {
                var start = BitConverter.ToInt32(plc[(k * 4)..]);
                var end = BitConverter.ToInt32(plc[((k + 1) * 4)..]);
                total += Math.Max(0L, (long)end - start);
            }

            return total;
        }

        return 0;
    }

    /// <summary>Up to <paramref name="count"/> bytes from the start of a stream at the compound file's root.</summary>
    private static byte[] ReadPrefix(POIFSFileSystem compound, string name, int count)
    {
        using var stream = compound.Root.CreateDocumentInputStream(name);
        var bytes = new byte[count];
        var read = stream.ReadAtLeast(bytes, count, throwOnEndOfStream: false);
        return bytes[..read];
    }

    /// <summary>
    /// <paramref name="count"/> bytes from <paramref name="offset"/> in a stream at the compound
    /// file's root, or fewer when the stream ends first. Read forward rather than sought, so only
    /// the stretch asked for is ever held.
    /// </summary>
    private static byte[] ReadRange(POIFSFileSystem compound, string name, long offset, int count)
    {
        using var stream = compound.Root.CreateDocumentInputStream(name);
        var skip = new byte[81920];
        while (offset > 0)
        {
            var read = stream.Read(skip, 0, (int)Math.Min(skip.Length, offset));
            if (read <= 0)
            {
                return [];
            }

            offset -= read;
        }

        var bytes = new byte[count];
        var got = stream.ReadAtLeast(bytes, count, throwOnEndOfStream: false);
        return bytes[..got];
    }

    /// <summary>
    /// Refuses a password-protected presentation, for the same reason and on the same evidence:
    /// the record naming the current revision carries one of two documented values, and one of
    /// them means the rest of the file is encrypted. A presentation with no such record at all is
    /// let through — it is malformed rather than protected, and the parser will say so.
    /// </summary>
    private static void RefuseEncryptedPresentation(
        POIFSFileSystem compound, IEnumerable<string> entries)
    {
        const int headerBytes = 16;
        const int tokenOffset = 12;
        const uint encryptedToken = 0xF3D1C4DF;

        if (!entries.Contains(CurrentUserStream))
        {
            return;
        }

        Span<byte> header = stackalloc byte[headerBytes];
        using (var stream = compound.Root.CreateDocumentInputStream(CurrentUserStream))
        {
            stream.ReadExactly(header);
        }

        if (BitConverter.ToUInt32(header[tokenOffset..]) == encryptedToken)
        {
            throw new ProtectedContentException("The presentation is password-protected.");
        }
    }

    /// <summary>An in-memory stream that refuses to grow past a ceiling.</summary>
    private sealed class CappedMemoryStream(long capacityLimit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Admit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Admit(1);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value > capacityLimit)
            {
                throw TooLarge();
            }

            base.SetLength(value);
        }

        private void Admit(int count)
        {
            if (Math.Max(Position, 0) + count > capacityLimit)
            {
                throw TooLarge();
            }
        }

        private ContentTooLargeException TooLarge() =>
            new($"The converted document grew past the {capacityLimit / (1024 * 1024)} MB a reader may take.");
    }
}
