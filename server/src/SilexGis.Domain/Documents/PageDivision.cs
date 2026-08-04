// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// What the page number on a piece of extracted text actually counts.
/// <para>
/// Append-only: the numbers travel to a client, so a member keeps the value it was given.
/// </para>
/// </summary>
public enum PageDivision : short
{
    /// <summary>
    /// The file has no numbered divisions and its whole text is stored as page one. Saying
    /// "page 1" about it would be a claim the file never made.
    /// </summary>
    Whole = 0,

    /// <summary>A page the file itself paginates - only a PDF does.</summary>
    Page = 1,

    /// <summary>A worksheet in a spreadsheet, numbered in workbook order.</summary>
    Sheet = 2,

    /// <summary>A slide in a presentation, numbered in deck order.</summary>
    Slide = 3,
}

/// <summary>
/// Whether a format's page numbers mean anything a reader would recognise, and what they are
/// called when they do.
/// <para>
/// This exists because the honest answer is uncomfortable: text is stored per page, but only a
/// PDF has pages. A spreadsheet has sheets and a presentation has slides - both are numbered
/// divisions the file declares, so they are real, just not pages. A word-processor document has
/// pages only once something has decided a paper size and a font, which is a rendering decision
/// no reader of the file makes, so its whole text is stored as a single row. Reporting "page 1
/// of 1" for a forty-page report would be the interface inventing a fact.
/// </para>
/// <para>
/// The answer is a property of the format, which is why it lives here rather than in whichever
/// reader produced the rows: a search result knows the file's recorded format long before it
/// knows which reader ever opened it.
/// </para>
/// </summary>
public static class DocumentPagination
{
    /// <summary>What the stored page numbers of a file in this format count.</summary>
    public static PageDivision DivisionOf(string? mimeType) => mimeType switch
    {
        "application/pdf" => PageDivision.Page,

        // Only the modern Open XML pair declares its sheets and slides in a form the reader
        // walks one by one. The legacy binary formats are read whole, so their text arrives as
        // one row however many sheets the workbook had, and claiming otherwise here would
        // describe rows that do not exist.
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => PageDivision.Sheet,
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => PageDivision.Slide,

        _ => PageDivision.Whole,
    };

    /// <summary>
    /// Whether a hit in this format can honestly be shown with its page number. False means the
    /// number exists only because the text had to be stored somewhere.
    /// </summary>
    public static bool NumbersRealDivisions(string? mimeType) => DivisionOf(mimeType) != PageDivision.Whole;
}
