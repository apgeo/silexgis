// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which stored formats are worth reading text out of. The list decides what is queued, so a
/// format wrongly on it costs a reading that always finds nothing, and one wrongly off it means
/// a document that can never be searched by its contents and no sign anywhere of why.
/// </summary>
public class TextExtractionFormatsTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/rtf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/x-ole-storage")]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("text/csv")]
    [InlineData("application/json")]
    [InlineData("application/gpx+xml")]
    public void Formats_that_carry_a_text_layer_are_read(string mimeType) =>
        TextExtractionFormats.CarriesText(mimeType).ShouldBeTrue();

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/tiff")]
    [InlineData("audio/mpeg")]
    [InlineData("video/mp4")]
    [InlineData("model/gltf-binary")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("application/epub+zip")]
    [InlineData("")]
    [InlineData(null)]
    public void Formats_that_carry_no_text_layer_are_left_alone(string? mimeType) =>
        TextExtractionFormats.CarriesText(mimeType).ShouldBeFalse();

    /// <summary>
    /// A scanned page is a picture inside a paged wrapper. The wrapper is still read — it is
    /// the file's own format that decides this, not what happens to be on its pages, and a PDF
    /// that turns out to hold no text is a result rather than a mistake.
    /// </summary>
    [Fact]
    public void A_paged_format_is_read_whether_or_not_a_given_file_holds_text() =>
        TextExtractionFormats.CarriesText("application/pdf").ShouldBeTrue();
}
