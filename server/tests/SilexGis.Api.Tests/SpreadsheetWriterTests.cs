// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Shouldly;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Tests;

/// <summary>
/// The workbook writer, held to producing a file without measuring any text.
///
/// This is the shape of failure the surrounding tests cannot see. Sizing a column to fit its
/// contents means measuring the contents, which means loading a font — and the container this
/// application is deployed in has no fonts installed at all, so the measuring call throws there
/// while succeeding on every machine a test is run on. The export route would then answer every
/// caller with a server error in every real installation, having passed the whole gate.
///
/// So the property pinned here is not "the file looks right" but "the widths are decided without
/// asking the operating system anything": they are constants, and a written file states them back
/// unchanged. Re-introducing a measured width changes them and fails this.
/// </summary>
public sealed class SpreadsheetWriterTests
{
    private const int WidthUnitsPerChar = 256;

    private readonly XlsxSpreadsheetWriter writer = new();

    [Fact]
    public void Column_widths_are_fixed_rather_than_measured_from_the_text()
    {
        var bytes = writer.Write("Figures",
        [
            [SheetCell.Title("Figure"), SheetCell.Title("Value")],
            [
                SheetCell.Of(
                    "A label long enough that measuring it would give a width unlike any other "
                    + "row's, which is what makes this assertion able to tell the two apart"),
                SheetCell.Of(12d),
            ],
            [SheetCell.Of("Short"), SheetCell.Of(3d)],
        ]);

        using var workbook = WorkbookOf(bytes);
        var sheet = workbook.GetSheetAt(0);

        // Every label shares one width and every figure shares another, whatever they say. A
        // measured width would differ between the long row and the short one.
        var label = sheet.GetColumnWidth(0);
        var value = sheet.GetColumnWidth(1);
        (label % WidthUnitsPerChar).ShouldBe(0);
        (value % WidthUnitsPerChar).ShouldBe(0);
        label.ShouldBeGreaterThan(value);

        using var narrower = WorkbookOf(writer.Write("Figures",
        [
            [SheetCell.Of("Short"), SheetCell.Of(3d)],
        ]));
        var again = narrower.GetSheetAt(0);
        again.GetColumnWidth(0).ShouldBe(label);
        again.GetColumnWidth(1).ShouldBe(value);
    }

    [Fact]
    public void A_figure_stays_a_figure_and_words_stay_words()
    {
        using var workbook = WorkbookOf(writer.Write("Figures",
        [
            [SheetCell.Of("Trips"), SheetCell.Of(4d)],
            [],
            [SheetCell.Of("Nothing recorded"), SheetCell.Of((double?)null)],
        ]));
        var sheet = workbook.GetSheetAt(0);

        var first = sheet.GetRow(0);
        first.GetCell(0).CellType.ShouldBe(CellType.String);
        // A number written as text is a number the spreadsheet cannot add up, and one written in
        // the server's idea of a decimal point reads wrongly wherever the separators differ.
        first.GetCell(1).CellType.ShouldBe(CellType.Numeric);
        first.GetCell(1).NumericCellValue.ShouldBe(4d);

        // An empty row stays empty and a figure that has nothing to state writes no cell at all,
        // rather than a zero that would read as a measurement.
        sheet.GetRow(1).ShouldBeNull();
        sheet.GetRow(2).GetCell(1).ShouldBeNull();
    }

    private static XSSFWorkbook WorkbookOf(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new XSSFWorkbook(stream);
    }
}
