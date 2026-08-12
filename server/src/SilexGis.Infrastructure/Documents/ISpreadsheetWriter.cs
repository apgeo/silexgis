// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Documents;

/// <summary>One cell of a written sheet: some text, a number, or nothing at all.</summary>
/// <remarks>
/// A number stays a number rather than becoming its own printed form. A figure written as text is
/// a figure a spreadsheet cannot add up, and one written in the server's idea of a decimal point
/// is a figure that reads as a thousand times too large wherever the separators are the other way
/// round.
/// </remarks>
public readonly record struct SheetCell(string? Text, double? Number, bool Heading)
{
    /// <summary>A cell holding words.</summary>
    public static SheetCell Of(string? text) => new(text, null, false);

    /// <summary>A cell holding a figure; nothing when there is no figure to state.</summary>
    public static SheetCell Of(double? number) => new(null, number, false);

    /// <summary>A cell holding words that name what follows them.</summary>
    public static SheetCell Title(string text) => new(text, null, true);
}

/// <summary>
/// Writes a sheet of cells as a workbook file. The format engine sits behind this so a surface that
/// wants a spreadsheet describes its rows and never learns how one is built.
/// </summary>
public interface ISpreadsheetWriter
{
    /// <summary>The media type of what <see cref="Write"/> returns.</summary>
    string ContentType { get; }

    /// <summary>The file extension of what <see cref="Write"/> returns, without a leading dot.</summary>
    string Extension { get; }

    /// <summary>
    /// Builds a one-sheet workbook. Every row is as wide as it is, and a short row simply ends.
    /// </summary>
    byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<SheetCell>> rows);
}
