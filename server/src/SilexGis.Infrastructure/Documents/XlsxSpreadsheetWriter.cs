// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// The spreadsheet writer, over the workbook library this project already carries for reading the
/// same formats back. Everything is built in memory: a workbook of figures is small, and a
/// temporary file would be one more thing to clean up on two operating systems.
/// </summary>
public sealed class XlsxSpreadsheetWriter : ISpreadsheetWriter
{
    /// <summary>How wide a column is written, in characters.</summary>
    /// <remarks>
    /// Fixed rather than sized to the contents. Sizing a column to its text means measuring that
    /// text, which means loading a font, and the runtime container this application ships in has
    /// no fonts installed at all — the call that measures throws there while succeeding on every
    /// developer machine, so the failure would appear only after deployment and only on the one
    /// route that writes a sheet. A fixed width also makes the file byte-identical wherever it is
    /// produced, which is what lets two copies of an export be compared at all.
    ///
    /// The first column carries labels and the rest carry figures, so they are not the same width;
    /// the label width is generous enough for the longest sentence any sheet here writes without
    /// pushing the figures off the side of a screen.
    /// </remarks>
    private const int LabelColumnWidthChars = 44;

    /// <summary>How wide every column after the first is written, in characters.</summary>
    private const int ValueColumnWidthChars = 18;

    /// <summary>Column widths are counted in 1/256ths of a character.</summary>
    private const int WidthUnitsPerChar = 256;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string Extension => "xlsx";

    public byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<SheetCell>> rows)
    {
        using var workbook = new XSSFWorkbook();

        var heading = workbook.CreateCellStyle();
        var bold = workbook.CreateFont();
        bold.IsBold = true;
        heading.SetFont(bold);

        var sheet = workbook.CreateSheet(WorkbookUtil.CreateSafeSheetName(sheetName));
        var widest = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var source = rows[r];
            if (source.Count == 0)
            {
                continue;
            }

            var row = sheet.CreateRow(r);
            widest = Math.Max(widest, source.Count);
            for (var c = 0; c < source.Count; c++)
            {
                var value = source[c];
                if (value.Text is null && value.Number is null)
                {
                    continue;
                }

                var cell = row.CreateCell(c);
                if (value.Text is { } text)
                {
                    cell.SetCellValue(text);
                }
                else
                {
                    cell.SetCellValue(value.Number!.Value);
                }

                if (value.Heading)
                {
                    cell.CellStyle = heading;
                }
            }
        }

        for (var c = 0; c < widest; c++)
        {
            var chars = c == 0 ? LabelColumnWidthChars : ValueColumnWidthChars;
            sheet.SetColumnWidth(c, chars * WidthUnitsPerChar);
        }

        using var buffer = new MemoryStream();
        workbook.Write(buffer, leaveOpen: true);
        return buffer.ToArray();
    }
}
