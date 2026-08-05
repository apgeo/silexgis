// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a stored page number is allowed to claim. The rule matters because the storage shape
/// tempts an interface into a lie: text is stored per page for every format, but only some
/// formats have anything a reader would call a page.
/// </summary>
public class PageDivisionTests
{
    [Fact]
    public void A_pdf_has_pages()
    {
        DocumentPagination.DivisionOf("application/pdf").ShouldBe(PageDivision.Page);
        DocumentPagination.NumbersRealDivisions("application/pdf").ShouldBeTrue();
    }

    [Fact]
    public void A_workbook_has_sheets_and_a_deck_has_slides()
    {
        DocumentPagination
            .DivisionOf("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .ShouldBe(PageDivision.Sheet);
        DocumentPagination
            .DivisionOf("application/vnd.openxmlformats-officedocument.presentationml.presentation")
            .ShouldBe(PageDivision.Slide);
    }

    /// <summary>
    /// A legacy presentation is read by converting it into the modern one, so its slides survive
    /// the reading as separate rows and are as real as any other deck's. A legacy workbook is not
    /// converted — it is read whole — so the same argument does not reach it, and the two answers
    /// are asserted together because the difference is the point.
    /// </summary>
    [Fact]
    public void A_legacy_deck_keeps_its_slides_although_a_legacy_workbook_loses_its_sheets()
    {
        DocumentPagination.DivisionOf("application/vnd.ms-powerpoint").ShouldBe(PageDivision.Slide);
        DocumentPagination.NumbersRealDivisions("application/vnd.ms-powerpoint").ShouldBeTrue();

        DocumentPagination.DivisionOf("application/vnd.ms-excel").ShouldBe(PageDivision.Whole);
        DocumentPagination.NumbersRealDivisions("application/vnd.ms-excel").ShouldBeFalse();
    }

    [Theory]
    // A word processor paginates only once something has chosen a paper size, which is not a
    // fact about the file.
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("application/rtf")]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    // A legacy workbook is read whole however many sheets it holds, so its one row is the whole
    // file and numbering it would describe rows that do not exist. A compound file whose name
    // settled nothing is worse than that: under the one media type a workbook, a word-processor
    // document and a presentation all arrive, and only the last of the three has divisions at
    // all — so the media type alone cannot say what a number would be counting.
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/msword")]
    [InlineData("application/x-ole-storage")]
    // Including the spreadsheet and presentation shapes of OpenDocument, which are read whole
    // even though the same content in the modern Office formats is not.
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("application/vnd.oasis.opendocument.presentation")]
    public void Everything_else_is_one_undivided_body_of_text(string mimeType)
    {
        DocumentPagination.DivisionOf(mimeType).ShouldBe(PageDivision.Whole);
        DocumentPagination.NumbersRealDivisions(mimeType).ShouldBeFalse();
    }

    [Fact]
    public void An_unrecorded_format_claims_nothing()
    {
        DocumentPagination.DivisionOf(null).ShouldBe(PageDivision.Whole);
        DocumentPagination.DivisionOf(string.Empty).ShouldBe(PageDivision.Whole);
    }
}
