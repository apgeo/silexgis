// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.POIFS.FileSystem;
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
    public async Task A_word_document_whose_piece_table_declares_millions_of_characters_is_refused_unconverted()
    {
        // The same real report, with its piece table's last position moved so the table declares
        // one character more than a reader takes. The file is no larger: a piece table only says
        // which runs of the file make up the text, and runs may repeat the same bytes any number
        // of times. The converter would index every declared character before writing anything.
        byte[] original;
        using (var fixture = Fixture("legacy-report.doc"))
        using (var copy = new MemoryStream())
        {
            fixture.CopyTo(copy);
            original = copy.ToArray();
        }

        var hostile = WithPieceTableDeclaring(original, LegacyOfficeTextExtractor.MaxWordCharacters + 1L);
        Math.Abs(hostile.Length - original.Length).ShouldBeLessThan(4096);

        await Should.ThrowAsync<ContentTooLargeException>(() => Selector.For("application/msword")!
            .ExtractAsync(new MemoryStream(hostile), CancellationToken.None));
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

    /// <summary>
    /// The document rewritten so that its piece table's runs add up to
    /// <paramref name="characters"/>, by moving the table's last character position. Every other
    /// stream at the root of the compound file is carried over unchanged.
    /// </summary>
    private static byte[] WithPieceTableDeclaring(byte[] document, long characters)
    {
        var compound = new POIFSFileSystem(new MemoryStream(document));
        var streams = compound.Root.EntryNames
            .Where(name => compound.Root.GetEntry(name) is DocumentEntry)
            .ToDictionary(name => name, name =>
            {
                using var input = compound.Root.CreateDocumentInputStream(name);
                using var copy = new MemoryStream();
                input.CopyTo(copy);
                return copy.ToArray();
            });

        // The header's three length-prefixed arrays, then the thirty-fourth offset/length pair.
        var header = streams["WordDocument"];
        var position = 32;
        position += 2 + (BitConverter.ToUInt16(header, position) * 2);
        position += 2 + (BitConverter.ToUInt16(header, position) * 4);
        var clxAt = position + 2 + (33 * 8);
        var clxOffset = (int)BitConverter.ToUInt32(header, clxAt);
        var table = streams[(BitConverter.ToUInt16(header, 10) & 0x0200) != 0 ? "1Table" : "0Table"];

        var i = clxOffset;
        while (table[i] == 0x01)
        {
            i += 3 + BitConverter.ToInt16(table, i + 1);
        }

        table[i].ShouldBe((byte)0x02, "the piece table follows the formatting records");
        var length = BitConverter.ToInt32(table, i + 1);
        var pieces = (length - 4) / 12;
        var first = BitConverter.ToInt32(table, i + 5);
        BitConverter.TryWriteBytes(table.AsSpan(i + 5 + (pieces * 4)), (int)(first + characters));

        var rebuilt = new POIFSFileSystem();
        foreach (var (name, bytes) in streams)
        {
            rebuilt.Root.CreateDocument(name, new MemoryStream(bytes));
        }

        using var output = new MemoryStream();
        rebuilt.WriteFileSystem(output);
        return output.ToArray();
    }
}
